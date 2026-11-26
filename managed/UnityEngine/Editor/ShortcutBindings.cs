using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UnityEditor.ShortcutManagement
{
    /// <summary>
    /// A key and its modifiers, as Unity's own C# writes it: the key code reads None for a key the
    /// shortcut system does not support; the text names the modifiers and the key in the Editor's
    /// platform style (key glyphs on macOS, "Ctrl+Alt+Shift+" elsewhere, where the action and control
    /// modifiers also compare equal); the menu string is a menu item's hotkey suffix.
    /// </summary>
    [Serializable]
    public partial struct KeyCombination : IEquatable<KeyCombination>
    {
        [SerializeField] internal KeyCode m_KeyCode;
        [SerializeField] ShortcutModifiers m_Modifiers;

        static readonly Dictionary<KeyCode, string> s_KeyCodeToMenuItemKeyCodeString = new()
        {
            { KeyCode.LeftArrow, "LEFT" }, { KeyCode.UpArrow, "UP" }, { KeyCode.RightArrow, "RIGHT" }, { KeyCode.DownArrow, "DOWN" },
            { KeyCode.PageDown, "PGDN" }, { KeyCode.PageUp, "PGUP" }, { KeyCode.Home, "HOME" }, { KeyCode.Insert, "INS" },
            { KeyCode.Delete, "DEL" }, { KeyCode.End, "END" }, { KeyCode.Tab, "TAB" }, { KeyCode.Space, "SPACE" },
            { KeyCode.Backspace, "BACKSPACE" }, { KeyCode.Return, "ENTER" },
            { KeyCode.F1, "F1" }, { KeyCode.F2, "F2" }, { KeyCode.F3, "F3" }, { KeyCode.F4, "F4" }, { KeyCode.F5, "F5" },
            { KeyCode.F6, "F6" }, { KeyCode.F7, "F7" }, { KeyCode.F8, "F8" }, { KeyCode.F9, "F9" }, { KeyCode.F10, "F10" },
            { KeyCode.F11, "F11" }, { KeyCode.F12, "F12" }, { KeyCode.F13, "F13" }, { KeyCode.F14, "F14" }, { KeyCode.F15, "F15" },
            { KeyCode.F16, "F16" }, { KeyCode.F17, "F17" }, { KeyCode.F18, "F18" }, { KeyCode.F19, "F19" }, { KeyCode.F20, "F20" },
            { KeyCode.F21, "F21" }, { KeyCode.F22, "F22" }, { KeyCode.F23, "F23" }, { KeyCode.F24, "F24" },
            { KeyCode.Mouse0, "M0" }, { KeyCode.Mouse1, "M1" }, { KeyCode.Mouse2, "M2" }, { KeyCode.Mouse3, "M3" },
            { KeyCode.Mouse4, "M4" }, { KeyCode.Mouse5, "M5" }, { KeyCode.Mouse6, "M6" },
        };

        static readonly Dictionary<string, KeyCode> s_MenuItemKeyCodeStringToKeyCode =
            s_KeyCodeToMenuItemKeyCodeString.ToDictionary(item => item.Value, item => item.Key);

        static readonly Dictionary<KeyCode, string> s_KeyCodeNamesMacOS = new()
        {
            { KeyCode.Backspace, "⌫" }, { KeyCode.Tab, "⇥" }, { KeyCode.Return, "↩" }, { KeyCode.Escape, "⎋" },
            { KeyCode.Delete, "⌦" }, { KeyCode.UpArrow, "↑" }, { KeyCode.DownArrow, "↓" }, { KeyCode.RightArrow, "→" },
            { KeyCode.LeftArrow, "←" }, { KeyCode.Home, "↖" }, { KeyCode.End, "↘" }, { KeyCode.PageUp, "⇞" },
            { KeyCode.PageDown, "⇟" },
        };

        static readonly Dictionary<KeyCode, string> s_KeyCodeNamesNotMacOS = new()
        {
            { KeyCode.Return, "Enter" }, { KeyCode.Escape, "Esc" }, { KeyCode.Delete, "Del" },
            { KeyCode.UpArrow, "Up Arrow" }, { KeyCode.DownArrow, "Down Arrow" }, { KeyCode.RightArrow, "Right Arrow" },
            { KeyCode.LeftArrow, "Left Arrow" }, { KeyCode.PageUp, "Page Up" }, { KeyCode.PageDown, "Page Down" },
        };

        static readonly Dictionary<KeyCode, string> s_KeyCodeNamesCommon = new()
        {
            { KeyCode.Exclaim, "!" }, { KeyCode.DoubleQuote, "\"" }, { KeyCode.Hash, "#" }, { KeyCode.Dollar, "$" },
            { KeyCode.Percent, "%" }, { KeyCode.Ampersand, "&" }, { KeyCode.Quote, "'" }, { KeyCode.LeftParen, "(" },
            { KeyCode.RightParen, ")" }, { KeyCode.Asterisk, "*" }, { KeyCode.Plus, "+" }, { KeyCode.Comma, "," },
            { KeyCode.Minus, "-" }, { KeyCode.Period, "." }, { KeyCode.Slash, "/" },
            { KeyCode.Alpha0, "0" }, { KeyCode.Alpha1, "1" }, { KeyCode.Alpha2, "2" }, { KeyCode.Alpha3, "3" }, { KeyCode.Alpha4, "4" },
            { KeyCode.Alpha5, "5" }, { KeyCode.Alpha6, "6" }, { KeyCode.Alpha7, "7" }, { KeyCode.Alpha8, "8" }, { KeyCode.Alpha9, "9" },
            { KeyCode.Colon, ":" }, { KeyCode.Semicolon, ";" }, { KeyCode.Less, "<" }, { KeyCode.Equals, "=" },
            { KeyCode.Greater, ">" }, { KeyCode.Question, "?" }, { KeyCode.At, "@" }, { KeyCode.LeftBracket, "[" },
            { KeyCode.Backslash, "\\" }, { KeyCode.RightBracket, "]" }, { KeyCode.Caret, "^" }, { KeyCode.Underscore, "_" },
            { KeyCode.BackQuote, "`" }, { KeyCode.LeftCurlyBracket, "{" }, { KeyCode.Pipe, "|" }, { KeyCode.RightCurlyBracket, "}" },
            { KeyCode.Tilde, "~" },
            { KeyCode.Keypad0, "Num 0" }, { KeyCode.Keypad1, "Num 1" }, { KeyCode.Keypad2, "Num 2" }, { KeyCode.Keypad3, "Num 3" },
            { KeyCode.Keypad4, "Num 4" }, { KeyCode.Keypad5, "Num 5" }, { KeyCode.Keypad6, "Num 6" }, { KeyCode.Keypad7, "Num 7" },
            { KeyCode.Keypad8, "Num 8" }, { KeyCode.Keypad9, "Num 9" }, { KeyCode.KeypadPeriod, "Num ." },
            { KeyCode.KeypadDivide, "Num /" }, { KeyCode.KeypadMultiply, "Num *" }, { KeyCode.KeypadMinus, "Num -" },
            { KeyCode.KeypadPlus, "Num +" }, { KeyCode.KeypadEnter, "Num Enter" }, { KeyCode.KeypadEquals, "Num =" },
            { KeyCode.WheelUp, "Wheel Up" }, { KeyCode.WheelDown, "Wheel Down" },
            { KeyCode.Mouse0, "Mouse 0" }, { KeyCode.Mouse1, "Mouse 1" }, { KeyCode.Mouse2, "Mouse 2" }, { KeyCode.Mouse3, "Mouse 3" },
            { KeyCode.Mouse4, "Mouse 4" }, { KeyCode.Mouse5, "Mouse 5" }, { KeyCode.Mouse6, "Mouse 6" },
        };

        internal static readonly KeyCode[] k_MouseKeyCodes =
        {
            KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6,
        };

        internal static readonly Dictionary<KeyCode, EventModifiers> k_KeyCodeToEventModifiers = new()
        {
            { KeyCode.None, EventModifiers.None },
            { KeyCode.LeftAlt, EventModifiers.Alt }, { KeyCode.RightAlt, EventModifiers.Alt },
            { KeyCode.LeftControl, EventModifiers.Control }, { KeyCode.RightControl, EventModifiers.Control },
            { KeyCode.LeftMeta, EventModifiers.Command }, { KeyCode.RightMeta, EventModifiers.Command },
            { KeyCode.LeftShift, EventModifiers.Shift }, { KeyCode.RightShift, EventModifiers.Shift },
        };

        static bool MacEditor => Application.platform == RuntimePlatform.OSXEditor;

        public KeyCode keyCode => IsKeyCodeSupported(m_KeyCode) ? m_KeyCode : KeyCode.None;

        /// <summary>The shortcut directory's check (Directory.IsKeyCodeSupported, whose class is
        /// not written by hand yet): the keyboard and mouse keys up to the last mouse button, and
        /// F16 to F24.</summary>
        static bool IsKeyCodeSupported(KeyCode key) => key <= KeyCode.Mouse6 || (key >= KeyCode.F16 && key <= KeyCode.F24);
        public ShortcutModifiers modifiers => m_Modifiers;
        public bool alt => (modifiers & ShortcutModifiers.Alt) == ShortcutModifiers.Alt;
        public bool action => (modifiers & ShortcutModifiers.Action) == ShortcutModifiers.Action;
        public bool shift => (modifiers & ShortcutModifiers.Shift) == ShortcutModifiers.Shift;
        public bool control => (modifiers & ShortcutModifiers.Control) == ShortcutModifiers.Control;

        public KeyCombination(KeyCode keyCode, ShortcutModifiers shortcutModifiers = ShortcutModifiers.None)
        {
            m_KeyCode = keyCode;
            m_Modifiers = shortcutModifiers;
        }

        internal static KeyCombination FromInput(Event evt) => FromKeyboardInput(evt.keyCode, evt.modifiers);

        internal static KeyCombination FromKeyboardInput(Event evt) => FromKeyboardInput(evt.keyCode, evt.modifiers);

        internal static KeyCombination FromKeyboardInput(KeyCode keyCode, EventModifiers modifiers) =>
            new(keyCode, ConvertEventModifiersToShortcutModifiers(modifiers, coalesceCommandAndControl: false));

        internal static KeyCombination FromKeyboardInputAndAddEventModifiers(KeyCombination combination, EventModifiers modifiers) =>
            new(combination.keyCode, combination.modifiers | ConvertEventModifiersToShortcutModifiers(modifiers, coalesceCommandAndControl: false));

        internal static KeyCombination FromKeyboardInputAndRemoveEventModifiers(KeyCombination combination, EventModifiers modifiers) =>
            new(combination.keyCode, combination.modifiers & ~ConvertEventModifiersToShortcutModifiers(modifiers, coalesceCommandAndControl: false));

        internal static KeyCombination FromPrefKeyKeyboardEvent(Event evt) =>
            new(evt.keyCode, ConvertEventModifiersToShortcutModifiers(evt.modifiers, coalesceCommandAndControl: true));

        /// <summary>Alt and Shift as they are; Command is the action key on macOS (where Control
        /// stays Control), Control is the action key elsewhere; a preference key's either one.</summary>
        static ShortcutModifiers ConvertEventModifiersToShortcutModifiers(EventModifiers eventModifiers, bool coalesceCommandAndControl)
        {
            var result = ShortcutModifiers.None;
            if ((eventModifiers & EventModifiers.Alt) != 0) result |= ShortcutModifiers.Alt;
            if ((eventModifiers & EventModifiers.Shift) != 0) result |= ShortcutModifiers.Shift;
            if (coalesceCommandAndControl)
            {
                if ((eventModifiers & (EventModifiers.Control | EventModifiers.Command)) != 0) result |= ShortcutModifiers.Action;
            }
            else if (MacEditor)
            {
                if ((eventModifiers & EventModifiers.Command) != 0) result |= ShortcutModifiers.Action;
                if ((eventModifiers & EventModifiers.Control) != 0) result |= ShortcutModifiers.Control;
            }
            else if ((eventModifiers & EventModifiers.Control) != 0)
                result |= ShortcutModifiers.Action;
            return result;
        }

        internal Event ToKeyboardEvent() => new Event
        {
            type = EventType.KeyDown,
            alt = alt,
            command = action && MacEditor,
            control = (action && !MacEditor) || control,
            shift = shift,
            keyCode = keyCode,
        };

        internal static string SequenceToString(IEnumerable<KeyCombination> keyCombinations)
        {
            if (!keyCombinations.Any()) return "";
            var builder = new StringBuilder();
            builder.Append(keyCombinations.First());
            foreach (var combination in keyCombinations.Skip(1))
            {
                builder.Append(", ");
                builder.Append(combination);
            }
            return builder.ToString();
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            VisualizeModifiers(modifiers, builder);
            VisualizeKeyCode(keyCode, builder);
            return builder.ToString();
        }

        /// <summary>The hotkey suffix of a menu item's path: "&amp;" Alt, "#" Shift, "%" action,
        /// "^" Control, "_" for none, then the key; empty without a key.</summary>
        internal string ToMenuShortcutString()
        {
            if (keyCode == KeyCode.None) return string.Empty;
            var builder = new StringBuilder();
            if ((modifiers & ShortcutModifiers.Alt) != 0) builder.Append('&');
            if ((modifiers & ShortcutModifiers.Shift) != 0) builder.Append('#');
            if ((modifiers & ShortcutModifiers.Action) != 0) builder.Append('%');
            if ((modifiers & ShortcutModifiers.Control) != 0) builder.Append('^');
            if (modifiers == ShortcutModifiers.None) builder.Append('_');
            KeyCodeToMenuItemKeyCodeString(keyCode, builder);
            return builder.ToString();
        }

        static void KeyCodeToMenuItemKeyCodeString(KeyCode keyCode, StringBuilder builder)
        {
            if (s_KeyCodeToMenuItemKeyCodeString.TryGetValue(keyCode, out var name))
            {
                builder.Append(name);
                return;
            }
            char c = (char)keyCode;
            if ((c >= ' ' && c <= '@') || (c >= '[' && c <= '~'))
                builder.Append(c);
        }

        internal static bool TryParseMenuItemBindingString(string menuItemBindingString, out KeyCombination keyCombination)
        {
            keyCombination = default;
            if (string.IsNullOrEmpty(menuItemBindingString)) return false;
            var parsed = ShortcutModifiers.None;
            int index = 0;
            bool anyPrefix = false;
            bool isPrefix;
            do
            {
                isPrefix = true;
                if (index >= menuItemBindingString.Length) break;
                switch (menuItemBindingString[index])
                {
                    case '&': parsed |= ShortcutModifiers.Alt; index++; break;
                    case '%': parsed |= ShortcutModifiers.Action; index++; break;
                    case '#': parsed |= ShortcutModifiers.Shift; index++; break;
                    case '^': parsed |= ShortcutModifiers.Control; index++; break;
                    case '_': index++; break;
                    default: isPrefix = false; break;
                }
                anyPrefix |= isPrefix;
            }
            while (isPrefix);
            if (!anyPrefix || !TryParseMenuItemKeyCodeString(menuItemBindingString.Substring(index), out var key, out var additional))
                return false;
            keyCombination = new KeyCombination(key, parsed | additional);
            return true;
        }

        static bool TryParseMenuItemKeyCodeString(string keyCodeString, out KeyCode keyCode, out ShortcutModifiers additionalModifiers)
        {
            keyCode = KeyCode.None;
            additionalModifiers = ShortcutModifiers.None;
            if (string.IsNullOrEmpty(keyCodeString)) return false;
            if (s_MenuItemKeyCodeStringToKeyCode.TryGetValue(keyCodeString.ToUpperInvariant(), out keyCode)) return true;
            if (keyCodeString.Length != 1) return false;
            keyCode = (KeyCode)char.ToLowerInvariant(keyCodeString[0]);
            return Enum.IsDefined(typeof(KeyCode), keyCode);
        }

        internal static string SequenceToMenuString(IEnumerable<KeyCombination> keyCombinations) =>
            keyCombinations.Any() ? keyCombinations.Single().ToMenuShortcutString() : "";

        internal static void VisualizeModifiers(ShortcutModifiers modifiers, StringBuilder builder)
        {
            if (MacEditor)
            {
                if ((modifiers & ShortcutModifiers.Alt) != 0) builder.Append('⌥');
                if ((modifiers & ShortcutModifiers.Shift) != 0) builder.Append('⇧');
                if ((modifiers & ShortcutModifiers.Action) != 0) builder.Append('⌘');
                if ((modifiers & ShortcutModifiers.Control) != 0) builder.Append('^');
                return;
            }
            if ((modifiers & (ShortcutModifiers.Action | ShortcutModifiers.Control)) != 0) builder.Append("Ctrl+");
            if ((modifiers & ShortcutModifiers.Alt) != 0) builder.Append("Alt+");
            if ((modifiers & ShortcutModifiers.Shift) != 0) builder.Append("Shift+");
        }

        static void VisualizeKeyCode(KeyCode keyCode, StringBuilder builder)
        {
            if (!TryFormatKeycode(keyCode, builder))
                builder.Append(keyCode.ToString());
        }

        static bool TryFormatKeycode(KeyCode code, StringBuilder builder)
        {
            if ((MacEditor ? s_KeyCodeNamesMacOS : s_KeyCodeNamesNotMacOS).TryGetValue(code, out var name)
                || s_KeyCodeNamesCommon.TryGetValue(code, out name))
            {
                builder.Append(name);
                return true;
            }
            return false;
        }

        /// <summary>The same key and modifiers; outside macOS the action and control modifiers
        /// count as one.</summary>
        public bool Equals(KeyCombination other)
        {
            bool sameModifiers;
            if (!MacEditor)
            {
                const ShortcutModifiers ActionOrControl = ShortcutModifiers.Action | ShortcutModifiers.Control;
                sameModifiers = (m_Modifiers & ~ActionOrControl) == (other.m_Modifiers & ~ActionOrControl)
                    && ((modifiers & ActionOrControl) != 0) == ((other.modifiers & ActionOrControl) != 0);
            }
            else
                sameModifiers = m_Modifiers == other.m_Modifiers;
            return m_KeyCode == other.m_KeyCode && sameModifiers;
        }

        public override bool Equals(object obj) => obj is KeyCombination other && Equals(other);

        public override int GetHashCode() => ((int)m_KeyCode * 397) ^ (int)m_Modifiers;
    }

    /// <summary>A shortcut's key combinations, in order; the empty binding has none.</summary>
    public partial struct ShortcutBinding : IEquatable<ShortcutBinding>
    {
        readonly KeyCombination[] m_KeyCombinationSequence;

        public static ShortcutBinding empty { get; } = default;

        public IEnumerable<KeyCombination> keyCombinationSequence => m_KeyCombinationSequence ?? Enumerable.Empty<KeyCombination>();

        internal ShortcutBinding(IEnumerable<KeyCombination> keyCombinationSequence)
        {
            if (keyCombinationSequence == null)
                throw new ArgumentNullException(nameof(keyCombinationSequence));
            m_KeyCombinationSequence = keyCombinationSequence.Any() ? keyCombinationSequence.ToArray() : null;
        }

        public ShortcutBinding(KeyCombination keyCombination) : this(new[] { keyCombination }) { }

        public override string ToString() => KeyCombination.SequenceToString(keyCombinationSequence);

        public bool Equals(ShortcutBinding other) => keyCombinationSequence.SequenceEqual(other.keyCombinationSequence);

        public override bool Equals(object obj) => obj is ShortcutBinding other && Equals(other);

        /// <summary>The combinations' hashes folded as the Editor's tuples fold theirs.</summary>
        public override int GetHashCode()
        {
            if (m_KeyCombinationSequence == null || m_KeyCombinationSequence.Length == 0)
                return 0;
            int hash = m_KeyCombinationSequence[0].GetHashCode();
            for (int i = 1; i < m_KeyCombinationSequence.Length; i++)
                hash = Tuple.CombineHashCodes(hash, m_KeyCombinationSequence[i].GetHashCode());
            return hash;
        }
    }

    public partial struct ShortcutArguments
    {
        public object context;
        public ShortcutStage stage;
    }

    public partial struct ShortcutBindingChangedEventArgs
    {
        public string shortcutId { get; }
        public ShortcutBinding oldBinding { get; }
        public ShortcutBinding newBinding { get; }

        public ShortcutBindingChangedEventArgs(string shortcutId, ShortcutBinding oldBinding, ShortcutBinding newBinding)
        {
            this.shortcutId = shortcutId;
            this.oldBinding = oldBinding;
            this.newBinding = newBinding;
        }
    }

    public partial struct ActiveProfileChangedEventArgs
    {
        public string previousActiveProfileId { get; }
        public string currentActiveProfileId { get; }

        public ActiveProfileChangedEventArgs(string previousActiveProfileId, string currentActiveProfileId)
        {
            this.previousActiveProfileId = previousActiveProfileId;
            this.currentActiveProfileId = currentActiveProfileId;
        }
    }

    /// <summary>A shortcut's id: its path, "/" between the levels.</summary>
    [Serializable]
    internal partial struct Identifier
    {
        public string path;

        public Identifier(System.Reflection.MethodInfo methodInfo, ShortcutAttribute attribute) => path = attribute.identifier;

        public Identifier(string path) => this.path = path;

        public override string ToString() => path;
    }
}
