using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Unbound.ApiTools;

sealed partial class StubBuilder
{
    void CheckAttributeLookups()
    {
        if (_sources.DirectoryPath == null) return;
        var root = Path.GetDirectoryName(_sources.DirectoryPath)!;
        var trees = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "Generated" or "obj" or "bin" || part.EndsWith(".Tests", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        CheckAttributeLookups(CSharpCompilation.Create("attribute-lookups", trees, _impl.References,
            Options.WithAllowUnsafe(true)), root);
    }

    void CheckAttributeLookups(CSharpCompilation source, string root)
    {
        foreach (var tree in source.SyntaxTrees)
        {
            CheckConditionalLookups(tree, root);
            var model = source.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!LookupName(syntax)) continue;
                if (model.GetOperation(syntax) is not IInvocationOperation call) continue;
                var method = call.TargetMethod;
                if (method.Name is not ("IsDefined" or "GetCustomAttribute" or "GetCustomAttributes")) continue;
                var ns = method.ContainingNamespace.ToDisplayString();
                if (ns != "System.Reflection" && method.ContainingType.ToDisplayString() is not ("System.Attribute" or "System.Type")) continue;
                var inherit = call.Arguments.FirstOrDefault(a => a.Parameter?.Name == "inherit" && a.ArgumentKind == ArgumentKind.Explicit);
                if (inherit == null) continue;
                // FieldInfo does not inherit annotations, even when the field was obtained through
                // a derived ReflectedType. Both bool arguments give the same declaration's attributes.
                if (IsFieldValue(call.Instance) || call.Arguments.Any(a => IsFieldValue(a.Value))) continue;
                ITypeSymbol? attribute = method.TypeArguments.Length == 1 ? method.TypeArguments[0] : null;
                if (attribute == null)
                {
                    var argument = call.Arguments.FirstOrDefault(a => a.Parameter?.Name == "attributeType");
                    if (argument?.Value is ITypeOfOperation typeOf) attribute = typeOf.TypeOperand;
                }
                var label = attribute is INamedTypeSymbol named ? MetadataName(named) : attribute?.Name ?? "<dynamic attribute>";
                INamedTypeSymbol? reference = null;
                if (attribute is INamedTypeSymbol)
                {
                    if (DeclarationMetadata.IgnoreAttribute(label)) continue;
                    reference = _unity.GetTypeByMetadataName(label);
                    // Implementation-only attributes are outside Unity's declaration policy.
                    if (reference == null) continue;
                }
                var actual = inherit.Value.ConstantValue is { HasValue: true, Value: bool value } ? value : (bool?)null;
                (bool Inherited, int Targets)? usage = reference == null ? null : LookupUsage(reference);
                // A general inherited query delegates each annotation's AttributeUsage to the CLR.
                // Direct or computed discovery needs an explicit, measured consumer policy.
                if (reference == null && actual == true) continue;
                if (usage is { } known)
                {
                    // Structs/enums/interfaces cannot inherit a class annotation. Constructors,
                    // assembly/module attributes and fields likewise have no override chain.
                    const int InheritableTargets = (int)(AttributeTargets.Class | AttributeTargets.Method
                        | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue);
                    if ((known.Targets & InheritableTargets) == 0) continue;
                    if (actual == known.Inherited) continue;
                }
                var context = syntax.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is BaseMethodDeclarationSyntax or PropertyDeclarationSyntax);
                var owner = context == null ? "<initializer>" : context switch
                {
                    MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
                    ConstructorDeclarationSyntax declaration => declaration.Identifier.ValueText,
                    PropertyDeclarationSyntax declaration => declaration.Identifier.ValueText,
                    _ => context.Kind().ToString(),
                };
                var location = Path.GetRelativePath(root, tree.FilePath).Replace(Path.DirectorySeparatorChar, '/');
                var expression = syntax.NormalizeWhitespace().ToFullString().Replace("\r", "").Replace("\n", " ");
                _errors.Add(new ApiCheckError { Category = "lookup", Type = label,
                    Message = $"{location} {owner}: {expression}: inherit is {actual?.ToString() ?? "computed"}, reference AttributeUsage is {usage?.Inherited.ToString() ?? "not statically known"}" });
            }
        }
    }

    static bool LookupName(InvocationExpressionSyntax syntax) => (syntax.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => "",
    }) is "IsDefined" or "GetCustomAttribute" or "GetCustomAttributes";

    void CheckConditionalLookups(SyntaxTree tree, string root)
    {
        foreach (var trivia in tree.GetRoot().DescendantTrivia(descendIntoTrivia: true).Where(t => t.IsKind(SyntaxKind.DisabledTextTrivia)))
        {
            var disabled = CSharpSyntaxTree.ParseText(trivia.ToFullString()).GetRoot();
            foreach (var call in disabled.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(LookupName))
            {
                // An inactive branch has no semantic binding in this compilation. Keep its explicit
                // lookup visible until the branch has a measured policy, rather than silently losing it.
                if (call.ArgumentList.Arguments.Count == 0) continue;
                var location = Path.GetRelativePath(root, tree.FilePath).Replace(Path.DirectorySeparatorChar, '/');
                _errors.Add(new ApiCheckError { Category = "lookup", Type = "<conditional lookup>",
                    Message = $"{location}: inactive conditional lookup {call.NormalizeWhitespace()}: bind and validate the reflection overload and inherit argument for this configuration" });
            }
        }
    }

    static bool IsFieldValue(IOperation? value)
    {
        while (value is IConversionOperation conversion) value = conversion.Operand;
        var type = value?.Type;
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            if (current.ToDisplayString() == "System.Reflection.FieldInfo") return true;
        return false;
    }

    static (bool Inherited, int Targets) LookupUsage(INamedTypeSymbol attribute)
    {
        for (var current = attribute; current != null; current = current.BaseType)
        {
            var usage = current.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
            if (usage == null) continue;
            var inherited = usage.NamedArguments.FirstOrDefault(a => a.Key == "Inherited").Value.Value is not false;
            var targets = usage.ConstructorArguments.FirstOrDefault().Value is int value ? value : (int)AttributeTargets.All;
            return (inherited, targets);
        }
        return (true, (int)AttributeTargets.All);
    }
}
