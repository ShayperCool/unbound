using System;
using System.Collections.Generic;

// The concrete UI Toolkit events with Unity's propagation (bubbles, trickles down, skips disabled
// elements), dispatch targets (element under the pointer or the capturing element, the focused
// element, an assigned target) and pre/post dispatch work (compatibility mouse events, pointer
// state, click detection, focus changes).

namespace UnityEngine.UIElements
{
    public static partial class PointerId
    {
        public static readonly int maxPointers = 32;
        public static readonly int invalidPointerId = -1;
        public static readonly int mousePointerId = 0;
        public static readonly int touchPointerIdBase = 1;
        public static readonly int touchPointerCount = 20;
        public static readonly int penPointerIdBase = touchPointerIdBase + touchPointerCount;
        public static readonly int penPointerCount = 2;
        internal static readonly int[] hoveringPointers = { mousePointerId };
    }

    public static partial class PointerType
    {
        public static readonly string mouse = "mouse";
        public static readonly string touch = "touch";
        public static readonly string pen = "pen";
        public static readonly string unknown = "";

        internal static string GetPointerType(int pointerId) =>
            pointerId == PointerId.mousePointerId ? mouse : pointerId == PointerId.penPointerIdBase ? pen : touch;

        internal static bool IsDirectManipulationDevice(string pointerType) => ReferenceEquals(pointerType, touch) || ReferenceEquals(pointerType, pen);
    }

    internal static partial class PointerDeviceState
    {
        struct PointerLocation
        {
            internal Vector2 Position;
            internal IPanel Panel;
        }

        static readonly PointerLocation[] s_EditorPointerLocations = new PointerLocation[PointerId.maxPointers];
        static readonly PointerLocation[] s_PlayerPointerLocations = new PointerLocation[PointerId.maxPointers];
        static readonly int[] s_PressedButtons = new int[PointerId.maxPointers];
        static readonly IPanel[] s_PlayerPanelWithSoftPointerCapture = new IPanel[PointerId.maxPointers];

        internal static void Reset()
        {
            for (int i = 0; i < PointerId.maxPointers; i++)
            {
                s_EditorPointerLocations[i] = default;
                s_PlayerPointerLocations[i] = default;
                s_PressedButtons[i] = 0;
                s_PlayerPanelWithSoftPointerCapture[i] = null;
            }
        }

        internal static void RemovePanelData(IPanel panel)
        {
            for (int i = 0; i < PointerId.maxPointers; i++)
            {
                if (s_EditorPointerLocations[i].Panel == panel) s_EditorPointerLocations[i] = default;
                if (s_PlayerPointerLocations[i].Panel == panel) s_PlayerPointerLocations[i] = default;
                if (s_PlayerPanelWithSoftPointerCapture[i] == panel) s_PlayerPanelWithSoftPointerCapture[i] = null;
            }
        }

        public static void SavePointerPosition(int pointerId, Vector2 position, IPanel panel, ContextType contextType)
        {
            var location = new PointerLocation { Position = position, Panel = panel };
            if (contextType == ContextType.Player) s_PlayerPointerLocations[pointerId] = location;
            else s_EditorPointerLocations[pointerId] = location;
        }

        public static void PressButton(int pointerId, int buttonId) => s_PressedButtons[pointerId] |= 1 << buttonId;
        public static void ReleaseButton(int pointerId, int buttonId) => s_PressedButtons[pointerId] &= ~(1 << buttonId);
        public static void ReleaseAllButtons(int pointerId) => s_PressedButtons[pointerId] = 0;

        public static Vector2 GetPointerPosition(int pointerId, ContextType contextType) =>
            contextType == ContextType.Player ? s_PlayerPointerLocations[pointerId].Position : s_EditorPointerLocations[pointerId].Position;

        public static IPanel GetPanel(int pointerId, ContextType contextType) =>
            contextType == ContextType.Player ? s_PlayerPointerLocations[pointerId].Panel : s_EditorPointerLocations[pointerId].Panel;

        public static int GetPressedButtons(int pointerId) => s_PressedButtons[pointerId];
        internal static bool HasAdditionalPressedButtons(int pointerId, int exceptButtonId) => (s_PressedButtons[pointerId] & ~(1 << exceptButtonId)) != 0;
        internal static void SetPlayerPanelWithSoftPointerCapture(int pointerId, IPanel panel) => s_PlayerPanelWithSoftPointerCapture[pointerId] = panel;
        internal static IPanel GetPlayerPanelWithSoftPointerCapture(int pointerId) => s_PlayerPanelWithSoftPointerCapture[pointerId];
    }

    public partial interface IPointerEvent
    {
        int pointerId { get; }
        string pointerType { get; }
        bool isPrimary { get; }
        int button { get; }
        int pressedButtons { get; }
        Vector3 position { get; }
        Vector3 localPosition { get; }
        Vector3 deltaPosition { get; }
        float deltaTime { get; }
        int clickCount { get; }
        float pressure { get; }
        float tangentialPressure { get; }
        float altitudeAngle { get; }
        float azimuthAngle { get; }
        float twist { get; }
        Vector2 tilt { get; }
        PenStatus penStatus { get; }
        Vector2 radius { get; }
        Vector2 radiusVariance { get; }
        EventModifiers modifiers { get; }
        bool shiftKey { get; }
        bool ctrlKey { get; }
        bool commandKey { get; }
        bool altKey { get; }
        bool actionKey { get; }
    }

    internal partial interface IPointerEventInternal
    {
        bool triggeredByOS { get; set; }
        IMouseEvent compatibilityMouseEvent { get; set; }
        int displayIndex { get; set; }
    }

    internal partial interface IPointerOrMouseEvent
    {
        int pointerId { get; }
        Vector3 position { get; }
    }

    public partial interface IMouseEvent
    {
        EventModifiers modifiers { get; }
        Vector2 mousePosition { get; }
        Vector2 localMousePosition { get; }
        Vector2 mouseDelta { get; }
        int clickCount { get; }
        int button { get; }
        int pressedButtons { get; }
        bool shiftKey { get; }
        bool ctrlKey { get; }
        bool commandKey { get; }
        bool altKey { get; }
        bool actionKey { get; }
    }

    internal partial interface IMouseEventInternal
    {
        bool triggeredByOS { get; set; }
        IPointerEvent sourcePointerEvent { get; set; }
    }

    internal static class UaModifiers
    {
        internal static bool Shift(EventModifiers m) => (m & EventModifiers.Shift) != 0;
        internal static bool Ctrl(EventModifiers m) => (m & EventModifiers.Control) != 0;
        internal static bool Command(EventModifiers m) => (m & EventModifiers.Command) != 0;
        internal static bool Alt(EventModifiers m) => (m & EventModifiers.Alt) != 0;
        internal static bool Action(EventModifiers m) =>
            Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer ? Command(m) : Ctrl(m);
    }

    public abstract partial class PointerEventBase<T> : EventBase<T>, IPointerEvent, IPointerEventInternal, IPointerOrMouseEvent where T : PointerEventBase<T>, new()
    {
        public int pointerId { get; protected set; }
        public string pointerType { get; protected set; }
        public bool isPrimary { get; protected set; }
        public int button { get; protected set; }
        public int pressedButtons { get; protected set; }
        public Vector3 position { get; protected set; }
        public Vector3 localPosition { get; protected set; }
        public Vector3 deltaPosition { get; protected set; }
        public float deltaTime { get; protected set; }
        public int clickCount { get; protected set; }
        public float pressure { get; protected set; }
        public float tangentialPressure { get; protected set; }
        public float altitudeAngle { get; protected set; }
        public float azimuthAngle { get; protected set; }
        public float twist { get; protected set; }
        public Vector2 tilt { get; protected set; }
        public PenStatus penStatus { get; protected set; }
        public Vector2 radius { get; protected set; }
        public Vector2 radiusVariance { get; protected set; }
        public EventModifiers modifiers { get; protected set; }
        public bool shiftKey => UaModifiers.Shift(modifiers);
        public bool ctrlKey => UaModifiers.Ctrl(modifiers);
        public bool commandKey => UaModifiers.Command(modifiers);
        public bool altKey => UaModifiers.Alt(modifiers);
        public bool actionKey => UaModifiers.Action(modifiers);

        bool IPointerEventInternal.triggeredByOS { get; set; }
        IMouseEvent IPointerEventInternal.compatibilityMouseEvent { get; set; }
        int IPointerEventInternal.displayIndex { get; set; }

        public override IEventHandler currentTarget
        {
            get => base.currentTarget;
            internal set
            {
                base.currentTarget = value;
                localPosition = value is VisualElement ve ? (Vector3)ve.WorldToLocal(position) : position;
            }
        }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            pointerId = 0;
            pointerType = PointerType.unknown;
            isPrimary = false;
            button = -1;
            pressedButtons = 0;
            position = Vector3.zero;
            localPosition = Vector3.zero;
            deltaPosition = Vector3.zero;
            deltaTime = 0;
            clickCount = 0;
            pressure = 0;
            tangentialPressure = 0;
            altitudeAngle = 0;
            azimuthAngle = 0;
            tilt = Vector2.zero;
            twist = 0;
            penStatus = PenStatus.None;
            radius = Vector2.zero;
            radiusVariance = Vector2.zero;
            modifiers = EventModifiers.None;
            var self = (IPointerEventInternal)this;
            self.triggeredByOS = false;
            if (self.compatibilityMouseEvent != null)
            {
                ((IDisposable)self.compatibilityMouseEvent).Dispose();
                self.compatibilityMouseEvent = null;
            }
        }

        static bool IsMouse(Event e)
        {
            var t = e.rawType;
            return t == EventType.MouseMove || t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag ||
                   t == EventType.ContextClick || t == EventType.MouseEnterWindow || t == EventType.MouseLeaveWindow;
        }

        public static T GetPooled(Event systemEvent)
        {
            T e = GetPooled();
            switch (systemEvent.pointerType)
            {
                case UnityEngine.PointerType.Touch:
                    e.pointerType = PointerType.touch;
                    e.pointerId = PointerId.touchPointerIdBase;
                    break;
                case UnityEngine.PointerType.Pen:
                    e.pointerType = PointerType.pen;
                    e.pointerId = PointerId.penPointerIdBase;
                    break;
                default:
                    e.pointerType = PointerType.mouse;
                    e.pointerId = PointerId.mousePointerId;
                    break;
            }
            e.isPrimary = true;
            e.imguiEvent = systemEvent;
            var raw = systemEvent.rawType;
            if (raw == EventType.MouseDown || raw == EventType.TouchDown)
            {
                PointerDeviceState.PressButton(e.pointerId, systemEvent.button);
                e.button = systemEvent.button;
            }
            else if (raw == EventType.MouseUp || raw == EventType.TouchUp)
            {
                PointerDeviceState.ReleaseButton(e.pointerId, systemEvent.button);
                e.button = systemEvent.button;
            }
            else if (raw == EventType.MouseMove || raw == EventType.TouchMove)
            {
                e.button = -1;
            }
            e.pressedButtons = PointerDeviceState.GetPressedButtons(e.pointerId);
            e.position = systemEvent.mousePosition;
            e.localPosition = systemEvent.mousePosition;
            e.deltaPosition = systemEvent.delta;
            e.clickCount = systemEvent.clickCount;
            e.modifiers = systemEvent.modifiers;
            e.tilt = systemEvent.tilt;
            e.twist = systemEvent.twist;
            e.pressure = systemEvent.pointerType == UnityEngine.PointerType.Mouse ? (e.pressedButtons == 0 ? 0f : 0.5f) : systemEvent.pressure;
            e.tangentialPressure = 0;
            ((IPointerEventInternal)e).triggeredByOS = true;
            return e;
        }

        internal static T GetPooled(EventType eventType, Vector3 mousePosition, Vector2 delta, int button, int clickCount, EventModifiers modifiers, int displayIndex)
        {
            T e = GetPooled();
            e.pointerId = PointerId.mousePointerId;
            e.pointerType = PointerType.mouse;
            e.isPrimary = true;
            ((IPointerEventInternal)e).displayIndex = displayIndex;
            if (eventType == EventType.MouseDown)
            {
                PointerDeviceState.PressButton(e.pointerId, button);
                e.button = button;
            }
            else if (eventType == EventType.MouseUp)
            {
                PointerDeviceState.ReleaseButton(e.pointerId, button);
                e.button = button;
            }
            else
            {
                e.button = -1;
            }
            e.pressedButtons = PointerDeviceState.GetPressedButtons(e.pointerId);
            e.position = mousePosition;
            e.localPosition = mousePosition;
            e.deltaPosition = delta;
            e.clickCount = clickCount;
            e.modifiers = modifiers;
            e.pressure = e.pressedButtons == 0 ? 0f : 0.5f;
            ((IPointerEventInternal)e).triggeredByOS = true;
            return e;
        }

        public static T GetPooled(Touch touch, EventModifiers modifiers = EventModifiers.None)
        {
            T e = GetPooled();
            e.pointerId = touch.fingerId + PointerId.touchPointerIdBase;
            e.pointerType = PointerType.touch;
            bool primary = true;
            for (int i = PointerId.touchPointerIdBase; i < PointerId.touchPointerIdBase + PointerId.touchPointerCount; i++)
            {
                if (i != e.pointerId && PointerDeviceState.GetPressedButtons(i) != 0)
                {
                    primary = false;
                    break;
                }
            }
            e.isPrimary = primary;
            if (touch.phase == TouchPhase.Began)
            {
                PointerDeviceState.PressButton(e.pointerId, 0);
                e.button = 0;
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                PointerDeviceState.ReleaseButton(e.pointerId, 0);
                e.button = 0;
            }
            e.pressedButtons = PointerDeviceState.GetPressedButtons(e.pointerId);
            e.position = touch.position;
            e.localPosition = touch.position;
            e.deltaPosition = touch.deltaPosition;
            e.deltaTime = touch.deltaTime;
            e.clickCount = touch.tapCount;
            e.pressure = Mathf.Abs(touch.maximumPossiblePressure) > 1e-30f ? touch.pressure / touch.maximumPossiblePressure : 1f;
            e.altitudeAngle = touch.altitudeAngle;
            e.azimuthAngle = touch.azimuthAngle;
            e.radius = new Vector2(touch.radius, touch.radius);
            e.radiusVariance = new Vector2(touch.radiusVariance, touch.radiusVariance);
            e.modifiers = modifiers;
            ((IPointerEventInternal)e).triggeredByOS = true;
            return e;
        }

        public static T GetPooled(PenData pen, EventModifiers modifiers = EventModifiers.None)
        {
            T e = GetPooled();
            e.pointerId = PointerId.penPointerIdBase;
            e.pointerType = PointerType.pen;
            e.isPrimary = true;
            if (pen.contactType == PenEventType.PenDown)
            {
                PointerDeviceState.PressButton(e.pointerId, 0);
                e.button = 0;
            }
            else if (pen.contactType == PenEventType.PenUp)
            {
                PointerDeviceState.ReleaseButton(e.pointerId, 0);
                e.button = 0;
            }
            e.pressedButtons = PointerDeviceState.GetPressedButtons(e.pointerId);
            e.position = pen.position;
            e.localPosition = pen.position;
            e.deltaPosition = pen.deltaPos;
            e.pressure = pen.pressure;
            e.tilt = pen.tilt;
            e.twist = pen.twist;
            e.penStatus = pen.penStatus;
            e.modifiers = modifiers;
            ((IPointerEventInternal)e).triggeredByOS = true;
            return e;
        }

        internal static T GetPooled(IPointerEvent triggerEvent, Vector2 position, int pointerId)
        {
            if (triggerEvent != null) return GetPooled(triggerEvent);
            T e = GetPooled();
            e.position = position;
            e.localPosition = position;
            e.pointerId = pointerId;
            e.pointerType = PointerType.GetPointerType(pointerId);
            return e;
        }

        public static T GetPooled(IPointerEvent triggerEvent)
        {
            T e = GetPooled();
            if (triggerEvent != null)
            {
                e.pointerId = triggerEvent.pointerId;
                e.pointerType = triggerEvent.pointerType;
                e.isPrimary = triggerEvent.isPrimary;
                e.button = triggerEvent.button;
                e.pressedButtons = triggerEvent.pressedButtons;
                e.position = triggerEvent.position;
                e.localPosition = triggerEvent.localPosition;
                e.deltaPosition = triggerEvent.deltaPosition;
                e.deltaTime = triggerEvent.deltaTime;
                e.clickCount = triggerEvent.clickCount;
                e.pressure = triggerEvent.pressure;
                e.tangentialPressure = triggerEvent.tangentialPressure;
                e.altitudeAngle = triggerEvent.altitudeAngle;
                e.azimuthAngle = triggerEvent.azimuthAngle;
                e.twist = triggerEvent.twist;
                e.tilt = triggerEvent.tilt;
                e.penStatus = triggerEvent.penStatus;
                e.radius = triggerEvent.radius;
                e.radiusVariance = triggerEvent.radiusVariance;
                e.modifiers = triggerEvent.modifiers;
                if (triggerEvent is IPointerEventInternal internalEvent)
                    ((IPointerEventInternal)e).triggeredByOS |= internalEvent.triggeredByOS;
            }
            return e;
        }

        internal void UaSetClickCount(int count) => clickCount = count;

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((IPointerEventInternal)this).triggeredByOS)
                PointerDeviceState.SavePointerPosition(pointerId, position, panel, panel.contextType);
            ((EventBase)((IPointerEventInternal)this).compatibilityMouseEvent)?.PreDispatch(panel);
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            var p = panel as BaseVisualElementPanel;
            for (int i = 0; i < PointerId.maxPointers; i++) p?.ProcessPointerCapture(i);
            if (((IPointerEventInternal)this).triggeredByOS) p?.CommitElementUnderPointers();
            ((EventBase)((IPointerEventInternal)this).compatibilityMouseEvent)?.PostDispatch(panel);
            base.PostDispatch(panel);
        }

        internal override void Dispatch(BaseVisualElementPanel panel) =>
            Unbound.UIElements.Dispatching.DispatchToCapturingElementOrElementUnderPointer(this, panel, pointerId, position);

        protected PointerEventBase() { LocalInit(); }
    }

    public sealed partial class PointerDownEvent : PointerEventBase<PointerDownEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            ((IPointerEventInternal)this).triggeredByOS = true;
        }
        public PointerDownEvent() { LocalInit(); }

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((BaseVisualElementPanel)panel).ShouldSendCompatibilityMouseEvents(this))
                ((IPointerEventInternal)this).compatibilityMouseEvent = MouseDownEvent.UaFromPointer(this);
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            panel.focusController.SwitchFocusOnEvent(panel.focusController.GetLeafFocusedElement(), this);
            panel.dispatcher.m_UaClickDetector.ProcessEvent(this);
            base.PostDispatch(panel);
        }
    }

    public sealed partial class PointerMoveEvent : PointerEventBase<PointerMoveEvent>
    {
        internal bool isHandledByDraggable { get; set; }
        internal bool isPointerUpDown => button >= 0;
        internal bool isPointerDown => button >= 0 && (pressedButtons & (1 << button)) != 0;
        internal bool isPointerUp => button >= 0 && (pressedButtons & (1 << button)) == 0;

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            ((IPointerEventInternal)this).triggeredByOS = true;
            isHandledByDraggable = false;
        }
        public PointerMoveEvent() { LocalInit(); }

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((BaseVisualElementPanel)panel).ShouldSendCompatibilityMouseEvents(this))
            {
                IMouseEvent compat;
                if (imguiEvent != null && imguiEvent.rawType == EventType.MouseDown) compat = MouseDownEvent.UaFromPointer(this);
                else if (imguiEvent != null && imguiEvent.rawType == EventType.MouseUp) compat = MouseUpEvent.UaFromPointer(this);
                else compat = MouseMoveEvent.UaFromPointer(this);
                ((IPointerEventInternal)this).compatibilityMouseEvent = compat;
            }
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            panel.dispatcher.m_UaClickDetector.ProcessEvent(this);
            base.PostDispatch(panel);
        }
    }

    public sealed partial class PointerUpEvent : PointerEventBase<PointerUpEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            ((IPointerEventInternal)this).triggeredByOS = true;
        }
        public PointerUpEvent() { LocalInit(); }

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((BaseVisualElementPanel)panel).ShouldSendCompatibilityMouseEvents(this))
                ((IPointerEventInternal)this).compatibilityMouseEvent = MouseUpEvent.UaFromPointer(this);
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            panel.dispatcher.m_UaClickDetector.ProcessEvent(this);
            if (PointerType.IsDirectManipulationDevice(pointerType))
            {
                panel.ReleasePointer(pointerId);
                (panel as BaseVisualElementPanel)?.ClearCachedElementUnderPointer(pointerId, this);
            }
            base.PostDispatch(panel);
            ((BaseVisualElementPanel)panel).ActivateCompatibilityMouseEvents(pointerId);
        }
    }

    public sealed partial class PointerCancelEvent : PointerEventBase<PointerCancelEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            ((IPointerEventInternal)this).triggeredByOS = true;
        }
        public PointerCancelEvent() { LocalInit(); }

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((BaseVisualElementPanel)panel).ShouldSendCompatibilityMouseEvents(this))
                ((IPointerEventInternal)this).compatibilityMouseEvent = MouseUpEvent.UaFromPointer(this);
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            panel.dispatcher.m_UaClickDetector.ProcessEvent(this);
            if (PointerType.IsDirectManipulationDevice(pointerType))
            {
                panel.ReleasePointer(pointerId);
                (panel as BaseVisualElementPanel)?.ClearCachedElementUnderPointer(pointerId, this);
            }
            base.PostDispatch(panel);
            ((BaseVisualElementPanel)panel).ActivateCompatibilityMouseEvents(pointerId);
        }
    }

    public sealed partial class PointerStationaryEvent : PointerEventBase<PointerStationaryEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            ((IPointerEventInternal)this).triggeredByOS = true;
        }
        public PointerStationaryEvent() { LocalInit(); }
    }

    public sealed partial class ClickEvent : PointerEventBase<ClickEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
        public ClickEvent() { LocalInit(); }

        internal static ClickEvent GetPooled(IPointerEvent pointerEvent, int clickCount)
        {
            var e = GetPooled(pointerEvent);
            e.UaSetClickCount(clickCount);
            return e;
        }
    }

    public sealed partial class PointerEnterEvent : PointerEventBase<PointerEnterEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.TricklesDown;
        public PointerEnterEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public sealed partial class PointerLeaveEvent : PointerEventBase<PointerLeaveEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.TricklesDown;
        public PointerLeaveEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public sealed partial class PointerOverEvent : PointerEventBase<PointerOverEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;
        public PointerOverEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public sealed partial class PointerOutEvent : PointerEventBase<PointerOutEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;
        public PointerOutEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    // ---- mouse ----

    public abstract partial class MouseEventBase<T> : EventBase<T>, IMouseEvent, IMouseEventInternal, IPointerOrMouseEvent where T : MouseEventBase<T>, new()
    {
        public EventModifiers modifiers { get; protected set; }
        public Vector2 mousePosition { get; protected set; }
        public Vector2 localMousePosition { get; internal set; }
        public Vector2 mouseDelta { get; protected set; }
        public int clickCount { get; protected set; }
        public int button { get; protected set; }
        public int pressedButtons { get; protected set; }
        public bool shiftKey => UaModifiers.Shift(modifiers);
        public bool ctrlKey => UaModifiers.Ctrl(modifiers);
        public bool commandKey => UaModifiers.Command(modifiers);
        public bool altKey => UaModifiers.Alt(modifiers);
        public bool actionKey => UaModifiers.Action(modifiers);

        bool IMouseEventInternal.triggeredByOS { get; set; }
        IPointerEvent IMouseEventInternal.sourcePointerEvent { get; set; }
        int IPointerOrMouseEvent.pointerId => PointerId.mousePointerId;
        Vector3 IPointerOrMouseEvent.position => mousePosition;

        public override IEventHandler currentTarget
        {
            get => base.currentTarget;
            internal set
            {
                base.currentTarget = value;
                localMousePosition = value is VisualElement ve ? ve.WorldToLocal(mousePosition) : mousePosition;
            }
        }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            modifiers = EventModifiers.None;
            mousePosition = Vector2.zero;
            localMousePosition = Vector2.zero;
            mouseDelta = Vector2.zero;
            clickCount = 0;
            button = 0;
            pressedButtons = 0;
            ((IMouseEventInternal)this).triggeredByOS = false;
            ((IMouseEventInternal)this).sourcePointerEvent = null;
        }

        protected internal override void PreDispatch(IPanel panel)
        {
            base.PreDispatch(panel);
            if (((IMouseEventInternal)this).sourcePointerEvent == null && ((IMouseEventInternal)this).triggeredByOS)
                PointerDeviceState.SavePointerPosition(PointerId.mousePointerId, mousePosition, panel, panel.contextType);
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            if (((IMouseEventInternal)this).sourcePointerEvent is EventBase source)
            {
                if (isPropagationStopped) source.StopPropagation();
                if (isImmediatePropagationStopped) source.StopImmediatePropagation();
                source.processedByFocusController |= processedByFocusController;
            }
            base.PostDispatch(panel);
        }

        internal override void Dispatch(BaseVisualElementPanel panel) =>
            Unbound.UIElements.Dispatching.DispatchToCapturingElementOrElementUnderPointer(this, panel, PointerId.mousePointerId, mousePosition);

        public static T GetPooled(Event systemEvent)
        {
            T e = GetPooled();
            e.imguiEvent = systemEvent;
            if (systemEvent != null)
            {
                e.modifiers = systemEvent.modifiers;
                e.mousePosition = systemEvent.mousePosition;
                e.localMousePosition = systemEvent.mousePosition;
                e.mouseDelta = systemEvent.delta;
                e.button = systemEvent.button;
                e.pressedButtons = PointerDeviceState.GetPressedButtons(PointerId.mousePointerId);
                e.clickCount = systemEvent.clickCount;
                ((IMouseEventInternal)e).triggeredByOS = true;
            }
            return e;
        }

        public static T GetPooled(Vector2 position, int button, int clickCount, Vector2 delta, EventModifiers modifiers = EventModifiers.None) =>
            GetPooled(position, button, clickCount, delta, modifiers, false);

        internal static T GetPooled(Vector2 position, int button, int clickCount, Vector2 delta, EventModifiers modifiers, bool fromOS)
        {
            T e = GetPooled();
            e.modifiers = modifiers;
            e.mousePosition = position;
            e.localMousePosition = position;
            e.mouseDelta = delta;
            e.button = button;
            e.pressedButtons = PointerDeviceState.GetPressedButtons(PointerId.mousePointerId);
            e.clickCount = clickCount;
            ((IMouseEventInternal)e).triggeredByOS = fromOS;
            return e;
        }

        internal static T GetPooled(IMouseEvent triggerEvent, Vector2 mousePosition)
        {
            if (triggerEvent != null) return GetPooled(triggerEvent);
            T e = GetPooled();
            e.mousePosition = mousePosition;
            e.localMousePosition = mousePosition;
            return e;
        }

        public static T GetPooled(IMouseEvent triggerEvent)
        {
            T e = GetPooled(triggerEvent as EventBase);
            if (triggerEvent != null)
            {
                e.modifiers = triggerEvent.modifiers;
                e.mousePosition = triggerEvent.mousePosition;
                e.localMousePosition = triggerEvent.mousePosition;
                e.mouseDelta = triggerEvent.mouseDelta;
                e.button = triggerEvent.button;
                e.pressedButtons = triggerEvent.pressedButtons;
                e.clickCount = triggerEvent.clickCount;
                if (triggerEvent is IMouseEventInternal internalEvent)
                    ((IMouseEventInternal)e).triggeredByOS = internalEvent.triggeredByOS;
            }
            return e;
        }

        protected static T GetPooled(IPointerEvent pointerEvent)
        {
            T e = GetPooled();
            e.elementTarget = (pointerEvent as EventBase)?.elementTarget;
            e.imguiEvent = (pointerEvent as EventBase)?.imguiEvent;
            e.modifiers = pointerEvent.modifiers;
            e.mousePosition = pointerEvent.position;
            e.localMousePosition = pointerEvent.position;
            e.mouseDelta = pointerEvent.deltaPosition;
            e.button = pointerEvent.button != -1 ? pointerEvent.button : 0;
            e.pressedButtons = pointerEvent.pressedButtons;
            e.clickCount = pointerEvent.clickCount;
            if (pointerEvent is IPointerEventInternal internalEvent)
            {
                ((IMouseEventInternal)e).triggeredByOS = internalEvent.triggeredByOS;
                ((IMouseEventInternal)e).sourcePointerEvent = pointerEvent;
            }
            return e;
        }

        internal static T UaFromPointer(IPointerEvent pointerEvent) => GetPooled(pointerEvent);

        protected MouseEventBase() { LocalInit(); }
    }

    public partial class MouseDownEvent : MouseEventBase<MouseDownEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
        public MouseDownEvent() { LocalInit(); }
    }

    public partial class MouseUpEvent : MouseEventBase<MouseUpEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
        public MouseUpEvent() { LocalInit(); }
    }

    public partial class MouseMoveEvent : MouseEventBase<MouseMoveEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;
        public MouseMoveEvent() { LocalInit(); }
    }

    public partial class MouseEnterEvent : MouseEventBase<MouseEnterEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.TricklesDown;
        public MouseEnterEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public partial class MouseLeaveEvent : MouseEventBase<MouseLeaveEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.TricklesDown;
        public MouseLeaveEvent() { LocalInit(); }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public partial class MouseOverEvent : MouseEventBase<MouseOverEvent>
    {
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public partial class MouseOutEvent : MouseEventBase<MouseOutEvent>
    {
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToAssignedTarget(this, panel);
    }

    public partial class WheelEvent : MouseEventBase<WheelEvent>
    {
        public const float scrollDeltaPerTick = 3f;
        public Vector3 delta { get; private set; }

        public new static WheelEvent GetPooled(Event systemEvent)
        {
            var e = MouseEventBase<WheelEvent>.GetPooled(systemEvent);
            if (systemEvent != null) e.delta = systemEvent.delta;
            return e;
        }

        internal static WheelEvent GetPooled(Vector3 delta, Vector3 mousePosition, EventModifiers modifiers = EventModifiers.None)
        {
            var e = GetPooled();
            e.delta = delta;
            e.mousePosition = mousePosition;
            e.modifiers = modifiers;
            return e;
        }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            delta = Vector3.zero;
        }
        public WheelEvent() { LocalInit(); }

        internal override void Dispatch(BaseVisualElementPanel panel) =>
            Unbound.UIElements.Dispatching.DispatchToElementUnderPointerOrPanelRoot(this, panel, PointerId.mousePointerId, mousePosition);
    }

    // ---- keyboard ----

    public partial interface IKeyboardEvent
    {
        EventModifiers modifiers { get; }
        char character { get; }
        KeyCode keyCode { get; }
        bool shiftKey { get; }
        bool ctrlKey { get; }
        bool commandKey { get; }
        bool altKey { get; }
        bool actionKey { get; }
    }

    public abstract partial class KeyboardEventBase<T> : EventBase<T>, IKeyboardEvent where T : KeyboardEventBase<T>, new()
    {
        public EventModifiers modifiers { get; protected set; }
        public char character { get; protected set; }
        public KeyCode keyCode { get; protected set; }
        public bool shiftKey => UaModifiers.Shift(modifiers);
        public bool ctrlKey => UaModifiers.Ctrl(modifiers);
        public bool commandKey => UaModifiers.Command(modifiers);
        public bool altKey => UaModifiers.Alt(modifiers);
        public bool actionKey => UaModifiers.Action(modifiers);

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            modifiers = EventModifiers.None;
            character = '\0';
            keyCode = KeyCode.None;
        }

        public static T GetPooled(char c, KeyCode keyCode, EventModifiers modifiers)
        {
            T e = GetPooled();
            e.modifiers = modifiers;
            e.character = c;
            e.keyCode = keyCode;
            return e;
        }

        public static T GetPooled(Event systemEvent)
        {
            T e = GetPooled();
            e.imguiEvent = systemEvent;
            if (systemEvent != null)
            {
                e.modifiers = systemEvent.modifiers;
                e.character = systemEvent.character;
                e.keyCode = systemEvent.keyCode;
            }
            return e;
        }

        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToFocusedElementOrPanelRoot(this, panel);

        protected KeyboardEventBase() { LocalInit(); }
    }

    public partial class KeyDownEvent : KeyboardEventBase<KeyDownEvent>
    {
        internal void GetEquivalentImguiEvent(Event outImguiEvent)
        {
            if (imguiEvent != null)
            {
                outImguiEvent.CopyFrom(imguiEvent);
                return;
            }
            outImguiEvent.type = EventType.KeyDown;
            outImguiEvent.modifiers = modifiers;
            outImguiEvent.character = character;
            outImguiEvent.keyCode = keyCode;
        }

        protected internal override void PostDispatch(IPanel panel)
        {
            base.PostDispatch(panel);
            if (panel.contextType == ContextType.Editor && (imguiEvent == null || imguiEvent.type != EventType.Used))
                SendEquivalentNavigationEventIfAny(panel);
        }

        void SendEquivalentNavigationEventIfAny(IPanel panel)
        {
            if (character == '\n' || character == '\u0003' || character == ' ')
            {
                using (var submit = NavigationSubmitEvent.UaGetPooled(NavigationDeviceType.Keyboard, modifiers))
                {
                    submit.elementTarget = elementTarget;
                    panel.visualTree.SendEvent(submit);
                }
                return;
            }
            if (keyCode == KeyCode.Escape)
            {
                using (var cancel = NavigationCancelEvent.UaGetPooled(NavigationDeviceType.Keyboard, modifiers))
                {
                    cancel.elementTarget = elementTarget;
                    panel.visualTree.SendEvent(cancel);
                }
                return;
            }
            if (UaShouldSendNavigationMoveEvent())
            {
                using (var move = NavigationMoveEvent.GetPooled(shiftKey ? NavigationMoveEvent.Direction.Previous : NavigationMoveEvent.Direction.Next, NavigationDeviceType.Keyboard, modifiers))
                {
                    move.elementTarget = elementTarget;
                    panel.visualTree.SendEvent(move);
                }
                return;
            }
            if (keyCode == KeyCode.RightArrow || keyCode == KeyCode.LeftArrow || keyCode == KeyCode.UpArrow || keyCode == KeyCode.DownArrow)
            {
                var v = keyCode == KeyCode.RightArrow ? Vector2.right : keyCode == KeyCode.LeftArrow ? Vector2.left : keyCode == KeyCode.UpArrow ? Vector2.up : Vector2.down;
                using (var move = NavigationMoveEvent.GetPooled(v, NavigationDeviceType.Keyboard, modifiers))
                {
                    move.elementTarget = elementTarget;
                    panel.visualTree.SendEvent(move);
                }
            }
        }

        internal bool UaShouldSendNavigationMoveEvent() =>
            keyCode == KeyCode.Tab && !ctrlKey && !altKey && !commandKey && !(modifiers.HasFlag(EventModifiers.FunctionKey) && false);
    }

    public partial class KeyUpEvent : KeyboardEventBase<KeyUpEvent>
    {
    }

    // ---- focus ----

    public partial interface IFocusEvent
    {
        Focusable relatedTarget { get; }
        FocusChangeDirection direction { get; }
    }

    public abstract partial class FocusEventBase<T> : EventBase<T>, IFocusEvent where T : FocusEventBase<T>, new()
    {
        public Focusable relatedTarget { get; private set; }
        public FocusChangeDirection direction { get; private set; }
        protected FocusController focusController { get; private set; }
        internal bool IsFocusDelegated { get; private set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.TricklesDown;
            relatedTarget = null;
            direction = FocusChangeDirection.unspecified;
            focusController = null;
        }

        public static T GetPooled(IEventHandler target, Focusable relatedTarget, FocusChangeDirection direction, FocusController focusController, bool bIsFocusDelegated = false)
        {
            T e = GetPooled();
            e.elementTarget = (VisualElement)target;
            e.relatedTarget = relatedTarget;
            e.direction = direction;
            e.focusController = focusController;
            e.IsFocusDelegated = bIsFocusDelegated;
            return e;
        }

        internal FocusController UaFocusController => focusController;

        protected FocusEventBase() { LocalInit(); }
    }

    public partial class FocusEvent : FocusEventBase<FocusEvent> { }
    public partial class BlurEvent : FocusEventBase<BlurEvent> { }

    public partial class FocusInEvent : FocusEventBase<FocusInEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;
        public FocusInEvent() { LocalInit(); }

        protected internal override void PostDispatch(IPanel panel)
        {
            UaFocusController.ProcessPendingFocusChange(elementTarget);
            base.PostDispatch(panel);
        }
    }

    public partial class FocusOutEvent : FocusEventBase<FocusOutEvent>
    {
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;
        public FocusOutEvent() { LocalInit(); }

        protected internal override void PostDispatch(IPanel panel)
        {
            if (relatedTarget == null) UaFocusController.ProcessPendingFocusChange(null);
            base.PostDispatch(panel);
        }
    }

    // ---- values ----

    public partial interface IChangeEvent { }

    public partial class ChangeEvent<T> : EventBase<ChangeEvent<T>>, IChangeEvent
    {
        public T previousValue { get; protected set; }
        public T newValue { get; protected set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            previousValue = default;
            newValue = default;
        }

        public static ChangeEvent<T> GetPooled(T previousValue, T newValue)
        {
            var e = GetPooled();
            e.previousValue = previousValue;
            e.newValue = newValue;
            return e;
        }

        public ChangeEvent() { LocalInit(); }
    }

    public partial class InputEvent : EventBase<InputEvent>
    {
        public string previousData { get; protected set; }
        public string newData { get; protected set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            previousData = null;
            newData = null;
        }

        public static InputEvent GetPooled(string previousData, string newData)
        {
            var e = GetPooled();
            e.previousData = previousData;
            e.newData = newData;
            return e;
        }

        public InputEvent() { LocalInit(); }
    }

    // ---- navigation ----

    public partial interface INavigationEvent
    {
        EventModifiers modifiers { get; }
        internal NavigationDeviceType deviceType { get; }
        bool shiftKey { get; }
        bool ctrlKey { get; }
        bool commandKey { get; }
        bool altKey { get; }
        bool actionKey { get; }
    }

    public abstract partial class NavigationEventBase<T> : EventBase<T>, INavigationEvent where T : NavigationEventBase<T>, new()
    {
        public EventModifiers modifiers { get; protected set; }
        internal NavigationDeviceType deviceType { get; private set; }
        NavigationDeviceType INavigationEvent.deviceType => deviceType;
        public bool shiftKey => UaModifiers.Shift(modifiers);
        public bool ctrlKey => UaModifiers.Ctrl(modifiers);
        public bool commandKey => UaModifiers.Command(modifiers);
        public bool altKey => UaModifiers.Alt(modifiers);
        public bool actionKey => UaModifiers.Action(modifiers);

        protected NavigationEventBase() { LocalInit(); }
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            modifiers = EventModifiers.None;
            deviceType = NavigationDeviceType.Unknown;
        }

        public static T GetPooled(EventModifiers modifiers = EventModifiers.None) => UaGetPooled(NavigationDeviceType.Unknown, modifiers);

        internal static T UaGetPooled(NavigationDeviceType deviceType, EventModifiers modifiers = EventModifiers.None)
        {
            T e = GetPooled();
            e.deviceType = deviceType;
            e.modifiers = modifiers;
            return e;
        }

        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToFocusedElementOrPanelRoot(this, panel);
    }

    public partial class NavigationMoveEvent : NavigationEventBase<NavigationMoveEvent>
    {
        public enum Direction
        {
            None = 0,
            Left = 1,
            Up = 2,
            Right = 3,
            Down = 4,
            Next = 5,
            Previous = 6,
        }

        public Direction direction { get; private set; }
        public Vector2 move { get; private set; }

        internal static Direction DetermineMoveDirection(float x, float y, float deadZone = 0.6f)
        {
            if (new Vector2(x, y).sqrMagnitude < deadZone * deadZone) return Direction.None;
            if (Mathf.Abs(x) > Mathf.Abs(y)) return x > 0 ? Direction.Right : Direction.Left;
            return y > 0 ? Direction.Up : Direction.Down;
        }

        public static NavigationMoveEvent GetPooled(Vector2 moveVector, EventModifiers modifiers = EventModifiers.None) =>
            GetPooled(moveVector, NavigationDeviceType.Unknown, modifiers);

        internal static NavigationMoveEvent GetPooled(Vector2 moveVector, NavigationDeviceType deviceType, EventModifiers modifiers = EventModifiers.None)
        {
            var e = UaGetPooled(deviceType, modifiers);
            e.direction = DetermineMoveDirection(moveVector.x, moveVector.y);
            e.move = moveVector;
            return e;
        }

        public static NavigationMoveEvent GetPooled(Direction direction, EventModifiers modifiers = EventModifiers.None) =>
            GetPooled(direction, NavigationDeviceType.Unknown, modifiers);

        internal static NavigationMoveEvent GetPooled(Direction direction, NavigationDeviceType deviceType, EventModifiers modifiers = EventModifiers.None)
        {
            var e = UaGetPooled(deviceType, modifiers);
            e.direction = direction;
            e.move = direction switch
            {
                Direction.Left => Vector2.left,
                Direction.Up => Vector2.up,
                Direction.Right => Vector2.right,
                Direction.Down => Vector2.down,
                _ => Vector2.zero,
            };
            return e;
        }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            direction = Direction.None;
            move = Vector2.zero;
        }
        public NavigationMoveEvent() { LocalInit(); }

        protected internal override void PostDispatch(IPanel panel)
        {
            panel.focusController.SwitchFocusOnEvent(panel.focusController.GetLeafFocusedElement(), this);
            base.PostDispatch(panel);
        }
    }

    public partial class NavigationSubmitEvent : NavigationEventBase<NavigationSubmitEvent> { }
    public partial class NavigationCancelEvent : NavigationEventBase<NavigationCancelEvent> { }

    // ---- layout, panel, style ----

    public partial class GeometryChangedEvent : EventBase<GeometryChangedEvent>
    {
        public Rect oldRect { get; private set; }
        public Rect newRect { get; private set; }
        internal int layoutPass { get; set; }

        public static GeometryChangedEvent GetPooled(Rect oldRect, Rect newRect)
        {
            var e = GetPooled();
            e.oldRect = oldRect;
            e.newRect = newRect;
            return e;
        }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            oldRect = Rect.zero;
            newRect = Rect.zero;
            layoutPass = 0;
        }
        public GeometryChangedEvent() { LocalInit(); }
    }

    public partial interface IPanelChangedEvent { }

    public abstract partial class PanelChangedEventBase<T> : EventBase<T>, IPanelChangedEvent where T : PanelChangedEventBase<T>, new()
    {
        public IPanel originPanel { get; private set; }
        public IPanel destinationPanel { get; private set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            originPanel = null;
            destinationPanel = null;
        }

        public static T GetPooled(IPanel originPanel, IPanel destinationPanel)
        {
            T e = GetPooled();
            e.originPanel = originPanel;
            e.destinationPanel = destinationPanel;
            return e;
        }

        protected PanelChangedEventBase() { LocalInit(); }
    }

    public partial class AttachToPanelEvent : PanelChangedEventBase<AttachToPanelEvent> { }
    public partial class DetachFromPanelEvent : PanelChangedEventBase<DetachFromPanelEvent> { }

    public partial class CustomStyleResolvedEvent : EventBase<CustomStyleResolvedEvent>
    {
        public ICustomStyle customStyle => elementTarget?.customStyle;
    }

    public partial class TooltipEvent : EventBase<TooltipEvent>
    {
        public string tooltip { get; set; }
        public Rect rect { get; set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            rect = default;
            tooltip = string.Empty;
        }
        public TooltipEvent() { LocalInit(); }
    }

    // ---- capture ----

    public partial interface IPointerCaptureEvent { }
    public partial interface IMouseCaptureEvent { }

    public abstract partial class PointerCaptureEventBase<T> : EventBase<T>, IPointerCaptureEvent where T : PointerCaptureEventBase<T>, new()
    {
        public IEventHandler relatedTarget { get; private set; }
        public int pointerId { get; private set; }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            relatedTarget = null;
            pointerId = PointerId.invalidPointerId;
        }

        public static T GetPooled(IEventHandler target, IEventHandler relatedTarget, int pointerId)
        {
            T e = GetPooled();
            e.elementTarget = target as VisualElement;
            e.relatedTarget = relatedTarget;
            e.pointerId = pointerId;
            return e;
        }

        protected PointerCaptureEventBase() { LocalInit(); }
    }

    public partial class PointerCaptureEvent : PointerCaptureEventBase<PointerCaptureEvent> { }
    public partial class PointerCaptureOutEvent : PointerCaptureEventBase<PointerCaptureOutEvent> { }

    public abstract partial class MouseCaptureEventBase<T> : PointerCaptureEventBase<T>, IMouseCaptureEvent where T : MouseCaptureEventBase<T>, new()
    {
        public new IEventHandler relatedTarget => base.relatedTarget;

        public static T GetPooled(IEventHandler target, IEventHandler relatedTarget) =>
            PointerCaptureEventBase<T>.GetPooled(target, relatedTarget, PointerId.mousePointerId);

        protected MouseCaptureEventBase() { }
    }

    public partial class MouseCaptureEvent : MouseCaptureEventBase<MouseCaptureEvent> { }
    public partial class MouseCaptureOutEvent : MouseCaptureEventBase<MouseCaptureOutEvent> { }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Click events from pointer down/up pairs, as Unity's click detector sends them.</summary>
    internal sealed class ClickDetection
    {
        sealed class Status
        {
            public VisualElement Target;
            public Vector3 DownPosition;
            public long LastDownTime;
            public int ClickCount;
            public void Reset()
            {
                Target = null;
                ClickCount = 0;
                LastDownTime = 0;
                DownPosition = Vector3.zero;
            }
        }

        readonly Status[] m_Status = new Status[PointerId.maxPointers];
        static int s_DoubleClickTime = -1;

        internal ClickDetection()
        {
            for (int i = 0; i < m_Status.Length; i++) m_Status[i] = new Status();
            if (s_DoubleClickTime == -1) s_DoubleClickTime = 500;
        }

        void Start(EventBase evt, IPointerEvent pe)
        {
            var s = m_Status[pe.pointerId];
            var target = evt.elementTarget;
            if (target != s.Target) s.Reset();
            s.Target = target;
            if (evt.timestamp - s.LastDownTime > s_DoubleClickTime) s.ClickCount = 1;
            else s.ClickCount++;
            s.LastDownTime = evt.timestamp;
            s.DownPosition = pe.position;
        }

        void SendClick(EventBase evt, IPointerEvent pe)
        {
            var s = m_Status[pe.pointerId];
            var target = evt.elementTarget;
            if (target == null || !ContainsPointer(target, pe) || s.Target == null || s.ClickCount <= 0) return;
            var ancestor = s.Target.FindCommonAncestor(target);
            if (ancestor == null) return;
            using (var click = ClickEvent.GetPooled(pe, s.ClickCount))
            {
                click.elementTarget = ancestor;
                ancestor.SendEvent(click, DispatchMode.Immediate);
            }
        }

        static bool ContainsPointer(VisualElement element, IPointerEvent pe)
        {
            if (!element.worldBound.Contains(pe.position) || element.panel == null) return false;
            var top = element.elementPanel.GetTopElementUnderPointer(pe.pointerId);
            return element == top || element.Contains(top);
        }

        internal void ProcessEvent<TEvent>(PointerEventBase<TEvent> evt) where TEvent : PointerEventBase<TEvent>, new()
        {
            long type = evt.eventTypeId;
            if (type == EventBase<PointerDownEvent>.TypeId() && evt.button == 0)
            {
                Start(evt, evt);
            }
            else if (type == EventBase<PointerMoveEvent>.TypeId())
            {
                if (evt.button == 0 && (evt.pressedButtons & 1) == 1) Start(evt, evt);
                else if (evt.button == 0 && (evt.pressedButtons & 1) == 0) SendClick(evt, evt);
                else if (m_Status[evt.pointerId].Target != null) m_Status[evt.pointerId].LastDownTime = 0;
            }
            else if (type == EventBase<PointerCancelEvent>.TypeId())
            {
                m_Status[evt.pointerId].Reset();
            }
            else if (type == EventBase<PointerUpEvent>.TypeId() && evt.button == 0)
            {
                SendClick(evt, evt);
            }
        }

        internal void Cleanup(List<VisualElement> elements)
        {
            foreach (var s in m_Status)
                if (s.Target != null && elements.Contains(s.Target)) s.Reset();
        }
    }
}
