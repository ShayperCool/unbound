using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Object = UnityEngine.Object;

namespace Unbound.UIElements
{
    /// <summary>
    /// Imports USS/TSS source text into a <see cref="StyleSheet"/> laid out as Unity's style sheet
    /// importer lays it out: one rule per declaration block, one complex selector per selector of the
    /// block's list (specificity 1 + 100 per id, 10 per class or pseudo class, 1 per type), values in
    /// typed tables in source order (numbers without unit are floats, lengths and times dimensions,
    /// hex and rgb()/rgba() colors colors with alpha rounded up to a byte, identifiers keywords,
    /// variables or enums, url() asset references, resource() resource paths, other functions their
    /// id and argument count followed by the arguments). A sheet with errors imports empty, as in
    /// Unity, and the errors are logged.
    /// </summary>
    internal sealed class UssImporter
    {
        readonly string m_AssetPath;
        readonly List<string> m_Errors = new List<string>();
        readonly List<string> m_Warnings = new List<string>();

        readonly List<float> m_Floats = new List<float>();
        readonly List<Dimension> m_Dimensions = new List<Dimension>();
        readonly List<Color> m_Colors = new List<Color>();
        readonly List<string> m_Strings = new List<string>();
        readonly List<Object> m_Assets = new List<Object>();
        readonly List<ScalableImage> m_ScalableImages = new List<ScalableImage>();
        readonly List<StyleRule> m_Rules = new List<StyleRule>();
        readonly List<StyleComplexSelector> m_ComplexSelectors = new List<StyleComplexSelector>();
        readonly List<StyleSheet.ImportStruct> m_Imports = new List<StyleSheet.ImportStruct>();
        List<StyleValueHandle> m_Values;
        string m_Text;
        int m_Pos;
        int m_Line = 1;

        /// <summary>Resolves an asset url of the sheet (already made project relative) to its object.</summary>
        internal Func<string, string, Object> resolveAsset;
        /// <summary>Loads the style sheet an @import names.</summary>
        internal Func<string, StyleSheet> resolveImport;

        internal UssImporter(string assetPath) { m_AssetPath = assetPath; }

        internal IReadOnlyList<string> errors => m_Errors;
        internal IReadOnlyList<string> warnings => m_Warnings;

        internal void Import(StyleSheet asset, string text)
        {
            m_Text = text ?? "";
            m_Pos = 0;
            try
            {
                ParseSheet();
            }
            catch (UssSyntaxException e)
            {
                m_Errors.Add($"{m_AssetPath} (line {m_Line}): USS parsing error: {e.Message}");
            }
            if (m_Errors.Count == 0)
            {
                asset.floats = m_Floats.ToArray();
                asset.dimensions = m_Dimensions.ToArray();
                asset.colors = m_Colors.ToArray();
                asset.strings = m_Strings.ToArray();
                asset.assets = m_Assets.ToArray();
                asset.scalableImages = m_ScalableImages.ToArray();
                asset.imports = m_Imports.ToArray();
                asset.rules = m_Rules.ToArray();
                asset.complexSelectors = m_ComplexSelectors.ToArray();
                if (asset.imports.Length > 0) asset.FlattenImportedStyleSheetsRecursive();
            }
            else
            {
                asset.floats = Array.Empty<float>();
                asset.dimensions = Array.Empty<Dimension>();
                asset.colors = Array.Empty<Color>();
                asset.strings = Array.Empty<string>();
                asset.assets = Array.Empty<Object>();
                asset.scalableImages = Array.Empty<ScalableImage>();
                asset.imports = Array.Empty<StyleSheet.ImportStruct>();
                asset.rules = Array.Empty<StyleRule>();
                asset.complexSelectors = Array.Empty<StyleComplexSelector>();
            }
            asset.contentHash = Encoding.UTF8.GetByteCount(m_Text) == 0 ? 0 : StableHash(m_Text);
            asset.importedWithErrors = m_Errors.Count > 0;
            asset.importedWithWarnings = m_Warnings.Count > 0;
            foreach (var e in m_Errors) Debug.LogError(e);
            foreach (var w in m_Warnings) Debug.LogWarning(w);
        }

        static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (var c in s) h = (h ^ c) * 16777619;
                return h;
            }
        }

        sealed class UssSyntaxException : Exception
        {
            public UssSyntaxException(string message) : base(message) { }
        }

        // ---- lexing helpers ----

        bool AtEnd => m_Pos >= m_Text.Length;
        char Cur => m_Pos < m_Text.Length ? m_Text[m_Pos] : '\0';
        char Peek(int o) => m_Pos + o < m_Text.Length ? m_Text[m_Pos + o] : '\0';

        void Advance()
        {
            if (m_Text[m_Pos] == '\n') m_Line++;
            m_Pos++;
        }

        /// <summary>Skips whitespace and comments; whether any was skipped.</summary>
        bool SkipSpace()
        {
            bool any = false;
            while (!AtEnd)
            {
                if (char.IsWhiteSpace(Cur)) { Advance(); any = true; }
                else if (Cur == '/' && Peek(1) == '*')
                {
                    Advance(); Advance();
                    while (!AtEnd && !(Cur == '*' && Peek(1) == '/')) Advance();
                    if (!AtEnd) { Advance(); Advance(); }
                    any = true;
                }
                else break;
            }
            return any;
        }

        // ---- sheet ----

        void ParseSheet()
        {
            while (true)
            {
                SkipSpace();
                if (AtEnd) return;
                if (Cur == '@') ParseAtRule();
                else if (Cur == '}') throw new UssSyntaxException("unexpected '}'");
                else ParseRule();
            }
        }

        void ParseAtRule()
        {
            int start = m_Pos;
            Advance();
            var name = ReadIdent();
            if (name == "import")
            {
                SkipSpace();
                string href;
                if (Cur == '"' || Cur == '\'') href = ReadString();
                else
                {
                    var ident = ReadIdent();
                    if (ident != "url" || Cur != '(') throw new UssSyntaxException("invalid @import");
                    href = ReadUrlBody();
                }
                var media = new List<string>();
                SkipSpace();
                while (!AtEnd && Cur != ';')
                {
                    var m = ReadIdent();
                    if (m.Length == 0) { Advance(); continue; }
                    media.Add(m);
                    SkipSpace();
                    if (Cur == ',') { Advance(); SkipSpace(); }
                }
                if (Cur == ';') Advance();
                StyleSheet imported = resolveImport?.Invoke(href);
                if (imported == null) m_Warnings.Add($"{m_AssetPath} (line {m_Line}): could not resolve @import {href}");
                m_Imports.Add(new StyleSheet.ImportStruct { styleSheet = imported, mediaQueries = media.ToArray() });
                return;
            }
            // Other at-rules: skip their block or statement.
            int depth = 0;
            while (!AtEnd)
            {
                if (Cur == '{') depth++;
                else if (Cur == '}') { depth--; if (depth <= 0) { Advance(); return; } }
                else if (Cur == ';' && depth == 0) { Advance(); return; }
                Advance();
            }
            m_Warnings.Add($"{m_AssetPath}: unsupported at-rule @{name} (from offset {start}) ignored");
        }

        void ParseRule()
        {
            int ruleLine = m_Line;
            int selectorStart = m_Pos;
            while (!AtEnd && Cur != '{')
            {
                if (Cur == '/' && Peek(1) == '*') { SkipSpace(); continue; }
                if (Cur == '"' || Cur == '\'') { ReadString(); continue; }
                Advance();
            }
            if (AtEnd) throw new UssSyntaxException("expected '{'");
            var selectorText = StripComments(m_Text.Substring(selectorStart, m_Pos - selectorStart));
            Advance();

            var rule = new StyleRule { line = ruleLine };
            int ruleIndex = m_Rules.Count;
            foreach (var single in SplitTopLevel(selectorText, ','))
            {
                var trimmed = single.Trim();
                if (trimmed.Length == 0) throw new UssSyntaxException("empty selector");
                AddComplexSelector(trimmed, ruleIndex);
            }

            var properties = new List<StyleProperty>();
            while (true)
            {
                SkipSpace();
                if (AtEnd) throw new UssSyntaxException("expected '}'");
                if (Cur == '}') { Advance(); break; }
                if (Cur == ';') { Advance(); continue; }
                int propLine = m_Line;
                var name = ReadPropertyName();
                SkipSpace();
                if (Cur != ':') throw new UssSyntaxException($"expected ':' after {name}");
                Advance();
                int valueStart = m_Pos;
                int depth = 0;
                while (!AtEnd)
                {
                    if (Cur == '"' || Cur == '\'') { ReadString(); continue; }
                    if (Cur == '/' && Peek(1) == '*') { SkipSpace(); continue; }
                    if (Cur == '(') depth++;
                    else if (Cur == ')') depth--;
                    else if (depth <= 0 && (Cur == ';' || Cur == '}')) break;
                    Advance();
                }
                var valueText = m_Text.Substring(valueStart, m_Pos - valueStart);
                if (Cur == ';') Advance();
                var property = new StyleProperty { name = name, line = propLine };
                m_Values = new List<StyleValueHandle>();
                ParseValue(valueText, propLine, name);
                property.values = m_Values.ToArray();
                properties.Add(property);
            }
            rule.properties = properties.ToArray();
            m_Rules.Add(rule);
        }

        static string StripComments(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 1;
                    sb.Append(' ');
                    continue;
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static IEnumerable<string> SplitTopLevel(string s, char separator)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                else if (s[i] == separator && depth == 0)
                {
                    yield return s.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return s.Substring(start);
        }

        string ReadPropertyName()
        {
            int start = m_Pos;
            while (!AtEnd && (char.IsLetterOrDigit(Cur) || Cur == '-' || Cur == '_')) Advance();
            if (m_Pos == start) throw new UssSyntaxException($"unexpected '{Cur}'");
            return m_Text.Substring(start, m_Pos - start);
        }

        string ReadIdent()
        {
            int start = m_Pos;
            while (!AtEnd && (char.IsLetterOrDigit(Cur) || Cur == '-' || Cur == '_')) Advance();
            return m_Text.Substring(start, m_Pos - start);
        }

        string ReadString()
        {
            char quote = Cur;
            Advance();
            var sb = new StringBuilder();
            while (!AtEnd && Cur != quote)
            {
                if (Cur == '\\' && m_Pos + 1 < m_Text.Length) { Advance(); }
                sb.Append(Cur);
                Advance();
            }
            if (!AtEnd) Advance();
            return sb.ToString();
        }

        /// <summary>At "(" after url: the url, quoted or not.</summary>
        string ReadUrlBody()
        {
            Advance();
            SkipSpace();
            string url;
            if (Cur == '"' || Cur == '\'') url = ReadString();
            else
            {
                int start = m_Pos;
                while (!AtEnd && Cur != ')') Advance();
                url = m_Text.Substring(start, m_Pos - start).Trim();
            }
            SkipSpace();
            if (Cur == ')') Advance();
            return url;
        }

        // ---- selectors ----

        static readonly Regex s_SelectorPart = new Regex(
            "(?<id>#[-]?\\w[\\w-]*)|(?<class>\\.[\\w-]+)|(?<pseudoclass>:[\\w-]+(\\((?<param>.+)\\))?)|(?<type>([^\\-]\\w+|\\w+))|(?<wildcard>\\*)|\\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>A compound selector's parts, as Unity's CSSSpec parses them.</summary>
        static bool ParseSimpleSelector(string selector, out StyleSelectorPart[] parts)
        {
            var matches = s_SelectorPart.Matches(selector);
            if (matches.Count < 1)
            {
                parts = null;
                return false;
            }
            parts = new StyleSelectorPart[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var type = StyleSelectorType.Unknown;
                string value = string.Empty;
                if (m.Groups["wildcard"].Value.Length > 0) { value = "*"; type = StyleSelectorType.Wildcard; }
                else if (m.Groups["id"].Value.Length > 0) { value = m.Groups["id"].Value.Substring(1); type = StyleSelectorType.ID; }
                else if (m.Groups["class"].Value.Length > 0) { value = m.Groups["class"].Value.Substring(1); type = StyleSelectorType.Class; }
                else if (m.Groups["pseudoclass"].Value.Length > 0)
                {
                    var param = m.Groups["param"].Value;
                    if (param.Length > 0) { value = param; type = StyleSelectorType.RecursivePseudoClass; }
                    else { value = m.Groups["pseudoclass"].Value.Substring(1); type = StyleSelectorType.PseudoClass; }
                }
                else if (m.Groups["type"].Value.Length > 0) { value = m.Groups["type"].Value; type = StyleSelectorType.Type; }
                parts[i] = new StyleSelectorPart { type = type, value = value };
            }
            return true;
        }

        static int Specificity(IEnumerable<StyleSelectorPart> parts)
        {
            int s = 1;
            foreach (var p in parts)
            {
                switch (p.type)
                {
                    case StyleSelectorType.Type: s += 1; break;
                    case StyleSelectorType.Class:
                    case StyleSelectorType.PseudoClass: s += 10; break;
                    case StyleSelectorType.ID: s += 100; break;
                }
            }
            return s;
        }

        void AddComplexSelector(string text, int ruleIndex)
        {
            // Compound selectors and the combinators between them.
            var compounds = new List<(string Text, StyleSelectorRelationship Previous)>();
            var relationship = StyleSelectorRelationship.None;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                if (text[i] == '>')
                {
                    relationship = StyleSelectorRelationship.Child;
                    i++;
                    continue;
                }
                int start = i;
                int depth = 0;
                while (i < text.Length && (depth > 0 || (!char.IsWhiteSpace(text[i]) && text[i] != '>')))
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')') depth--;
                    i++;
                }
                compounds.Add((text.Substring(start, i - start), compounds.Count == 0 ? StyleSelectorRelationship.None : relationship));
                relationship = StyleSelectorRelationship.Descendent;
            }
            var selectors = new List<StyleSelector>();
            var allParts = new List<StyleSelectorPart>();
            foreach (var (compound, previous) in compounds)
            {
                if (!ParseSimpleSelector(compound, out var parts) || Array.Exists(parts, p => p.type == StyleSelectorType.Unknown))
                {
                    m_Errors.Add($"{m_AssetPath} (line {m_Line}): Unsupported selector format: '{compound}'");
                    return;
                }
                if (Array.Exists(parts, p => p.type == StyleSelectorType.RecursivePseudoClass))
                {
                    m_Errors.Add($"{m_AssetPath} (line {m_Line}): Recursive selector detected: '{compound}'");
                    return;
                }
                selectors.Add(new StyleSelector { parts = parts, previousRelationship = previous });
                allParts.AddRange(parts);
            }
            if (selectors.Count == 0) return;
            m_ComplexSelectors.Add(new StyleComplexSelector
            {
                specificity = Specificity(allParts),
                ruleIndex = ruleIndex,
                selectors = selectors.ToArray(),
            });
        }

        // ---- values ----

        void AddHandle(StyleValueType type, int index) => m_Values.Add(new StyleValueHandle(index, type));

        void AddFloat(float f) { m_Floats.Add(f); AddHandle(StyleValueType.Float, m_Floats.Count - 1); }
        void AddDimension(Dimension d) { m_Dimensions.Add(d); AddHandle(StyleValueType.Dimension, m_Dimensions.Count - 1); }
        void AddColor(Color c) { m_Colors.Add(c); AddHandle(StyleValueType.Color, m_Colors.Count - 1); }
        void AddString(string s, StyleValueType type)
        {
            if (type == StyleValueType.Variable)
            {
                int index = m_Strings.IndexOf(s);
                if (index < 0) { m_Strings.Add(s); index = m_Strings.Count - 1; }
                AddHandle(type, index);
                return;
            }
            m_Strings.Add(s);
            AddHandle(type, m_Strings.Count - 1);
        }
        void AddAsset(Object o) { m_Assets.Add(o); AddHandle(StyleValueType.AssetReference, m_Assets.Count - 1); }
        void AddKeyword(StyleValueKeyword k) => AddHandle(StyleValueType.Keyword, (int)k);
        void AddComma() => AddHandle(StyleValueType.CommaSeparator, 0);

        enum TermKind { Number, Ident, String, Hash, Function, Url, Comma, Slash, Other }

        sealed class Term
        {
            public TermKind Kind;
            public string Text;
            public float Number;
            public string Unit;
            public List<Term> Args;
        }

        /// <summary>The terms of a value text; whitespace is dropped (it only separates terms).</summary>
        static List<Term> Lex(string s, ref int i, bool inFunction)
        {
            var terms = new List<Term>();
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 2;
                    continue;
                }
                if (c == ')' && inFunction) { i++; return terms; }
                if (c == ',') { terms.Add(new Term { Kind = TermKind.Comma, Text = "," }); i++; continue; }
                if (c == '"' || c == '\'')
                {
                    char q = c;
                    int start = ++i;
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != q)
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) i++;
                        sb.Append(s[i++]);
                    }
                    i++;
                    terms.Add(new Term { Kind = TermKind.String, Text = sb.ToString() });
                    continue;
                }
                if (c == '#')
                {
                    int start = ++i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
                    terms.Add(new Term { Kind = TermKind.Hash, Text = s.Substring(start, i - start) });
                    continue;
                }
                if (char.IsDigit(c) || ((c == '.' || c == '-' || c == '+') && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || (s[i + 1] == '.' && i + 2 < s.Length && char.IsDigit(s[i + 2])))))
                {
                    int start = i;
                    if (c == '-' || c == '+') i++;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                    if (i < s.Length && s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                    {
                        i++;
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                    }
                    if (i + 1 < s.Length && (s[i] == 'e' || s[i] == 'E') && (char.IsDigit(s[i + 1]) || ((s[i + 1] == '-' || s[i + 1] == '+') && i + 2 < s.Length && char.IsDigit(s[i + 2]))))
                    {
                        i += 2;
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                    }
                    float number = float.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                    string unit = "";
                    if (i < s.Length && s[i] == '%') { unit = "%"; i++; }
                    else
                    {
                        int us = i;
                        while (i < s.Length && char.IsLetter(s[i])) i++;
                        unit = s.Substring(us, i - us).ToLowerInvariant();
                    }
                    terms.Add(new Term { Kind = TermKind.Number, Number = number, Unit = unit });
                    continue;
                }
                if (char.IsLetter(c) || c == '-' || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
                    var ident = s.Substring(start, i - start);
                    if (i < s.Length && s[i] == '(')
                    {
                        i++;
                        if (ident.Equals("url", StringComparison.OrdinalIgnoreCase))
                        {
                            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                            string url;
                            if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                            {
                                char q = s[i++];
                                int us = i;
                                while (i < s.Length && s[i] != q) i++;
                                url = s.Substring(us, i - us);
                                i++;
                                while (i < s.Length && s[i] != ')') i++;
                            }
                            else
                            {
                                int us = i;
                                while (i < s.Length && s[i] != ')') i++;
                                url = s.Substring(us, i - us).Trim();
                            }
                            i++;
                            terms.Add(new Term { Kind = TermKind.Url, Text = url });
                            continue;
                        }
                        var args = Lex(s, ref i, true);
                        terms.Add(new Term { Kind = TermKind.Function, Text = ident.ToLowerInvariant(), Args = args });
                        continue;
                    }
                    terms.Add(new Term { Kind = TermKind.Ident, Text = ident });
                    continue;
                }
                if (c == '/') { terms.Add(new Term { Kind = TermKind.Slash, Text = "/" }); i++; continue; }
                terms.Add(new Term { Kind = TermKind.Other, Text = c.ToString() });
                i++;
            }
            return terms;
        }

        void ParseValue(string text, int line, string propertyName)
        {
            int i = 0;
            var terms = Lex(text, ref i, false);
            foreach (var term in terms) VisitTerm(term, line, propertyName);
        }

        static readonly Dictionary<string, StyleValueKeyword> s_Keywords = BuildKeywords();

        static Dictionary<string, StyleValueKeyword> BuildKeywords()
        {
            var d = new Dictionary<string, StyleValueKeyword>();
            foreach (StyleValueKeyword k in Enum.GetValues(typeof(StyleValueKeyword))) d[k.ToString().ToLowerInvariant()] = k;
            return d;
        }

        void VisitTerm(Term term, int line, string propertyName)
        {
            switch (term.Kind)
            {
                case TermKind.Comma:
                    AddComma();
                    return;
                case TermKind.Number:
                    switch (term.Unit)
                    {
                        case "": AddFloat(term.Number); return;
                        case "px": AddDimension(new Dimension(term.Number, Dimension.Unit.Pixel)); return;
                        case "%": AddDimension(new Dimension(term.Number, Dimension.Unit.Percent)); return;
                        case "s": AddDimension(new Dimension(term.Number, Dimension.Unit.Second)); return;
                        case "ms": AddDimension(new Dimension(term.Number, Dimension.Unit.Millisecond)); return;
                        case "deg": AddDimension(new Dimension(term.Number, Dimension.Unit.Degree)); return;
                        case "grad": AddDimension(new Dimension(term.Number, Dimension.Unit.Gradian)); return;
                        case "rad": AddDimension(new Dimension(term.Number, Dimension.Unit.Radian)); return;
                        case "turn": AddDimension(new Dimension(term.Number, Dimension.Unit.Turn)); return;
                    }
                    m_Errors.Add($"{m_AssetPath} (line {line}): Unsupported unit: '{Unbound.MonoNumberFormat.Format(term.Number, null, CultureInfo.InvariantCulture)}{term.Unit}'");
                    return;
                case TermKind.Ident:
                    if (term.Text == "inherit") { AddKeyword(StyleValueKeyword.Inherit); return; }
                    if (s_Keywords.TryGetValue(term.Text.ToLowerInvariant(), out var keyword)) AddKeyword(keyword);
                    else if (term.Text.StartsWith("--", StringComparison.Ordinal)) AddString(term.Text, StyleValueType.Variable);
                    else AddString(term.Text, StyleValueType.Enum);
                    return;
                case TermKind.String:
                    AddString(term.Text, StyleValueType.String);
                    return;
                case TermKind.Hash:
                    if (TryHexColor(term.Text, out var color)) { AddColor(color); return; }
                    m_Errors.Add($"{m_AssetPath} (line {line}): USS parsing error: invalid color #{term.Text}");
                    return;
                case TermKind.Url:
                    VisitUrl(term.Text, line, propertyName);
                    return;
                case TermKind.Function:
                    VisitFunction(term, line, propertyName);
                    return;
            }
            m_Errors.Add($"{m_AssetPath} (line {line}): Unsupported term: '{term.Text}'");
        }

        static bool TryHexColor(string hex, out Color color)
        {
            color = default;
            static int H(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            int r, g, b;
            if (hex.Length == 3)
            {
                if (H(hex[0]) < 0 || H(hex[1]) < 0 || H(hex[2]) < 0) return false;
                r = H(hex[0]) * 17; g = H(hex[1]) * 17; b = H(hex[2]) * 17;
            }
            else if (hex.Length == 6)
            {
                for (int k = 0; k < 6; k++) if (H(hex[k]) < 0) return false;
                r = H(hex[0]) * 16 + H(hex[1]); g = H(hex[2]) * 16 + H(hex[3]); b = H(hex[4]) * 16 + H(hex[5]);
            }
            else return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)v));

        void VisitFunction(Term term, int line, string propertyName)
        {
            var args = term.Args;
            if (term.Text == "rgb" || term.Text == "rgba")
            {
                // As ExCSS: numbers separated by commas.
                bool numbers = true;
                for (int k = 0; k < args.Count; k++)
                    numbers &= k % 2 == 1 ? args[k].Kind == TermKind.Comma : args[k].Kind == TermKind.Number && args[k].Unit == "";
                if (term.Text == "rgb" && args.Count == 5 && numbers)
                {
                    AddColor(new Color(ToByte(args[0].Number) / 255f, ToByte(args[2].Number) / 255f, ToByte(args[4].Number) / 255f, 1f));
                    return;
                }
                if (term.Text == "rgba" && args.Count == 7 && numbers)
                {
                    byte a = (byte)Math.Max(Math.Min(Math.Ceiling(255.0 * args[6].Number), 255.0), 0.0);
                    AddColor(new Color(ToByte(args[0].Number) / 255f, ToByte(args[2].Number) / 255f, ToByte(args[4].Number) / 255f, a / 255f));
                    return;
                }
            }
            if (term.Text == "resource")
            {
                if (args.Count == 0 || (args[0].Kind != TermKind.String && args[0].Kind != TermKind.Ident))
                {
                    m_Errors.Add($"{m_AssetPath} (line {line}): Missing function argument: resource");
                    return;
                }
                AddString(args[0].Text, StyleValueType.ResourcePath);
                return;
            }
            if (args.Count == 0)
            {
                m_Errors.Add($"{m_AssetPath} (line {line}): Missing function argument: {term.Text}");
                return;
            }
            StyleValueFunction function;
            switch (term.Text)
            {
                case "var": function = StyleValueFunction.Var; break;
                case "env": function = StyleValueFunction.Env; break;
                case "linear-gradient": function = StyleValueFunction.LinearGradient; break;
                default:
                    m_Warnings.Add($"{m_AssetPath} (line {line}): Unknown function {term.Text} in property {propertyName}");
                    return;
            }
            if (function == StyleValueFunction.Var && !ValidVar(args, line)) return;
            AddHandle(StyleValueType.Function, (int)function);
            AddFloat(args.Count);
            foreach (var arg in args) VisitTerm(arg, line, propertyName);
        }

        bool ValidVar(List<Term> args, int line)
        {
            var first = args[0];
            if (first.Kind != TermKind.Ident || string.IsNullOrEmpty(first.Text))
            {
                m_Errors.Add($"{m_AssetPath} (line {line}): var() needs a variable name");
                return false;
            }
            if (!first.Text.StartsWith("--", StringComparison.Ordinal) || first.Text.Length < 3)
            {
                m_Errors.Add($"{m_AssetPath} (line {line}): invalid variable name '{first.Text}'");
                return false;
            }
            if (args.Count > 1 && args[1].Kind != TermKind.Comma)
            {
                m_Errors.Add($"{m_AssetPath} (line {line}): unexpected token in var()");
                return false;
            }
            if (args.Count == 2)
            {
                m_Errors.Add($"{m_AssetPath} (line {line}): empty function argument in var()");
                return false;
            }
            return true;
        }

        void VisitUrl(string url, int line, string propertyName)
        {
            var obj = resolveAsset?.Invoke(m_AssetPath, url);
            if (obj == null)
            {
                AddString(url, StyleValueType.MissingAssetReference);
                m_Warnings.Add($"{m_AssetPath} (line {line}): Invalid asset path: '{url}'");
                return;
            }
            AddAsset(obj);
        }
    }
}
