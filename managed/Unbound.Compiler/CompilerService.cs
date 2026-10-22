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
    const string FormatVersion = "unbound-compiler-1";

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
                return Done(result, "cached", watch);
            }

            var compilation = UpdateCompilation(state, spec, request, sources, references.Select(r => r.Reference).ToList());
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
            foreach (var diagnostic in emit.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    if (result.Errors.Count < 100)
                        result.Errors.Add(ToInfo(diagnostic));
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    result.Warnings++;
                }
            }
            if (!emit.Success)
            {
                state.Key = null;
                state.Result = null;
                result.Dll = result.Pdb = null;
                return Done(result, "failed", watch);
            }
            Directory.CreateDirectory(dir);
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
            source.File.Tree = CSharpSyntaxTree.ParseText(text, state.ParseOptions, source.Path);
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
        return Hash(Encoding.UTF8.GetBytes(builder.ToString())).Substring(0, 32);
    }

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

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
