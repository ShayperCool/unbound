using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Reads the subset of YAML Unity writes (block maps, block sequences whose dashes sit at their
    /// key's indentation, flow maps such as <c>{x: 0, y: 1}</c>, <c>[]</c>, plain scalars) into the
    /// engine's <see cref="YNode"/> trees, so tests can feed real scene and prefab documents to the
    /// readers that the native side's parser normally feeds.
    /// </summary>
    internal static class TestYaml
    {
        /// <summary>The body of the first document: the map under its class name line.</summary>
        internal static YNode DocumentBody(string text)
        {
            var root = Parse(text);
            return root.Kind == YKind.Map && root.Values.Length > 0 ? root.Values[0] : root;
        }

        internal static YNode Parse(string text)
        {
            var lines = new List<(int Indent, string Text)>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("---") || raw.StartsWith("%")) continue;
                var trimmed = raw.TrimEnd();
                if (trimmed.Trim().Length == 0) continue;
                int indent = trimmed.Length - trimmed.TrimStart(' ').Length;
                lines.Add((indent, trimmed.Substring(indent)));
            }
            int i = 0;
            return lines.Count == 0 ? YNode.Null : Block(lines, ref i, lines[0].Indent);
        }

        static YNode Block(List<(int Indent, string Text)> lines, ref int i, int indent) =>
            lines[i].Text.StartsWith("- ") || lines[i].Text == "-" ? Sequence(lines, ref i, indent) : Map(lines, ref i, indent);

        static YNode Map(List<(int Indent, string Text)> lines, ref int i, int indent)
        {
            var keys = new List<string>();
            var values = new List<YNode>();
            while (i < lines.Count && lines[i].Indent == indent && !lines[i].Text.StartsWith("- "))
            {
                var text = lines[i].Text;
                int colon = text.IndexOf(':');
                if (colon < 0) throw new FormatException("expected a key: " + text);
                var key = text.Substring(0, colon);
                var rest = text.Substring(colon + 1).Trim();
                i++;
                YNode value;
                if (rest.Length > 0) value = Inline(rest);
                else if (i < lines.Count && (lines[i].Indent > indent || (lines[i].Indent == indent && lines[i].Text.StartsWith("- "))))
                    value = Block(lines, ref i, lines[i].Indent);
                else value = YNode.Null;
                keys.Add(key);
                values.Add(value);
            }
            return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
        }

        static YNode Sequence(List<(int Indent, string Text)> lines, ref int i, int indent)
        {
            var items = new List<YNode>();
            while (i < lines.Count && lines[i].Indent == indent && lines[i].Text.StartsWith("- "))
            {
                var content = lines[i].Text.Substring(2);
                bool isMap = !content.StartsWith("{") && (content.Contains(": ") || content.EndsWith(":"));
                if (isMap)
                {
                    // "- key: value" opens a map whose keys sit two columns right of the dash.
                    lines[i] = (indent + 2, content);
                    items.Add(Map(lines, ref i, indent + 2));
                }
                else
                {
                    items.Add(Inline(content));
                    i++;
                }
            }
            return new YNode { Kind = YKind.Seq, Items = items.ToArray() };
        }

        static YNode Inline(string text)
        {
            if (text == "[]") return new YNode { Kind = YKind.Seq, Items = Array.Empty<YNode>() };
            if (text == "{}") return new YNode { Kind = YKind.Map, Keys = Array.Empty<string>(), Values = Array.Empty<YNode>() };
            if (text.StartsWith("{") && text.EndsWith("}"))
            {
                var keys = new List<string>();
                var values = new List<YNode>();
                foreach (var part in text.Substring(1, text.Length - 2).Split(','))
                {
                    int colon = part.IndexOf(':');
                    keys.Add(part.Substring(0, colon).Trim());
                    values.Add(Inline(part.Substring(colon + 1).Trim()));
                }
                return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
            }
            if (text.Length >= 2 && (text[0] == '\'' || text[0] == '"') && text[text.Length - 1] == text[0])
                text = text.Substring(1, text.Length - 2);
            return YNode.MakeScalar(text);
        }
    }

    /// <summary>Resolves every reference to nothing (the fixtures' references point at other files).</summary>
    internal sealed class NullResolver : IReferenceResolver
    {
        internal readonly List<ObjectRef> Requests = new List<ObjectRef>();

        public Object Resolve(ObjectRef reference, Type expected)
        {
            Requests.Add(reference);
            return null;
        }
    }
}
