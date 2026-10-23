using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unbound.Runtime;

namespace Unbound.Tests
{
    /// <summary>
    /// A reader of the subset of YAML Unity writes (block maps, block sequences at the key's indent,
    /// flow maps and sequences, plain scalars), producing the documents the native side would hand
    /// to the managed loader. For test fixtures only: the engine's parser is on the Rust side.
    /// </summary>
    internal static class UnityYaml
    {
        internal static List<YDocument> Parse(string text)
        {
            var docs = new List<YDocument>();
            var lines = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                lines.Add(raw.TrimEnd());
            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                if (!line.StartsWith("--- !u!", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }
                var header = line.Substring(7).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var doc = new YDocument
                {
                    ClassId = int.Parse(header[0], CultureInfo.InvariantCulture),
                    FileId = long.Parse(header[1].TrimStart('&'), CultureInfo.InvariantCulture),
                    Stripped = header.Length > 2 && header[2] == "stripped",
                };
                i++;
                doc.TypeName = lines[i].TrimEnd(':');
                i++;
                var reader = new Reader(lines, i);
                doc.Body = reader.Block(2);
                i = reader.Position;
                docs.Add(doc);
            }
            return docs;
        }

        sealed class Reader
        {
            readonly List<string> m_Lines;
            internal int Position;

            internal Reader(List<string> lines, int position)
            {
                m_Lines = lines;
                Position = position;
            }

            static int Indent(string line)
            {
                int n = 0;
                while (n < line.Length && line[n] == ' ') n++;
                return n;
            }

            bool AtEnd => Position >= m_Lines.Count || m_Lines[Position].StartsWith("---", StringComparison.Ordinal);

            void SkipBlank()
            {
                while (Position < m_Lines.Count && m_Lines[Position].Trim().Length == 0) Position++;
            }

            internal YNode Block(int indent)
            {
                SkipBlank();
                if (AtEnd) return YNode.MakeMap();
                var line = m_Lines[Position];
                if (Indent(line) == indent && line.Substring(indent).StartsWith("- ", StringComparison.Ordinal))
                    return Sequence(indent);
                return Map(indent);
            }

            YNode Map(int indent)
            {
                var keys = new List<string>();
                var values = new List<YNode>();
                while (true)
                {
                    SkipBlank();
                    if (AtEnd) break;
                    var line = m_Lines[Position];
                    int lineIndent = Indent(line);
                    if (lineIndent != indent) break;
                    var content = line.Substring(indent);
                    if (content.StartsWith("- ", StringComparison.Ordinal) || content == "-") break;
                    int colon = KeyEnd(content);
                    if (colon < 0) break;
                    var key = content.Substring(0, colon);
                    var rest = content.Substring(colon + 1).Trim();
                    Position++;
                    YNode value;
                    if (rest.Length > 0)
                    {
                        value = Inline(rest);
                    }
                    else
                    {
                        SkipBlank();
                        if (!AtEnd && Indent(m_Lines[Position]) > indent) value = Block(Indent(m_Lines[Position]));
                        else if (!AtEnd && Indent(m_Lines[Position]) == indent && m_Lines[Position].Substring(indent).StartsWith("- ", StringComparison.Ordinal))
                            value = Sequence(indent);
                        else value = YNode.MakeScalar("");
                    }
                    keys.Add(key);
                    values.Add(value);
                }
                return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
            }

            static int KeyEnd(string content)
            {
                if (content.StartsWith("{", StringComparison.Ordinal) || content.StartsWith("[", StringComparison.Ordinal)) return -1;
                int i = content.IndexOf(": ", StringComparison.Ordinal);
                if (i >= 0) return i;
                return content.EndsWith(":", StringComparison.Ordinal) ? content.Length - 1 : -1;
            }

            YNode Sequence(int indent)
            {
                var items = new List<YNode>();
                while (true)
                {
                    SkipBlank();
                    if (AtEnd) break;
                    var line = m_Lines[Position];
                    if (Indent(line) != indent) break;
                    var content = line.Substring(indent);
                    if (!content.StartsWith("- ", StringComparison.Ordinal)) break;
                    var item = content.Substring(2);
                    if (KeyEnd(item) >= 0)
                    {
                        // `- key: value` starts a map whose other keys are indented under the first.
                        m_Lines[Position] = new string(' ', indent + 2) + item;
                        items.Add(Map(indent + 2));
                    }
                    else
                    {
                        Position++;
                        items.Add(Inline(item));
                    }
                }
                return new YNode { Kind = YKind.Seq, Items = items.ToArray() };
            }
        }

        static YNode Inline(string text)
        {
            if (!text.StartsWith("{", StringComparison.Ordinal) && !text.StartsWith("[", StringComparison.Ordinal))
            {
                // A block scalar runs to the end of the line (commas included).
                if (text.Length >= 2 && (text[0] == '\'' || text[0] == '"') && text[text.Length - 1] == text[0])
                    text = text.Substring(1, text.Length - 2);
                return YNode.MakeScalar(text);
            }
            int i = 0;
            return Flow(text, ref i);
        }

        static YNode Flow(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ') i++;
            if (i < s.Length && s[i] == '{')
            {
                i++;
                var keys = new List<string>();
                var values = new List<YNode>();
                while (true)
                {
                    while (i < s.Length && (s[i] == ' ' || s[i] == ',')) i++;
                    if (i >= s.Length || s[i] == '}') { i++; break; }
                    int colon = s.IndexOf(':', i);
                    keys.Add(s.Substring(i, colon - i).Trim());
                    i = colon + 1;
                    values.Add(Flow(s, ref i));
                }
                return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
            }
            if (i < s.Length && s[i] == '[')
            {
                i++;
                var items = new List<YNode>();
                while (true)
                {
                    while (i < s.Length && (s[i] == ' ' || s[i] == ',')) i++;
                    if (i >= s.Length || s[i] == ']') { i++; break; }
                    items.Add(Flow(s, ref i));
                }
                return new YNode { Kind = YKind.Seq, Items = items.ToArray() };
            }
            var sb = new StringBuilder();
            bool quoted = i < s.Length && (s[i] == '\'' || s[i] == '"');
            if (quoted)
            {
                char q = s[i++];
                while (i < s.Length && s[i] != q) sb.Append(s[i++]);
                i++;
                return YNode.MakeScalar(sb.ToString());
            }
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (depth == 0 && (c == ',' || c == '}' || c == ']')) break;
                sb.Append(c);
                i++;
            }
            return YNode.MakeScalar(sb.ToString().Trim());
        }
    }
}
