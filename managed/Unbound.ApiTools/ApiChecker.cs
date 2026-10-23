using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unbound.ApiTools;

/// <summary>
/// Checks the hand-written implementation against a release's Unity modules. Game code binds to
/// Unity's exact signatures, so a hand-written member that has Unity's name but not its signature is
/// worse than a missing one: it suppresses the generated stub and the game still fails with a
/// MissingMethodException. Reports, as errors:
/// <list type="bullet">
/// <item><c>signature</c>: a member named like one of Unity's whose full signature (kind, static,
/// return/property/field/event type, parameter types and ref-ness, generic arity) matches none of
/// Unity's members of that name, or that lacks an accessor Unity's has;</item>
/// <item><c>type</c>: a public type Unity does not have (wrong namespace or nesting, or invented),
/// with Unity's full name when a type of that simple name exists elsewhere; a type of another kind
/// or base type than Unity's;</item>
/// <item><c>enum</c>: enum values missing or with other values than Unity's;</item>
/// <item><c>access</c>: a type or member less accessible than Unity's;</item>
/// <item><c>layout</c>: a struct whose public instance fields differ from Unity's in order or type.</item>
/// </list>
/// </summary>
public static class ApiChecker
{
    public static ApiCheckReport Check(string unityManagedEngineDir, string implAssembly)
    {
        var watch = Stopwatch.StartNew();
        var builder = new StubBuilder(unityManagedEngineDir, implAssembly, new HandWrittenSources(null));
        var report = builder.Check();
        report.Milliseconds = watch.ElapsedMilliseconds;
        return report;
    }
}

public sealed class ApiCheckReport
{
    /// <summary>Sorted by category, then type, then message.</summary>
    public List<ApiCheckError> Errors { get; set; } = new();
    /// <summary>Hand-written types compared with Unity's.</summary>
    public int CheckedTypes { get; set; }
    public long Milliseconds { get; set; }
}

public sealed class ApiCheckError
{
    /// <summary><c>signature</c>, <c>type</c>, <c>enum</c>, <c>access</c> or <c>layout</c>.</summary>
    public string Category { get; set; } = "";
    /// <summary>The hand-written type, by full name.</summary>
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}

sealed partial class StubBuilder
{
    static readonly SymbolDisplayFormat SignatureFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters
                       | SymbolDisplayMemberOptions.IncludeModifiers | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    List<ApiCheckError> _errors = new();

    public ApiCheckReport Check()
    {
        IndexOwners();
        var report = new ApiCheckReport();
        _errors = report.Errors;

        // Unity's types by simple name, to suggest where a misplaced type belongs.
        var bySimpleName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, type) in _owners.Values)
            foreach (var t in SelfAndNested(type))
                if (!IsFiltered(t))
                {
                    if (!bySimpleName.TryGetValue(t.MetadataName, out var list))
                        bySimpleName[t.MetadataName] = list = new();
                    list.Add(MetadataName(t));
                }

        foreach (var impl in SelfAndNested(TopLevelTypes(_implAssembly.GlobalNamespace)))
        {
            if (impl.IsImplicitlyDeclared || !SyntaxFacts.IsValidIdentifier(impl.Name))
                continue;
            var unity = FindUnity(impl);
            if (unity == null)
            {
                if (IsPubliclyVisible(impl) && !IsUnboundNamespace(impl))
                {
                    var suggestion = bySimpleName.TryGetValue(impl.MetadataName, out var names)
                        ? $"; Unity's is {string.Join(" or ", names)}"
                        : "";
                    Error("type", impl, $"public type {MetadataName(impl)} does not exist in Unity{suggestion}");
                }
                continue;
            }
            report.CheckedTypes++;
            CheckType(unity, impl);
        }

        report.Errors = report.Errors
            .OrderBy(e => e.Category, StringComparer.Ordinal)
            .ThenBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Message, StringComparer.Ordinal)
            .ToList();
        return report;
    }

    static bool IsUnboundNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace.ToDisplayString();
        return ns == "Unbound" || ns.StartsWith("Unbound.", StringComparison.Ordinal);
    }

    static IEnumerable<INamedTypeSymbol> SelfAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
            foreach (var t in SelfAndNested(nested))
                yield return t;
    }

    static IEnumerable<INamedTypeSymbol> SelfAndNested(IEnumerable<INamedTypeSymbol> types) =>
        types.SelectMany(SelfAndNested);

    /// <summary>Unity's type for a hand-written one: same namespace, nesting and name.</summary>
    INamedTypeSymbol? FindUnity(INamedTypeSymbol impl)
    {
        if (impl.ContainingType != null)
            return FindUnity(impl.ContainingType)?.GetTypeMembers(impl.Name, impl.Arity).FirstOrDefault(t => !IsFiltered(t));
        return _owners.TryGetValue(MetadataName(impl), out var owner) && !IsFiltered(owner.Type) ? owner.Type : null;
    }

    void Error(string category, INamedTypeSymbol type, string message) =>
        _errors.Add(new ApiCheckError { Category = category, Type = MetadataName(type), Message = message });

    void CheckType(INamedTypeSymbol unity, INamedTypeSymbol impl)
    {
        if (unity.TypeKind != impl.TypeKind)
        {
            Error("type", impl, $"is {KindOf(impl)}, Unity's is {KindOf(unity)}");
            return;
        }
        if (Rank(impl.DeclaredAccessibility) < Rank(unity.DeclaredAccessibility))
            Error("access", impl, $"is {Access(impl.DeclaredAccessibility)}, Unity's is {Access(unity.DeclaredAccessibility)}");
        if (unity.TypeKind == TypeKind.Class)
        {
            if (unity.IsStatic != impl.IsStatic)
                Error("type", impl, $"is {(impl.IsStatic ? "" : "not ")}static, Unity's is {(unity.IsStatic ? "" : "not ")}static");
            var unityBase = unity.BaseType is { SpecialType: not SpecialType.System_Object } b ? TypeKey(b) : "object";
            var implBase = impl.BaseType is { SpecialType: not SpecialType.System_Object } c ? TypeKey(c) : "object";
            if (unityBase != implBase)
                Error("type", impl, $"derives from {implBase}, Unity's from {unityBase}");
        }
        if (unity.TypeKind == TypeKind.Enum)
        {
            CheckEnumValues(unity, impl);
            return;
        }
        if (unity.TypeKind == TypeKind.Delegate)
        {
            var u = unity.DelegateInvokeMethod!;
            var i = impl.DelegateInvokeMethod!;
            if (FullKey(u) != FullKey(i))
                Error("signature", impl, $"delegate is {Show(i)}, Unity's is {Show(u)}");
            return;
        }
        if (unity.TypeKind == TypeKind.Struct)
            CheckLayout(unity, impl);
        CheckMembers(unity, impl);
    }

    void CheckEnumValues(INamedTypeSymbol unity, INamedTypeSymbol impl)
    {
        if (TypeKey(unity.EnumUnderlyingType!) != TypeKey(impl.EnumUnderlyingType!))
            Error("enum", impl, $"underlying type is {impl.EnumUnderlyingType}, Unity's is {unity.EnumUnderlyingType}");
        var unityValues = unity.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
            .ToDictionary(f => f.Name, f => f.ConstantValue, StringComparer.Ordinal);
        var implValues = impl.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
            .ToDictionary(f => f.Name, f => f.ConstantValue, StringComparer.Ordinal);
        foreach (var (name, value) in unityValues)
        {
            if (!implValues.TryGetValue(name, out var mine))
                Error("enum", impl, $"lacks {name} = {Literal(value)}");
            else if (!Equals(mine, value))
                Error("enum", impl, $"{name} is {Literal(mine)}, Unity's is {Literal(value)}");
        }
        foreach (var (name, value) in implValues)
            if (!unityValues.ContainsKey(name))
                Error("enum", impl, $"has {name} = {Literal(value)}, which Unity's does not");
    }

    /// <summary>Public instance fields in declaration order (C# lays out a sequential struct in
    /// that order; generated stub fields would only be appended).</summary>
    void CheckLayout(INamedTypeSymbol unity, INamedTypeSymbol impl)
    {
        static List<IFieldSymbol> PublicFields(INamedTypeSymbol t) => t.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && f.DeclaredAccessibility == Accessibility.Public && !f.IsImplicitlyDeclared)
            .ToList();
        var u = PublicFields(unity);
        var i = PublicFields(impl);
        string Describe(List<IFieldSymbol> fields) => string.Join(", ", fields.Select(f => $"{f.Type.ToDisplayString(SignatureFormat)} {f.Name}"));
        var same = u.Count == i.Count && u.Zip(i).All(p => p.First.Name == p.Second.Name && TypeKey(p.First.Type) == TypeKey(p.Second.Type));
        if (!same)
            Error("layout", impl, $"public fields are ({Describe(i)}), Unity's are ({Describe(u)})");
    }

    void CheckMembers(INamedTypeSymbol unity, INamedTypeSymbol impl)
    {
        // Unity's non-private members by name, with their full signatures.
        var unityMembers = new Dictionary<string, List<ISymbol>>(StringComparer.Ordinal);
        foreach (var member in unity.GetMembers())
        {
            if (member is INamedTypeSymbol || member.IsImplicitlyDeclared || IsAccessor(member) || IsExplicitImplementation(member))
                continue;
            if (member.DeclaredAccessibility is Accessibility.Private or Accessibility.NotApplicable)
                continue;
            var name = MemberName(member);
            if (!unityMembers.TryGetValue(name, out var list))
                unityMembers[name] = list = new();
            list.Add(member);
        }

        foreach (var member in impl.GetMembers())
        {
            if (member is INamedTypeSymbol || member.IsImplicitlyDeclared || IsAccessor(member) || IsExplicitImplementation(member))
                continue;
            if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor or MethodKind.Destructor })
                continue;
            if (!unityMembers.TryGetValue(MemberName(member), out var candidates))
                continue; // Unbound's own member: fine
            var key = FullKey(member);
            var match = candidates.FirstOrDefault(c => FullKey(c) == key);
            if (match == null)
            {
                // An internal or private overload of a method or constructor is Unbound's own helper,
                // unless it takes the place of Unity's (same parameters: the stub is suppressed).
                // Other members cannot share a name, so any of them blocks Unity's.
                var collides = candidates.Any(c => KeyOf(c) == KeyOf(member));
                if (member is IMethodSymbol && !collides
                    && member.DeclaredAccessibility is Accessibility.Internal or Accessibility.Private or Accessibility.ProtectedAndInternal)
                    continue;
                Error("signature", impl, $"{Show(member)} matches none of Unity's: {string.Join(" | ", candidates.Select(Show))}");
                continue;
            }
            if (Rank(member.DeclaredAccessibility) < Rank(match.DeclaredAccessibility))
                Error("access", impl, $"{Show(member)} is {Access(member.DeclaredAccessibility)}, Unity's is {Access(match.DeclaredAccessibility)}");
            if (member is IPropertySymbol property && match is IPropertySymbol unityProperty)
            {
                if (Visible(unityProperty.GetMethod) && property.GetMethod == null)
                    Error("signature", impl, $"{Show(member)} has no getter, Unity's has one");
                if (Visible(unityProperty.SetMethod) && property.SetMethod == null)
                    Error("signature", impl, $"{Show(member)} has no setter, Unity's has one");
                foreach (var (mine, theirs) in new[] { (property.GetMethod, unityProperty.GetMethod), (property.SetMethod, unityProperty.SetMethod) })
                    if (mine != null && Visible(theirs) && Rank(mine.DeclaredAccessibility) < Rank(theirs!.DeclaredAccessibility))
                        Error("access", impl, $"{Show(mine)} is {Access(mine.DeclaredAccessibility)}, Unity's is {Access(theirs.DeclaredAccessibility)}");
            }
        }
    }

    static string MemberName(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => ".ctor",
        IPropertySymbol { IsIndexer: true } => "this[]",
        _ => member.Name,
    };

    /// <summary>Everything game code binds to: kind, static-ness, name, generic arity, parameter
    /// types with their exact ref kind, and the return, property, field or event type.</summary>
    static string FullKey(ISymbol member)
    {
        static string Ref(RefKind kind) => kind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => "",
        };
        string Parameters(IEnumerable<IParameterSymbol> ps) => string.Join(",", ps.Select(p => Ref(p.RefKind) + TypeKey(p.Type)));
        var prefix = member.IsStatic ? "static " : "";
        return member switch
        {
            IFieldSymbol f => $"F:{prefix}{(f.IsConst ? "const " : "")}{f.Name}:{TypeKey(f.Type)}",
            IEventSymbol e => $"E:{prefix}{e.Name}:{TypeKey(e.Type)}",
            IPropertySymbol p => $"P:{prefix}{MemberName(p)}({Parameters(p.Parameters)}):{(p.ReturnsByRef ? "ref " : p.ReturnsByRefReadonly ? "ref readonly " : "")}{TypeKey(p.Type)}",
            IMethodSymbol m => $"M:{prefix}{MemberName(m)}`{m.Arity}({Parameters(m.Parameters)}):{(m.ReturnsByRef ? "ref " : m.ReturnsByRefReadonly ? "ref readonly " : "")}{TypeKey(m.ReturnType)}",
            _ => member.ToDisplayString(),
        };
    }

    static string Show(ISymbol member) => member.ToDisplayString(SignatureFormat);
}
