using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    public abstract partial class Manipulator : IManipulator
    {
        VisualElement m_Target;

        protected Manipulator() { }

        public VisualElement target
        {
            get => m_Target;
            set
            {
                if (m_Target != null) UnregisterCallbacksFromTarget();
                m_Target = value;
                if (m_Target != null) RegisterCallbacksOnTarget();
            }
        }

        protected abstract void RegisterCallbacksOnTarget();

        protected abstract void UnregisterCallbacksFromTarget();
    }

    public partial struct ManipulatorActivationFilter : IEquatable<ManipulatorActivationFilter>
    {
        public MouseButton button { get; set; }
        public EventModifiers modifiers { get; set; }
        public int clickCount { get; set; }

        public override bool Equals(object obj) => obj is ManipulatorActivationFilter other && Equals(other);

        public bool Equals(ManipulatorActivationFilter other) => button == other.button && modifiers == other.modifiers && clickCount == other.clickCount;

        public override int GetHashCode() => HashCode.Combine(button, modifiers, clickCount);

        public bool Matches(IMouseEvent e)
        {
            if (e == null) return false;
            bool clicks = clickCount == 0 || e.clickCount >= clickCount;
            return button == (MouseButton)e.button && MatchModifiers(e.altKey, e.ctrlKey, e.shiftKey, e.commandKey) && clicks;
        }

        public bool Matches(IPointerEvent e)
        {
            if (e == null) return false;
            bool clicks = clickCount == 0 || e.clickCount >= clickCount;
            return button == (MouseButton)e.button && MatchModifiers(e.altKey, e.ctrlKey, e.shiftKey, e.commandKey) && clicks;
        }

        /// <summary>Each modifier must be down exactly when the filter asks for it.</summary>
        bool MatchModifiers(bool alt, bool ctrl, bool shift, bool command)
        {
            var m = modifiers;
            return Same(m, EventModifiers.Alt, alt) && Same(m, EventModifiers.Control, ctrl) && Same(m, EventModifiers.Shift, shift) && Same(m, EventModifiers.Command, command);
        }

        static bool Same(EventModifiers modifiers, EventModifiers flag, bool down) => ((modifiers & flag) != 0) == down;

        public static bool operator ==(ManipulatorActivationFilter filter1, ManipulatorActivationFilter filter2) => filter1.Equals(filter2);

        public static bool operator !=(ManipulatorActivationFilter filter1, ManipulatorActivationFilter filter2) => !(filter1 == filter2);
    }

    public abstract partial class MouseManipulator : Manipulator
    {
        ManipulatorActivationFilter m_CurrentActivator;

        public List<ManipulatorActivationFilter> activators { get; private set; }

        protected MouseManipulator()
        {
            activators = new List<ManipulatorActivationFilter>();
        }

        protected bool CanStartManipulation(IMouseEvent e)
        {
            foreach (var activator in activators)
            {
                if (activator.Matches(e))
                {
                    m_CurrentActivator = activator;
                    return true;
                }
            }
            return false;
        }

        protected bool CanStopManipulation(IMouseEvent e) => e != null && e.button == (int)m_CurrentActivator.button;
    }

    public abstract partial class PointerManipulator : MouseManipulator
    {
        int m_CurrentPointerId;

        protected PointerManipulator() { }

        protected bool CanStartManipulation(IPointerEvent e)
        {
            foreach (var activator in activators)
            {
                if (activator.Matches(e))
                {
                    m_CurrentPointerId = e.pointerId;
                    return true;
                }
            }
            return false;
        }

        protected bool CanStopManipulation(IPointerEvent e) => e != null && e.pointerId == m_CurrentPointerId;
    }

    /// <summary>Clicks on an element: the pointer pressed and released over it (or, with a delay or an
    /// interval, repeated while it is held), as Unity's Clickable detects them.</summary>
    public partial class Clickable : PointerManipulator
    {
        readonly long m_Delay;
        readonly long m_Interval;
        int m_ActivePointerId = -1;
        bool m_AcceptClicksIfDisabled;
        IVisualElementScheduledItem m_Repeater;
        IVisualElementScheduledItem m_PendingActivePseudoStateReset;

        protected bool active { get; set; }

        public Vector2 lastMousePosition { get; private set; }

        internal bool acceptClicksIfDisabled
        {
            get => m_AcceptClicksIfDisabled;
            set
            {
                if (m_AcceptClicksIfDisabled == value) return;
                if (target != null) UnregisterCallbacksFromTarget();
                m_AcceptClicksIfDisabled = value;
                if (target != null) RegisterCallbacksOnTarget();
            }
        }

        UaInvokePolicy invokePolicy => acceptClicksIfDisabled ? UaInvokePolicy.IncludeDisabled : UaInvokePolicy.Default;

        public event Action<EventBase> clickedWithEventInfo;

        public event Action clicked;

        public Clickable(Action handler, long delay, long interval) : this(handler)
        {
            m_Delay = delay;
            m_Interval = interval;
            active = false;
        }

        public Clickable(Action<EventBase> handler)
        {
            clickedWithEventInfo = handler;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        public Clickable(Action handler)
        {
            clicked = handler;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            active = false;
        }

        void OnTimer(TimerState timerState)
        {
            if ((clicked == null && clickedWithEventInfo == null) || !IsRepeatable()) return;
            if (ContainsPointer(m_ActivePointerId) && (target.enabledInHierarchy || acceptClicksIfDisabled))
            {
                Invoke(null);
                target.pseudoStates |= PseudoStates.Active;
            }
            else
            {
                target.pseudoStates &= ~PseudoStates.Active;
            }
        }

        bool IsRepeatable() => m_Delay > 0 || m_Interval > 0;

        protected override void RegisterCallbacksOnTarget()
        {
            target.UaRegisterCallback<PointerDownEvent>(OnPointerDown, invokePolicy);
            target.UaRegisterCallback<PointerMoveEvent>(OnPointerMove, invokePolicy);
            target.UaRegisterCallback<PointerUpEvent>(OnPointerUp, UaInvokePolicy.IncludeDisabled);
            target.UaRegisterCallback<PointerCancelEvent>(OnPointerCancel, UaInvokePolicy.IncludeDisabled);
            target.UaRegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, UaInvokePolicy.IncludeDisabled);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            ResetActivePseudoState();
        }

        [Obsolete("OnMouseDown has been removed and replaced by its pointer-based equivalent. Please use OnPointerDown.", false)]
        protected void OnMouseDown(MouseDownEvent evt)
        {
            if (CanStartManipulation(evt)) ProcessDownEvent(evt, evt.localMousePosition, PointerId.mousePointerId);
        }

        [Obsolete("OnMouseMove has been removed and replaced by its pointer-based equivalent. Please use OnPointerMove.", false)]
        protected void OnMouseMove(MouseMoveEvent evt)
        {
            if (active) ProcessMoveEvent(evt, evt.localMousePosition);
        }

        [Obsolete("OnMouseUp has been removed and replaced by its pointer-based equivalent. Please use OnPointerUp.", false)]
        protected void OnMouseUp(MouseUpEvent evt)
        {
            if (active && CanStopManipulation(evt)) ProcessUpEvent(evt, evt.localMousePosition, PointerId.mousePointerId);
        }

        protected void OnPointerDown(PointerDownEvent evt)
        {
            if (CanStartManipulation(evt)) ProcessDownEvent(evt, evt.localPosition, evt.pointerId);
        }

        protected void OnPointerMove(PointerMoveEvent evt)
        {
            if (active) ProcessMoveEvent(evt, evt.localPosition);
        }

        protected void OnPointerUp(PointerUpEvent evt)
        {
            if (active && CanStopManipulation(evt)) ProcessUpEvent(evt, evt.localPosition, evt.pointerId);
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (active && CanStopManipulation(evt)) ProcessCancelEvent(evt, evt.pointerId);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (active) ProcessCancelEvent(evt, evt.pointerId);
        }

        bool ContainsPointer(int pointerId)
        {
            var under = target.elementPanel?.GetTopElementUnderPointer(pointerId);
            return target == under || target.Contains(under);
        }

        protected void Invoke(EventBase evt)
        {
            clicked?.Invoke();
            clickedWithEventInfo?.Invoke(evt);
        }

        internal void SimulateSingleClick(EventBase evt, int delayMs = 100)
        {
            target.pseudoStates |= PseudoStates.Active;
            m_PendingActivePseudoStateReset = target.schedule.Execute(ResetActivePseudoState);
            m_PendingActivePseudoStateReset.ExecuteLater(delayMs);
            Invoke(evt);
        }

        void ResetActivePseudoState()
        {
            if (m_PendingActivePseudoStateReset == null) return;
            target.pseudoStates &= ~PseudoStates.Active;
            m_PendingActivePseudoStateReset = null;
        }

        protected virtual void ProcessDownEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            active = true;
            m_ActivePointerId = pointerId;
            target.CapturePointer(pointerId);
            if (!(evt is IPointerEvent)) target.panel.ProcessPointerCapture(pointerId);
            lastMousePosition = localPosition;
            if (IsRepeatable())
            {
                if (ContainsPointer(pointerId) && (target.enabledInHierarchy || acceptClicksIfDisabled)) Invoke(evt);
                if (m_Repeater == null) m_Repeater = target.schedule.Execute(OnTimer).Every(m_Interval).StartingIn(m_Delay);
                else m_Repeater.ExecuteLater(m_Delay);
            }
            target.pseudoStates |= PseudoStates.Active;
            evt.StopImmediatePropagation();
        }

        protected virtual void ProcessMoveEvent(EventBase evt, Vector2 localPosition)
        {
            lastMousePosition = localPosition;
            if (ContainsPointer(m_ActivePointerId)) target.pseudoStates |= PseudoStates.Active;
            else target.pseudoStates &= ~PseudoStates.Active;
            evt.StopPropagation();
        }

        protected virtual void ProcessUpEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            active = false;
            m_ActivePointerId = -1;
            target.ReleasePointer(pointerId);
            if (!(evt is IPointerEvent)) target.panel.ProcessPointerCapture(pointerId);
            target.pseudoStates &= ~PseudoStates.Active;
            if (IsRepeatable()) m_Repeater?.Pause();
            else if (ContainsPointer(pointerId) && (target.enabledInHierarchy || acceptClicksIfDisabled)) Invoke(evt);
            evt.StopPropagation();
        }

        protected virtual void ProcessCancelEvent(EventBase evt, int pointerId)
        {
            active = false;
            m_ActivePointerId = -1;
            target.ReleasePointer(pointerId);
            if (!(evt is IPointerEvent)) target.panel.ProcessPointerCapture(pointerId);
            target.pseudoStates &= ~PseudoStates.Active;
            if (IsRepeatable()) m_Repeater?.Pause();
            evt.StopPropagation();
        }
    }
}
