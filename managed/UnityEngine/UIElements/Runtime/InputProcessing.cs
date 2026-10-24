using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputForUI;
using Unity.IntegerTime;
using UnityEngine.UIElements;
using UIEventModifiers = UnityEngine.EventModifiers;
using UIPointerType = UnityEngine.UIElements.PointerType;

namespace UnityEngine.UIElements
{
    public abstract partial class PointerEventBase<T>
    {
        /// <summary>A pointer event from an InputForUI pointer event at a panel position (Unity's runtime input path).</summary>
        internal static T GetPooled(PointerEvent pointerEvent, Vector2 position, Vector2 deltaPosition, int pointerId, float deltaTime)
        {
            T e = GetPooled();
            e.position = position;
            e.localPosition = position;
            e.deltaPosition = deltaPosition;
            e.pointerId = pointerId;
            e.deltaTime = deltaTime;
            ((IPointerEventInternal)e).displayIndex = pointerEvent.displayIndex;
            e.isPrimary = pointerEvent.isPrimaryPointer;
            e.button = -1;
            switch (pointerEvent.eventSource)
            {
                case EventSource.Mouse:
                    e.pointerType = UIPointerType.mouse;
                    if (pointerEvent.button == PointerEvent.Button.Primary) e.button = 0;
                    else if (pointerEvent.button == PointerEvent.Button.PenEraserInTouch) e.button = 1;
                    else if (pointerEvent.button == PointerEvent.Button.PenBarrelButton) e.button = 2;
                    break;
                case EventSource.Touch:
                    e.pointerType = UIPointerType.touch;
                    if (pointerEvent.button == PointerEvent.Button.Primary) e.button = 0;
                    break;
                case EventSource.Pen:
                    e.pointerType = UIPointerType.pen;
                    if (pointerEvent.button == PointerEvent.Button.Primary) e.button = 0;
                    else if (pointerEvent.button == PointerEvent.Button.PenBarrelButton) e.button = 1;
                    else if (pointerEvent.button == PointerEvent.Button.PenEraserInTouch) e.button = 5;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(pointerEvent), "Unsupported EventSource for pointer event");
            }
            if (pointerEvent.type == PointerEvent.Type.ButtonPressed) PointerDeviceState.PressButton(e.pointerId, e.button);
            else if (pointerEvent.type == PointerEvent.Type.ButtonReleased) PointerDeviceState.ReleaseButton(e.pointerId, e.button);
            e.pressedButtons = PointerDeviceState.GetPressedButtons(e.pointerId);
            if (pointerEvent.eventSource == EventSource.Pen)
            {
                var status = PenStatus.None;
                if ((e.pressedButtons & 1) != 0) status |= PenStatus.Contact;
                if ((e.pressedButtons & 2) != 0) status |= PenStatus.Barrel;
                if ((e.pressedButtons & 0x20) != 0) status |= PenStatus.Eraser;
                if (pointerEvent.isInverted) status |= PenStatus.Inverted;
                e.penStatus = status;
            }
            e.clickCount = pointerEvent.clickCount;
            e.pressure = pointerEvent.pressure;
            e.altitudeAngle = pointerEvent.altitude;
            e.azimuthAngle = pointerEvent.azimuth;
            e.twist = pointerEvent.twist;
            e.tilt = pointerEvent.tilt;
            var modifiers = UIEventModifiers.None;
            if (pointerEvent.eventModifiers.isShiftPressed) modifiers |= UIEventModifiers.Shift;
            if (pointerEvent.eventModifiers.isCtrlPressed) modifiers |= UIEventModifiers.Control;
            if (pointerEvent.eventModifiers.isAltPressed) modifiers |= UIEventModifiers.Alt;
            if (pointerEvent.eventModifiers.isMetaPressed) modifiers |= UIEventModifiers.Command;
            e.modifiers = modifiers;
            ((IPointerEventInternal)e).triggeredByOS = true;
            return e;
        }
    }
}

namespace Unbound.UIElements
{
    /// <summary>Feeds player panels from InputForUI (which reads the legacy Input Manager or the Input
    /// System): the default event system's processor queues the events of a frame and sends them as
    /// UI Toolkit events in order, as Unity's DefaultEventSystem.InputForUIProcessor does.</summary>
    internal static partial class UaInput
    {
        sealed class Processor
        {
            readonly DefaultEventSystem m_System;
            readonly Queue<UnityEngine.InputForUI.Event> m_Events = new Queue<UnityEngine.InputForUI.Event>();
            DiscreteTime m_LastPointerTimestamp = DiscreteTime.Zero;
            DiscreteTime m_NextPointerTimestamp = DiscreteTime.Zero;
            internal readonly EventConsumer consumer;

            internal Processor(DefaultEventSystem system)
            {
                m_System = system;
                consumer = OnEvent;
            }

            bool OnEvent(in UnityEngine.InputForUI.Event ev)
            {
                m_Events.Enqueue(ev);
                return true;
            }

            internal void Reset()
            {
                m_LastPointerTimestamp = DiscreteTime.Zero;
                m_NextPointerTimestamp = DiscreteTime.Zero;
                m_Events.Clear();
            }

            internal void Process()
            {
                if (m_Events.Count == 0) return;
                System.IDisposable sequence = null;
                while (m_Events.Count > 0)
                {
                    var ev = m_Events.Dequeue();
                    switch (ev.type)
                    {
                        case UnityEngine.InputForUI.Event.Type.PointerEvent:
                            ProcessPointerEvent(ev.asPointerEvent);
                            break;
                        case UnityEngine.InputForUI.Event.Type.KeyEvent:
                            sequence ??= m_System.UaFocusSequence();
                            ProcessKeyEvent(ev.asKeyEvent);
                            break;
                        case UnityEngine.InputForUI.Event.Type.TextInputEvent:
                            sequence ??= m_System.UaFocusSequence();
                            var text = ev.asTextInputEvent;
                            m_System.SendFocusBasedEvent(t => KeyDownEvent.GetPooled(t.character, KeyCode.None, t.modifiers), (modifiers: Modifiers(text.eventModifiers), character: text.character));
                            break;
                        case UnityEngine.InputForUI.Event.Type.NavigationEvent:
                            sequence ??= m_System.UaFocusSequence();
                            ProcessNavigationEvent(ev.asNavigationEvent);
                            break;
                        case UnityEngine.InputForUI.Event.Type.IMECompositionEvent:
                        case UnityEngine.InputForUI.Event.Type.CommandEvent:
                            sequence ??= m_System.UaFocusSequence();
                            break;
                    }
                }
                sequence?.Dispose();
                m_LastPointerTimestamp = m_NextPointerTimestamp;
            }

            static UIEventModifiers Modifiers(UnityEngine.InputForUI.EventModifiers m)
            {
                var result = UIEventModifiers.None;
                if (m.isShiftPressed) result |= UIEventModifiers.Shift;
                if (m.isCtrlPressed) result |= UIEventModifiers.Control;
                if (m.isAltPressed) result |= UIEventModifiers.Alt;
                if (m.isMetaPressed) result |= UIEventModifiers.Command;
                if (m.isCapsLockEnabled) result |= UIEventModifiers.CapsLock;
                if (m.isNumericPressed) result |= UIEventModifiers.Numeric;
                if (m.isFunctionKeyPressed) result |= UIEventModifiers.FunctionKey;
                return result;
            }

            void ProcessPointerEvent(PointerEvent pointerEvent)
            {
                var position = pointerEvent.position;
                int display = pointerEvent.displayIndex;
                var delta = pointerEvent.deltaPosition;
                int pointerBase = pointerEvent.eventSource == EventSource.Touch ? PointerId.touchPointerIdBase
                    : pointerEvent.eventSource == EventSource.Pen ? PointerId.penPointerIdBase : PointerId.mousePointerId;
                int pointerId = pointerBase + pointerEvent.pointerIndex;
                float deltaTime = m_LastPointerTimestamp != DiscreteTime.Zero ? (float)(pointerEvent.timestamp - m_LastPointerTimestamp) : 0f;
                m_NextPointerTimestamp = pointerEvent.timestamp;
                var arg = (pointerEvent, pointerId, deltaTime);
                switch (pointerEvent.type)
                {
                    case PointerEvent.Type.PointerMoved:
                        if (Mathf.Approximately(delta.x, 0f) && Mathf.Approximately(delta.y, 0f)) return;
                        m_System.SendPositionBasedEvent(position, delta, pointerId, display,
                            (p, d, t) => PointerMoveEvent.GetPooled(t.pointerEvent, p, d, t.pointerId, t.deltaTime), arg);
                        break;
                    case PointerEvent.Type.ButtonPressed:
                        m_System.SendPositionBasedEvent(position, delta, pointerId, display,
                            (p, d, t) => PointerDownEvent.GetPooled(t.pointerEvent, p, d, t.pointerId, t.deltaTime), arg);
                        break;
                    case PointerEvent.Type.ButtonReleased:
                        m_System.SendPositionBasedEvent(position, delta, pointerId, display,
                            (p, d, t) => PointerUpEvent.GetPooled(t.pointerEvent, p, d, t.pointerId, t.deltaTime), arg, deselectIfNoTarget: true);
                        break;
                    case PointerEvent.Type.TouchCanceled:
                        m_System.SendPositionBasedEvent(position, delta, pointerId, display,
                            (p, d, t) => PointerCancelEvent.GetPooled(t.pointerEvent, p, d, t.pointerId, t.deltaTime), arg);
                        break;
                    case PointerEvent.Type.Scroll:
                        m_System.SendPositionBasedEvent(pointerEvent.position, pointerEvent.deltaPosition, PointerId.mousePointerId, display,
                            (p, _, t) => WheelEvent.GetPooled(t.scroll, p, t.modifiers), (modifiers: Modifiers(pointerEvent.eventModifiers), scroll: (Vector3)pointerEvent.scroll));
                        break;
                }
            }

            void ProcessNavigationEvent(NavigationEvent navigationEvent)
            {
                var modifiers = Modifiers(navigationEvent.eventModifiers);
                var device = navigationEvent.eventSource == EventSource.Keyboard ? NavigationDeviceType.Keyboard
                    : navigationEvent.eventSource != EventSource.Unspecified ? NavigationDeviceType.NonKeyboard : NavigationDeviceType.Unknown;
                switch (navigationEvent.type)
                {
                    case NavigationEvent.Type.Move:
                    {
                        var move = Vector2.zero;
                        if (navigationEvent.direction == NavigationEvent.Direction.Left) move.x = -1f;
                        else if (navigationEvent.direction == NavigationEvent.Direction.Right) move.x = 1f;
                        else if (navigationEvent.direction == NavigationEvent.Direction.Up) move.y = 1f;
                        else if (navigationEvent.direction == NavigationEvent.Direction.Down) move.y = -1f;
                        if (move != Vector2.zero)
                        {
                            m_System.SendFocusBasedEvent(t => NavigationMoveEvent.GetPooled(t.move, t.device, t.modifiers), (move, device, modifiers));
                            return;
                        }
                        var direction = navigationEvent.direction == NavigationEvent.Direction.Previous ? NavigationMoveEvent.Direction.Previous : NavigationMoveEvent.Direction.Next;
                        m_System.SendFocusBasedEvent(t => NavigationMoveEvent.GetPooled(t.direction, t.device, t.modifiers), (direction, device, modifiers));
                        break;
                    }
                    case NavigationEvent.Type.Submit:
                        m_System.SendFocusBasedEvent(t => NavigationSubmitEvent.UaGetPooled(t.device, t.modifiers), (device, modifiers));
                        break;
                    case NavigationEvent.Type.Cancel:
                        m_System.SendFocusBasedEvent(t => NavigationCancelEvent.UaGetPooled(t.device, t.modifiers), (device, modifiers));
                        break;
                }
            }

            void ProcessKeyEvent(KeyEvent keyEvent)
            {
                var modifiers = Modifiers(keyEvent.eventModifiers);
                if (keyEvent.type == KeyEvent.Type.KeyPressed || keyEvent.type == KeyEvent.Type.KeyRepeated)
                    m_System.SendFocusBasedEvent(t => KeyDownEvent.GetPooled('\0', t.keyCode, t.modifiers), (modifiers, keyCode: keyEvent.keyCode));
                else if (keyEvent.type == KeyEvent.Type.KeyReleased)
                    m_System.SendFocusBasedEvent(t => KeyUpEvent.GetPooled('\0', t.keyCode, t.modifiers), (modifiers, keyCode: keyEvent.keyCode));
            }
        }

        static Processor s_Processor;
        static DefaultEventSystem s_ProcessorSystem;

        /// <summary>Subscribes the event system to InputForUI while player panels need input.</summary>
        internal static void SetInputReady(DefaultEventSystem system, bool ready)
        {
            if (ready)
            {
                if (s_Processor != null && s_ProcessorSystem == system) return;
                SetInputReady(system, false);
                s_Processor = new Processor(system);
                s_ProcessorSystem = system;
                UnityEngine.InputForUI.EventProvider.SetEnabled(true);
                UnityEngine.InputForUI.EventProvider.Subscribe(s_Processor.consumer, 0, null);
            }
            else if (s_Processor != null)
            {
                UnityEngine.InputForUI.EventProvider.Unsubscribe(s_Processor.consumer);
                UnityEngine.InputForUI.EventProvider.SetEnabled(false);
                s_Processor = null;
                s_ProcessorSystem = null;
            }
        }

        static partial void ProcessInputImpl(DefaultEventSystem system)
        {
            if (s_ProcessorSystem == system) s_Processor?.Process();
        }

        internal static void ResetProcessor() => s_Processor?.Reset();
    }
}
