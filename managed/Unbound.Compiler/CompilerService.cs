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
public sealed partial class CompilerService
{
    /// <summary>Bump when the outputs of the same inputs would differ.</summary>
    const string FormatVersion = "unbound-compiler-6";

    static readonly string CompilerIdentity =
        typeof(CSharpCompilation).Assembly.GetName().Version + "/" + FormatVersion;

    readonly ConcurrentDictionary<string, AssemblyState> _states = new();
    readonly ILPostProcessing _ilpp = new();
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
            var results = request.Assemblies.ToDictionary(a => a.Name, a => tasks[a.Name].Result);
            _ilpp.Run(request, results, PostProcessingReferences);
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

            var commandLine = CommandLine(spec, request);
            if (commandLine.Errors.Count > 0)
            {
                result.Errors.AddRange(commandLine.Errors.Select(d => ToInfo(d, request.DisabledModules)));
                return Done(result, "failed", watch);
            }
            var parseOptions = commandLine.Parse;
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
            foreach (var path in (spec.NetStandard ? request.NetStandardReferences : request.BclReferences))
                references.Add(FileRef(path));
            foreach (var path in EngineReferences(spec, request))
                references.Add(FileRef(path));
            foreach (var path in spec.Plugins)
                references.Add(FileRef(path));
            foreach (var aliased in spec.AliasedReferences)
            {
                var (identity, reference) = FileRef(aliased.Path);
                references.Add(($"{identity}:alias={aliased.Alias}", reference.WithAliases(new[] { aliased.Alias })));
            }
            foreach (var dependency in dependencies)
            {
                var interfaceRef = _states[dependency.Name].Interface!;
                references.Add(($"{dependency.Name}:{dependency.InterfaceHash}", interfaceRef));
            }

            var key = Key(spec, request, sources, references, commandLine.RuleSet);
            if (state.Key == key && state.Result != null && File.Exists(state.Result.Dll))
            {
                return Done(state.Result, "unchanged", watch);
            }

            var dir = Path.Combine(request.CacheDir, spec.Name);
            var dll = Path.Combine(dir, key + ".dll");
            var pdb = Path.Combine(dir, key + ".pdb");
            var refDll = Path.Combine(dir, key + ".ref.dll");
            // The compile's warnings: a cached output reports them as its compile did.
            var warnings = Path.Combine(dir, key + ".warnings.json");
            result.Dll = dll;
            result.Pdb = pdb;
            if (File.Exists(dll) && File.Exists(pdb) && File.Exists(refDll) && ReadWarnings(warnings) is { } stored)
            {
                result.WarningList = stored;
                result.Warnings = stored.Count;
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
            var failedStage = FailedEarlyStage(compilation);
            var emit = failedStage != null ? null : compilation.Emit(
                peStream,
                pdbStream,
                metadataPEStream: refStream,
                options: new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb,
                    // The name the PDB is stored under next to the DLL, where the runtime looks for
                    // the symbols of stack traces.
                    pdbFilePath: key + ".pdb",
                    includePrivateMembers: false));
            foreach (var option in commandLine.Ignored)
            {
                result.Warnings++;
                result.WarningList.Add(new DiagnosticInfo { Id = "UNBOUND0003", Message = $"Unbound does not apply the response file option {option}" });
            }
            foreach (var diagnostic in generatorDiagnostics.Concat(failedStage ?? emit!.Diagnostics))
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error || GeneratorFailed(diagnostic))
                {
                    if (result.Errors.Count < 100)
                        result.Errors.Add(ToInfo(diagnostic, request.DisabledModules));
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    result.Warnings++;
                    result.WarningList.Add(ToInfo(diagnostic, request.DisabledModules));
                }
            }
            if (emit == null || !emit.Success || generatorDiagnostics.Any(GeneratorFailed))
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
            using (var warningStream = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result.WarningList)))
                WriteAtomically(warnings, warningStream);
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

    /// <summary>The engine assemblies <paramref name="spec"/> compiles against: none
    /// (<c>noEngineReferences</c>), every module for an Editor-only assembly, else the modules the
    /// build target and the project's packages have.</summary>
    static IEnumerable<string> EngineReferences(AssemblySpec spec, CompileRequest request) =>
        spec.NoEngineReferences ? Enumerable.Empty<string>()
        : spec.EditorOnly ? request.EngineReferences
        : request.RuntimeEngineReferences;

    /// <summary>What an assembly's post-processing sees as its references, as the Editor's runner
    /// gets them: the class library, the engine, plugins and the compiled script assemblies.</summary>
    List<string> PostProcessingReferences(AssemblySpec spec, CompileRequest request)
    {
        var list = new List<string>(spec.NetStandard ? request.NetStandardReferences : request.BclReferences);
        list.AddRange(EngineReferences(spec, request));
        list.AddRange(spec.Plugins);
        foreach (var reference in spec.References)
            if (_states.TryGetValue(reference, out var state) && state.Result?.Dll != null)
                list.Add(state.Result.UnprocessedDll ?? state.Result.Dll);
        return list;
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
        var options = CommandLine(spec, request).Compilation;
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

    /// <summary>An assembly's options, read from its compiler command line as the Editor builds it:
    /// the release's language version, the assembly's unsafe and optimization flags, the release's
    /// arguments, then the response file's (a later option wins: Unity 6000.0.71f1,
    /// tests/projects/gameinterop rsp.*).</summary>
    sealed record Options(CSharpParseOptions Parse, CSharpCompilationOptions Compilation, List<Diagnostic> Errors, List<string> Ignored, string? RuleSet);

    /// <summary>Options that change what the compiler does but that Unbound does not apply.</summary>
    static readonly string[] s_IgnoredOptions = { "analyzer", "a", "additionalfile", "resource", "res", "linkresource", "linkres",
        "link", "l", "addmodule", "keyfile", "keycontainer", "delaysign", "publicsign", "win32res", "win32icon", "win32manifest", "doc", "generatedfilesout",
        "features", "analyzerconfig", "globalconfig", "platform", "target", "t", "recurse", "lib", "main", "m", "out", "refout", "refonly",
        "pathmap", "embed", "modulename", "nostdlib", "noconfig", "appconfig", "subsystemversion", "baseaddress", "filealign", "highentropyva",
        "sourcelink", "instrument", "errorlog", "reportanalyzer", "skipanalyzers" };

    static Options CommandLine(AssemblySpec spec, CompileRequest request)
    {
        var args = new List<string> { "-target:library", "-langversion:" + request.LanguageVersion, spec.Optimize ? "-optimize+" : "-optimize-" };
        if (spec.AllowUnsafe) args.Add("-unsafe");
        args.AddRange(request.CompilerOptions);
        // The compiler reads each of the response file's lines as a command line of its own.
        foreach (var argument in spec.ResponseArguments)
            args.AddRange(CommandLineParser.SplitCommandLineIntoArguments(argument, removeHashComments: false));
        var parsed = CSharpCommandLineParser.Default.Parse(args, string.IsNullOrEmpty(request.ProjectPath) ? Directory.GetCurrentDirectory() : request.ProjectPath, sdkDirectory: null);
        // Without source files or an output the parser warns; those are not the options' fault.
        var errors = parsed.Errors.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id is not ("CS2008" or "CS1562")).ToList();
        var language = parsed.ParseOptions.LanguageVersion;
        // The release's compiler is older than Unbound's: "latest", "preview" and the like mean its
        // newest version, and a version past it is refused as that compiler refuses it.
        if (request.LanguageVersionMax != null && LanguageVersionFacts.TryParse(request.LanguageVersionMax, out var max) && language > max)
        {
            var asked = args.LastOrDefault(a => a.Length > 1 && (a[0] == '-' || a[0] == '/') && a.Substring(1).StartsWith("langversion:", StringComparison.OrdinalIgnoreCase));
            var value = asked?.Substring(asked.IndexOf(':') + 1) ?? "";
            if (LanguageVersionFacts.TryParse(value, out var named) && named.MapSpecifiedToEffectiveVersion() == named && named > max)
                errors.Add(Diagnostic.Create(new DiagnosticDescriptor("CS1617", "Invalid option",
                    $"Invalid option '{value}' for /langversion: the release's compiler knows C# up to {request.LanguageVersionMax}", "Compiler", DiagnosticSeverity.Error, true), Location.None));
            language = max;
        }
        var ignored = spec.ResponseArguments.Where(a =>
        {
            if (a.Length < 2 || (a[0] != '-' && a[0] != '/')) return false;
            var name = a.Substring(1).Split(':', 2)[0].TrimEnd('+', '-');
            return s_IgnoredOptions.Contains(name, StringComparer.OrdinalIgnoreCase);
        }).ToList();
        var parse = new CSharpParseOptions(language, DocumentationMode.None, SourceCodeKind.Regular,
            spec.Defines.Concat(parsed.ParseOptions.PreprocessorSymbolNames));
        var c = parsed.CompilationOptions;
        // Binding redirect notes that the Editor does not show either.
        var diagnostics = c.SpecificDiagnosticOptions.SetItem("CS1701", ReportDiagnostic.Suppress).SetItem("CS1702", ReportDiagnostic.Suppress);
        var compilation = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: c.OptimizationLevel,
                checkOverflow: c.CheckOverflow,
                allowUnsafe: c.AllowUnsafe,
                deterministic: true,
                concurrentBuild: true,
                nullableContextOptions: c.NullableContextOptions,
                generalDiagnosticOption: c.GeneralDiagnosticOption,
                warningLevel: c.WarningLevel,
                specificDiagnosticOptions: diagnostics,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
        return new Options(parse, compilation, errors, ignored, parsed.RuleSetPath);
    }

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
        List<(string Identity, MetadataReference Reference)> references,
        string? ruleSet)
    {
        var builder = new StringBuilder();
        // A rule set file's contents decide diagnostics: an edit of it compiles again (not of a
        // rule set it includes).
        if (ruleSet != null) builder.Append("ruleset ").Append(File.Exists(ruleSet) ? Hash(File.ReadAllBytes(ruleSet)) : "missing").Append('\n');
        builder.Append(CompilerIdentity).Append('\n');
        builder.Append(spec.Name).Append('\n');
        builder.Append(request.LanguageVersion).Append('\n');
        builder.Append(spec.AllowUnsafe).Append('\n');
        builder.Append(spec.Optimize ? "optimize" : "debug").Append('\n');
        foreach (var option in request.CompilerOptions) builder.Append(option).Append('\n');
        foreach (var argument in spec.ResponseArguments) builder.Append("rsp ").Append(argument).Append('\n');
        builder.Append(request.LanguageVersionMax).Append('\n');
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
        var commandLine = CommandLine(spec, request);
        var parseOptions = commandLine.Parse;
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
        var compilation = CSharpCompilation.Create(spec.Name, trees, references, commandLine.Compilation);
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
        var options = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options
            ?? CommandLine(spec, request).Parse;
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
        foreach (var path in (spec.NetStandard ? request.NetStandardReferences : request.BclReferences)) references.Add(FileRef(path).Reference);
        foreach (var path in EngineReferences(spec, request)) references.Add(FileRef(path).Reference);
        foreach (var path in spec.Plugins) references.Add(FileRef(path).Reference);
        foreach (var dependency in spec.References)
            if (_states.TryGetValue(dependency, out var d) && d.Interface != null) references.Add(d.Interface);
        if (state.Compilation != null && state.Generated != null) return (state.Generated, references);
        var ordered = spec.Sources.Where(state.Sources.ContainsKey).Select(p => (p, state.Sources[p])).ToList();
        var compilation = state.Compilation ?? UpdateCompilation(state, spec, request, ordered, references);
        return (WithGenerators(state, spec, request, compilation, new List<Diagnostic>()), references);
    }

    /// <summary>A cached output's warnings, or null when they cannot be read (a cache miss).</summary>
    static List<DiagnosticInfo>? ReadWarnings(string path)
    {
        if (!File.Exists(path)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<List<DiagnosticInfo>>(File.ReadAllBytes(path)); }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException) { return null; }
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

    /// <summary>As the command-line compiler the Editor runs: errors in parsing end the compile with
    /// that stage's diagnostics, then errors in declarations with those; method bodies are compiled
    /// (and their errors reported) only after both are free of errors. Null when they are.</summary>
    static IReadOnlyList<Diagnostic>? FailedEarlyStage(CSharpCompilation compilation)
    {
        var parse = compilation.GetParseDiagnostics();
        if (parse.Any(d => d.Severity == DiagnosticSeverity.Error)) return parse;
        var declaration = compilation.GetDeclarationDiagnostics();
        if (declaration.Any(d => d.Severity == DiagnosticSeverity.Error)) return parse.AddRange(declaration);
        return null;
    }

    const string AddReferenceHint = " Consider adding a reference to that assembly.";

    /// <summary>A diagnostic as the Editor reports it: a name forwarded to the module of a built-in
    /// package the project leaves out (CS1069) asks for that package rather than a reference.</summary>
    static DiagnosticInfo ToInfo(Diagnostic diagnostic, IReadOnlyList<DisabledModule> disabled)
    {
        var info = new DiagnosticInfo { Id = diagnostic.Id, Message = diagnostic.GetMessage() };
        if (diagnostic.Id == "CS1069" && info.Message.EndsWith(AddReferenceHint, StringComparison.Ordinal))
        {
            var module = disabled.FirstOrDefault(m => info.Message.Contains($"forwarded to assembly '{m.Assembly},", StringComparison.Ordinal));
            if (module != null)
                info.Message = info.Message[..^AddReferenceHint.Length] +
                    $" Enable the built in package '{module.DisplayName}' in the Package Manager window to fix this error.";
        }
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
