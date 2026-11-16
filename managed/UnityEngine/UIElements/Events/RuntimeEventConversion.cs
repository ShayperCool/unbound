namespace UnityEngine.UIElements
{
    internal static partial class KeyboardEventExtensions
    {
        internal static bool ShouldSendNavigationMoveEventRuntime(this Event e) => e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab;
    }

    internal static partial class UIElementsRuntimeUtility
    {
        public static EventBase CreateEvent(Event systemEvent)
        {
            switch (systemEvent.rawType)
            {
                case EventType.MouseMove:
                case EventType.MouseDrag:
                case EventType.TouchMove:
                    return PointerMoveEvent.GetPooled(systemEvent);
                case EventType.MouseDown:
                case EventType.TouchDown:
                    return PointerDeviceState.HasAdditionalPressedButtons(PointerId.mousePointerId, systemEvent.button)
                        ? (EventBase)PointerMoveEvent.GetPooled(systemEvent) : PointerDownEvent.GetPooled(systemEvent);
                case EventType.MouseUp:
                case EventType.TouchUp:
                    return PointerDeviceState.HasAdditionalPressedButtons(PointerId.mousePointerId, systemEvent.button)
                        ? (EventBase)PointerMoveEvent.GetPooled(systemEvent) : PointerUpEvent.GetPooled(systemEvent);
                case EventType.ContextClick: return ContextClickEvent.GetPooled(systemEvent);
                case EventType.MouseEnterWindow: return MouseEnterWindowEvent.GetPooled(systemEvent);
                case EventType.MouseLeaveWindow: return MouseLeaveWindowEvent.GetPooled(systemEvent);
                case EventType.ScrollWheel: return WheelEvent.GetPooled(systemEvent);
                case EventType.KeyDown: return KeyDownEvent.GetPooled(systemEvent);
                case EventType.KeyUp: return KeyUpEvent.GetPooled(systemEvent);
                case EventType.DragUpdated: return DragUpdatedEvent.GetPooled(systemEvent);
                case EventType.DragPerform: return DragPerformEvent.GetPooled(systemEvent);
                case EventType.DragExited: return DragExitedEvent.GetPooled(systemEvent);
                case EventType.ValidateCommand: return ValidateCommandEvent.GetPooled(systemEvent);
                case EventType.ExecuteCommand: return ExecuteCommandEvent.GetPooled(systemEvent);
                default: return IMGUIEvent.GetPooled(systemEvent);
            }
        }
    }

    [EventCategory(EventCategory.IMGUI)]
    public partial class IMGUIEvent : EventBase<IMGUIEvent>
    {
        public static IMGUIEvent GetPooled(Event systemEvent)
        {
            var e = GetPooled();
            e.imguiEvent = systemEvent;
            return e;
        }
        protected override void Init() { base.Init(); propagation = EventPropagation.BubblesOrTricklesDown; }
        public IMGUIEvent() { propagation = EventPropagation.BubblesOrTricklesDown; }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToPanelRoot(this, panel);
    }

    public partial class ContextClickEvent : MouseEventBase<ContextClickEvent> { }

    [EventCategory(EventCategory.EnterLeaveWindow)]
    public partial class MouseEnterWindowEvent : MouseEventBase<MouseEnterWindowEvent>
    {
        protected override void Init() { base.Init(); propagation = EventPropagation.Bubbles; }
        public MouseEnterWindowEvent() { propagation = EventPropagation.Bubbles; }
        protected internal override void PostDispatch(IPanel panel)
        {
            UaCommitElementUnderPointers(panel);
            base.PostDispatch(panel);
        }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToElementUnderPointerOrPanelRoot(this, panel, PointerId.mousePointerId, mousePosition);
    }

    [EventCategory(EventCategory.EnterLeaveWindow)]
    public partial class MouseLeaveWindowEvent : MouseEventBase<MouseLeaveWindowEvent>
    {
        protected override void Init() { base.Init(); propagation = EventPropagation.Bubbles; }
        public MouseLeaveWindowEvent() { propagation = EventPropagation.Bubbles; }
        public new static MouseLeaveWindowEvent GetPooled(Event systemEvent)
        {
            if (systemEvent != null) PointerDeviceState.ReleaseAllButtons(PointerId.mousePointerId);
            return MouseEventBase<MouseLeaveWindowEvent>.GetPooled(systemEvent);
        }
        protected internal override void PostDispatch(IPanel panel)
        {
            if (pressedButtons == 0) (panel as BaseVisualElementPanel)?.ClearCachedElementUnderPointer(PointerId.mousePointerId, this);
            UaCommitElementUnderPointers(panel);
            base.PostDispatch(panel);
        }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToCachedElementUnderPointerOrPanelRoot(this, panel, PointerId.mousePointerId, mousePosition);
    }

    public partial interface ICommandEvent { string commandName { get; } }

    [EventCategory(EventCategory.Command)]
    public abstract partial class CommandEventBase<T> : EventBase<T>, ICommandEvent where T : CommandEventBase<T>, new()
    {
        string m_CommandName;
        public string commandName { get => m_CommandName ?? imguiEvent?.commandName; protected set => m_CommandName = value; }
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() { propagation = EventPropagation.BubblesOrTricklesDown; m_CommandName = null; }
        protected CommandEventBase() { LocalInit(); }
        public static T GetPooled(Event systemEvent) { var e = GetPooled(); e.imguiEvent = systemEvent; return e; }
        public static T GetPooled(string commandName) { var e = GetPooled(); e.commandName = commandName; return e; }
        internal override void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DispatchToFocusedElementOrPanelRoot(this, panel);
    }

    public partial class ValidateCommandEvent : CommandEventBase<ValidateCommandEvent> { }
    public partial class ExecuteCommandEvent : CommandEventBase<ExecuteCommandEvent> { }

    /// <summary>The input method's composition changed; sent to the focused element.</summary>
    [EventCategory(EventCategory.Keyboard)]
    internal partial class IMEEvent : EventBase<IMEEvent>
    {
        public string compositionString { get; protected set; }

        public static IMEEvent GetPooled(string compositionString)
        {
            var e = GetPooled();
            e.compositionString = compositionString;
            return e;
        }

        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
            compositionString = null;
        }
        public IMEEvent() { LocalInit(); }
    }
    public partial interface IDragAndDropEvent { }

    [EventCategory(EventCategory.DragAndDrop)]
    public abstract partial class DragAndDropEventBase<T> : MouseEventBase<T>, IDragAndDropEvent where T : DragAndDropEventBase<T>, new()
    {
        private protected static EventPropagation UaBoundaryPropagation => Unbound.Runtime.Compat.DragBoundaryEventsTrickle
            ? EventPropagation.TricklesDown | EventPropagation.SkipDisabledElements : EventPropagation.SkipDisabledElements;
        protected override void Init() { base.Init(); LocalInit(); }
        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown | EventPropagation.SkipDisabledElements;
        protected DragAndDropEventBase() { LocalInit(); }
    }

    public partial class DragEnterEvent : DragAndDropEventBase<DragEnterEvent>
    {
        protected override void Init() { base.Init(); propagation = UaBoundaryPropagation; }
        public DragEnterEvent() { propagation = UaBoundaryPropagation; }
    }
    public partial class DragLeaveEvent : DragAndDropEventBase<DragLeaveEvent>
    {
        protected override void Init() { base.Init(); propagation = UaBoundaryPropagation; }
        public DragLeaveEvent() { propagation = UaBoundaryPropagation; }
    }

    public partial class DragPerformEvent : DragAndDropEventBase<DragPerformEvent> { }
    public partial class DragUpdatedEvent : DragAndDropEventBase<DragUpdatedEvent>
    {
        public new static DragUpdatedEvent GetPooled(Event systemEvent)
        {
            if (systemEvent != null) PointerDeviceState.PressButton(PointerId.mousePointerId, systemEvent.button);
            var e = MouseEventBase<DragUpdatedEvent>.GetPooled(systemEvent);
            e.button = 0;
            return e;
        }
        internal static DragUpdatedEvent GetPooled(PointerMoveEvent pointerEvent) => MouseEventBase<DragUpdatedEvent>.GetPooled(pointerEvent);
        protected internal override void PostDispatch(IPanel panel)
        {
            UaCommitElementUnderPointers(panel);
            base.PostDispatch(panel);
        }
    }
    public partial class DragExitedEvent : DragAndDropEventBase<DragExitedEvent>
    {
        protected override void Init() => base.Init();
        public new static DragExitedEvent GetPooled(Event systemEvent)
        {
            if (systemEvent != null) PointerDeviceState.ReleaseButton(PointerId.mousePointerId, systemEvent.button);
            return MouseEventBase<DragExitedEvent>.GetPooled(systemEvent);
        }
        protected internal override void PostDispatch(IPanel panel)
        {
            UaCommitElementUnderPointers(panel);
            base.PostDispatch(panel);
        }
    }
}
