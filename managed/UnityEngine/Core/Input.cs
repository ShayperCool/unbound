using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The window as the game sees it, like the Editor's Game view.</summary>
    public sealed partial class Screen
    {
        static int s_Width = 1080;
        static int s_Height = 1920;
        static bool s_SizeChanged;

        internal static void Update(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            if (width != s_Width || height != s_Height) s_SizeChanged = true;
            s_Width = width;
            s_Height = height;
        }

        internal static bool ConsumeSizeChanged()
        {
            bool changed = s_SizeChanged;
            s_SizeChanged = false;
            return changed;
        }

        public static int width => s_Width;
        public static int height => s_Height;
        public static float dpi { get; internal set; } = 96f;
        public static Resolution currentResolution => new Resolution { width = s_Width, height = s_Height, refreshRateRatio = new RefreshRate { numerator = 60, denominator = 1 } };
        public static Resolution[] resolutions => new[] { currentResolution };
        public static bool fullScreen { get; set; }
        public static FullScreenMode fullScreenMode { get; set; } = FullScreenMode.Windowed;
        /// <summary>The Editor's answer: Portrait whatever the window's shape, and setting it
        /// changes nothing (only a device rotates the screen).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/platform (Portrait, setting it changes nothing)")]
        public static ScreenOrientation orientation
        {
            get => ScreenOrientation.Portrait;
            set { }
        }
        public static bool autorotateToPortrait { get; set; } = true;
        public static bool autorotateToPortraitUpsideDown { get; set; } = true;
        public static bool autorotateToLandscapeLeft { get; set; } = true;
        public static bool autorotateToLandscapeRight { get; set; } = true;
        public static int sleepTimeout { get; set; } = SleepTimeout.SystemSetting;
        public static float brightness { get; set; } = 1f;
        public static Rect safeArea => new Rect(0, 0, s_Width, s_Height);
        [Unbound.EditorFaithful("measured: tests/projects/platform (no cutouts)")]
        public static Rect[] cutouts => Array.Empty<Rect>();
        [Unbound.EditorFaithful("the Editor's window has no multisampled back buffer (the Game view renders to a texture)")]
        public static int msaaSamples => 1;
        [Unbound.EditorFaithful("a player-window API: the Editor answers zero")]
        public static Vector2Int mainWindowPosition => Vector2Int.zero;
        public static DisplayInfo mainWindowDisplayInfo => new DisplayInfo { width = s_Width, height = s_Height, name = "Unbound", refreshRate = new RefreshRate { numerator = 60, denominator = 1 } };

        [Obsolete("Property lockCursor has been deprecated. Use Cursor.lockState and Cursor.visible instead.")]
        public static bool lockCursor
        {
            get => Cursor.lockState == CursorLockMode.Locked;
            set => Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
        }

        public static void SetResolution(int width, int height, FullScreenMode fullscreenMode, RefreshRate preferredRefreshRate) => SetResolution(width, height, fullscreenMode);

        public static void SetResolution(int width, int height, FullScreenMode fullscreenMode, [Internal.DefaultValue("0")] int preferredRefreshRate) => SetResolution(width, height, fullscreenMode);

        public static void SetResolution(int width, int height, FullScreenMode fullscreenMode)
        {
            fullScreenMode = fullscreenMode;
            Native.Log((int)LogType.Log, $"[Unbound] Screen.SetResolution({width}x{height}) ignored: the window keeps its size");
        }

        public static void SetResolution(int width, int height, bool fullscreen, [Internal.DefaultValue("0")] int preferredRefreshRate) => SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

        public static void SetResolution(int width, int height, bool fullscreen) => SetResolution(width, height, fullscreen, 0);

        public static void GetDisplayLayout(List<DisplayInfo> displayLayout)
        {
            displayLayout.Clear();
            displayLayout.Add(mainWindowDisplayInfo);
        }

        public static AsyncOperation MoveMainWindowTo(in DisplayInfo display, Vector2Int position) => AsyncOperation.Completed();
    }

    public partial struct Resolution
    {
        int m_Width;
        int m_Height;
        RefreshRate m_RefreshRate;

        public int width { get => m_Width; set => m_Width = value; }
        public int height { get => m_Height; set => m_Height = value; }
        public RefreshRate refreshRateRatio { get => m_RefreshRate; set => m_RefreshRate = value; }

        [Obsolete("Resolution.refreshRate is obsolete. Use refreshRateRatio instead.", false)]
        public int refreshRate
        {
            get => (int)Math.Round(m_RefreshRate.value);
            set => m_RefreshRate = new RefreshRate { numerator = (uint)value, denominator = 1 };
        }

        public override string ToString() => $"{m_Width} x {m_Height} @ {m_RefreshRate}Hz";
    }

    public partial struct RefreshRate : IEquatable<RefreshRate>, IComparable<RefreshRate>
    {
        public uint numerator;
        public uint denominator;
        public double value => denominator == 0 ? 0 : (double)numerator / denominator;
        public bool Equals(RefreshRate other) => value == other.value;
        public int CompareTo(RefreshRate other) => value.CompareTo(other.value);
        public override string ToString() => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public partial struct DisplayInfo : IEquatable<DisplayInfo>
    {
        internal ulong handle;
        public int width;
        public int height;
        public RefreshRate refreshRate;
        public RectInt workArea;
        public string name;
        public bool Equals(DisplayInfo other) => handle == other.handle && width == other.width && height == other.height && name == other.name;
    }

    public sealed partial class SleepTimeout
    {
        public const int NeverSleep = -1;
        public const int SystemSetting = -2;
    }

    /// <summary>The window's cursor: locked (the window holds it; the mouse's raw motion drives
    /// the deltas), confined, hidden, or an image. In the Editor the values read back as set and,
    /// while locked, Input.mousePosition follows the pointer (measured: tests/projects/messages).</summary>
    public partial class Cursor
    {
        static bool s_Visible = true;
        static CursorLockMode s_LockState;

        public static bool visible
        {
            get => s_Visible;
            set
            {
                if (s_Visible == value) return;
                s_Visible = value;
                Send(null, false);
            }
        }

        public static CursorLockMode lockState
        {
            get => s_LockState;
            set
            {
                if (s_LockState == value) return;
                s_LockState = value;
                Send(null, false);
            }
        }

        /// <summary>The cursor's image (null: the system's arrow), its hotspot in pixels from the
        /// top left. The texture's pixels must be readable, as Unity asks.</summary>
        public static void SetCursor(Texture2D texture, Vector2 hotspot, CursorMode cursorMode)
        {
            if (texture == null)
            {
                Send("null", true);
                return;
            }
            Color32[] pixels;
            try
            {
                pixels = texture.GetPixels32();
            }
            catch (Exception e)
            {
                Debug.LogError($"Cursor.SetCursor: the texture '{texture.name}' has no readable pixels ({e.Message})");
                return;
            }
            int width = texture.width, height = texture.height;
            if (pixels == null || pixels.Length < width * height || width <= 0 || height <= 0) return;
            var rgba = new System.Text.StringBuilder(width * height * 16);
            // Texture rows go up from the bottom; a cursor's go down from the top.
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = pixels[y * width + x];
                    if (rgba.Length > 0) rgba.Append(',');
                    rgba.Append(p.r).Append(',').Append(p.g).Append(',').Append(p.b).Append(',').Append(p.a);
                }
            }
            int hx = Mathf.Clamp(Mathf.RoundToInt(hotspot.x), 0, width - 1);
            int hy = Mathf.Clamp(Mathf.RoundToInt(hotspot.y), 0, height - 1);
            Send($"{{\"width\":{width},\"height\":{height},\"rgba\":[{rgba}],\"hotspot\":[{hx},{hy}]}}", true);
        }

        /// <summary>Hands the cursor to the window (the native side applies it after the frame).</summary>
        static void Send(string image, bool withImage)
        {
            var json = $"{{\"lock\":{(int)s_LockState},\"visible\":{(s_Visible ? "true" : "false")}" + (withImage ? $",\"image\":{image}" : "") + "}";
            try { Native.Call("cursor", json); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public partial struct Touch
    {
        int m_FingerId;
        Vector2 m_Position;
        Vector2 m_RawPosition;
        Vector2 m_PositionDelta;
        float m_TimeDelta;
        int m_TapCount;
        TouchPhase m_Phase;
        TouchType m_Type;
        float m_Pressure;
        float m_maximumPossiblePressure;
        float m_Radius;
        float m_RadiusVariance;
        float m_AltitudeAngle;
        float m_AzimuthAngle;

        public int fingerId { get => m_FingerId; set => m_FingerId = value; }
        public Vector2 position { get => m_Position; set => m_Position = value; }
        public Vector2 rawPosition { get => m_RawPosition; set => m_RawPosition = value; }
        public Vector2 deltaPosition { get => m_PositionDelta; set => m_PositionDelta = value; }
        public float deltaTime { get => m_TimeDelta; set => m_TimeDelta = value; }
        public int tapCount { get => m_TapCount; set => m_TapCount = value; }
        public TouchPhase phase { get => m_Phase; set => m_Phase = value; }
        public float pressure { get => m_Pressure; set => m_Pressure = value; }
        public float maximumPossiblePressure { get => m_maximumPossiblePressure; set => m_maximumPossiblePressure = value; }
        public TouchType type { get => m_Type; set => m_Type = value; }
        public float altitudeAngle { get => m_AltitudeAngle; set => m_AltitudeAngle = value; }
        public float azimuthAngle { get => m_AzimuthAngle; set => m_AzimuthAngle = value; }
        public float radius { get => m_Radius; set => m_Radius = value; }
        public float radiusVariance { get => m_RadiusVariance; set => m_RadiusVariance = value; }
    }

    /// <summary>The legacy Input Manager, fed by the native window once per frame.</summary>
    public partial class Input
    {
        const int KeyCount = 512;
        static readonly bool[] s_Keys = new bool[KeyCount];
        static readonly bool[] s_KeysDown = new bool[KeyCount];
        static readonly bool[] s_KeysUp = new bool[KeyCount];
        internal static Vector3 s_MousePosition;
        static Vector3 s_PreviousMouse;
        static Vector2 s_MouseDelta;
        static Vector2 s_Scroll;
        static string s_InputString = "";
        static bool s_AnyKeyDown;
        // Each name's entries: Unity reads them all (a key entry and a joystick entry of
        // "Horizontal" both drive it).
        static readonly Dictionary<string, List<AxisState>> s_Axes = new Dictionary<string, List<AxisState>>();
        static readonly List<Touch> s_Touches = new List<Touch>();
        static Vector2 s_TouchStart;
        static bool s_SimulatedTouchDown;

        /// <summary>Simulates a touch with the left mouse button, for mobile games tested on a desktop.</summary>
        internal static bool SimulateTouchWithMouse;

        /// <summary>Player Settings hand input to the Input System package alone: the members that
        /// read the Input Manager's input refuse, as Unity's do (tests/projects/input).</summary>
        internal static bool s_InputManagerDisabled;

        /// <summary>Whether Player Settings turned the Input Manager off (SendMouseEvents then
        /// reads no mouse).</summary>
        internal static bool CheckDisabled() => s_InputManagerDisabled;

        /// <summary>Whether the window holds a key, for the engine's own use (never refuses).</summary>
        internal static bool KeyHeld(KeyCode key) => (int)key >= 0 && (int)key < KeyCount && s_Keys[(int)key];

        static void CheckEnabled()
        {
            if (s_InputManagerDisabled)
                throw new InvalidOperationException("You are trying to read Input using the UnityEngine.Input class, but you have switched active Input handling to Input System package in Player Settings.");
        }

        sealed class AxisState
        {
            internal InputAxis Config;
            internal float Value;
        }

        /// <summary>An axis of ProjectSettings/InputManager.asset.</summary>
        internal sealed class InputAxis
        {
            internal string Name;
            internal string Negative, Positive, AltNegative, AltPositive;
            internal float Gravity, Dead, Sensitivity;
            internal bool Snap, Invert;
            internal int Type; // 0 key or mouse button, 1 mouse movement, 2 joystick axis
            internal int Axis;
            internal int JoyNum; // 0 any joystick, else the joystick's number
        }

        internal static void ConfigureAxes(IEnumerable<InputAxis> axes)
        {
            s_Axes.Clear();
            foreach (var axis in axes)
            {
                if (!s_Axes.TryGetValue(axis.Name, out var entries))
                    s_Axes[axis.Name] = entries = new List<AxisState>();
                entries.Add(new AxisState { Config = axis });
            }
        }

        // The keys and buttons held after the frame's events (GetKey also counts those pressed
        // during the frame).
        static readonly bool[] s_Held = new bool[KeyCount];

        /// <summary>Takes the frame's input. Every press and release since the last frame counts,
        /// in order (measured, tests/projects/inputframes): a key pressed and released between two
        /// frames is down, up and held for that frame; one released and pressed again is down, up
        /// and held; FixedUpdate sees the frame's state.</summary>
        internal static unsafe void BeginFrame(FrameInput* input)
        {
            s_AnyKeyDown = false;
            Array.Clear(s_KeysDown);
            Array.Clear(s_KeysUp);
            for (int i = 0; i < input->KeyEventCount; i++)
            {
                var e = input->KeyEvents[i];
                if (e.Key <= 0 || e.Key >= KeyCount) continue;
                Change(e.Key, e.Pressed != 0);
            }
            // What changed without an event (taps, joysticks, a release the window never saw when
            // it lost the focus) changes now.
            for (int i = 0; i < KeyCount; i++)
            {
                bool down = input->Keys != null && (input->Keys[i >> 3] & (1 << (i & 7))) != 0;
                if (i >= (int)KeyCode.Mouse0 && i <= (int)KeyCode.Mouse6)
                    down = (input->MouseButtons & (1u << (i - (int)KeyCode.Mouse0))) != 0;
                else if (i >= (int)KeyCode.JoystickButton0 && i <= (int)KeyCode.Joystick8Button19)
                    down = JoystickKey(i);
                if (down != s_Held[i]) Change(i, down);
                s_Keys[i] = s_Held[i] || s_KeysDown[i];
                if (s_KeysDown[i]) s_AnyKeyDown = true;
            }
            s_PreviousMouse = s_MousePosition;
            // The native side reports pixels from the top-left; Unity counts from the bottom-left.
            s_MousePosition = new Vector3(input->MouseX, Screen.height - input->MouseY, 0f);
            s_MouseDelta = Time.frameCount <= 1 ? Vector2.zero : (Vector2)(s_MousePosition - s_PreviousMouse);
            // A locked cursor stays put (or at the window's edge): the mouse's raw motion moves it.
            if (Cursor.lockState == CursorLockMode.Locked && input->RawMotion != 0)
                s_MouseDelta = new Vector2(input->RawMotionX, -input->RawMotionY);
            s_Scroll = new Vector2(input->ScrollX, input->ScrollY);
            if (Trace.InputOn && s_KeysDown[(int)KeyCode.Mouse0]) Trace.Pointer(s_MousePosition);
            // Return types "\r" (measured).
            s_InputString = input->TextLength > 0 ? System.Text.Encoding.UTF8.GetString(input->Text, input->TextLength) : "";
            UpdateAxes();
            UpdateSimulatedTouch();
            GuiEvents.BeginFrame(input);
        }

        internal static void EndFrame() { }

        static void Change(int key, bool down)
        {
            if (down == s_Held[key]) return;
            s_Held[key] = down;
            if (down) s_KeysDown[key] = true;
            else s_KeysUp[key] = true;
        }

        /// <summary>A joystick button's key: "joystick button N" is any joystick's button N,
        /// "joystick J button N" joystick J's (SDL's button order, measured).</summary>
        static bool JoystickKey(int code)
        {
            int index = code - (int)KeyCode.JoystickButton0;
            if (index < 20)
            {
                for (int j = 1; j <= Gamepads.Attached.Count; j++)
                    if (Gamepads.Button(j, index)) return true;
                return false;
            }
            index -= 20;
            return Gamepads.Button(1 + index / 20, index % 20);
        }

        static void UpdateSimulatedTouch()
        {
            s_Touches.Clear();
            if (!SimulateTouchWithMouse) return;
            // A touch begins with a press and ends with the next release: a click within a frame
            // begins it and the next frame ends it; a button released and pressed again within a
            // frame ends it and the next frame begins another.
            bool pressed = !s_SimulatedTouchDown && (s_KeysDown[(int)KeyCode.Mouse0] || s_Held[(int)KeyCode.Mouse0]);
            bool released = s_SimulatedTouchDown && (s_KeysUp[(int)KeyCode.Mouse0] || !s_Held[(int)KeyCode.Mouse0]);
            if (!pressed && !s_SimulatedTouchDown) return;
            bool down = !released;
            if (pressed) s_TouchStart = s_MousePosition;
            var touch = new Touch
            {
                fingerId = 0,
                position = s_MousePosition,
                rawPosition = s_TouchStart,
                deltaPosition = pressed ? Vector2.zero : s_MouseDelta,
                deltaTime = Time.unscaledDeltaTime,
                tapCount = 1,
                phase = pressed ? TouchPhase.Began : released ? TouchPhase.Ended : s_MouseDelta.sqrMagnitude > 0f ? TouchPhase.Moved : TouchPhase.Stationary,
                type = TouchType.Direct,
                pressure = 1f,
                maximumPossiblePressure = 1f,
            };
            s_SimulatedTouchDown = down;
            s_Touches.Add(touch);
        }

        static void UpdateAxes()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var entries in s_Axes.Values)
            foreach (var state in entries)
            {
                var a = state.Config;
                if (a.Type == 2)
                {
                    // The dead zone is cut out and the rest stretched over the range (measured).
                    float v = Gamepads.Axis(a.JoyNum, a.Axis);
                    float magnitude = Mathf.Abs(v);
                    v = magnitude <= a.Dead || a.Dead >= 1f ? 0f : Mathf.Sign(v) * (magnitude - a.Dead) / (1f - a.Dead);
                    state.Value = v * a.Sensitivity * (a.Invert ? -1f : 1f);
                    continue;
                }
                if (a.Type == 1)
                {
                    float delta = a.Axis == 0 ? s_MouseDelta.x : a.Axis == 1 ? s_MouseDelta.y : s_Scroll.y;
                    state.Value = delta * a.Sensitivity * (a.Invert ? -1f : 1f);
                    continue;
                }
                if (a.Type != 0) { state.Value = 0f; continue; }
                bool positive = KeyByName(a.Positive) || KeyByName(a.AltPositive);
                bool negative = KeyByName(a.Negative) || KeyByName(a.AltNegative);
                float target = (positive ? 1f : 0f) - (negative ? 1f : 0f);
                if (a.Invert) target = -target;
                if (target != 0f)
                {
                    if (a.Snap && Mathf.Sign(target) != Mathf.Sign(state.Value)) state.Value = 0f;
                    state.Value = Mathf.MoveTowards(state.Value, target, a.Sensitivity * dt);
                }
                else
                {
                    state.Value = Mathf.MoveTowards(state.Value, 0f, a.Gravity * dt);
                }
            }
        }

        static bool KeyByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var code = KeyNames.Parse(name);
            return code != KeyCode.None && s_Keys[(int)code];
        }

        public static bool GetKey(KeyCode key) { CheckEnabled(); return (int)key >= 0 && (int)key < KeyCount && s_Keys[(int)key]; }
        public static bool GetKeyDown(KeyCode key) { CheckEnabled(); return (int)key >= 0 && (int)key < KeyCount && s_KeysDown[(int)key]; }
        public static bool GetKeyUp(KeyCode key) { CheckEnabled(); return (int)key >= 0 && (int)key < KeyCount && s_KeysUp[(int)key]; }
        public static bool GetKey(string name) => GetKey(ParseKey(name));
        public static bool GetKeyDown(string name) => GetKeyDown(ParseKey(name));
        public static bool GetKeyUp(string name) => GetKeyUp(ParseKey(name));

        static KeyCode ParseKey(string name)
        {
            var code = KeyNames.Parse(name);
            if (code == KeyCode.None) throw new ArgumentException($"Input Key named: {name} is unknown");
            return code;
        }

        public static bool GetMouseButton(int button) { CheckEnabled(); return button >= 0 && button < 7 && s_Keys[(int)KeyCode.Mouse0 + button]; }
        public static bool GetMouseButtonDown(int button) { CheckEnabled(); return button >= 0 && button < 7 && s_KeysDown[(int)KeyCode.Mouse0 + button]; }
        public static bool GetMouseButtonUp(int button) { CheckEnabled(); return button >= 0 && button < 7 && s_KeysUp[(int)KeyCode.Mouse0 + button]; }

        /// <summary>An axis's value: of its entries, the one pushed furthest.</summary>
        public static float GetAxis(string axisName)
        {
            float value = 0f;
            foreach (var state in Entries(axisName, "Axis"))
            {
                float v = state.Config.Type == 0 && Mathf.Abs(state.Value) < state.Config.Dead ? 0f : state.Value;
                if (Mathf.Abs(v) > Mathf.Abs(value)) value = v;
            }
            return value;
        }

        public static float GetAxisRaw(string axisName)
        {
            float value = 0f;
            foreach (var state in Entries(axisName, "Axis"))
            {
                var a = state.Config;
                float v;
                if (a.Type != 0)
                {
                    v = state.Value;
                }
                else
                {
                    bool positive = KeyByName(a.Positive) || KeyByName(a.AltPositive);
                    bool negative = KeyByName(a.Negative) || KeyByName(a.AltNegative);
                    v = (positive ? 1f : 0f) - (negative ? 1f : 0f);
                    if (a.Invert) v = -v;
                }
                if (Mathf.Abs(v) > Mathf.Abs(value)) value = v;
            }
            return value;
        }

        static List<AxisState> Entries(string name, string kind)
        {
            CheckEnabled();
            if (!s_Axes.TryGetValue(name, out var entries))
                throw new ArgumentException($"Input {kind} {name} is not setup.\n To change the input settings use: {Compat.InputSettingsMenu}");
            return entries;
        }

        public static bool GetButton(string buttonName) => ButtonState(buttonName, s_Keys);
        public static bool GetButtonDown(string buttonName) => ButtonState(buttonName, s_KeysDown);
        public static bool GetButtonUp(string buttonName) => ButtonState(buttonName, s_KeysUp);

        static bool ButtonState(string name, bool[] keys)
        {
            foreach (var state in Entries(name, "Button"))
            {
                var a = state.Config;
                foreach (var key in new[] { a.Positive, a.AltPositive, a.Negative, a.AltNegative })
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    var code = KeyNames.Parse(key);
                    if (code != KeyCode.None && keys[(int)code]) return true;
                }
            }
            return false;
        }

        public static void ResetInputAxes()
        {
            foreach (var entries in s_Axes.Values)
                foreach (var state in entries) state.Value = 0f;
        }

        public static string[] GetJoystickNames()
        {
            CheckEnabled();
            var names = new string[Gamepads.Attached.Count];
            for (int i = 0; i < names.Length; i++) names[i] = Gamepads.Attached[i].Name;
            return names;
        }

        public static bool anyKey
        {
            get
            {
                CheckEnabled();
                foreach (var k in s_Keys) if (k) return true;
                return false;
            }
        }

        public static bool anyKeyDown { get { CheckEnabled(); return s_AnyKeyDown; } }
        public static string inputString { get { CheckEnabled(); return s_InputString; } }
        public static Vector3 mousePosition { get { CheckEnabled(); return s_MousePosition; } }
        public static Vector3 mousePositionDelta { get { CheckEnabled(); return s_MouseDelta; } }
        public static Vector2 mouseScrollDelta { get { CheckEnabled(); return s_Scroll; } }
        [Unbound.EditorFaithful("measured: tests/projects/gamepad (the Editor always has a mouse)")]
        public static bool mousePresent => true;
        public static bool simulateMouseWithTouches { get; set; } = true;
        public static bool touchSupported => SimulateTouchWithMouse;
        public static bool multiTouchEnabled { get; set; } = true;
        [Unbound.EditorFaithful("measured: tests/projects/gamepad")]
        public static bool touchPressureSupported => false;
        [Unbound.EditorFaithful("measured: tests/projects/gamepad")]
        public static bool stylusTouchSupported => false;
        public static int touchCount => s_Touches.Count;
        public static Touch[] touches => s_Touches.ToArray();

        public static Touch GetTouch(int index)
        {
            if (index < 0 || index >= s_Touches.Count)
                throw new ArgumentException("Index out of bounds.");
            return s_Touches[index];
        }

        [Unbound.EditorFaithful("the desktop Editor has no gyroscope; measured: tests/projects/platform")]
        public static bool isGyroAvailable => false;
        [Unbound.EditorFaithful("the desktop Editor has no accelerometer; measured: tests/projects/platform")]
        public static Vector3 acceleration => Vector3.zero;
        [Unbound.EditorFaithful("the desktop Editor has no accelerometer")]
        public static int accelerationEventCount => 0;
        [Unbound.EditorFaithful("the desktop Editor has no accelerometer")]
        public static AccelerationEvent[] accelerationEvents => Array.Empty<AccelerationEvent>();
        public static AccelerationEvent GetAccelerationEvent(int index) => throw new ArgumentException("Index out of bounds.");
        public static DeviceOrientation deviceOrientation => DeviceOrientation.Unknown;
        public static IMECompositionMode imeCompositionMode { get; set; }
        public static string compositionString
        {
            get
            {
                Stubs.Ignored("Input.compositionString", "is always empty (IME composition is not read yet)");
                return CompositionString;
            }
        }
        /// <summary>The IME composition for the engine's own polling (InputForUI): no warning.</summary>
        internal static string CompositionString => "";
        public static bool imeIsSelected
        {
            get
            {
                Stubs.Ignored("Input.imeIsSelected", "is always false (IME state is not read yet)");
                return false;
            }
        }
        public static Vector2 compositionCursorPos { get; set; }
        public static bool backButtonLeavesApp { get; set; }
        public static bool compensateSensors { get; set; } = true;
        public static LocationService location { get; } = new LocationService();
        public static Compass compass { get; } = new Compass();
        public static Gyroscope gyro { get; } = new Gyroscope(0);

        [Obsolete("eatKeyPressOnTextFieldFocus property is deprecated, and only provided to support legacy behavior.")]
        public static bool eatKeyPressOnTextFieldFocus { get; set; }
    }

    public partial struct AccelerationEvent
    {
        internal float x, y, z;
        internal float m_TimeDelta;
        public Vector3 acceleration => new Vector3(x, y, z);
        public float deltaTime => m_TimeDelta;
    }

    [Unbound.EditorFaithful("the desktop Editor has no location service: measured: tests/projects/platform")]
    public partial class LocationService
    {
        public bool isEnabledByUser => false;
        public LocationServiceStatus status => LocationServiceStatus.Stopped;
        public LocationInfo lastData => default;
        public void Start() { }
        public void Start(float desiredAccuracyInMeters) { }
        public void Start(float desiredAccuracyInMeters, float updateDistanceInMeters) { }
        public void Stop() { }
    }

    [Unbound.EditorFaithful("only ever the empty LocationService.lastData of the desktop Editor")]
    public partial struct LocationInfo
    {
        public float latitude => 0f;
        public float longitude => 0f;
        public float altitude => 0f;
        public float horizontalAccuracy => 0f;
        public float verticalAccuracy => 0f;
        public double timestamp => 0;
    }

    public enum LocationServiceStatus
    {
        Stopped = 0,
        Initializing = 1,
        Running = 2,
        Failed = 3,
    }

    [Unbound.EditorFaithful("the desktop Editor has no compass: measured: tests/projects/platform")]
    public partial class Compass
    {
        public float magneticHeading => 0f;
        public float trueHeading => 0f;
        public float headingAccuracy => 0f;
        public Vector3 rawVector => Vector3.zero;
        public double timestamp => 0;
        public bool enabled { get; set; }
    }

    [Unbound.EditorFaithful("the desktop Editor has no gyroscope: measured: tests/projects/platform")]
    public partial class Gyroscope
    {
        internal Gyroscope(int index) { }

        public Vector3 rotationRate => Vector3.zero;
        public Vector3 rotationRateUnbiased => Vector3.zero;
        public Vector3 gravity => new Vector3(0, -1, 0);
        public Vector3 userAcceleration => Vector3.zero;
        public Quaternion attitude => Quaternion.identity;
        public bool enabled { get; set; }
        public float updateInterval { get; set; } = 0.1f;
    }
}

namespace Unbound.Runtime
{
    /// <summary>Key names of the Input Manager ("a", "left shift", "mouse 0", "joystick button 1").</summary>
    internal static class KeyNames
    {
        static readonly Dictionary<string, UnityEngine.KeyCode> s_ByName = Build();

        static Dictionary<string, UnityEngine.KeyCode> Build()
        {
            var map = new Dictionary<string, UnityEngine.KeyCode>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.KeyCode code in Enum.GetValues(typeof(UnityEngine.KeyCode)))
                map[code.ToString()] = code;
            for (char c = 'a'; c <= 'z'; c++) map[c.ToString()] = (UnityEngine.KeyCode)c;
            for (int i = 0; i <= 9; i++)
            {
                map[i.ToString()] = UnityEngine.KeyCode.Alpha0 + i;
                map[$"[{i}]"] = UnityEngine.KeyCode.Keypad0 + i;
            }
            for (int i = 0; i <= 6; i++) map[$"mouse {i}"] = UnityEngine.KeyCode.Mouse0 + i;
            for (int i = 1; i <= 15; i++) map[$"f{i}"] = UnityEngine.KeyCode.F1 + (i - 1);
            for (int i = 0; i < 20; i++)
            {
                map[$"joystick button {i}"] = UnityEngine.KeyCode.JoystickButton0 + i;
                for (int j = 1; j <= 8; j++) map[$"joystick {j} button {i}"] = UnityEngine.KeyCode.Joystick1Button0 + (j - 1) * 20 + i;
            }
            void Add(string name, UnityEngine.KeyCode code) => map[name] = code;
            Add("space", UnityEngine.KeyCode.Space); Add("return", UnityEngine.KeyCode.Return); Add("enter", UnityEngine.KeyCode.KeypadEnter);
            Add("escape", UnityEngine.KeyCode.Escape); Add("backspace", UnityEngine.KeyCode.Backspace); Add("tab", UnityEngine.KeyCode.Tab);
            Add("delete", UnityEngine.KeyCode.Delete); Add("insert", UnityEngine.KeyCode.Insert); Add("home", UnityEngine.KeyCode.Home);
            Add("end", UnityEngine.KeyCode.End); Add("page up", UnityEngine.KeyCode.PageUp); Add("page down", UnityEngine.KeyCode.PageDown);
            Add("up", UnityEngine.KeyCode.UpArrow); Add("down", UnityEngine.KeyCode.DownArrow); Add("left", UnityEngine.KeyCode.LeftArrow); Add("right", UnityEngine.KeyCode.RightArrow);
            Add("left shift", UnityEngine.KeyCode.LeftShift); Add("right shift", UnityEngine.KeyCode.RightShift);
            Add("left ctrl", UnityEngine.KeyCode.LeftControl); Add("right ctrl", UnityEngine.KeyCode.RightControl);
            Add("left alt", UnityEngine.KeyCode.LeftAlt); Add("right alt", UnityEngine.KeyCode.RightAlt);
            Add("left cmd", UnityEngine.KeyCode.LeftCommand); Add("right cmd", UnityEngine.KeyCode.RightCommand);
            Add("caps lock", UnityEngine.KeyCode.CapsLock); Add("numlock", UnityEngine.KeyCode.Numlock);
            Add("-", UnityEngine.KeyCode.Minus); Add("=", UnityEngine.KeyCode.Equals); Add("[", UnityEngine.KeyCode.LeftBracket); Add("]", UnityEngine.KeyCode.RightBracket);
            Add(";", UnityEngine.KeyCode.Semicolon); Add("'", UnityEngine.KeyCode.Quote); Add(",", UnityEngine.KeyCode.Comma); Add(".", UnityEngine.KeyCode.Period);
            Add("/", UnityEngine.KeyCode.Slash); Add("\\", UnityEngine.KeyCode.Backslash); Add("`", UnityEngine.KeyCode.BackQuote);
            return map;
        }

        internal static UnityEngine.KeyCode Parse(string name) =>
            name != null && s_ByName.TryGetValue(name.Trim(), out var code) ? code : UnityEngine.KeyCode.None;
    }
}
