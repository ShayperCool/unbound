using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unbound.ApiTools;

/// <summary>
/// Generates compatibility stubs: C# declarations of every type and member a Unity release's
/// modules have that Unbound's implementation (<c>UnityEngine.CoreModule</c>) lacks, so that the
/// implementation has the release's complete API surface. Game code then loads and binds; calling a
/// stub logs a warning once (<c>Unbound.Runtime.Stubs.NotImplemented</c>) and returns a default.
///
/// Inputs are the release's module assemblies (signatures and constants only: no Unity code is
/// read) and the implementation compiled without stubs. The output is one source file per module,
/// <c>&lt;Module&gt;.g.cs</c>: new types in full, and <c>partial</c> declarations adding the missing
/// members to hand-written types. Generated types and members carry <c>[Unbound.Stub]</c> so the API
/// gap report can tell them from real implementations. Output is deterministic and files are only
/// rewritten when their content changes.
/// </summary>
public static class StubGenerator
{
    /// <summary>Bump when the output for the same inputs would differ.</summary>
    public const string FormatVersion = "unbound-stubs-1";

    /// <param name="implSourceDir">The implementation's sources (<c>managed/UnityEngine</c>): the
    /// generator reads which hand-written types are partial and declare constructors.</param>
    /// <param name="platformModules">The install's platform modules (see <see cref="UnityModules"/>),
    /// stubbed like the others.</param>
    public static StubReport Generate(string unityManagedEngineDir, string implAssembly, string outputDir, string? implSourceDir = null,
        IEnumerable<string>? platformModules = null)
    {
        var watch = Stopwatch.StartNew();
        var builder = new StubBuilder(unityManagedEngineDir, platformModules, implAssembly, new HandWrittenSources(implSourceDir));
        var files = builder.Build();

        Directory.CreateDirectory(outputDir);
        var report = builder.Report;
        report.OutputDir = outputDir;
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(outputDir, name);
            written.Add(name);
            if (File.Exists(path) && File.ReadAllText(path) == content)
                continue;
            File.WriteAllText(path, content);
            report.ChangedFiles++;
        }
        foreach (var stale in Directory.GetFiles(outputDir, "*.g.cs"))
        {
            if (!written.Contains(Path.GetFileName(stale)))
            {
                File.Delete(stale);
                report.ChangedFiles++;
            }
        }
        report.Milliseconds = watch.ElapsedMilliseconds;
        return report;
    }
}

public sealed class StubReport
{
    public string OutputDir { get; set; } = "";
    public List<StubModuleReport> Modules { get; set; } = new();
    /// <summary>Types generated in full.</summary>
    public int NewTypes { get; set; }
    /// <summary>Hand-written types that got missing members through a partial declaration.</summary>
    public int ExtendedTypes { get; set; }
    public int Members { get; set; }
    /// <summary>Types and members that could not be generated, with the reason (a type from an
    /// assembly the implementation does not reference, ...).</summary>
    public List<string> Skipped { get; set; } = new();
    /// <summary>Differences between a hand-written type and Unity's that generation cannot fix:
    /// a different kind or base type, an enum missing values, a member that is less accessible.</summary>
    public List<string> Conflicts { get; set; } = new();
    public int ChangedFiles { get; set; }
    public long Milliseconds { get; set; }
}

public sealed class StubModuleReport
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public int NewTypes { get; set; }
    public int ExtendedTypes { get; set; }
    public int Members { get; set; }
}

/// <summary>The two views the generator compares: Unity's modules (against Unity's netstandard) and
/// the implementation (against the .NET reference assemblies it is built with).</summary>
sealed partial class StubBuilder
{
    readonly CSharpCompilation _unity;
    readonly CSharpCompilation _impl;
    readonly IAssemblySymbol _implAssembly;
    /// <summary>Unity's modules in ownership order: real modules first, then the monolithic
    /// UnityEngine.dll/UnityEditor.dll, whose own types are only legacy leftovers.</summary>
    readonly List<(string Name, IAssemblySymbol Assembly, MetadataReader Reader)> _modules = new();
    readonly HashSet<IAssemblySymbol> _unityModules = new(SymbolEqualityComparer.Default);
    /// <summary>Each top-level Unity type by metadata name, and the module that owns it.</summary>
    readonly Dictionary<string, (string Module, INamedTypeSymbol Type)> _owners = new(StringComparer.Ordinal);

    public StubReport Report { get; } = new();

    readonly HandWrittenSources _sources;

    public StubBuilder(string unityManagedEngineDir, IEnumerable<string>? platformModules, string implAssembly, HandWrittenSources sources)
    {
        _sources = sources;
        var moduleFiles = UnityModules.Files(unityManagedEngineDir, platformModules)
            .OrderBy(f => Path.GetFileNameWithoutExtension(f) is "UnityEngine" or "UnityEditor" ? 1 : 0)
            .ThenBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
        // The class library Unity's modules are built against: the release's netstandard, else (a
        // directory of modules outside an install, in tests) the framework the generator runs on.
        var netstandard = Path.GetFullPath(Path.Combine(unityManagedEngineDir, "../../NetStandard/ref/2.1.0/netstandard.dll"));
        var classLibrary = File.Exists(netstandard)
            ? new[] { MetadataReference.CreateFromFile(netstandard) }
            : FrameworkReferences(implementation: true).ToArray();

        var moduleReferences = moduleFiles.Select(f => MetadataReference.CreateFromFile(f)).ToList();
        _unity = CSharpCompilation.Create(
            "unity",
            references: moduleReferences.Concat(classLibrary),
            options: Options);
        for (int i = 0; i < moduleFiles.Count; i++)
        {
            var assembly = (IAssemblySymbol)_unity.GetAssemblyOrModuleSymbol(moduleReferences[i])!;
            var pe = new PEReader(File.ReadAllBytes(moduleFiles[i]).ToImmutableArrayUnsafe());
            _modules.Add((assembly.Name, assembly, pe.GetMetadataReader()));
            _unityModules.Add(assembly);
        }

        var implReference = MetadataReference.CreateFromFile(implAssembly);
        _impl = CSharpCompilation.Create(
            "impl",
            references: FrameworkReferences(implementation: ReferencesCoreLib(implAssembly)).Append(implReference),
            options: Options);
        _implAssembly = (IAssemblySymbol)_impl.GetAssemblyOrModuleSymbol(implReference)!;
    }

    static readonly CSharpCompilationOptions Options = new(
        OutputKind.DynamicallyLinkedLibrary,
        metadataImportOptions: MetadataImportOptions.All);

    /// <summary>The .NET reference assemblies the implementation is compiled against (the targeting
    /// pack matching the running runtime), or the runtime's own assemblies when it is absent.</summary>
    /// <param name="implementation">The running runtime's own assemblies instead (what assemblies
    /// built against the implementation assemblies, rather than reference assemblies, need).</param>
    static IEnumerable<MetadataReference> FrameworkReferences(bool implementation = false)
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "../../.."));
        var packs = Path.Combine(dotnetRoot, "packs/Microsoft.NETCore.App.Ref");
        var major = Environment.Version.Major;
        string? refDir = null;
        if (Directory.Exists(packs) && !implementation)
        {
            refDir = Directory.GetDirectories(packs)
                .Where(d => Path.GetFileName(d).StartsWith(major + ".", StringComparison.Ordinal))
                .OrderBy(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version())
                .Select(d => Path.Combine(d, $"ref/net{major}.0"))
                .LastOrDefault(Directory.Exists);
        }
        var dir = refDir ?? runtimeDir;
        foreach (var file in Directory.GetFiles(dir, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
        {
            using (var pe = new PEReader(File.OpenRead(file)))
                if (!pe.HasMetadata)
                    continue;
            yield return MetadataReference.CreateFromFile(file);
        }
    }

    /// <summary>Whether an assembly was built against the runtime's implementation assemblies
    /// (it references System.Private.CoreLib) rather than the reference assemblies.</summary>
    static bool ReferencesCoreLib(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();
        return md.AssemblyReferences.Any(r => md.GetString(md.GetAssemblyReference(r).Name) == "System.Private.CoreLib");
    }

    /// <summary>Records which module owns each top-level Unity type name (the first in ownership
    /// order, for names several modules define).</summary>
    void IndexOwners()
    {
        if (_owners.Count > 0)
            return;
        foreach (var (module, assembly, _) in _modules)
        {
            foreach (var type in TopLevelTypes(assembly.GlobalNamespace))
            {
                var name = MetadataName(type);
                if (!_owners.ContainsKey(name))
                    _owners[name] = (module, type);
            }
        }
    }

    /// <summary>Generates every module's file; returns (file name, content) pairs.</summary>
    public List<(string Name, string Content)> Build()
    {
        IndexOwners();
        var files = new List<(string, string)>();
        foreach (var (module, assembly, _) in _modules)
        {
            var types = _owners.Values
                .Where(o => o.Module == module)
                .Select(o => o.Type)
                .OrderBy(MetadataName, StringComparer.Ordinal)
                .ToList();
            var moduleReport = new StubModuleReport { Name = module, File = module + ".g.cs" };
            var body = new CodeWriter();
            foreach (var group in types.GroupBy(t => t.ContainingNamespace.IsGlobalNamespace ? "" : t.ContainingNamespace.ToDisplayString()))
            {
                var namespaceWriter = new CodeWriter();
                foreach (var type in group)
                    EmitTopLevel(namespaceWriter, type, moduleReport);
                if (namespaceWriter.IsEmpty)
                    continue;
                if (group.Key.Length == 0)
                {
                    body.Append(namespaceWriter);
                }
                else
                {
                    body.Line($"namespace {group.Key}");
                    body.Line("{");
                    body.Indent();
                    body.Append(namespaceWriter);
                    body.Outdent();
                    body.Line("}");
                }
            }
            if (body.IsEmpty)
                continue;
            Report.Modules.Add(moduleReport);
            Report.NewTypes += moduleReport.NewTypes;
            Report.ExtendedTypes += moduleReport.ExtendedTypes;
            Report.Members += moduleReport.Members;
            files.Add((moduleReport.File, Header($"the API of {module}") + body));
        }
        files.Add(("Unbound.Assembly.g.cs", Header("assembly-level attributes of the modules") + AssemblyAttributes()));
        return files;
    }

    static string Header(string what) => new StringBuilder()
        .Append("// <auto-generated/>\n")
        .Append($"// Compatibility stubs: {what}, generated by Unbound.ApiTools.StubGenerator ({StubGenerator.FormatVersion})\n")
        .Append("// from the signatures and constants of Unity's reference assemblies. Do not edit: regenerate\n")
        .Append("// with `unbound gen-stubs`. Implement a member by writing it in the hand-written sources; the\n")
        .Append("// next generation drops the stub.\n")
        .Append("#pragma warning disable\n\n")
        .ToString();

    /// <summary>The union of the modules' InternalsVisibleTo (internals of every module now live in
    /// the one implementation assembly) and forwarders for the class library types Unity's modules
    /// define themselves (UnityEngine.dll forwards e.g. <c>UnmanagedCallersOnlyAttribute</c> to a
    /// module, so its facade forwards it to us and we forward it on to the runtime's own).</summary>
    string AssemblyAttributes()
    {
        var existing = _implAssembly.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var friends = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, assembly, _) in _modules)
            foreach (var attribute in assembly.GetAttributes())
                if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InternalsVisibleToAttribute"
                    && attribute.ConstructorArguments.FirstOrDefault().Value is string friend
                    && !existing.Contains(friend))
                    friends.Add(friend);

        var forwards = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, assembly, _) in _modules)
        {
            foreach (var type in TopLevelTypes(assembly.GlobalNamespace))
            {
                var name = MetadataName(type);
                if (!IsClassLibraryNamespace(type) || type.IsGenericType)
                    continue;
                if (_impl.GetTypesByMetadataName(name).FirstOrDefault(t => t.DeclaredAccessibility == Accessibility.Public
                        && !SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, _implAssembly)) is { } bcl)
                    forwards.Add(bcl.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        var w = new StringBuilder();
        foreach (var friend in friends)
            w.Append($"[assembly: global::System.Runtime.CompilerServices.InternalsVisibleTo({SymbolDisplay.FormatLiteral(friend, true)})]\n");
        foreach (var forward in forwards)
            w.Append($"[assembly: global::System.Runtime.CompilerServices.TypeForwardedTo(typeof({forward}))]\n");
        return w.ToString();
    }

    static IEnumerable<INamedTypeSymbol> TopLevelTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in TopLevelTypes(child))
                yield return type;
    }

    /// <summary><c>Namespace.Outer+Inner`1</c>: the name <see cref="Compilation.GetTypeByMetadataName"/> takes.</summary>
    static string MetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType != null)
            return MetadataName(type.ContainingType) + "+" + type.MetadataName;
        return type.ContainingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
    }

    /// <summary>Types Unity's modules define in the class library's namespaces (compiler-embedded
    /// attributes, polyfills): never generated, the runtime has its own.</summary>
    static bool IsClassLibraryNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace.ToDisplayString();
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
            || ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }
}

static class ImmutableArrayExtensions
{
    public static System.Collections.Immutable.ImmutableArray<byte> ToImmutableArrayUnsafe(this byte[] bytes) =>
        System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes);
}

/// <summary>An indenting source writer.</summary>
sealed class CodeWriter
{
    readonly StringBuilder _text = new();
    int _indent;

    public bool IsEmpty => _text.Length == 0;
    public void Indent() => _indent++;
    public void Outdent() => _indent--;

    public void Line(string line)
    {
        if (line.Length > 0)
            _text.Append(' ', _indent * 4);
        _text.Append(line).Append('\n');
    }

    /// <summary>Appends another writer's text, indented to this writer's level.</summary>
    public void Append(CodeWriter other)
    {
        foreach (var line in other._text.ToString().Split('\n'))
        {
            if (line.Length == 0)
                continue;
            _text.Append(' ', _indent * 4).Append(line).Append('\n');
        }
    }

    public override string ToString() => _text.ToString();
}
