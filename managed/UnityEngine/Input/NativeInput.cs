using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngineInternal.Input;

namespace Unbound.Runtime
{
    /// <summary>
    /// What Unity's native input module does for the Input System package
    /// (<see cref="NativeInputSystem"/>), as the Linux Editor does it: it reports the window's
    /// keyboard and mouse as devices, turns each frame's window input into the events the native
    /// layer queues (keyboard and mouse state events, text events), keeps the event queue, answers
    /// device commands, and runs the input updates at their places in the player loop. Recorded
    /// against the Editor in tests/projects/input.
    /// </summary>
    internal static unsafe class NativeInput
    {
        // Device ids the Linux Editor gives its window devices (managed allocations follow).
        internal const int MouseId = 1;
        internal const int KeyboardId = 2;
        // The descriptions it reports (the "Gtk" interface).
        const string MouseDescription = "{\"interface\":\"Gtk\",\"type\":\"Mouse\",\"product\":\"\",\"manufacturer\":\"\",\"serial\":\"\",\"version\":\"\",\"capabilities\":\"\"}";
        const string KeyboardDescription = "{\"interface\":\"Gtk\",\"type\":\"Keyboard\",\"product\":\"\",\"manufacturer\":\"\",\"serial\":\"\",\"version\":\"\",\"capabilities\":\"\"}";
        const string KeyboardLayout = "English (US)";

        // Event types and state formats (FourCCs, as the package reads them).
        const int StateEventType = 0x53544154; // 'STAT'
        const int TextEventType = 0x54455854; // 'TEXT'
        const int KeyboardFormat = 0x4B455953; // 'KEYS'
        const int MouseFormat = 0x4D4F5553; // 'MOUS'
        const int GamepadFormat = 0x47504144; // 'GPAD'
        const int DeviceRemoveEventType = 0x4452454D; // 'DREM'
        const int EventHeaderSize = 20;
        // The native keyboard state is 160 bits (the package's Key enum fits in its first 127);
        // the mouse state 32 bytes: position, delta, scroll, buttons, display index, click count.
        const int KeyboardStateSize = 20;
        const int MouseStateSize = 32;
        // The gamepad state: buttons (32 bits), the sticks, the triggers.
        const int GamepadStateSize = 28;

        // Device command results.
        const long GenericFailure = -1;
        const long GenericSuccess = 1;
        const int KeyboardLayoutCommand = 0x4B424C54; // 'KBLT'
        const int KeyNameCommand = 0x4B594346; // 'KYCF'

        /// <summary>Whether the project lets the Input System's native backends run
        /// (Player Settings' active input handling is the Input System package or both).</summary>
        internal static bool Enabled = false;

        static int s_NextDeviceId = KeyboardId + 1;
        static bool s_DevicesReported;
        static readonly object s_Lock = new object();
        // Events queued since the last update (any thread), then the buffer an update hands out:
        // the events the last update left in it first, then the newly queued ones.
        static byte* s_Pending;
        static int s_PendingSize, s_PendingCapacity, s_PendingCount;
        static byte* s_Buffer;
        static int s_BufferSize, s_BufferCapacity, s_BufferCount;
        static int s_NextEventId = 1;
        static bool s_InUpdate;

        // The window's devices as the native layer last reported them.
        static readonly byte[] s_Keys = new byte[KeyboardStateSize];
        static Vector2 s_MousePosition;
        static ushort s_MouseButtons;
        static bool s_HaveWindowMouse;
        static Vector2 s_WindowMouse;

        sealed class Pad
        {
            internal int DeviceId;
            internal bool Reported;
            internal uint Buttons;
            internal float[] Values = new float[6];
        }

        // The SDL pads by SDL instance id.
        static readonly System.Collections.Generic.Dictionary<int, Pad> s_Pads = new System.Collections.Generic.Dictionary<int, Pad>();
        static bool s_WarnedJoystick;

        internal static bool HasDiscoveryCallback { get; set; }
        internal static float PollingFrequency { get; set; } = 60f;
        internal static bool NormalizeScrollWheelDelta { get; set; } = true;

        /// <summary>A wheel notch is one unit of scroll on Linux, normalized or not (measured).</summary>
        internal static float ScrollWheelDeltaPerTick => 1f;

        /// <summary>The input clock: the frame's unscaled time. Unity's runs on the real time since
        /// startup, which its unscaled time follows from the second frame on; Unbound's unscaled time
        /// is the frame clock (exact with --fixed-fps), and the package times fixed-update input
        /// against <c>Time.fixedUnscaledTime</c>, so input runs on it too.</summary>
        internal static double CurrentTime => Time.unscaledTimeAsDouble;

        internal static double TimeOffsetToRealtimeSinceStartup => 0.0;

        internal static int AllocateDeviceId()
        {
            lock (s_Lock) return s_NextDeviceId++;
        }

        // ------------------------------------------------------------------ events

        internal static void QueueEvent(IntPtr inputEvent)
        {
            if (inputEvent == IntPtr.Zero) throw new ArgumentNullException(nameof(inputEvent));
            var header = (NativeInputEvent*)inputEvent;
            int size = header->sizeInBytes;
            if (size < EventHeaderSize) throw new ArgumentException($"Input event of {size} bytes is smaller than its header", nameof(inputEvent));
            lock (s_Lock)
            {
                int aligned = Align(size);
                Reserve(ref s_Pending, ref s_PendingCapacity, s_PendingSize + aligned);
                var target = s_Pending + s_PendingSize;
                Buffer.MemoryCopy((void*)inputEvent, target, aligned, size);
                ((NativeInputEvent*)target)->eventId = s_NextEventId++;
                s_PendingSize += aligned;
                s_PendingCount++;
            }
        }

        static int Align(int size) => (size + 3) & ~3;

        static void Reserve(ref byte* buffer, ref int capacity, int needed)
        {
            if (needed <= capacity) return;
            int grown = Math.Max(needed, Math.Max(4096, capacity * 2));
            buffer = buffer == null ? (byte*)Marshal.AllocHGlobal(grown) : (byte*)Marshal.ReAllocHGlobal((IntPtr)buffer, (IntPtr)grown);
            capacity = grown;
        }

        /// <summary>An input update: the package prepares (<c>onBeforeUpdate</c>), then processes the
        /// queued events (<c>onUpdate</c>), leaving those it does not consume for the next update.
        /// Devices not yet reported are reported first.</summary>
        internal static void Update(NativeInputUpdateType updateType)
        {
            // An update the package asks for while it processes one has nothing new to process.
            if (s_InUpdate) return;
            s_InUpdate = true;
            try
            {
                ReportDevices();
                ReportPads();
                NativeInputSystem.NotifyBeforeUpdate(updateType);
                lock (s_Lock)
                {
                    Reserve(ref s_Buffer, ref s_BufferCapacity, s_BufferSize + s_PendingSize);
                    if (s_PendingSize > 0) Buffer.MemoryCopy(s_Pending, s_Buffer + s_BufferSize, s_BufferCapacity - s_BufferSize, s_PendingSize);
                    s_BufferSize += s_PendingSize;
                    s_BufferCount += s_PendingCount;
                    s_PendingSize = s_PendingCount = 0;
                }
                var buffer = new NativeInputEventBuffer
                {
                    eventBuffer = s_Buffer,
                    eventCount = s_BufferCount,
                    sizeInBytes = s_BufferSize,
                    capacityInBytes = s_BufferCapacity,
                };
                NativeInputSystem.NotifyUpdate(updateType, (IntPtr)(&buffer));
                // What the package left (compacted to the front of the buffer, or in its own).
                if (buffer.eventCount > 0 && buffer.sizeInBytes > 0)
                {
                    if (buffer.eventBuffer != s_Buffer)
                    {
                        Reserve(ref s_Buffer, ref s_BufferCapacity, buffer.sizeInBytes);
                        Buffer.MemoryCopy(buffer.eventBuffer, s_Buffer, s_BufferCapacity, buffer.sizeInBytes);
                    }
                    s_BufferCount = buffer.eventCount;
                    s_BufferSize = buffer.sizeInBytes;
                }
                else
                {
                    s_BufferCount = s_BufferSize = 0;
                }
            }
            finally
            {
                s_InUpdate = false;
            }
        }

        /// <summary>An update the player loop runs: only when the package wants it.</summary>
        internal static void RunUpdate(NativeInputUpdateType updateType)
        {
            NativeInputSystem.ShouldRunUpdate(updateType, out bool run);
            if (run) Update(updateType);
        }

        internal static void DynamicUpdate() => RunUpdate(NativeInputUpdateType.Dynamic);
        internal static void FixedUpdate() => RunUpdate(NativeInputUpdateType.Fixed);
        internal static void BeforeRenderUpdate() => RunUpdate(NativeInputUpdateType.BeforeRender);
        /// <summary>The Editor's own input update, after each frame of Play mode.</summary>
        internal static void EditorUpdate() => RunUpdate(NativeInputUpdateType.Editor);

        static void ReportDevices()
        {
            if (s_DevicesReported || !Enabled || !HasDiscoveryCallback) return;
            s_DevicesReported = true;
            NativeInputSystem.NotifyDeviceDiscovered(MouseId, MouseDescription);
            NativeInputSystem.NotifyDeviceDiscovered(KeyboardId, KeyboardDescription);
        }

        // ------------------------------------------------------------------ device commands

        internal static long DeviceCommand(int deviceId, int code, IntPtr data, int sizeInBytes)
        {
            if (!Enabled || deviceId != KeyboardId || data == IntPtr.Zero) return GenericFailure;
            // data points at the command's payload, after its 8-byte header.
            switch (code)
            {
                case KeyboardLayoutCommand:
                    return WriteString((byte*)data, sizeInBytes, KeyboardLayout) ? GenericSuccess : GenericFailure;
                case KeyNameCommand:
                {
                    if (sizeInBytes < 4) return GenericFailure;
                    int key = *(int*)data;
                    if (!KeyNames.TryGet(key, out int scanCode, out string name)) return GenericFailure;
                    *(int*)data = scanCode;
                    return WriteString((byte*)data + 4, sizeInBytes - 4, name) ? GenericSuccess : GenericFailure;
                }
                default:
                    return GenericFailure;
            }
        }

        /// <summary>A string as the package reads it from a command: its length (16 bits), then
        /// UTF-16 characters.</summary>
        static bool WriteString(byte* buffer, int size, string text)
        {
            int length = text?.Length ?? 0;
            if (2 * length + 4 > size) return false;
            *(ushort*)buffer = (ushort)length;
            var chars = (char*)(buffer + 2);
            for (int i = 0; i < length; i++) chars[i] = text[i];
            return true;
        }

        // ------------------------------------------------------------------ the window's input

        /// <summary>Queues the events the native layer would have queued for the frame's window
        /// input: per key change a keyboard state event (and a text event per character the press
        /// types; repeats type without changing the state), per mouse change a mouse state event
        /// (a move, then each button change, then the wheel).</summary>
        internal static void BeginFrame(FrameInput* input)
        {
            if (!Enabled) return;
            // The window's input arrived before the frame began.
            double time = CurrentTime;
            KeyboardEvents(input, time);
            MouseEvents(input, time);
            GamepadEvents(time);
        }

        // ------------------------------------------------------------------ gamepads

        /// <summary>The pads SDL maps, as the Editor reports them: the "SDL" interface, the
        /// mapping's name as the product, udev's vendor name, the USB version in decimal, no
        /// serial nor capabilities.</summary>
        static string PadDescription(Gamepads.Device d) =>
            "{\"interface\":\"SDL\",\"type\":\"Gamepad\",\"product\":" + Json(d.Name) + ",\"manufacturer\":" + Json(d.Manufacturer) +
            ",\"serial\":\"\",\"version\":\"" + d.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}";

        static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s ?? "");

        /// <summary>Pads found since the last update, reported to the package.</summary>
        static void ReportPads()
        {
            if (!Enabled || !HasDiscoveryCallback) return;
            foreach (var d in Gamepads.Attached)
            {
                if (!s_Pads.TryGetValue(d.Id, out var pad) || pad.Reported) continue;
                pad.Reported = true;
                NativeInputSystem.NotifyDeviceDiscovered(pad.DeviceId, PadDescription(d));
            }
        }

        /// <summary>A pad's arrival, removal and each change of its state.</summary>
        static void GamepadEvents(double time)
        {
            foreach (int id in Gamepads.Removed)
            {
                if (!s_Pads.Remove(id, out var pad)) continue;
                if (!pad.Reported) continue;
                byte* e = stackalloc byte[EventHeaderSize];
                Header(e, DeviceRemoveEventType, EventHeaderSize, pad.DeviceId, time);
                QueueEvent((IntPtr)e);
            }
            foreach (var d in Gamepads.Added)
            {
                if (d.IsGamepad)
                {
                    s_Pads[d.Id] = new Pad { DeviceId = AllocateDeviceId() };
                }
                else if (!s_WarnedJoystick)
                {
                    s_WarnedJoystick = true;
                    Debug.LogWarning($"Unbound: the joystick \"{d.Name}\" has no gamepad mapping in Unity's controller database: it is not reported to the Input System (the legacy Input Manager reads it)");
                }
            }
            foreach (var d in Gamepads.Attached)
            {
                if (!s_Pads.TryGetValue(d.Id, out var pad)) continue;
                uint buttons = PadButtons(d.PadButtons);
                // SDL's sticks point down for positive y, the Input System's up.
                var a = d.PadAxes;
                Span<float> values = stackalloc float[6];
                values[0] = a[0] / 32768f;
                values[1] = -(a[1] / 32768f);
                values[2] = a[2] / 32768f;
                values[3] = -(a[3] / 32768f);
                values[4] = a[4] / 32768f;
                values[5] = a[5] / 32768f;
                bool changed = buttons != pad.Buttons;
                for (int i = 0; i < 6; i++)
                {
                    if (values[i] != pad.Values[i]) changed = true;
                    pad.Values[i] = values[i];
                }
                pad.Buttons = buttons;
                if (changed) QueueGamepadState(pad, time);
            }
        }

        /// <summary>SDL's controller buttons as the Input System's gamepad bits.</summary>
        static uint PadButtons(uint sdl)
        {
            // SDL: A B X Y Back Guide Start LeftStick RightStick LeftShoulder RightShoulder Up Down Left Right.
            ReadOnlySpan<sbyte> bits = stackalloc sbyte[] { 6, 5, 7, 4, 13, -1, 12, 8, 9, 10, 11, 0, 1, 2, 3 };
            uint result = 0;
            for (int i = 0; i < bits.Length; i++)
                if ((sdl & (1u << i)) != 0 && bits[i] >= 0) result |= 1u << bits[i];
            return result;
        }

        static void QueueGamepadState(Pad pad, double time)
        {
            const int size = EventHeaderSize + 4 + GamepadStateSize;
            byte* e = stackalloc byte[size];
            Header(e, StateEventType, size, pad.DeviceId, time);
            *(int*)(e + EventHeaderSize) = GamepadFormat;
            *(uint*)(e + EventHeaderSize + 4) = pad.Buttons;
            var values = (float*)(e + EventHeaderSize + 8);
            for (int i = 0; i < 6; i++) values[i] = pad.Values[i];
            QueueEvent((IntPtr)e);
        }

        static void KeyboardEvents(FrameInput* input, double time)
        {
            Span<bool> pressedThisFrame = stackalloc bool[KeyboardStateSize * 8];
            for (int i = 0; i < input->KeyEventCount; i++)
            {
                var e = input->KeyEvents[i];
                int key = KeyMap.ToInputSystemKey(e.Key);
                bool down = key > 0 && (s_Keys[key >> 3] & (1 << (key & 7))) != 0;
                if (e.Pressed != 0)
                {
                    if (key > 0 && !down)
                    {
                        s_Keys[key >> 3] |= (byte)(1 << (key & 7));
                        pressedThisFrame[key] = true;
                        QueueKeyboardState(time);
                    }
                    if (e.TextLength > 0)
                    {
                        var text = Encoding.UTF8.GetString(input->Text + e.TextStart, e.TextLength);
                        for (int c = 0; c < text.Length; c++)
                        {
                            int character = text[c];
                            if (char.IsHighSurrogate(text[c]) && c + 1 < text.Length)
                                character = char.ConvertToUtf32(text[c], text[++c]);
                            QueueText(character, time);
                        }
                    }
                    else if (KeyMap.ControlCharacter(e.Key) is int control)
                    {
                        QueueText(control, time);
                    }
                }
                else if (down)
                {
                    s_Keys[key >> 3] &= (byte)~(1 << (key & 7));
                    QueueKeyboardState(time);
                }
            }
            // A key the window no longer holds whose release went unreported (the window lost
            // the focus) is released too.
            if (input->Keys == null) return;
            bool released = false;
            for (int code = 0; code < 512; code++)
            {
                if ((input->Keys[code >> 3] & (1 << (code & 7))) != 0) continue;
                int key = KeyMap.ToInputSystemKey(code);
                if (key <= 0 || pressedThisFrame[key] || (s_Keys[key >> 3] & (1 << (key & 7))) == 0) continue;
                s_Keys[key >> 3] &= (byte)~(1 << (key & 7));
                released = true;
            }
            if (released) QueueKeyboardState(time);
        }

        static void QueueKeyboardState(double time)
        {
            const int size = EventHeaderSize + 4 + KeyboardStateSize;
            byte* e = stackalloc byte[Align(size)];
            Header(e, StateEventType, size, KeyboardId, time);
            *(int*)(e + EventHeaderSize) = KeyboardFormat;
            for (int i = 0; i < KeyboardStateSize; i++) e[EventHeaderSize + 4 + i] = s_Keys[i];
            QueueEvent((IntPtr)e);
        }

        static void QueueText(int character, double time)
        {
            const int size = EventHeaderSize + 4;
            byte* e = stackalloc byte[size];
            Header(e, TextEventType, size, KeyboardId, time);
            *(int*)(e + EventHeaderSize) = character;
            QueueEvent((IntPtr)e);
        }

        static void MouseEvents(FrameInput* input, double time)
        {
            var window = new Vector2(input->MouseX, input->MouseY);
            // The first frame tells where the pointer is, not that it moved: a pointer that never
            // moves sends nothing, as the Editor's does.
            if (!s_HaveWindowMouse)
            {
                s_HaveWindowMouse = true;
                s_WindowMouse = window;
            }
            if (window != s_WindowMouse)
            {
                s_WindowMouse = window;
                // Unity's screen space: from the bottom left.
                var position = new Vector2(window.x, input->ScreenHeight - window.y);
                var delta = position - s_MousePosition;
                s_MousePosition = position;
                QueueMouseState(delta, Vector2.zero, time);
            }
            ushort buttons = MouseButtons(input->MouseButtons);
            for (int bit = 0; bit < 5; bit++)
            {
                ushort mask = (ushort)(1 << bit);
                if ((buttons & mask) == (s_MouseButtons & mask)) continue;
                s_MouseButtons = (ushort)((s_MouseButtons & ~mask) | (buttons & mask));
                QueueMouseState(Vector2.zero, Vector2.zero, time);
            }
            if (input->ScrollX != 0f || input->ScrollY != 0f)
                QueueMouseState(Vector2.zero, new Vector2(input->ScrollX, input->ScrollY) * ScrollWheelDeltaPerTick, time);
        }

        /// <summary>The window's button bits (left, right, middle, back, forward) as the Input
        /// System's (left, right, middle, forward, back).</summary>
        static ushort MouseButtons(uint window)
        {
            uint bits = window & 0x7u;
            if ((window & 0x8u) != 0) bits |= 1u << 4;
            if ((window & 0x10u) != 0) bits |= 1u << 3;
            return (ushort)bits;
        }

        static void QueueMouseState(Vector2 delta, Vector2 scroll, double time)
        {
            const int size = EventHeaderSize + 4 + MouseStateSize;
            byte* e = stackalloc byte[size];
            new Span<byte>(e, size).Clear();
            Header(e, StateEventType, size, MouseId, time);
            *(int*)(e + EventHeaderSize) = MouseFormat;
            var state = (float*)(e + EventHeaderSize + 4);
            state[0] = s_MousePosition.x;
            state[1] = s_MousePosition.y;
            state[2] = delta.x;
            state[3] = delta.y;
            state[4] = scroll.x;
            state[5] = scroll.y;
            *(ushort*)(e + EventHeaderSize + 4 + 24) = s_MouseButtons;
            QueueEvent((IntPtr)e);
        }

        static void Header(byte* e, int type, int size, int deviceId, double time)
        {
            var header = (NativeInputEvent*)e;
            header->type = (NativeInputEventType)type;
            header->sizeInBytes = (ushort)size;
            header->deviceId = (ushort)deviceId;
            header->time = time;
            header->eventId = 0;
        }

        /// <summary>The package's <c>Key</c> values of Unity's <c>KeyCode</c>s, and the text the
        /// native layer sends for keys that type control characters.</summary>
        static class KeyMap
        {
            static readonly byte[] s_Keys = Build();

            static byte[] Build()
            {
                var map = new byte[512];
                void Set(KeyCode code, int key) => map[(int)code] = (byte)key;
                Set(KeyCode.Space, 1); Set(KeyCode.Return, 2); Set(KeyCode.Tab, 3); Set(KeyCode.BackQuote, 4);
                Set(KeyCode.Quote, 5); Set(KeyCode.Semicolon, 6); Set(KeyCode.Comma, 7); Set(KeyCode.Period, 8);
                Set(KeyCode.Slash, 9); Set(KeyCode.Backslash, 10); Set(KeyCode.LeftBracket, 11); Set(KeyCode.RightBracket, 12);
                Set(KeyCode.Minus, 13); Set(KeyCode.Equals, 14);
                for (int i = 0; i < 26; i++) Set(KeyCode.A + i, 15 + i);
                for (int i = 1; i <= 9; i++) Set(KeyCode.Alpha0 + i, 40 + i);
                Set(KeyCode.Alpha0, 50);
                Set(KeyCode.LeftShift, 51); Set(KeyCode.RightShift, 52); Set(KeyCode.LeftAlt, 53); Set(KeyCode.RightAlt, 54);
                Set(KeyCode.LeftControl, 55); Set(KeyCode.RightControl, 56); Set(KeyCode.LeftMeta, 57); Set(KeyCode.RightMeta, 58);
                Set(KeyCode.Menu, 59); Set(KeyCode.Escape, 60); Set(KeyCode.LeftArrow, 61); Set(KeyCode.RightArrow, 62);
                Set(KeyCode.UpArrow, 63); Set(KeyCode.DownArrow, 64); Set(KeyCode.Backspace, 65); Set(KeyCode.PageDown, 66);
                Set(KeyCode.PageUp, 67); Set(KeyCode.Home, 68); Set(KeyCode.End, 69); Set(KeyCode.Insert, 70);
                Set(KeyCode.Delete, 71); Set(KeyCode.CapsLock, 72); Set(KeyCode.Numlock, 73); Set(KeyCode.Print, 74);
                Set(KeyCode.ScrollLock, 75); Set(KeyCode.Pause, 76); Set(KeyCode.KeypadEnter, 77); Set(KeyCode.KeypadDivide, 78);
                Set(KeyCode.KeypadMultiply, 79); Set(KeyCode.KeypadPlus, 80); Set(KeyCode.KeypadMinus, 81); Set(KeyCode.KeypadPeriod, 82);
                Set(KeyCode.KeypadEquals, 83);
                for (int i = 0; i <= 9; i++) Set(KeyCode.Keypad0 + i, 84 + i);
                for (int i = 0; i < 12; i++) Set(KeyCode.F1 + i, 94 + i);
                for (int i = 0; i < 3; i++) Set(KeyCode.F13 + i, 112 + i);
                return map;
            }

            internal static int ToInputSystemKey(int keyCode) => keyCode > 0 && keyCode < 512 ? s_Keys[keyCode] : 0;

            internal static int? ControlCharacter(int keyCode) => (KeyCode)keyCode switch
            {
                KeyCode.Return or KeyCode.KeypadEnter => '\r',
                KeyCode.Escape => 0x1b,
                KeyCode.Backspace => 0x08,
                KeyCode.Tab => '\t',
                KeyCode.Delete => 0x7f,
                _ => null,
            };
        }

        /// <summary>What the Linux Editor answers to <c>QueryKeyNameCommand</c> on a US layout: per
        /// key its scan code and the name it types or is labelled with (measured).</summary>
        static class KeyNames
        {
            static readonly (int Scan, string Name)[] s_Names = Build();

            static (int, string)[] Build()
            {
                var names = new (int, string)[127];
                for (int i = 0; i < names.Length; i++) names[i] = (-1, null);
                void Set(int key, string name, int scan = -1) => names[key] = (scan < 0 ? key : scan, name);
                Set(1, " "); Set(2, "\r"); Set(3, "\t"); Set(4, "`"); Set(5, "'"); Set(6, ";"); Set(7, ","); Set(8, ".");
                Set(9, "/"); Set(10, "\\"); Set(11, "["); Set(12, "]"); Set(13, "-"); Set(14, "=");
                for (int i = 0; i < 26; i++) Set(15 + i, ((char)('A' + i)).ToString());
                for (int i = 1; i <= 9; i++) Set(40 + i, i.ToString());
                Set(50, "0");
                Set(51, "Left Shift"); Set(52, "Right Shift"); Set(53, "Left Alt"); Set(54, "Right Alt");
                Set(55, "Left Control"); Set(56, "Right Control"); Set(57, "Left System"); Set(58, "Right System");
                Set(59, "Context Menu"); Set(60, "\u001b"); Set(61, "Left Arrow"); Set(62, "Right Arrow");
                Set(63, "Up Arrow"); Set(64, "Down Arrow"); Set(65, "\b"); Set(66, "Page Down"); Set(67, "Page Up");
                Set(68, "Home"); Set(69, "End"); Set(70, "Insert"); Set(71, "\u007f"); Set(72, "Caps Lock");
                Set(73, "Num Lock"); Set(74, "Print Screen"); Set(75, "Scroll Lock"); Set(76, "Pause/Break");
                Set(77, "Numpad Enter"); Set(78, "/"); Set(79, "*"); Set(80, "+"); Set(81, "-"); Set(82, "Numpad .");
                Set(83, "=");
                for (int i = 0; i <= 9; i++) Set(84 + i, "Numpad " + i);
                for (int i = 1; i <= 12; i++) Set(93 + i, "F" + i);
                // The ISO key between left shift and Z; the next has a scan code but no name.
                Set(106, "<"); Set(107, "");
                for (int i = 13; i <= 24; i++) Set(99 + i, "F" + i);
                // F19 and F24 answer with their X keysyms.
                names[118] = (0xFFD0, "F19");
                names[123] = (0xFFD5, "F24");
                return names;
            }

            internal static bool TryGet(int key, out int scanCode, out string name)
            {
                scanCode = 0;
                name = null;
                if (key <= 0 || key >= s_Names.Length || s_Names[key].Name == null) return false;
                (scanCode, name) = s_Names[key];
                return true;
            }
        }
    }
}
