using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityEngine.UIElements.StyleSheets
{
    /// <summary>
    /// The USS importer's check of a declaration against its property's value syntax (matches the
    /// Unity 6000.0.71f1 Editor, tests/projects/ussvalidation): a warning naming the syntax and the
    /// value that does not fit it, the extra value after a complete one, or an unknown property with
    /// the closest known name; the declaration stays. The syntaxes are the ones the project's
    /// release ships, read from its UI Toolkit module (UnityEngine.UIElementsModule's
    /// StylePropertyCache): a fact of the installation. The value definitions follow CSS's: keywords,
    /// data types (&lt;number&gt;, &lt;integer&gt;, &lt;length&gt;, &lt;percentage&gt;, &lt;color&gt;,
    /// &lt;resource&gt;, &lt;url&gt;, &lt;time&gt;, &lt;angle&gt;, &lt;custom-ident&gt;, and the
    /// release's named definitions), groups, juxtaposition, &amp;&amp;, || and |, and the multipliers
    /// ?, *, +, # and {a,b}; a value that is a var() fits any part of a definition.
    /// </summary>
    internal static class UssValidation
    {
        internal struct Result
        {
            internal string Message;
            internal string Hint;
            internal bool Ok => Message == null;
        }

        static Dictionary<string, string> s_Syntaxes, s_Definitions;
        static readonly Dictionary<string, Node> s_Parsed = new Dictionary<string, Node>(StringComparer.Ordinal);
        static bool s_TableReported;

        /// <summary>Checks <paramref name="value"/> (the declaration's value as the importer writes
        /// it) against <paramref name="property"/>'s syntax; null when there is no table to check with.</summary>
        internal static Result? Validate(string property, string value)
        {
            if (property.StartsWith("--", StringComparison.Ordinal)) return new Result();
            if (!Unbound.Runtime.Compat.UssValuesValidated || !LoadTable()) return null;
            if (!s_Syntaxes.TryGetValue(property, out var syntax))
            {
                var message = "Unknown property '" + property + "'";
                var closest = Closest(property);
                if (!string.IsNullOrEmpty(closest)) message += " (did you mean '" + closest + "'?)";
                return new Result { Message = message };
            }
            var node = Parse(syntax);
            if (node == null) return new Result { Message = "Invalid '" + property + "' property syntax '" + syntax + "'" };
            if (string.IsNullOrEmpty(value)) return new Result { Message = "Expected (" + syntax + ") but found empty value" };
            var m = new Matcher(new TextParts(Split(value)));
            bool matched = m.StartsWide() ? m.Step() : m.Match(node);
            if (!matched)
            {
                string hint = null;
                if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    if (syntax.Contains("<length>") || syntax.Contains("<length-percentage>")) hint = "px or %";
                    else if (syntax.Contains("<time>")) hint = "s or ms";
                    if (hint != null) hint = "Property expects a unit. Did you forget to add " + hint + "?";
                }
                if (hint == null && syntax.StartsWith("<color>", StringComparison.Ordinal)) hint = "Unsupported color '" + value + "'.";
                return new Result { Message = "Expected (" + syntax + ") but found '" + value + "'", Hint = hint };
            }
            if (m.HasCurrent) return new Result { Message = "Expected end of value but found '" + m.Current + "'" };
            return new Result();
        }

        /// <summary>Whether values a style pass resolved (var() substituted) fit the property's
        /// syntax, read by their types (a float is a number or integer, and a zero length; a
        /// dimension a length, percentage, time or angle by its unit; a color or color name a
        /// color); true for a custom property and when there is no table to check with.</summary>
        internal static bool Fits(string property, IParts values)
        {
            if (property.StartsWith("--", StringComparison.Ordinal) || !LoadTable()) return true;
            if (!s_Syntaxes.TryGetValue(property, out var syntax)) return false;
            var node = Parse(syntax);
            if (node == null || values.Count == 0) return false;
            var m = new Matcher(values);
            // As the importer's check: a leading initial or env() fits (unmeasured for substitutions).
            return (m.StartsWide() ? m.Step() : m.Match(node)) && !m.HasCurrent;
        }

        /// <summary>A value's parts as the matcher reads them.</summary>
        internal interface IParts
        {
            int Count { get; }
            string Text(int index);
            bool IsVariable(int index);
            bool IsComma(int index);
            bool Keyword(int index, string keyword);
            bool Data(int index, string type);
        }

        sealed class TextParts : IParts
        {
            readonly List<string> m_Parts;
            internal TextParts(List<string> parts) => m_Parts = parts;
            public int Count => m_Parts.Count;
            public string Text(int index) => m_Parts[index];
            public bool IsVariable(int index) => m_Parts[index].StartsWith("var(", StringComparison.Ordinal);
            public bool IsComma(int index) => m_Parts[index] == ",";
            public bool Keyword(int index, string keyword) => string.Equals(m_Parts[index], keyword, StringComparison.OrdinalIgnoreCase);
            public bool Data(int index, string type) => MatchText(type, m_Parts[index]);
        }

        static bool MatchText(string type, string part) => type switch
        {
            "number" => s_Number.IsMatch(part),
            "integer" => s_Integer.IsMatch(part),
            "length" => s_Length.IsMatch(part) || s_Zero.IsMatch(part),
            "percentage" => s_Percent.IsMatch(part) || s_Zero.IsMatch(part),
            "color" => s_Hex.IsMatch(part) || s_Rgb.IsMatch(part) || s_Rgba.IsMatch(part) || Unbound.UIElements.StyleProperties.TryGetColor(part.ToLowerInvariant(), out _),
            "resource" => Wrapped(s_Resource, part),
            "url" => Wrapped(s_Url, part),
            "time" => s_Time.IsMatch(part),
            "angle" => s_Angle.IsMatch(part) || s_Zero.IsMatch(part),
            "custom-ident" => IsCustomIdent(part),
            _ => false,
        };

        internal static bool IsCustomIdent(string part) => s_Ident.Match(part) is { Success: true } m && m.Length == part.Length;

        static bool Wrapped(Regex function, string part)
        {
            var m = function.Match(part);
            return m.Success && !s_Var.IsMatch(m.Groups[1].Value.Trim());
        }

        /// <summary>The known property closest to <paramref name="name"/>: least edit distance, a
        /// tenth of it for a name containing it, the first such in the table's order.</summary>
        static string Closest(string name)
        {
            float best = float.MaxValue;
            string result = null;
            foreach (var key in s_Syntaxes.Keys)
            {
                float d = Levenshtein(name, key) * (key.Contains(name) ? 0.1f : 1f);
                if (d < best)
                {
                    best = d;
                    result = key;
                }
            }
            return result;
        }

        static int Levenshtein(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }

        /// <summary>The value's parts: words split at white space and commas (a comma is a part of
        /// its own), a function call one part with what it holds.</summary>
        static List<string> Split(string value)
        {
            var parts = new List<string>();
            var sb = new System.Text.StringBuilder();
            void Flush()
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    while (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1])) i++;
                    Flush();
                }
                else if (c == ',')
                {
                    while (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1])) i++;
                    Flush();
                    parts.Add(",");
                }
                else if (c == '(')
                {
                    while (i < value.Length && value[i] != ')') sb.Append(value[i++]);
                    if (i < value.Length) sb.Append(')');
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) parts.Add(sb.ToString());
            return parts;
        }

        // ------------------------------------------------------------------ the syntax table

        /// <summary>The release's property syntaxes Unbound cannot read (tests: none).</summary>
        internal static List<string> UnreadSyntaxes()
        {
            var unread = new List<string>();
            if (!LoadTable()) return unread;
            foreach (var pair in s_Syntaxes)
                if (Parse(pair.Value) == null) unread.Add(pair.Key);
            return unread;
        }

        /// <summary>Forgets the table read (tests that name an installation).</summary>
        internal static void Reset()
        {
            s_Syntaxes = s_Definitions = null;
            s_Parsed.Clear();
            s_TableReported = false;
        }

        static bool LoadTable()
        {
            if (s_Syntaxes != null) return true;
            var data = UnityEditor.UnityInstallInfo.DataPath;
            var path = string.IsNullOrEmpty(data) ? null : Path.Combine(data, "Managed", "UnityEngine", "UnityEngine.UIElementsModule.dll");
            try
            {
                if (path != null && File.Exists(path)) ReadTable(path);
            }
            catch (Exception e) when (e is IOException || e is BadImageFormatException || e is InvalidOperationException)
            {
                s_Syntaxes = s_Definitions = null;
            }
            if (s_Syntaxes == null || s_Definitions == null)
            {
                s_Syntaxes = s_Definitions = null;
                // Without an installation (unit tests) nothing is checked, quietly.
                if (!s_TableReported && !string.IsNullOrEmpty(data))
                {
                    s_TableReported = true;
                    Debug.LogWarning("Unbound: the release's USS property syntaxes could not be read from its UI Toolkit module ("
                        + (path ?? "no Unity installation") + "); USS values are not checked against them");
                }
                return false;
            }
            return true;
        }

        /// <summary>The two string dictionaries the module's StylePropertyCache initializes (property
        /// syntaxes and named definitions), from the string pairs its type initializer adds.</summary>
        static void ReadTable(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                if (md.GetString(type.Name) != "StylePropertyCache" || md.GetString(type.Namespace) != "UnityEngine.UIElements.StyleSheets") continue;
                foreach (var mh in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(mh);
                    if (md.GetString(method.Name) != ".cctor") continue;
                    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                    var strings = new List<string>();
                    while (il.RemainingBytes > 0)
                    {
                        var op = ReadOpCode(ref il);
                        if (op == ILOpCode.Ldstr)
                            strings.Add(md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF)));
                        else if (op == ILOpCode.Stsfld)
                        {
                            var field = md.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(il.ReadInt32() & 0xFFFFFF));
                            var table = new Dictionary<string, string>(StringComparer.Ordinal);
                            for (int i = 0; i + 1 < strings.Count; i += 2) table[strings[i]] = strings[i + 1];
                            strings.Clear();
                            var name = md.GetString(field.Name);
                            if (name == "s_PropertySyntaxCache") s_Syntaxes = table;
                            else if (name == "s_NonTerminalValues") s_Definitions = table;
                        }
                        else SkipOperand(ref il, op);
                    }
                    return;
                }
            }
        }

        static ILOpCode ReadOpCode(ref BlobReader il)
        {
            byte b = il.ReadByte();
            return b == 0xFE ? (ILOpCode)(0xFE00 | il.ReadByte()) : (ILOpCode)b;
        }

        static void SkipOperand(ref BlobReader il, ILOpCode op)
        {
            if (op == ILOpCode.Switch)
            {
                il.Offset += 4 * il.ReadInt32();
                return;
            }
            il.Offset += OperandSizes.TryGetValue((short)op, out var size) ? size : 0;
        }

        static Dictionary<short, int> s_OperandSizes;

        /// <summary>Each opcode's operand size, from the runtime's opcode table.</summary>
        static Dictionary<short, int> OperandSizes
        {
            get
            {
                if (s_OperandSizes != null) return s_OperandSizes;
                var sizes = new Dictionary<short, int>();
                foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (f.GetValue(null) is not System.Reflection.Emit.OpCode code) continue;
                    sizes[code.Value] = code.OperandType switch
                    {
                        System.Reflection.Emit.OperandType.InlineNone => 0,
                        System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                        System.Reflection.Emit.OperandType.InlineVar => 2,
                        System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                        _ => 4,
                    };
                }
                return s_OperandSizes = sizes;
            }
        }

        // ------------------------------------------------------------------ syntax

        enum Kind { Keyword, Data, Juxtaposition, AndAnd, OrOr, Or }

        sealed class Node
        {
            internal Kind Kind;
            internal string Text;
            internal Node[] Children;
            internal int Min = 1, Max = 1;
            internal bool CommaList, Multiplied;
        }

        static Node Parse(string syntax)
        {
            if (s_Parsed.TryGetValue(syntax, out var node)) return node;
            try
            {
                var tokens = Tokenize(syntax);
                int i = 0;
                node = ParseOr(tokens, ref i);
                if (i != tokens.Count) node = null;
            }
            catch (FormatException)
            {
                node = null;
            }
            return s_Parsed[syntax] = node;
        }

        static List<string> Tokenize(string s)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    if (tokens.Count > 0 && i < s.Length) tokens.Add(" ");
                    continue;
                }
                if (c == '|' && i + 1 < s.Length && s[i + 1] == '|') { tokens.Add("||"); i += 2; continue; }
                if (c == '&' && i + 1 < s.Length && s[i + 1] == '&') { tokens.Add("&&"); i += 2; continue; }
                if ("|[]?*+#".IndexOf(c) >= 0) { tokens.Add(c.ToString()); i++; continue; }
                if (c == '{' || c == '<')
                {
                    int end = s.IndexOf(c == '{' ? '}' : '>', i);
                    if (end < 0) throw new FormatException(s);
                    tokens.Add(s.Substring(i, end - i + 1));
                    i = end + 1;
                    continue;
                }
                int start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && "|&[]?*+#{<".IndexOf(s[i]) < 0) i++;
                tokens.Add(s.Substring(start, i - start));
            }
            // White space next to an operator is not juxtaposition.
            var cleaned = new List<string>();
            for (int k = 0; k < tokens.Count; k++)
            {
                if (tokens[k] == " " && (k + 1 >= tokens.Count || IsOperator(tokens[k + 1]) || tokens[k + 1] == "]"
                    || (cleaned.Count > 0 && (IsOperator(cleaned[cleaned.Count - 1]) || cleaned[cleaned.Count - 1] == "[")))) continue;
                if (tokens[k] == " " && IsMultiplier(tokens[k + 1])) continue;
                cleaned.Add(tokens[k]);
            }
            return cleaned;
        }

        static bool IsOperator(string t) => t == "|" || t == "||" || t == "&&";
        static bool IsMultiplier(string t) => t == "?" || t == "*" || t == "+" || t == "#" || t.StartsWith("{", StringComparison.Ordinal);

        // Precedence, loosest first: |, ||, &&, juxtaposition.
        static Node ParseOr(List<string> t, ref int i) => ParseLevel(t, ref i, "|", Kind.Or, ParseOrOr);
        static Node ParseOrOr(List<string> t, ref int i) => ParseLevel(t, ref i, "||", Kind.OrOr, ParseAndAnd);
        static Node ParseAndAnd(List<string> t, ref int i) => ParseLevel(t, ref i, "&&", Kind.AndAnd, ParseJuxtaposition);
        static Node ParseJuxtaposition(List<string> t, ref int i) => ParseLevel(t, ref i, " ", Kind.Juxtaposition, ParseTerm);

        delegate Node Parser(List<string> t, ref int i);

        static Node ParseLevel(List<string> t, ref int i, string op, Kind kind, Parser next)
        {
            var first = next(t, ref i);
            if (i >= t.Count || t[i] != op) return first;
            var children = new List<Node> { first };
            while (i < t.Count && t[i] == op)
            {
                i++;
                children.Add(next(t, ref i));
            }
            return new Node { Kind = kind, Children = children.ToArray() };
        }

        static Node ParseTerm(List<string> t, ref int i)
        {
            if (i >= t.Count) throw new FormatException("expected a term");
            Node node;
            var token = t[i++];
            if (token == "[")
            {
                node = ParseOr(t, ref i);
                if (i >= t.Count || t[i] != "]") throw new FormatException("expected ]");
                i++;
                // A group stands alone, so its multiplier applies to the whole of it.
                node = new Node { Kind = Kind.Juxtaposition, Children = new[] { node } };
            }
            else if (token.StartsWith("<'", StringComparison.Ordinal))
            {
                // Another property's syntax (flex-basis is <'width'>).
                var name = token.Substring(2, token.Length - 4);
                if (!s_Syntaxes.TryGetValue(name, out var referenced)) throw new FormatException(name);
                var inner = Parse(referenced) ?? throw new FormatException(name);
                node = new Node { Kind = Kind.Juxtaposition, Children = new[] { inner } };
            }
            else if (token.StartsWith("<", StringComparison.Ordinal))
            {
                var name = token.Substring(1, token.Length - 2);
                if (s_Definitions.TryGetValue(name, out var definition))
                {
                    var inner = Parse(definition) ?? throw new FormatException(name);
                    node = new Node { Kind = Kind.Juxtaposition, Children = new[] { inner } };
                }
                else
                {
                    if (Array.IndexOf(s_DataTypes, name) < 0) throw new FormatException(name);
                    node = new Node { Kind = Kind.Data, Text = name };
                }
            }
            else if (IsOperator(token) || token == "]" || IsMultiplier(token)) throw new FormatException(token);
            else node = new Node { Kind = Kind.Keyword, Text = token };
            if (i < t.Count && IsMultiplier(t[i]))
            {
                var m = t[i++];
                node.Multiplied = true;
                switch (m)
                {
                    case "?": node.Min = 0; node.Max = 1; break;
                    case "*": node.Min = 0; node.Max = 100; break;
                    case "+": node.Min = 1; node.Max = 100; break;
                    case "#": node.Min = 1; node.Max = 100; node.CommaList = true; break;
                    default:
                        var range = m.Substring(1, m.Length - 2).Split(',');
                        node.Min = int.Parse(range[0], CultureInfo.InvariantCulture);
                        node.Max = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : node.Min;
                        break;
                }
            }
            return node;
        }

        static readonly string[] s_DataTypes = { "number", "integer", "length", "percentage", "color", "resource", "url", "time", "angle", "custom-ident" };

        // ------------------------------------------------------------------ matching

        static readonly Regex s_Number = new Regex(@"^[+-]?\d+(?:\.\d+)?$", RegexOptions.Compiled);
        static readonly Regex s_Integer = new Regex(@"^[+-]?\d+$", RegexOptions.Compiled);
        static readonly Regex s_Zero = new Regex(@"^0(?:\.0+)?$", RegexOptions.Compiled);
        static readonly Regex s_Length = new Regex(@"^[+-]?\d+(?:\.\d+)?px$", RegexOptions.Compiled);
        static readonly Regex s_Percent = new Regex(@"^[+-]?\d+(?:\.\d+)?%$", RegexOptions.Compiled);
        static readonly Regex s_Hex = new Regex(@"^#[a-fA-F0-9]{3}(?:[a-fA-F0-9]{3})?$", RegexOptions.Compiled);
        static readonly Regex s_Rgb = new Regex(@"^rgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)$", RegexOptions.Compiled);
        static readonly Regex s_Rgba = new Regex(@"rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*[\d.]+\s*\)$", RegexOptions.Compiled);
        static readonly Regex s_Var = new Regex(@"^var\(.+\)$", RegexOptions.Compiled);
        static readonly Regex s_Resource = new Regex(@"^resource\((.+)\)$", RegexOptions.Compiled);
        static readonly Regex s_Url = new Regex(@"^url\((.+)\)$", RegexOptions.Compiled);
        static readonly Regex s_Time = new Regex(@"^[+-]?\.?\d+(?:\.\d+)?(?:s|ms)$", RegexOptions.Compiled);
        static readonly Regex s_Angle = new Regex(@"^[+-]?\d+(?:\.\d+)?(?:deg|grad|rad|turn)$", RegexOptions.Compiled);
        static readonly Regex s_Ident = new Regex(@"^-?[_a-z][_a-z0-9-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Matches parts against a definition: an alternative takes the one that reads the
        /// most parts; || and &amp;&amp; take their members in any order (each order starting from
        /// another member, the one reading most); a var() part fits any member, and a definition the
        /// parts run out in fits when var() parts were read.</summary>
        sealed class Matcher
        {
            readonly IParts m_Parts;
            int m_Index, m_Variables;

            internal Matcher(IParts parts) => m_Parts = parts;

            internal bool HasCurrent => m_Index < m_Parts.Count;
            internal string Current => HasCurrent ? m_Parts.Text(m_Index) : null;

            /// <summary>Whether the value starts with what fits every property: initial, env().</summary>
            internal bool StartsWide() => Current == "initial" || Current != null && Current.StartsWith("env(", StringComparison.Ordinal);

            internal bool Step()
            {
                if (m_Index < m_Parts.Count) m_Index++;
                return true;
            }

            internal bool Match(Node n) => n.Multiplied ? MatchRepeated(n) : MatchOnce(n);

            bool MatchOnce(Node n)
            {
                bool ok;
                if (n.Kind is Kind.Keyword or Kind.Data)
                {
                    if (HasCurrent && m_Parts.IsVariable(m_Index))
                    {
                        ok = true;
                        m_Variables++;
                    }
                    else ok = HasCurrent && (n.Kind == Kind.Keyword ? m_Parts.Keyword(m_Index, n.Text) : m_Parts.Data(m_Index, n.Text));
                    if (ok) Step();
                }
                else
                {
                    int index = m_Index, variables = m_Variables;
                    ok = n.Kind switch
                    {
                        Kind.Or => MatchOr(n),
                        Kind.OrOr => MatchMany(n) > 0,
                        Kind.AndAnd => MatchMany(n) == n.Children.Length,
                        _ => MatchSequence(n),
                    };
                    if (!ok)
                    {
                        m_Index = index;
                        m_Variables = variables;
                    }
                }
                if (!ok && !HasCurrent && m_Variables > 0) ok = true;
                return ok;
            }

            bool MatchRepeated(Node n)
            {
                int count = 0, tries = 0;
                bool ok = true;
                while (ok && HasCurrent && tries < n.Max)
                {
                    ok = MatchOnce(n);
                    if (ok)
                    {
                        count++;
                        if (n.CommaList)
                        {
                            if (!HasCurrent || !m_Parts.IsComma(m_Index)) break;
                            Step();
                        }
                    }
                    tries++;
                }
                ok = count >= n.Min && count <= n.Max;
                if (!ok && count <= n.Max && m_Variables > 0) ok = true;
                return ok;
            }

            bool MatchSequence(Node n)
            {
                foreach (var child in n.Children)
                    if (!Match(child)) return false;
                return true;
            }

            bool MatchOr(Node n)
            {
                int start = m_Index, startVariables = m_Variables, best = 0, bestIndex = start, bestVariables = startVariables;
                foreach (var child in n.Children)
                {
                    bool ok = Match(child);
                    int read = m_Index - start;
                    if (ok && read > best)
                    {
                        best = read;
                        bestIndex = m_Index;
                        bestVariables = m_Variables;
                    }
                    m_Index = start;
                    m_Variables = startVariables;
                }
                if (best == 0) return false;
                m_Index = bestIndex;
                m_Variables = bestVariables;
                return true;
            }

            int MatchMany(Node n)
            {
                int count = n.Children.Length, best = 0, start = m_Index, startVariables = m_Variables;
                int bestIndex = start, bestVariables = startVariables;
                var order = new int[count];
                for (int rotation = 0; best < count && rotation < count; rotation++)
                {
                    for (int k = 0; k < count; k++) order[k] = (rotation + k) % count;
                    int matched = MatchInOrder(n, order);
                    if (matched > best)
                    {
                        best = matched;
                        bestIndex = m_Index;
                        bestVariables = m_Variables;
                    }
                    m_Index = start;
                    m_Variables = startVariables;
                }
                if (best > 0)
                {
                    m_Index = bestIndex;
                    m_Variables = bestVariables;
                }
                return best;
            }

            int MatchInOrder(Node n, int[] order)
            {
                int count = n.Children.Length, used = 0, variables = 0, k = 0;
                var taken = new bool[count];
                while (k < count && used + variables < count)
                {
                    int member = order[k];
                    if (!taken[member] && Match(n.Children[member]))
                    {
                        if (variables == m_Variables)
                        {
                            taken[member] = true;
                            used++;
                        }
                        else variables = m_Variables;
                        k = 0;
                    }
                    else k++;
                }
                return used + variables;
            }
        }
    }
}
