using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unbound.ApiTools;

/// <summary>
/// What the compiled implementation does not tell about its hand-written types: whether every
/// declaration is <c>partial</c> (a stub part needs that) and whether the type declares instance
/// constructors itself (a stub part adding a constructor removes an implicit default one, so the
/// generator then declares it explicitly). Read from the sources' syntax alone.
/// </summary>
sealed class HandWrittenSources
{
    public sealed class TypeInfo
    {
        public bool Partial = true;
        public bool DeclaresConstructor;
    }

    readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

    /// <summary>Scans <paramref name="dir"/>'s C# files, except the generated stubs.</summary>
    public HandWrittenSources(string? dir)
    {
        if (dir == null || !Directory.Exists(dir))
            return;
        var generated = Path.Combine(Path.GetFullPath(dir), "Generated") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFullPath(file).StartsWith(generated, StringComparison.Ordinal))
                continue;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var name = MetadataName(type);
                if (!_types.TryGetValue(name, out var info))
                    _types[name] = info = new TypeInfo();
                if (!type.Modifiers.Any(SyntaxKind.PartialKeyword))
                    info.Partial = false;
                if (type.Members.OfType<ConstructorDeclarationSyntax>().Any(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword))
                    || type.ParameterList != null) // a primary constructor
                    info.DeclaresConstructor = true;
            }
        }
    }

    /// <summary>A hand-written type by metadata name (<c>Ns.Outer+Inner`1</c>), if its sources
    /// were scanned.</summary>
    public TypeInfo? Find(string metadataName) => _types.GetValueOrDefault(metadataName);

    static string MetadataName(BaseTypeDeclarationSyntax type)
    {
        var name = type.Identifier.ValueText
                   + (type is TypeDeclarationSyntax { TypeParameterList: { } list } ? "`" + list.Parameters.Count : "");
        if (type.Parent is BaseTypeDeclarationSyntax outer)
            return MetadataName(outer) + "+" + name;
        var namespaces = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()).ToList();
        return namespaces.Count == 0 ? name : string.Join(".", namespaces) + "." + name;
    }
}
