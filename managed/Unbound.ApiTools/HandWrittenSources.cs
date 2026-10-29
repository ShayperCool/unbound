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

    /// <summary>A member whose body does nothing or answers a constant, without saying so: no
    /// <c>[EditorFaithful]</c> and no call to <c>Stubs</c>. Unity's API members of this kind
    /// suppress the generated stub's warning, so a game using them fails silently.</summary>
    public sealed class SilentMember
    {
        public string Type = "";
        public string Member = "";
        public string Location = "";
        public string What = "";
    }

    readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

    /// <summary>The hand-written members that silently do nothing (see <see cref="SilentMember"/>).</summary>
    public List<SilentMember> Silent { get; } = new();

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
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            var root = tree.GetRoot();
            var relative = Path.GetRelativePath(dir, file);
            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                FindSilent(type, relative);
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

    void FindSilent(TypeDeclarationSyntax type, string file)
    {
        if (type is InterfaceDeclarationSyntax || HasFaithfulMarker(type.AttributeLists))
            return;
        var typeName = MetadataName(type);
        void Add(MemberDeclarationSyntax member, string name, SyntaxNode at, string what)
        {
            if (Marked(member))
                return;
            var line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Silent.Add(new SilentMember { Type = typeName, Member = name, Location = $"{file}:{line}", What = what });
        }
        foreach (var member in type.Members)
        {
            if (member.Modifiers.Any(SyntaxKind.AbstractKeyword) || member.Modifiers.Any(SyntaxKind.ExternKeyword))
                continue;
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    if (method.Modifiers.Any(SyntaxKind.PartialKeyword) && method.Body == null && method.ExpressionBody == null)
                        break;
                    if (IsEmpty(method.Body))
                        Add(member, method.Identifier.ValueText, method, "has an empty body");
                    else if (ConstantAnswer(method.Body, method.ExpressionBody) is { } constant
                             && !(method.ReturnType is PredefinedTypeSyntax { Keyword.ValueText: "void" }))
                        Add(member, method.Identifier.ValueText, method, $"always returns {constant}");
                    break;
                case PropertyDeclarationSyntax property:
                {
                    var name = property.Identifier.ValueText;
                    if (property.ExpressionBody != null)
                    {
                        if (ConstantAnswer(null, property.ExpressionBody) is { } constant)
                            Add(member, name, property, $"always is {constant}");
                        break;
                    }
                    var accessors = property.AccessorList?.Accessors;
                    if (accessors == null || accessors.Value.All(a => a.Body == null && a.ExpressionBody == null))
                        break; // auto-property
                    var get = accessors.Value.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                    var set = accessors.Value.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));
                    if (set != null && IsEmpty(set.Body))
                        Add(member, name, set, "ignores what is set");
                    else if (get != null && ConstantAnswer(get.Body, get.ExpressionBody) is { } constant
                             && (set == null || IsEmpty(set.Body)))
                        Add(member, name, get, $"always is {constant}");
                    break;
                }
                case EventDeclarationSyntax @event:
                    if (@event.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.AddAccessorDeclaration) && IsEmpty(a.Body)) == true)
                        Add(member, @event.Identifier.ValueText, @event, "never raises (an empty add)");
                    break;
            }
        }
    }

    /// <summary>A type marked as a whole answers as the Editor does (a platform API the Linux Editor
    /// answers with defaults, measured).</summary>
    static bool HasFaithfulMarker(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is "EditorFaithful" or "Unbound.EditorFaithful" or "EditorFaithfulAttribute");

    static bool Marked(MemberDeclarationSyntax member) =>
        HasFaithfulMarker(member.AttributeLists)
        || member.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression.ToString() is var e
            && (e.Contains("Stubs.") || e.EndsWith("NotImplemented", StringComparison.Ordinal)))
        || member.DescendantNodes().Any(n => n is ThrowStatementSyntax or ThrowExpressionSyntax);

    static bool IsEmpty(BlockSyntax? body) => body != null && body.Statements.Count == 0;

    /// <summary>The constant a body answers whatever the arguments (<c>=> false</c>,
    /// <c>{ return null; }</c>, <c>=> Vector3.zero</c>), or null.</summary>
    static string? ConstantAnswer(BlockSyntax? body, ArrowExpressionClauseSyntax? arrow)
    {
        ExpressionSyntax? value = arrow?.Expression;
        if (value == null && body is { Statements.Count: 1 } && body.Statements[0] is ReturnStatementSyntax ret)
            value = ret.Expression;
        while (value is CastExpressionSyntax cast)
            value = cast.Expression;
        switch (value)
        {
            case LiteralExpressionSyntax literal:
                return literal.ToString();
            case DefaultExpressionSyntax:
                return value.ToString();
            case PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax } negative:
                return negative.ToString();
            case MemberAccessExpressionSyntax access when access.Name.Identifier.ValueText is "zero" or "one" or "identity" or "Empty" or "white" or "black" or "clear" or "None":
                return access.ToString();
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty" } } empty:
                return empty.ToString();
            default:
                return null;
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
