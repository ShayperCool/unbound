using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Unbound.Runtime
{
    /// <summary>The field declarations generated from the compiled readers. Helpers and all
    /// reference-dependent branches contribute; analyzing a project never invokes the readers.</summary>
    internal static class ReaderFieldSchema
    {
        sealed class Declaration
        {
            [System.Text.Json.Serialization.JsonPropertyName("reads")] public string[] Reads { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("returns")] public string[] Returns { get; set; }
        }
        static readonly ConcurrentDictionary<Assembly, Dictionary<string, Declaration>> s_Tables = new();
        static readonly ConcurrentDictionary<string, Regex> s_Patterns = new(StringComparer.Ordinal);

        static Dictionary<string, Declaration> Table(Assembly assembly) => s_Tables.GetOrAdd(assembly, a =>
        {
            foreach (var attribute in a.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (attribute.Key == "Unbound.ReaderFields")
                    return JsonSerializer.Deserialize<Dictionary<string, Declaration>>(attribute.Value);
            throw new NotSupportedException("the assembly has no generated reader field table");
        });

        static readonly ConcurrentDictionary<(Type, string, int, int), string[]> s_Fields = new();

        internal static string[] Fields(Type type, string method, int parameters, int nodeParameter = 0) =>
            s_Fields.GetOrAdd((type, method, parameters, nodeParameter), key => BuildFields(key.Item1, key.Item2, key.Item3, key.Item4));

        static string[] BuildFields(Type type, string method, int parameters, int nodeParameter)
        {
            string key = type.FullName.Replace('+', '.') + "." + method + "#" + parameters;
            if (!Table(type.Assembly).TryGetValue(key, out var fields))
                throw new NotSupportedException("no generated field declaration for " + key);
            string root = "@" + nodeParameter;
            var result = new List<string>();
            foreach (var field in fields.Reads)
            {
                if (field.StartsWith("!unknown", StringComparison.Ordinal)) result.Add(field);
                else if (field == root) result.Add("");
                else if (field.StartsWith(root + "\u001f", StringComparison.Ordinal)) result.Add(field.Substring(root.Length + 1));
            }
            return result.ToArray();
        }

        sealed class Branch
        {
            internal bool Read;
            internal Branch Array;
            internal readonly Dictionary<string, Branch> Keys = new(StringComparer.Ordinal);
            internal readonly List<(Regex Match, Branch Next)> Patterns = new();
        }

        static readonly ConcurrentDictionary<string[], Branch> s_Tries = new();

        internal static void Consume(YNode body, string[] declarations)
        {
            var root = s_Tries.GetOrAdd(declarations, patterns =>
            {
                var root = new Branch();
                foreach (var pattern in patterns)
                {
                    if (pattern.Contains("!unknown", StringComparison.Ordinal) || pattern.Contains('§'))
                        throw new NotSupportedException("reader field key could not be derived: " + pattern);
                    var branch = root;
                    if (pattern.Length != 0)
                        foreach (var step in pattern.Split('\u001f'))
                        {
                            if (step == "a") branch = branch.Array ??= new Branch();
                            else if (step.StartsWith("k", StringComparison.Ordinal))
                            {
                                string key = step.Substring(1);
                                if (key.Contains('*'))
                                {
                                    var matcher = s_Patterns.GetOrAdd(key, k => new Regex("\\A" + Regex.Escape(k).Replace("\\*", ".*") + "\\z", RegexOptions.CultureInvariant));
                                    var next = new Branch();
                                    branch.Patterns.Add((matcher, next));
                                    branch = next;
                                }
                                else
                                {
                                    if (!branch.Keys.TryGetValue(key, out var next)) branch.Keys[key] = next = new Branch();
                                    branch = next;
                                }
                            }
                        }
                    branch.Read = true;
                }
                return root;
            });
            Visit(body, root);
        }

        static void Visit(YNode node, Branch branch)
        {
            if (branch.Read) node.CoverageRead = true;
            if (node.Kind == YKind.Map)
            {
                for (int i = 0; i < node.Keys.Length; i++)
                {
                    if (branch.Keys.TryGetValue(node.Keys[i], out var next)) Visit(node.Values[i], next);
                    foreach (var rule in branch.Patterns)
                        if (rule.Match.IsMatch(node.Keys[i])) Visit(node.Values[i], rule.Next);
                }
            }
            else if (node.Kind == YKind.Seq && branch.Array != null)
                foreach (var item in node.Items) Visit(item, branch.Array);
        }
    }
}
