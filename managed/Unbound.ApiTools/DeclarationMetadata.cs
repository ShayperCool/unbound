using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Unbound.ApiTools;

/// <summary>One shared exclusion policy for declaration checking and declaration generation.
/// Everything else, including unknown attributes, participates in comparison. See
/// docs/API_DECLARATIONS.md for the scope of each exclusion.</summary>
public static class DeclarationMetadata
{
    public static bool IgnoreAttribute(string name) =>
        name.StartsWith("JetBrains.Annotations.", StringComparison.Ordinal)
        || name.StartsWith("UnityEngine.Bindings.", StringComparison.Ordinal)
        || name.StartsWith("UnityEngine.Scripting.APIUpdating.", StringComparison.Ordinal)
        || name.StartsWith("Unbound.", StringComparison.Ordinal)
        || name is "System.Runtime.CompilerServices.CompilerGeneratedAttribute"
            or "System.Runtime.CompilerServices.NullableAttribute"
            or "System.Runtime.CompilerServices.NullableContextAttribute"
            or "System.Runtime.CompilerServices.RefSafetyRulesAttribute"
            or "System.Runtime.CompilerServices.IsReadOnlyAttribute"
            or "System.Runtime.CompilerServices.IsByRefLikeAttribute"
            or "System.Runtime.CompilerServices.ExtensionAttribute"
            or "System.Runtime.CompilerServices.ParamCollectionAttribute"
            or "System.Runtime.CompilerServices.AsyncStateMachineAttribute"
            or "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
            or "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute"
            or "System.Diagnostics.DebuggerHiddenAttribute"
            or "System.Diagnostics.DebuggerStepThroughAttribute"
            or "System.Diagnostics.DebuggerBrowsableAttribute"
            or "System.Runtime.InteropServices.DllImportAttribute"
            or "UnityEngine.Scripting.RequiredByNativeCodeAttribute"
            or "UnityEngine.Scripting.UsedByNativeCodeAttribute"
            or "UnityEngine.Internal.ExcludeFromDocsAttribute"
            or "UnityEngine.Internal.DefaultValueAttribute"
            or "UnityEngine.Internal.TypeInferenceRuleAttribute";
}

sealed partial class StubBuilder
{
    void CheckDeclaration(ISymbol unity, ISymbol impl, INamedTypeSymbol owner)
    {
        var location = impl is INamedTypeSymbol ? "type" : FullKey(impl);
        void Difference(string category, string subject, string mine, string reference)
        {
            if (mine != reference)
                Error(category, owner, $"{location} {subject} is {mine}, Unity's is {reference}");
        }
        void Attributes(ISymbol u, ISymbol i, string subject) =>
            Difference("attributes", subject, AttributeList(i.GetAttributes()), AttributeList(u.GetAttributes()));
        void Modifiers(ISymbol u, ISymbol i, string subject) =>
            Difference("modifiers", subject, ModifierList(i), ModifierList(u));
        void Parameters(IEnumerable<IParameterSymbol> us, IEnumerable<IParameterSymbol> ins)
        {
            foreach (var (u, i) in us.Zip(ins))
            {
                var subject = $"parameter[{i.Ordinal}]";
                Attributes(u, i, subject + " attributes");
                Difference("parameters", subject, ParameterMetadata(i), ParameterMetadata(u));
            }
        }
        void Generics(IEnumerable<ITypeParameterSymbol> us, IEnumerable<ITypeParameterSymbol> ins)
        {
            foreach (var (u, i) in us.Zip(ins))
            {
                Difference("constraints", $"generic[{i.Ordinal}]", Constraints(i), Constraints(u));
                Attributes(u, i, $"generic[{i.Ordinal}] attributes");
            }
        }
        void Accessor(IMethodSymbol? u, IMethodSymbol? i, string subject)
        {
            Difference("accessors", subject, i == null ? "absent" : Access(i.DeclaredAccessibility),
                u == null ? "absent" : Access(u.DeclaredAccessibility));
            if (u != null && i != null) CheckDeclaration(u, i, owner);
        }

        Attributes(unity, impl, "attributes");
        Modifiers(unity, impl, "modifiers");
        switch (unity, impl)
        {
            case (INamedTypeSymbol u, INamedTypeSymbol i):
                Difference("interfaces", "interfaces", Interfaces(i), Interfaces(u));
                Difference("modifiers", "readonly/ref-like/serializable", $"{i.IsReadOnly}/{i.IsRefLikeType}/{i.IsSerializable}",
                    $"{u.IsReadOnly}/{u.IsRefLikeType}/{u.IsSerializable}");
                Generics(u.TypeParameters, i.TypeParameters);
                break;
            case (IMethodSymbol u, IMethodSymbol i):
                Difference("attributes", "return attributes", AttributeList(i.GetReturnTypeAttributes()), AttributeList(u.GetReturnTypeAttributes()));
                Difference("modifiers", "readonly", i.IsReadOnly.ToString(), u.IsReadOnly.ToString());
                Difference("modifiers", "extension/vararg", $"{i.IsExtensionMethod}/{i.IsVararg}", $"{u.IsExtensionMethod}/{u.IsVararg}");
                Parameters(u.Parameters, i.Parameters);
                Generics(u.TypeParameters, i.TypeParameters);
                break;
            case (IPropertySymbol u, IPropertySymbol i):
                Parameters(u.Parameters, i.Parameters);
                Accessor(u.GetMethod, i.GetMethod, "getter");
                Accessor(u.SetMethod, i.SetMethod, "setter");
                Difference("accessors", "init", (i.SetMethod?.IsInitOnly == true).ToString(), (u.SetMethod?.IsInitOnly == true).ToString());
                break;
            case (IEventSymbol u, IEventSymbol i):
                Accessor(u.AddMethod, i.AddMethod, "add");
                Accessor(u.RemoveMethod, i.RemoveMethod, "remove");
                Accessor(u.RaiseMethod, i.RaiseMethod, "raise");
                break;
            case (IFieldSymbol u, IFieldSymbol i):
                if (u.ContainingType.TypeKind != TypeKind.Enum) Difference("constants", "constant", i.HasConstantValue ? Constant(i.ConstantValue) : "absent", u.HasConstantValue ? Constant(u.ConstantValue) : "absent");
                Difference("modifiers", "readonly/volatile/fixed", $"{i.IsReadOnly}/{i.IsVolatile}/{i.FixedSize}", $"{u.IsReadOnly}/{u.IsVolatile}/{u.FixedSize}");
                break;
        }
    }

    static string Interfaces(INamedTypeSymbol type) =>
        string.Join(",", type.Interfaces.Select(TypeKey).Order(StringComparer.Ordinal));

    static string ModifierList(ISymbol value) =>
        $"static={value.IsStatic},abstract={value.IsAbstract},virtual={value.IsVirtual},override={value.IsOverride},sealed={value.IsSealed}";

    static string ParameterMetadata(IParameterSymbol p) =>
        $"{p.RefKind},params={p.IsParams},optional={p.IsOptional},default={(p.HasExplicitDefaultValue ? Constant(p.ExplicitDefaultValue) : "absent")}";

    static string Constraints(ITypeParameterSymbol p) =>
        $"{p.Variance},class={p.HasReferenceTypeConstraint},struct={p.HasValueTypeConstraint},new={p.HasConstructorConstraint},unmanaged={p.HasUnmanagedTypeConstraint},notnull={p.HasNotNullConstraint},ref-like={p.AllowsRefLikeType};"
        + string.Join(",", p.ConstraintTypes.Select(TypeKey).Order(StringComparer.Ordinal));

    static string AttributeList(IEnumerable<AttributeData> attributes) =>
        "[" + string.Join(";", attributes.Where(a => !DeclarationMetadata.IgnoreAttribute(a.AttributeClass?.ToDisplayString() ?? ""))
            .Select(a => (a.AttributeClass == null ? "<unresolved>" : TypeKey(a.AttributeClass))
                + ":" + (a.AttributeConstructor == null ? "<unresolved>" : FullKey(a.AttributeConstructor))
                + "(" + string.Join(",", a.ConstructorArguments.Select(AttributeValue)) + ")"
                + "{" + string.Join(",", a.NamedArguments.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Key + "=" + AttributeValue(n.Value))) + "}")
            .Order(StringComparer.Ordinal)) + "]";

    static string AttributeValue(TypedConstant value) =>
        (value.Type == null ? "?" : TypeKey(value.Type)) + ":" +
        (value.Kind == TypedConstantKind.Array
            ? value.IsNull ? "null-array" : "[" + string.Join(",", value.Values.Select(AttributeValue)) + "]"
            : value.Value is ITypeSymbol type ? TypeKey(type) : Constant(value.Value));

    static string Constant(object? value) => value switch
    {
        null => "null",
        string text => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, true),
        char c => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(c, true),
        float f => "float:" + f.ToString("R", CultureInfo.InvariantCulture),
        double d => "double:" + d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable number => value.GetType().Name + ":" + number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
