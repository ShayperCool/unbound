using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unbound.Compiler;

/// <summary>
/// The Editor's IL post-processing (Unity.ILPP.Runner): after the script assemblies compile, the
/// <c>ILPostProcessor</c>s of the IL post-processor assemblies (<c>Unity.*.CodeGen</c>,
/// <c>Unity.*.Compiler</c>: Burst, Collections, Netcode, Entities) run, sorted by type name, over
/// every other assembly not on the Editor's skip list; what they produce is what the game loads.
/// The processors load into a collectible context of their own, with their dependencies (their
/// own Mono.Cecil) resolved from the assemblies they reference, as the runner resolves them. The
/// context is unloaded once a request is processed: the runner is a process of its own, so the
/// game's domain never has the processors or the Editor's assemblies they read
/// (<c>AppDomain.GetAssemblies()</c> would list them, and their types do not load there).
/// Outputs are cached by the hashes of the input, the processor assemblies and the references.
/// </summary>
public sealed class ILPostProcessing
{
    /// <summary>Bump when processing the same inputs would give different outputs.</summary>
    const string FormatVersion = "unbound-ilpp-1";

    sealed class Configuration
    {
        public string Key = "";
        public ProcessorContext? Context;
        public List<(string Name, Type Type)> Processors = new();
    }

    Configuration? _configuration;
    readonly object _lock = new();
    readonly ConcurrentDictionary<string, (long, long, string)> _fileHashes = new();

    /// <summary>The name rule of the Editor's build program: <c>Unity.*.CodeGen</c> or <c>Unity.*.Compiler</c>.</summary>
    public static bool IsPostProcessorAssembly(string name) =>
        name.StartsWith("Unity.", StringComparison.Ordinal) &&
        (name.EndsWith(".CodeGen", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Compiler", StringComparison.OrdinalIgnoreCase));

    /// <summary>Hash of a file's content, cached by its stamp.</summary>
    string FileHash(string path)
    {
        var info = new FileInfo(path);
        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
        if (_fileHashes.TryGetValue(path, out var known) && known.Item1 == stamp.Ticks && known.Item2 == stamp.Length) return known.Item3;
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        _fileHashes[path] = (stamp.Ticks, stamp.Length, hash);
        return hash;
    }

    /// <summary>
    /// Post-processes the compiled assemblies of a request in place of their results: the result of
    /// an assembly a processor changed names the processed image (<c>&lt;key&gt;.pp.dll</c>).
    /// </summary>
    public void Run(CompileRequest request, Dictionary<string, AssemblyResult> results, Func<AssemblySpec, CompileRequest, List<string>> references)
    {
        var processorSpecs = request.Assemblies.Where(a => IsPostProcessorAssembly(a.Name)).ToList();
        if (processorSpecs.Count == 0) return;
        if (processorSpecs.Any(a => !results.TryGetValue(a.Name, out var r) || r.Dll == null))
        {
            // A processor that does not compile leaves the others' outputs as they are, as the
            // Editor cannot post-process either: the failure is reported with the compile.
            return;
        }
        var specs = request.Assemblies.ToDictionary(a => a.Name);
        // The script assemblies under their own names (the cache names outputs by key): processors
        // find references by file name (Burst looks for Unity.Burst.dll), and so does the runner.
        var named = NamedView(request.CacheDir, results);
        // The processors and everything they depend on: the runner's "hint paths".
        var processorPaths = processorSpecs.Select(a => Named(UnprocessedDll(results[a.Name]), named)).ToList();
        var hints = new List<string>();
        var visited = new HashSet<string>();
        void AddDependencies(AssemblySpec spec)
        {
            foreach (var reference in spec.References)
            {
                if (!visited.Add(reference) || !specs.TryGetValue(reference, out var dependency)) continue;
                if (results.TryGetValue(reference, out var r) && r.Dll != null) hints.Add(Named(UnprocessedDll(r), named));
                AddDependencies(dependency);
            }
            foreach (var plugin in spec.Plugins) hints.Add(plugin);
        }
        foreach (var spec in processorSpecs) AddDependencies(spec);
        // Unity's own assemblies too, as the runner's hint paths have them: processors take the
        // types of attributes they add from them (typeof(InitializeOnLoadMethodAttribute)). They
        // load for their metadata; processors run no engine code.
        hints.AddRange(request.EngineReferences);
        hints = hints.Distinct().ToList();

        var configurationKey = Hash(string.Join("\n", processorPaths.Concat(hints).Select(p => Path.GetFileName(p) + "=" + FileHash(p))));
        try
        {
            ProcessAll(request, results, references, named, Configure(configurationKey, processorPaths, hints, processorSpecs.Select(s => s.Name).ToList()));
        }
        finally
        {
            // Outside the frame that used the processors, so nothing of theirs is still alive.
            Unload();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    void ProcessAll(CompileRequest request, Dictionary<string, AssemblyResult> results, Func<AssemblySpec, CompileRequest, List<string>> references,
        Dictionary<string, string> named, Configuration configuration)
    {
        if (configuration.Processors.Count == 0) return;
        var work = request.Assemblies
            .Where(a => !IsPostProcessorAssembly(a.Name) && !a.SkipCodeGen && results.TryGetValue(a.Name, out var r) && r.Dll != null)
            .ToList();
        System.Threading.Tasks.Parallel.ForEach(work, spec =>
        {
            var result = results[spec.Name];
            try
            {
                Process(spec, request, result, configuration, references(spec, request).Select(p => Named(p, named)).ToArray());
            }
            catch (Exception e)
            {
                result.Status = "failed";
                result.Dll = result.Pdb = null;
                result.Errors.Add(new DiagnosticInfo { Id = "UNBOUND0003", Message = $"IL post-processing failed: {e}" });
            }
        });
    }

    /// <summary>Unloads the processors' context and waits until its assemblies are gone.</summary>
    void Unload()
    {
        lock (_lock)
        {
            if (_configuration?.Context == null) return;
            _configuration.Context.Unload();
            _configuration = null;
        }
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>The compiler's output a result had before post-processing.</summary>
    static string UnprocessedDll(AssemblyResult result) => result.UnprocessedDll ?? result.Dll!;

    Configuration Configure(string key, List<string> processorPaths, List<string> hints, List<string> processorNames)
    {
        lock (_lock)
        {
            if (_configuration != null && _configuration.Key == key) return _configuration;
            _configuration?.Context?.Unload();
            var context = new ProcessorContext(hints);
            var configuration = new Configuration { Key = key, Context = context };
            using (context.EnterContextualReflection())
            {
                foreach (var path in processorPaths)
                {
                    var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        throw new InvalidOperationException($"loading the IL post-processors of {Path.GetFileName(path)}: " +
                            string.Join("; ", e.LoaderExceptions.Where(l => l != null).Select(l => l!.Message).Distinct()), e);
                    }
                    foreach (var type in types)
                        if (typeof(ILPostProcessor).IsAssignableFrom(type) && !type.IsAbstract)
                            configuration.Processors.Add((type.FullName ?? type.Name, type));
                }
            }
            configuration.Processors.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            _configuration = configuration;
            return configuration;
        }
    }

    sealed class CompiledAssembly : ICompiledAssembly
    {
        public InMemoryAssembly InMemoryAssembly { get; set; } = null!;
        public string Name { get; init; } = "";
        public string[] References { get; init; } = Array.Empty<string>();
        public string[] Defines { get; init; } = Array.Empty<string>();
    }

    void Process(AssemblySpec spec, CompileRequest request, AssemblyResult result, Configuration configuration, string[] references)
    {
        var dll = UnprocessedDll(result);
        var pdb = Path.ChangeExtension(dll, ".pdb");
        var key = Hash(FormatVersion + "\n" + configuration.Key + "\n" + FileHash(dll) + "\n" + string.Join(";", spec.Defines) + "\n" +
            string.Join("\n", references.Where(File.Exists).Select(r => Path.GetFileName(r) + "=" + FileHash(r))));
        var outputDll = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileNameWithoutExtension(dll) + "." + key.Substring(0, 16) + ".pp.dll");
        var outputPdb = Path.ChangeExtension(outputDll, ".pdb");
        var unchangedMarker = Path.ChangeExtension(outputDll, ".unchanged");
        result.UnprocessedDll = dll;
        if (File.Exists(unchangedMarker)) return;
        if (File.Exists(outputDll))
        {
            Use(result, outputDll, File.Exists(outputPdb) ? outputPdb : null);
            return;
        }
        var compiled = new CompiledAssembly
        {
            Name = spec.Name,
            References = references,
            Defines = spec.Defines.ToArray(),
            InMemoryAssembly = new InMemoryAssembly(File.ReadAllBytes(dll), File.Exists(pdb) ? File.ReadAllBytes(pdb) : null),
        };
        bool changed = false;
        var errors = new List<string>();
        using (configuration.Context!.EnterContextualReflection())
        {
            foreach (var (name, type) in configuration.Processors)
            {
                var processor = (ILPostProcessor?)Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Can't create instance of {type}");
                if (!processor.WillProcess(compiled)) continue;
                var processed = processor.Process(compiled);
                if (processed == null) continue;
                if (processed.Diagnostics != null)
                {
                    foreach (var message in processed.Diagnostics)
                    {
                        var text = $"{message.File}({message.Line},{message.Column}): {(message.DiagnosticType == DiagnosticType.Error ? "error" : "warning")} {message.MessageData}";
                        if (message.DiagnosticType == DiagnosticType.Error) errors.Add($"{name}: {text}");
                        else result.Warnings++;
                    }
                    if (errors.Count > 0) break;
                }
                if (processed.InMemoryAssembly != null)
                {
                    if (processed.InMemoryAssembly.PeData == null) throw new InvalidOperationException($"{name}: ILPostProcessor produced an assembly without PE data");
                    compiled.InMemoryAssembly = processed.InMemoryAssembly;
                    changed = true;
                }
            }
        }
        if (errors.Count > 0)
        {
            result.Status = "failed";
            result.Dll = result.Pdb = null;
            foreach (var error in errors.Take(100)) result.Errors.Add(new DiagnosticInfo { Id = "ILPP", Message = error });
            return;
        }
        if (!changed)
        {
            File.WriteAllText(unchangedMarker, "");
            return;
        }
        WriteAtomically(outputDll, compiled.InMemoryAssembly.PeData);
        if (compiled.InMemoryAssembly.PdbData != null) WriteAtomically(outputPdb, compiled.InMemoryAssembly.PdbData);
        Use(result, outputDll, compiled.InMemoryAssembly.PdbData != null ? outputPdb : null);
    }

    static void Use(AssemblyResult result, string dll, string? pdb)
    {
        result.Dll = dll;
        result.Pdb = pdb;
        result.PostProcessed = true;
    }

    static void WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Links named after the assemblies, to the compiler's outputs.</summary>
    static Dictionary<string, string> NamedView(string cacheDir, Dictionary<string, AssemblyResult> results)
    {
        var map = results.Values.Where(r => r.Dll != null).ToDictionary(r => UnprocessedDll(r), r => r.Name);
        var key = Hash(string.Join("\n", map.OrderBy(p => p.Value, StringComparer.Ordinal).Select(p => p.Value + "=" + p.Key))).Substring(0, 24);
        var dir = Path.Combine(cacheDir, "named", key);
        var view = new Dictionary<string, string>(StringComparer.Ordinal);
        Directory.CreateDirectory(dir);
        foreach (var (path, name) in map)
        {
            var link = Path.Combine(dir, name + ".dll");
            if (!File.Exists(link))
            {
                try
                {
                    File.CreateSymbolicLink(link, path);
                }
                catch (IOException) when (File.Exists(link))
                {
                }
            }
            view[path] = link;
        }
        return view;
    }

    static string Named(string path, Dictionary<string, string> named) => named.TryGetValue(path, out var link) ? link : path;

    /// <summary>The processors' load context: the post-processing API from this compiler, the
    /// runtime's own assemblies, everything else by name from the processors' dependencies.</summary>
    sealed class ProcessorContext : AssemblyLoadContext
    {
        static int s_Id;
        readonly Dictionary<string, string> _hints = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> s_System = new(
            Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Select(f => Path.GetFileNameWithoutExtension(f)),
            StringComparer.OrdinalIgnoreCase);

        public ProcessorContext(IEnumerable<string> hints) : base($"ILPP context {++s_Id}", isCollectible: true)
        {
            foreach (var hint in hints)
            {
                var name = Path.GetFileNameWithoutExtension(hint);
                if (s_System.Contains(name) || !File.Exists(hint)) continue;
                _hints.TryAdd(name, hint);
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null) return null;
            if (assemblyName.Name == "Unity.CompilationPipeline.Common") return typeof(ILPostProcessor).Assembly;
            if (s_System.Contains(assemblyName.Name)) return Default.LoadFromAssemblyName(assemblyName);
            if (_hints.TryGetValue(assemblyName.Name, out var path))
            {
                try
                {
                    return LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
                }
                catch (BadImageFormatException)
                {
                    return null;
                }
            }
            return null;
        }
    }
}
