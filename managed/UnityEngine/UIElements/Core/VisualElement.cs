using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.UIElements;
using Unbound.UIElements.Layout;

// The visual element: a node of a panel's tree with a name, classes, style sheets, inline style,
// a computed style, a flexbox layout node and event callbacks.

namespace UnityEngine.UIElements
{
    public partial class VisualElement : Focusable, IResolvedStyle, ITransform, IVisualElementScheduler
    {
        public enum MeasureMode
        {
            Undefined = 0,
            Exactly = 1,
            AtMost = 2,
        }

        public static readonly string disabledUssClassName = "unity-disabled";
        internal const string k_RootVisualContainerName = "rootVisualContainer";

        static readonly List<VisualElement> s_EmptyChildren = new List<VisualElement>();
        static readonly List<string> s_EmptyClasses = new List<string>();
        static uint s_NextId;
        static readonly Dictionary<Type, string> s_TypeNames = new Dictionary<Type, string>();

        List<VisualElement> m_Children = s_EmptyChildren;
        VisualElement m_PhysicalParent;
        List<string> m_ClassList = s_EmptyClasses;
        string m_Name;
        PseudoStates m_PseudoStates;
        bool m_EnabledSelf;
        PickingMode m_PickingMode;
        string m_ViewDataKey;
        string m_Tooltip;
        Rect m_ManualLayout;
        bool m_LayoutManual;
        UsageHints m_UsageHints;
        VisualTreeAsset m_VisualTreeAssetSource;
        object m_UserData;
        InlineStyle m_InlineStyle;
        ResolvedStyle m_ResolvedStyle;
        UaCustomStyle m_CustomStyle;
        LanguageDirection m_LanguageDirection;

        internal readonly uint controlid;
        internal BaseVisualElementPanel elementPanel { get; private set; }
        internal List<StyleSheet> styleSheetList;

        // Style state (Unbound).
        internal ComputedValues computedValues = ComputedValues.Initial;
        internal InlineStyle uaInlineStyle => m_InlineStyle;
        internal StyleSheet uaInlineSheet;
        internal StyleRule uaInlineRule;
        internal VariableContext uaVariables;
        internal bool uaStyleDirty = true;

        /// <summary>The geometry the renderer generated for the element when it last repainted,
        /// kept (and moved with the element) until a change repaints it, as UIR keeps meshes.</summary>
        internal Unbound.UIElements.UaRetainedGeometry uaGeometry;
        internal bool uaStyleDirtyDescendants;
        internal bool styleInitialized { get; set; }

        // Layout state.
        internal readonly YogaNode yogaNode;
        internal Rect lastLayout;
        internal Rect lastPseudoPadding;
        bool m_RequireMeasureFunction;
        internal bool isCompositeRoot { get; set; }
        internal bool areAncestorsAndSelfDisplayed { get; set; } = true;
        internal bool disableRendering { get; set; }
        internal bool isRootVisualContainer { get; set; }

        /// <summary>Called when the element is added to a parent (the hierarchy change tracker).</summary>
        internal event Action<VisualElement, int> elementAdded;
        internal event Action<VisualElement> elementRemoved;

        public VisualElement()
        {
            controlid = ++s_NextId;
            hierarchy = new Hierarchy(this);
            m_EnabledSelf = true;
            focusable = false;
            name = string.Empty;
            yogaNode = new YogaNode(UaLayout.DefaultConfig) { Owner = this };
            UaLayout.SyncStyle(this, computedValues);
        }

        // ---- identity ----

        public string name
        {
            get => m_Name;
            set
            {
                if (m_Name == value) return;
                m_Name = value;
                IncrementVersion(VersionChangeType.StyleSheet);
            }
        }

        internal string typeName
        {
            get
            {
                var type = GetType();
                if (!s_TypeNames.TryGetValue(type, out var n))
                {
                    n = type.Name;
                    if (type.IsGenericType)
                    {
                        int tick = n.IndexOf('`');
                        if (tick >= 0) n = n.Remove(tick);
                    }
                    s_TypeNames[type] = n;
                }
                return n;
            }
        }

        internal string fullTypeName => GetType().FullName;

        public string viewDataKey { get => m_ViewDataKey; set => m_ViewDataKey = value; }

        public object userData { get => m_UserData; set => m_UserData = value; }

        public string tooltip { get => m_Tooltip ?? string.Empty; set => m_Tooltip = value; }

        public UsageHints usageHints { get => m_UsageHints; set => m_UsageHints = value; }

        public bool disablePlayModeTint { get; set; }

        public LanguageDirection languageDirection { get => m_LanguageDirection; set => m_LanguageDirection = value; }

        public VisualTreeAsset visualTreeAssetSource { get => m_VisualTreeAssetSource; internal set => m_VisualTreeAssetSource = value; }

        public Action<MeshGenerationContext> generateVisualContent { get; set; }

        public override string ToString() => GetType().Name + " " + name + " " + layout + " world rect: " + worldBound;

        // ---- classes ----

        internal List<string> uaClassList => m_ClassList;

        public IEnumerable<string> GetClasses() => m_ClassList;

        internal List<string> GetClassesForIteration() => m_ClassList;

        public void ClearClassList()
        {
            if (m_ClassList.Count == 0) return;
            m_ClassList = s_EmptyClasses;
            IncrementVersion(VersionChangeType.StyleSheet);
        }

        public void AddToClassList(string className)
        {
            if (string.IsNullOrEmpty(className)) return;
            if (m_ClassList == s_EmptyClasses) m_ClassList = new List<string>();
            else if (m_ClassList.Contains(className)) return;
            m_ClassList.Add(className);
            IncrementVersion(VersionChangeType.StyleSheet);
        }

        public void RemoveFromClassList(string className)
        {
            if (m_ClassList.Remove(className)) IncrementVersion(VersionChangeType.StyleSheet);
        }

        public void ToggleInClassList(string className)
        {
            if (ClassListContains(className)) RemoveFromClassList(className);
            else AddToClassList(className);
        }

        public void EnableInClassList(string className, bool enable)
        {
            if (enable) AddToClassList(className);
            else RemoveFromClassList(className);
        }

        public bool ClassListContains(string cls)
        {
            for (int i = 0; i < m_ClassList.Count; i++)
                if (string.Equals(m_ClassList[i], cls, StringComparison.Ordinal)) return true;
            return false;
        }

        public object FindAncestorUserData()
        {
            for (var p = parent; p != null; p = p.parent)
                if (p.userData != null) return p.userData;
            return null;
        }

        // ---- pseudo states and enabled state ----

        internal PseudoStates pseudoStates
        {
            get => m_PseudoStates;
            set
            {
                var changed = m_PseudoStates ^ value;
                m_PseudoStates = value;
                if (changed == 0) return;
                if ((value & PseudoStates.Root) == PseudoStates.Root) isRootVisualContainer = true;
                if (changed != PseudoStates.Root) IncrementVersion(VersionChangeType.StyleSheet);
            }
        }

        /// <summary>Sets pseudo states without restyling (the style matcher's temporary :root state).</summary>
        internal PseudoStates pseudoStatesNoNotify
        {
            get => m_PseudoStates;
            set => m_PseudoStates = value;
        }

        public bool enabledInHierarchy => (pseudoStates & PseudoStates.Disabled) != PseudoStates.Disabled;

        public bool enabledSelf
        {
            get => m_EnabledSelf;
            set
            {
                if (m_EnabledSelf == value) return;
                m_EnabledSelf = value;
                PropagateEnabledToChildren(value);
            }
        }

        public void SetEnabled(bool value) => enabledSelf = value;

        [Obsolete("SetEnabledFromHierarchy is deprecated and will be removed in a future release. Please use SetEnabled instead.")]
        protected internal bool SetEnabledFromHierarchy(bool state) => SetEnabledFromHierarchyPrivate(state);

        bool isParentEnabledInHierarchy => hierarchy.parent == null || hierarchy.parent.enabledInHierarchy;

        void PropagateEnabledToChildren(bool value)
        {
            if (!SetEnabledFromHierarchyPrivate(value)) return;
            for (int i = 0; i < m_Children.Count; i++) m_Children[i].PropagateEnabledToChildren(value);
        }

        bool SetEnabledFromHierarchyPrivate(bool state)
        {
            bool before = enabledInHierarchy;
            bool disable = false;
            if (state)
            {
                if (isParentEnabledInHierarchy)
                {
                    if (enabledSelf) RemoveFromClassList(disabledUssClassName);
                    else
                    {
                        disable = true;
                        AddToClassList(disabledUssClassName);
                    }
                }
                else
                {
                    disable = true;
                    RemoveFromClassList(disabledUssClassName);
                }
            }
            else
            {
                disable = true;
                EnableInClassList(disabledUssClassName, isParentEnabledInHierarchy);
            }
            if (disable)
            {
                if (focusController != null && focusController.IsFocused(this))
                {
                    using (new UaEventDispatcherGate(panel?.dispatcher))
                        BlurImmediately();
                }
                pseudoStates |= PseudoStates.Disabled;
            }
            else
            {
                pseudoStates &= ~PseudoStates.Disabled;
            }
            return before != enabledInHierarchy;
        }

        public PickingMode pickingMode
        {
            get => m_PickingMode;
            set => m_PickingMode = value;
        }

        public bool visible
        {
            get => resolvedStyle.visibility == Visibility.Visible;
            set => style.visibility = value ? Visibility.Visible : Visibility.Hidden;
        }

        public void MarkDirtyRepaint() => IncrementVersion(VersionChangeType.Repaint);

        // ---- focus ----

        public override FocusController focusController => panel?.focusController;

        public override bool canGrabFocus
        {
            get
            {
                bool ancestorsFocusable = true;
                for (var p = hierarchy.parent; p != null; p = p.parent)
                {
                    if (p.isCompositeRoot)
                    {
                        ancestorsFocusable &= p.canGrabFocus;
                        break;
                    }
                }
                return ancestorsFocusable && visible && resolvedStyle.display != DisplayStyle.None && enabledInHierarchy && base.canGrabFocus;
            }
        }

        public sealed override void Focus()
        {
            if (!canGrabFocus && hierarchy.parent != null) hierarchy.parent.Focus();
            else base.Focus();
        }

        internal VisualElement RetargetElement(VisualElement retargetAgainst)
        {
            if (retargetAgainst == null) return this;
            var root = retargetAgainst.m_PhysicalParent ?? retargetAgainst;
            while (root.m_PhysicalParent != null && !root.isCompositeRoot) root = root.m_PhysicalParent;
            var result = this;
            var p = m_PhysicalParent;
            while (p != null)
            {
                p = p.m_PhysicalParent;
                if (p == root) return result;
                if (p != null && p.isCompositeRoot) result = p;
            }
            return this;
        }

        // ---- panel and events ----

        public IPanel panel => elementPanel;

        public sealed override void SendEvent(EventBase e) => elementPanel?.SendEvent(e, DispatchMode.Default);

        internal sealed override void SendEvent(EventBase e, DispatchMode dispatchMode) => elementPanel?.SendEvent(e, dispatchMode);

        internal sealed override void HandleEvent(EventBase e) => Dispatching.HandleEvent(e, this);

        internal void IncrementVersion(VersionChangeType changeType) => elementPanel?.OnVersionChanged(this, changeType);

        internal void SetPanel(BaseVisualElementPanel p)
        {
            if (elementPanel == p) return;
            var list = new List<VisualElement> { this };
            GatherAllChildren(list);
            var previous = elementPanel;
            using (new UaEventDispatcherGate(p?.dispatcher))
            using (new UaEventDispatcherGate(previous?.dispatcher != null && previous.dispatcher != p?.dispatcher ? previous.dispatcher : null))
            {
                previous?.dispatcher?.m_UaClickDetector.Cleanup(list);
                foreach (var e in list) e.WillChangePanel(p);
                foreach (var e in list)
                {
                    e.elementPanel = p;
                    e.m_NeedsAttach = p != null;
                }
                foreach (var e in list) e.HasChangedPanel(previous);
            }
        }

        bool m_NeedsAttach;

        void GatherAllChildren(List<VisualElement> elements)
        {
            if (m_Children.Count == 0) return;
            int i = elements.Count;
            elements.AddRange(m_Children);
            for (; i < elements.Count; i++) elements.AddRange(elements[i].m_Children);
        }

        void WillChangePanel(BaseVisualElementPanel destination)
        {
            if (elementPanel == null) return;
            elementPanel.OnElementLeavingPanel(this);
            if (!m_NeedsAttach)
            {
                using (var e = DetachFromPanelEvent.GetPooled(elementPanel, destination))
                {
                    e.elementTarget = this;
                    Dispatching.HandleEventAtTargetAndDefaultPhase(e, elementPanel, this);
                }
            }
        }

        void HasChangedPanel(BaseVisualElementPanel previous)
        {
            if (elementPanel != null)
            {
                yogaNode.Config = elementPanel.uaLayoutConfig;
                pseudoStatesNoNotify &= ~(PseudoStates.Active | PseudoStates.Hover | PseudoStates.Focus);
                if (m_NeedsAttach)
                {
                    using (var e = AttachToPanelEvent.GetPooled(previous, elementPanel))
                    {
                        e.elementTarget = this;
                        Dispatching.HandleEventAtTargetAndDefaultPhase(e, elementPanel, this);
                    }
                    m_NeedsAttach = false;
                }
            }
            else
            {
                yogaNode.Config = UaLayout.DefaultConfig;
            }
            styleInitialized = false;
            uaStyleDirty = true;
            uaGeometry = null;
            IncrementVersion(VersionChangeType.Layout | VersionChangeType.StyleSheet | VersionChangeType.Transform);
        }

        // ---- hierarchy ----

        public Hierarchy hierarchy { get; private set; }

        public VisualElement parent => m_PhysicalParent;

        public virtual VisualElement contentContainer => this;

        internal VisualElement physicalParent => m_PhysicalParent;

        public void Add(VisualElement child)
        {
            if (child == null) return;
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't add directly to this VisualElement. Use hierarchy.Add() if you know what you're doing.");
            if (container == this) hierarchy.Add(child);
            else container.Add(child);
            child.m_LogicalParent = this;
        }

        internal VisualElement m_LogicalParent;

        public void Insert(int index, VisualElement element)
        {
            if (element == null) return;
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't insert directly to this VisualElement. Use hierarchy.Insert() if you know what you're doing.");
            if (container == this) hierarchy.Insert(index, element);
            else container.Insert(index, element);
            element.m_LogicalParent = this;
        }

        public void Remove(VisualElement element)
        {
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't remove directly from this VisualElement. Use hierarchy.Remove() if you know what you're doing.");
            if (container == this) hierarchy.Remove(element);
            else container.Remove(element);
        }

        public void RemoveAt(int index)
        {
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't remove directly from this VisualElement. Use hierarchy.RemoveAt() if you know what you're doing.");
            if (container == this) hierarchy.RemoveAt(index);
            else container.RemoveAt(index);
        }

        public void Clear()
        {
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't clear directly this VisualElement. Use hierarchy.Clear() if you know what you're doing.");
            if (container == this) hierarchy.Clear();
            else container.Clear();
        }

        public VisualElement ElementAt(int index) => this[index];

        public VisualElement this[int key]
        {
            get
            {
                var container = contentContainer;
                if (container == null) throw new InvalidOperationException("You can't access directly this VisualElement. Use hierarchy[] if you know what you're doing.");
                return container == this ? hierarchy[key] : container[key];
            }
        }

        public int childCount
        {
            get
            {
                var container = contentContainer;
                if (container == null) return 0;
                return container == this ? hierarchy.childCount : container.childCount;
            }
        }

        public int IndexOf(VisualElement element)
        {
            var container = contentContainer;
            if (container == null) return -1;
            return container == this ? hierarchy.IndexOf(element) : container.IndexOf(element);
        }

        public IEnumerable<VisualElement> Children()
        {
            var container = contentContainer;
            if (container == null) return Array.Empty<VisualElement>();
            return container == this ? hierarchy.Children() : container.Children();
        }

        public void Sort(Comparison<VisualElement> comp)
        {
            var container = contentContainer;
            if (container == null) throw new InvalidOperationException("You can't sort directly this VisualElement. Use hierarchy.Sort() if you know what you're doing.");
            if (container == this) hierarchy.Sort(comp);
            else container.Sort(comp);
        }

        public void BringToFront()
        {
            if (hierarchy.parent == null) return;
            hierarchy.parent.hierarchy.BringToFront(this);
        }

        public void SendToBack()
        {
            if (hierarchy.parent == null) return;
            hierarchy.parent.hierarchy.SendToBack(this);
        }

        public void PlaceBehind(VisualElement sibling)
        {
            if (sibling == null) throw new ArgumentNullException(nameof(sibling));
            if (hierarchy.parent == null || sibling.hierarchy.parent != hierarchy.parent) throw new ArgumentException("VisualElements are not siblings");
            hierarchy.parent.hierarchy.PlaceBehind(this, sibling);
        }

        public void PlaceInFront(VisualElement sibling)
        {
            if (sibling == null) throw new ArgumentNullException(nameof(sibling));
            if (hierarchy.parent == null || sibling.hierarchy.parent != hierarchy.parent) throw new ArgumentException("VisualElements are not siblings");
            hierarchy.parent.hierarchy.PlaceInFront(this, sibling);
        }

        public void RemoveFromHierarchy()
        {
            if (hierarchy.parent != null) hierarchy.parent.hierarchy.Remove(this);
        }

        public T GetFirstOfType<T>() where T : class
        {
            if (this is T t) return t;
            return GetFirstAncestorOfType<T>();
        }

        public T GetFirstAncestorOfType<T>() where T : class
        {
            for (var p = hierarchy.parent; p != null; p = p.hierarchy.parent)
                if (p is T t) return t;
            return null;
        }

        public bool Contains(VisualElement child)
        {
            for (; child != null; child = child.hierarchy.parent)
                if (child.hierarchy.parent == this) return true;
            return false;
        }

        public VisualElement FindCommonAncestor(VisualElement other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (panel != other.panel) return null;
            var a = this;
            int da = 0;
            for (var p = a; p != null; p = p.hierarchy.parent) da++;
            var b = other;
            int db = 0;
            for (var p = b; p != null; p = p.hierarchy.parent) db++;
            while (da > db) { a = a.hierarchy.parent; da--; }
            while (db > da) { b = b.hierarchy.parent; db--; }
            while (a != b)
            {
                a = a.hierarchy.parent;
                b = b.hierarchy.parent;
            }
            return a;
        }

        internal VisualElement UaRoot()
        {
            var e = this;
            while (e.hierarchy.parent != null) e = e.hierarchy.parent;
            return e;
        }

        internal VisualElement UaNextDepthFirst()
        {
            if (m_Children.Count > 0) return m_Children[0];
            var e = this;
            var p = e.hierarchy.parent;
            while (p != null)
            {
                int i = p.m_Children.IndexOf(e);
                if (i + 1 < p.m_Children.Count) return p.m_Children[i + 1];
                e = p;
                p = p.hierarchy.parent;
            }
            return null;
        }

        internal VisualElement UaPreviousDepthFirst()
        {
            var p = hierarchy.parent;
            if (p == null) return null;
            int i = p.m_Children.IndexOf(this);
            if (i == 0) return p;
            var e = p.m_Children[i - 1];
            while (e.m_Children.Count > 0) e = e.m_Children[e.m_Children.Count - 1];
            return e;
        }

        public partial struct Hierarchy : IEquatable<Hierarchy>
        {
            readonly VisualElement m_Owner;

            internal Hierarchy(VisualElement owner) { m_Owner = owner; }

            public VisualElement parent => m_Owner.m_PhysicalParent;

            public void Add(VisualElement child)
            {
                if (child == null) throw new ArgumentException("Cannot add null child");
                Insert(childCount, child);
            }

            public void Insert(int index, VisualElement child)
            {
                if (child == null) throw new ArgumentException("Cannot insert null child");
                if (index > childCount) throw new ArgumentOutOfRangeException("Index out of range: " + index);
                if (child == m_Owner) throw new ArgumentException("Cannot insert element as its own child");
                if (m_Owner.elementPanel != null && m_Owner.elementPanel.duringLayoutPhase) throw new InvalidOperationException("Cannot modify VisualElement hierarchy during layout calculation");
                child.RemoveFromHierarchy();
                if (m_Owner.m_Children == s_EmptyChildren) m_Owner.m_Children = new List<VisualElement>();
                if (m_Owner.m_RequireMeasureFunction) m_Owner.yogaNode.MeasureFunction = null;
                m_Owner.m_Children.Insert(index, child);
                m_Owner.yogaNode.Insert(index, child.yogaNode);
                child.m_PhysicalParent = m_Owner;
                child.m_LogicalParent = m_Owner;
                child.SetPanel(m_Owner.elementPanel);
                child.PropagateEnabledToChildren(m_Owner.enabledInHierarchy);
                child.uaStyleDirty = true;
                child.IncrementVersion(VersionChangeType.Hierarchy);
                m_Owner.IncrementVersion(VersionChangeType.Hierarchy);
                m_Owner.elementAdded?.Invoke(child, index);
            }

            public void Remove(VisualElement child)
            {
                if (child == null) throw new ArgumentException("Cannot remove null child");
                if (child.hierarchy.parent != m_Owner) throw new ArgumentException("This VisualElement is not my child");
                RemoveAt(m_Owner.m_Children.IndexOf(child));
            }

            public void RemoveAt(int index)
            {
                if (m_Owner.elementPanel != null && m_Owner.elementPanel.duringLayoutPhase) throw new InvalidOperationException("Cannot modify VisualElement hierarchy during layout calculation");
                if (index < 0 || index >= childCount) throw new ArgumentOutOfRangeException("Index out of range: " + index);
                var child = m_Owner.m_Children[index];
                m_Owner.m_Children.RemoveAt(index);
                m_Owner.yogaNode.RemoveAt(index);
                child.m_PhysicalParent = null;
                child.m_LogicalParent = null;
                child.SetPanel(null);
                if (m_Owner.m_Children.Count == 0)
                {
                    m_Owner.m_Children = s_EmptyChildren;
                    if (m_Owner.m_RequireMeasureFunction) m_Owner.AssignMeasureFunction();
                }
                m_Owner.IncrementVersion(VersionChangeType.Hierarchy);
                m_Owner.elementRemoved?.Invoke(child);
            }

            public void Clear()
            {
                if (m_Owner.elementPanel != null && m_Owner.elementPanel.duringLayoutPhase) throw new InvalidOperationException("Cannot modify VisualElement hierarchy during layout calculation");
                if (childCount == 0) return;
                var children = new List<VisualElement>(m_Owner.m_Children);
                m_Owner.m_Children = s_EmptyChildren;
                m_Owner.yogaNode.Clear();
                foreach (var child in children)
                {
                    child.m_PhysicalParent = null;
                    child.m_LogicalParent = null;
                    child.SetPanel(null);
                    m_Owner.elementRemoved?.Invoke(child);
                }
                if (m_Owner.m_RequireMeasureFunction) m_Owner.AssignMeasureFunction();
                m_Owner.IncrementVersion(VersionChangeType.Hierarchy);
            }

            internal void BringToFront(VisualElement child)
            {
                if (childCount > 1)
                {
                    int i = m_Owner.m_Children.IndexOf(child);
                    if (i >= 0 && i < childCount - 1) MoveChildElement(child, i, childCount);
                }
            }

            internal void SendToBack(VisualElement child)
            {
                if (childCount > 1)
                {
                    int i = m_Owner.m_Children.IndexOf(child);
                    if (i > 0) MoveChildElement(child, i, 0);
                }
            }

            internal void PlaceBehind(VisualElement child, VisualElement over)
            {
                if (childCount <= 0) return;
                int current = m_Owner.m_Children.IndexOf(child);
                if (current < 0) return;
                int next = m_Owner.m_Children.IndexOf(over);
                if (next > 0 && current < next) next--;
                MoveChildElement(child, current, next);
            }

            internal void PlaceInFront(VisualElement child, VisualElement under)
            {
                if (childCount <= 0) return;
                int current = m_Owner.m_Children.IndexOf(child);
                if (current < 0) return;
                int next = m_Owner.m_Children.IndexOf(under);
                if (current > next) next++;
                MoveChildElement(child, current, next);
            }

            void MoveChildElement(VisualElement child, int currentIndex, int nextIndex)
            {
                m_Owner.m_Children.RemoveAt(currentIndex);
                m_Owner.yogaNode.RemoveAt(currentIndex);
                if (nextIndex > currentIndex) nextIndex--;
                m_Owner.m_Children.Insert(nextIndex, child);
                m_Owner.yogaNode.Insert(nextIndex, child.yogaNode);
                m_Owner.IncrementVersion(VersionChangeType.Hierarchy);
            }

            public int childCount => m_Owner.m_Children.Count;

            public VisualElement this[int key] => m_Owner.m_Children[key];

            public int IndexOf(VisualElement element) => m_Owner.m_Children.IndexOf(element);

            public VisualElement ElementAt(int index) => this[index];

            public IEnumerable<VisualElement> Children() => m_Owner.m_Children;

            internal List<VisualElement> children => m_Owner.m_Children;

            public void Sort(Comparison<VisualElement> comp)
            {
                if (m_Owner.elementPanel != null && m_Owner.elementPanel.duringLayoutPhase) throw new InvalidOperationException("Cannot modify VisualElement hierarchy during layout calculation");
                if (childCount <= 1) return;
                m_Owner.m_Children.Sort(comp);
                m_Owner.yogaNode.Clear();
                for (int i = 0; i < m_Owner.m_Children.Count; i++) m_Owner.yogaNode.Insert(i, m_Owner.m_Children[i].yogaNode);
                m_Owner.IncrementVersion(VersionChangeType.Hierarchy);
            }

            public bool Equals(Hierarchy other) => other == this;
            public override bool Equals(object obj) => obj is Hierarchy h && Equals(h);
            public override int GetHashCode() => m_Owner != null ? m_Owner.GetHashCode() : 0;
            public static bool operator ==(Hierarchy x, Hierarchy y) => ReferenceEquals(x.m_Owner, y.m_Owner);
            public static bool operator !=(Hierarchy x, Hierarchy y) => !(x == y);
        }

        // ---- style sheets and styles ----

        public VisualElementStyleSheetSet styleSheets => new VisualElementStyleSheetSet(this);

        public IStyle style => m_InlineStyle ??= new InlineStyle(this);

        internal bool hasInlineStyle => m_InlineStyle != null;

        public IResolvedStyle resolvedStyle => m_ResolvedStyle ??= new ResolvedStyle(this);

        public ICustomStyle customStyle => m_CustomStyle ??= new UaCustomStyle(this);

        /// <summary>The pointers inside the element (bits by pointer id), kept by enter and leave events.</summary>
        internal int containedPointerIds { get; set; }

        /// <summary>Hovered while a pointer is inside, unless the pointer is captured outside the element's chain.</summary>
        internal void UpdateHoverPseudoState()
        {
            if (containedPointerIds == 0 || panel == null)
            {
                pseudoStates &= ~PseudoStates.Hover;
                return;
            }
            bool hovered = false;
            for (int i = 0; i < PointerId.maxPointers; i++)
            {
                if ((containedPointerIds & (1 << i)) == 0) continue;
                var capturing = elementPanel.GetCapturingElement(i);
                if (capturing == null || capturing == this || Contains(capturing as VisualElement))
                {
                    hovered = true;
                    break;
                }
            }
            if (hovered) pseudoStates |= PseudoStates.Hover;
            else pseudoStates &= ~PseudoStates.Hover;
        }

        internal void UpdateHoverPseudoStateAfterCaptureChange(int pointerId)
        {
            for (var ve = this; ve != null; ve = ve.parent) ve.UpdateHoverPseudoState();
            for (var ve = elementPanel?.GetTopElementUnderPointer(pointerId); ve != null && ve != this; ve = ve.parent) ve.UpdateHoverPseudoState();
        }

        internal void UaStyleChanged()
        {
            uaStyleDirty = true;
            IncrementVersion(VersionChangeType.Styles);
        }

        /// <summary>Applies the inline style to the computed style at once, as Unity's inline style access
        /// does (resolvedStyle reflects a value set from code before the next style update); values
        /// reset to null wait for the next update to fall back to the style sheets.</summary>
        internal void UaApplyInlineNow()
        {
            if (!styleInitialized || m_InlineStyle == null) return;
            var applied = computedValues.Clone();
            m_InlineStyle.ApplyTo(applied);
            Unbound.UIElements.StyleResolver.FinalizeApply(applied, hierarchy.parent?.computedValues);
            var previous = computedValues;
            computedValues = applied;
            UaLayout.SyncStyle(this, applied);
            if (previous.display != applied.display) IncrementVersion(VersionChangeType.Size);
            if (previous.translate != applied.translate || previous.rotate != applied.rotate || previous.scale != applied.scale || previous.transformOrigin != applied.transformOrigin)
                IncrementVersion(VersionChangeType.Transform);
            var rendering = ComputedValues.CompareRenderingChanges(previous, applied);
            if (rendering != 0) IncrementVersion(rendering);
            OnUaStyleResolved(previous, applied);
        }

        /// <summary>Installs a newly computed style: the layout node follows it, and the element repaints.</summary>
        internal void SetComputedValues(ComputedValues style)
        {
            var previous = computedValues;
            computedValues = style;
            styleInitialized = true;
            UaLayout.SyncStyle(this, style);
            if (previous.display != style.display) IncrementVersion(VersionChangeType.Size);
            if (previous.translate != style.translate || previous.rotate != style.rotate || previous.scale != style.scale || previous.transformOrigin != style.transformOrigin)
                IncrementVersion(VersionChangeType.Transform);
            // Only changes that show repaint (Unity's ComputedStyle.CompareChanges): a style that is
            // the same again, as after a scale change, keeps the element's geometry.
            var rendering = ComputedValues.CompareRenderingChanges(previous, style);
            if (rendering != 0) IncrementVersion(rendering);
            if (style.customProperties != null && style.customProperties.Count > 0 || previous.customProperties != null && previous.customProperties.Count > 0)
            {
                using (var e = CustomStyleResolvedEvent.GetPooled())
                {
                    e.elementTarget = this;
                    if (elementPanel != null) Dispatching.HandleEventAtTargetAndDefaultPhase(e, elementPanel, this);
                }
            }
            OnUaStyleResolved(previous, style);
        }

        /// <summary>A hook for elements that cache style-dependent state (text).</summary>
        internal virtual void OnUaStyleResolved(ComputedValues previous, ComputedValues current) { }

        internal StyleFloat ResolveLengthValue(Length length, bool isRow)
        {
            if (length.IsAuto()) return new StyleFloat(StyleKeyword.Auto);
            if (length.IsNone()) return new StyleFloat(StyleKeyword.None);
            if (length.unit != LengthUnit.Percent) return new StyleFloat(length.value);
            var p = hierarchy.parent;
            if (p == null) return 0f;
            float size = isRow ? p.resolvedStyle.width : p.resolvedStyle.height;
            return length.value * size / 100f;
        }

        // ---- scheduler ----

        public IVisualElementScheduler schedule => this;

        IVisualElementScheduledItem IVisualElementScheduler.Execute(Action<TimerState> timerUpdateEvent) =>
            new UaScheduledItem(this, timerUpdateEvent);

        IVisualElementScheduledItem IVisualElementScheduler.Execute(Action updateEvent) =>
            new UaScheduledItem(this, _ => updateEvent());

        internal List<UaScheduledItem> uaScheduledItems;
    }

    public partial struct VisualElementStyleSheetSet : IEquatable<VisualElementStyleSheetSet>
    {
        readonly VisualElement m_Element;

        internal VisualElementStyleSheetSet(VisualElement element) { m_Element = element; }

        public void Add(StyleSheet styleSheet)
        {
            if (styleSheet == null) throw new ArgumentNullException(nameof(styleSheet));
            m_Element.styleSheetList ??= new List<StyleSheet>();
            if (!m_Element.styleSheetList.Contains(styleSheet))
            {
                m_Element.styleSheetList.Add(styleSheet);
                m_Element.IncrementVersion(VersionChangeType.StyleSheet);
            }
        }

        public void Insert(int index, StyleSheet styleSheet)
        {
            if (styleSheet == null) throw new ArgumentNullException(nameof(styleSheet));
            m_Element.styleSheetList ??= new List<StyleSheet>();
            if (m_Element.styleSheetList.Contains(styleSheet)) return;
            m_Element.styleSheetList.Insert(index, styleSheet);
            m_Element.IncrementVersion(VersionChangeType.StyleSheet);
        }

        public void Clear()
        {
            if (m_Element.styleSheetList == null) return;
            m_Element.styleSheetList = null;
            m_Element.IncrementVersion(VersionChangeType.StyleSheet);
        }

        public bool Remove(StyleSheet styleSheet)
        {
            if (styleSheet == null) throw new ArgumentNullException(nameof(styleSheet));
            if (m_Element.styleSheetList != null && m_Element.styleSheetList.Remove(styleSheet))
            {
                if (m_Element.styleSheetList.Count == 0) m_Element.styleSheetList = null;
                m_Element.IncrementVersion(VersionChangeType.StyleSheet);
                return true;
            }
            return false;
        }

        public bool Contains(StyleSheet styleSheet) => m_Element.styleSheetList != null && m_Element.styleSheetList.Contains(styleSheet);

        public int count => m_Element.styleSheetList?.Count ?? 0;

        public StyleSheet this[int index]
        {
            get
            {
                if (m_Element.styleSheetList == null) throw new InvalidOperationException("Accessing an empty style sheet list");
                return m_Element.styleSheetList[index];
            }
        }

        public bool Equals(VisualElementStyleSheetSet other) => Equals(m_Element, other.m_Element);
        public override bool Equals(object obj) => obj is VisualElementStyleSheetSet s && Equals(s);
        public override int GetHashCode() => m_Element != null ? m_Element.GetHashCode() : 0;
        public static bool operator ==(VisualElementStyleSheetSet left, VisualElementStyleSheetSet right) => left.Equals(right);
        public static bool operator !=(VisualElementStyleSheetSet left, VisualElementStyleSheetSet right) => !left.Equals(right);
    }

    public partial interface ITransform
    {
        Vector3 position { get; set; }
        Quaternion rotation { get; set; }
        Vector3 scale { get; set; }
        Matrix4x4 matrix { get; }
    }

    public partial interface ICustomStyle
    {
        bool TryGetValue(CustomStyleProperty<float> property, out float value);
        bool TryGetValue(CustomStyleProperty<int> property, out int value);
        bool TryGetValue(CustomStyleProperty<bool> property, out bool value);
        bool TryGetValue(CustomStyleProperty<Color> property, out Color value);
        bool TryGetValue(CustomStyleProperty<Texture2D> property, out Texture2D value);
        bool TryGetValue(CustomStyleProperty<Sprite> property, out Sprite value);
        bool TryGetValue(CustomStyleProperty<VectorImage> property, out VectorImage value);
        bool TryGetValue<T>(CustomStyleProperty<T> property, out T value) where T : Object;
        bool TryGetValue(CustomStyleProperty<string> property, out string value);
    }

    public partial struct CustomStyleProperty<T> : IEquatable<CustomStyleProperty<T>>
    {
        public string name { get; private set; }

        public CustomStyleProperty(string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName) && !propertyName.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Custom style property \"{propertyName}\" must start with \"--\" prefix.");
            name = propertyName;
        }

        public override bool Equals(object obj) => obj is CustomStyleProperty<T> other && Equals(other);
        public bool Equals(CustomStyleProperty<T> other) => name == other.name;
        public override int GetHashCode() => name != null ? name.GetHashCode() : 0;
        public static bool operator ==(CustomStyleProperty<T> a, CustomStyleProperty<T> b) => a.Equals(b);
        public static bool operator !=(CustomStyleProperty<T> a, CustomStyleProperty<T> b) => !a.Equals(b);
    }

    internal sealed class UaCustomStyle : ICustomStyle
    {
        readonly VisualElement m_Element;

        internal UaCustomStyle(VisualElement element) { m_Element = element; }

        bool TryGet(string name, out UssValue value)
        {
            value = default;
            var props = m_Element.computedValues.customProperties;
            return props != null && name != null && props.TryGetValue(name, out value);
        }

        public bool TryGetValue(CustomStyleProperty<float> property, out float value)
        {
            value = 0;
            if (!TryGet(property.name, out var v)) return false;
            if (v.handle.valueType == StyleValueType.Float || v.handle.valueType == StyleValueType.Dimension)
            {
                value = v.sheet.ReadFloat(v.handle);
                return true;
            }
            return false;
        }

        public bool TryGetValue(CustomStyleProperty<int> property, out int value)
        {
            value = 0;
            if (!TryGet(property.name, out var v)) return false;
            if (v.handle.valueType == StyleValueType.Float || v.handle.valueType == StyleValueType.Dimension)
            {
                value = (int)v.sheet.ReadFloat(v.handle);
                return true;
            }
            return false;
        }

        public bool TryGetValue(CustomStyleProperty<bool> property, out bool value)
        {
            value = false;
            if (!TryGet(property.name, out var v)) return false;
            if (v.handle.valueType == StyleValueType.Keyword)
            {
                var k = v.sheet.ReadKeyword(v.handle);
                value = k == StyleValueKeyword.True;
                return k == StyleValueKeyword.True || k == StyleValueKeyword.False;
            }
            return false;
        }

        public bool TryGetValue(CustomStyleProperty<Color> property, out Color value)
        {
            value = Color.clear;
            if (!TryGet(property.name, out var v)) return false;
            if (v.handle.valueType == StyleValueType.Color) { value = v.sheet.ReadColor(v.handle); return true; }
            if (v.handle.valueType == StyleValueType.Enum) return StyleProperties.TryGetColor(v.sheet.ReadEnum(v.handle).ToLowerInvariant(), out value);
            return false;
        }

        public bool TryGetValue(CustomStyleProperty<Texture2D> property, out Texture2D value)
        {
            value = null;
            if (!TryGet(property.name, out var v) || !UssReader.TryGetImage(v, m_Element.computedValues.dpiScaling, out var image)) return false;
            value = image.texture;
            return value != null;
        }

        public bool TryGetValue(CustomStyleProperty<Sprite> property, out Sprite value)
        {
            value = null;
            if (!TryGet(property.name, out var v) || !UssReader.TryGetImage(v, m_Element.computedValues.dpiScaling, out var image)) return false;
            value = image.sprite;
            return value != null;
        }

        public bool TryGetValue(CustomStyleProperty<VectorImage> property, out VectorImage value)
        {
            value = null;
            if (!TryGet(property.name, out var v) || !UssReader.TryGetImage(v, m_Element.computedValues.dpiScaling, out var image)) return false;
            value = image.vectorImage;
            return value != null;
        }

        public bool TryGetValue<T>(CustomStyleProperty<T> property, out T value) where T : Object
        {
            value = null;
            if (!TryGet(property.name, out var v)) return false;
            if (v.handle.valueType == StyleValueType.AssetReference) value = v.sheet.ReadAssetReference(v.handle) as T;
            else if (v.handle.valueType == StyleValueType.ResourcePath) value = UiResources.Load(v.sheet.ReadResourcePath(v.handle), typeof(T), m_Element.computedValues.dpiScaling) as T;
            return value != null;
        }

        public bool TryGetValue(CustomStyleProperty<string> property, out string value)
        {
            value = null;
            if (!TryGet(property.name, out var v)) return false;
            value = v.sheet.ReadAsString(v.handle);
            return true;
        }
    }
}
