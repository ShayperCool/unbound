using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The Editor's style catalog: the values its extension style sheets give to named blocks,
    /// which the Editor applies to the GUI styles of its current skin (a style named `button`
    /// takes the block `Button`, a custom style `AC BoldHeader` the block `AC-BoldHeader`).
    ///
    /// The sheets are merged in order: a rule is the union of the rules with the same selector
    /// text, a later sheet replacing a property's values; `--` properties are variables shared by
    /// all rules, the last definition winning; `-unity-extend` gives a rule (and its pseudo-state
    /// rules) the properties of the named parent it lacks; then variables are substituted. Rules
    /// of one compound selector become blocks named by its type, class and id parts joined with
    /// dots, their values keyed by property, pseudo-state set and kind.
    /// (Matches the Unity 6000.0.71f1 Editor, tests/projects/editorskin.)
    /// </summary>
    internal sealed class EditorStyleCatalog
    {
        /// <summary>The pseudo-state sets a block's values are stored under.</summary>
        [Flags]
        internal enum State
        {
            None = 0,
            Normal = 1,
            Active = 2,
            Checked = 8,
            Focus = 0x2000,
            Hover = 0x10000,
            /// <summary>`:root`; the bits above it are other pseudo-classes (`:disabled`, ...),
            /// which no GUI style reads.</summary>
            OtherFirst = 0x100000,
        }

        internal enum Kind { Keyword, Number, Color, Text, Rect, Other }

        /// <summary>The value of the `true` keyword; `false` and `none` are 0, a keyword the
        /// catalog does not know -1 (which reads as no value).</summary>
        const int KeywordTrue = 1;

        const float Unset = float.MaxValue;

        internal struct Value
        {
            internal Kind Kind;
            internal float Number;
            internal Color Color;
            internal string Text;
            /// <summary>The object of an asset reference (the Editor keeps its path and loads it again).</summary>
            internal Object Asset;
            internal float Top, Right, Bottom, Left;
        }

        internal sealed class Block
        {
            internal readonly string Name;
            readonly Dictionary<(string, State, Kind), Value> m_Values = new();
            readonly State m_State;

            internal Block(string name) : this(name, null, State.Normal) { }

            Block(string name, Dictionary<(string, State, Kind), Value> values, State state)
            {
                Name = name;
                if (values != null) m_Values = values;
                m_State = state;
            }

            /// <summary>The same block read under another pseudo-state set.</summary>
            internal Block In(State state) => new Block(Name, m_Values, state);

            internal void Set(string key, State state, Value value) => m_Values[(key, state, value.Kind)] = value;

            internal bool TryGet(string key, Kind kind, out Value value)
            {
                if (m_Values.TryGetValue((key, m_State, kind), out value))
                    return kind != Kind.Keyword || value.Number >= 0;
                return false;
            }

            internal bool Has(string key, Kind kind) => TryGet(key, kind, out _);

            internal float GetFloat(string key, float fallback) => TryGet(key, Kind.Number, out var v) ? v.Number : fallback;

            /// <summary>A number truncated, as the Editor reads integers.</summary>
            internal int GetInt(string key, int fallback) => (int)GetFloat(key, fallback);

            internal bool GetBool(string key, bool fallback) => TryGet(key, Kind.Keyword, out var v) ? v.Number == KeywordTrue : fallback;

            internal Color GetColor(string key, Color fallback) => TryGet(key, Kind.Color, out var v) ? v.Color : fallback;

            internal string GetText(string key, string fallback = "") => TryGet(key, Kind.Text, out var v) ? v.Text : fallback;

            /// <summary>The object a text value names: the referenced asset, or the Editor
            /// resource at the path; the fallback without a value, null when nothing loads.</summary>
            internal T GetResource<T>(string key, T fallback) where T : Object
            {
                if (!TryGet(key, Kind.Text, out var v)) return fallback;
                if (v.Asset != null) return v.Asset as T;
                if (string.IsNullOrEmpty(v.Text)) return fallback;
                return Unbound.UIElements.UiResources.EditorLoad(v.Text) as T;
            }
        }

        readonly Dictionary<string, Block> m_Blocks = new(StringComparer.Ordinal);

        /// <summary>The block of a name, null when no rule made one.</summary>
        internal Block Find(string name) => name != null && m_Blocks.TryGetValue(name, out var block) ? block : null;

        /// <summary>The block of `:root`, where the theme's default text colour lives; an empty
        /// block when the sheets have none.</summary>
        internal Block Root => (Find("") ?? new Block("")).In(RootState);

        internal int Count => m_Blocks.Count;

        // ---- the sheets' rules -------------------------------------------------------------

        sealed class RawValue
        {
            internal StyleValueType Type;
            internal object Obj;
            /// <summary>A function's arguments, split at commas.</summary>
            internal List<RawValue[]> Args;

            internal bool IsFloat => Type == StyleValueType.Float || Type == StyleValueType.Dimension;
            internal float Float => Type == StyleValueType.Dimension ? ((Dimension)Obj).value : (float)Obj;
            internal string Text => Obj as string ?? Obj?.ToString() ?? "";
            internal bool IsVariable => Type == StyleValueType.Variable || (Type == StyleValueType.Function && Text == "env");
        }

        sealed class Property
        {
            internal string Name;
            internal List<RawValue> Values;
        }

        sealed class Rule
        {
            internal string Selector;
            /// <summary>The parts of the rule's only compound selector; null for a selector with
            /// descendants, which makes no block.</summary>
            internal StyleSelectorPart[] Parts;
            internal readonly List<Property> Ordered = new();
            internal readonly Dictionary<string, Property> Properties = new(StringComparer.Ordinal);
            internal readonly List<string> PseudoRules = new();

            internal void Add(Property property)
            {
                Properties.Add(property.Name, property);
                Ordered.Add(property);
            }

            internal void Remove(string name)
            {
                if (Properties.Remove(name, out var property)) Ordered.Remove(property);
            }
        }

        sealed class Extension
        {
            internal string Parent;
            internal readonly List<Rule> Children = new();
        }

        readonly List<Rule> m_RuleOrder = new();
        readonly Dictionary<string, Rule> m_Rules = new(StringComparer.Ordinal);
        readonly Dictionary<string, Property> m_Variables = new(StringComparer.Ordinal);
        readonly List<string> m_VariableOrder = new();
        readonly List<Extension> m_Extensions = new();
        readonly Dictionary<string, Extension> m_ExtensionsByParent = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> m_ParentOfChild = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> m_OtherStates = new(StringComparer.Ordinal);

        const string ExtendProperty = "-unity-extend";

        /// <summary>The catalog of style sheets, in their order.</summary>
        internal static EditorStyleCatalog Build(IEnumerable<StyleSheet> sheets)
        {
            var catalog = new EditorStyleCatalog();
            foreach (var sheet in sheets)
                if (sheet != null) catalog.AddRules(sheet);
            foreach (var rule in catalog.m_RuleOrder.ToArray()) catalog.NotePseudoRule(rule);
            catalog.Extend();
            catalog.SubstituteVariables();
            foreach (var rule in catalog.m_RuleOrder) catalog.Compile(rule);
            return catalog;
        }

        void AddRule(Rule rule)
        {
            m_Rules.Add(rule.Selector, rule);
            m_RuleOrder.Add(rule);
        }

        void AddRules(StyleSheet sheet)
        {
            foreach (var selector in sheet.complexSelectors ?? Array.Empty<StyleComplexSelector>())
            {
                if (selector.rule == null) continue;
                string text = SelectorText(selector);
                if (!m_Rules.TryGetValue(text, out var rule))
                {
                    rule = new Rule { Selector = text, Parts = selector.selectors.Length == 1 ? selector.selectors[0].parts : null };
                    AddRule(rule);
                }
                foreach (var source in selector.rule.properties ?? Array.Empty<StyleProperty>())
                {
                    var values = ReadValues(sheet, source.values ?? Array.Empty<StyleValueHandle>());
                    if (rule.Properties.TryGetValue(source.name, out var property)) property.Values = values;
                    else rule.Add(property = new Property { Name = source.name, Values = values });
                    if (property.Name.StartsWith("--", StringComparison.Ordinal))
                    {
                        if (!m_Variables.ContainsKey(property.Name)) m_VariableOrder.Add(property.Name);
                        m_Variables[property.Name] = property;
                    }
                }
                if (rule.Properties.TryGetValue(ExtendProperty, out var extend) && extend.Values.Count > 0)
                {
                    string parent = extend.Values[0].Text;
                    if (!m_ExtensionsByParent.TryGetValue(parent, out var extension))
                    {
                        extension = new Extension { Parent = parent };
                        m_ExtensionsByParent.Add(parent, extension);
                        m_Extensions.Add(extension);
                    }
                    extension.Children.Add(rule);
                    m_ParentOfChild.TryAdd(rule.Selector, parent);
                }
            }
        }

        static string SelectorText(StyleComplexSelector selector)
        {
            var text = new StringBuilder();
            foreach (var compound in selector.selectors)
            {
                if (text.Length > 0) text.Append(compound.previousRelationship == StyleSelectorRelationship.Child ? " > " : " ");
                foreach (var part in compound.parts ?? Array.Empty<StyleSelectorPart>())
                {
                    switch (part.type)
                    {
                        case StyleSelectorType.Wildcard: text.Append('*'); break;
                        case StyleSelectorType.Type: text.Append(part.value); break;
                        case StyleSelectorType.Class: text.Append('.').Append(part.value); break;
                        case StyleSelectorType.ID: text.Append('#').Append(part.value); break;
                        case StyleSelectorType.PseudoClass:
                        case StyleSelectorType.RecursivePseudoClass: text.Append(':').Append(part.value); break;
                    }
                }
            }
            return text.ToString();
        }

        static List<RawValue> ReadValues(StyleSheet sheet, StyleValueHandle[] handles)
        {
            var values = new List<RawValue>(handles.Length);
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].valueType == StyleValueType.Function) values.Add(ReadFunction(sheet, handles, ref i));
                else if (handles[i].valueType != StyleValueType.CommaSeparator) values.Add(ReadValue(sheet, handles[i]));
            }
            return values;
        }

        /// <summary>A function: its name, the count of the handles of its arguments, then those.</summary>
        static RawValue ReadFunction(StyleSheet sheet, StyleValueHandle[] handles, ref int index)
        {
            var function = new RawValue
            {
                Type = StyleValueType.Function,
                Obj = sheet.ReadFunction(handles[index]).ToString().ToLowerInvariant(),
                Args = new List<RawValue[]>(),
            };
            int count = ++index < handles.Length ? (int)sheet.ReadFloat(handles[index]) : 0;
            var argument = new List<RawValue>();
            for (int i = 0; i < count && index + 1 < handles.Length; i++)
            {
                var handle = handles[++index];
                if (handle.valueType == StyleValueType.Function) argument.Add(ReadFunction(sheet, handles, ref index));
                else if (handle.valueType == StyleValueType.CommaSeparator)
                {
                    function.Args.Add(argument.ToArray());
                    argument.Clear();
                }
                else argument.Add(ReadValue(sheet, handle));
            }
            function.Args.Add(argument.ToArray());
            return function;
        }

        static RawValue ReadValue(StyleSheet sheet, StyleValueHandle handle)
        {
            object obj = handle.valueType switch
            {
                StyleValueType.Keyword => sheet.ReadKeyword(handle),
                StyleValueType.Float => sheet.ReadFloat(handle),
                StyleValueType.Dimension => sheet.ReadDimension(handle),
                StyleValueType.Color => sheet.ReadColor(handle),
                StyleValueType.ResourcePath => sheet.ReadResourcePath(handle),
                StyleValueType.AssetReference => sheet.ReadAssetReference(handle),
                StyleValueType.Enum => sheet.ReadEnum(handle),
                StyleValueType.Variable => sheet.ReadVariable(handle),
                StyleValueType.String => sheet.ReadString(handle),
                StyleValueType.ScalableImage => sheet.ReadScalableImage(handle).normalImage,
                StyleValueType.MissingAssetReference => sheet.ReadMissingAssetReferenceUrl(handle),
                _ => null,
            };
            return new RawValue { Type = handle.valueType, Obj = obj };
        }

        static string WithoutPseudo(string selector)
        {
            int colon = selector.IndexOf(':');
            return colon < 0 ? selector : selector.Substring(0, colon);
        }

        void NotePseudoRule(Rule rule)
        {
            if (rule.Selector.IndexOf(':') < 0) return;
            string plain = WithoutPseudo(rule.Selector);
            if (plain.Length > 0 && m_Rules.TryGetValue(plain, out var main)) main.PseudoRules.Add(rule.Selector);
        }

        // ---- -unity-extend -----------------------------------------------------------------

        void Extend()
        {
            var done = new HashSet<Extension>();
            foreach (var extension in m_Extensions) Extend(extension, done);
        }

        /// <summary>Extends the children of a parent, after the parent took what it extends itself.</summary>
        void Extend(Extension extension, HashSet<Extension> done)
        {
            if (!done.Add(extension)) return;
            if (m_ParentOfChild.TryGetValue(extension.Parent, out var grandparent) && m_ExtensionsByParent.TryGetValue(grandparent, out var earlier))
                Extend(earlier, done);
            if (!m_Rules.TryGetValue(extension.Parent, out var parent))
            {
                Debug.LogWarning("Cannot resolve parent rule: " + extension.Parent);
                return;
            }
            var sources = new List<Rule> { parent };
            foreach (string pseudo in parent.PseudoRules) sources.Add(m_Rules[pseudo]);
            foreach (var child in extension.Children)
            {
                foreach (var source in sources)
                {
                    string selector = source.Selector.Replace(extension.Parent, child.Selector);
                    if (selector == child.Selector)
                    {
                        child.Remove(ExtendProperty);
                        Inherit(source, child);
                        continue;
                    }
                    if (!m_Rules.TryGetValue(selector, out var target))
                    {
                        target = new Rule { Selector = selector, Parts = RebasedParts(source.Parts, child.Selector) };
                        AddRule(target);
                        NotePseudoRule(target);
                    }
                    Inherit(source, target);
                }
            }
        }

        /// <summary>A parent's pseudo-state selector for a child: its first part is the child's
        /// class or type.</summary>
        static StyleSelectorPart[] RebasedParts(StyleSelectorPart[] source, string childSelector)
        {
            if (source == null || source.Length == 0 || childSelector.Length == 0) return null;
            var parts = (StyleSelectorPart[])source.Clone();
            bool isClass = childSelector[0] == '.';
            parts[0] = new StyleSelectorPart { type = isClass ? StyleSelectorType.Class : StyleSelectorType.Type, value = isClass ? childSelector.Substring(1) : childSelector };
            return parts;
        }

        static void Inherit(Rule parent, Rule child)
        {
            foreach (var property in parent.Ordered)
                if (!child.Properties.ContainsKey(property.Name)) child.Add(property);
        }

        // ---- variables ---------------------------------------------------------------------

        sealed class VariableNode
        {
            internal Property Variable;
            internal VariableNode Parent;
            internal List<VariableNode> Children;
        }

        /// <summary>
        /// Substitutes variable values: in the variables first, each after the first variable it
        /// refers to, then in every rule. A substituted value is not looked at again.
        /// </summary>
        void SubstituteVariables()
        {
            var nodes = new Dictionary<string, VariableNode>(StringComparer.Ordinal);
            var roots = new List<VariableNode>();
            foreach (string name in m_VariableOrder)
            {
                var node = Node(m_Variables[name], nodes);
                if (node.Parent == null) roots.Add(node);
            }
            foreach (var root in roots) SubstituteTree(root);
            foreach (var rule in m_RuleOrder)
                foreach (var property in rule.Ordered) Substitute(property);
        }

        VariableNode Node(Property variable, Dictionary<string, VariableNode> nodes)
        {
            if (nodes.TryGetValue(variable.Name, out var node)) return node;
            nodes[variable.Name] = node = new VariableNode { Variable = variable };
            foreach (var value in variable.Values)
            {
                if (!value.IsVariable) continue;
                string name = ReferredVariable(value);
                if (name == null || !m_Variables.TryGetValue(name, out var referred)) continue;
                var parent = Node(referred, nodes);
                if (node.Parent != null) continue;
                (parent.Children ??= new List<VariableNode>()).Add(node);
                node.Parent = parent;
            }
            return node;
        }

        void SubstituteTree(VariableNode node)
        {
            Substitute(node.Variable);
            if (node.Children == null) return;
            foreach (var child in node.Children) SubstituteTree(child);
        }

        static string ReferredVariable(RawValue value)
        {
            if (value.Type != StyleValueType.Function) return value.Text;
            return value.Args.Count > 0 && value.Args[0].Length > 0 ? value.Args[0][0].Text : null;
        }

        void Substitute(Property property)
        {
            var values = property.Values;
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (!value.IsVariable) continue;
                IList<RawValue> replacement = null;
                string name = ReferredVariable(value);
                if (name != null && m_Variables.TryGetValue(name, out var variable)) replacement = variable.Values;
                else if (value.Type == StyleValueType.Function && value.Args.Count > 1) replacement = value.Args[1];
                if (replacement == null || replacement.Count == 0) continue;
                // The variable's own list may be the one being rewritten.
                var copy = new List<RawValue>(replacement);
                values[i] = copy[0];
                for (int k = 1; k < copy.Count; k++) values.Insert(++i, copy[k]);
            }
        }

        // ---- blocks ------------------------------------------------------------------------

        static readonly State RootState = State.OtherFirst;

        State StateOf(StyleSelectorPart[] parts)
        {
            var state = State.None;
            foreach (var part in parts)
            {
                if (part.type != StyleSelectorType.PseudoClass) continue;
                string name = (part.value ?? "").Replace("-", "").ToLowerInvariant();
                state |= name switch
                {
                    "normal" => State.Normal,
                    "active" => State.Active,
                    "checked" => State.Checked,
                    "focus" => State.Focus,
                    "hover" => State.Hover,
                    "root" => RootState,
                    _ => OtherState(name),
                };
            }
            return state == State.None ? State.Normal : state;
        }

        /// <summary>A bit for a pseudo-class GUI styles have no state for, so that its values
        /// stay apart from the states styles read (the first ten each have their own, further
        /// ones share the last).</summary>
        State OtherState(string name)
        {
            if (!m_OtherStates.TryGetValue(name, out int bit))
                m_OtherStates[name] = bit = Math.Min(m_OtherStates.Count + 1, 10);
            return (State)((int)State.OtherFirst << bit);
        }

        static string BlockName(StyleSelectorPart[] parts)
        {
            var name = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                var type = parts[i].type;
                if (type != StyleSelectorType.ID && type != StyleSelectorType.Type && type != StyleSelectorType.Class && type != StyleSelectorType.Wildcard) continue;
                if (i > 0) name.Append('.');
                name.Append(parts[i].value);
            }
            return name.ToString();
        }

        void Compile(Rule rule)
        {
            if (rule.Parts == null || rule.Parts.Length == 0) return;
            var state = StateOf(rule.Parts);
            var values = new List<(string Key, Value Value)>(rule.Ordered.Count);
            foreach (var property in rule.Ordered)
                if (TryCompile(property.Values, out var value)) values.Add((property.Name, value));
            foreach (string rect in s_Rects) ExpandRect(values, rect);
            string name = BlockName(rule.Parts);
            if (!m_Blocks.TryGetValue(name, out var block)) m_Blocks.Add(name, block = new Block(name));
            foreach (var (key, value) in values) block.Set(key, state, value);
        }

        /// <summary>The properties whose `-top`, `-right`, `-bottom` and `-left` members GUI
        /// styles read.</summary>
        static readonly string[] s_Rects = { "margin", "padding", "-unity-overflow", "-unity-slice" };

        static bool TryCompile(List<RawValue> values, out Value value)
        {
            value = default;
            int count = values.Count;
            if (count == 0) return false;
            if (count == 1) return TryCompileOne(values[0], out value);
            if (count <= 4 && values.TrueForAll(v => v.IsFloat))
            {
                // CSS shorthand: two values are vertical and horizontal, three top, horizontal, bottom.
                value.Kind = Kind.Rect;
                value.Top = values[0].Float;
                value.Right = values[1].Float;
                value.Bottom = values[count == 2 ? 0 : 2].Float;
                value.Left = values[count == 4 ? 3 : 1].Float;
                return true;
            }
            // Lists of other values (borders, transitions) are nothing GUI styles read.
            value.Kind = Kind.Other;
            return count <= 5;
        }

        static bool TryCompileOne(RawValue raw, out Value value)
        {
            value = default;
            switch (raw.Type)
            {
                case StyleValueType.Keyword:
                    value.Kind = Kind.Keyword;
                    value.Number = (StyleValueKeyword)raw.Obj switch
                    {
                        StyleValueKeyword.True => KeywordTrue,
                        StyleValueKeyword.False or StyleValueKeyword.None => 0,
                        StyleValueKeyword.Inherit => 2,
                        StyleValueKeyword.Auto => 3,
                        StyleValueKeyword.Unset => 4,
                        _ => -1,
                    };
                    return true;
                case StyleValueType.Float:
                case StyleValueType.Dimension:
                    value.Kind = Kind.Number;
                    value.Number = raw.Float;
                    return true;
                case StyleValueType.Color:
                    value.Kind = Kind.Color;
                    value.Color = (Color)raw.Obj;
                    return true;
                case StyleValueType.AssetReference:
                case StyleValueType.ScalableImage:
                    value.Kind = Kind.Text;
                    value.Asset = raw.Obj as Object;
                    value.Text = "";
                    return true;
                case StyleValueType.ResourcePath:
                case StyleValueType.Enum:
                case StyleValueType.Variable:
                    // A name of a colour is a colour.
                    if (ColorUtility.TryParseHtmlString(raw.Text, out var color))
                    {
                        value.Kind = Kind.Color;
                        value.Color = color;
                    }
                    else
                    {
                        value.Kind = Kind.Text;
                        value.Text = raw.Text;
                    }
                    return true;
                case StyleValueType.String:
                    value.Kind = Kind.Text;
                    value.Text = raw.Text;
                    return true;
                case StyleValueType.Function:
                    value.Kind = Kind.Other;
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The members of a rect property of one rule: the values are read in order, a rect
        /// replacing all four sides, a single number filling the sides not given yet, a member
        /// its side. The sides that end up given are stored as numbers under `name-side`.
        /// </summary>
        static void ExpandRect(List<(string Key, Value Value)> values, string name)
        {
            string top = name + "-top", right = name + "-right", bottom = name + "-bottom", left = name + "-left";
            float t = Unset, r = Unset, b = Unset, l = Unset;
            bool any = false;
            foreach (var (key, value) in values)
            {
                if (key == name && value.Kind == Kind.Rect) { t = value.Top; r = value.Right; b = value.Bottom; l = value.Left; }
                else if (key == name && value.Kind == Kind.Number)
                {
                    if (t == Unset) t = value.Number;
                    if (r == Unset) r = value.Number;
                    if (b == Unset) b = value.Number;
                    if (l == Unset) l = value.Number;
                }
                else if (key == top && value.Kind == Kind.Number) t = value.Number;
                else if (key == right && value.Kind == Kind.Number) r = value.Number;
                else if (key == bottom && value.Kind == Kind.Number) b = value.Number;
                else if (key == left && value.Kind == Kind.Number) l = value.Number;
                else continue;
                any = true;
            }
            if (!any) return;
            void Member(string key, float number)
            {
                if (number == Unset) return;
                var member = new Value { Kind = Kind.Number, Number = number };
                int at = values.FindIndex(v => v.Key == key && v.Value.Kind == Kind.Number);
                if (at >= 0) values[at] = (key, member);
                else values.Add((key, member));
            }
            Member(top, t);
            Member(right, r);
            Member(bottom, b);
            Member(left, l);
        }

        // ---- GUI styles --------------------------------------------------------------------

        /// <summary>The properties of GUISkin that are styles, by lower-case name, with the name
        /// of their block: the property's name with a capital first letter.</summary>
        static readonly Dictionary<string, string> s_SkinStyleBlocks = SkinStyleBlocks();

        static Dictionary<string, string> SkinStyleBlocks()
        {
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in typeof(GUISkin).GetProperties())
            {
                if (property.PropertyType != typeof(GUIStyle)) continue;
                blocks[property.Name.ToLower()] = char.ToUpper(property.Name[0]) + property.Name.Substring(1);
            }
            return blocks;
        }

        /// <summary>The block a GUI style of a name takes its values from.</summary>
        internal static string BlockNameOfStyle(string styleName)
        {
            styleName ??= "";
            if (s_SkinStyleBlocks.TryGetValue(styleName, out var block)) styleName = block;
            return styleName.Replace(" ", "-").Replace(".", "-");
        }

        /// <summary>
        /// Gives the styles of a skin that have a block the block's values. Such a style is drawn
        /// from the catalog and no longer has background textures.
        /// </summary>
        internal void ApplyTo(GUISkin skin)
        {
            foreach (var style in skin.AllStyles())
            {
                if (style == null) continue;
                string name = BlockNameOfStyle(style.name);
                if (Find(name) == null) continue;
                ApplyTo(style, name);
                foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
                    state.ClearBackgrounds();
            }
        }

        internal void ApplyTo(GUIStyle style, string blockName)
        {
            var block = Find(blockName) ?? new Block(blockName);
            style.name = block.GetText("-unity-name", style.name);
            style.fixedWidth = block.GetFloat("width", style.fixedWidth);
            style.fixedHeight = block.GetFloat("height", style.fixedHeight);
            ReadOffset(block, "margin", style.margin);
            ReadOffset(block, "padding", style.padding);
            style.stretchHeight = block.GetBool("-unity-stretch-height", style.stretchHeight);
            style.stretchWidth = block.GetBool("-unity-stretch-width", style.stretchWidth);
            ReadOffset(block, "-unity-slice", style.border);
            ReadOffset(block, "-unity-overflow", style.overflow);
            // Two numbers, x and y, which the catalog holds as the first two sides of a rect.
            if (block.TryGet("-unity-content-offset", Kind.Rect, out var offset))
                style.contentOffset = new Vector2(offset.Top, offset.Right);
            style.font = block.GetResource("-unity-font", style.font);
            style.font = block.GetResource("font", style.font);
            if (style.fontSize == 0 || block.Has("font-size", Kind.Number))
                style.fontSize = block.GetInt("font-size", style.fontSize);
            string slant = block.GetText("font-style"), weight = block.GetText("font-weight");
            if (slant == "italic") style.fontStyle = weight == "bold" ? FontStyle.BoldAndItalic : FontStyle.Italic;
            else if (weight == "bold") style.fontStyle = FontStyle.Bold;
            else if (slant == "normal") style.fontStyle = FontStyle.Normal;
            if (block.TryGet("-unity-image-position", Kind.Text, out var image))
                style.imagePosition = image.Text switch
                {
                    "image-left" => ImagePosition.ImageLeft,
                    "image-only" => ImagePosition.ImageOnly,
                    "text-only" => ImagePosition.TextOnly,
                    _ => ImagePosition.ImageAbove,
                };
            if (block.TryGet("-unity-clipping", Kind.Text, out var clipping))
                style.clipping = clipping.Text == "overflow" ? TextClipping.Overflow : TextClipping.Clip;
            if (block.TryGet("-unity-text-align", Kind.Text, out var align))
                style.alignment = align.Text switch
                {
                    "lower-left" => TextAnchor.LowerLeft,
                    "lower-right" => TextAnchor.LowerRight,
                    "upper-center" => TextAnchor.UpperCenter,
                    "upper-left" => TextAnchor.UpperLeft,
                    "upper-right" => TextAnchor.UpperRight,
                    "middle-center" => TextAnchor.MiddleCenter,
                    "middle-left" => TextAnchor.MiddleLeft,
                    "middle-right" => TextAnchor.MiddleRight,
                    _ => TextAnchor.LowerCenter,
                };
            style.richText = block.GetBool("-unity-rich-text", style.richText);
            style.wordWrap = block.GetBool("-unity-word-wrap", style.wordWrap);

            // The text colour of a state without a background of its own is the block's, or the
            // theme's; an on state takes after its off state.
            Color fallback = block.GetColor("color", Root.GetColor("--unity-text-color", default));
            ReadState(block, style.normal, fallback);
            ReadState(block.In(State.Hover), style.hover, fallback);
            ReadState(block.In(State.Focus), style.focused, fallback);
            ReadState(block.In(State.Active | State.Hover), style.active, fallback);
            ReadState(block.In(State.Active), style.active, null);
            ReadState(block.In(State.Checked), style.onNormal, style.normal.textColor);
            ReadState(block.In(State.Checked | State.Hover), style.onHover, style.hover.textColor);
            ReadState(block.In(State.Active | State.Checked), style.onActive, style.active.textColor);
            ReadState(block.In(State.Active | State.Checked | State.Hover), style.onActive, null);
            ReadState(block.In(State.Checked | State.Focus), style.onFocused, style.focused.textColor);
            ReadState(block.In(State.Checked | State.Focus | State.Hover), style.onFocused, null);
        }

        static void ReadOffset(Block block, string property, RectOffset offset)
        {
            offset.left = block.GetInt(property + "-left", offset.left);
            offset.right = block.GetInt(property + "-right", offset.right);
            offset.top = block.GetInt(property + "-top", offset.top);
            offset.bottom = block.GetInt(property + "-bottom", offset.bottom);
        }

        static void ReadState(Block block, GUIStyleState state, Color? fallback)
        {
            var background = block.GetResource<Texture2D>("background-image", null);
            if (background != null) state.background = background;
            var scaled = block.GetResource<Texture2D>("-unity-scaled-backgrounds", null);
            if (scaled != null) state.scaledBackgrounds = new[] { scaled };
            state.textColor = block.GetColor("color", state.background != null || fallback == null ? state.textColor : fallback.Value);
        }
    }
}
