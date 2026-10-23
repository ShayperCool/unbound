using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Unbound.Compiler;

/// <summary>
/// A warm, in-process C# compiler for a project's script assemblies.
///
/// Every assembly keeps its parsed syntax trees and its last <see cref="CSharpCompilation"/>; a
/// request re-reads only files whose stamp changed, re-parses only files whose content changed,
/// and re-emits only assemblies whose inputs changed. A dependent's inputs include the hash of its
/// references' public surface (their reference assemblies), so a method body edit recompiles one
/// assembly. Outputs are content-addressed on disk, so a new process reuses them without compiling.
/// </summary>
public sealed class CompilerService
{
    /// <summary>Bump when the outputs of the same inputs would differ.</summary>
    const string FormatVersion = "unbound-compiler-4";

    static readonly string CompilerIdentity =
        typeof(CSharpCompilation).Assembly.GetName().Version + "/" + FormatVersion;

    readonly ConcurrentDictionary<string, AssemblyState> _states = new();
    readonly ConcurrentDictionary<string, FileReference> _fileReferences = new();
    readonly object _lock = new();

    sealed class AssemblyState
    {
        public CSharpParseOptions? ParseOptions;
        public Dictionary<string, SourceFile> Sources = new();
        public CSharpCompilation? Compilation;
        public string? Key;
        public AssemblyResult? Result;
        /// <summary>The reference assembly image, referenced by dependents.</summary>
        public MetadataReference? Interface;
        /// <summary>The source generators' driver, kept warm across compiles.</summary>
        public GeneratorDriver? Generators;
        public string? GeneratorsKey;
        /// <summary><see cref="Compilation"/> with the generated sources: what was emitted.</summary>
        public CSharpCompilation? Generated;
    }

    sealed class SourceFile
    {
        public required (long, long) Stamp;
        public required string Hash;
        /// <summary>Parsed only when the assembly is compiled: a cache hit needs the hash alone.</summary>
        public SyntaxTree? Tree;
    }

    sealed class FileReference
    {
        public required (long, long) Stamp;
        public required MetadataReference Reference;
    }

    public CompileResponse Compile(CompileRequest request)
    {
        lock (_lock)
        {
            var watch = Stopwatch.StartNew();
            var tasks = new Dictionary<string, Task<AssemblyResult>>();
            foreach (var spec in request.Assemblies)
            {
                var dependencies = spec.References
                    .Where(tasks.ContainsKey)
                    .Select(r => (Name: r, Task: tasks[r]))
                    .ToArray();
                tasks[spec.Name] = Task.Run(async () =>
                {
                    await Task.WhenAll(dependencies.Select(d => d.Task));
                    return CompileOne(spec, request, dependencies.Select(d => d.Task.Result).ToArray());
                });
            }
            Task.WaitAll(tasks.Values.ToArray());
            var response = new CompileResponse();
            foreach (var spec in request.Assemblies)
            {
                var result = tasks[spec.Name].Result;
                response.Assemblies.Add(result);
                switch (result.Status)
                {
                    case "compiled": response.Compiled++; break;
                    case "cached": response.Cached++; break;
                    case "failed": response.Failed++; break;
                }
            }
            response.Milliseconds = watch.ElapsedMilliseconds;
            return response;
        }
    }

    AssemblyResult CompileOne(AssemblySpec spec, CompileRequest request, AssemblyResult[] dependencies)
    {
        var watch = Stopwatch.StartNew();
        var state = _states.GetOrAdd(spec.Name, _ => new AssemblyState());
        var result = new AssemblyResult { Name = spec.Name };
        try
        {
            var failed = dependencies.FirstOrDefault(d => d.Status is "failed" or "skipped");
            if (failed != null)
            {
                result.Status = "skipped";
                result.Errors.Add(new DiagnosticInfo { Id = "UNBOUND0001", Message = $"reference {failed.Name} did not compile" });
                return result;
            }

            var parseOptions = new CSharpParseOptions(
                ParseLanguageVersion(request.LanguageVersion),
                DocumentationMode.None,
                SourceCodeKind.Regular,
                spec.Defines.Concat(OptionDefines(request.CompilerOptions)));
            if (state.ParseOptions == null || !state.ParseOptions.Equals(parseOptions))
            {
                state.ParseOptions = parseOptions;
                state.Sources.Clear();
                state.Compilation = null;
                state.Generated = null;
                state.Generators = null;
            }
            var sources = UpdateSources(state, spec.Sources, parseOptions);

            var references = new List<(string Identity, MetadataReference Reference)>();
            foreach (var path in request.BclReferences)
                references.Add(FileRef(path));
            if (!spec.NoEngineReferences)
                foreach (var path in request.EngineReferences)
                    references.Add(FileRef(path));
            foreach (var path in spec.Plugins)
                references.Add(FileRef(path));
            foreach (var dependency in dependencies)
            {
                var interfaceRef = _states[dependency.Name].Interface!;
                references.Add(($"{dependency.Name}:{dependency.InterfaceHash}", interfaceRef));
            }

            var key = Key(spec, request, sources, references);
            if (state.Key == key && state.Result != null && File.Exists(state.Result.Dll))
            {
                return Done(state.Result, "unchanged", watch);
            }

            var dir = Path.Combine(request.CacheDir, spec.Name);
            var dll = Path.Combine(dir, key + ".dll");
            var pdb = Path.Combine(dir, key + ".pdb");
            var refDll = Path.Combine(dir, key + ".ref.dll");
            result.Dll = dll;
            result.Pdb = pdb;
            if (File.Exists(dll) && File.Exists(pdb) && File.Exists(refDll))
            {
                var image = File.ReadAllBytes(refDll);
                state.Interface = MetadataReference.CreateFromImage(image);
                result.InterfaceHash = Hash(image);
                state.Key = key;
                state.Result = result;
                // The warm compilation is stale now; it is rebuilt on the next real compile.
                state.Compilation = null;
                state.Generated = null;
                return Done(result, "cached", watch);
            }

            var compilation = UpdateCompilation(state, spec, request, sources, references.Select(r => r.Reference).ToList());
            var generatorDiagnostics = new List<Diagnostic>();
            compilation = WithGenerators(state, spec, request, compilation, generatorDiagnostics);
            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            using var refStream = new MemoryStream();
            var emit = compilation.Emit(
                peStream,
                pdbStream,
                metadataPEStream: refStream,
                options: new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb,
                    pdbFilePath: spec.Name + ".pdb",
                    includePrivateMembers: false));
            foreach (var diagnostic in generatorDiagnostics.Concat(emit.Diagnostics))
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error || GeneratorFailed(diagnostic))
                {
                    if (result.Errors.Count < 100)
                        result.Errors.Add(ToInfo(diagnostic));
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    result.Warnings++;
                }
            }
            if (!emit.Success || generatorDiagnostics.Any(GeneratorFailed))
            {
                state.Key = null;
                state.Result = null;
                result.Dll = result.Pdb = null;
                return Done(result, "failed", watch);
            }
            Directory.CreateDirectory(dir);
            StoreSources(request.CacheDir, sources);
            File.WriteAllText(Path.Combine(dir, key + ".sources"), SourceManifest(sources));
            WriteAtomically(pdb, pdbStream);
            WriteAtomically(refDll, refStream);
            WriteAtomically(dll, peStream);
            var refImage = refStream.ToArray();
            state.Interface = MetadataReference.CreateFromImage(refImage);
            result.InterfaceHash = Hash(refImage);
            state.Key = key;
            state.Result = result;
            return Done(result, "compiled", watch);
        }
        catch (Exception e)
        {
            state.Key = null;
            state.Result = null;
            result.Errors.Add(new DiagnosticInfo { Id = "UNBOUND0002", Message = e.ToString() });
            return Done(result, "failed", watch);
        }
    }

    static AssemblyResult Done(AssemblyResult result, string status, Stopwatch watch)
    {
        result.Status = status;
        result.Milliseconds = watch.ElapsedMilliseconds;
        return result;
    }

    /// <summary>Brings the assembly's syntax trees up to date with the files on disk.</summary>
    List<(string Path, SourceFile File)> UpdateSources(AssemblyState state, List<string> paths, CSharpParseOptions options)
    {
        var updated = new (string Path, SourceFile File)[paths.Count];
        Parallel.For(0, paths.Count, i =>
        {
            var path = paths[i];
            var info = new FileInfo(path);
            var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
            state.Sources.TryGetValue(path, out var previous);
            if (previous != null && previous.Stamp == stamp)
            {
                updated[i] = (path, previous);
                return;
            }
            var bytes = File.ReadAllBytes(path);
            var hash = Hash(bytes);
            var tree = previous != null && previous.Hash == hash ? previous.Tree : null;
            updated[i] = (path, new SourceFile { Stamp = stamp, Hash = hash, Tree = tree });
        });
        lock (state)
        {
            state.Sources = updated.ToDictionary(u => u.Path, u => u.File);
        }
        return updated.ToList();
    }

    /// <summary>Reuses the previous compilation when only some trees changed; Roslyn then keeps
    /// the declarations of the unchanged ones.</summary>
    static CSharpCompilation UpdateCompilation(
        AssemblyState state,
        AssemblySpec spec,
        CompileRequest request,
        List<(string Path, SourceFile File)> sources,
        List<MetadataReference> references)
    {
        Parallel.ForEach(sources, source =>
        {
            if (source.File.Tree != null) return;
            var bytes = File.ReadAllBytes(source.Path);
            var text = SourceText.From(bytes, bytes.Length, Encoding.UTF8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: false, canBeEmbedded: true);
            source.File.Tree = FinalizerGuard.Parse(text, state.ParseOptions!, source.Path, !spec.NoEngineReferences);
        });
        var trees = sources.Select(s => s.File.Tree!).ToList();
        var options = CompilationOptions(spec, request);
        var previous = state.Compilation;
        CSharpCompilation compilation;
        if (previous != null && previous.Options.Equals(options))
        {
            compilation = previous;
            var wanted = trees.ToHashSet();
            var current = previous.SyntaxTrees.ToHashSet();
            var removed = current.Where(t => !wanted.Contains(t)).ToList();
            var added = trees.Where(t => !current.Contains(t)).ToList();
            // A replaced file is a removed and an added tree with the same path.
            var addedByPath = added.ToDictionary(t => t.FilePath);
            foreach (var old in removed.ToList())
            {
                if (addedByPath.TryGetValue(old.FilePath, out var replacement))
                {
                    compilation = compilation.ReplaceSyntaxTree(old, replacement);
                    removed.Remove(old);
                    added.Remove(replacement);
                }
            }
            if (removed.Count > 0)
                compilation = compilation.RemoveSyntaxTrees(removed);
            if (added.Count > 0)
                compilation = compilation.AddSyntaxTrees(added);
            if (!previous.References.SequenceEqual(references))
                compilation = compilation.WithReferences(references);
        }
        else
        {
            compilation = CSharpCompilation.Create(spec.Name, trees, references, options);
        }
        state.Compilation = compilation;
        return compilation;
    }

    static CSharpCompilationOptions CompilationOptions(AssemblySpec spec, CompileRequest request)
    {
        var suppressed = new Dictionary<string, ReportDiagnostic>();
        foreach (var option in request.CompilerOptions)
        {
            var value = OptionValue(option, "nowarn");
            if (value == null) continue;
            foreach (var id in value.Split(',', ';'))
            {
                var trimmed = id.Trim();
                if (trimmed.Length == 0) continue;
                suppressed[trimmed.StartsWith("CS") ? trimmed : "CS" + trimmed.PadLeft(4, '0')] = ReportDiagnostic.Suppress;
            }
        }
        // Binding redirect notes that the Editor does not show either.
        suppressed["CS1701"] = ReportDiagnostic.Suppress;
        suppressed["CS1702"] = ReportDiagnostic.Suppress;
        return new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: spec.AllowUnsafe || request.CompilerOptions.Any(o => OptionValue(o, "unsafe") != null),
                deterministic: true,
                concurrentBuild: true,
                nullableContextOptions: NullableContextOptions.Disable,
                specificDiagnosticOptions: suppressed,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
    }

    static IEnumerable<string> OptionDefines(List<string> options)
    {
        foreach (var option in options)
        {
            var value = OptionValue(option, "define") ?? OptionValue(option, "d");
            if (value == null) continue;
            foreach (var symbol in value.Split(',', ';'))
                if (symbol.Trim().Length > 0)
                    yield return symbol.Trim();
        }
    }

    /// <summary><c>/name:value</c>, <c>-name:value</c>, or <c>/name</c> (empty value).</summary>
    static string? OptionValue(string option, string name)
    {
        if (option.Length < 2 || (option[0] != '/' && option[0] != '-')) return null;
        var body = option.Substring(1);
        if (body.Equals(name, StringComparison.OrdinalIgnoreCase) || body.Equals(name + "+", StringComparison.OrdinalIgnoreCase))
            return "";
        if (body.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            return body.Substring(name.Length + 1);
        return null;
    }

    static LanguageVersion ParseLanguageVersion(string text) =>
        LanguageVersionFacts.TryParse(text, out var version) ? version : LanguageVersion.CSharp9;

    (string Identity, MetadataReference Reference) FileRef(string path)
    {
        var info = new FileInfo(path);
        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
        var cached = _fileReferences.AddOrUpdate(
            path,
            p => new FileReference { Stamp = stamp, Reference = MetadataReference.CreateFromFile(p) },
            (p, existing) => existing.Stamp == stamp
                ? existing
                : new FileReference { Stamp = stamp, Reference = MetadataReference.CreateFromFile(p) });
        return ($"{path}:{stamp.Item1}:{stamp.Item2}", cached.Reference);
    }

    static string Key(
        AssemblySpec spec,
        CompileRequest request,
        List<(string Path, SourceFile File)> sources,
        List<(string Identity, MetadataReference Reference)> references)
    {
        var builder = new StringBuilder();
        builder.Append(CompilerIdentity).Append('\n');
        builder.Append(spec.Name).Append('\n');
        builder.Append(request.LanguageVersion).Append('\n');
        builder.Append(spec.AllowUnsafe).Append('\n');
        foreach (var option in request.CompilerOptions) builder.Append(option).Append('\n');
        foreach (var define in spec.Defines) builder.Append(define).Append(';');
        builder.Append('\n');
        foreach (var (path, file) in sources) builder.Append(path).Append('=').Append(file.Hash).Append('\n');
        foreach (var (identity, _) in references) builder.Append(identity).Append('\n');
        foreach (var analyzer in AnalyzerPaths(spec, request))
        {
            var info = new FileInfo(analyzer);
            builder.Append("analyzer ").Append(analyzer).Append(':').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append(':')
                .Append(info.Exists ? info.Length : 0).Append('\n');
        }
        builder.Append(request.ProjectPath).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(builder.ToString())).Substring(0, 32);
    }

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Every compiled source, by content hash: Edit and Continue rebuilds the compilation
    /// of an assembly the game loaded from the sources it was compiled from.</summary>
    static void StoreSources(string cacheDir, List<(string Path, SourceFile File)> sources)
    {
        var store = Path.Combine(cacheDir, "sources");
        Directory.CreateDirectory(store);
        Parallel.ForEach(sources, source =>
        {
            var target = Path.Combine(store, source.File.Hash);
            if (File.Exists(target)) return;
            try
            {
                var bytes = File.ReadAllBytes(source.Path);
                if (Hash(bytes) != source.File.Hash) return; // changed since it was read
                var temp = target + "." + Environment.ProcessId + "." + Environment.CurrentManagedThreadId + ".tmp";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, target, overwrite: true);
            }
            catch (IOException)
            {
            }
        });
    }

    static string SourceManifest(List<(string Path, SourceFile File)> sources)
    {
        var builder = new StringBuilder();
        foreach (var (path, file) in sources) builder.Append(file.Hash).Append(' ').Append(path).Append('\n');
        return builder.ToString();
    }

    /// <summary>The compilation of an assembly as it was when its output `dll` was emitted:
    /// rebuilt from the stored sources of that output and the current references.</summary>
    public CSharpCompilation? RebuildCompilation(AssemblySpec spec, CompileRequest request, string dll, List<MetadataReference> references)
    {
        var manifest = Path.ChangeExtension(dll, null) + ".sources";
        if (!File.Exists(manifest)) return null;
        var store = Path.Combine(request.CacheDir, "sources");
        var parseOptions = new CSharpParseOptions(
            ParseLanguageVersion(request.LanguageVersion),
            DocumentationMode.None,
            SourceCodeKind.Regular,
            spec.Defines.Concat(OptionDefines(request.CompilerOptions)));
        var lines = File.ReadAllLines(manifest).Where(l => l.Length > 65).ToArray();
        var trees = new SyntaxTree[lines.Length];
        bool missing = false;
        Parallel.For(0, lines.Length, i =>
        {
            var hash = lines[i].Substring(0, 64);
            var path = lines[i].Substring(65);
            var stored = Path.Combine(store, hash);
            if (!File.Exists(stored)) { missing = true; return; }
            var bytes = File.ReadAllBytes(stored);
            var text = SourceText.From(bytes, bytes.Length, Encoding.UTF8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: false, canBeEmbedded: true);
            trees[i] = FinalizerGuard.Parse(text, parseOptions, path, !spec.NoEngineReferences);
        });
        if (missing) return null;
        var compilation = CSharpCompilation.Create(spec.Name, trees, references, CompilationOptions(spec, request));
        return WithGenerators(null, spec, request, compilation, new List<Diagnostic>());
    }

    /// <summary>A generator that could not load or threw: what it should have added is missing, so
    /// the assembly would not be the Editor's.</summary>
    static bool GeneratorFailed(Diagnostic diagnostic) => diagnostic.Id is "CS8032" or "CS8784" or "CS8785";

    static IEnumerable<string> AnalyzerPaths(AssemblySpec spec, CompileRequest request) =>
        request.Analyzers.Concat(spec.Analyzers).Distinct();

    /// <summary>The compilation with the source generators' output, as the Editor compiles it.
    /// <paramref name="state"/> keeps the driver warm; without one a fresh driver runs.</summary>
    static CSharpCompilation WithGenerators(AssemblyState? state, AssemblySpec spec, CompileRequest request,
        CSharpCompilation compilation, List<Diagnostic> diagnostics)
    {
        var paths = AnalyzerPaths(spec, request).Where(File.Exists).ToList();
        if (paths.Count == 0) return compilation;
        var options = (CSharpParseOptions)compilation.SyntaxTrees.FirstOrDefault()?.Options
            ?? new CSharpParseOptions(ParseLanguageVersion(request.LanguageVersion));
        var key = string.Join("\n", paths) + "\n" + request.ProjectPath;
        GeneratorDriver driver;
        if (state != null && state.Generators != null && state.GeneratorsKey == key)
            driver = state.Generators;
        else
            driver = SourceGenerators.Driver(paths, spec.Name, request.ProjectPath, options);
        var result = SourceGenerators.Run(ref driver, compilation, diagnostics);
        if (state != null)
        {
            state.Generators = driver;
            state.GeneratorsKey = key;
            state.Generated = result;
        }
        return result;
    }

    /// <summary>The current compilation of an assembly and the references it compiles against,
    /// after a <see cref="Compile"/>; built from its trees when the output came from the cache.</summary>
    public (CSharpCompilation? Compilation, List<MetadataReference> References) CurrentCompilation(AssemblySpec spec, CompileRequest request)
    {
        if (!_states.TryGetValue(spec.Name, out var state) || state.Result == null) return (null, new List<MetadataReference>());
        var references = new List<MetadataReference>();
        foreach (var path in request.BclReferences) references.Add(FileRef(path).Reference);
        if (!spec.NoEngineReferences)
            foreach (var path in request.EngineReferences) references.Add(FileRef(path).Reference);
        foreach (var path in spec.Plugins) references.Add(FileRef(path).Reference);
        foreach (var dependency in spec.References)
            if (_states.TryGetValue(dependency, out var d) && d.Interface != null) references.Add(d.Interface);
        if (state.Compilation != null && state.Generated != null) return (state.Generated, references);
        var ordered = spec.Sources.Where(state.Sources.ContainsKey).Select(p => (p, state.Sources[p])).ToList();
        var compilation = state.Compilation ?? UpdateCompilation(state, spec, request, ordered, references);
        return (WithGenerators(state, spec, request, compilation, new List<Diagnostic>()), references);
    }

    static void WriteAtomically(string path, MemoryStream stream)
    {
        var temp = path + "." + Environment.ProcessId + ".tmp";
        using (var file = File.Create(temp))
        {
            stream.Position = 0;
            stream.CopyTo(file);
        }
        File.Move(temp, path, overwrite: true);
    }

    static DiagnosticInfo ToInfo(Diagnostic diagnostic)
    {
        var info = new DiagnosticInfo { Id = diagnostic.Id, Message = diagnostic.GetMessage() };
        if (diagnostic.Location.IsInSource)
        {
            var span = diagnostic.Location.GetLineSpan();
            info.File = span.Path;
            info.Line = span.StartLinePosition.Line + 1;
            info.Column = span.StartLinePosition.Character + 1;
        }
        return info;
    }
}
