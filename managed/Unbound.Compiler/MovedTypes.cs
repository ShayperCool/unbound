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
                if (root.FindNode(error.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax at) continue;
                if (Moved(Chain(at), model, moved) is { } found)
                    replacements[found.Node] = NameOf(found.Type, found.Name).WithTriviaFrom(found.Node);
            }
            if (replacements.Count == 0) continue;
            var rewritten = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
            updated = updated.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(rewritten, tree.Options));
        }
        return ReferenceEquals(updated, compilation) ? null : updated;
    }

    /// <summary>The dotted name the error lies in (<c>A.B.C</c>, as a type or in an expression), as
    /// its parts, each with the node that ends with it.</summary>
    static List<(SimpleNameSyntax Part, ExpressionSyntax Node)> Chain(ExpressionSyntax at)
    {
        var top = at;
        while (top.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax
               || top.Parent is MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression })
            top = (ExpressionSyntax)top.Parent;
        var parts = new List<(SimpleNameSyntax, ExpressionSyntax)>();
        bool Flatten(ExpressionSyntax e)
        {
            switch (e)
            {
                case QualifiedNameSyntax q:
                    if (!Flatten(q.Left)) return false;
                    parts.Add((q.Right, q));
                    return true;
                case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } m:
                    if (!Flatten(m.Expression)) return false;
                    parts.Add((m.Name, m));
                    return true;
                // `global::A`: A from the global namespace.
                case AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global" } g:
                    parts.Add((g.Name, g));
                    return true;
                case SimpleNameSyntax simple:
                    parts.Add((simple, simple));
                    return true;
                default:
                    return false;
            }
        }
        return Flatten(top) ? parts : new List<(SimpleNameSyntax, ExpressionSyntax)>();
    }

    /// <summary>The first part of <paramref name="chain"/> that names a moved type from where it
    /// was: the parts before it are the namespace it was in, named through an alias, else as
    /// written from the global namespace or relative to an enclosing one; a first part is the type
    /// itself in a namespace the usings or the enclosing namespaces bring into scope.</summary>
    static (ExpressionSyntax Node, SimpleNameSyntax Name, INamedTypeSymbol Type)? Moved(
        List<(SimpleNameSyntax Part, ExpressionSyntax Node)> chain, SemanticModel model, Dictionary<string, INamedTypeSymbol> moved)
    {
        if (chain.Count == 0) return null;
        var first = chain[0].Part;
        var enclosing = EnclosingNamespaces(first);
        for (int j = 0; j < chain.Count; j++)
        {
            var (part, node) = chain[j];
            IEnumerable<string> places;
            if (j == 0)
            {
                places = Usings(first).Concat(enclosing);
            }
            else
            {
                var written = string.Join(".", chain.Take(j).Select(c => c.Part.Identifier.ValueText));
                var rooted = chain[0].Node is AliasQualifiedNameSyntax;
                if (!rooted && first is IdentifierNameSyntax id
                    && model.GetSpeculativeAliasInfo(first.SpanStart, id, SpeculativeBindingOption.BindAsTypeOrNamespace) is { Target: INamespaceSymbol alias })
                    places = new[] { string.Join(".", new[] { alias.ToDisplayString() }.Concat(chain.Skip(1).Take(j - 1).Select(c => c.Part.Identifier.ValueText))) };
                else if (rooted)
                    places = new[] { written };
                else
                    places = enclosing.Select(n => n + "." + written).Append(written);
            }
            foreach (var place in places)
                if (moved.TryGetValue(place + "." + MetadataName(part), out var type))
                    return (node, part, type);
        }
        return null;
    }

    /// <summary>The namespaces enclosing <paramref name="node"/> and their parents, innermost
    /// first (<c>A.B.C</c>, <c>A.B</c>, <c>A</c>), across nested declarations.</summary>
    static List<string> EnclosingNamespaces(SyntaxNode node)
    {
        var parts = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .SelectMany(n => n.Name.ToString().Split('.')).ToList();
        var names = new List<string>();
        for (int k = parts.Count; k > 0; k--) names.Add(string.Join(".", parts.Take(k)));
        return names;
    }

    /// <summary>The namespaces the using directives in scope of <paramref name="node"/> import.</summary>
    static IEnumerable<string> Usings(SyntaxNode node) =>
        node.Ancestors()
            .SelectMany(n => n switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default,
            })
            .Where(u => u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(u => u.Name!.ToString().Replace("global::", ""));

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
    /// or <c>MovedFrom(string sourceNamespace)</c>, which the release declares as updating
    /// automatically (<c>autoUpdateAPI: true</c>).</summary>
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
