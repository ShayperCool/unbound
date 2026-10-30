using System;
using System.Collections.Generic;
using Unbound.UIElements;
using Unbound.UIElements.Layout;

// Panels: the root of a visual tree, its dispatcher, focus, pointer state, scheduler and the update
// passes (styles, layout with geometry events, rendering).

namespace UnityEngine.UIElements
{
    public partial interface IPanel : IDisposable
    {
        VisualElement visualTree { get; }
        EventDispatcher dispatcher { get; }
        ContextType contextType { get; }
        FocusController focusController { get; }
        ContextualMenuManager contextualMenuManager { get; }
        bool isDirty { get; }
        float scaledPixelsPerPoint { get; }
        VisualElement Pick(Vector2 point);
        VisualElement PickAll(Vector2 point, List<VisualElement> picked);
    }

    public abstract partial class ContextualMenuManager
    {
        protected ContextualMenuManager() { }
        public abstract void DisplayMenuIfEventMatches(EventBase evt, IEventHandler eventHandler);
        internal bool displayMenuHandledOSX { get; set; }
    }

    internal partial class PanelRootElement : VisualElement
    {
        public PanelRootElement()
        {
            name = "unity-panel-container";
            viewDataKey = "PanelContainer";
            pickingMode = PickingMode.Ignore;
        }
    }

    internal abstract partial class BaseVisualElementPanel : IPanel, IDisposable
    {
        float m_Scale = 1f;
        float m_PixelsPerPoint = 1f;
        internal readonly YogaConfig uaLayoutConfig = new YogaConfig { RoundEdgesToPixelGrid = true, PercentFlexBasisNeedsExactContainer = true, OverflowHiddenMeasuresContent = true, RoundPositionsFromAbsolute = true, MeasuredNodesRoundAsOthers = true };
        internal readonly UaElementUnderPointer uaUnderPointer = new UaElementUnderPointer();
        readonly StyleResolver m_StyleResolver = new StyleResolver();
        internal uint uaVersion;
        internal readonly HashSet<StyleTransitions> uaTransitionUpdates = new HashSet<StyleTransitions>();

        public abstract VisualElement visualTree { get; }
        public abstract EventDispatcher dispatcher { get; set; }
        public abstract ContextType contextType { get; }
        public abstract FocusController focusController { get; set; }
        public ContextualMenuManager contextualMenuManager { get; internal set; }
        public bool isDirty => uaVersion != uaRepaintVersion;
        internal uint uaRepaintVersion;
        internal bool disposed { get; private set; }
        internal bool duringLayoutPhase { get; set; }
        public bool isFlat { get; set; } = true;

        [Unbound.EditorFaithful("Panel, the only concrete panel, overrides it with the real rendering")]
        public virtual void Render() { }
        public abstract ScriptableObject ownerObject { get; protected set; }
        internal string name { get; set; }

        internal float scale
        {
            get => m_Scale;
            set
            {
                if (Mathf.Approximately(m_Scale, value)) return;
                m_Scale = value;
                uaLayoutConfig.PointScaleFactor = scaledPixelsPerPoint;
                MarkAllLayoutDirty(visualTree);
                visualTree.IncrementVersion(VersionChangeType.StyleSheet | VersionChangeType.Layout);
            }
        }

        internal float pixelsPerPoint
        {
            get => m_PixelsPerPoint;
            set
            {
                if (Mathf.Approximately(m_PixelsPerPoint, value)) return;
                m_PixelsPerPoint = value;
                uaLayoutConfig.PointScaleFactor = scaledPixelsPerPoint;
                MarkAllLayoutDirty(visualTree);
                visualTree.IncrementVersion(VersionChangeType.StyleSheet | VersionChangeType.Layout);
            }
        }

        static void MarkAllLayoutDirty(VisualElement ve)
        {
            if (ve.yogaNode.MeasureFunction != null) ve.yogaNode.MarkDirty();
            else ve.yogaNode.MarkDirtyInternal();
            int n = ve.hierarchy.childCount;
            for (int i = 0; i < n; i++) MarkAllLayoutDirty(ve.hierarchy[i]);
        }

        public float scaledPixelsPerPoint => m_PixelsPerPoint * m_Scale;

        public float referenceSpritePixelsPerUnit { get; set; } = 100f;

        public abstract VisualElement Pick(Vector2 point);
        public abstract VisualElement PickAll(Vector2 point, List<VisualElement> picked);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                visualTree.uaTransitions?.CancelAll();
                uaTransitionUpdates.Clear();
                PointerDeviceState.RemovePanelData(this);
            }
            disposed = true;
        }

        internal void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default) => dispatcher?.Dispatch(e, this, dispatchMode);

        internal abstract void OnVersionChanged(VisualElement ele, VersionChangeType changeTypeFlag);

        /// <summary>An element changed: styles, layout or rendering need updating.</summary>
        uint m_UaHierarchyFlagsVersion;
        uint m_UaHierarchyFlagsLastVersion;

        const VersionChangeType k_PickingChanges = VersionChangeType.Hierarchy | VersionChangeType.Overflow | VersionChangeType.BorderWidth |
            VersionChangeType.Transform | VersionChangeType.Size | VersionChangeType.EventCallbackCategories | VersionChangeType.Picking;

        internal void UaOnVersionChanged(VisualElement ve, VersionChangeType change)
        {
            uaVersion++;
            if ((change & k_PickingChanges) != 0) m_UaHierarchyFlagsVersion++;
            if ((change & (VersionChangeType.Transform | VersionChangeType.Hierarchy)) != 0) ve.UaDirtyWorldTransform();
            // UIR regenerates an element's geometry on repaints and color changes (without dynamic
            // color hints); transforms move it, opacity and clipping are drawn from shader data.
            if ((change & (VersionChangeType.Repaint | VersionChangeType.Color)) != 0) ve.uaGeometry?.Invalidate();
            if ((change & (VersionChangeType.StyleSheet | VersionChangeType.Styles | VersionChangeType.Hierarchy)) != 0)
            {
                ve.uaStyleDirty = true;
                for (var p = ve.hierarchy.parent; p != null && !p.uaStyleDirtyDescendants; p = p.hierarchy.parent) p.uaStyleDirtyDescendants = true;
            }
            if ((change & (VersionChangeType.Hierarchy | VersionChangeType.Layout)) != 0 && ve.yogaNode.MeasureFunction != null)
                ve.yogaNode.MarkDirty();
        }

        internal virtual void OnElementLeavingPanel(VisualElement ve) { }

        // ---- pointer state ----

        internal VisualElement GetTopElementUnderPointer(int pointerId) => uaUnderPointer.GetTopElementUnderPointer(pointerId);

        internal VisualElement RecomputeTopElementUnderPointer(int pointerId, Vector2 pointerPos, EventBase triggerEvent)
        {
            VisualElement element = null;
            if (PointerDeviceState.GetPanel(pointerId, contextType) == this && !PointerDeviceState.HasLocationFlag(pointerId, contextType, PointerDeviceState.LocationFlag.OutsidePanel))
                element = Pick(pointerPos);
            uaUnderPointer.SetElementUnderPointer(element, pointerId, triggerEvent);
            if (triggerEvent is IPointerEventInternal pe && pe.compatibilityMouseEvent != null)
                uaUnderPointer.SetElementUnderPointer(element, PointerId.mousePointerId, (EventBase)pe.compatibilityMouseEvent);
            return element;
        }

        internal void ClearCachedElementUnderPointer(int pointerId, EventBase triggerEvent) =>
            uaUnderPointer.SetTemporaryElementUnderPointer(null, pointerId, triggerEvent);

        internal void CommitElementUnderPointers() => uaUnderPointer.CommitElementUnderPointers(dispatcher, contextType);

        /// <summary>After elements moved, appeared or changed size: picks again what is under the hovering
        /// pointers and sends over/out and enter/leave events for the changes (without a trigger event).</summary>
        internal void UpdateElementUnderPointers()
        {
            foreach (int pointerId in PointerId.hoveringPointers)
            {
                if (PointerDeviceState.GetPanel(pointerId, contextType) != this || PointerDeviceState.HasLocationFlag(pointerId, contextType, PointerDeviceState.LocationFlag.OutsidePanel))
                {
                    uaUnderPointer.SetElementUnderPointer(null, pointerId, new Vector2(int.MinValue, int.MinValue));
                    continue;
                }
                var position = PointerDeviceState.GetPointerPosition(pointerId, contextType);
                uaUnderPointer.SetElementUnderPointer(PerformPick(visualTree, position), pointerId, position);
            }
            CommitElementUnderPointers();
        }

        internal IEventHandler GetCapturingElement(int pointerId) => dispatcher?.m_UaPointerState.GetCapturingElement(pointerId);
        internal void ReleasePointer(int pointerId) => dispatcher?.m_UaPointerState.ReleasePointer(pointerId);
        internal void ProcessPointerCapture(int pointerId) => dispatcher?.m_UaPointerState.ProcessPointerCapture(pointerId);
        internal bool ShouldSendCompatibilityMouseEvents(IPointerEvent evt) => dispatcher?.m_UaPointerState.ShouldSendCompatibilityMouseEvents(evt) ?? true;
        internal void ActivateCompatibilityMouseEvents(int pointerId) => dispatcher?.m_UaPointerState.ActivateCompatibilityMouseEvents(pointerId);
        internal void PreventCompatibilityMouseEvents(int pointerId) => dispatcher?.m_UaPointerState.PreventCompatibilityMouseEvents(pointerId);

        // ---- updates ----

        internal void UaApplyStyles() => m_StyleResolver.Update(visualTree, scaledPixelsPerPoint);

        public virtual void UpdateAnimations()
        {
            long now = Panel.TimeSinceStartupMs();
            // Events can start or detach animations. New registrations wait for the next update.
            var updates = new List<StyleTransitions>(uaTransitionUpdates);
            foreach (var transitions in updates)
            {
                if (!uaTransitionUpdates.Contains(transitions)) continue;
                transitions.Update(now);
                if (!transitions.active) uaTransitionUpdates.Remove(transitions);
            }
        }

        public abstract void ApplyStyles();
        public abstract void ValidateLayout();

        bool m_Validating;

        internal void UaValidateLayout()
        {
            if (m_Validating) return;
            m_Validating = true;
            try
            {
                UaApplyStyles();
                UpdateAnimations();
                UaApplyStyles();
                UpdateLayout();
                if (m_UaHierarchyFlagsVersion != m_UaHierarchyFlagsLastVersion)
                {
                    m_UaHierarchyFlagsLastVersion = m_UaHierarchyFlagsVersion;
                    UpdateElementUnderPointers();
                }
                UaDiagnostics.AfterLayout(visualTree);
            }
            finally
            {
                m_Validating = false;
            }
        }

        readonly List<(Rect OldRect, Rect NewRect, VisualElement Element)> m_ChangeEvents = new List<(Rect, Rect, VisualElement)>();

        void UpdateLayout()
        {
            var root = visualTree;
            int pass = 0;
            while (root.yogaNode.IsDirty)
            {
                m_ChangeEvents.Clear();
                if (pass > 0) UaApplyStyles();
                duringLayoutPhase = true;
                try
                {
                    root.yogaNode.CalculateLayout();
                }
                finally
                {
                    duringLayoutPhase = false;
                }
                UpdateSubTree(root, true);
                foreach (var (oldRect, newRect, element) in m_ChangeEvents)
                {
                    using (var e = GeometryChangedEvent.GetPooled(oldRect, newRect))
                    {
                        e.layoutPass = pass;
                        e.elementTarget = element;
                        Dispatching.HandleEventAtTargetAndDefaultPhase(e, this, element);
                    }
                }
                if (pass++ >= 10)
                {
                    Debug.LogError("Layout update is struggling to process current layout (consider simplifying to avoid recursive layout): " + root);
                    break;
                }
            }
            focusController?.ReevaluateFocus();
        }

        bool UpdateHierarchyDisplayed(VisualElement ve, bool inheritedDisplayed)
        {
            bool displayed = inheritedDisplayed && ve.resolvedStyle.display != DisplayStyle.None;
            if (inheritedDisplayed && !displayed) ve.disableRendering = true;
            else if (displayed) ve.disableRendering = false;
            if (ve.areAncestorsAndSelfDisplayed == displayed) return false;
            ve.areAncestorsAndSelfDisplayed = displayed;
            if (!displayed)
            {
                if (inheritedDisplayed) ve.IncrementVersion(VersionChangeType.Size);
                m_ChangeEvents.Add((ve.lastLayout, Rect.zero, ve));
                int n = ve.hierarchy.childCount;
                for (int i = 0; i < n; i++) UpdateHierarchyDisplayed(ve.hierarchy[i], false);
            }
            return true;
        }

        void UpdateSubTree(VisualElement ve, bool inheritedDisplayed)
        {
            bool displayChanged = UpdateHierarchyDisplayed(ve, inheritedDisplayed);
            if (!ve.areAncestorsAndSelfDisplayed) return;
            var n = ve.yogaNode;
            var rect = new Rect(n.LayoutX, n.LayoutY, n.LayoutWidth, n.LayoutHeight);
            var last = ve.lastLayout;
            bool sizeChanged = last.size != rect.size;
            bool positionChanged = last.position != rect.position;
            // Size changes repaint; position changes (and showing again) move the world transform, as
            // Unity's layout updater versions them. A size change moves it only around a transform origin.
            VersionChangeType change = 0;
            if (sizeChanged) change |= VersionChangeType.Size | VersionChangeType.Repaint;
            if (positionChanged || displayChanged) change |= VersionChangeType.Transform;
            if (displayChanged) change |= VersionChangeType.Size;
            if ((change & (VersionChangeType.Transform | VersionChangeType.Size)) == VersionChangeType.Size && !ve.hasDefaultRotationAndScale
                && (!Mathf.Approximately(ve.resolvedStyle.transformOrigin.x, 0f) || !Mathf.Approximately(ve.resolvedStyle.transformOrigin.y, 0f)))
                change |= VersionChangeType.Transform;
            if (change != 0) ve.IncrementVersion(change);
            ve.lastLayout = rect;
            bool hasNewLayout = n.HasNewLayout;
            if (sizeChanged || positionChanged || displayChanged)
                m_ChangeEvents.Add((displayChanged ? Rect.zero : last, rect, ve));
            if (hasNewLayout)
            {
                int count = ve.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    var child = ve.hierarchy[i];
                    if (child.yogaNode.HasNewLayout) UpdateSubTree(child, true);
                }
                n.MarkLayoutSeen();
            }
        }

        // ---- picking ----

        internal static VisualElement PerformPick(VisualElement root, Vector2 point, List<VisualElement> picked = null)
        {
            if (root.resolvedStyle.display == DisplayStyle.None) return null;
            if (root.pickingMode == PickingMode.Ignore && root.hierarchy.childCount == 0) return null;
            var local = root.WorldToLocal(point);
            bool contains = root.ContainsPoint(local);
            if (!contains && UaClips(root)) return null;
            VisualElement result = null;
            for (int i = root.hierarchy.childCount - 1; i >= 0; i--)
            {
                var found = PerformPick(root.hierarchy[i], point, picked);
                if (result == null && found != null)
                {
                    if (picked == null) return found;
                    result = found;
                }
            }
            if (root.visible && root.pickingMode == PickingMode.Position && contains)
            {
                picked?.Add(root);
                if (result == null) result = root;
            }
            return result;
        }

        internal static bool UaClips(VisualElement ve) => ve.computedValues.overflow != OverflowInternal.Visible;
    }

    internal partial class Panel : BaseVisualElementPanel
    {
        readonly VisualElement m_RootContainer;
        EventDispatcher m_Dispatcher;
        ContextType m_ContextType;
        FocusController m_FocusController;
        internal readonly UaScheduler uaScheduler = new UaScheduler();

        public override VisualElement visualTree => m_RootContainer;
        public override EventDispatcher dispatcher { get => m_Dispatcher; set => m_Dispatcher = value; }
        public override ContextType contextType => m_ContextType;
        public override ScriptableObject ownerObject { get; protected set; }
        internal override void OnVersionChanged(VisualElement ele, VersionChangeType changeTypeFlag) => UaOnVersionChanged(ele, changeTypeFlag);
        public override void ApplyStyles() => UaApplyStyles();
        public override void UpdateAnimations() => base.UpdateAnimations();
        public override void ValidateLayout() => UaValidateLayout();
        public override FocusController focusController { get => m_FocusController; set => m_FocusController = value; }

        static TimeMsFunction s_TimeSinceStartup;

        internal static TimeMsFunction TimeSinceStartup { private get => s_TimeSinceStartup; set => s_TimeSinceStartup = value; }

        public static long TimeSinceStartupMs() => s_TimeSinceStartup?.Invoke() ?? (long)(Time.realtimeSinceStartup * 1000f);

        public Panel(ScriptableObject ownerObject, ContextType contextType, EventDispatcher dispatcher, InitEditorUpdaterFunction initEditorUpdater = null)
        {
            this.ownerObject = ownerObject;
            m_ContextType = contextType;
            m_Dispatcher = dispatcher;
            m_RootContainer = new PanelRootElement();
            m_RootContainer.SetPanel(this);
            m_FocusController = new FocusController(new VisualElementFocusRing(m_RootContainer));
        }

        public override VisualElement Pick(Vector2 point)
        {
            ValidateLayout();
            var top = uaUnderPointer.GetTopElementUnderPointer(PointerId.mousePointerId, out var pickPosition, out bool isTemporary);
            if (!isTemporary && Vector2Int.FloorToInt(pickPosition) == Vector2Int.FloorToInt(point)) return top;
            return PerformPick(visualTree, point);
        }

        public override VisualElement PickAll(Vector2 point, List<VisualElement> picked)
        {
            ValidateLayout();
            picked?.Clear();
            return PerformPick(visualTree, point, picked);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing) visualTree.Clear();
            base.Dispose(disposing);
        }
    }

    internal abstract partial class BaseRuntimePanel : Panel
    {
        float m_SortingPriority;
        static int s_CurrentRuntimePanelCounter;
        internal readonly int m_RuntimePanelCreationIndex;
        internal int resolvedSortingIndex;
        internal RenderTexture targetTexture;
        internal static readonly Func<Vector2, Vector2> DefaultScreenToPanelSpace = p => p;
        Func<Vector2, Vector2> m_ScreenToPanelSpace = DefaultScreenToPanelSpace;
        internal UaPanelClear uaClear;
        internal bool uaForceGamma;

        protected BaseRuntimePanel(ScriptableObject ownerObject, EventDispatcher dispatcher)
            : base(ownerObject, ContextType.Player, dispatcher)
        {
            m_RuntimePanelCreationIndex = s_CurrentRuntimePanelCounter++;
        }

        public float sortingPriority
        {
            get => m_SortingPriority;
            set
            {
                if (Mathf.Approximately(m_SortingPriority, value)) return;
                m_SortingPriority = value;
                if (contextType == ContextType.Player) UIElementsRuntimeUtility.SetPanelOrderingDirty();
            }
        }

        internal bool drawsInCameras { get; set; }
        internal float pixelsPerUnit { get; set; } = 100f;
        internal int targetDisplay { get; set; }
        internal int worldSpaceLayer;

        public Func<Vector2, Vector2> screenToPanelSpace
        {
            get => m_ScreenToPanelSpace;
            set => m_ScreenToPanelSpace = value ?? DefaultScreenToPanelSpace;
        }

        public event Action destroyed;

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing) destroyed?.Invoke();
            base.Dispose(disposing);
        }

        internal virtual void Update()
        {
            uaScheduler.UpdateScheduledEvents();
            ValidateLayout();
        }

        internal Vector2 ScreenToPanel(Vector2 screen) => screenToPanelSpace(screen) / scale;

        internal bool ScreenToPanel(Vector2 screenPosition, Vector2 screenDelta, out Vector2 panelPosition, out Vector2 panelDelta, bool allowOutside = false)
        {
            panelPosition = ScreenToPanel(screenPosition);
            Vector2 previous;
            if (!allowOutside)
            {
                var r = visualTree.layout;
                if (!r.Contains(panelPosition))
                {
                    panelDelta = screenDelta;
                    return false;
                }
                previous = ScreenToPanel(screenPosition - screenDelta);
                if (!r.Contains(previous))
                {
                    panelDelta = screenDelta;
                    return true;
                }
            }
            else
            {
                previous = ScreenToPanel(screenPosition - screenDelta);
            }
            panelDelta = panelPosition - previous;
            return true;
        }

        internal void PointerLeavesPanel(int pointerId, Vector2 position)
        {
            ClearCachedElementUnderPointer(pointerId, null);
            CommitElementUnderPointers();
            PointerDeviceState.SavePointerPosition(pointerId, position, null, contextType);
        }

        internal void PointerEntersPanel(int pointerId, Vector2 position) =>
            PointerDeviceState.SavePointerPosition(pointerId, position, this, contextType);

        internal void Repaint(Event e)
        {
            uaRepaintVersion = uaVersion;
            ValidateLayout();
        }

        public override void Render() => PanelRendering.Render(this);
    }

    internal delegate long TimeMsFunction();

    internal delegate void InitEditorUpdaterFunction(BaseVisualElementPanel panel, VisualTreeUpdater visualTreeUpdater);

    internal sealed partial class VisualTreeUpdater : IDisposable
    {
        public VisualTreeUpdater(BaseVisualElementPanel panel) { }
        [Unbound.EditorFaithful("this updater holds no updaters of its own to dispose")]
        public void Dispose() { }
    }

    internal struct UaPanelClear
    {
        public bool clearColor;
        public bool clearDepthStencil;
        public Color color;
    }

    internal sealed partial class RuntimePanel : BaseRuntimePanel
    {
        internal static readonly EventDispatcher s_EventDispatcher = EventDispatcher.CreateDefault();
        readonly PanelSettings m_PanelSettings;

        public PanelSettings panelSettings => m_PanelSettings;

        public static RuntimePanel Create(ScriptableObject ownerObject) => new RuntimePanel(ownerObject);

        RuntimePanel(ScriptableObject ownerObject) : base(ownerObject, s_EventDispatcher)
        {
            focusController = new FocusController(new VisualElementFocusRing(visualTree));
            m_PanelSettings = ownerObject as PanelSettings;
            name = m_PanelSettings != null ? m_PanelSettings.name : "RuntimePanel";
            visualTree.RegisterCallback<FocusEvent, RuntimePanel>((e, p) => p.OnElementFocus(e), this, TrickleDown.TrickleDown);
        }

        internal override void Update()
        {
            if (m_PanelSettings != null) m_PanelSettings.ApplyPanelSettings();
            base.Update();
        }

        void OnElementFocus(FocusEvent evt) => UIElementsRuntimeUtility.defaultEventSystem.OnFocusEvent(this, evt);
    }

    public static partial class PointerCaptureHelper
    {
        static Unbound.UIElements.PointerCaptureState StateFor(IEventHandler handler) =>
            (handler as VisualElement)?.panel?.dispatcher?.m_UaPointerState;

        public static bool HasPointerCapture(this IEventHandler handler, int pointerId) => StateFor(handler)?.HasPointerCapture(handler, pointerId) ?? false;
        public static void CapturePointer(this IEventHandler handler, int pointerId) => StateFor(handler)?.CapturePointer(handler, pointerId);
        public static void ReleasePointer(this IEventHandler handler, int pointerId) => StateFor(handler)?.ReleasePointer(handler, pointerId);
        public static IEventHandler GetCapturingElement(this IPanel panel, int pointerId) => panel?.dispatcher?.m_UaPointerState.GetCapturingElement(pointerId);
        public static void ReleasePointer(this IPanel panel, int pointerId) => panel?.dispatcher?.m_UaPointerState.ReleasePointer(pointerId);
        internal static void ProcessPointerCapture(this IPanel panel, int pointerId) => panel?.dispatcher?.m_UaPointerState.ProcessPointerCapture(pointerId);
    }

    public static partial class MouseCaptureController
    {
        public static bool IsMouseCaptured() => RuntimePanel.s_EventDispatcher.m_UaPointerState.GetCapturingElement(PointerId.mousePointerId) != null;

        public static bool HasMouseCapture(this IEventHandler handler) => (handler as VisualElement).HasPointerCapture(PointerId.mousePointerId);

        public static void CaptureMouse(this IEventHandler handler)
        {
            if (handler is VisualElement ve)
            {
                ve.CapturePointer(PointerId.mousePointerId);
                ve.panel.ProcessPointerCapture(PointerId.mousePointerId);
            }
        }

        public static void ReleaseMouse(this IEventHandler handler)
        {
            if (handler is VisualElement ve)
            {
                ve.ReleasePointer(PointerId.mousePointerId);
                ve.panel.ProcessPointerCapture(PointerId.mousePointerId);
            }
        }

        public static void ReleaseMouse()
        {
            RuntimePanel.s_EventDispatcher.m_UaPointerState.ReleasePointer(PointerId.mousePointerId);
            RuntimePanel.s_EventDispatcher.m_UaPointerState.ProcessPointerCapture(PointerId.mousePointerId);
        }
    }

    // ---- scheduling ----

    public partial struct TimerState : IEquatable<TimerState>
    {
        public long start { get; set; }
        public long now { get; set; }
        public long deltaTime => now - start;

        public override bool Equals(object obj) => obj is TimerState t && Equals(t);
        public bool Equals(TimerState other) => start == other.start && now == other.now && deltaTime == other.deltaTime;
        public override int GetHashCode() => HashCode.Combine(start, now);
        public static bool operator ==(TimerState state1, TimerState state2) => state1.Equals(state2);
        public static bool operator !=(TimerState state1, TimerState state2) => !(state1 == state2);
    }

    public partial interface IVisualElementScheduler
    {
        IVisualElementScheduledItem Execute(Action<TimerState> timerUpdateEvent);
        IVisualElementScheduledItem Execute(Action updateEvent);
    }

    public partial interface IVisualElementScheduledItem
    {
        VisualElement element { get; }
        bool isActive { get; }
        void Resume();
        void Pause();
        void ExecuteLater(long delayMs);
        IVisualElementScheduledItem StartingIn(long delayMs);
        IVisualElementScheduledItem Every(long intervalMs);
        IVisualElementScheduledItem Until(Func<bool> stopCondition);
        IVisualElementScheduledItem ForDuration(long durationMs);
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>A scheduled callback of an element: runs while the element is on a panel, after its
    /// delay, then every interval (once without one), until its stop condition or duration ends.</summary>
    internal sealed class UaScheduledItem : IVisualElementScheduledItem
    {
        readonly Action<TimerState> m_Callback;
        internal long startMs;
        internal long delayMs;
        internal long intervalMs;
        internal long endTimeMs = -1;
        internal long lastMs;
        internal Func<bool> until;
        internal bool active;

        public VisualElement element { get; }
        public bool isActive => active;

        internal UaScheduledItem(VisualElement element, Action<TimerState> callback)
        {
            this.element = element;
            m_Callback = callback;
            element.RegisterCallback<AttachToPanelEvent>(_ => { if (active) Schedule(); });
            element.RegisterCallback<DetachFromPanelEvent>(_ => Unschedule());
            Resume();
        }

        UaScheduler Scheduler => (element.panel as Panel)?.uaScheduler;

        void Schedule()
        {
            startMs = Panel.TimeSinceStartupMs();
            lastMs = startMs;
            Scheduler?.Add(this);
        }

        void Unschedule() => Scheduler?.Remove(this);

        public void Resume()
        {
            if (active) return;
            active = true;
            if (element.panel != null) Schedule();
        }

        public void Pause()
        {
            if (!active) return;
            active = false;
            Unschedule();
        }

        public void ExecuteLater(long delayMs)
        {
            if (!active) Resume();
            this.delayMs = delayMs;
            startMs = Panel.TimeSinceStartupMs();
        }

        public IVisualElementScheduledItem StartingIn(long delayMs)
        {
            this.delayMs = delayMs;
            return this;
        }

        public IVisualElementScheduledItem Every(long intervalMs)
        {
            this.intervalMs = intervalMs;
            return this;
        }

        public IVisualElementScheduledItem Until(Func<bool> stopCondition)
        {
            until = stopCondition;
            return this;
        }

        public IVisualElementScheduledItem ForDuration(long durationMs)
        {
            endTimeMs = durationMs;
            return this;
        }

        /// <summary>Runs the item if it is due; whether it stays scheduled.</summary>
        internal bool Tick(long now)
        {
            if (until != null && until())
            {
                active = false;
                return false;
            }
            if (endTimeMs >= 0 && now - startMs > delayMs + endTimeMs)
            {
                active = false;
                return false;
            }
            if (now - startMs < delayMs) return true;
            if (intervalMs > 0 && lastMs != startMs && now - lastMs < intervalMs) return true;
            var state = new TimerState { start = lastMs, now = now };
            lastMs = now;
            m_Callback(state);
            if (intervalMs <= 0 && endTimeMs < 0 && until == null)
            {
                active = false;
                return false;
            }
            return active;
        }
    }

    internal sealed class UaScheduler
    {
        readonly List<UaScheduledItem> m_Items = new List<UaScheduledItem>();

        internal void Add(UaScheduledItem item)
        {
            if (!m_Items.Contains(item)) m_Items.Add(item);
        }

        internal void Remove(UaScheduledItem item) => m_Items.Remove(item);

        internal void UpdateScheduledEvents()
        {
            long now = Panel.TimeSinceStartupMs();
            foreach (var item in m_Items.ToArray())
                if (!item.Tick(now)) m_Items.Remove(item);
        }
    }

    /// <summary>Which element is under each pointer, and the over/out/enter/leave events when it changes.</summary>
    internal sealed class UaElementUnderPointer
    {
        readonly VisualElement[] m_Pending = new VisualElement[PointerId.maxPointers];
        readonly VisualElement[] m_Top = new VisualElement[PointerId.maxPointers];
        readonly EventBase[] m_Trigger = new EventBase[PointerId.maxPointers];
        readonly Vector2[] m_PickingPositions = new Vector2[PointerId.maxPointers];
        readonly bool[] m_Temporary = new bool[PointerId.maxPointers];

        internal VisualElement GetTopElementUnderPointer(int pointerId) => m_Pending[pointerId];

        internal VisualElement GetTopElementUnderPointer(int pointerId, out Vector2 pickPosition, out bool isTemporary)
        {
            pickPosition = m_PickingPositions[pointerId];
            isTemporary = m_Temporary[pointerId];
            return m_Pending[pointerId];
        }

        /// <summary>A pick without a triggering event: changes the pending element only if it differs from the committed one.</summary>
        internal void SetElementUnderPointer(VisualElement element, int pointerId, Vector2 pointerPosition)
        {
            m_Temporary[pointerId] = false;
            m_PickingPositions[pointerId] = pointerPosition;
            if (element == m_Top[pointerId]) return;
            m_Pending[pointerId] = element;
            m_Trigger[pointerId] = null;
        }

        internal void SetElementUnderPointer(VisualElement element, int pointerId, EventBase trigger) => Set(element, pointerId, trigger, false);
        internal void SetTemporaryElementUnderPointer(VisualElement element, int pointerId, EventBase trigger) => Set(element, pointerId, trigger, true);

        static Vector2 EventPosition(EventBase trigger)
        {
            if (trigger is IPointerEvent pe) return new Vector2(pe.position.x, pe.position.y);
            if (trigger is IMouseEvent me) return me.mousePosition;
            return new Vector2(float.MinValue, float.MinValue);
        }

        void Set(VisualElement element, int pointerId, EventBase trigger, bool temporary)
        {
            m_Temporary[pointerId] = temporary;
            m_PickingPositions[pointerId] = EventPosition(trigger);
            m_Pending[pointerId] = element;
            if (element != m_Top[pointerId] && m_Trigger[pointerId] == null && trigger is IPointerOrMouseEvent) m_Trigger[pointerId] = trigger;
        }

        internal void CommitElementUnderPointers(EventDispatcher dispatcher, ContextType contextType)
        {
            for (int i = 0; i < PointerId.maxPointers; i++)
            {
                var trigger = m_Trigger[i];
                var previous = m_Top[i];
                var current = m_Pending[i];
                if (current == previous)
                {
                    if (trigger != null) m_PickingPositions[i] = EventPosition(trigger);
                    continue;
                }
                m_Top[i] = current;
                if (trigger == null)
                {
                    using (new UaEventDispatcherGate(dispatcher))
                    {
                        var position = PointerDeviceState.GetPointerPosition(i, contextType);
                        m_PickingPositions[i] = position;
                        SendOverOut(previous, current, null, position, i);
                        SendEnterLeave<PointerLeaveEvent, PointerEnterEvent>(previous, current, null, position, i);
                        if (i == PointerId.mousePointerId)
                        {
                            SendMouseOverOut(previous, current, null, position);
                            SendMouseEnterLeave(previous, current, null, position);
                        }
                    }
                }
                else
                {
                    var position = (Vector2)((IPointerOrMouseEvent)trigger).position;
                    m_PickingPositions[i] = position;
                    long type = trigger.eventTypeId;
                    if (type == PointerMoveEvent.TypeId() || type == PointerDownEvent.TypeId() || type == PointerUpEvent.TypeId() || type == PointerCancelEvent.TypeId())
                    {
                        using (new UaEventDispatcherGate(dispatcher))
                        {
                            var pe = (IPointerEvent)trigger;
                            SendOverOut(previous, current, pe, position, i);
                            SendEnterLeave<PointerLeaveEvent, PointerEnterEvent>(previous, current, pe, position, i);
                            if (pe.pointerId == PointerId.mousePointerId && ((IPointerEventInternal)trigger).compatibilityMouseEvent is IMouseEvent compat)
                            {
                                SendMouseOverOut(previous, current, compat, position);
                                SendMouseEnterLeave(previous, current, compat, position);
                            }
                        }
                    }
                    else if (type == WheelEvent.TypeId())
                    {
                        using (new UaEventDispatcherGate(dispatcher))
                        {
                            SendMouseOverOut(previous, current, (IMouseEvent)trigger, position);
                            SendMouseEnterLeave(previous, current, (IMouseEvent)trigger, position);
                        }
                    }
                }
                m_Trigger[i] = null;
            }
        }

        static void SendEnterLeave<TLeave, TEnter>(VisualElement previous, VisualElement current, IPointerEvent trigger, Vector2 position, int pointerId)
            where TLeave : PointerEventBase<TLeave>, new() where TEnter : PointerEventBase<TEnter>, new()
        {
            if (previous != null && previous.panel == null) previous = null;
            int depthPrevious = 0;
            for (var e = previous; e != null; e = e.hierarchy.parent) depthPrevious++;
            int depthCurrent = 0;
            for (var e = current; e != null; e = e.hierarchy.parent) depthCurrent++;
            var a = previous;
            var b = current;
            while (depthPrevious > depthCurrent)
            {
                using (var leave = PointerEventBase<TLeave>.GetPooled(trigger, position, pointerId))
                {
                    leave.elementTarget = a;
                    a.SendEvent(leave);
                }
                depthPrevious--;
                a = a.hierarchy.parent;
            }
            var entered = new List<VisualElement>();
            while (depthCurrent > depthPrevious)
            {
                entered.Add(b);
                depthCurrent--;
                b = b.hierarchy.parent;
            }
            while (a != b)
            {
                using (var leave = PointerEventBase<TLeave>.GetPooled(trigger, position, pointerId))
                {
                    leave.elementTarget = a;
                    a.SendEvent(leave);
                }
                entered.Add(b);
                a = a.hierarchy.parent;
                b = b.hierarchy.parent;
            }
            for (int i = entered.Count - 1; i >= 0; i--)
            {
                using (var enter = PointerEventBase<TEnter>.GetPooled(trigger, position, pointerId))
                {
                    enter.elementTarget = entered[i];
                    entered[i].SendEvent(enter);
                }
            }
        }

        static void SendOverOut(VisualElement previous, VisualElement current, IPointerEvent trigger, Vector2 position, int pointerId)
        {
            if (previous != null && previous.panel != null)
            {
                using (var e = PointerEventBase<PointerOutEvent>.GetPooled(trigger, position, pointerId))
                {
                    e.elementTarget = previous;
                    previous.SendEvent(e);
                }
            }
            if (current != null)
            {
                using (var e = PointerEventBase<PointerOverEvent>.GetPooled(trigger, position, pointerId))
                {
                    e.elementTarget = current;
                    current.SendEvent(e);
                }
            }
        }

        static void SendMouseEnterLeave(VisualElement previous, VisualElement current, IMouseEvent trigger, Vector2 position)
        {
            if (previous != null && previous.panel == null) previous = null;
            int depthPrevious = 0;
            for (var e = previous; e != null; e = e.hierarchy.parent) depthPrevious++;
            int depthCurrent = 0;
            for (var e = current; e != null; e = e.hierarchy.parent) depthCurrent++;
            var a = previous;
            var b = current;
            while (depthPrevious > depthCurrent)
            {
                using (var leave = MouseEventBase<MouseLeaveEvent>.GetPooled(trigger, position))
                {
                    leave.elementTarget = a;
                    a.SendEvent(leave);
                }
                depthPrevious--;
                a = a.hierarchy.parent;
            }
            var entered = new List<VisualElement>();
            while (depthCurrent > depthPrevious)
            {
                entered.Add(b);
                depthCurrent--;
                b = b.hierarchy.parent;
            }
            while (a != b)
            {
                using (var leave = MouseEventBase<MouseLeaveEvent>.GetPooled(trigger, position))
                {
                    leave.elementTarget = a;
                    a.SendEvent(leave);
                }
                entered.Add(b);
                a = a.hierarchy.parent;
                b = b.hierarchy.parent;
            }
            for (int i = entered.Count - 1; i >= 0; i--)
            {
                using (var enter = MouseEventBase<MouseEnterEvent>.GetPooled(trigger, position))
                {
                    enter.elementTarget = entered[i];
                    entered[i].SendEvent(enter);
                }
            }
        }

        static void SendMouseOverOut(VisualElement previous, VisualElement current, IMouseEvent trigger, Vector2 position)
        {
            if (previous != null && previous.panel != null)
            {
                using (var e = MouseEventBase<MouseOutEvent>.GetPooled(trigger, position))
                {
                    e.elementTarget = previous;
                    previous.SendEvent(e);
                }
            }
            if (current != null)
            {
                using (var e = MouseEventBase<MouseOverEvent>.GetPooled(trigger, position))
                {
                    e.elementTarget = current;
                    current.SendEvent(e);
                }
            }
        }
    }

    /// <summary>Pointer capture per pointer id: requests take effect (with capture events) when the
    /// panel processes them after each pointer event.</summary>
    internal sealed class PointerCaptureState
    {
        readonly IEventHandler[] m_Pending = new IEventHandler[PointerId.maxPointers];
        readonly IEventHandler[] m_Capture = new IEventHandler[PointerId.maxPointers];
        readonly bool[] m_SendCompatibility = new bool[PointerId.maxPointers];

        internal PointerCaptureState() => Reset();

        internal void Reset()
        {
            for (int i = 0; i < m_Capture.Length; i++)
            {
                m_Pending[i] = null;
                m_Capture[i] = null;
                m_SendCompatibility[i] = true;
            }
        }

        internal IEventHandler GetCapturingElement(int pointerId) => m_Pending[pointerId];
        internal bool HasPointerCapture(IEventHandler handler, int pointerId) => m_Pending[pointerId] == handler;
        internal void CapturePointer(IEventHandler handler, int pointerId) => m_Pending[pointerId] = handler;
        internal void ReleasePointer(int pointerId) => m_Pending[pointerId] = null;

        internal void ReleasePointer(IEventHandler handler, int pointerId)
        {
            if (handler == m_Pending[pointerId]) m_Pending[pointerId] = null;
        }

        internal void ProcessPointerCapture(int pointerId)
        {
            var current = m_Capture[pointerId];
            if (current == m_Pending[pointerId]) return;
            if (current != null)
            {
                using (var e = PointerCaptureOutEvent.GetPooled(current, m_Pending[pointerId], pointerId)) current.SendEvent(e);
                if (pointerId == PointerId.mousePointerId && m_Capture[pointerId] == current)
                    using (var e = MouseCaptureOutEvent.GetPooled(current, m_Pending[pointerId], pointerId)) current.SendEvent(e);
            }
            var next = m_Pending[pointerId];
            if (next != null)
            {
                using (var e = PointerCaptureEvent.GetPooled(next, m_Capture[pointerId], pointerId)) next.SendEvent(e);
                if (pointerId == PointerId.mousePointerId && m_Pending[pointerId] == next)
                    using (var e = MouseCaptureEvent.GetPooled(next, m_Capture[pointerId], pointerId)) next.SendEvent(e);
            }
            m_Capture[pointerId] = m_Pending[pointerId];
        }

        internal void ActivateCompatibilityMouseEvents(int pointerId) => m_SendCompatibility[pointerId] = true;
        internal void PreventCompatibilityMouseEvents(int pointerId) => m_SendCompatibility[pointerId] = false;
        internal bool ShouldSendCompatibilityMouseEvents(IPointerEvent evt) => evt.isPrimary && m_SendCompatibility[evt.pointerId];
    }
}
