using System;
using System.Collections.Generic;

// Focus: focusable elements, the panel's focus controller (focus changes send FocusOut, FocusIn,
// Blur and Focus in that order, the focused element of each composite root receiving its own
// Focus/Blur) and the focus ring Tab navigation walks.

namespace UnityEngine.UIElements
{
    public abstract partial class Focusable : CallbackEventHandler
    {
        bool m_Focusable;
        int m_TabIndex;
        bool m_DelegatesFocus;
        bool m_ExcludeFromFocusRing;

        public abstract FocusController focusController { get; }

        public virtual bool focusable { get => m_Focusable; set => m_Focusable = value; }
        public int tabIndex { get => m_TabIndex; set => m_TabIndex = value; }
        public bool delegatesFocus { get => m_DelegatesFocus; set => m_DelegatesFocus = value; }

        internal bool excludeFromFocusRing
        {
            get => m_ExcludeFromFocusRing;
            set
            {
                if (!((VisualElement)this).isCompositeRoot) throw new InvalidOperationException("excludeFromFocusRing should only be set on composite roots.");
                m_ExcludeFromFocusRing = value;
            }
        }

        internal bool isEligibleToReceiveFocusFromDisabledChild { get; set; } = true;

        public virtual bool canGrabFocus => focusable;

        protected Focusable()
        {
            focusable = true;
            tabIndex = 0;
        }

        public virtual void Focus()
        {
            if (focusController == null) return;
            if (canGrabFocus)
            {
                var target = GetFocusDelegate();
                focusController.SwitchFocus(target, this != target);
            }
            else
            {
                focusController.SwitchFocus(null);
            }
        }

        public virtual void Blur() => focusController?.Blur(this);

        internal void BlurImmediately() => focusController?.Blur(this, false, DispatchMode.Immediate);

        internal Focusable GetFocusDelegate()
        {
            Focusable f = this;
            while (f != null && f.delegatesFocus) f = FirstFocusableChild(f as VisualElement);
            return f;
        }

        static Focusable FirstFocusableChild(VisualElement ve)
        {
            int count = ve.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = ve.hierarchy[i];
                if (child.canGrabFocus && child.tabIndex >= 0) return child;
                bool isSlot = child.hierarchy.parent != null && child == child.hierarchy.parent.contentContainer;
                if (!child.isCompositeRoot && !isSlot)
                {
                    var found = FirstFocusableChild(child);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }

    public partial class FocusChangeDirection : IDisposable
    {
        readonly int m_Value;

        public static FocusChangeDirection unspecified { get; } = new FocusChangeDirection(-1);
        public static FocusChangeDirection none { get; } = new FocusChangeDirection(0);
        protected static FocusChangeDirection lastValue { get; } = none;

        protected FocusChangeDirection(int value) { m_Value = value; }

        public static implicit operator int(FocusChangeDirection fcd) => fcd?.m_Value ?? 0;

        void IDisposable.Dispose() => Dispose();
        [Unbound.EditorFaithful("Unity's base implementation")]
        protected virtual void Dispose() { }

        internal virtual void ApplyTo(FocusController focusController, Focusable f) => focusController.SwitchFocus(f, this);
    }

    public partial class VisualElementFocusChangeDirection : FocusChangeDirection
    {
        static readonly VisualElementFocusChangeDirection s_Left = new VisualElementFocusChangeDirection(1);
        static readonly VisualElementFocusChangeDirection s_Right = new VisualElementFocusChangeDirection(2);

        public static FocusChangeDirection left => s_Left;
        public static FocusChangeDirection right => s_Right;
        protected new static VisualElementFocusChangeDirection lastValue => s_Right;

        protected VisualElementFocusChangeDirection(int value) : base(value) { }
    }

    internal sealed class UaFocusChangeTarget : FocusChangeDirection
    {
        public Focusable target { get; private set; }

        public UaFocusChangeTarget(Focusable target) : base(-1) { this.target = target; }

        internal override void ApplyTo(FocusController focusController, Focusable f)
        {
            focusController.selectedTextElement = null;
            f.Focus();
        }
    }

    public partial interface IFocusRing
    {
        FocusChangeDirection GetFocusChangeDirection(Focusable currentFocusable, EventBase e);
        Focusable GetNextFocusable(Focusable currentFocusable, FocusChangeDirection direction);
    }

    public partial class FocusController
    {
        struct FocusedElement
        {
            public VisualElement m_SubTreeRoot;
            public VisualElement m_FocusedElement;
        }

        TextElement m_SelectedTextElement;
        readonly List<FocusedElement> m_FocusedElements = new List<FocusedElement>();
        Focusable m_LastFocusedElement;
        internal Focusable m_LastPendingFocusedElement;
        int m_PendingFocusCount;

        IFocusRing focusRing { get; }

        public FocusController(IFocusRing focusRing)
        {
            this.focusRing = focusRing;
            imguiKeyboardControl = 0;
        }

        internal TextElement selectedTextElement
        {
            get => m_SelectedTextElement;
            set
            {
                if (m_SelectedTextElement == value) return;
                m_SelectedTextElement?.UaSelectNone();
                m_SelectedTextElement = value;
            }
        }

        public Focusable focusedElement
        {
            get
            {
                var f = GetRetargetedFocusedElement(null);
                return IsLocalElement(f) ? f : null;
            }
        }

        internal int imguiKeyboardControl { get; set; }

        public void IgnoreEvent(EventBase evt)
        {
            evt.processedByFocusController = true;
            if (evt is IMouseEventInternal m && m.sourcePointerEvent is EventBase source) source.processedByFocusController = true;
        }

        internal bool IsFocused(Focusable f)
        {
            if (!IsLocalElement(f)) return false;
            foreach (var e in m_FocusedElements)
                if (e.m_FocusedElement == f) return true;
            return false;
        }

        internal Focusable GetRetargetedFocusedElement(VisualElement retargetAgainst)
        {
            var root = retargetAgainst?.hierarchy.parent;
            if (root == null)
            {
                if (m_FocusedElements.Count > 0) return m_FocusedElements[m_FocusedElements.Count - 1].m_FocusedElement;
            }
            else
            {
                while (!root.isCompositeRoot && root.hierarchy.parent != null) root = root.hierarchy.parent;
                foreach (var e in m_FocusedElements)
                    if (e.m_SubTreeRoot == root) return e.m_FocusedElement;
            }
            return null;
        }

        internal Focusable GetLeafFocusedElement()
        {
            if (m_FocusedElements.Count == 0) return null;
            var ve = m_FocusedElements[0].m_FocusedElement;
            return IsLocalElement(ve) ? ve : null;
        }

        bool IsLocalElement(Focusable f) => f?.focusController == this;

        internal void ClearPendingFocusEvents()
        {
            m_PendingFocusCount = 0;
            m_LastPendingFocusedElement = null;
        }

        internal bool IsPendingFocus(Focusable f)
        {
            for (var ve = m_LastPendingFocusedElement as VisualElement; ve != null; ve = ve.hierarchy.parent)
                if (f == ve) return true;
            return false;
        }

        internal void SetFocusToLastFocusedElement()
        {
            if (m_LastFocusedElement != null && !(m_LastFocusedElement is IMGUIContainer)) m_LastFocusedElement.Focus();
        }

        internal void BlurLastFocusedElement()
        {
            selectedTextElement = null;
            if (m_LastFocusedElement != null && !(m_LastFocusedElement is IMGUIContainer))
            {
                var last = m_LastFocusedElement;
                m_LastFocusedElement.Blur();
                m_LastFocusedElement = last;
            }
        }

        internal void DoFocusChange(Focusable f)
        {
            m_FocusedElements.Clear();
            GetFocusTargets(f, m_FocusedElements);
        }

        internal void ProcessPendingFocusChange(Focusable f)
        {
            m_PendingFocusCount--;
            if (m_PendingFocusCount == 0) m_LastPendingFocusedElement = null;
            foreach (var e in m_FocusedElements) e.m_FocusedElement.pseudoStates &= ~PseudoStates.Focus;
            DoFocusChange(f);
            foreach (var e in m_FocusedElements) e.m_FocusedElement.pseudoStates |= PseudoStates.Focus;
        }

        static void GetFocusTargets(Focusable f, List<FocusedElement> outTargets)
        {
            var focused = f as VisualElement;
            for (var ve = focused; ve != null; ve = ve.hierarchy.parent)
            {
                if (ve.hierarchy.parent == null || ve.isCompositeRoot)
                {
                    outTargets.Add(new FocusedElement { m_SubTreeRoot = ve, m_FocusedElement = focused });
                    focused = ve;
                }
            }
        }

        internal Focusable FocusNextInDirection(Focusable currentFocusable, FocusChangeDirection direction)
        {
            var next = focusRing.GetNextFocusable(currentFocusable, direction);
            direction.ApplyTo(this, next);
            return next;
        }

        void AboutToReleaseFocus(Focusable focusable, Focusable willGiveFocusTo, FocusChangeDirection direction, DispatchMode mode)
        {
            using (var e = FocusOutEvent.GetPooled(focusable, willGiveFocusTo, direction, this))
                focusable.SendEvent(e, mode);
        }

        void ReleaseFocus(Focusable focusable, Focusable willGiveFocusTo, FocusChangeDirection direction, DispatchMode mode)
        {
            var targets = new List<FocusedElement>();
            GetFocusTargets(focusable, targets);
            foreach (var t in targets)
            {
                using (var e = BlurEvent.GetPooled(t.m_FocusedElement, willGiveFocusTo, direction, this))
                {
                    e.target = t.m_FocusedElement;
                    focusable.SendEvent(e, mode);
                }
            }
        }

        void AboutToGrabFocus(Focusable focusable, Focusable willTakeFocusFrom, FocusChangeDirection direction, DispatchMode mode)
        {
            using (var e = FocusInEvent.GetPooled(focusable, willTakeFocusFrom, direction, this))
                focusable.SendEvent(e, mode);
        }

        void GrabFocus(Focusable focusable, Focusable willTakeFocusFrom, FocusChangeDirection direction, bool delegated, DispatchMode mode)
        {
            var targets = new List<FocusedElement>();
            GetFocusTargets(focusable, targets);
            foreach (var t in targets)
            {
                using (var e = FocusEvent.GetPooled(t.m_FocusedElement, willTakeFocusFrom, direction, this, delegated))
                {
                    e.target = t.m_FocusedElement;
                    focusable.SendEvent(e, mode);
                }
            }
        }

        internal void Blur(Focusable focusable, bool bIsFocusDelegated = false, DispatchMode dispatchMode = DispatchMode.Default)
        {
            if (m_PendingFocusCount > 0 ? IsPendingFocus(focusable) : IsFocused(focusable))
                SwitchFocus(null, bIsFocusDelegated, dispatchMode);
        }

        internal void SwitchFocus(Focusable newFocusedElement, bool bIsFocusDelegated = false, DispatchMode dispatchMode = DispatchMode.Default) =>
            SwitchFocus(newFocusedElement, FocusChangeDirection.unspecified, bIsFocusDelegated, dispatchMode);

        internal void SwitchFocus(Focusable newFocusedElement, FocusChangeDirection direction, bool bIsFocusDelegated = false, DispatchMode dispatchMode = DispatchMode.Default)
        {
            m_LastFocusedElement = newFocusedElement;
            var current = m_PendingFocusCount > 0 ? m_LastPendingFocusedElement : GetLeafFocusedElement();
            if (current == newFocusedElement) return;
            if (newFocusedElement is VisualElement newElement && newFocusedElement.canGrabFocus && newElement.panel != null)
            {
                var giveTo = newElement.RetargetElement(current as VisualElement) ?? newFocusedElement;
                var takeFrom = (current as VisualElement)?.RetargetElement(newElement) ?? current;
                m_LastPendingFocusedElement = newFocusedElement;
                m_PendingFocusCount++;
                using (new UaEventDispatcherGate(newElement.panel.dispatcher))
                {
                    if (current != null) AboutToReleaseFocus(current, giveTo, direction, dispatchMode);
                    AboutToGrabFocus(newFocusedElement, takeFrom, direction, dispatchMode);
                    if (current != null) ReleaseFocus(current, giveTo, direction, dispatchMode);
                    GrabFocus(newFocusedElement, takeFrom, direction, bIsFocusDelegated, dispatchMode);
                }
                return;
            }
            if (current is VisualElement currentElement && currentElement.elementPanel != null)
            {
                m_LastPendingFocusedElement = null;
                m_PendingFocusCount++;
                using (new UaEventDispatcherGate(currentElement.elementPanel.dispatcher))
                {
                    AboutToReleaseFocus(current, null, direction, dispatchMode);
                    ReleaseFocus(current, null, direction, dispatchMode);
                }
                return;
            }
            ProcessPendingFocusChange(null);
        }

        internal void SwitchFocusOnEvent(Focusable currentFocusable, EventBase e)
        {
            if (e.processedByFocusController) return;
            var direction = focusRing.GetFocusChangeDirection(currentFocusable, e);
            if (direction != FocusChangeDirection.none)
            {
                FocusNextInDirection(currentFocusable, direction);
                e.processedByFocusController = true;
            }
        }

        internal void ReevaluateFocus()
        {
            if (focusedElement is VisualElement ve && (!ve.areAncestorsAndSelfDisplayed || !ve.visible)) ve.Blur();
        }

        internal bool GetFocusableParentForPointerEvent(Focusable target, out Focusable effectiveTarget)
        {
            if (target == null || !target.focusable)
            {
                effectiveTarget = target;
                return target != null;
            }
            effectiveTarget = target;
            while (effectiveTarget is VisualElement ve && (!ve.enabledInHierarchy || !ve.focusable || !ve.isEligibleToReceiveFocusFromDisabledChild) && ve.hierarchy.parent != null)
                effectiveTarget = ve.hierarchy.parent;
            return !IsFocused(effectiveTarget);
        }
    }

    public partial class VisualElementFocusRing : IFocusRing
    {
        public enum DefaultFocusOrder
        {
            ChildOrder = 0,
            PositionXY = 1,
            PositionYX = 2,
        }

        sealed class Record
        {
            public int AutoIndex;
            public Focusable Focusable;
            public bool IsSlot;
            public List<Record> Scope;
        }

        readonly VisualElement root;
        readonly List<Record> m_FocusRing = new List<Record>();

        public DefaultFocusOrder defaultFocusOrder { get; set; }

        public VisualElementFocusRing(VisualElement root, DefaultFocusOrder dfo = DefaultFocusOrder.ChildOrder)
        {
            defaultFocusOrder = dfo;
            this.root = root;
        }

        FocusController focusController => root.focusController;

        int AutoIndexSort(Record a, Record b)
        {
            if (defaultFocusOrder != DefaultFocusOrder.ChildOrder && a.Focusable is VisualElement va && b.Focusable is VisualElement vb)
            {
                var pa = va.layout.position;
                var pb = vb.layout.position;
                float a1 = defaultFocusOrder == DefaultFocusOrder.PositionXY ? pa.x : pa.y;
                float b1 = defaultFocusOrder == DefaultFocusOrder.PositionXY ? pb.x : pb.y;
                float a2 = defaultFocusOrder == DefaultFocusOrder.PositionXY ? pa.y : pa.x;
                float b2 = defaultFocusOrder == DefaultFocusOrder.PositionXY ? pb.y : pb.x;
                if (a1 < b1) return -1;
                if (a1 > b1) return 1;
                if (a2 < b2) return -1;
                if (a2 > b2) return 1;
            }
            return a.AutoIndex.CompareTo(b.AutoIndex);
        }

        int RingSort(Record a, Record b)
        {
            if (a.Focusable.tabIndex == 0 && b.Focusable.tabIndex == 0) return AutoIndexSort(a, b);
            if (a.Focusable.tabIndex == 0) return 1;
            if (b.Focusable.tabIndex == 0) return -1;
            int c = a.Focusable.tabIndex.CompareTo(b.Focusable.tabIndex);
            return c != 0 ? c : AutoIndexSort(a, b);
        }

        void DoUpdate()
        {
            m_FocusRing.Clear();
            if (root == null) return;
            var list = new List<Record>();
            int index = 0;
            Build(root, ref index, list);
            Flatten(list);
        }

        void Build(VisualElement ve, ref int index, List<Record> scope)
        {
            int count = ve.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = ve.hierarchy[i];
                bool slot = child.parent != null && child == child.parent.contentContainer;
                if (child.isCompositeRoot || slot)
                {
                    var record = new Record { AutoIndex = index++, Focusable = child, IsSlot = slot, Scope = new List<Record>() };
                    scope.Add(record);
                    int inner = 0;
                    Build(child, ref inner, record.Scope);
                }
                else
                {
                    if (child.canGrabFocus && child.areAncestorsAndSelfDisplayed && child.tabIndex >= 0)
                        scope.Add(new Record { AutoIndex = index++, Focusable = child });
                    Build(child, ref index, scope);
                }
            }
        }

        void Flatten(List<Record> scope)
        {
            if (scope == null) return;
            scope.Sort(RingSort);
            foreach (var r in scope)
            {
                if (r.Focusable.canGrabFocus && r.Focusable.tabIndex >= 0)
                {
                    if (!r.Focusable.excludeFromFocusRing) m_FocusRing.Add(r);
                    Flatten(r.Scope);
                }
                else if (r.IsSlot)
                {
                    Flatten(r.Scope);
                }
                r.Scope = null;
            }
        }

        int IndexOf(Focusable f)
        {
            if (f == null) return -1;
            for (int i = 0; i < m_FocusRing.Count; i++)
                if (m_FocusRing[i].Focusable == f) return i;
            return -1;
        }

        public FocusChangeDirection GetFocusChangeDirection(Focusable currentFocusable, EventBase e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.eventTypeId == PointerDownEvent.TypeId() && focusController.GetFocusableParentForPointerEvent(e.elementTarget, out var effective))
                return new UaFocusChangeTarget(effective);
            if (currentFocusable != null && currentFocusable.isIMGUIContainer) return FocusChangeDirection.none;
            if (e.eventTypeId == NavigationMoveEvent.TypeId())
            {
                switch (((NavigationMoveEvent)e).direction)
                {
                    case NavigationMoveEvent.Direction.Previous: return VisualElementFocusChangeDirection.left;
                    case NavigationMoveEvent.Direction.Next: return VisualElementFocusChangeDirection.right;
                }
            }
            return FocusChangeDirection.none;
        }

        public Focusable GetNextFocusable(Focusable currentFocusable, FocusChangeDirection direction)
        {
            if (direction == FocusChangeDirection.none || direction == FocusChangeDirection.unspecified) return currentFocusable;
            if (direction is UaFocusChangeTarget t) return t.target;
            DoUpdate();
            if (m_FocusRing.Count == 0) return null;
            int index = IndexOf(currentFocusable);
            if (currentFocusable != null && index == -1)
            {
                if (direction == VisualElementFocusChangeDirection.right) return NextInTree(currentFocusable as VisualElement);
                if (direction == VisualElementFocusChangeDirection.left) return PreviousInTree(currentFocusable as VisualElement);
            }
            int i = 0;
            if (direction == VisualElementFocusChangeDirection.right)
            {
                i = index + 1;
                if (i == m_FocusRing.Count) i = 0;
                while (m_FocusRing[i].Focusable.delegatesFocus)
                {
                    i++;
                    if (i == m_FocusRing.Count) return null;
                }
            }
            else if (direction == VisualElementFocusChangeDirection.left)
            {
                i = index - 1;
                if (i < 0) i = m_FocusRing.Count - 1;
                while (m_FocusRing[i].Focusable.delegatesFocus)
                {
                    i--;
                    if (i == -1) return null;
                }
            }
            return m_FocusRing[i].Focusable;
        }

        static Focusable NextInTree(VisualElement current)
        {
            if (current == null) return null;
            var next = current.UaNextDepthFirst();
            while (next == null || !next.canGrabFocus || next.tabIndex < 0 || (next.isCompositeRoot && next.excludeFromFocusRing))
            {
                next = next?.UaNextDepthFirst() ?? current.UaRoot();
                if (next == current) return current;
            }
            return next;
        }

        static Focusable PreviousInTree(VisualElement current)
        {
            if (current == null) return null;
            var prev = current.UaPreviousDepthFirst();
            while (prev == null || !prev.canGrabFocus || prev.tabIndex < 0 || (prev.isCompositeRoot && prev.excludeFromFocusRing))
            {
                prev = prev?.UaPreviousDepthFirst();
                if (prev == null)
                {
                    prev = current.UaRoot();
                    while (prev.hierarchy.childCount > 0) prev = prev.hierarchy[prev.hierarchy.childCount - 1];
                }
                if (prev == current) return current;
            }
            return prev;
        }
    }
}
