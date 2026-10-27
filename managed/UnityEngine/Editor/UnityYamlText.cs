using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unbound.Runtime
{
    /// <summary>
    /// A Unity YAML file as text, parsed so it can be written back as the Editor writes it: every
    /// scalar keeps its text, every map and sequence its style (block, or flow as in
    /// <c>{x: 0, y: -9.81, z: 0}</c> and <c>[]</c>), so an unchanged file writes out byte for byte
    /// and an edited one differs only where the values did (as the Editor's own save of a settings
    /// asset does). The subset the Editor writes: block maps two spaces in, block sequences at their
    /// key's indentation, single-line flow maps and empty flow sequences.
    /// </summary>
    internal sealed class YText
    {
        internal YKind Kind;
        internal bool Flow;
        /// <summary>A scalar's text as written (quotes included); empty for an empty scalar.</summary>
        internal string Raw = "";
        internal readonly List<string> Keys = new List<string>();
        internal readonly List<YText> Values = new List<YText>();
        internal readonly List<YText> Items = new List<YText>();

        internal static YText Scalar(string raw) => new YText { Kind = YKind.Scalar, Raw = raw ?? "" };
        internal static YText Map(bool flow = false) => new YText { Kind = YKind.Map, Flow = flow };
        internal static YText Seq(bool flow = false) => new YText { Kind = YKind.Seq, Flow = flow };

        internal YText Get(string key)
        {
            for (int i = 0; i < Keys.Count; i++)
                if (Keys[i] == key) return Values[i];
            return null;
        }

        internal void Set(string key, YText value)
        {
            for (int i = 0; i < Keys.Count; i++)
                if (Keys[i] == key) { Values[i] = value; return; }
            Keys.Add(key);
            Values.Add(value);
        }

        internal void Add(string key, YText value)
        {
            Keys.Add(key);
            Values.Add(value);
        }

        /// <summary>A scalar's value (quotes and escapes resolved).</summary>
        internal string Text => Kind == YKind.Scalar ? UnityYamlText.Unquote(Raw) : "";

        internal YText Clone()
        {
            var copy = new YText { Kind = Kind, Flow = Flow, Raw = Raw };
            copy.Keys.AddRange(Keys);
            foreach (var v in Values) copy.Values.Add(v.Clone());
            foreach (var i in Items) copy.Items.Add(i.Clone());
            return copy;
        }

        /// <summary>The managed reader's form of the node.</summary>
        internal YNode ToNode()
        {
            switch (Kind)
            {
                case YKind.Scalar: return YNode.MakeScalar(Text);
                case YKind.Seq:
                {
                    var items = new YNode[Items.Count];
                    for (int i = 0; i < items.Length; i++) items[i] = Items[i].ToNode();
                    return new YNode { Kind = YKind.Seq, Items = items };
                }
                case YKind.Map:
                {
                    var values = new YNode[Values.Count];
                    for (int i = 0; i < values.Length; i++) values[i] = Values[i].ToNode();
                    return new YNode { Kind = YKind.Map, Keys = Keys.ToArray(), Values = values };
                }
                default: return YNode.Null;
            }
        }
    }

    /// <summary>A document of a Unity YAML file: its header line (<c>--- !u!13 &amp;1</c>), its class
    /// key and its body.</summary>
    internal sealed class YTextDocument
    {
        internal string Header;
        internal int ClassId;
        internal long FileId;
        internal string ClassName;
        internal YText Body;
    }

    internal static class UnityYamlText
    {
        internal const string Preamble = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

        // ---------------------------------------------------------------- reading

        sealed class Line
        {
            internal int Indent;
            internal string Text;
        }

        internal static List<YTextDocument> Parse(string text)
        {
            var docs = new List<YTextDocument>();
            var lines = new List<Line>();
            YTextDocument current = null;
            void Flush()
            {
                if (current == null) return;
                int at = 0;
                if (lines.Count > 0)
                {
                    var first = lines[0].Text;
                    current.ClassName = first.EndsWith(":", StringComparison.Ordinal) ? first.Substring(0, first.Length - 1) : first;
                    at = 1;
                    current.Body = lines.Count > 1 ? ParseBlock(lines, ref at, lines[1].Indent) : YText.Map();
                }
                else current.Body = YText.Map();
                docs.Add(current);
                lines.Clear();
            }
            foreach (var raw in text.Split('\n'))
            {
                if (raw.StartsWith("%", StringComparison.Ordinal)) continue;
                if (raw.StartsWith("--- ", StringComparison.Ordinal))
                {
                    Flush();
                    current = new YTextDocument { Header = raw };
                    ParseHeader(raw, current);
                    continue;
                }
                if (raw.Length == 0 || current == null) continue;
                int indent = 0;
                while (indent < raw.Length && raw[indent] == ' ') indent++;
                lines.Add(new Line { Indent = indent, Text = raw.Substring(indent) });
            }
            Flush();
            return docs;
        }

        static void ParseHeader(string header, YTextDocument doc)
        {
            // --- !u!<class> &<file id>[ stripped]
            var parts = header.Split(' ');
            foreach (var p in parts)
            {
                if (p.StartsWith("!u!", StringComparison.Ordinal)) int.TryParse(p.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out doc.ClassId);
                else if (p.StartsWith("&", StringComparison.Ordinal)) long.TryParse(p.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out doc.FileId);
            }
        }

        /// <summary>A block map or sequence whose lines start at <paramref name="indent"/>.</summary>
        static YText ParseBlock(List<Line> lines, ref int at, int indent)
        {
            if (at < lines.Count && lines[at].Indent == indent && IsItem(lines[at].Text)) return ParseSeq(lines, ref at, indent);
            return ParseMap(lines, ref at, indent, null);
        }

        static bool IsItem(string text) => text == "-" || text.StartsWith("- ", StringComparison.Ordinal);

        /// <summary>A block map at <paramref name="indent"/>; <paramref name="first"/> is its first
        /// entry's text when it starts on a sequence item's line.</summary>
        static YText ParseMap(List<Line> lines, ref int at, int indent, string first)
        {
            var map = YText.Map();
            if (first != null) Entry(map, first, lines, ref at, indent);
            while (at < lines.Count && lines[at].Indent == indent && !IsItem(lines[at].Text))
            {
                var text = lines[at].Text;
                at++;
                Entry(map, text, lines, ref at, indent);
            }
            return map;
        }

        static void Entry(YText map, string text, List<Line> lines, ref int at, int indent)
        {
            int colon = KeyEnd(text);
            if (colon < 0)
            {
                // Not an entry: the previous scalar's continuation.
                if (map.Values.Count > 0 && map.Values[map.Values.Count - 1].Kind == YKind.Scalar)
                    map.Values[map.Values.Count - 1].Raw += "\n" + new string(' ', indent) + text;
                return;
            }
            var key = text.Substring(0, colon);
            if (colon + 1 == text.Length)
            {
                // Nested: a block map further in, or a block sequence at this indentation.
                if (at < lines.Count && lines[at].Indent > indent) map.Add(key, ParseBlock(lines, ref at, lines[at].Indent));
                else if (at < lines.Count && lines[at].Indent == indent && IsItem(lines[at].Text)) map.Add(key, ParseSeq(lines, ref at, indent));
                else map.Add(key, YText.Map());
                return;
            }
            var rest = text.Substring(colon + 2);
            var value = Value(rest);
            map.Add(key, value);
            Continuations(value, lines, ref at, indent);
        }

        /// <summary>Lines further in than <paramref name="indent"/> that are no entry: a multi-line
        /// scalar's.</summary>
        static void Continuations(YText value, List<Line> lines, ref int at, int indent)
        {
            if (value.Kind != YKind.Scalar) return;
            while (at < lines.Count && lines[at].Indent > indent && KeyEnd(lines[at].Text) < 0 && !IsItem(lines[at].Text))
            {
                value.Raw += "\n" + new string(' ', lines[at].Indent) + lines[at].Text;
                at++;
            }
        }

        static YText ParseSeq(List<Line> lines, ref int at, int indent)
        {
            var seq = YText.Seq();
            while (at < lines.Count && lines[at].Indent == indent && IsItem(lines[at].Text))
            {
                var text = lines[at].Text;
                at++;
                var rest = text.Length > 2 ? text.Substring(2) : "";
                if (text == "-")
                {
                    seq.Items.Add(at < lines.Count && lines[at].Indent > indent ? ParseBlock(lines, ref at, lines[at].Indent) : YText.Scalar(null));
                    seq.Items[seq.Items.Count - 1].Flow = seq.Items[seq.Items.Count - 1].Flow;
                    continue;
                }
                if (rest.Length > 0 && rest[0] != '{' && rest[0] != '[' && rest[0] != '\'' && rest[0] != '"' && KeyEnd(rest) >= 0)
                {
                    seq.Items.Add(ParseMap(lines, ref at, indent + 2, rest));
                    continue;
                }
                var value = Value(rest);
                seq.Items.Add(value);
                Continuations(value, lines, ref at, indent);
            }
            return seq;
        }

        /// <summary>Where a map entry's key ends (its colon, followed by a space or the line's end);
        /// -1 when the text is no entry.</summary>
        static int KeyEnd(string text)
        {
            if (text.Length == 0 || text[0] == '\'' || text[0] == '"' || text[0] == '{' || text[0] == '[') return -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ':') continue;
                if (i + 1 == text.Length || text[i + 1] == ' ') return i;
            }
            return -1;
        }

        static YText Value(string raw)
        {
            if (raw.StartsWith("{", StringComparison.Ordinal) && raw.EndsWith("}", StringComparison.Ordinal)) return ParseFlowMap(raw);
            if (raw == "[]") return YText.Seq(flow: true);
            return YText.Scalar(raw);
        }

        static YText ParseFlowMap(string raw)
        {
            var map = YText.Map(flow: true);
            var inner = raw.Substring(1, raw.Length - 2).Trim();
            if (inner.Length == 0) return map;
            int depth = 0, start = 0;
            bool single = false, dbl = false;
            var parts = new List<string>();
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (single) { if (c == '\'') single = false; continue; }
                if (dbl) { if (c == '\\') i++; else if (c == '"') dbl = false; continue; }
                if (c == '\'') single = true;
                else if (c == '"') dbl = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0) { parts.Add(inner.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(inner.Substring(start));
            foreach (var part in parts)
            {
                var p = part.Trim();
                int colon = KeyEnd(p);
                if (colon < 0) { map.Add(p, YText.Scalar("")); continue; }
                map.Add(p.Substring(0, colon), Value(colon + 2 <= p.Length ? p.Substring(colon + 2) : ""));
            }
            return map;
        }

        // ---------------------------------------------------------------- writing

        internal static string Write(List<YTextDocument> docs)
        {
            var sb = new StringBuilder(Preamble);
            foreach (var doc in docs)
            {
                sb.Append(doc.Header).Append('\n');
                sb.Append(doc.ClassName).Append(":\n");
                WriteMap(sb, doc.Body, 2);
            }
            return sb.ToString();
        }

        static void Pad(StringBuilder sb, int indent) => sb.Append(' ', indent);

        static void WriteMap(StringBuilder sb, YText map, int indent, bool firstOnItemLine = false)
        {
            for (int i = 0; i < map.Keys.Count; i++)
            {
                if (!(firstOnItemLine && i == 0)) Pad(sb, indent);
                WriteEntry(sb, map.Keys[i], map.Values[i], indent);
            }
        }

        static void WriteEntry(StringBuilder sb, string key, YText value, int indent)
        {
            sb.Append(key).Append(':');
            switch (value.Kind)
            {
                case YKind.Scalar:
                    sb.Append(' ').Append(value.Raw).Append('\n');
                    break;
                case YKind.Map when value.Flow:
                    sb.Append(' ').Append(FlowText(value)).Append('\n');
                    break;
                case YKind.Seq when value.Flow || value.Items.Count == 0:
                    sb.Append(" []\n");
                    break;
                case YKind.Map:
                    sb.Append('\n');
                    if (value.Keys.Count == 0) break;
                    WriteMap(sb, value, indent + 2);
                    break;
                case YKind.Seq:
                    sb.Append('\n');
                    WriteSeq(sb, value, indent);
                    break;
                default:
                    sb.Append(" \n");
                    break;
            }
        }

        static void WriteSeq(StringBuilder sb, YText seq, int indent)
        {
            foreach (var item in seq.Items)
            {
                Pad(sb, indent);
                switch (item.Kind)
                {
                    case YKind.Scalar:
                        sb.Append("- ").Append(item.Raw).Append('\n');
                        break;
                    case YKind.Map when item.Flow:
                        sb.Append("- ").Append(FlowText(item)).Append('\n');
                        break;
                    case YKind.Map when item.Keys.Count > 0:
                        sb.Append("- ");
                        WriteMap(sb, item, indent + 2, firstOnItemLine: true);
                        break;
                    case YKind.Seq when item.Items.Count > 0 && !item.Flow:
                        sb.Append("-\n");
                        WriteSeq(sb, item, indent + 2);
                        break;
                    default:
                        sb.Append("- ").Append(item.Kind == YKind.Seq ? "[]" : "{}").Append('\n');
                        break;
                }
            }
        }

        internal static string FlowText(YText map)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < map.Keys.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(map.Keys[i]).Append(": ");
                var v = map.Values[i];
                if (v.Kind == YKind.Map) sb.Append(FlowText(v));
                else if (v.Kind == YKind.Seq) sb.Append("[]");
                else sb.Append(v.Raw);
            }
            return sb.Append('}').ToString();
        }

        // ---------------------------------------------------------------- scalars

        /// <summary>A scalar's text as written: quotes and escapes resolved, folded lines joined.</summary>
        internal static string Unquote(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (raw[0] == '\'')
            {
                var body = Fold(raw.Length >= 2 && raw[raw.Length - 1] == '\'' ? raw.Substring(1, raw.Length - 2) : raw.Substring(1));
                return body.Replace("''", "'");
            }
            if (raw[0] == '"')
            {
                var body = Fold(raw.Length >= 2 && raw[raw.Length - 1] == '"' ? raw.Substring(1, raw.Length - 2) : raw.Substring(1));
                var sb = new StringBuilder();
                for (int i = 0; i < body.Length; i++)
                {
                    char c = body[i];
                    if (c != '\\' || i + 1 >= body.Length) { sb.Append(c); continue; }
                    char e = body[++i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '0': sb.Append('\0'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case ' ': sb.Append(' '); break;
                        case 'x' when i + 2 < body.Length:
                            sb.Append((char)int.Parse(body.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 2;
                            break;
                        case 'u' when i + 4 < body.Length:
                            sb.Append((char)int.Parse(body.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                return sb.ToString();
            }
            return Fold(raw);
        }

        /// <summary>Joins a multi-line scalar's lines as YAML folds them (one line break becomes a
        /// space, an empty line a line break).</summary>
        static string Fold(string s)
        {
            if (s.IndexOf('\n') < 0) return s;
            var lines = s.Split('\n');
            var sb = new StringBuilder(lines[0].TrimEnd());
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) sb.Append('\n');
                else sb.Append(sb.Length > 0 && sb[sb.Length - 1] != '\n' ? " " : "").Append(line);
            }
            return sb.ToString();
        }
    }
}
