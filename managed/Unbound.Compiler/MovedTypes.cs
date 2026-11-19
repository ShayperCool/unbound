using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unbound.Compiler;

/// <summary>
/// The Editor's script updater for types that moved: when a package it keeps in its package cache
/// fails to compile for a name that does not exist (CS0234, CS0246, CS0103) and a referenced
/// assembly whose name starts with "Unity" declares a type <c>[MovedFrom]</c> that place, the
/// Editor rewrites the package's scripts to name the type where it is now and compiles again
/// (Unity 6000.0.71f1, tests/projects/compilesetup <c>call.converter</c>: com.unity.2d.pixel-perfect
/// 5.0.3 names URP's PixelPerfectCamera by its old namespace). Unbound rewrites the syntax trees
/// in memory: the files stay as they are, the compiled code is the same.
/// </summary>
static class MovedTypes
{
    const string MovedFromAttribute = "UnityEngine.Scripting.APIUpdating.MovedFromAttribute";

    static bool Updatable(Diagnostic d) =>
        d.Severity == DiagnosticSeverity.Error && d.Id is "CS0234" or "CS0246" or "CS0103" && d.Location.IsInSource;

    /// <summary>The compilation with the moved types named where they are now, or null when no
    /// error names a moved type.</summary>
    public static CSharpCompilation? Update(CSharpCompilation compilation, IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(Updatable).ToList();
        if (errors.Count == 0) return null;
        var moved = MovedFrom(compilation);
        if (moved.Count == 0) return null;
        var updated = compilation;
        foreach (var file in errors.GroupBy(e => e.Location.SourceTree!))
        {
            var tree = file.Key;
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var error in file)
            {
                // The error spans the missing name, or in an expression the whole qualified name.
                var name = root.FindNode(error.Location.SourceSpan, getInnermostNodeForTie: true) switch
                {
                    SimpleNameSyntax simple => simple,
                    MemberAccessExpressionSyntax access => access.Name,
                    QualifiedNameSyntax qualified => qualified.Right,
                    _ => null,
                };
                if (name == null) continue;
                var (target, places) = Places(name, model);
                foreach (var place in places)
                {
                    if (!moved.TryGetValue(place + "." + MetadataName(name), out var type)) continue;
                    replacements[target] = NameOf(type, name).WithTriviaFrom(target);
                    break;
                }
            }
            if (replacements.Count == 0) continue;
            var rewritten = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
            updated = updated.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(rewritten, tree.Options));
        }
        return ReferenceEquals(updated, compilation) ? null : updated;
    }

    /// <summary>The node naming the type and the namespaces it may have been named in: the one
    /// qualifying it (directly or through an alias), else those its usings and enclosing
    /// namespaces bring into scope.</summary>
    static (SyntaxNode Target, IEnumerable<string> Places) Places(SimpleNameSyntax name, SemanticModel model)
    {
        ExpressionSyntax? qualifier = name.Parent switch
        {
            QualifiedNameSyntax q when q.Right == name => q.Left,
            MemberAccessExpressionSyntax m when m.Name == name => m.Expression,
            _ => null,
        };
        if (qualifier != null)
        {
            // A namespace named through an alias, else by its name, which may be relative to the
            // enclosing namespaces.
            if (qualifier is IdentifierNameSyntax id
                && model.GetSpeculativeAliasInfo(qualifier.SpanStart, id, SpeculativeBindingOption.BindAsTypeOrNamespace) is { Target: INamespaceSymbol target })
                return (name.Parent!, new[] { target.ToDisplayString() });
            var written = qualifier.ToString().Replace("global::", "");
            var qualified = new List<string> { written };
            qualified.AddRange(name.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name + "." + written));
            return (name.Parent!, qualified);
        }
        var places = new List<string>();
        foreach (var node in name.Ancestors())
        {
            var usings = node switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default,
            };
            places.AddRange(usings.Where(u => u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None)).Select(u => u.Name!.ToString()));
            if (node is BaseNamespaceDeclarationSyntax declared) places.Add(declared.Name.ToString());
        }
        return (name, places);
    }

    static string MetadataName(SimpleNameSyntax name) =>
        name is GenericNameSyntax generic ? $"{name.Identifier.ValueText}`{generic.Arity}" : name.Identifier.ValueText;

    /// <summary>The type's fully qualified name, with the original type arguments.</summary>
    static NameSyntax NameOf(INamedTypeSymbol type, SimpleNameSyntax original)
    {
        var qualified = SyntaxFactory.ParseName("global::" + type.ContainingNamespace.ToDisplayString());
        SimpleNameSyntax last = original is GenericNameSyntax generic
            ? SyntaxFactory.GenericName(SyntaxFactory.Identifier(type.Name), generic.TypeArgumentList)
            : SyntaxFactory.IdentifierName(type.Name);
        return SyntaxFactory.QualifiedName(qualified, last);
    }

    /// <summary>Where each moved top-level type of the referenced "Unity*" assemblies was, as
    /// <c>Namespace.MetadataName</c>, to the type.</summary>
    static Dictionary<string, INamedTypeSymbol> MovedFrom(CSharpCompilation compilation)
    {
        var moved = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly
                || !assembly.Name.StartsWith("Unity", StringComparison.Ordinal)) continue;
            foreach (var type in Types(assembly.GlobalNamespace))
            {
                var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == MovedFromAttribute);
                if (attribute == null || !AutoUpdates(attribute, out var sourceNamespace, out var sourceClass)) continue;
                var place = sourceNamespace ?? type.ContainingNamespace.ToDisplayString();
                moved.TryAdd(place + "." + (sourceClass ?? type.MetadataName), type);
            }
        }
        return moved;
    }

    /// <summary>The attribute's arguments: <c>MovedFrom(bool autoUpdateAPI, string
    /// sourceNamespace = null, string sourceAssembly = null, string sourceClassName = null)</c>
    /// or the obsolete <c>MovedFrom(string sourceNamespace)</c>, which updates too.</summary>
    static bool AutoUpdates(AttributeData attribute, out string? sourceNamespace, out string? sourceClass)
    {
        sourceNamespace = sourceClass = null;
        var args = attribute.ConstructorArguments;
        if (args.Length == 1 && args[0].Value is string onlyNamespace)
        {
            sourceNamespace = onlyNamespace;
            return true;
        }
        if (args.Length == 0 || args[0].Value is not true) return false;
        sourceNamespace = args.Length > 1 ? args[1].Value as string : null;
        sourceClass = args.Length > 3 ? args[3].Value as string : null;
        // A nested type's old place is named with '/': not a top-level name.
        return sourceClass == null || !sourceClass.Contains('/');
    }

    static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
                foreach (var type in Types(child)) yield return type;
            else if (member is INamedTypeSymbol type)
                yield return type;
        }
    }
}
