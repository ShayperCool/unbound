using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Unbound.ApiTools;

/// <summary>
/// Measures how much of the Unity API a project's assemblies use that Unbound's implementation
/// (<c>UnityEngine.CoreModule</c>) does not provide yet.
///
/// Every type and member reference of the game's assemblies that binds to a Unity module is checked
/// against the implementation the way the runtime will bind it once the module facades forward to
/// it: a type by full name (any visibility: Unity's modules grant their friends access), a member by
/// name and signature, searching base types. On top of explicit references, overrides of Unity
/// virtual methods and implementations of Unity interfaces need the virtual/interface member to
/// exist, or the engine never calls the game's method; those are checked against the declaring
/// type Unity's own modules give.
///
/// Usage counts are call sites: IL instructions referencing the item (directly or through a generic
/// instantiation) plus custom attribute uses; a reference with no such site (a signature, base type,
/// or explicit interface implementation) counts once. A type's count includes its members' counts.
/// </summary>
public static class ApiGaps
{
    public static GapReport Analyze(IEnumerable<string> gameAssemblies, string implAssembly, string unityManagedEngineDir,
        IEnumerable<string>? platformModules = null)
    {
        var watch = Stopwatch.StartNew();
        var report = new GapReport();
        var impl = ApiIndex.Load(new[] { implAssembly });
        var unityFiles = UnityModules.Files(unityManagedEngineDir, platformModules);
        var unity = ApiIndex.Load(unityFiles);

        var games = new List<LoadedAssembly>();
        var gameNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in gameAssemblies.Select(Path.GetFullPath).Distinct().OrderBy(p => p, StringComparer.Ordinal))
        {
            var assembly = LoadedAssembly.TryLoad(path, out var problem);
            if (assembly == null)
                report.Skipped.Add($"{path}: {problem}");
            else if (!gameNames.Add(assembly.Name))
                report.Skipped.Add($"{path}: another copy of {assembly.Name} is analyzed");
            else
                games.Add(assembly);
        }
        report.Assemblies = games.Select(g => g.Name).ToList();

        var analysis = new Analysis(unity, games, gameNames, unityFiles.Select(f => Path.GetFileNameWithoutExtension(f)));
        foreach (var game in games)
            analysis.Run(game);
        Evaluate(analysis, impl, report);
        report.Milliseconds = watch.ElapsedMilliseconds;
        return report;
    }

    sealed class TypeUse
    {
        public required string Name;
        public required string Module;
        public int Uses;
        public readonly SortedSet<string> Assemblies = new(StringComparer.Ordinal);
    }

    sealed class MemberUse
    {
        public required string Type;
        public required string Name;
        public required string Signature;
        public required string Display;
        public required string Kind;
        public int Uses;
        public readonly SortedSet<string> Assemblies = new(StringComparer.Ordinal);
    }

    /// <summary>Collects every Unity type and member the game's assemblies need.</summary>
    sealed class Analysis
    {
        readonly ApiIndex _unity;
        readonly ApiIndex _game;
        readonly HashSet<string> _gameNames;
        readonly HashSet<string> _modules;
        public readonly Dictionary<string, TypeUse> Types = new(StringComparer.Ordinal);
        public readonly Dictionary<string, MemberUse> Members = new(StringComparer.Ordinal);

        public Analysis(ApiIndex unity, List<LoadedAssembly> games, HashSet<string> gameNames, IEnumerable<string> modules)
        {
            _unity = unity;
            _game = ApiIndex.From(games);
            _gameNames = gameNames;
            _modules = modules.ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Whether references into this assembly are references to the engine. Packages
        /// compiled from source can be named <c>UnityEngine.*</c> too (<c>UnityEngine.UI</c>), so a
        /// name counts only when it is one of the release's modules, or a module-style name
        /// (<c>*Module</c>, a module of another release or platform) that no game assembly has.</summary>
        bool IsEngine(string? assembly) =>
            assembly != null && !_gameNames.Contains(assembly)
            && (_modules.Contains(assembly)
                || (UnityModules.HasModuleName(assembly) && assembly.EndsWith("Module", StringComparison.Ordinal)));

        void UseType(string name, string referencedAssembly, string user, int uses)
        {
            if (!Types.TryGetValue(name, out var use))
            {
                var module = _unity.Find(name, referencedAssembly)?.Assembly.Name ?? referencedAssembly;
                Types[name] = use = new TypeUse { Name = name, Module = module };
            }
            use.Uses += uses;
            use.Assemblies.Add(user);
        }

        void UseMember(string type, string module, string name, string signature, string display, string kind, string user, int uses)
        {
            var key = type + "::" + name + " " + kind[0] + " " + signature;
            if (!Members.TryGetValue(key, out var use))
                Members[key] = use = new MemberUse { Type = type, Name = name, Signature = signature, Display = display, Kind = kind };
            use.Uses += uses;
            use.Assemblies.Add(user);
            UseType(type, module, user, uses);
        }

        /// <summary>The Unity type a TypeRef or generic instantiation names, if it is one.</summary>
        (string Name, string Assembly)? EngineType(MetadataReader md, EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeSpecification)
                handle = MetadataNames.GenericDefinition(md, (TypeSpecificationHandle)handle);
            if (handle.Kind != HandleKind.TypeReference)
                return null;
            var reference = (TypeReferenceHandle)handle;
            var assembly = MetadataNames.TypeReferenceAssembly(md, reference);
            return IsEngine(assembly) ? (MetadataNames.TypeReference(md, reference), assembly!) : null;
        }

        public void Run(LoadedAssembly game)
        {
            var md = game.Reader;
            var sites = CountSites(game);
            int Sites(EntityHandle handle) => sites.TryGetValue(handle, out var n) ? n : 0;

            foreach (var handle in md.TypeReferences)
                if (EngineType(md, handle) is { } type)
                    UseType(type.Name, type.Assembly, game.Name, Math.Max(1, Sites(handle)));

            // typeof(UnityEvent<int>), new List<...> etc. name a type through a TypeSpec.
            for (int row = 1, rows = md.GetTableRowCount(TableIndex.TypeSpec); row <= rows; row++)
            {
                var handle = MetadataTokens.TypeSpecificationHandle(row);
                if (Sites(handle) > 0 && EngineType(md, handle) is { } type)
                    UseType(type.Name, type.Assembly, game.Name, Sites(handle));
            }

            foreach (var handle in md.MemberReferences)
            {
                var member = md.GetMemberReference(handle);
                if (EngineType(md, member.Parent) is not { } type)
                    continue;
                var name = md.GetString(member.Name);
                var uses = Math.Max(1, Sites(handle));
                if (member.GetKind() == MemberReferenceKind.Method)
                {
                    var sig = member.DecodeMethodSignature(CanonicalSignatures.Instance, null);
                    UseMember(type.Name, type.Assembly, name, CanonicalSignatures.Method(sig),
                        CanonicalSignatures.Display(name, sig), "method", game.Name, uses);
                }
                else
                {
                    var sig = member.DecodeFieldSignature(CanonicalSignatures.Instance, null);
                    UseMember(type.Name, type.Assembly, name, sig, name + " : " + sig, "field", game.Name, uses);
                }
            }

            foreach (var handle in md.TypeDefinitions)
                CheckInheritance(game, handle);
        }

        /// <summary>Overrides of Unity virtual methods and implementations of Unity interfaces.</summary>
        void CheckInheritance(LoadedAssembly game, TypeDefinitionHandle handle)
        {
            var md = game.Reader;
            var definition = md.GetTypeDefinition(handle);
            if (EngineBase(game, definition.BaseType) is { } engineBase)
            {
                foreach (var methodHandle in definition.GetMethods())
                {
                    var method = md.GetMethodDefinition(methodHandle);
                    // `override` is virtual without newslot; explicit interface implementations
                    // are newslot and reference their interface method through a MemberRef.
                    if ((method.Attributes & MethodAttributes.Virtual) == 0 || (method.Attributes & MethodAttributes.NewSlot) != 0)
                        continue;
                    var name = md.GetString(method.Name);
                    var sig = method.DecodeSignature(CanonicalSignatures.Instance, null);
                    var signature = CanonicalSignatures.Method(sig);
                    if (ObjectVirtuals.Contains(name + " " + signature))
                        continue; // System.Object provides the slot whatever Unity's type overrides
                    if (_unity.FindVirtual(engineBase, name, signature)?.Type is { } declaring)
                        UseMember(declaring.FullName, declaring.Assembly.Name, name, signature,
                            CanonicalSignatures.Display(name, sig), "override", game.Name, 1);
                }
            }
            foreach (var implementation in definition.GetInterfaceImplementations())
            {
                var face = md.GetInterfaceImplementation(implementation).Interface;
                if (EngineType(md, face) is not { } type || _unity.Find(type.Name, type.Assembly) is not { } unityType)
                    continue;
                var umd = unityType.Assembly.Reader;
                foreach (var methodHandle in unityType.Definition.GetMethods())
                {
                    var method = umd.GetMethodDefinition(methodHandle);
                    if ((method.Attributes & MethodAttributes.Abstract) == 0)
                        continue; // static members and default implementations need no override
                    var name = umd.GetString(method.Name);
                    var sig = method.DecodeSignature(CanonicalSignatures.Instance, null);
                    UseMember(unityType.FullName, unityType.Assembly.Name, name, CanonicalSignatures.Method(sig),
                        CanonicalSignatures.Display(name, sig), "interface", game.Name, 1);
                }
            }
        }

        static readonly HashSet<string> ObjectVirtuals = new(StringComparer.Ordinal)
        {
            "ToString instance System.String()",
            "Equals instance System.Boolean(System.Object)",
            "GetHashCode instance System.Int32()",
            "Finalize instance System.Void()",
        };

        /// <summary>The nearest Unity type among a game type's base types, following base types
        /// through the game's own assemblies.</summary>
        ApiType? EngineBase(LoadedAssembly assembly, EntityHandle baseType)
        {
            for (int depth = 0; depth < 64 && !baseType.IsNil; depth++)
            {
                var md = assembly.Reader;
                switch (baseType.Kind)
                {
                    case HandleKind.TypeDefinition:
                        baseType = md.GetTypeDefinition((TypeDefinitionHandle)baseType).BaseType;
                        break;
                    case HandleKind.TypeSpecification:
                        baseType = MetadataNames.GenericDefinition(md, (TypeSpecificationHandle)baseType);
                        break;
                    case HandleKind.TypeReference:
                        var reference = (TypeReferenceHandle)baseType;
                        var name = MetadataNames.TypeReference(md, reference);
                        var scope = MetadataNames.TypeReferenceAssembly(md, reference) ?? assembly.Name;
                        if (IsEngine(scope))
                            return _unity.Find(name, scope);
                        if (_game.Find(name, scope) is not { } next || next.Assembly.Name != scope)
                            return null; // the class library, or an assembly not analyzed
                        assembly = next.Assembly;
                        baseType = next.Definition.BaseType;
                        break;
                    default:
                        return null;
                }
            }
            return null;
        }

        /// <summary>Call sites per referenced TypeRef, TypeSpec, MemberRef and MethodSpec. A generic
        /// method instantiation's sites count for the method too.</summary>
        static Dictionary<EntityHandle, int> CountSites(LoadedAssembly game)
        {
            var md = game.Reader;
            var sites = new Dictionary<EntityHandle, int>();
            void Add(EntityHandle handle, int n) => sites[handle] = (sites.TryGetValue(handle, out var c) ? c : 0) + n;

            foreach (var handle in md.MethodDefinitions)
            {
                var rva = md.GetMethodDefinition(handle).RelativeVirtualAddress;
                if (rva == 0)
                    continue;
                try
                {
                    ILScanner.Tokens(game.PE.GetMethodBody(rva).GetILReader(), token =>
                    {
                        switch (token >> 24)
                        {
                            case 0x01: // TypeRef
                            case 0x0A: // MemberRef
                            case 0x1B: // TypeSpec
                            case 0x2B: // MethodSpec
                                Add(MetadataTokens.EntityHandle(token), 1);
                                break;
                        }
                    });
                }
                catch (BadImageFormatException)
                {
                    // An unreadable body (obfuscation): its sites go uncounted.
                }
            }
            foreach (var handle in md.CustomAttributes)
            {
                var constructor = md.GetCustomAttribute(handle).Constructor;
                if (constructor.Kind == HandleKind.MemberReference)
                    Add(constructor, 1);
            }
            for (int row = 1, rows = md.GetTableRowCount(TableIndex.MethodSpec); row <= rows; row++)
            {
                var handle = MetadataTokens.MethodSpecificationHandle(row);
                if (sites.TryGetValue(handle, out var n))
                    Add(md.GetMethodSpecification(handle).Method, n);
            }
            return sites;
        }
    }

    /// <summary>Checks every collected use against the implementation and fills the report.</summary>
    static void Evaluate(Analysis analysis, ApiIndex impl, GapReport report)
    {
        var missingTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var use in analysis.Types.Values)
        {
            var found = impl.Find(use.Name);
            var stub = found?.IsStub == true;
            var satisfied = (found != null || impl.IsForwarded(use.Name)) && !stub;
            report.Types.Count(satisfied, stub, use.Uses);
            if (satisfied)
                continue;
            missingTypes.Add(use.Name);
            report.MissingTypes.Add(new MissingType
            {
                Name = use.Name,
                Module = use.Module,
                Stub = stub,
                Uses = use.Uses,
                Assemblies = use.Assemblies.ToList(),
            });
        }

        var groups = new Dictionary<string, MissingMemberGroup>(StringComparer.Ordinal);
        foreach (var use in analysis.Members.Values)
        {
            var found = impl.Find(use.Type) is { } type
                ? use.Kind switch
                {
                    "field" => impl.FindField(type, use.Name, use.Signature),
                    "override" or "interface" => impl.FindVirtual(type, use.Name, use.Signature),
                    _ => impl.FindMethod(type, use.Name, use.Signature),
                }
                : null;
            var stub = found?.IsStub == true;
            // A type forwarded elsewhere by the implementation is not ours to check.
            var satisfied = found != null ? !stub : impl.Find(use.Type) == null && impl.IsForwarded(use.Type);
            report.Members.Count(satisfied, stub, use.Uses);
            if (satisfied)
                continue;
            if (!groups.TryGetValue(use.Type, out var group))
            {
                groups[use.Type] = group = new MissingMemberGroup
                {
                    Type = use.Type,
                    Module = analysis.Types[use.Type].Module,
                    TypeMissing = missingTypes.Contains(use.Type),
                };
            }
            group.Uses += use.Uses;
            group.Members.Add(new MissingMember
            {
                Name = use.Name,
                Kind = use.Kind,
                Stub = stub,
                Signature = use.Display,
                Uses = use.Uses,
                Assemblies = use.Assemblies.ToList(),
            });
        }

        report.MissingTypes.Sort((a, b) => b.Uses != a.Uses ? b.Uses.CompareTo(a.Uses) : string.CompareOrdinal(a.Name, b.Name));
        foreach (var group in groups.Values)
            group.Members.Sort((a, b) => b.Uses != a.Uses ? b.Uses.CompareTo(a.Uses) : string.CompareOrdinal(a.Signature, b.Signature));
        report.MissingMembers = groups.Values
            .OrderByDescending(g => g.Uses)
            .ThenBy(g => g.Type, StringComparer.Ordinal)
            .ToList();
    }
}

public sealed class GapReport
{
    /// <summary>The game assemblies analyzed, by name.</summary>
    public List<string> Assemblies { get; set; } = new();
    /// <summary>Inputs that could not be analyzed (native DLLs, duplicates), with the reason.</summary>
    public List<string> Skipped { get; set; } = new();
    public Coverage Types { get; set; } = new();
    public Coverage Members { get; set; } = new();
    /// <summary>Sorted by uses, most used first.</summary>
    public List<MissingType> MissingTypes { get; set; } = new();
    /// <summary>Grouped by declaring type; groups and members sorted by uses.</summary>
    public List<MissingMemberGroup> MissingMembers { get; set; } = new();
    public long Milliseconds { get; set; }
}

/// <summary>How many distinct referenced items the implementation satisfies, and how many uses
/// (call sites) those account for. Items that exist only as generated stubs count as missing, and
/// are also counted on their own.</summary>
public sealed class Coverage
{
    public int Referenced { get; set; }
    public int Satisfied { get; set; }
    public int Missing { get; set; }
    public long Uses { get; set; }
    public long SatisfiedUses { get; set; }
    /// <summary>Missing items that exist as generated stubs.</summary>
    public int Stubbed { get; set; }
    public long StubbedUses { get; set; }

    internal void Count(bool satisfied, bool stub, int uses)
    {
        Referenced++;
        Uses += uses;
        if (stub)
        {
            Stubbed++;
            StubbedUses += uses;
        }
        if (satisfied)
        {
            Satisfied++;
            SatisfiedUses += uses;
        }
        else
        {
            Missing++;
        }
    }
}

public sealed class MissingType
{
    public string Name { get; set; } = "";
    /// <summary>The Unity module that defines the type.</summary>
    public string Module { get; set; } = "";
    /// <summary>The implementation has it as a generated stub.</summary>
    public bool Stub { get; set; }
    public int Uses { get; set; }
    public List<string> Assemblies { get; set; } = new();
}

public sealed class MissingMemberGroup
{
    public string Type { get; set; } = "";
    public string Module { get; set; } = "";
    /// <summary>The declaring type itself is missing too.</summary>
    public bool TypeMissing { get; set; }
    public int Uses { get; set; }
    public List<MissingMember> Members { get; set; } = new();
}

public sealed class MissingMember
{
    public string Name { get; set; } = "";
    /// <summary><c>method</c>, <c>field</c>, <c>override</c> (a Unity virtual the game overrides) or
    /// <c>interface</c> (a member of a Unity interface the game implements).</summary>
    public string Kind { get; set; } = "";
    /// <summary>Readable signature: <c>Name(params) : return</c>, <c>static</c> when static.</summary>
    public string Signature { get; set; } = "";
    /// <summary>The implementation has it as a generated stub.</summary>
    public bool Stub { get; set; }
    public int Uses { get; set; }
    public List<string> Assemblies { get; set; } = new();
}
