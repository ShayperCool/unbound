using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unbound.Compiler;

public sealed class UnsupportedMessage
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class MessageCoverageReport
{
    public List<UnsupportedMessage> Messages { get; set; } = new();
    public List<string> FailedAssemblies { get; set; } = new();
}

public sealed partial class CompilerService
{
    /// <summary>Analyze declarations with the project's defines, references and script inheritance.
    /// Rules come from the runtime's message coverage table. No game code is loaded or executed.</summary>
    public MessageCoverageReport FindUnsupportedMessages(CompileRequest request, IEnumerable<(string Host, string Name)> rules)
    {
        lock (_lock)
        {
            var compiled = Compile(request);
            var report = new MessageCoverageReport
            {
                FailedAssemblies = compiled.Assemblies.Where(a => a.Dll == null).Select(a => a.Name).ToList(),
            };
            var byName = rules.ToLookup(r => r.Name, StringComparer.Ordinal);
            var seen = new HashSet<(string, string, string)>();
            foreach (var spec in request.Assemblies)
            {
                var (compilation, _) = CurrentCompilation(spec, request);
                if (compilation == null) continue;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                    {
                        if (!byName.Contains(method.Identifier.ValueText)) continue;
                        if (model.GetDeclaredSymbol(method) is not IMethodSymbol { IsStatic: false } declared) continue;
                        foreach (var rule in byName[declared.Name])
                        {
                            var owner = declared.ContainingType;
                            // Only script declarations below the engine host, matching runtime warnings.
                            var host = owner.BaseType;
                            while (host != null && host.ToDisplayString() != rule.Host) host = host.BaseType;
                            if (host == null) continue;
                            var name = owner.ToDisplayString();
                            if (!seen.Add((tree.FilePath, name, declared.Name))) continue;
                            report.Messages.Add(new UnsupportedMessage
                            {
                                File = tree.FilePath,
                                Line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                Type = name,
                                Message = declared.Name,
                            });
                        }
                    }
                }
            }
            report.Messages = report.Messages.OrderBy(m => m.File, StringComparer.Ordinal)
                .ThenBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.Message, StringComparer.Ordinal).ToList();
            return report;
        }
    }
}
