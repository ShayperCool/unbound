using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A UnityGUI event: mouse and keyboard input, plus Layout and Repaint. Each frame's input
    /// events are queued (<see cref="PopEvent"/>, which uGUI's input fields read) and dispatched to
    /// OnGUI; positions are GUI pixels from the top left of the screen.
    /// </summary>
    public sealed partial class Event
    {
        EventType m_Type;
        Vector2 m_MousePosition;
        Vector2 m_Delta;
        PointerType m_PointerType;
        int m_Button;
        EventModifiers m_Modifiers;
        float m_Pressure;
        float m_Twist;
        Vector2 m_Tilt;
        int m_ClickCount;
        char m_Character;
        KeyCode m_KeyCode;
        int m_DisplayIndex;
        string m_CommandName = "";

        internal const float scrollWheelDeltaPerTick = 3f;

        static Event s_Current;

        public Event() { }

        public Event(int displayIndex) => m_DisplayIndex = displayIndex;

        public Event(Event other)
        {
            if (other == null) throw new ArgumentException("Event to copy from is null.");
            CopyFrom(other);
        }

        internal void CopyFrom(Event e)
        {
            m_Type = e.m_Type;
            m_MousePosition = e.m_MousePosition;
            m_Delta = e.m_Delta;
            m_PointerType = e.m_PointerType;
            m_Button = e.m_Button;
            m_Modifiers = e.m_Modifiers;
            m_Pressure = e.m_Pressure;
            m_Twist = e.m_Twist;
            m_Tilt = e.m_Tilt;
            m_ClickCount = e.m_ClickCount;
            m_Character = e.m_Character;
            m_KeyCode = e.m_KeyCode;
            m_DisplayIndex = e.m_DisplayIndex;
            m_CommandName = e.m_CommandName;
        }

        /// <summary>The event OnGUI is handling; null outside OnGUI.</summary>
        public static Event current
        {
            get => s_Current;
            set => s_Current = value;
        }

        public EventType rawType => m_Type;

        public EventType type
        {
            get => m_Type;
            set => m_Type = value;
        }

        public Vector2 mousePosition
        {
            get => m_MousePosition;
            set => m_MousePosition = value;
        }

        public Vector2 delta
        {
            get => m_Delta;
            set => m_Delta = value;
        }

        public PointerType pointerType
        {
            get => m_PointerType;
            set => m_PointerType = value;
        }

        public int button
        {
            get => m_Button;
            set => m_Button = value;
        }

        public EventModifiers modifiers
        {
            get => m_Modifiers;
            set => m_Modifiers = value;
        }

        public float pressure
        {
            get => m_Pressure;
            set => m_Pressure = value;
        }

        public float twist
        {
            get => m_Twist;
            set => m_Twist = value;
        }

        public Vector2 tilt
        {
            get => m_Tilt;
            set => m_Tilt = value;
        }

        public int clickCount
        {
            get => m_ClickCount;
            set => m_ClickCount = value;
        }

        public char character
        {
            get => m_Character;
            set => m_Character = value;
        }

        public KeyCode keyCode
        {
            get => m_KeyCode;
            set => m_KeyCode = value;
        }

        public int displayIndex
        {
            get => m_DisplayIndex;
            set => m_DisplayIndex = value;
        }

        public string commandName
        {
            get => m_CommandName;
            set => m_CommandName = value ?? "";
        }

        bool Has(EventModifiers m) => (m_Modifiers & m) != 0;

        void Set(EventModifiers m, bool on) => m_Modifiers = on ? m_Modifiers | m : m_Modifiers & ~m;

        public bool shift { get => Has(EventModifiers.Shift); set => Set(EventModifiers.Shift, value); }
        public bool control { get => Has(EventModifiers.Control); set => Set(EventModifiers.Control, value); }
        public bool alt { get => Has(EventModifiers.Alt); set => Set(EventModifiers.Alt, value); }
        public bool command { get => Has(EventModifiers.Command); set => Set(EventModifiers.Command, value); }
        public bool capsLock { get => Has(EventModifiers.CapsLock); set => Set(EventModifiers.CapsLock, value); }
        public bool numeric { get => Has(EventModifiers.Numeric); set => Set(EventModifiers.Numeric, value); }
        public bool functionKey => Has(EventModifiers.FunctionKey);

        public bool isKey => m_Type == EventType.KeyDown || m_Type == EventType.KeyUp;

        public bool isMouse =>
            m_Type == EventType.MouseMove || m_Type == EventType.MouseDown || m_Type == EventType.MouseUp ||
            m_Type == EventType.MouseDrag || m_Type == EventType.ContextClick ||
            m_Type == EventType.MouseEnterWindow || m_Type == EventType.MouseLeaveWindow;

        public bool isScrollWheel => m_Type == EventType.ScrollWheel;

        internal bool isDirectManipulationDevice => m_PointerType == PointerType.Pen || m_PointerType == PointerType.Touch;

        /// <summary>The event's type as a control sees it: mouse events belong to the control
        /// holding the mouse (the hot control), and a disabled GUI ignores input.</summary>
        public EventType GetTypeForControl(int controlID)
        {
            switch (m_Type)
            {
                case EventType.Repaint:
                case EventType.Layout:
                case EventType.Used:
                    return m_Type;
            }
            if (!GUI.enabled) return EventType.Ignore;
            int hot = GUIUtility.hotControl;
            if (hot != 0 && hot != controlID)
            {
                switch (m_Type)
                {
                    case EventType.MouseDown:
                    case EventType.MouseUp:
                    case EventType.MouseMove:
                    case EventType.MouseDrag:
                    case EventType.ContextClick:
                    case EventType.ScrollWheel:
                        return EventType.Ignore;
                }
            }
            return m_Type;
        }

        public void Use()
        {
            if (m_Type == EventType.Repaint || m_Type == EventType.Layout)
            {
                Debug.LogWarning($"Event.Use() should not be called for events of type {m_Type}");
                return;
            }
            m_Type = EventType.Used;
        }

        /// <summary>Moves the next of this frame's input events into <paramref name="outEvent"/>.</summary>
        public static bool PopEvent(Event outEvent) => GuiEvents.Pop(outEvent);

        internal static void QueueEvent(Event outEvent) => GuiEvents.Queue(outEvent);

        public static int GetEventCount() => GuiEvents.Count;

        internal static void GetEventAtIndex(int index, Event outEvent) => GuiEvents.CopyAt(index, outEvent);

        internal static void ClearEvents() => GuiEvents.Clear();

        internal static int GetDoubleClickTime() => GuiEvents.DoubleClickMilliseconds;

        /// <summary>A key event from a description: modifier prefixes (<c>&amp;</c> Alt, <c>^</c>
        /// Control, <c>%</c> Command, <c>#</c> Shift) then a character or a key name, as Unity
        /// reads it (navigation, editing and function keys carry the FunctionKey modifier;
        /// "pgup"/"pgdown" map to PageDown/PageUp as in Unity).</summary>
        public static Event KeyboardEvent(string key)
        {
            var e = new Event { m_Type = EventType.KeyDown };
            if (string.IsNullOrEmpty(key)) return e;
            int i = 0;
            for (; i < key.Length; i++)
            {
                if (key[i] == '&') e.m_Modifiers |= EventModifiers.Alt;
                else if (key[i] == '^') e.m_Modifiers |= EventModifiers.Control;
                else if (key[i] == '%') e.m_Modifiers |= EventModifiers.Command;
                else if (key[i] == '#') e.m_Modifiers |= EventModifiers.Shift;
                else break;
            }
            var name = key.Substring(i).ToLowerInvariant();
            void Key(KeyCode code, char c = '\0', bool function = false)
            {
                e.m_KeyCode = code;
                if (c != '\0') e.m_Character = c;
                if (function) e.m_Modifiers |= EventModifiers.FunctionKey;
            }
            if (name.Length == 3 && name[0] == '[' && name[2] == ']' && name[1] >= '0' && name[1] <= '9')
            {
                Key(KeyCode.Keypad0 + (name[1] - '0'), name[1]);
                return e;
            }
            switch (name)
            {
                case "[.]": Key(KeyCode.KeypadPeriod, '.'); return e;
                case "[/]": Key(KeyCode.KeypadDivide, '/'); return e;
                case "[-]": Key(KeyCode.KeypadMinus, '-'); return e;
                case "[+]": Key(KeyCode.KeypadPlus, '+'); return e;
                case "[=]":
                case "[equals]": Key(KeyCode.KeypadEquals, '='); return e;
                case "[enter]": Key(KeyCode.KeypadEnter, '\n'); return e;
                case "up": Key(KeyCode.UpArrow, function: true); return e;
                case "down": Key(KeyCode.DownArrow, function: true); return e;
                case "left": Key(KeyCode.LeftArrow, function: true); return e;
                case "right": Key(KeyCode.RightArrow, function: true); return e;
                case "insert": Key(KeyCode.Insert, function: true); return e;
                case "home": Key(KeyCode.Home, function: true); return e;
                case "end": Key(KeyCode.End, function: true); return e;
                case "pgup": Key(KeyCode.PageDown, function: true); return e;
                case "page up": Key(KeyCode.PageUp, function: true); return e;
                case "pgdown": Key(KeyCode.PageUp, function: true); return e;
                case "page down": Key(KeyCode.PageDown, function: true); return e;
                case "backspace": Key(KeyCode.Backspace, function: true); return e;
                case "delete": Key(KeyCode.Delete, function: true); return e;
                case "tab": Key(KeyCode.Tab); return e;
                case "[esc]": Key(KeyCode.Escape); return e;
                case "return": Key(KeyCode.Return, '\n'); return e;
                case "space": Key(KeyCode.Space, ' '); return e;
            }
            if (name.Length > 1 && name[0] == 'f' && int.TryParse(name.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
            {
                Key(f <= 15 ? KeyCode.F1 + (f - 1) : KeyCode.F16 + (f - 16), function: true);
                return e;
            }
            if (name.Length == 1)
            {
                e.m_Character = name[0];
                e.m_KeyCode = (KeyCode)name[0];
                if (e.m_Modifiers != EventModifiers.None) e.m_Character = '\0';
                return e;
            }
            if (Enum.TryParse<KeyCode>(name, true, out var code) && !int.TryParse(name, out _)) e.m_KeyCode = code;
            else Debug.LogError($"Unable to find key name that matches '{name}'");
            return e;
        }

        public override int GetHashCode()
        {
            int hc = 1;
            if (isKey) hc = (ushort)m_KeyCode;
            if (isMouse) hc = m_MousePosition.GetHashCode();
            return hc * 37 | (int)m_Modifiers;
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            var e = (Event)obj;
            if (m_Type != e.m_Type || (m_Modifiers & ~EventModifiers.CapsLock) != (e.m_Modifiers & ~EventModifiers.CapsLock)) return false;
            if (isKey) return m_KeyCode == e.m_KeyCode;
            if (isMouse) return m_MousePosition == e.m_MousePosition;
            return false;
        }

        public override string ToString()
        {
            if (isKey)
            {
                if (m_Character == '\0')
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Event:{0}   Character:\\0   Modifiers:{1}   KeyCode:{2}", m_Type, m_Modifiers, m_KeyCode);
                return "Event:" + m_Type + "   Character:" + (int)m_Character + "   Modifiers:" + m_Modifiers + "   KeyCode:" + m_KeyCode;
            }
            if (isMouse)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Event: {0}   Position: {1} Modifiers: {2}", m_Type, m_MousePosition, m_Modifiers);
            if (m_Type == EventType.ExecuteCommand || m_Type == EventType.ValidateCommand)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "Event: {0}  \"{1}\"", m_Type, m_CommandName);
            return "" + m_Type;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// The frame's input as UnityGUI events, built from the native input once per frame: mouse
    /// presses, drags and releases, the scroll wheel, then key presses (a <c>KeyDown</c> with the
    /// key code, then one per typed character with <c>KeyCode.None</c>) and releases, in order.
    /// </summary>
    internal static class GuiEvents
    {
        internal const int DoubleClickMilliseconds = 500;
        const float DoubleClickDistance = 4f;

        static readonly List<Event> s_Frame = new List<Event>();
        static int s_Next;
        static uint s_Buttons;
        static Vector2 s_Mouse;
        static bool s_HasMouse;
        static readonly Dictionary<int, (double time, Vector2 at, int count)> s_LastClick = new Dictionary<int, (double, Vector2, int)>();
        static readonly bool[] s_Held = new bool[512];

        /// <summary>This frame's input events, in order.</summary>
        internal static IReadOnlyList<Event> Frame => s_Frame;

        internal static int Count => s_Frame.Count;

        internal static bool Pop(Event outEvent)
        {
            if (s_Next >= s_Frame.Count) return false;
            outEvent?.CopyFrom(s_Frame[s_Next++]);
            return true;
        }

        internal static void Queue(Event e)
        {
            if (e != null) s_Frame.Add(new Event(e));
        }

        internal static void CopyAt(int index, Event outEvent)
        {
            if (index < 0 || index >= s_Frame.Count) throw new ArgumentOutOfRangeException(nameof(index));
            outEvent.CopyFrom(s_Frame[index]);
        }

        internal static void Clear()
        {
            s_Frame.Clear();
            s_Next = 0;
        }

        /// <summary>The modifier keys held now.</summary>
        internal static EventModifiers Modifiers()
        {
            var m = EventModifiers.None;
            if (Input.KeyHeld(KeyCode.LeftShift) || Input.KeyHeld(KeyCode.RightShift)) m |= EventModifiers.Shift;
            if (Input.KeyHeld(KeyCode.LeftControl) || Input.KeyHeld(KeyCode.RightControl)) m |= EventModifiers.Control;
            if (Input.KeyHeld(KeyCode.LeftAlt) || Input.KeyHeld(KeyCode.RightAlt)) m |= EventModifiers.Alt;
            if (Input.KeyHeld(KeyCode.LeftCommand) || Input.KeyHeld(KeyCode.RightCommand)) m |= EventModifiers.Command;
            return m;
        }

        static EventModifiers KeyModifiers(KeyCode key)
        {
            int k = (int)key;
            if (k >= (int)KeyCode.Keypad0 && k <= (int)KeyCode.KeypadEquals) return EventModifiers.Numeric;
            if ((k >= (int)KeyCode.UpArrow && k <= (int)KeyCode.F15) || key == KeyCode.Delete) return EventModifiers.FunctionKey;
            return EventModifiers.None;
        }

        static Event Make(EventType type, Vector2 position, EventModifiers modifiers) => new Event
        {
            type = type,
            mousePosition = position,
            modifiers = modifiers,
            pointerType = PointerType.Mouse,
        };

        /// <summary>Builds the frame's events. Mouse coordinates are native: pixels from the top
        /// left, which is GUI space.</summary>
        internal static unsafe void BeginFrame(FrameInput* input)
        {
            Clear();
            var mods = Modifiers();
            var mouse = new Vector2(input->MouseX, input->MouseY);
            var moved = s_HasMouse ? mouse - s_Mouse : Vector2.zero;
            uint buttons = input->MouseButtons;
            if (moved != Vector2.zero && s_Buttons != 0)
            {
                int held = 0;
                while (held < 32 && (s_Buttons & (1u << held)) == 0) held++;
                var drag = Make(EventType.MouseDrag, mouse, mods);
                drag.delta = moved;
                drag.button = held;
                s_Frame.Add(drag);
            }
            for (int b = 0; b < 5; b++)
            {
                uint bit = 1u << b;
                if ((buttons & bit) != 0 && (s_Buttons & bit) == 0)
                {
                    var down = Make(EventType.MouseDown, mouse, mods);
                    down.button = b;
                    down.clickCount = ClickCount(b, mouse);
                    s_Frame.Add(down);
                }
            }
            for (int b = 0; b < 5; b++)
            {
                uint bit = 1u << b;
                if ((buttons & bit) == 0 && (s_Buttons & bit) != 0)
                {
                    var up = Make(EventType.MouseUp, mouse, mods);
                    up.button = b;
                    up.clickCount = s_LastClick.TryGetValue(b, out var last) ? last.count : 1;
                    s_Frame.Add(up);
                }
            }
            if (input->ScrollX != 0f || input->ScrollY != 0f)
            {
                // Unity's wheel delta: three lines per notch, positive towards the user.
                var wheel = Make(EventType.ScrollWheel, mouse, mods);
                wheel.delta = new Vector2(input->ScrollX, -input->ScrollY) * Event.scrollWheelDeltaPerTick;
                s_Frame.Add(wheel);
            }
            s_Buttons = buttons;
            s_Mouse = mouse;
            s_HasMouse = true;

            for (int i = 0; i < input->KeyEventCount; i++)
            {
                var k = input->KeyEvents[i];
                var key = (KeyCode)k.Key;
                bool known = k.Key > 0 && k.Key < s_Held.Length;
                if (k.Pressed == 0)
                {
                    if (known) s_Held[k.Key] = false;
                    var up = Make(EventType.KeyUp, mouse, mods | KeyModifiers(key));
                    up.keyCode = key;
                    s_Frame.Add(up);
                    continue;
                }
                if (known) s_Held[k.Key] = true;
                if (key != KeyCode.None)
                {
                    var down = Make(EventType.KeyDown, mouse, mods | KeyModifiers(key));
                    down.keyCode = key;
                    s_Frame.Add(down);
                }
                if (k.TextLength <= 0 || input->Text == null) continue;
                // Characters typed with Control or Command held are shortcuts, not text.
                if ((mods & (EventModifiers.Control | EventModifiers.Command)) != 0) continue;
                var text = Encoding.UTF8.GetString(input->Text + k.TextStart, k.TextLength);
                foreach (char c in text)
                {
                    char typed = c == '\r' ? '\n' : c;
                    if (char.IsControl(typed) && typed != '\n' && typed != '\t') continue;
                    var ch = Make(EventType.KeyDown, mouse, mods | KeyModifiers(key));
                    ch.character = typed;
                    s_Frame.Add(ch);
                }
            }
        }

        static int ClickCount(int button, Vector2 at)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            int count = 1;
            if (s_LastClick.TryGetValue(button, out var last) &&
                (now - last.time) * 1000.0 <= DoubleClickMilliseconds &&
                (at - last.at).sqrMagnitude <= DoubleClickDistance * DoubleClickDistance)
                count = last.count + 1;
            s_LastClick[button] = (now, at, count);
            return count;
        }
    }
}
