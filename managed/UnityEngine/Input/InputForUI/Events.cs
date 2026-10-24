using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.IntegerTime;
using UnityEngine.Bindings;

namespace UnityEngine.InputForUI
{
    /// <summary>What every InputForUI event carries.</summary>
    internal partial interface IEventProperties
    {
        DiscreteTime timestamp { get; }
        EventSource eventSource { get; }
        uint playerId { get; }
        EventModifiers eventModifiers { get; }
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal enum EventSource
    {
        Unspecified,
        Keyboard,
        Gamepad,
        Mouse,
        Pen,
        Touch,
    }

    /// <summary>The modifier keys and toggles held when an event happened.</summary>
    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct EventModifiers
    {
        [Flags]
        public enum Modifiers : uint
        {
            LeftShift = 1u,
            RightShift = 2u,
            Shift = LeftShift | RightShift,
            LeftCtrl = 4u,
            RightCtrl = 8u,
            Ctrl = LeftCtrl | RightCtrl,
            LeftAlt = 0x10u,
            RightAlt = 0x20u,
            Alt = LeftAlt | RightAlt,
            LeftMeta = 0x40u,
            RightMeta = 0x80u,
            Meta = LeftMeta | RightMeta,
            CapsLock = 0x100u,
            Numlock = 0x200u,
            FunctionKey = 0x400u,
            Numeric = 0x800u,
        }

        uint _state;

        public bool isShiftPressed => IsPressed(Modifiers.Shift);
        public bool isCtrlPressed => IsPressed(Modifiers.Ctrl);
        public bool isAltPressed => IsPressed(Modifiers.Alt);
        public bool isMetaPressed => IsPressed(Modifiers.Meta);
        public bool isCapsLockEnabled => IsPressed(Modifiers.CapsLock);
        public bool isNumLockEnabled => IsPressed(Modifiers.Numlock);
        public bool isFunctionKeyPressed => IsPressed(Modifiers.FunctionKey);
        public bool isNumericPressed => IsPressed(Modifiers.Numeric);

        public bool IsPressed(Modifiers mod) => (_state & (uint)mod) != 0;

        public void SetPressed(Modifiers modifier, bool pressed)
        {
            if (pressed) _state |= (uint)modifier;
            else _state &= ~(uint)modifier;
        }

        public void Reset() => _state = 0u;

        static void Append(ref string str, string value) => str = string.IsNullOrEmpty(str) ? value : str + "," + value;

        public override string ToString()
        {
            string str = string.Empty;
            if (IsPressed(Modifiers.LeftShift)) Append(ref str, "LeftShift");
            if (IsPressed(Modifiers.RightShift)) Append(ref str, "RightShift");
            if (IsPressed(Modifiers.LeftCtrl)) Append(ref str, "LeftCtrl");
            if (IsPressed(Modifiers.RightCtrl)) Append(ref str, "RightCtrl");
            if (IsPressed(Modifiers.LeftAlt)) Append(ref str, "LeftAlt");
            if (IsPressed(Modifiers.RightAlt)) Append(ref str, "RightAlt");
            if (IsPressed(Modifiers.LeftMeta)) Append(ref str, "LeftMeta");
            if (IsPressed(Modifiers.RightMeta)) Append(ref str, "RightMeta");
            if (IsPressed(Modifiers.CapsLock)) Append(ref str, "CapsLock");
            if (IsPressed(Modifiers.Numlock)) Append(ref str, "Numlock");
            if (IsPressed(Modifiers.FunctionKey)) Append(ref str, "FunctionKey");
            if (IsPressed(Modifiers.Numeric)) Append(ref str, "Numeric");
            return str;
        }
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct CommandEvent : IEventProperties
    {
        public enum Type
        {
            Validate = 1,
            Execute = 2,
        }

        public enum Command
        {
            Invalid,
            Cut,
            Copy,
            Paste,
            SelectAll,
            DeselectAll,
            InvertSelection,
            Duplicate,
            Rename,
            Delete,
            SoftDelete,
            Find,
            SelectChildren,
            SelectPrefabRoot,
            UndoRedoPerformed,
            OnLostFocus,
            NewKeyboardFocus,
            ModifierKeysChanged,
            EyeDropperUpdate,
            EyeDropperClicked,
            EyeDropperCancelled,
            ColorPickerChanged,
            FrameSelected,
            FrameSelectedWithLock,
        }

        public Type type;
        public Command command;

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString() => $"{type} {command}";
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct IMECompositionEvent : IEventProperties
    {
        public string compositionString;

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString() => "IME '" + compositionString + "'";
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct KeyEvent : IEventProperties
    {
        public enum Type
        {
            KeyPressed = 1,
            KeyRepeated = 2,
            KeyReleased = 3,
            State = 4,
        }

        /// <summary>The pressed keys: one bit per <see cref="KeyCode"/> up to 319 (Menu).</summary>
        public partial struct ButtonsState
        {
            const uint kMaxIndex = 319u;
            const uint kSizeInBytes = 40u;

            unsafe fixed byte buttons[40];

            internal static bool ShouldBeProcessed(KeyCode keyCode) => (uint)keyCode <= kMaxIndex;

            unsafe bool GetUnchecked(uint index) => (buttons[index >> 3] & (byte)(1 << (int)(index & 7))) != 0;

            unsafe void SetUnchecked(uint index) => buttons[index >> 3] |= (byte)(1 << (int)(index & 7));

            unsafe void ClearUnchecked(uint index) => buttons[index >> 3] &= (byte)~(1 << (int)(index & 7));

            public bool IsPressed(KeyCode keyCode) => ShouldBeProcessed(keyCode) && GetUnchecked((uint)keyCode);

            public IEnumerable<KeyCode> GetAllPressed()
            {
                // A copy: an iterator cannot capture the struct it runs on.
                var copy = this;
                for (uint index = 0; index <= kMaxIndex; index++)
                    if (copy.GetUnchecked(index))
                        yield return (KeyCode)index;
            }

            public void SetPressed(KeyCode keyCode, bool pressed)
            {
                if (!ShouldBeProcessed(keyCode)) return;
                if (pressed) SetUnchecked((uint)keyCode);
                else ClearUnchecked((uint)keyCode);
            }

            public unsafe void Reset()
            {
                for (int i = 0; i < kSizeInBytes; i++) buttons[i] = 0;
            }

            public override string ToString() => string.Join(",", GetAllPressed());
        }

        public Type type;
        public KeyCode keyCode;
        public ButtonsState buttonsState;

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString()
        {
            switch (type)
            {
                case Type.KeyPressed:
                case Type.KeyRepeated:
                case Type.KeyReleased:
                    return $"{type} {keyCode}";
                case Type.State:
                    return $"{type} Pressed:{buttonsState}";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct NavigationEvent : IEventProperties
    {
        public enum Type
        {
            Move = 1,
            Submit = 2,
            Cancel = 3,
        }

        public enum Direction
        {
            None,
            Left,
            Up,
            Right,
            Down,
            Next,
            Previous,
        }

        public Type type;
        public Direction direction;
        public bool shouldBeUsed;

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString() =>
            $"Navigation {type}" + (type == Type.Move ? $" {direction}" : "") + (eventSource != EventSource.Keyboard ? $" {eventSource}" : "");

        internal static Direction DetermineMoveDirection(Vector2 vec, float deadZone = 0.6f)
        {
            if (vec.sqrMagnitude < deadZone * deadZone) return Direction.None;
            if (Mathf.Abs(vec.x) > Mathf.Abs(vec.y)) return vec.x > 0f ? Direction.Right : Direction.Left;
            return vec.y > 0f ? Direction.Up : Direction.Down;
        }
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct PointerEvent : IEventProperties
    {
        public enum Type
        {
            PointerMoved = 1,
            Scroll = 2,
            ButtonPressed = 3,
            ButtonReleased = 4,
            State = 5,
            TouchCanceled = 6,
        }

        [Flags]
        public enum Button : uint
        {
            None = 0u,
            Primary = 1u,
            FingerInTouch = Primary,
            PenTipInTouch = Primary,
            PenEraserInTouch = 2u,
            PenBarrelButton = 4u,
            MouseLeft = Primary,
            MouseRight = PenEraserInTouch,
            MouseMiddle = PenBarrelButton,
            MouseForward = 8u,
            MouseBack = 0x10u,
        }

        public partial struct ButtonsState
        {
            uint _state;

            public void Set(Button button, bool pressed)
            {
                if (pressed) _state |= (uint)button;
                else _state &= ~(uint)button;
            }

            public bool Get(Button button) => (_state & (uint)button) != 0;

            public void Reset() => _state = 0u;

            public override string ToString() => $"{_state:x2}";
        }

        public Type type;
        public int pointerIndex;
        public Vector2 position;
        public Vector2 deltaPosition;
        public Vector2 scroll;
        public int displayIndex;
        public Vector2 tilt;
        public float twist;
        public float pressure;
        public bool isInverted;
        public Button button;
        public ButtonsState buttonsState;
        public int clickCount;

        public bool isPrimaryPointer => pointerIndex == 0;
        public float azimuth => InputManagerProvider.TiltToAzimuth(tilt);
        public float altitude => InputManagerProvider.TiltToAltitude(tilt);
        public bool isPressed => buttonsState.Get(isInverted ? Button.PenEraserInTouch : Button.Primary);

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString()
        {
            string pen = eventSource == EventSource.Pen
                ? string.Format(" tilt:({0:f1},{1:f1}) az:{2:f2} al:{3:f2} twist:{4} pressure:{5} isInverted:{6}", tilt.x, tilt.y, azimuth, altitude, twist, pressure, isInverted ? 1 : 0)
                : "";
            string touch = eventSource == EventSource.Touch
                ? string.Format(" finger:{0} tilt:({1:f1},{2:f1}) twist:{3} pressure:{4}", pointerIndex, tilt.x, tilt.y, twist, pressure)
                : "";
            string extra = pen + touch + $" dsp:{displayIndex}";
            switch (type)
            {
                case Type.PointerMoved:
                    return string.Format("{0} pos:{1} dlt:{2} btns:{3}{4}", type, position, deltaPosition, buttonsState, extra);
                case Type.Scroll:
                    return string.Format("{0} pos:{1} scr:{2}{3}", type, position, scroll, extra);
                case Type.ButtonPressed:
                case Type.ButtonReleased:
                    return string.Format("{0} pos:{1} btn:{2} btns:{3} clk:{4}{5}", type, position, button, buttonsState, clickCount, extra);
                case Type.State:
                    return string.Format("{0} pos:{1} btns:{2}{3}", type, position, buttonsState, extra);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal static Button ButtonFromButtonIndex(int index) => index <= 31 ? (Button)(1 << index) : Button.None;
    }

    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct TextInputEvent : IEventProperties
    {
        public char character;

        public DiscreteTime timestamp { get; set; }
        public EventSource eventSource { get; set; }
        public uint playerId { get; set; }
        public EventModifiers eventModifiers { get; set; }

        public override string ToString()
        {
            string text = character == '\0' ? string.Empty : character.ToString();
            return $"text input 0x{(int)character:x8} '{text}'";
        }

        public static bool ShouldBeProcessed(char character) => character > '\u001f' && character != '\u007f';
    }

    /// <summary>One event of any kind: a tagged union of the event structs (the IME event, which
    /// holds a string, is boxed beside them).</summary>
    [StructLayout(LayoutKind.Explicit)]
    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal partial struct Event : IEventProperties
    {
        public enum Type
        {
            Invalid,
            KeyEvent,
            PointerEvent,
            TextInputEvent,
            IMECompositionEvent,
            CommandEvent,
            NavigationEvent,
        }

        interface IMapFn<TOutputType>
        {
            TOutputType Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties;
        }

        struct MapAsObject : IMapFn<IEventProperties>
        {
            public IEventProperties Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties => ev;
        }

        struct MapAsTimestamp : IMapFn<DiscreteTime>
        {
            public DiscreteTime Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties => ev.timestamp;
        }

        struct MapAsEventSource : IMapFn<EventSource>
        {
            public EventSource Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties => ev.eventSource;
        }

        struct MapAsPlayerId : IMapFn<uint>
        {
            public uint Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties => ev.playerId;
        }

        struct MapAsEventModifiers : IMapFn<EventModifiers>
        {
            public EventModifiers Map<TEventType>(ref TEventType ev) where TEventType : IEventProperties => ev.eventModifiers;
        }

        public static Type[] TypesWithState = { Type.KeyEvent, Type.PointerEvent, Type.IMECompositionEvent };

        const int kManagedOffset = 8;
        const int kUnmanagedOffset = 16;

        [FieldOffset(0)] Type _type;
        [FieldOffset(kManagedOffset)] object _managedEvent;
        [FieldOffset(kUnmanagedOffset)] KeyEvent _keyEvent;
        [FieldOffset(kUnmanagedOffset)] PointerEvent _pointerEvent;
        [FieldOffset(kUnmanagedOffset)] TextInputEvent _textInputEvent;
        [FieldOffset(kUnmanagedOffset)] CommandEvent _commandEvent;
        [FieldOffset(kUnmanagedOffset)] NavigationEvent _navigationEvent;

        public Type type => _type;
        IEventProperties asObject => Map<IEventProperties, MapAsObject>();
        public DiscreteTime timestamp => Map<DiscreteTime, MapAsTimestamp>();
        public EventSource eventSource => Map<EventSource, MapAsEventSource>();
        public uint playerId => Map<uint, MapAsPlayerId>();
        public EventModifiers eventModifiers => Map<EventModifiers, MapAsEventModifiers>();

        public KeyEvent asKeyEvent
        {
            get
            {
                Ensure(Type.KeyEvent);
                return _keyEvent;
            }
        }

        public PointerEvent asPointerEvent
        {
            get
            {
                Ensure(Type.PointerEvent);
                return _pointerEvent;
            }
        }

        public TextInputEvent asTextInputEvent
        {
            get
            {
                Ensure(Type.TextInputEvent);
                return _textInputEvent;
            }
        }

        public IMECompositionEvent asIMECompositionEvent
        {
            get
            {
                Ensure(Type.IMECompositionEvent);
                return (IMECompositionEvent)_managedEvent;
            }
        }

        public CommandEvent asCommandEvent
        {
            get
            {
                Ensure(Type.CommandEvent);
                return _commandEvent;
            }
        }

        public NavigationEvent asNavigationEvent
        {
            get
            {
                Ensure(Type.NavigationEvent);
                return _navigationEvent;
            }
        }

        /// <summary>Orders events by type; pointer events by source, highest first.</summary>
        internal static int CompareType(Event a, Event b)
        {
            if (a.type == Type.PointerEvent && b.type == Type.PointerEvent)
                return ((int)b.eventSource).CompareTo((int)a.eventSource);
            return ((int)a.type).CompareTo((int)b.type);
        }

        void Ensure(Type t) => Debug.Assert(type == t);

        public override string ToString()
        {
            string modifiers = eventModifiers.ToString();
            if (!string.IsNullOrEmpty(modifiers)) modifiers = " ev:" + modifiers;
            return type == Type.Invalid ? "Invalid" : $"{asObject}{modifiers} src:{eventSource}";
        }

        public static Event From(KeyEvent keyEvent) => new Event { _type = Type.KeyEvent, _keyEvent = keyEvent };
        public static Event From(PointerEvent pointerEvent) => new Event { _type = Type.PointerEvent, _pointerEvent = pointerEvent };
        public static Event From(TextInputEvent textInputEvent) => new Event { _type = Type.TextInputEvent, _textInputEvent = textInputEvent };
        public static Event From(IMECompositionEvent imeCompositionEvent) => new Event { _type = Type.IMECompositionEvent, _managedEvent = imeCompositionEvent };
        public static Event From(CommandEvent commandEvent) => new Event { _type = Type.CommandEvent, _commandEvent = commandEvent };
        public static Event From(NavigationEvent navigationEvent) => new Event { _type = Type.NavigationEvent, _navigationEvent = navigationEvent };

        TOutputType Map<TOutputType, TMapType>(TMapType fn) where TMapType : IMapFn<TOutputType>
        {
            switch (type)
            {
                case Type.Invalid:
                    return default;
                case Type.KeyEvent:
                    return fn.Map(ref _keyEvent);
                case Type.PointerEvent:
                    return fn.Map(ref _pointerEvent);
                case Type.TextInputEvent:
                    return fn.Map(ref _textInputEvent);
                case Type.IMECompositionEvent:
                {
                    var ev = (IMECompositionEvent)_managedEvent;
                    return fn.Map(ref ev);
                }
                case Type.CommandEvent:
                    return fn.Map(ref _commandEvent);
                case Type.NavigationEvent:
                    return fn.Map(ref _navigationEvent);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        TOutputType Map<TOutputType, TMapType>() where TMapType : IMapFn<TOutputType>, new() => Map<TOutputType, TMapType>(new TMapType());
    }
}
