using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace Unbound.UIElements
{
    /// <summary>
    /// Computes the styles of a panel's elements as Unity's style updater does: the style sheets of an
    /// element and its ancestors form a stack (imports before their sheet); selectors of every sheet
    /// match right to left against the element, its pseudo states and its ancestors; matches apply in
    /// order of default (theme) sheets first, then specificity, then nearness of the sheet, then order
    /// in the sheet; custom properties of matched rules define variables for the element and its
    /// descendants; inline rules (UXML style attributes) and code-set inline values apply last.
    /// </summary>
    internal sealed class StyleResolver
    {
        struct Match
        {
            public StyleSheet Sheet;
            public int SheetIndex;
            public StyleComplexSelector Selector;
        }

        readonly List<StyleSheet> m_Stack = new List<StyleSheet>();
        readonly List<Match> m_Matches = new List<Match>();
        readonly UssReader m_Reader = new UssReader();
        readonly HashSet<StyleSheet> m_Seen = new HashSet<StyleSheet>();

        /// <summary>Restyles the dirty parts of the tree under root.</summary>
        internal void Update(VisualElement root, float pixelsPerPoint)
        {
            if (!root.uaStyleDirty && !root.uaStyleDirtyDescendants) return;
            m_Stack.Clear();
            // Sheets of the root's ancestors (none for a panel root).
            Traverse(root, VariableContext.none, false, pixelsPerPoint);
        }

        void Traverse(VisualElement element, VariableContext inheritedVariables, bool forced, float pixelsPerPoint)
        {
            int before = m_Stack.Count;
            var sheets = element.styleSheetList;
            if (sheets != null)
            {
                foreach (var sheet in sheets)
                {
                    if (sheet == null) continue;
                    sheet.EnsureReferences();
                    if (sheet.flattenedRecursiveImports != null)
                        foreach (var imported in sheet.flattenedRecursiveImports)
                            if (imported != null)
                            {
                                imported.EnsureReferences();
                                m_Stack.Add(imported);
                            }
                    m_Stack.Add(sheet);
                }
            }
            bool restyle = forced || element.uaStyleDirty;
            var variables = inheritedVariables;
            if (restyle)
            {
                variables = Compute(element, before - 1, inheritedVariables, pixelsPerPoint);
                element.uaStyleDirty = false;
            }
            else
            {
                variables = element.uaVariables ?? inheritedVariables;
            }
            if (restyle || element.uaStyleDirtyDescendants)
            {
                int count = element.hierarchy.childCount;
                for (int i = 0; i < count; i++) Traverse(element.hierarchy[i], variables, restyle, pixelsPerPoint);
            }
            element.uaStyleDirtyDescendants = false;
            if (m_Stack.Count > before) m_Stack.RemoveRange(before, m_Stack.Count - before);
        }

        VariableContext Compute(VisualElement element, int parentSheetIndex, VariableContext inheritedVariables, float pixelsPerPoint)
        {
            FindMatches(element, parentSheetIndex);
            m_Matches.Sort(Compare);

            var variables = inheritedVariables;
            bool hasCustom = false;
            foreach (var m in m_Matches)
                if (m.Selector.rule != null && m.Selector.rule.customPropertiesCount > 0) { hasCustom = true; break; }
            if (hasCustom)
            {
                variables = new VariableContext(inheritedVariables);
                foreach (var m in m_Matches)
                {
                    var rule = m.Selector.rule;
                    if (rule == null || rule.customPropertiesCount == 0) continue;
                    foreach (var p in rule.properties)
                        if (p.isCustomProperty) variables.Add(p.name, m.Sheet, p.values);
                }
            }
            element.uaVariables = variables;

            var parent = element.hierarchy.parent;
            var parentStyle = parent?.computedValues;
            var style = ComputedValues.CreateFor(parentStyle);
            float dpi = pixelsPerPoint;
            foreach (var m in m_Matches)
            {
                if (m.Selector.rule == null) continue;
                m_Reader.SetContext(m.Sheet, m.Selector.rule.properties, variables, dpi);
                Apply(style, m_Reader, parentStyle);
            }
            FinalizeApply(style, parentStyle);
            if (element.uaInlineRule != null && element.uaInlineSheet != null)
            {
                m_Reader.SetContext(element.uaInlineSheet, element.uaInlineRule.properties, variables, dpi);
                Apply(style, m_Reader, parentStyle);
                FinalizeApply(style, parentStyle);
            }
            element.uaInlineStyle?.ApplyTo(style);
            if (element.uaInlineStyle != null) FinalizeApply(style, parentStyle);
            style.dpiScaling = dpi;
            element.SetComputedValues(style);
            m_Matches.Clear();
            return variables;
        }

        static int Compare(Match a, Match b)
        {
            if (a.Sheet.isDefaultStyleSheet != b.Sheet.isDefaultStyleSheet) return a.Sheet.isDefaultStyleSheet ? -1 : 1;
            int c = a.Selector.specificity.CompareTo(b.Selector.specificity);
            if (c == 0) c = a.SheetIndex.CompareTo(b.SheetIndex);
            if (c == 0) c = a.Selector.orderInStyleSheet.CompareTo(b.Selector.orderInStyleSheet);
            return c;
        }

        void FindMatches(VisualElement element, int parentSheetIndex)
        {
            m_Seen.Clear();
            string typeName = element.typeName;
            string name = element.name;
            var classes = element.uaClassList;
            bool isRootForSome = false;
            for (int i = m_Stack.Count - 1; i >= 0; i--)
            {
                var sheet = m_Stack[i];
                if (!m_Seen.Add(sheet)) continue;
                bool root = i > parentSheetIndex;
                if (root) element.pseudoStatesNoNotify |= PseudoStates.Root;
                else element.pseudoStatesNoNotify &= ~PseudoStates.Root;
                isRootForSome |= root;
                var tables = sheet.tables;
                if (tables == null) continue;
                if ((sheet.nonEmptyTablesMask & (1 << (int)StyleSheet.OrderedSelectorType.Type)) != 0 &&
                    tables[(int)StyleSheet.OrderedSelectorType.Type].TryGetValue(typeName, out var first))
                    TestList(element, first, sheet, i);
                if (!string.IsNullOrEmpty(name) && (sheet.nonEmptyTablesMask & (1 << (int)StyleSheet.OrderedSelectorType.Name)) != 0 &&
                    tables[(int)StyleSheet.OrderedSelectorType.Name].TryGetValue(name, out first))
                    TestList(element, first, sheet, i);
                if ((sheet.nonEmptyTablesMask & (1 << (int)StyleSheet.OrderedSelectorType.Class)) != 0)
                {
                    foreach (var cls in classes)
                        if (tables[(int)StyleSheet.OrderedSelectorType.Class].TryGetValue(cls, out first))
                            TestList(element, first, sheet, i);
                }
                if (root) TestList(element, sheet.firstRootSelector, sheet, i);
                TestList(element, sheet.firstWildCardSelector, sheet, i);
            }
            if (isRootForSome) element.pseudoStatesNoNotify &= ~PseudoStates.Root;
        }

        void TestList(VisualElement element, StyleComplexSelector selector, StyleSheet sheet, int index)
        {
            for (; selector != null; selector = selector.nextInTable)
                if (MatchRightToLeft(element, selector))
                    m_Matches.Add(new Match { Sheet = sheet, SheetIndex = index, Selector = selector });
        }

        internal static bool MatchesSelector(VisualElement element, StyleSelector selector)
        {
            foreach (var part in selector.parts)
            {
                switch (part.type)
                {
                    case StyleSelectorType.Class:
                        if (!element.ClassListContains(part.value)) return false;
                        break;
                    case StyleSelectorType.ID:
                        if (!string.Equals(element.name, part.value, StringComparison.Ordinal)) return false;
                        break;
                    case StyleSelectorType.Type:
                        if (!string.Equals(element.typeName, part.value, StringComparison.Ordinal)) return false;
                        break;
                    case StyleSelectorType.Predicate:
                        if (!(part.tempData is Func<VisualElement, bool> predicate && predicate(element))) return false;
                        break;
                    case StyleSelectorType.Wildcard:
                    case StyleSelectorType.PseudoClass:
                        break;
                    default:
                        return false;
                }
            }
            var states = (int)element.pseudoStates;
            if (selector.pseudoStateMask != 0 && (selector.pseudoStateMask & states) != selector.pseudoStateMask) return false;
            if (selector.negatedPseudoStateMask != 0 && (selector.negatedPseudoStateMask & ~states) != selector.negatedPseudoStateMask) return false;
            return true;
        }

        internal static bool MatchRightToLeft(VisualElement element, StyleComplexSelector complex)
        {
            var current = element;
            int index = complex.selectors.Length - 1;
            VisualElement retryElement = null;
            int retryIndex = -1;
            while (index >= 0 && current != null)
            {
                bool success = MatchesSelector(current, complex.selectors[index]);
                if (!success)
                {
                    if (index < complex.selectors.Length - 1 && complex.selectors[index + 1].previousRelationship == StyleSelectorRelationship.Descendent)
                    {
                        current = current.parent;
                        continue;
                    }
                    if (retryElement != null)
                    {
                        current = retryElement;
                        index = retryIndex;
                        continue;
                    }
                    break;
                }
                if (index < complex.selectors.Length - 1 && complex.selectors[index + 1].previousRelationship == StyleSelectorRelationship.Descendent)
                {
                    retryElement = current.parent;
                    retryIndex = index;
                }
                if (--index < 0) return true;
                current = current.parent;
            }
            return false;
        }

        // ---- applying properties ----

        static void Apply(ComputedValues style, UssReader r, ComputedValues parent)
        {
            for (var id = r.propertyId; r.property != null; id = r.MoveNextProperty())
            {
                if (r.valueCount == 0) continue;
                var first = r.GetValue(0).handle;
                if (first.valueType == StyleValueType.Keyword)
                {
                    var keyword = (StyleValueKeyword)first.valueIndex;
                    if (keyword == StyleValueKeyword.Initial)
                    {
                        if (id == StylePropertyId.Custom) RemoveCustom(style, r);
                        else if (id == StylePropertyId.All) style.SetAllInitial();
                        else style.ApplyInitial(id);
                        continue;
                    }
                    if (keyword == StyleValueKeyword.Unset)
                    {
                        if (id == StylePropertyId.Custom) RemoveCustom(style, r);
                        else if (id == StylePropertyId.All) style.SetAllInitial();
                        else style.ApplyUnset(id, parent);
                        continue;
                    }
                }
                switch (id)
                {
                    case StylePropertyId.Custom:
                        style.customProperties ??= new Dictionary<string, UssValue>();
                        style.customProperties[r.property.name] = r.GetValue(0);
                        style.dpiScaling = r.dpiScaling;
                        break;
                    case StylePropertyId.Unknown:
                    case StylePropertyId.All:
                        break;
                    case StylePropertyId.Margin:
                        BoxArea(r, out style.marginTop, out style.marginRight, out style.marginBottom, out style.marginLeft);
                        break;
                    case StylePropertyId.Padding:
                        BoxArea(r, out style.paddingTop, out style.paddingRight, out style.paddingBottom, out style.paddingLeft);
                        break;
                    case StylePropertyId.BorderWidth:
                    {
                        BoxArea(r, out Length t, out Length rt, out Length b, out Length l);
                        style.borderTopWidth = t.value;
                        style.borderRightWidth = rt.value;
                        style.borderBottomWidth = b.value;
                        style.borderLeftWidth = l.value;
                        break;
                    }
                    case StylePropertyId.BorderRadius:
                    {
                        BoxArea(r, out Length t, out Length rt, out Length b, out Length l);
                        style.borderTopLeftRadius = t.IsAuto() || t.IsNone() ? new Length(0f) : t;
                        style.borderTopRightRadius = rt.IsAuto() || rt.IsNone() ? new Length(0f) : rt;
                        style.borderBottomRightRadius = b.IsAuto() || b.IsNone() ? new Length(0f) : b;
                        style.borderBottomLeftRadius = l.IsAuto() || l.IsNone() ? new Length(0f) : l;
                        break;
                    }
                    case StylePropertyId.BorderColor:
                        BoxColors(r, out style.borderTopColor, out style.borderRightColor, out style.borderBottomColor, out style.borderLeftColor);
                        break;
                    case StylePropertyId.Flex:
                        Flex(r, out style.flexGrow, out style.flexShrink, out style.flexBasis);
                        break;
                    case StylePropertyId.BackgroundPosition:
                        BackgroundPositionShorthand(r, out style.backgroundPositionX, out style.backgroundPositionY);
                        break;
                    case StylePropertyId.UnityBackgroundScaleMode:
                    {
                        var mode = (ScaleMode)r.ReadEnum(StyleEnumType.ScaleMode, 0);
                        style.backgroundPositionX = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(mode);
                        style.backgroundPositionY = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(mode);
                        style.backgroundRepeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat(mode);
                        style.backgroundSize = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize(mode);
                        break;
                    }
                    case StylePropertyId.UnityTextOutline:
                    {
                        Color color = Color.clear;
                        float width = 0;
                        for (int i = 0; i < r.valueCount; i++)
                        {
                            var type = r.GetValueType(i);
                            if (type == StyleValueType.Dimension) width = r.ReadFloat(i);
                            else if (type == StyleValueType.Color || type == StyleValueType.Enum) color = r.ReadColor(i);
                        }
                        style.unityTextOutlineColor = color;
                        style.unityTextOutlineWidth = width;
                        break;
                    }
                    case StylePropertyId.Transition:
                        Transition(style, r);
                        break;
                    default:
                        if (!style.ApplyLonghand(id, r)) Debug.LogAssertion($"Unknown property id {id}");
                        break;
                }
            }
        }

        static void RemoveCustom(ComputedValues style, UssReader r) => style.customProperties?.Remove(r.property.name);

        internal static void FinalizeApply(ComputedValues style, ComputedValues parent)
        {
            if (style.fontSize.unit == LengthUnit.Percent && !style.fontSize.IsAuto() && !style.fontSize.IsNone())
            {
                float parentSize = (parent ?? ComputedValues.Initial).fontSize.value;
                style.fontSize = new Length(parentSize * style.fontSize.value / 100f);
            }
        }

        static void BoxArea(UssReader r, out Length top, out Length right, out Length bottom, out Length left)
        {
            top = right = bottom = left = new Length(0f);
            switch (r.valueCount)
            {
                case 0: break;
                case 1: top = right = bottom = left = r.ReadLength(0); break;
                case 2: top = bottom = r.ReadLength(0); left = right = r.ReadLength(1); break;
                case 3: top = r.ReadLength(0); left = right = r.ReadLength(1); bottom = r.ReadLength(2); break;
                default: top = r.ReadLength(0); right = r.ReadLength(1); bottom = r.ReadLength(2); left = r.ReadLength(3); break;
            }
        }

        static void BoxColors(UssReader r, out Color top, out Color right, out Color bottom, out Color left)
        {
            top = right = bottom = left = Color.clear;
            switch (r.valueCount)
            {
                case 0: break;
                case 1: top = right = bottom = left = r.ReadColor(0); break;
                case 2: top = bottom = r.ReadColor(0); left = right = r.ReadColor(1); break;
                case 3: top = r.ReadColor(0); left = right = r.ReadColor(1); bottom = r.ReadColor(2); break;
                default: top = r.ReadColor(0); right = r.ReadColor(1); bottom = r.ReadColor(2); left = r.ReadColor(3); break;
            }
        }

        static void Flex(UssReader r, out float grow, out float shrink, out Length basis)
        {
            grow = 0f;
            shrink = 1f;
            basis = Length.Auto();
            int count = r.valueCount;
            if (count == 1 && r.IsValueType(0, StyleValueType.Keyword))
            {
                if (r.IsKeyword(0, StyleValueKeyword.None)) { grow = 0f; shrink = 0f; basis = Length.Auto(); }
                else if (r.IsKeyword(0, StyleValueKeyword.Auto)) { grow = 1f; shrink = 1f; basis = Length.Auto(); }
                return;
            }
            if (count > 3) return;
            grow = 0f;
            shrink = 1f;
            basis = Length.Percent(0f);
            bool ok = true, hasGrow = false, hasBasis = false;
            for (int i = 0; i < count && ok; i++)
            {
                var type = r.GetValueType(i);
                if (type == StyleValueType.Dimension || type == StyleValueType.Keyword)
                {
                    if (hasBasis) { ok = false; break; }
                    hasBasis = true;
                    if (type == StyleValueType.Keyword) { if (r.IsKeyword(i, StyleValueKeyword.Auto)) basis = Length.Auto(); }
                    else basis = r.ReadLength(i);
                    if (hasGrow && i != count - 1) ok = false;
                }
                else if (type == StyleValueType.Float)
                {
                    float f = r.ReadFloat(i);
                    if (!hasGrow) { hasGrow = true; grow = f; }
                    else shrink = f;
                }
                else ok = false;
            }
        }

        static void BackgroundPositionShorthand(UssReader r, out BackgroundPosition x, out BackgroundPosition y)
        {
            x = default;
            y = default;
            int count = r.valueCount;
            var v1 = r.GetValue(0);
            var v2 = count > 1 ? r.GetValue(1) : default;
            var v3 = count > 2 ? r.GetValue(2) : default;
            var v4 = count > 3 ? r.GetValue(3) : default;
            bool Num(UssValue v) => v.handle.valueType == StyleValueType.Dimension || v.handle.valueType == StyleValueType.Float;
            BackgroundPositionKeyword K(int i) => (BackgroundPositionKeyword)r.ReadEnum(StyleEnumType.BackgroundPositionKeyword, i);
            switch (count)
            {
                case 1:
                    switch (K(0))
                    {
                        case BackgroundPositionKeyword.Left: x = new BackgroundPosition(BackgroundPositionKeyword.Left); y = BackgroundPosition.Initial(); break;
                        case BackgroundPositionKeyword.Right: x = new BackgroundPosition(BackgroundPositionKeyword.Right); y = BackgroundPosition.Initial(); break;
                        case BackgroundPositionKeyword.Top: x = BackgroundPosition.Initial(); y = new BackgroundPosition(BackgroundPositionKeyword.Top); break;
                        case BackgroundPositionKeyword.Bottom: x = BackgroundPosition.Initial(); y = new BackgroundPosition(BackgroundPositionKeyword.Bottom); break;
                        case BackgroundPositionKeyword.Center: x = new BackgroundPosition(BackgroundPositionKeyword.Center); y = new BackgroundPosition(BackgroundPositionKeyword.Center); break;
                    }
                    break;
                case 2:
                    if (Num(v1))
                    {
                        x = new BackgroundPosition(BackgroundPositionKeyword.Left, v1.sheet.ReadDimension(v1.handle).ToLength());
                        y = new BackgroundPosition(BackgroundPositionKeyword.Top, v2.sheet.ReadDimension(v2.handle).ToLength());
                    }
                    else if (v1.handle.valueType == StyleValueType.Enum && v2.handle.valueType == StyleValueType.Enum)
                    {
                        var a = K(0);
                        var b = K(1);
                        void Swap() { var t = a; a = b; b = t; }
                        if (b == BackgroundPositionKeyword.Left) Swap();
                        if (b == BackgroundPositionKeyword.Right) Swap();
                        if (a == BackgroundPositionKeyword.Top) Swap();
                        if (a == BackgroundPositionKeyword.Bottom) Swap();
                        x = new BackgroundPosition(a);
                        y = new BackgroundPosition(b);
                    }
                    break;
                case 3:
                    if (v1.handle.valueType == StyleValueType.Enum && v2.handle.valueType == StyleValueType.Enum && v3.handle.valueType == StyleValueType.Dimension)
                    {
                        x = new BackgroundPosition(K(0));
                        y = new BackgroundPosition(K(1), r.ReadLength(2));
                    }
                    else if (v1.handle.valueType == StyleValueType.Enum && v2.handle.valueType == StyleValueType.Dimension && v3.handle.valueType == StyleValueType.Enum)
                    {
                        x = new BackgroundPosition(K(0), r.ReadLength(1));
                        y = new BackgroundPosition(K(2));
                    }
                    break;
                case 4:
                    if (v1.handle.valueType == StyleValueType.Enum && v2.handle.valueType == StyleValueType.Dimension && v3.handle.valueType == StyleValueType.Enum && v4.handle.valueType == StyleValueType.Dimension)
                    {
                        x = new BackgroundPosition(K(0), r.ReadLength(1));
                        y = new BackgroundPosition(K(2), r.ReadLength(3));
                    }
                    break;
            }
        }

        static void Transition(ComputedValues style, UssReader r)
        {
            var delays = new List<TimeValue>();
            var durations = new List<TimeValue>();
            var properties = new List<StylePropertyName>();
            var timings = new List<EasingFunction>();
            int count = r.valueCount;
            int i = 0;
            bool ok = true;
            bool sawNone = false;
            int index = 0;
            do
            {
                if (sawNone) { ok = false; break; }
                var property = new StylePropertyName("all");
                var duration = new TimeValue(0f);
                var delay = new TimeValue(0f);
                var timing = new EasingFunction(EasingMode.Ease);
                bool hasProperty = false, hasDuration = false, hasDelay = false, hasTiming = false;
                for (; i < count; i++)
                {
                    var type = r.GetValueType(i);
                    if (type == StyleValueType.CommaSeparator) { i++; break; }
                    if (type == StyleValueType.Keyword && r.IsKeyword(i, StyleValueKeyword.None) && index == 0)
                    {
                        sawNone = true;
                        hasProperty = true;
                        property = new StylePropertyName("none");
                    }
                    else if (type == StyleValueType.Dimension || type == StyleValueType.Float)
                    {
                        var v = r.GetValue(i);
                        var time = v.sheet.ReadDimension(v.handle).ToTime();
                        if (!hasDuration) { duration = time; hasDuration = true; }
                        else if (!hasDelay) { delay = time; hasDelay = true; }
                        else ok = false;
                    }
                    else if (type == StyleValueType.Enum)
                    {
                        var text = r.ReadAsString(i);
                        if (!hasTiming && StyleProperties.TryGetEnumIntValue(StyleEnumType.EasingMode, text, out int mode))
                        {
                            timing = new EasingFunction((EasingMode)mode);
                            hasTiming = true;
                        }
                        else if (!hasProperty)
                        {
                            property = new StylePropertyName(text);
                            hasProperty = true;
                        }
                        else ok = false;
                    }
                    else ok = false;
                }
                properties.Add(property);
                durations.Add(duration);
                delays.Add(delay);
                timings.Add(timing);
                index++;
            }
            while (i < count && ok);
            if (!ok) return;
            style.transitionProperty = properties;
            style.transitionDuration = durations;
            style.transitionDelay = delays;
            style.transitionTimingFunction = timings;
        }
    }

    internal sealed partial class ComputedValues
    {
        void ApplyShorthandInitial(StylePropertyId id)
        {
            var i = Initial;
            switch (id)
            {
                case StylePropertyId.Margin: marginTop = i.marginTop; marginRight = i.marginRight; marginBottom = i.marginBottom; marginLeft = i.marginLeft; break;
                case StylePropertyId.Padding: paddingTop = i.paddingTop; paddingRight = i.paddingRight; paddingBottom = i.paddingBottom; paddingLeft = i.paddingLeft; break;
                case StylePropertyId.BorderWidth: borderTopWidth = i.borderTopWidth; borderRightWidth = i.borderRightWidth; borderBottomWidth = i.borderBottomWidth; borderLeftWidth = i.borderLeftWidth; break;
                case StylePropertyId.BorderColor: borderTopColor = i.borderTopColor; borderRightColor = i.borderRightColor; borderBottomColor = i.borderBottomColor; borderLeftColor = i.borderLeftColor; break;
                case StylePropertyId.BorderRadius: borderTopLeftRadius = i.borderTopLeftRadius; borderTopRightRadius = i.borderTopRightRadius; borderBottomRightRadius = i.borderBottomRightRadius; borderBottomLeftRadius = i.borderBottomLeftRadius; break;
                case StylePropertyId.Flex: flexGrow = i.flexGrow; flexShrink = i.flexShrink; flexBasis = i.flexBasis; break;
                case StylePropertyId.BackgroundPosition: backgroundPositionX = i.backgroundPositionX; backgroundPositionY = i.backgroundPositionY; break;
                case StylePropertyId.UnityBackgroundScaleMode: backgroundPositionX = i.backgroundPositionX; backgroundPositionY = i.backgroundPositionY; backgroundRepeat = i.backgroundRepeat; backgroundSize = i.backgroundSize; break;
                case StylePropertyId.UnityTextOutline: unityTextOutlineColor = i.unityTextOutlineColor; unityTextOutlineWidth = i.unityTextOutlineWidth; break;
                case StylePropertyId.Transition:
                    transitionDelay = new List<TimeValue>(i.transitionDelay);
                    transitionDuration = new List<TimeValue>(i.transitionDuration);
                    transitionProperty = new List<StylePropertyName>(i.transitionProperty);
                    transitionTimingFunction = new List<EasingFunction>(i.transitionTimingFunction);
                    break;
            }
        }
    }
}
