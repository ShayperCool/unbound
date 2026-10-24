using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements.StyleSheets;

namespace UnityEngine.UIElements.StyleSheets
{
    /// <summary>A depth-first, pre-order walk of an element and its hierarchy children.</summary>
    internal abstract partial class HierarchyTraversal
    {
        public virtual void Traverse(VisualElement element) => TraverseRecursive(element, 0);

        public abstract void TraverseRecursive(VisualElement element, int depth);

        protected void Recurse(VisualElement element, int depth)
        {
            int i = 0;
            while (i < element.hierarchy.childCount)
            {
                var child = element.hierarchy[i];
                TraverseRecursive(child, depth + 1);
                // A callback may remove the child: only advance when it is still there.
                if (child.hierarchy.parent == element) i++;
            }
        }
    }
}

namespace UnityEngine.UIElements
{
    internal partial struct RuleMatcher
    {
        public StyleSheet sheet;
        public StyleComplexSelector complexSelector;

        public RuleMatcher(StyleSheet sheet, StyleComplexSelector complexSelector, int styleSheetIndexInStack)
        {
            this.sheet = sheet;
            this.complexSelector = complexSelector;
        }

        public override string ToString() => complexSelector?.ToString() ?? string.Empty;
    }

    /// <summary>Selection of elements of a hierarchy by type, name, classes, pseudo-states and predicates,
    /// matched with the style selector engine against the root and its descendants in depth-first order.</summary>
    public static partial class UQuery
    {
        internal partial interface IVisualPredicateWrapper
        {
            bool Predicate(object e);
        }

        internal partial class IsOfType<T> : IVisualPredicateWrapper where T : VisualElement
        {
            public static IsOfType<T> s_Instance = new IsOfType<T>();

            public bool Predicate(object e) => e is T;
        }

        internal partial class PredicateWrapper<T> : IVisualPredicateWrapper where T : VisualElement
        {
            readonly Func<T, bool> m_Predicate;

            public PredicateWrapper(Func<T, bool> p) { m_Predicate = p; }

            public bool Predicate(object e) => e is T t && m_Predicate(t);
        }

        internal abstract partial class UQueryMatcher : HierarchyTraversal
        {
            internal List<RuleMatcher> m_Matchers;

            public override void Traverse(VisualElement element) => base.Traverse(element);

            protected virtual bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element) => false;

            public override void TraverseRecursive(VisualElement element, int depth)
            {
                int count = m_Matchers.Count;
                for (int i = 0; i < count; i++)
                {
                    var matcher = m_Matchers[i];
                    if (Unbound.UIElements.StyleResolver.MatchRightToLeft(element, matcher.complexSelector) && OnRuleMatchedElement(matcher, element))
                        return;
                }
                Recurse(element, depth);
                if (m_Matchers.Count > count) m_Matchers.RemoveRange(count, m_Matchers.Count - count);
            }

            public virtual void Run(VisualElement root, List<RuleMatcher> matchers)
            {
                m_Matchers = matchers;
                Traverse(root);
            }
        }

        internal abstract partial class SingleQueryMatcher : UQueryMatcher
        {
            public VisualElement match { get; set; }

            public override void Run(VisualElement root, List<RuleMatcher> matchers)
            {
                match = null;
                base.Run(root, matchers);
                m_Matchers = null;
            }

            public bool IsInUse() => m_Matchers != null;

            public abstract SingleQueryMatcher CreateNew();
        }

        internal partial class FirstQueryMatcher : SingleQueryMatcher
        {
            public static readonly FirstQueryMatcher Instance = new FirstQueryMatcher();

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                match ??= element;
                return true;
            }

            public override SingleQueryMatcher CreateNew() => new FirstQueryMatcher();
        }

        internal partial class LastQueryMatcher : SingleQueryMatcher
        {
            public static readonly LastQueryMatcher Instance = new LastQueryMatcher();

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                match = element;
                return false;
            }

            public override SingleQueryMatcher CreateNew() => new LastQueryMatcher();
        }

        internal partial class IndexQueryMatcher : SingleQueryMatcher
        {
            public static readonly IndexQueryMatcher Instance = new IndexQueryMatcher();
            int m_MatchCount = -1;
            int m_MatchIndex;

            public int matchIndex
            {
                get => m_MatchIndex;
                set
                {
                    m_MatchCount = -1;
                    m_MatchIndex = value;
                }
            }

            public override void Run(VisualElement root, List<RuleMatcher> matchers)
            {
                m_MatchCount = -1;
                base.Run(root, matchers);
            }

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                m_MatchCount++;
                if (m_MatchCount == m_MatchIndex) match = element;
                return m_MatchCount >= m_MatchIndex;
            }

            public override SingleQueryMatcher CreateNew() => new IndexQueryMatcher();
        }
    }

    public partial struct UQueryBuilder<T> : IEquatable<UQueryBuilder<T>> where T : VisualElement
    {
        List<StyleSelector> m_StyleSelectors;
        List<StyleSelectorPart> m_Parts;
        VisualElement m_Element;
        List<RuleMatcher> m_Matchers;
        StyleSelectorRelationship m_Relationship;
        int m_PseudoStatesMask;
        int m_NegatedPseudoStatesMask;

        List<StyleSelector> styleSelectors => m_StyleSelectors ??= new List<StyleSelector>();
        List<StyleSelectorPart> parts => m_Parts ??= new List<StyleSelectorPart>();

        public UQueryBuilder(VisualElement visualElement)
        {
            this = default;
            m_Element = visualElement;
            m_Relationship = StyleSelectorRelationship.None;
            m_Matchers = new List<RuleMatcher>();
        }

        public UQueryBuilder<T> Class(string classname)
        {
            AddClass(classname);
            return this;
        }

        public UQueryBuilder<T> Name(string id)
        {
            AddName(id);
            return this;
        }

        public UQueryBuilder<T2> Descendents<T2>(string name = null, params string[] classNames) where T2 : VisualElement
        {
            FinishCurrentSelector();
            AddType<T2>();
            AddName(name);
            AddClasses(classNames);
            return AddRelationship<T2>(StyleSelectorRelationship.Descendent);
        }

        public UQueryBuilder<T2> Descendents<T2>(string name = null, string classname = null) where T2 : VisualElement
        {
            FinishCurrentSelector();
            AddType<T2>();
            AddName(name);
            AddClass(classname);
            return AddRelationship<T2>(StyleSelectorRelationship.Descendent);
        }

        public UQueryBuilder<T2> Children<T2>(string name = null, params string[] classes) where T2 : VisualElement
        {
            FinishCurrentSelector();
            AddType<T2>();
            AddName(name);
            AddClasses(classes);
            return AddRelationship<T2>(StyleSelectorRelationship.Child);
        }

        public UQueryBuilder<T2> Children<T2>(string name = null, string className = null) where T2 : VisualElement
        {
            FinishCurrentSelector();
            AddType<T2>();
            AddName(name);
            AddClass(className);
            return AddRelationship<T2>(StyleSelectorRelationship.Child);
        }

        public UQueryBuilder<T2> OfType<T2>(string name = null, params string[] classes) where T2 : VisualElement
        {
            AddType<T2>();
            AddName(name);
            AddClasses(classes);
            return AddRelationship<T2>(StyleSelectorRelationship.None);
        }

        public UQueryBuilder<T2> OfType<T2>(string name = null, string className = null) where T2 : VisualElement
        {
            AddType<T2>();
            AddName(name);
            AddClass(className);
            return AddRelationship<T2>(StyleSelectorRelationship.None);
        }

        internal UQueryBuilder<T> SingleBaseType()
        {
            parts.Add(StyleSelectorPart.CreatePredicate(UQuery.IsOfType<T>.s_Instance));
            return this;
        }

        public UQueryBuilder<T> Where(Func<T, bool> selectorPredicate)
        {
            parts.Add(StyleSelectorPart.CreatePredicate(new UQuery.PredicateWrapper<T>(selectorPredicate)));
            return this;
        }

        void AddClass(string c)
        {
            if (c != null) parts.Add(StyleSelectorPart.CreateClass(c));
        }

        void AddClasses(params string[] classes)
        {
            if (classes == null) return;
            foreach (var c in classes) AddClass(c);
        }

        void AddName(string id)
        {
            if (id != null) parts.Add(StyleSelectorPart.CreateId(id));
        }

        void AddType<T2>() where T2 : VisualElement
        {
            if (typeof(T2) != typeof(VisualElement)) parts.Add(StyleSelectorPart.CreatePredicate(UQuery.IsOfType<T2>.s_Instance));
        }

        UQueryBuilder<T> AddPseudoState(PseudoStates s)
        {
            m_PseudoStatesMask |= (int)s;
            return this;
        }

        UQueryBuilder<T> AddNegativePseudoState(PseudoStates s)
        {
            m_NegatedPseudoStatesMask |= (int)s;
            return this;
        }

        public UQueryBuilder<T> Active() => AddPseudoState(PseudoStates.Active);
        public UQueryBuilder<T> NotActive() => AddNegativePseudoState(PseudoStates.Active);
        public UQueryBuilder<T> Visible() => Where(e => e.visible);
        public UQueryBuilder<T> NotVisible() => Where(e => !e.visible);
        public UQueryBuilder<T> Hovered() => AddPseudoState(PseudoStates.Hover);
        public UQueryBuilder<T> NotHovered() => AddNegativePseudoState(PseudoStates.Hover);
        public UQueryBuilder<T> Checked() => AddPseudoState(PseudoStates.Checked);
        public UQueryBuilder<T> NotChecked() => AddNegativePseudoState(PseudoStates.Checked);

        [Obsolete("Use Checked() instead")]
        public UQueryBuilder<T> Selected() => AddPseudoState(PseudoStates.Checked);

        [Obsolete("Use NotChecked() instead")]
        public UQueryBuilder<T> NotSelected() => AddNegativePseudoState(PseudoStates.Checked);

        public UQueryBuilder<T> Enabled() => AddNegativePseudoState(PseudoStates.Disabled);
        public UQueryBuilder<T> NotEnabled() => AddPseudoState(PseudoStates.Disabled);
        public UQueryBuilder<T> Focused() => AddPseudoState(PseudoStates.Focus);
        public UQueryBuilder<T> NotFocused() => AddNegativePseudoState(PseudoStates.Focus);

        UQueryBuilder<T2> AddRelationship<T2>(StyleSelectorRelationship relationship) where T2 : VisualElement
        {
            return new UQueryBuilder<T2>(m_Element)
            {
                m_Matchers = m_Matchers,
                m_Parts = m_Parts,
                m_StyleSelectors = m_StyleSelectors,
                m_Relationship = relationship == StyleSelectorRelationship.None ? m_Relationship : relationship,
                m_PseudoStatesMask = m_PseudoStatesMask,
                m_NegatedPseudoStatesMask = m_NegatedPseudoStatesMask,
            };
        }

        void FinishSelector()
        {
            FinishCurrentSelector();
            if (styleSelectors.Count == 0) return;
            var complex = new StyleComplexSelector { selectors = styleSelectors.ToArray() };
            styleSelectors.Clear();
            m_Matchers.Add(new RuleMatcher { complexSelector = complex });
        }

        bool CurrentSelectorEmpty() =>
            parts.Count == 0 && m_Relationship == StyleSelectorRelationship.None && m_PseudoStatesMask == 0 && m_NegatedPseudoStatesMask == 0;

        void FinishCurrentSelector()
        {
            if (CurrentSelectorEmpty()) return;
            var selector = new StyleSelector { previousRelationship = m_Relationship };
            if (m_PseudoStatesMask != 0 || m_NegatedPseudoStatesMask != 0) parts.Add(new StyleSelectorPart { type = StyleSelectorType.PseudoClass });
            selector.parts = parts.ToArray();
            selector.pseudoStateMask = m_PseudoStatesMask;
            selector.negatedPseudoStateMask = m_NegatedPseudoStatesMask;
            styleSelectors.Add(selector);
            parts.Clear();
            m_PseudoStatesMask = m_NegatedPseudoStatesMask = 0;
        }

        public UQueryState<T> Build()
        {
            FinishSelector();
            if (m_Matchers.Count == 0)
            {
                parts.Add(new StyleSelectorPart { type = StyleSelectorType.Wildcard });
                FinishSelector();
            }
            return new UQueryState<T>(m_Element, m_Matchers);
        }

        public static implicit operator T(UQueryBuilder<T> s) => s.First();

        public static bool operator ==(UQueryBuilder<T> builder1, UQueryBuilder<T> builder2) => builder1.Equals(builder2);

        public static bool operator !=(UQueryBuilder<T> builder1, UQueryBuilder<T> builder2) => !(builder1 == builder2);

        public T First() => Build().First();
        public T Last() => Build().Last();
        public List<T> ToList() => Build().ToList();
        public void ToList(List<T> results) => Build().ToList(results);
        public T AtIndex(int index) => Build().AtIndex(index);
        public void ForEach<T2>(List<T2> result, Func<T, T2> funcCall) => Build().ForEach(result, funcCall);
        public List<T2> ForEach<T2>(Func<T, T2> funcCall) => Build().ForEach(funcCall);
        public void ForEach(Action<T> funcCall) => Build().ForEach(funcCall);

        public bool Equals(UQueryBuilder<T> other) =>
            m_StyleSelectors == other.m_StyleSelectors && m_Parts == other.m_Parts && m_Element == other.m_Element && m_Matchers == other.m_Matchers &&
            m_Relationship == other.m_Relationship && m_PseudoStatesMask == other.m_PseudoStatesMask && m_NegatedPseudoStatesMask == other.m_NegatedPseudoStatesMask;

        public override bool Equals(object obj) => obj is UQueryBuilder<T> other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(m_StyleSelectors, m_Parts, m_Element, m_Matchers, m_Relationship, m_PseudoStatesMask, m_NegatedPseudoStatesMask);
    }

    public partial struct UQueryState<T> : IEnumerable<T>, IEquatable<UQueryState<T>> where T : VisualElement
    {
        sealed class ListQueryMatcher<TElement> : UQuery.UQueryMatcher where TElement : VisualElement
        {
            public List<TElement> matches { get; set; }

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                matches.Add(element as TElement);
                return false;
            }
        }

        sealed class ActionQueryMatcher : UQuery.UQueryMatcher
        {
            internal Action<T> callBack { get; set; }

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                if (element is T t) callBack(t);
                return false;
            }
        }

        sealed class DelegateQueryMatcher<TReturnType> : UQuery.UQueryMatcher
        {
            public Func<T, TReturnType> callBack { get; set; }
            public List<TReturnType> result { get; set; }

            protected override bool OnRuleMatchedElement(RuleMatcher matcher, VisualElement element)
            {
                if (element is T t) result.Add(callBack(t));
                return false;
            }
        }

        readonly VisualElement m_Element;
        internal readonly List<RuleMatcher> m_Matchers;

        internal UQueryState(VisualElement element, List<RuleMatcher> matchers)
        {
            m_Element = element;
            m_Matchers = matchers;
        }

        public UQueryState<T> RebuildOn(VisualElement element) => new UQueryState<T>(element, m_Matchers);

        T Single(UQuery.SingleQueryMatcher matcher)
        {
            if (matcher.IsInUse()) matcher = matcher.CreateNew();
            matcher.Run(m_Element, m_Matchers);
            var result = matcher.match as T;
            matcher.match = null;
            return result;
        }

        public T First() => Single(UQuery.FirstQueryMatcher.Instance);

        public T Last() => Single(UQuery.LastQueryMatcher.Instance);

        public void ToList(List<T> results)
        {
            new ListQueryMatcher<T> { matches = results }.Run(m_Element, m_Matchers);
        }

        public List<T> ToList()
        {
            var list = new List<T>();
            ToList(list);
            return list;
        }

        public T AtIndex(int index)
        {
            var matcher = new UQuery.IndexQueryMatcher { matchIndex = index };
            return Single(matcher);
        }

        public void ForEach(Action<T> funcCall) => new ActionQueryMatcher { callBack = funcCall }.Run(m_Element, m_Matchers);

        public void ForEach<T2>(List<T2> result, Func<T, T2> funcCall) =>
            new DelegateQueryMatcher<T2> { callBack = funcCall, result = result }.Run(m_Element, m_Matchers);

        public List<T2> ForEach<T2>(Func<T, T2> funcCall)
        {
            var result = new List<T2>();
            ForEach(result, funcCall);
            return result;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(UQueryState<T> other) => m_Element == other.m_Element && m_Matchers == other.m_Matchers;

        public override bool Equals(object obj) => obj is UQueryState<T> other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(m_Element, m_Matchers);

        public static bool operator ==(UQueryState<T> state1, UQueryState<T> state2) => state1.Equals(state2);

        public static bool operator !=(UQueryState<T> state1, UQueryState<T> state2) => !(state1 == state2);

        public partial struct Enumerator : IEnumerator<T>
        {
            List<VisualElement> m_List;
            int m_Index;

            internal Enumerator(UQueryState<T> queryState)
            {
                m_List = new List<VisualElement>();
                new ListQueryMatcher<VisualElement> { matches = m_List }.Run(queryState.m_Element, queryState.m_Matchers);
                m_Index = -1;
            }

            public T Current => (T)m_List[m_Index];

            object IEnumerator.Current => Current;

            public bool MoveNext() => ++m_Index < m_List.Count;

            public void Reset() => m_Index = -1;

            public void Dispose() => m_List = null;
        }
    }

    public static partial class UQueryExtensions
    {
        public static T Q<T>(this VisualElement e, string name = null, params string[] classes) where T : VisualElement => e.Query<T>(name, classes).Build().First();

        public static VisualElement Q(this VisualElement e, string name = null, params string[] classes) => e.Query<VisualElement>(name, classes).Build().First();

        public static T Q<T>(this VisualElement e, string name = null, string className = null) where T : VisualElement
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return e.Query<T>(name, className).Build().First();
        }

        internal static T MandatoryQ<T>(this VisualElement e, string name, string className = null) where T : VisualElement
        {
            var found = e.Q<T>(name, className);
            if (found == null) throw new InvalidOperationException($"Missing element of type {typeof(T).Name} named '{name}'" + (className != null ? $" with class '{className}'" : ""));
            return found;
        }

        public static VisualElement Q(this VisualElement e, string name = null, string className = null)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return e.Query<VisualElement>(name, className).Build().First();
        }

        internal static VisualElement MandatoryQ(this VisualElement e, string name, string className = null) => e.MandatoryQ<VisualElement>(name, className);

        public static UQueryBuilder<VisualElement> Query(this VisualElement e, string name = null, params string[] classes) => e.Query<VisualElement>(name, classes);

        public static UQueryBuilder<VisualElement> Query(this VisualElement e, string name = null, string className = null) => e.Query<VisualElement>(name, className);

        public static UQueryBuilder<T> Query<T>(this VisualElement e, string name = null, params string[] classes) where T : VisualElement
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return new UQueryBuilder<VisualElement>(e).OfType<T>(name, classes);
        }

        public static UQueryBuilder<T> Query<T>(this VisualElement e, string name = null, string className = null) where T : VisualElement
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return new UQueryBuilder<VisualElement>(e).OfType<T>(name, className);
        }

        public static UQueryBuilder<VisualElement> Query(this VisualElement e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return new UQueryBuilder<VisualElement>(e);
        }
    }
}
