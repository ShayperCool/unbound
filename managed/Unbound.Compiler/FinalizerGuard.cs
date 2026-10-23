using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Unbound.Compiler;

/// <summary>
/// An exception escaping a finalizer ends a .NET process; Unity's Mono logs it and carries on, and
/// game code relies on that. Finalizer bodies get a try/catch that logs the exception as Unity
/// does. The edits add no line breaks, so line numbers (debugging, hot reload) stay as written.
/// </summary>
static class FinalizerGuard
{
    internal static SyntaxTree Parse(SourceText text, CSharpParseOptions options, string path, bool engineReferences)
    {
        var tree = CSharpSyntaxTree.ParseText(text, options, path);
        if (!text.ToString().Contains('~')) return tree;
        var finalizers = tree.GetRoot().DescendantNodes().OfType<DestructorDeclarationSyntax>().ToList();
        if (finalizers.Count == 0) return tree;
        var report = engineReferences
            ? "global::UnityEngine.Debug.LogException(__unboundFinalizerException);"
            : "global::System.Console.Error.WriteLine(__unboundFinalizerException);";
        var changes = new List<TextChange>();
        foreach (var finalizer in finalizers)
        {
            if (finalizer.Body is { } body)
            {
                changes.Add(new TextChange(new TextSpan(body.OpenBraceToken.Span.End, 0), "try{"));
                changes.Add(new TextChange(new TextSpan(body.CloseBraceToken.SpanStart, 0),
                    "}catch(global::System.Exception __unboundFinalizerException){" + report + "}"));
            }
            else if (finalizer.ExpressionBody is { } arrow)
            {
                // `~C() => expr;` becomes `~C() {try{ expr; }catch...}` on the same line.
                changes.Add(new TextChange(new TextSpan(arrow.ArrowToken.Span.Start, arrow.ArrowToken.Span.Length), "{try{"));
                changes.Add(new TextChange(new TextSpan(finalizer.SemicolonToken.Span.End, 0),
                    "}catch(global::System.Exception __unboundFinalizerException){" + report + "}}"));
            }
        }
        return CSharpSyntaxTree.ParseText(text.WithChanges(changes), options, path);
    }
}
