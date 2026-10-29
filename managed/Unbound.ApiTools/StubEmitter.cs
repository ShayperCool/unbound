using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unbound.ApiTools;

/// <summary>A signature names a type the implementation cannot have (from an assembly it does not
/// reference, or a type that is not generated): the member or type is skipped.</summary>
sealed class UnresolvableException(string message) : Exception(message);

sealed partial class StubBuilder
{
    const string Stub = "[global::Unbound.Stub]";
    const string NotImplemented = "global::Unbound.Runtime.Stubs.NotImplemented";

    /// <summary>Unity type parameters of hand-written generic types, renamed to the hand-written
    /// declaration's names (all parts of a partial type must use the same names).</summary>
    readonly Dictionary<ITypeParameterSymbol, string> _typeParameterNames = new(SymbolEqualityComparer.Default);
    readonly Dictionary<INamedTypeSymbol, bool> _generatable = new(SymbolEqualityComparer.Default);
    readonly Dictionary<string, bool> _classLibrary = new(StringComparer.Ordinal);
    readonly Dictionary<INamedTypeSymbol, INamedTypeSymbol?> _implTypes = new(SymbolEqualityComparer.Default);

    /// <summary>What generating into one type needs to know.</summary>
    sealed class TypeContext
    {
        public required INamedTypeSymbol Unity;
        /// <summary>The hand-written type, when the members go into a partial declaration of it.</summary>
        public INamedTypeSymbol? Impl;
        public readonly HashSet<string> ImplKeys = new(StringComparer.Ordinal);
        /// <summary>Interface members the hand-written type implements explicitly, and those
        /// implemented explicitly by this generation (see <see cref="ExplicitKey"/>).</summary>
        public readonly HashSet<string> ImplExplicit = new(StringComparer.Ordinal);
        public readonly HashSet<string> ExplicitEmitted = new(StringComparer.Ordinal);
        public bool ReportedStatic;
        public readonly Dictionary<string, List<ISymbol>> ImplNames = new(StringComparer.Ordinal);
        public required string Id;
        public required StubModuleReport Module;
        public bool AddedConstructor;
        public bool HandWritten => Impl != null;
        public bool IsInterface => Unity.TypeKind == TypeKind.Interface;
    }

    // ───────────────────────────── types ─────────────────────────────

    void EmitTopLevel(CodeWriter w, INamedTypeSymbol type, StubModuleReport module)
    {
        if (IsFiltered(type))
            return;
        var impl = FindImpl(type);
        if (impl == null && !Generatable(type, out var reason))
        {
            Report.Skipped.Add($"type {MetadataName(type)}: {reason}");
            return;
        }
        EmitType(w, type, impl, module);
    }

    /// <summary>Types that are never part of the API surface: compiler-generated, private, or the
    /// class library's own types that Unity embeds.</summary>
    static bool IsFiltered(INamedTypeSymbol type) =>
        type.IsImplicitlyDeclared
        || !SyntaxFacts.IsValidIdentifier(type.Name)
        || type.DeclaredAccessibility is Accessibility.Private or Accessibility.NotApplicable
        || (type.ContainingType == null && IsClassLibraryNamespace(type))
        || type.GetAttributes().Any(a => a.AttributeClass?.Name == "CompilerGeneratedAttribute");

    /// <summary>The hand-written implementation of a Unity type, if any.</summary>
    INamedTypeSymbol? FindImpl(INamedTypeSymbol unity)
    {
        unity = unity.OriginalDefinition;
        if (_implTypes.TryGetValue(unity, out var cached))
            return cached;
        INamedTypeSymbol? impl;
        if (unity.ContainingType != null)
            impl = FindImpl(unity.ContainingType)?.GetTypeMembers(unity.Name, unity.Arity).FirstOrDefault();
        else
            impl = _implAssembly.GetTypeByMetadataName(MetadataName(unity));
        _implTypes[unity] = impl;
        return impl;
    }

    /// <summary>The Unity type that represents a name: the owning module's definition for types
    /// several modules define.</summary>
    INamedTypeSymbol Canonical(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (type.ContainingType != null)
            return Canonical(type.ContainingType).GetTypeMembers(type.Name, type.Arity).FirstOrDefault() ?? type;
        return _owners.TryGetValue(MetadataName(type), out var owner) ? owner.Type : type;
    }

    /// <summary>Whether a Unity type exists once stubs are built: hand-written, or generatable.</summary>
    bool Available(INamedTypeSymbol type) => FindImpl(type) != null || Generatable(type, out _);

    /// <summary>Whether a Unity type can be generated: everything its declaration names (base type,
    /// constraints, delegate signature) is available.</summary>
    bool Generatable(INamedTypeSymbol type, out string? reason)
    {
        type = Canonical(type);
        reason = null;
        if (_generatable.TryGetValue(type, out var known))
        {
            if (!known)
                reason = "depends on a type that cannot be generated";
            return known;
        }
        _generatable[type] = true; // provisionally, for self-referencing declarations
        try
        {
            if (IsFiltered(type))
                throw new UnresolvableException("not part of the API surface");
            if (type.ContainingType != null && !Available(type.ContainingType))
                throw new UnresolvableException("its containing type cannot be generated");
            if (type.TypeKind == TypeKind.Class && type.BaseType is { } baseType && baseType.SpecialType != SpecialType.System_Object)
                Render(baseType);
            foreach (var parameter in type.TypeParameters)
                foreach (var constraint in parameter.ConstraintTypes)
                    Render(constraint);
            if (type.DelegateInvokeMethod is { } invoke)
            {
                Render(invoke.ReturnType);
                foreach (var p in invoke.Parameters)
                    Render(p.Type);
            }
            return true;
        }
        catch (UnresolvableException e)
        {
            _generatable[type] = false;
            reason = e.Message;
            return false;
        }
    }

    void EmitType(CodeWriter w, INamedTypeSymbol u, INamedTypeSymbol? impl, StubModuleReport module)
    {
        if (impl != null && KindOf(impl) != KindOf(u))
        {
            Report.Conflicts.Add($"{Display(u)}: Unity's is {KindOf(u)}, the hand-written one is {KindOf(impl)}; not extended");
            return;
        }
        if (impl != null && impl.DeclaredAccessibility < u.DeclaredAccessibility)
            Report.Conflicts.Add($"{Display(u)}: hand-written as {Access(impl.DeclaredAccessibility)}, Unity's is {Access(u.DeclaredAccessibility)}");
        switch (u.TypeKind)
        {
            case TypeKind.Enum:
                if (impl == null)
                    EmitEnum(w, u, module);
                else
                    CheckEnum(u, impl);
                return;
            case TypeKind.Delegate:
                if (impl == null)
                    EmitDelegate(w, u, module);
                return;
        }

        var source = impl != null ? _sources.Find(MetadataName(impl)) : null;
        if (source is { Partial: false })
        {
            Report.Conflicts.Add($"{Display(u)}: the hand-written type is not partial; its missing members are not generated");
            return;
        }
        var ctx = new TypeContext { Unity = u, Impl = impl, Id = Display(u), Module = module };
        if (impl != null)
        {
            foreach (var (unityParameter, implParameter) in u.TypeParameters.Zip(impl.TypeParameters))
                _typeParameterNames[unityParameter] = implParameter.Name;
            foreach (var member in impl.GetMembers())
            {
                if (KeyOf(member) is { } key)
                    ctx.ImplKeys.Add(key);
                foreach (var implemented in ExplicitImplementations(member))
                    ctx.ImplExplicit.Add(ExplicitKey(Associated(implemented)));
                var name = member is IMethodSymbol { MethodKind: MethodKind.Constructor } ? ".ctor" : member.Name;
                if (!ctx.ImplNames.TryGetValue(name, out var list))
                    ctx.ImplNames[name] = list = new();
                list.Add(member);
            }
        }

        var body = new CodeWriter();
        foreach (var member in u.GetMembers())
        {
            if (member is INamedTypeSymbol nested)
                EmitNested(body, ctx, nested);
            else
                EmitMember(body, ctx, member);
        }
        if (ctx.AddedConstructor && impl is { TypeKind: TypeKind.Class } && source is { DeclaresConstructor: false })
        {
            // A part declaring a constructor removes the implicit one the hand-written part relied on.
            body.Line($"{(impl.IsAbstract ? "protected" : "public")} {Escape(impl.Name)}() {{ }}");
        }
        if (!ctx.AddedConstructor && impl == null && u.TypeKind == TypeKind.Class && !u.IsStatic
            && BaseConstructorCall(ctx) is { Length: > 0 } call)
        {
            // Unity's constructors are all private: the implicit constructor C# would declare
            // instead cannot chain to a base class without a parameterless constructor.
            body.Line($"private {Escape(u.Name)}(){call} {{ }}");
        }

        string header;
        if (impl == null)
        {
            foreach (var attribute in TypeAttributes(u))
                w.Line(attribute);
            header = $"{Access(u.DeclaredAccessibility)} unsafe {TypeModifiers(u)}partial {Keyword(u)} {Escape(u.Name)}{TypeParameterList(u.TypeParameters, variance: true)}{BaseList(u)}{Constraints(u.TypeParameters)}";
            module.NewTypes++;
        }
        else
        {
            if (body.IsEmpty)
                return;
            header = $"unsafe {PartialModifiers(impl)}partial {Keyword(u)} {Escape(impl.Name)}{TypeParameterList(impl.TypeParameters, variance: false)}{AddedInterfaces(u, impl)}";
            CheckBase(u, impl);
            module.ExtendedTypes++;
        }
        w.Line(header);
        w.Line("{");
        w.Indent();
        w.Append(body);
        w.Outdent();
        w.Line("}");
    }

    void EmitNested(CodeWriter w, TypeContext ctx, INamedTypeSymbol nested)
    {
        if (IsFiltered(nested))
            return;
        var impl = ctx.Impl?.GetTypeMembers(nested.Name, nested.Arity).FirstOrDefault();
        if (impl == null)
        {
            if (ctx.Impl != null && ctx.ImplNames.ContainsKey(nested.Name))
            {
                Report.Conflicts.Add($"{Display(nested)}: the hand-written {ctx.Id} has a member of that name; type not generated");
                return;
            }
            if (!Generatable(nested, out var reason))
            {
                Report.Skipped.Add($"type {MetadataName(nested)}: {reason}");
                return;
            }
        }
        EmitType(w, nested, impl, ctx.Module);
    }

    static string KindOf(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => "a class",
        TypeKind.Struct => "a struct",
        TypeKind.Interface => "an interface",
        TypeKind.Enum => "an enum",
        TypeKind.Delegate => "a delegate",
        _ => type.TypeKind.ToString(),
    };

    static string Keyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Struct => "struct",
        TypeKind.Interface => "interface",
        _ => "class",
    };

    static string TypeModifiers(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Class)
            return type.IsStatic ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "";
        if (type.TypeKind == TypeKind.Struct)
            return (type.IsReadOnly ? "readonly " : "") + (type.IsRefLikeType ? "ref " : "");
        return "";
    }

    /// <summary>Modifiers a partial part repeats from the hand-written declaration.</summary>
    static string PartialModifiers(INamedTypeSymbol impl)
    {
        if (impl.TypeKind == TypeKind.Class)
            return impl.IsStatic ? "static " : "";
        if (impl.TypeKind == TypeKind.Struct)
            return (impl.IsReadOnly ? "readonly " : "") + (impl.IsRefLikeType ? "ref " : "");
        return "";
    }

    string BaseList(INamedTypeSymbol u)
    {
        var bases = new List<string>();
        if (u.TypeKind == TypeKind.Class && u.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
            bases.Add(Render(baseType));
        foreach (var face in u.Interfaces)
            if (TryRenderInterface(face) is { } rendered)
                bases.Add(rendered);
        return bases.Count == 0 ? "" : " : " + string.Join(", ", bases);
    }

    /// <summary>Unity's interfaces the hand-written type does not implement yet.</summary>
    string AddedInterfaces(INamedTypeSymbol u, INamedTypeSymbol impl)
    {
        var has = impl.AllInterfaces.Select(i => TypeKey(i)).ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var face in u.Interfaces)
            if (!has.Contains(TypeKey(face)) && TryRenderInterface(face) is { } rendered)
                added.Add(rendered);
        return added.Count == 0 ? "" : " : " + string.Join(", ", added);
    }

    string? TryRenderInterface(INamedTypeSymbol face)
    {
        if (face.DeclaredAccessibility == Accessibility.Private)
            return null;
        try
        {
            return Render(face);
        }
        catch (UnresolvableException)
        {
            return null;
        }
    }

    void CheckBase(INamedTypeSymbol u, INamedTypeSymbol impl)
    {
        if (u.TypeKind != TypeKind.Class)
            return;
        var unityBase = u.BaseType is { SpecialType: not SpecialType.System_Object } b ? TypeKey(b) : null;
        var implBase = impl.BaseType is { SpecialType: not SpecialType.System_Object } c ? TypeKey(c) : null;
        if (unityBase != implBase)
            Report.Conflicts.Add($"{Display(u)}: Unity's base type is {unityBase ?? "object"}, the hand-written one's is {implBase ?? "object"}");
    }

    void EmitEnum(CodeWriter w, INamedTypeSymbol u, StubModuleReport module)
    {
        foreach (var attribute in TypeAttributes(u))
            w.Line(attribute);
        var underlying = u.EnumUnderlyingType!.SpecialType == SpecialType.System_Int32 ? "" : " : " + Render(u.EnumUnderlyingType);
        w.Line($"{Access(u.DeclaredAccessibility)} enum {Escape(u.Name)}{underlying}");
        w.Line("{");
        w.Indent();
        foreach (var field in u.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.HasConstantValue || !SyntaxFacts.IsValidIdentifier(field.Name))
                continue;
            var obsolete = ObsoleteAttribute(field);
            w.Line($"{(obsolete != null ? obsolete + " " : "")}{Escape(field.Name)} = {Literal(field.ConstantValue)},");
        }
        w.Outdent();
        w.Line("}");
        module.NewTypes++;
    }

    void CheckEnum(INamedTypeSymbol u, INamedTypeSymbol impl)
    {
        var have = impl.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
            .ToDictionary(f => f.Name, f => f.ConstantValue, StringComparer.Ordinal);
        foreach (var field in u.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
        {
            if (!have.TryGetValue(field.Name, out var value))
                Report.Conflicts.Add($"{Display(u)}: the hand-written enum lacks {field.Name} = {Literal(field.ConstantValue)}");
            else if (!Equals(value, field.ConstantValue))
                Report.Conflicts.Add($"{Display(u)}.{field.Name}: hand-written as {Literal(value)}, Unity's is {Literal(field.ConstantValue)}");
        }
    }

    void EmitDelegate(CodeWriter w, INamedTypeSymbol u, StubModuleReport module)
    {
        var invoke = u.DelegateInvokeMethod!;
        foreach (var attribute in TypeAttributes(u))
            w.Line(attribute);
        w.Line($"{Access(u.DeclaredAccessibility)} unsafe delegate {ReturnType(invoke)} {Escape(u.Name)}{TypeParameterList(u.TypeParameters, variance: true)}({Parameters(invoke.Parameters, false)}){Constraints(u.TypeParameters)};");
        module.NewTypes++;
    }

    IEnumerable<string> TypeAttributes(INamedTypeSymbol u)
    {
        if (u.IsSerializable)
            yield return "[global::System.Serializable]";
        if (u.TypeKind == TypeKind.Struct && StructLayout(u) is { } layout)
            yield return layout;
        foreach (var attribute in u.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            switch (name)
            {
                case "System.ObsoleteAttribute":
                    yield return ObsoleteAttribute(u)!;
                    break;
                case "System.FlagsAttribute":
                    yield return "[global::System.Flags]";
                    break;
                case "System.AttributeUsageAttribute":
                case "UnityEngine.RequireComponent":
                case "UnityEngine.DisallowMultipleComponent":
                case "UnityEngine.DefaultExecutionOrder":
                case "UnityEngine.ExecuteAlways":
                case "UnityEngine.ExecuteInEditMode":
                    if (TryRenderAttribute(attribute) is { } rendered)
                        yield return rendered;
                    break;
            }
        }
        // New classes and structs are stubs as a whole; enums, delegates and interfaces are
        // complete declarations.
        if (u.TypeKind is TypeKind.Class or TypeKind.Struct)
            yield return Stub;
    }

    /// <summary><c>[Obsolete("message")]</c> without <c>error: true</c>, so that the implementation
    /// itself never fails to compile against its own obsolete members.</summary>
    static string? ObsoleteAttribute(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute");
        if (attribute == null)
            return null;
        return attribute.ConstructorArguments.FirstOrDefault() is { Value: string message }
            ? $"[global::System.Obsolete({SymbolDisplay.FormatLiteral(message, true)})]"
            : "[global::System.Obsolete]";
    }

    string? TryRenderAttribute(AttributeData attribute)
    {
        try
        {
            var args = attribute.ConstructorArguments.Select(RenderConstant)
                .Concat(attribute.NamedArguments.Select(n => $"{n.Key} = {RenderConstant(n.Value)}"))
                .ToList();
            // `@Name`: Unity has attributes both with and without the Attribute suffix
            // (DisallowMultipleComponent), which a plain `[Name]` finds ambiguous.
            var name = Render(attribute.AttributeClass!);
            var dot = name.LastIndexOf('.');
            if (name[dot + 1] != '@')
                name = name[..(dot + 1)] + "@" + name[(dot + 1)..];
            return $"[{name}{(args.Count > 0 ? "(" + string.Join(", ", args) + ")" : "")}]";
        }
        catch (UnresolvableException)
        {
            return null;
        }
    }

    string RenderConstant(TypedConstant constant) => constant.Kind switch
    {
        TypedConstantKind.Array => constant.IsNull
            ? "null"
            : $"new {Render(constant.Type!)} {{ {string.Join(", ", constant.Values.Select(RenderConstant))} }}",
        TypedConstantKind.Type => constant.Value is ITypeSymbol type ? $"typeof({Render(type)})" : "null",
        TypedConstantKind.Enum => $"({Render(constant.Type!)}){Parenthesize(Literal(constant.Value))}",
        _ => Literal(constant.Value),
    };

    /// <summary>Unity's struct layouts (sequential with a size or packing, or explicit), from the
    /// metadata Roslyn does not expose.</summary>
    string? StructLayout(INamedTypeSymbol u)
    {
        if (Reader(u) is not { } md)
            return null;
        var definition = md.GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.EntityHandle(u.MetadataToken));
        var layout = definition.GetLayout();
        var kind = definition.Attributes & System.Reflection.TypeAttributes.LayoutMask;
        var options = new List<string>();
        if (layout.PackingSize != 0)
            options.Add($"Pack = {layout.PackingSize}");
        if (layout.Size != 0)
            options.Add($"Size = {layout.Size}");
        string? name = kind switch
        {
            System.Reflection.TypeAttributes.ExplicitLayout => "Explicit",
            System.Reflection.TypeAttributes.AutoLayout => "Auto",
            _ => options.Count > 0 ? "Sequential" : null,
        };
        if (name == null)
            return null;
        return $"[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.{name}{string.Concat(options.Select(o => ", " + o))})]";
    }

    int FieldOffset(IFieldSymbol field)
    {
        if (Reader(field.ContainingType) is not { } md)
            return -1;
        return md.GetFieldDefinition((FieldDefinitionHandle)MetadataTokens.EntityHandle(field.MetadataToken)).GetOffset();
    }

    MetadataReader? Reader(INamedTypeSymbol type)
    {
        foreach (var (_, assembly, reader) in _modules)
            if (SymbolEqualityComparer.Default.Equals(assembly, type.ContainingAssembly))
                return reader;
        return null;
    }

    // ───────────────────────────── members ─────────────────────────────

    void EmitMember(CodeWriter w, TypeContext ctx, ISymbol member)
    {
        if (member.IsImplicitlyDeclared)
            return;
        if (IsExplicitImplementation(member))
        {
            EmitExplicit(w, ctx, member);
            return;
        }
        if (IsAccessor(member))
            return;
        const bool isExplicit = false;
        if (ctx.Impl is { IsStatic: true } && !member.IsStatic)
        {
            if (!ctx.ReportedStatic)
                Report.Conflicts.Add($"{ctx.Id}: hand-written as a static class, Unity's has instance members; those are not generated");
            ctx.ReportedStatic = true;
            return;
        }
        {
            if (member.DeclaredAccessibility is Accessibility.Private or Accessibility.NotApplicable)
                return;
            if (member is not (IMethodSymbol { MethodKind: MethodKind.Constructor } or IPropertySymbol { IsIndexer: true }) && !SyntaxFacts.IsValidIdentifier(member.Name))
                return; // compiler-generated
        }
        if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor or MethodKind.Destructor })
            return;
        if (member.GetAttributes().Any(a => a.AttributeClass?.Name == "CompilerGeneratedAttribute") && member is IFieldSymbol)
            return;
        if (member is IPropertySymbol or IEventSymbol && !isExplicit && !HasVisibleAccessor(member))
            return;

        var key = KeyOf(member);
        if (key == null)
            return;
        if (ctx.HandWritten)
        {
            if (ctx.ImplKeys.Contains(key))
            {
                CheckAccessibility(ctx, member);
                return;
            }
            if (!isExplicit && NameClash(ctx, member) is { } clash)
            {
                Report.Conflicts.Add($"{ctx.Id}.{member.Name}: the hand-written type has {clash} of that name; Unity's {Kind(member)} not generated");
                return;
            }
        }
        if (!isExplicit && member.Name == ctx.Unity.Name && member is not IMethodSymbol { MethodKind: MethodKind.Constructor })
            return; // a member named like its type is not expressible in C#

        var lines = new CodeWriter();
        try
        {
            switch (member)
            {
                case IFieldSymbol field:
                    EmitField(lines, ctx, field);
                    break;
                case IPropertySymbol property:
                    EmitProperty(lines, ctx, property, isExplicit);
                    break;
                case IEventSymbol @event:
                    EmitEvent(lines, ctx, @event, isExplicit);
                    break;
                case IMethodSymbol method:
                    EmitMethod(lines, ctx, method, isExplicit);
                    break;
                default:
                    return;
            }
        }
        catch (UnresolvableException e)
        {
            Report.Skipped.Add($"{Kind(member)} {ctx.Id}.{member.Name}: {e.Message}");
            return;
        }
        if (lines.IsEmpty)
            return;
        w.Append(lines);
        ctx.Module.Members++;
    }

    static IEnumerable<ISymbol> ExplicitImplementations(ISymbol member) => member switch
    {
        IMethodSymbol m => m.ExplicitInterfaceImplementations,
        IPropertySymbol p => p.ExplicitInterfaceImplementations,
        IEventSymbol e => e.ExplicitInterfaceImplementations,
        _ => Enumerable.Empty<ISymbol>(),
    };

    /// <summary>An interface accessor's property or event (explicit implementations are declared
    /// per property or event in C#, whatever the metadata pairs up).</summary>
    static ISymbol Associated(ISymbol member) =>
        member is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : member;

    /// <summary>The identity of an explicitly implemented interface member.</summary>
    static string ExplicitKey(ISymbol interfaceMember) =>
        TypeKey(interfaceMember.ContainingType) + "::" + KeyOf(interfaceMember);

    /// <summary>An explicit interface implementation, declared from the interface member it
    /// implements.</summary>
    void EmitExplicit(CodeWriter w, TypeContext ctx, ISymbol member)
    {
        var implemented = Associated(ExplicitImplementations(member).First());
        var key = ExplicitKey(implemented);
        if (ctx.Impl is { IsStatic: true } || ctx.ImplExplicit.Contains(key) || !ctx.ExplicitEmitted.Add(key))
            return;
        var lines = new CodeWriter();
        try
        {
            var face = Render(implemented.ContainingType);
            var id = $"{ctx.Id}.{implemented.Name}";
            switch (implemented)
            {
                case IMethodSymbol method:
                    lines.Line($"{Stub} {ReturnType(method)} {face}.{Escape(method.Name)}{TypeParameterList(method.TypeParameters, variance: false)}({Parameters(method.Parameters, false, defaults: false)}) {MethodBody(method, id)}");
                    break;
                case IPropertySymbol property:
                {
                    var type = (property.ReturnsByRef ? "ref " : property.ReturnsByRefReadonly ? "ref readonly " : "") + Render(property.Type);
                    var name = property.IsIndexer ? $"this[{Parameters(property.Parameters, false, defaults: false)}]" : Escape(property.Name);
                    var accessors = new List<string>();
                    if (property.GetMethod != null)
                    {
                        var ret = property.ReturnsByRef || property.ReturnsByRefReadonly
                            ? $"return ref global::Unbound.Runtime.Stubs.Ref<{Render(property.Type)}>();"
                            : ReturnDefault(property.Type);
                        accessors.Add($"{Stub} get {{ {NotImplemented}({Quote(id)}); {ret} }}");
                    }
                    if (property.SetMethod != null)
                        accessors.Add($"{Stub} {(property.SetMethod.IsInitOnly ? "init" : "set")} {{ {NotImplemented}({Quote(id)}); }}");
                    lines.Line($"{type} {face}.{name} {{ {string.Join(" ", accessors)} }}");
                    break;
                }
                case IEventSymbol @event:
                    lines.Line($"event {Render(@event.Type)} {face}.{Escape(@event.Name)} {{ {Stub} add {{ {NotImplemented}({Quote(id)}); }} {Stub} remove {{ }} }}");
                    break;
                default:
                    return;
            }
        }
        catch (UnresolvableException e)
        {
            Report.Skipped.Add($"{Kind(implemented)} {ctx.Id}.{implemented.ContainingType.Name}.{implemented.Name}: {e.Message}");
            return;
        }
        w.Append(lines);
        ctx.Module.Members++;
    }

    static bool IsAccessor(ISymbol member) => member is IMethodSymbol
    {
        MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd
            or MethodKind.EventRemove or MethodKind.EventRaise
    };

    static bool IsExplicitImplementation(ISymbol member) => member switch
    {
        IMethodSymbol m => m.ExplicitInterfaceImplementations.Length > 0,
        IPropertySymbol p => p.ExplicitInterfaceImplementations.Length > 0,
        IEventSymbol e => e.ExplicitInterfaceImplementations.Length > 0,
        _ => false,
    };

    static bool HasVisibleAccessor(ISymbol member) => member switch
    {
        IPropertySymbol p => Visible(p.GetMethod) || Visible(p.SetMethod),
        IEventSymbol e => Visible(e.AddMethod) || Visible(e.RemoveMethod),
        _ => true,
    };

    static bool Visible(IMethodSymbol? accessor) =>
        accessor != null && accessor.DeclaredAccessibility is not (Accessibility.Private or Accessibility.NotApplicable);

    static string Kind(ISymbol member) => member switch
    {
        IFieldSymbol => "field",
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IEventSymbol => "event",
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
        IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => "operator",
        _ => "method",
    };

    /// <summary>What a hand-written member of the same name is, when adding this member would
    /// clash with it (C# allows one kind of member per name, except method overloads).</summary>
    static string? NameClash(TypeContext ctx, ISymbol member)
    {
        if (member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion })
            return null;
        if (member is IPropertySymbol { IsIndexer: true })
            return null;
        if (!ctx.ImplNames.TryGetValue(member.Name, out var existing))
            return null;
        if (member is IMethodSymbol)
            return existing.FirstOrDefault(e => e is not IMethodSymbol) is { } other ? Kind(other) : null;
        return Kind(existing[0]);
    }

    void CheckAccessibility(TypeContext ctx, ISymbol member)
    {
        var implMember = ctx.Impl!.GetMembers(member.Name).FirstOrDefault(m => KeyOf(m) == KeyOf(member));
        if (implMember != null && Rank(implMember.DeclaredAccessibility) < Rank(member.DeclaredAccessibility) && !IsExplicitImplementation(member))
            Report.Conflicts.Add($"{ctx.Id}.{member.Name}: hand-written as {Access(implMember.DeclaredAccessibility)}, Unity's is {Access(member.DeclaredAccessibility)}");
    }

    static int Rank(Accessibility a) => a switch
    {
        Accessibility.Public => 5,
        Accessibility.ProtectedOrInternal => 4,
        Accessibility.Protected or Accessibility.Internal => 3,
        Accessibility.ProtectedAndInternal => 2,
        _ => 1,
    };

    void EmitField(CodeWriter w, TypeContext ctx, IFieldSymbol field)
    {
        var attributes = new List<string>();
        if (ObsoleteAttribute(field) is { } obsolete)
            attributes.Add(obsolete);
        if (!field.IsStatic && !field.IsConst && ctx.Unity.TypeKind == TypeKind.Struct && FieldOffset(field) is >= 0 and var offset)
            attributes.Add($"[global::System.Runtime.InteropServices.FieldOffset({offset})]");
        var access = ctx.IsInterface && field.DeclaredAccessibility == Accessibility.Public ? "" : Access(field.DeclaredAccessibility) + " ";
        if (field.IsConst)
        {
            w.Line($"{Prefix(attributes)}{access}const {Render(field.Type)} {Escape(field.Name)} = {ConstantFor(field.Type, field.ConstantValue)};");
            return;
        }
        attributes.Add(Stub);
        if (field.IsFixedSizeBuffer && field.Type is IPointerTypeSymbol pointer)
        {
            w.Line($"{Prefix(attributes)}{access}fixed {Render(pointer.PointedAtType)} {Escape(field.Name)}[{field.FixedSize}];");
            return;
        }
        var modifiers = (field.IsStatic ? "static " : "") + (field.IsReadOnly ? "readonly " : "") + (field.IsVolatile ? "volatile " : "");
        w.Line($"{Prefix(attributes)}{access}{modifiers}{Render(field.Type)} {Escape(field.Name)};");
    }

    static string Prefix(List<string> attributes) => attributes.Count == 0 ? "" : string.Join(" ", attributes) + " ";

    void EmitProperty(CodeWriter w, TypeContext ctx, IPropertySymbol property, bool isExplicit)
    {
        var getter = isExplicit ? property.GetMethod : Visible(property.GetMethod) ? property.GetMethod : null;
        var setter = isExplicit ? property.SetMethod : Visible(property.SetMethod) ? property.SetMethod : null;
        if (getter == null && setter == null)
            return;
        var (modifiers, access, hasBody) = MemberModifiers(ctx, property, isExplicit);
        // The property's accessibility is its most accessible emitted accessor's.
        var propertyAccess = isExplicit ? Accessibility.NotApplicable : access;
        var declaredAccess = property.DeclaredAccessibility;
        if (!isExplicit && !property.IsOverride)
        {
            propertyAccess = declaredAccess = new[] { getter, setter }.Where(a => a != null).Select(a => a!.DeclaredAccessibility).MaxBy(Rank);
        }
        var type = (property.ReturnsByRef ? "ref " : property.ReturnsByRefReadonly ? "ref readonly " : "") + Render(property.Type);
        string name;
        if (property.IsIndexer)
        {
            name = (isExplicit ? Render(property.ExplicitInterfaceImplementations[0].ContainingType) + "." : "")
                   + $"this[{Parameters(property.Parameters, false)}]";
        }
        else
        {
            name = isExplicit
                ? Render(property.ExplicitInterfaceImplementations[0].ContainingType) + "." + Escape(property.ExplicitInterfaceImplementations[0].Name)
                : Escape(property.Name);
        }
        var attributes = new List<string>();
        if (ObsoleteAttribute(property) is { } obsolete)
            attributes.Add(obsolete);
        if (property.IsIndexer && property.MetadataName != "Item" && !isExplicit && !property.IsOverride)
            attributes.Add($"[global::System.Runtime.CompilerServices.IndexerName({SymbolDisplay.FormatLiteral(property.MetadataName, true)})]");

        var accessors = new List<string>();
        var id = $"{ctx.Id}.{property.Name}";
        foreach (var (accessor, keyword) in new[] { (getter, "get"), (setter, setter?.IsInitOnly == true ? "init" : "set") })
        {
            if (accessor == null)
                continue;
            var accessorAccess = !isExplicit && !ctx.IsInterface && Rank(accessor.DeclaredAccessibility) < Rank(declaredAccess)
                ? Access(accessor.DeclaredAccessibility) + " "
                : "";
            if (!hasBody)
            {
                accessors.Add($"{accessorAccess}{keyword};");
                continue;
            }
            string body;
            if (keyword == "get")
            {
                var ret = property.ReturnsByRef || property.ReturnsByRefReadonly
                    ? $"return ref global::Unbound.Runtime.Stubs.Ref<{Render(property.Type)}>();"
                    : ReturnDefault(property.Type);
                body = $"{{ {NotImplemented}({Quote(id)}); {ret} }}";
            }
            else
            {
                body = $"{{ {NotImplemented}({Quote(id)}); }}";
            }
            accessors.Add($"{Stub} {accessorAccess}{keyword} {body}");
        }
        var accessText = isExplicit ? "" : Access(propertyAccess) + " ";
        if (ctx.IsInterface && propertyAccess == Accessibility.Public)
            accessText = "";
        w.Line($"{Prefix(attributes)}{accessText}{modifiers}{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    void EmitEvent(CodeWriter w, TypeContext ctx, IEventSymbol @event, bool isExplicit)
    {
        var (modifiers, access, hasBody) = MemberModifiers(ctx, @event, isExplicit);
        var name = isExplicit
            ? Render(@event.ExplicitInterfaceImplementations[0].ContainingType) + "." + Escape(@event.ExplicitInterfaceImplementations[0].Name)
            : Escape(@event.Name);
        var accessText = isExplicit || (ctx.IsInterface && access == Accessibility.Public) ? "" : Access(access) + " ";
        var obsolete = ObsoleteAttribute(@event) is { } o ? o + " " : "";
        if (!hasBody)
        {
            w.Line($"{obsolete}{accessText}{modifiers}event {Render(@event.Type)} {name};");
            return;
        }
        // Subscribing warns: the event is never raised.
        var id = $"{ctx.Id}.{@event.Name}";
        w.Line($"{obsolete}{accessText}{modifiers}event {Render(@event.Type)} {name} {{ {Stub} add {{ {NotImplemented}({Quote(id)}); }} {Stub} remove {{ }} }}");
    }

    void EmitMethod(CodeWriter w, TypeContext ctx, IMethodSymbol method, bool isExplicit)
    {
        var (modifiers, access, hasBody) = MemberModifiers(ctx, method, isExplicit);
        var attributes = new List<string>();
        if (ObsoleteAttribute(method) is { } obsolete)
            attributes.Add(obsolete);
        if (hasBody)
            attributes.Add(Stub);
        var accessText = isExplicit || (ctx.IsInterface && access == Accessibility.Public) ? "" : Access(access) + " ";
        var parameters = Parameters(method.Parameters, method.IsExtensionMethod);
        var id = $"{ctx.Id}.{(method.MethodKind == MethodKind.Constructor ? "ctor" : method.Name)}";
        var body = hasBody ? " " + MethodBody(method, id) : ";";

        string declaration;
        switch (method.MethodKind)
        {
            case MethodKind.Constructor:
                if (ctx.Unity.IsStatic)
                    return;
                var initializer = ctx.Unity.TypeKind == TypeKind.Class ? BaseConstructorCall(ctx) : "";
                declaration = $"{accessText}{Escape(ctx.Impl?.Name ?? ctx.Unity.Name)}({parameters}){initializer}";
                ctx.AddedConstructor = true;
                break;
            case MethodKind.UserDefinedOperator:
                var op = OperatorToken(method.MetadataName) ?? throw new UnresolvableException($"unknown operator {method.MetadataName}");
                declaration = $"{accessText}{modifiers}{ReturnType(method)} operator {op}({parameters})";
                break;
            case MethodKind.Conversion:
                var conversion = method.MetadataName == "op_Implicit" ? "implicit" : "explicit";
                declaration = $"{accessText}{modifiers}{conversion} operator {Render(method.ReturnType)}({parameters})";
                break;
            default:
                var name = isExplicit
                    ? Render(method.ExplicitInterfaceImplementations[0].ContainingType) + "." + Escape(method.ExplicitInterfaceImplementations[0].Name)
                    : Escape(method.Name);
                var constraints = isExplicit || method.IsOverride ? "" : Constraints(method.TypeParameters);
                var readOnly = method.IsReadOnly && !ctx.Unity.IsReadOnly && !method.IsStatic && ctx.Unity.TypeKind == TypeKind.Struct ? "readonly " : "";
                declaration = $"{accessText}{modifiers}{readOnly}{ReturnType(method)} {name}{TypeParameterList(method.TypeParameters, variance: false)}({parameters}){constraints}";
                break;
        }
        w.Line($"{Prefix(attributes)}{declaration}{body}");
    }

    string MethodBody(IMethodSymbol method, string id)
    {
        var statements = new List<string>();
        foreach (var p in method.Parameters)
            if (p.RefKind == RefKind.Out)
                statements.Add($"{ParameterName(p)} = default;");
        statements.Add($"{NotImplemented}({Quote(id)});");
        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            statements.Add($"return ref global::Unbound.Runtime.Stubs.Ref<{Render(method.ReturnType)}>();");
        else if (!method.ReturnsVoid && method.MethodKind != MethodKind.Constructor)
            statements.Add(ReturnDefault(method.ReturnType));
        return "{ " + string.Join(" ", statements) + " }";
    }

    /// <summary>What a stub returns: an empty collection where Unity returns one (callers iterate
    /// them without a null check), the type's default otherwise.</summary>
    string ReturnDefault(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array && array.Rank == 1 && array.ElementType is not IPointerTypeSymbol && array.ElementType is not IFunctionPointerTypeSymbol)
            return $"return global::System.Array.Empty<{Render(array.ElementType)}>();";
        if (type is INamedTypeSymbol named && named.IsGenericType && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
        {
            var arguments = string.Join(", ", named.TypeArguments.Select(Render));
            switch (named.Name)
            {
                case "List":
                case "HashSet":
                case "Dictionary":
                case "Queue":
                case "Stack":
                case "SortedDictionary":
                case "SortedSet":
                case "LinkedList":
                    return $"return new {Render(named)}();";
                case "IEnumerable":
                case "ICollection":
                case "IList":
                case "IReadOnlyList":
                case "IReadOnlyCollection":
                    return $"return global::System.Array.Empty<{arguments}>();";
                case "IDictionary":
                case "IReadOnlyDictionary":
                    return $"return new global::System.Collections.Generic.Dictionary<{arguments}>();";
            }
        }
        if (type is INamedTypeSymbol plain && !plain.IsGenericType && plain.ContainingNamespace?.ToDisplayString() == "System.Collections")
        {
            switch (plain.Name)
            {
                case "ArrayList":
                case "IList":
                case "ICollection":
                case "IEnumerable":
                    return "return new global::System.Collections.ArrayList();";
                case "Hashtable":
                case "IDictionary":
                    return "return new global::System.Collections.Hashtable();";
            }
        }
        return "return default;";
    }

    /// <summary>Modifiers (<c>static</c>, <c>abstract</c>, <c>virtual</c>, <c>override</c>, ...),
    /// the accessibility to declare, and whether the member gets a body.</summary>
    (string Modifiers, Accessibility Access, bool HasBody) MemberModifiers(TypeContext ctx, ISymbol member, bool isExplicit)
    {
        var access = member.DeclaredAccessibility;
        if (isExplicit)
            return ("", access, true);
        if (ctx.IsInterface)
        {
            if (member.IsStatic)
                return (member.IsAbstract ? "static abstract " : "static ", access, !member.IsAbstract);
            // Members added to a hand-written interface get a default implementation, so the
            // hand-written types implementing it still compile.
            if (member.IsAbstract)
                return ("", access, ctx.HandWritten);
            return ("", access, true);
        }
        var parts = new List<string>();
        if (member.IsStatic)
            parts.Add("static");
        if (member.IsAbstract)
        {
            // Abstract members added to a hand-written type would break its hand-written subclasses.
            parts.Add(ctx.HandWritten ? "virtual" : "abstract");
        }
        else if (member.IsOverride)
        {
            var (modifier, overrideAccess) = Override(ctx, member);
            parts.Add(modifier);
            access = overrideAccess;
        }
        else if (member.IsVirtual)
        {
            parts.Add("virtual");
        }
        var hasBody = !(member.IsAbstract && !ctx.HandWritten);
        return (parts.Count == 0 ? "" : string.Join(" ", parts) + " ", access, hasBody);
    }

    /// <summary>How to declare an override, given what the overridden member is once stubs are
    /// built: a hand-written non-virtual member cannot be overridden, so the member hides it.</summary>
    (string Modifier, Accessibility Access) Override(TypeContext ctx, ISymbol member)
    {
        var sealedPrefix = member.IsSealed ? "sealed " : "";
        ISymbol? overridden = member switch
        {
            IMethodSymbol m => m.OverriddenMethod,
            IPropertySymbol p => p.OverriddenProperty,
            IEventSymbol e => e.OverriddenEvent,
            _ => null,
        };
        if (overridden == null || !_unityModules.Contains(overridden.ContainingAssembly))
            return (sealedPrefix + "override", member.DeclaredAccessibility);
        var owner = FindImpl(overridden.ContainingType);
        if (owner == null)
            return (sealedPrefix + "override", member.DeclaredAccessibility);
        var key = KeyOf(overridden.OriginalDefinition);
        var implMember = owner.GetMembers(overridden.Name).FirstOrDefault(m => KeyOf(m) == key);
        if (implMember == null)
            return (sealedPrefix + "override", member.DeclaredAccessibility);
        if ((implMember.IsVirtual || implMember.IsAbstract || implMember.IsOverride) && !implMember.IsSealed)
            return (sealedPrefix + "override", OverrideAccess(member, implMember));
        Report.Conflicts.Add($"{ctx.Id}.{member.Name}: overrides {Display(owner)}.{member.Name}, which is hand-written as non-virtual; generated as `new virtual`");
        return (member.IsSealed ? "new" : "new virtual", member.DeclaredAccessibility);
    }

    /// <summary>An override repeats the overridden member's accessibility, except that a
    /// <c>protected internal</c> member of another assembly is overridden as <c>protected</c>.</summary>
    Accessibility OverrideAccess(ISymbol member, ISymbol overridden)
    {
        if (overridden.DeclaredAccessibility == Accessibility.ProtectedOrInternal
            && !SymbolEqualityComparer.Default.Equals(overridden.ContainingAssembly, _implAssembly)
            && !_unityModules.Contains(overridden.ContainingAssembly))
            return Accessibility.Protected;
        return overridden.DeclaredAccessibility;
    }

    /// <summary><c> : base(...)</c> for a generated constructor: the base class's parameterless
    /// constructor when it has one, else its shortest one called with defaults.</summary>
    string BaseConstructorCall(TypeContext ctx)
    {
        var unityBase = ctx.Unity.BaseType;
        if (unityBase == null || unityBase.SpecialType == SpecialType.System_Object)
            return "";
        var candidates = new List<IMethodSymbol>();
        candidates.AddRange(unityBase.InstanceConstructors.Where(c => c.DeclaredAccessibility != Accessibility.Private));
        INamedTypeSymbol? implBase = ctx.Impl?.BaseType ?? (unityBase.OriginalDefinition is var d && _unityModules.Contains(d.ContainingAssembly) ? FindImpl(d) : null);
        if (implBase != null && !implBase.IsGenericType)
            candidates.AddRange(implBase.InstanceConstructors.Where(c => c.DeclaredAccessibility != Accessibility.Private));
        if (candidates.Count == 0 || candidates.Any(c => c.Parameters.Length == 0))
            return "";
        foreach (var candidate in candidates.Where(c => c.Parameters.All(p => p.RefKind is RefKind.None or RefKind.In)).OrderBy(c => c.Parameters.Length))
        {
            try
            {
                return " : base(" + string.Join(", ", candidate.Parameters.Select(p => $"default({Render(p.Type)})")) + ")";
            }
            catch (UnresolvableException)
            {
            }
        }
        Report.Conflicts.Add($"{ctx.Id}: no base constructor of {Display(unityBase)} to chain to");
        return "";
    }

    static string? OperatorToken(string metadataName) => metadataName switch
    {
        "op_Addition" => "+",
        "op_Subtraction" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_BitwiseAnd" => "&",
        "op_BitwiseOr" => "|",
        "op_ExclusiveOr" => "^",
        "op_LeftShift" => "<<",
        "op_RightShift" => ">>",
        "op_UnsignedRightShift" => ">>>",
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        "op_UnaryNegation" => "-",
        "op_UnaryPlus" => "+",
        "op_LogicalNot" => "!",
        "op_OnesComplement" => "~",
        "op_Increment" => "++",
        "op_Decrement" => "--",
        "op_True" => "true",
        "op_False" => "false",
        _ => null,
    };

    // ───────────────────────────── signatures ─────────────────────────────

    string ReturnType(IMethodSymbol method) =>
        (method.ReturnsByRef ? "ref " : method.ReturnsByRefReadonly ? "ref readonly " : "") + Render(method.ReturnType);

    string Parameters(ImmutableArray<IParameterSymbol> parameters, bool isExtension, bool defaults = true)
    {
        // C# needs optional parameters last: defaults before a required parameter are dropped.
        var lastRequired = -1;
        for (int i = 0; i < parameters.Length; i++)
            if (!parameters[i].HasExplicitDefaultValue && !parameters[i].IsParams)
                lastRequired = i;
        var parts = new List<string>();
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var text = (i == 0 && isExtension ? "this " : "")
                       + (p.IsParams ? "params " : "")
                       + p.RefKind switch
                       {
                           RefKind.Ref => "ref ",
                           RefKind.Out => "out ",
                           RefKind.In => "in ",
                           RefKind.RefReadOnlyParameter => "ref readonly ",
                           _ => "",
                       }
                       + Render(p.Type) + " " + ParameterName(p);
            if (defaults && p.HasExplicitDefaultValue && i > lastRequired && p.RefKind is RefKind.None or RefKind.In)
                text += " = " + ConstantFor(p.Type, p.ExplicitDefaultValue);
            parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    static string ParameterName(IParameterSymbol p) =>
        SyntaxFacts.IsValidIdentifier(p.Name) && p.ContainingSymbol is IMethodSymbol m && m.Parameters.Count(q => q.Name == p.Name) == 1
            ? Escape(p.Name)
            : "p" + p.Ordinal;

    string TypeParameterList(ImmutableArray<ITypeParameterSymbol> parameters, bool variance)
    {
        if (parameters.Length == 0)
            return "";
        return "<" + string.Join(", ", parameters.Select(p => (variance ? p.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => "",
        } : "") + Escape(p.Name))) + ">";
    }

    string Constraints(ImmutableArray<ITypeParameterSymbol> parameters)
    {
        var clauses = new List<string>();
        foreach (var p in parameters)
        {
            var parts = new List<string>();
            if (p.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            else if (p.HasValueTypeConstraint)
                parts.Add("struct");
            else if (p.HasReferenceTypeConstraint)
                parts.Add("class");
            else if (p.HasNotNullConstraint)
                parts.Add("notnull");
            foreach (var type in p.ConstraintTypes)
                parts.Add(Render(type));
            if (p.HasConstructorConstraint && !p.HasValueTypeConstraint)
                parts.Add("new()");
            if (parts.Count > 0)
                clauses.Add($" where {Escape(p.Name)} : {string.Join(", ", parts)}");
        }
        return string.Concat(clauses);
    }

    string ConstantFor(ITypeSymbol type, object? value)
    {
        if (value == null)
            return type.IsReferenceType || type is IPointerTypeSymbol || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        var target = type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type;
        if (target.TypeKind == TypeKind.Enum)
            return $"({Render(target)}){Parenthesize(Literal(value))}";
        if (target.SpecialType == SpecialType.System_Object && value is not string)
            return Literal(value);
        return Literal(value);
    }

    static string Parenthesize(string literal) => literal.StartsWith('-') ? "(" + literal + ")" : literal;

    static string Literal(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => SymbolDisplay.FormatLiteral(s, true),
        char c => SymbolDisplay.FormatLiteral(c, true),
        float f when float.IsNaN(f) => "float.NaN",
        float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        double d when double.IsNaN(d) => "double.NaN",
        double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong u => u.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u => u.ToString(CultureInfo.InvariantCulture) + "U",
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new UnresolvableException($"unsupported constant {value.GetType()}"),
    };

    static string Quote(string text) => SymbolDisplay.FormatLiteral(text, true);

    static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    static string Access(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    /// <summary>A type as C# source: fully qualified (<c>global::</c>), class library types by
    /// keyword where C# has one. Throws when the implementation cannot have the type.</summary>
    string Render(ITypeSymbol type)
    {
        switch (type)
        {
            case IErrorTypeSymbol error:
                throw new UnresolvableException($"{error.ToDisplayString()} is from an assembly the modules do not ship");
            case IArrayTypeSymbol array:
            {
                var ranks = new List<int>();
                ITypeSymbol element = array;
                while (element is IArrayTypeSymbol a)
                {
                    ranks.Add(a.Rank);
                    element = a.ElementType;
                }
                return Render(element) + string.Concat(ranks.Select(r => "[" + new string(',', r - 1) + "]"));
            }
            case IPointerTypeSymbol pointer:
                return Render(pointer.PointedAtType) + "*";
            case IFunctionPointerTypeSymbol functionPointer:
                return FunctionPointer(functionPointer.Signature);
            case ITypeParameterSymbol parameter:
                return _typeParameterNames.TryGetValue(parameter.OriginalDefinition, out var renamed) ? Escape(renamed) : Escape(parameter.Name);
            case IDynamicTypeSymbol:
                return "dynamic";
            case INamedTypeSymbol named:
                return RenderNamed(named);
            default:
                throw new UnresolvableException($"unsupported type {type.ToDisplayString()}");
        }
    }

    string RenderNamed(INamedTypeSymbol named)
    {
        if (named.IsTupleType && named.TupleUnderlyingType is { } underlying)
            named = underlying;
        var keyword = named.SpecialType switch
        {
            SpecialType.System_Object => "object",
            SpecialType.System_String => "string",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Char => "char",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Void => "void",
            _ => null,
        };
        if (keyword != null)
            return keyword;
        CheckAvailable(named.OriginalDefinition);
        var prefix = named.ContainingType != null
            ? RenderNamed(named.ContainingType) + "."
            : "global::" + (named.ContainingNamespace.IsGlobalNamespace ? "" : named.ContainingNamespace.ToDisplayString() + ".");
        var arguments = named.TypeArguments.Length == 0 ? "" : "<" + string.Join(", ", named.TypeArguments.Select(Render)) + ">";
        return prefix + Escape(named.Name) + arguments;
    }

    string FunctionPointer(IMethodSymbol signature)
    {
        var convention = signature.CallingConvention switch
        {
            System.Reflection.Metadata.SignatureCallingConvention.Default => "",
            System.Reflection.Metadata.SignatureCallingConvention.CDecl => " unmanaged[Cdecl]",
            System.Reflection.Metadata.SignatureCallingConvention.StdCall => " unmanaged[Stdcall]",
            System.Reflection.Metadata.SignatureCallingConvention.ThisCall => " unmanaged[Thiscall]",
            System.Reflection.Metadata.SignatureCallingConvention.FastCall => " unmanaged[Fastcall]",
            _ => " unmanaged",
        };
        static string Ref(RefKind kind) => kind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => "",
        };
        var types = signature.Parameters.Select(p => Ref(p.RefKind) + Render(p.Type))
            .Append((signature.ReturnsByRef ? "ref " : signature.ReturnsByRefReadonly ? "ref readonly " : "") + Render(signature.ReturnType));
        return $"delegate*{convention}<{string.Join(", ", types)}>";
    }

    /// <summary>Throws unless the implementation will have this type: hand-written or generated
    /// for Unity's types, present in the implementation's framework for class library types.</summary>
    void CheckAvailable(INamedTypeSymbol definition)
    {
        var assembly = definition.ContainingAssembly;
        if (assembly == null || SymbolEqualityComparer.Default.Equals(assembly, _implAssembly))
            return;
        if (_unityModules.Contains(assembly))
        {
            if (!Available(definition))
                throw new UnresolvableException($"{MetadataName(definition)} cannot be generated");
            return;
        }
        if (!IsUnityCompilationAssembly(assembly))
            return; // a type of the implementation's own framework
        var name = MetadataName(definition);
        if (!_classLibrary.TryGetValue(name, out var exists))
        {
            exists = _impl.GetTypesByMetadataName(name).Any(t => IsPubliclyVisible(t));
            _classLibrary[name] = exists;
        }
        if (!exists)
            throw new UnresolvableException($"{name} is not in the implementation's framework");
    }

    readonly Dictionary<IAssemblySymbol, bool> _unityCompilationAssemblies = new(SymbolEqualityComparer.Default);

    bool IsUnityCompilationAssembly(IAssemblySymbol assembly)
    {
        if (!_unityCompilationAssemblies.TryGetValue(assembly, out var known))
        {
            known = _unity.SourceModule.ReferencedAssemblySymbols.Contains(assembly, SymbolEqualityComparer.Default)
                    || _unity.References.Any(r => SymbolEqualityComparer.Default.Equals(_unity.GetAssemblyOrModuleSymbol(r), assembly));
            _unityCompilationAssemblies[assembly] = known;
        }
        return known;
    }

    static bool IsPubliclyVisible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;
        return true;
    }

    // ───────────────────────────── identity ─────────────────────────────

    /// <summary>A member's identity for "does the implementation have it": kind, name, generic
    /// arity, parameter types and ref-ness (and the return type of conversions), with types by full
    /// name and type parameters by position, so Unity's and the implementation's views compare.</summary>
    static string? KeyOf(ISymbol member) => member switch
    {
        IFieldSymbol f => "F:" + f.Name,
        IEventSymbol e => "E:" + e.Name,
        IPropertySymbol p => "P:" + (p.IsIndexer ? "this[]" : p.Name) + "(" + string.Join(",", p.Parameters.Select(ParameterKey)) + ")",
        IMethodSymbol m => "M:" + (m.MethodKind == MethodKind.Constructor ? ".ctor" : m.Name) + "`" + m.Arity
                           + "(" + string.Join(",", m.Parameters.Select(ParameterKey)) + ")"
                           + (m.MethodKind == MethodKind.Conversion ? "=>" + TypeKey(m.ReturnType) : ""),
        _ => null,
    };

    static string ParameterKey(IParameterSymbol p) => (p.RefKind == RefKind.None ? "" : "&") + TypeKey(p.Type);

    static string TypeKey(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => TypeKey(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]",
        IPointerTypeSymbol p => TypeKey(p.PointedAtType) + "*",
        IFunctionPointerTypeSymbol f => "fnptr(" + string.Join(",", f.Signature.Parameters.Select(ParameterKey)) + ")" + TypeKey(f.Signature.ReturnType),
        ITypeParameterSymbol t => (t.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!") + t.Ordinal,
        INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } => TypeKey(underlying),
        INamedTypeSymbol n => (n.ContainingType != null ? TypeKey(n.ContainingType) + "+" : n.ContainingNamespace.IsGlobalNamespace ? "" : n.ContainingNamespace.ToDisplayString() + ".")
                              + n.MetadataName
                              + (n.TypeArguments.Length > 0 ? "<" + string.Join(",", n.TypeArguments.Select(TypeKey)) + ">" : ""),
        _ => type.ToDisplayString(),
    };
}
