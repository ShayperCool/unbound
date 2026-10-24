using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Records what the Input System, PlayerInput and the uGUI input module do for a scripted input
/// sequence (<see cref="Steps"/>). In the Editor the sequence is played as real X11 input
/// (ProbeDriver, Editor/ProbeRunner.cs); in Unbound by the run's scripted input options.
/// </summary>
public class InputProbe : MonoBehaviour
{
    public Button button;
    public Slider slider;
    public PlayerInput unityEventsPlayer;
    public PlayerInput csharpPlayer;

    /// <summary>The last frame recorded.</summary>
    public const int EndFrame = 300;

    /// <summary>A scripted input step: at <see cref="frame"/> (Time.frameCount) the input is seen.
    /// Positions are game-view pixels from the top left of a 960x540 screen.</summary>
    public struct Step
    {
        public int frame;
        public string kind; // move, press, release, scroll, type
        public int x, y;
        public string key; // a Unity KeyCode name (Mouse0 for the left button)
        public string text;

        public override string ToString() => kind switch
        {
            "move" => $"{frame}:move {x},{y}",
            "scroll" => $"{frame}:scroll {x},{y}",
            "type" => $"{frame}:type {text}",
            _ => $"{frame}:{kind} {key}",
        };
    }

    static Step Move(int f, int x, int y) => new Step { frame = f, kind = "move", x = x, y = y };
    static Step Press(int f, string key) => new Step { frame = f, kind = "press", key = key };
    static Step Release(int f, string key) => new Step { frame = f, kind = "release", key = key };
    static Step Scroll(int f, int ticks) => new Step { frame = f, kind = "scroll", y = ticks };
    static Step Type(int f, string text) => new Step { frame = f, kind = "type", text = text };

    // Screen 960x540: the button's centre is (280, 270), the slider spans x 480..780 at y 270
    // (its handle area 490..770), the scroll target is centred at (480, 420).
    public static readonly Step[] Steps =
    {
        // Two pointer moves the Editor driver calibrates with, then a first click on empty space
        // (it gives the game view focus).
        Move(3, 200, 150), Move(5, 700, 400),
        Move(10, 480, 80), Press(11, "Mouse0"), Release(13, "Mouse0"),
        // Hover, click the button. Presses of a button are more than half a second (InputForUI's
        // double-click time) apart in game time, so click counts do not hang on frame rates.
        Move(30, 280, 270),
        Press(50, "Mouse0"), Release(54, "Mouse0"),
        Move(60, 300, 260),
        // Move (W held, shorter than the UI navigation repeat delay), Jump (Space), Interact (E
        // held past its hold time).
        Press(70, "W"), Release(80, "W"),
        Press(100, "Space"), Release(101, "Space"),
        Press(110, "E"), Release(140, "E"),
        // Click the slider at 75 %, then drag its handle.
        Move(160, 700, 270), Press(161, "Mouse0"), Release(164, "Mouse0"),
        Move(200, 560, 270), Press(201, "Mouse0"), Move(203, 600, 270), Move(205, 640, 270), Release(207, "Mouse0"),
        // Scroll over the scroll target.
        Move(220, 480, 420), Scroll(221, 1), Scroll(225, -1),
        // Typing.
        Type(240, "hi"),
        // Navigate to the button and submit, then cancel.
        Press(260, "LeftArrow"), Release(261, "LeftArrow"),
        Press(270, "Return"), Release(271, "Return"),
        Press(285, "Escape"), Release(286, "Escape"),
    };

    /// <summary>Plays a step as real input (the Editor's driver); null in Unbound.</summary>
    public static Action<Step> driver;
    public static Action<int> frameHook;
    public static Action finished;

    readonly List<PlayerInput> m_Players = new List<PlayerInput>();
    string m_LastKeyboard, m_LastMouse, m_LastSelected;
    bool m_Done;

    struct FrameStartSystem { }

    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ProbeDriver")).FirstOrDefault(t => t != null);
        type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

        // A system at the start of the frame records the frame's clocks.
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.EarlyUpdate)) continue;
            var subs = loop.subSystemList[i].subSystemList.ToList();
            subs.Insert(0, new PlayerLoopSystem { type = typeof(FrameStartSystem), updateDelegate = FrameStart });
            loop.subSystemList[i].subSystemList = subs.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void FrameStart()
    {
        if (Time.frameCount > EndFrame) return;
        var rational = Time.timeAsRational;
        ProbeLog.Add("frame", "realtime", ProbeLog.F(Time.realtimeSinceStartupAsDouble), "unscaled", ProbeLog.F(Time.unscaledTimeAsDouble),
            "time", ProbeLog.F(Time.timeAsDouble), "rational", ProbeLog.Q($"{rational.Count}/{rational.Ticks.Numerator}/{rational.Ticks.Denominator}"),
            "inputTime", ProbeLog.F(InputState.currentTime), "screen", ProbeLog.V(new Vector2(Screen.width, Screen.height)));
    }

    void Start()
    {
        ProbeLog.Add("meta", "unity", ProbeLog.Q(Application.unityVersion), "inputSystem", ProbeLog.Q(InputSystem.version.ToString()),
            "steps", ProbeLog.Q(string.Join(" ", Steps.Select(s => s.ToString()))),
            "actions", ProbeLog.Q(InputSystem.actions != null ? InputSystem.actions.name : null),
            "defaultTapTime", ProbeLog.F(InputSystem.settings.defaultTapTime),
            "updateMode", ProbeLog.Q(InputSystem.settings.updateMode.ToString()),
            "settingsName", ProbeLog.Q(InputSystem.settings.name));
        // The system's double-click time (InputForUI counts clicks with it).
        var doubleClick = typeof(UnityEngine.Event).GetMethod("GetDoubleClickTime", BindingFlags.NonPublic | BindingFlags.Static);
        ProbeLog.Add("doubleClickTime", "ms", ProbeLog.Q(Describe(doubleClick?.Invoke(null, null))));
        foreach (var phase in PlayerLoop.GetDefaultPlayerLoop().subSystemList)
            ProbeLog.Add("playerLoop", "phase", ProbeLog.Q(phase.type.Name),
                "systems", ProbeLog.Q(string.Join(",", phase.subSystemList.Select(s => s.type.Name))));
        LegacyInput();
        Devices();
        // The native scroll scale, uniform (the default) and the platform's own.
        var perTick = typeof(InputSystem).GetProperty("scrollWheelDeltaPerTick", BindingFlags.NonPublic | BindingFlags.Static);
        var uniform = perTick?.GetValue(null);
        InputSystem.settings.scrollDeltaBehavior = InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange;
        var platform = perTick?.GetValue(null);
        InputSystem.settings.scrollDeltaBehavior = InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
        ProbeLog.Add("scrollPerTick", "uniform", ProbeLog.Q(Describe(uniform)), "platform", ProbeLog.Q(Describe(platform)));

        InputSystem.onBeforeUpdate += () =>
        {
            if (Time.frameCount <= EndFrame) ProbeLog.Add("update", "type", ProbeLog.Q(InputState.currentUpdateType.ToString()), "fixed", Time.inFixedTimeStep ? "true" : "false");
        };
        InputSystem.onEvent += OnEvent;
        InputSystem.onDeviceChange += (device, change) =>
            ProbeLog.Add("deviceChange", "device", ProbeLog.Q(device.name), "change", ProbeLog.Q(change.ToString()));

        foreach (var map in InputSystem.actions.actionMaps)
        {
            foreach (var action in map.actions)
            {
                action.started += c => Action("started", c);
                action.performed += c => Action("performed", c);
                action.canceled += c => Action("canceled", c);
            }
        }
        if (Keyboard.current != null)
            Keyboard.current.onTextInput += c => ProbeLog.Add("text", "char", ProbeLog.Q(c.ToString()));
        SubscribeInputForUI();

        button.onClick.AddListener(() => ProbeLog.Add("click", "on", ProbeLog.Q(button.name)));
        slider.onValueChanged.AddListener(v => ProbeLog.Add("slider", "value", ProbeLog.F(v)));

        var devices = new InputDevice[] { Keyboard.current, Mouse.current };
        var events = PlayerInput.Instantiate(unityEventsPlayer.gameObject, controlScheme: "Keyboard&Mouse", pairWithDevices: devices);
        var csharp = PlayerInput.Instantiate(csharpPlayer.gameObject, controlScheme: "Keyboard&Mouse", pairWithDevices: devices);
        csharp.onActionTriggered += csharp.GetComponent<PlayerMessages>().OnActionTriggered;
        foreach (var player in PlayerInput.all)
            ProbeLog.Add("playerInput", "name", ProbeLog.Q(player.name), "index", player.playerIndex.ToString(),
                "scheme", ProbeLog.Q(player.currentControlScheme), "map", ProbeLog.Q(player.currentActionMap?.name),
                "devices", ProbeLog.Q(string.Join(",", player.devices.Select(d => d.name))),
                "behavior", ProbeLog.Q(player.notificationBehavior.ToString()));
    }

    /// <summary>Listens to InputForUI (what UI Toolkit reads; internal, hence reflection): the
    /// Input System's ForUI provider turns its actions into pointer, navigation, key and text events.</summary>
    static void SubscribeInputForUI()
    {
        const string module = ", UnityEngine.InputForUIModule";
        var provider = System.Type.GetType("UnityEngine.InputForUI.EventProvider" + module);
        var consumer = System.Type.GetType("UnityEngine.InputForUI.EventConsumer" + module);
        var eventType = System.Type.GetType("UnityEngine.InputForUI.Event" + module);
        if (provider == null || consumer == null || eventType == null)
        {
            ProbeLog.Add("inputForUI", "error", ProbeLog.Q("no EventProvider"));
            return;
        }
        var ev = System.Linq.Expressions.Expression.Parameter(eventType.MakeByRefType(), "ev");
        var log = typeof(InputProbe).GetMethod(nameof(OnInputForUI), BindingFlags.NonPublic | BindingFlags.Static);
        var body = System.Linq.Expressions.Expression.Call(log, System.Linq.Expressions.Expression.Convert(ev, typeof(object)));
        var handler = System.Linq.Expressions.Expression.Lambda(consumer, body, ev).Compile();
        var types = Array.CreateInstance(eventType.GetNestedType("Type"), 0);
        provider.GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { handler, 0, null, types });
        ProbeLog.Add("inputForUI", "provider", ProbeLog.Q((string)provider.GetProperty("_providerClassName", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)));
    }

    static bool OnInputForUI(object ev)
    {
        if (Time.frameCount <= EndFrame) ProbeLog.Add("inputForUI", "event", ProbeLog.Q(ev.ToString()));
        return false;
    }

    /// <summary>Which members of the legacy Input class refuse to work when only the Input System
    /// package handles input.</summary>
    void LegacyInput()
    {
        var calls = new (string, Func<object>)[]
        {
            ("GetKey", () => Input.GetKey(KeyCode.A)),
            ("GetKeyString", () => Input.GetKey("a")),
            ("GetKeyDown", () => Input.GetKeyDown(KeyCode.A)),
            ("GetKeyUp", () => Input.GetKeyUp(KeyCode.A)),
            ("GetAxis", () => Input.GetAxis("Horizontal")),
            ("GetAxisRaw", () => Input.GetAxisRaw("Horizontal")),
            ("GetButton", () => Input.GetButton("Fire1")),
            ("GetButtonDown", () => Input.GetButtonDown("Fire1")),
            ("GetMouseButton", () => Input.GetMouseButton(0)),
            ("GetMouseButtonDown", () => Input.GetMouseButtonDown(0)),
            ("mousePosition", () => Input.mousePosition),
            ("mouseScrollDelta", () => Input.mouseScrollDelta),
            ("mousePresent", () => Input.mousePresent),
            ("anyKey", () => Input.anyKey),
            ("anyKeyDown", () => Input.anyKeyDown),
            ("inputString", () => Input.inputString),
            ("touchCount", () => Input.touchCount),
            ("touches", () => Input.touches.Length),
            ("touchSupported", () => Input.touchSupported),
            ("multiTouchEnabled", () => Input.multiTouchEnabled),
            ("simulateMouseWithTouches", () => Input.simulateMouseWithTouches),
            ("acceleration", () => Input.acceleration),
            ("deviceOrientation", () => Input.deviceOrientation),
            ("compositionString", () => Input.compositionString),
            ("imeCompositionMode", () => Input.imeCompositionMode),
            ("compositionCursorPos", () => Input.compositionCursorPos),
            ("backButtonLeavesApp", () => Input.backButtonLeavesApp),
            ("GetJoystickNames", () => Input.GetJoystickNames().Length),
            ("gyroEnabled", () => Input.gyro.enabled),
            ("compassEnabled", () => Input.compass.enabled),
            ("locationEnabled", () => Input.location.isEnabledByUser),
            ("ResetInputAxes", () => { Input.ResetInputAxes(); return null; }),
        };
        foreach (var (name, call) in calls)
        {
            try
            {
                ProbeLog.Add("legacy", "member", ProbeLog.Q(name), "result", ProbeLog.Q(Describe(call())));
            }
            catch (Exception e)
            {
                ProbeLog.Add("legacy", "member", ProbeLog.Q(name), "exception", ProbeLog.Q(e.GetType().Name), "message", ProbeLog.Q(e.Message));
            }
        }
    }

    void Devices()
    {
        foreach (var device in InputSystem.devices)
        {
            ProbeLog.Add("device", "id", device.deviceId.ToString(), "name", ProbeLog.Q(device.name), "layout", ProbeLog.Q(device.layout),
                "native", device.native ? "true" : "false", "description", ProbeLog.Q(device.description.ToJson()),
                "usages", ProbeLog.Q(string.Join(",", device.usages)), "canRunInBackground", device.canRunInBackground ? "true" : "false",
                "enabled", device.enabled ? "true" : "false", "updateBeforeRender", device.updateBeforeRender ? "true" : "false",
                "stateSize", (device.stateBlock.sizeInBits / 8).ToString());
            Commands(device);
        }
        if (Keyboard.current != null)
        {
            var keys = new StringBuilder();
            foreach (var key in Keyboard.current.allKeys)
                keys.Append(key.keyCode).Append('=').Append(key.displayName).Append('/').Append(key.scanCode).Append(' ');
            ProbeLog.Add("keys", "layout", ProbeLog.Q(Keyboard.current.keyboardLayout), "names", ProbeLog.Q(keys.ToString().TrimEnd()));
        }
    }

    static void Commands(InputDevice device)
    {
        var enabled = QueryEnabledStateCommand.Create();
        Command(device, "QENB", device.ExecuteCommand(ref enabled), enabled.isEnabled ? "true" : "false");
        var background = QueryCanRunInBackground.Create();
        Command(device, "QRIB", device.ExecuteCommand(ref background), background.canRunInBackground ? "true" : "false");
        var layout = QueryKeyboardLayoutCommand.Create();
        long r = device.ExecuteCommand(ref layout);
        Command(device, "KBLT", r, r >= 0 ? layout.ReadLayoutName() : "");
        var dimensions = QueryDimensionsCommand.Create();
        Command(device, "DIMS", device.ExecuteCommand(ref dimensions), ProbeLog.V(dimensions.outDimensions));
        var frequency = new RawCommand(new FourCC('S', 'M', 'P', 'L'));
        Command(device, "SMPL", device.ExecuteCommand(ref frequency), ProbeLog.F(frequency.x));
        var window = new RawCommand(new FourCC('E', 'W', 'P', 'S')) { x = 100, y = 100 };
        Command(device, "EWPS", device.ExecuteCommand(ref window), ProbeLog.V(new Vector2(window.x, window.y)));
        var sync = RequestSyncCommand.Create();
        Command(device, "SYNC", device.ExecuteCommand(ref sync), "");
        var unknown = new InputDeviceCommand(new FourCC('Z', 'Z', 'Z', 'Z'), 0);
        Command(device, "ZZZZ", device.ExecuteCommand(ref unknown), "");
        var ime = EnableIMECompositionCommand.Create(true);
        Command(device, "IMEM", device.ExecuteCommand(ref ime), "");
        var imeOff = EnableIMECompositionCommand.Create(false);
        Command(device, "IMEM-off", device.ExecuteCommand(ref imeOff), "");
        var imePosition = SetIMECursorPositionCommand.Create(new Vector2(10, 20));
        Command(device, "IMEP", device.ExecuteCommand(ref imePosition), "");
        var enable = EnableDeviceCommand.Create();
        Command(device, "ENBL", device.ExecuteCommand(ref enable), "");
        var reset = RequestResetCommand.Create();
        Command(device, "RSET", device.ExecuteCommand(ref reset), "");
    }

    /// <summary>A command with an 8-byte payload, for the package's internal command types.</summary>
    [StructLayout(LayoutKind.Explicit, Size = InputDeviceCommand.BaseCommandSize + 8)]
    struct RawCommand : IInputDeviceCommandInfo
    {
        [FieldOffset(0)] public InputDeviceCommand baseCommand;
        [FieldOffset(InputDeviceCommand.BaseCommandSize)] public float x;
        [FieldOffset(InputDeviceCommand.BaseCommandSize + 4)] public float y;

        public RawCommand(FourCC type)
        {
            baseCommand = new InputDeviceCommand(type, InputDeviceCommand.BaseCommandSize + 8);
            x = y = 0;
        }

        public FourCC typeStatic => baseCommand.type;
    }

    static void Command(InputDevice device, string type, long result, string payload) =>
        ProbeLog.Add("ioctl", "device", ProbeLog.Q(device.name), "type", ProbeLog.Q(type), "result", result.ToString(), "payload", ProbeLog.Q(payload));

    static void Action(string phase, InputAction.CallbackContext c)
    {
        ProbeLog.Add("action", "action", ProbeLog.Q(c.action.actionMap.name + "/" + c.action.name), "phase", ProbeLog.Q(phase),
            "value", ProbeLog.Q(Describe(c.ReadValueAsObject())), "control", ProbeLog.Q(c.control?.path),
            "interaction", ProbeLog.Q(c.interaction?.GetType().Name), "time", ProbeLog.F(c.time), "start", ProbeLog.F(c.startTime),
            "duration", ProbeLog.F(c.duration));
    }

    public static string Describe(object value)
    {
        switch (value)
        {
            case null: return "null";
            case float f: return ProbeLog.F(f);
            case double d: return ProbeLog.F(d);
            case Vector2 v: return "(" + ProbeLog.F(v.x) + "," + ProbeLog.F(v.y) + ")";
            default: return value.ToString();
        }
    }

    unsafe void OnEvent(InputEventPtr ptr, InputDevice device)
    {
        if (Time.frameCount > EndFrame) return;
        var fields = new List<string>
        {
            "type", ProbeLog.Q(ptr.type.ToString()), "device", ProbeLog.Q(device?.name), "deviceId", ptr.deviceId.ToString(),
            "size", ptr.sizeInBytes.ToString(), "time", ProbeLog.F(ptr.time), "id", ptr.id.ToString(),
        };
        if (ptr.IsA<StateEvent>())
        {
            var state = StateEvent.From(ptr);
            fields.Add("format"); fields.Add(ProbeLog.Q(state->stateFormat.ToString()));
            fields.Add("bytes"); fields.Add(ProbeLog.Q(Hex((byte*)state->state, (int)state->stateSizeInBytes)));
        }
        else if (ptr.IsA<DeltaStateEvent>())
        {
            var delta = DeltaStateEvent.From(ptr);
            fields.Add("format"); fields.Add(ProbeLog.Q(delta->stateFormat.ToString()));
            fields.Add("offset"); fields.Add(delta->stateOffset.ToString());
            fields.Add("bytes"); fields.Add(ProbeLog.Q(Hex((byte*)delta->deltaState, (int)delta->deltaStateSizeInBytes)));
        }
        else if (ptr.IsA<TextEvent>())
        {
            var text = TextEvent.From(ptr);
            fields.Add("char"); fields.Add(text->character.ToString());
        }
        ProbeLog.Add("event", fields.ToArray());
    }

    static unsafe string Hex(byte* data, int size)
    {
        var b = new StringBuilder(size * 2);
        for (int i = 0; i < size; i++) b.Append(data[i].ToString("x2"));
        return b.ToString();
    }

    void LateUpdate()
    {
        if (Time.frameCount <= 2) ProbeLog.Add("lateUpdate");
    }

    void Update()
    {
        int frame = Time.frameCount;
        if (frame > EndFrame)
        {
            if (!m_Done)
            {
                m_Done = true;
                ProbeLog.Add("end");
                ProbeLog.Write();
                finished?.Invoke();
            }
            return;
        }
        DeviceState();
        frameHook?.Invoke(frame);
        // Input played during this frame arrives with the next one.
        if (driver != null)
            foreach (var step in Steps)
                if (step.frame == frame + 1) driver(step);
    }

    void DeviceState()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            var pressed = string.Join(",", keyboard.allKeys.Where(k => k.isPressed).Select(k => k.keyCode.ToString()));
            var down = string.Join(",", keyboard.allKeys.Where(k => k.wasPressedThisFrame).Select(k => k.keyCode.ToString()));
            var up = string.Join(",", keyboard.allKeys.Where(k => k.wasReleasedThisFrame).Select(k => k.keyCode.ToString()));
            var line = pressed + "|" + down + "|" + up + "|" + keyboard.anyKey.isPressed;
            if (line != m_LastKeyboard)
            {
                m_LastKeyboard = line;
                ProbeLog.Add("keyboard", "pressed", ProbeLog.Q(pressed), "down", ProbeLog.Q(down), "up", ProbeLog.Q(up));
            }
        }
        var mouse = Mouse.current;
        if (mouse != null)
        {
            var line = ProbeLog.V(mouse.position.ReadValue()) + ProbeLog.V(mouse.delta.ReadValue()) + ProbeLog.V(mouse.scroll.ReadValue()) +
                mouse.leftButton.isPressed + mouse.leftButton.wasPressedThisFrame + mouse.leftButton.wasReleasedThisFrame + mouse.clickCount.ReadValue();
            if (line != m_LastMouse)
            {
                m_LastMouse = line;
                ProbeLog.Add("mouse", "pos", ProbeLog.V(mouse.position.ReadValue()), "delta", ProbeLog.V(mouse.delta.ReadValue()),
                    "scroll", ProbeLog.V(mouse.scroll.ReadValue()), "left", mouse.leftButton.isPressed ? "true" : "false",
                    "down", mouse.leftButton.wasPressedThisFrame ? "true" : "false", "up", mouse.leftButton.wasReleasedThisFrame ? "true" : "false",
                    "clicks", mouse.clickCount.ReadValue().ToString());
            }
        }
        var selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.name : "";
        if (selected != m_LastSelected)
        {
            m_LastSelected = selected;
            ProbeLog.Add("selected", "name", ProbeLog.Q(selected));
        }
    }
}
