using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
/// Records, frame by frame, what every input path reports when buttons and keys go down and up
/// between two frames (once, several times, up then down), next to presses in adjacent frames, in
/// frames with several fixed steps or none, and while the Input System processes its events in
/// fixed updates: the Input Manager (<c>Input.GetKey*</c>, <c>GetMouseButton*</c>, <c>anyKey*</c>,
/// <c>inputString</c>) in Update and FixedUpdate, the Input System (controls, its event stream,
/// actions with and without interactions, polled and called back), IMGUI events, a uGUI button
/// under the StandaloneInputModule, and OnMouse* messages. Player Settings' active input handling
/// is Both. In the Editor the steps are played as real X11 input (ProbeDriver,
/// Editor/ProbeRunner.cs); in Unbound by the run's scripted input options.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class InputFramesProbe : MonoBehaviour
{
    /// <summary>The last frame recorded.</summary>
    public const int EndFrame = 720;

    /// <summary>A step: at <see cref="frame"/> (Time.frameCount) its input is seen. Tokens, played in
    /// order within the frame: <c>+Key</c> presses, <c>-Key</c> releases (Unity KeyCode names,
    /// Mouse0 the left button), <c>@X,Y</c> moves the pointer (game-view pixels from the top left of
    /// the 960x540 screen).</summary>
    public struct Step
    {
        public int frame;
        public string[] tokens;

        public override string ToString() => $"{frame}:{string.Join(",", tokens)}";
    }

    static Step S(int frame, string tokens) => new Step { frame = frame, tokens = tokens.Split(' ') };

    // The button and the cube are centred at (480, 270). Mouse clicks are 40 frames (more than a
    // second) apart and held buttons 20 frames, so double clicks (500 ms in IMGUI, 300 ms in uGUI)
    // never hang on the frame rate.
    public static readonly Step[] Steps =
    {
        // Two pointer moves the Editor driver calibrates with, then a click on empty space (it
        // gives the game view the focus), then the pointer over the button and the cube.
        S(3, "@200,150"), S(5, "@700,400"),
        S(10, "@480,80"), S(11, "+Mouse0"), S(13, "-Mouse0"),
        S(20, "@480,270"),
        // Mouse: a click within a frame, two, a held button released and pressed again within a
        // frame, the right and middle buttons, a press and release in adjacent frames, two buttons.
        S(30, "+Mouse0 -Mouse0"),
        S(70, "+Mouse0 -Mouse0 +Mouse0 -Mouse0"),
        S(110, "+Mouse0"), S(130, "-Mouse0 +Mouse0"), S(150, "-Mouse0"),
        S(190, "+Mouse1 -Mouse1"),
        S(230, "+Mouse2 -Mouse2"),
        S(270, "+Mouse0"), S(271, "-Mouse0"),
        S(310, "+Mouse0 +Mouse1 -Mouse0 -Mouse1"),
        // Keys: the same with A, two keys overlapping and in turn, keys that type control
        // characters, a shifted letter, a key pressed, released and pressed again.
        S(330, "+A -A"),
        S(340, "+A -A +A -A"),
        S(350, "+A"), S(360, "-A +A"), S(370, "-A"),
        S(380, "+A +B -A -B"),
        S(390, "+A -A +B -B"),
        S(400, "+Return -Return"),
        S(410, "+Space -Space"),
        S(420, "+LeftShift"), S(422, "+A -A"), S(424, "-LeftShift"),
        S(430, "+B -B +B"), S(432, "-B"),
        // A key and a button within a frame.
        S(440, "+A +Mouse0 -A -Mouse0"),
        S(480, "+Mouse0 -Mouse0 +B -B"),
        // Fixed steps (see Captured): a click and a key within a frame of five steps, a key within
        // a frame of none, a key held from a five-step frame to the next, a click in a frame of
        // none, a press in a frame of five steps released in a frame of none.
        S(520, "+Mouse0 -Mouse0 +A -A"),
        S(530, "+A -A"),
        S(540, "+A"), S(542, "-A"),
        S(560, "+Mouse0 -Mouse0"),
        S(600, "+Mouse0"), S(601, "-Mouse0"),
        // The Input System processes its events in fixed updates from frame 630 to 699.
        S(640, "+A -A"),
        S(650, "+A -A"),
        S(660, "+A -A"),
        S(670, "+Mouse0 -Mouse0"),
        S(680, "+A"), S(690, "-A"),
        // Modifier and other keys that type nothing, for the modifiers IMGUI gives their events
        // (right modifiers are not played: xdotool presses the left one with them).
        S(702, "+LeftControl -LeftControl"), S(704, "+Insert -Insert"), S(706, "+Home -Home"),
        S(708, "+LeftAlt -LeftAlt"),
    };

    /// <summary>Game time per frame is 1/60 s (Time.captureFramerate 60, fixed steps of 0.02 s);
    /// these frames step by another capture rate: 10 (0.1 s, five fixed steps) or 1000 (1 ms).</summary>
    public static readonly Dictionary<int, int> Captured = new Dictionary<int, int>
    {
        [520] = 10, [530] = 1000, [540] = 10, [542] = 10, [560] = 1000, [600] = 10, [601] = 1000,
        [650] = 10, [660] = 1000, [670] = 10,
    };

    public const int FixedInputFrom = 630, FixedInputTo = 700;

    /// <summary>Plays a step as real input (the Editor's driver); null in Unbound.</summary>
    public static Action<Step> driver;
    public static Action<int> frameHook;
    public static Action finished;

    static readonly KeyCode[] s_LegacyKeys =
    {
        KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.A, KeyCode.B, KeyCode.Space, KeyCode.Return, KeyCode.LeftShift,
        KeyCode.LeftControl, KeyCode.LeftAlt, KeyCode.Insert, KeyCode.Home,
    };

    static string s_Phase = "boot";
    static double s_FrameStart;
    int m_FixedStep;
    bool m_Done;
    readonly List<InputAction> m_Actions = new List<InputAction>();

    struct FrameStartSystem { }

    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Time.fixedDeltaTime = 0.02f;
        Time.captureFramerate = 60;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ProbeDriver")).FirstOrDefault(t => t != null);
        type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.EarlyUpdate)) continue;
            var subs = loop.subSystemList[i].subSystemList.ToList();
            subs.Insert(0, new PlayerLoopSystem { type = typeof(FrameStartSystem), updateDelegate = FrameStart });
            loop.subSystemList[i].subSystemList = subs.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
        Build();
    }

    static void FrameStart()
    {
        s_Phase = "early";
        s_FrameStart = Time.realtimeSinceStartupAsDouble;
        if (Time.frameCount > EndFrame) return;
        ProbeLog.Add("frame", "realtime", ProbeLog.F(Time.realtimeSinceStartupAsDouble), "time", ProbeLog.F(Time.timeAsDouble),
            "dt", ProbeLog.F(Time.deltaTime), "inputTime", ProbeLog.F(InputState.currentTime));
    }

    /// <summary>The camera, the cube (OnMouse*) and the uGUI button, both at the screen's centre.</summary>
    void Build()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.transform.position = Vector3.zero;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0f, 0f, 5f);
        cube.AddComponent<MouseMessages>();

        var events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<StandaloneInputModule>();
        var canvasObject = new GameObject("Canvas");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<GraphicRaycaster>();
        var buttonObject = new GameObject("Button");
        buttonObject.transform.SetParent(canvasObject.transform, false);
        var rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(300f, 200f);
        rect.anchoredPosition = Vector2.zero;
        buttonObject.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.6f, 1f);
        var button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(() => ProbeLog.Add("onClick", "p", ProbeLog.Q(s_Phase)));
        buttonObject.AddComponent<PointerEvents>();
    }

    void Start()
    {
        ProbeLog.Add("meta", "unity", ProbeLog.Q(Application.unityVersion), "inputSystem", ProbeLog.Q(InputSystem.version.ToString()),
            "steps", ProbeLog.Q(string.Join(" ", Steps.Select(s => s.ToString()))),
            "captured", ProbeLog.Q(string.Join(" ", Captured.Select(c => $"{c.Key}:{c.Value}"))),
            "fixedInput", ProbeLog.Q($"{FixedInputFrom}:{FixedInputTo}"),
            "updateMode", ProbeLog.Q(InputSystem.settings.updateMode.ToString()));
        InputSystem.onEvent += OnEvent;
        InputSystem.onBeforeUpdate += () =>
        {
            if (Time.frameCount <= EndFrame) ProbeLog.Add("inputUpdate", "type", ProbeLog.Q(InputState.currentUpdateType.ToString()), "p", ProbeLog.Q(s_Phase));
        };
        if (Keyboard.current != null)
            Keyboard.current.onTextInput += c => ProbeLog.Add("text", "char", ((int)c).ToString(), "p", ProbeLog.Q(s_Phase));

        Add("mouseLeft", InputActionType.Button, "<Mouse>/leftButton");
        Add("mouseRight", InputActionType.Button, "<Mouse>/rightButton");
        Add("keyA", InputActionType.Button, "<Keyboard>/a");
        Add("keyAPass", InputActionType.PassThrough, "<Keyboard>/a");
        Add("keyAValue", InputActionType.Value, "<Keyboard>/a");
        Add("keyARelease", InputActionType.Button, "<Keyboard>/a", "press(behavior=1)");
        Add("keyAPressRelease", InputActionType.Button, "<Keyboard>/a", "press(behavior=2)");
        Add("keyB", InputActionType.Button, "<Keyboard>/b");
    }

    void Add(string name, InputActionType type, string binding, string interactions = null)
    {
        var action = new InputAction(name, type, binding, interactions);
        action.started += c => Action("started", c);
        action.performed += c => Action("performed", c);
        action.canceled += c => Action("canceled", c);
        action.Enable();
        m_Actions.Add(action);
    }

    static void Action(string phase, InputAction.CallbackContext c)
    {
        if (Time.frameCount > EndFrame) return;
        ProbeLog.Add("action", "name", ProbeLog.Q(c.action.name), "phase", ProbeLog.Q(phase), "value", ProbeLog.F(c.ReadValue<float>()),
            "control", ProbeLog.Q(c.control?.path), "interaction", ProbeLog.Q(c.interaction?.GetType().Name),
            "time", ProbeLog.F(c.time), "start", ProbeLog.F(c.startTime), "update", ProbeLog.Q(InputState.currentUpdateType.ToString()),
            "p", ProbeLog.Q(s_Phase));
    }

    static unsafe void OnEvent(InputEventPtr e, InputDevice device)
    {
        if (Time.frameCount > EndFrame) return;
        var type = e.type;
        var fields = new List<string>
        {
            "type", ProbeLog.Q(type.ToString()), "device", ProbeLog.Q(device?.name), "time", ProbeLog.F(e.time),
            "update", ProbeLog.Q(InputState.currentUpdateType.ToString()), "p", ProbeLog.Q(s_Phase),
        };
        if (e.IsA<StateEvent>())
        {
            var state = StateEvent.From(e);
            fields.Add("format");
            fields.Add(ProbeLog.Q(state->stateFormat.ToString()));
            var bytes = (byte*)state->state;
            if (state->stateFormat == new FourCC('M', 'O', 'U', 'S') && state->stateSizeInBytes >= 26)
            {
                var floats = (float*)bytes;
                fields.Add("pos"); fields.Add(ProbeLog.V(new Vector2(floats[0], floats[1])));
                fields.Add("delta"); fields.Add(ProbeLog.V(new Vector2(floats[2], floats[3])));
                fields.Add("scroll"); fields.Add(ProbeLog.V(new Vector2(floats[4], floats[5])));
                fields.Add("buttons"); fields.Add((*(ushort*)(bytes + 24)).ToString());
            }
            else if (state->stateFormat == new FourCC('K', 'E', 'Y', 'S'))
            {
                var keys = new List<string>();
                for (int bit = 0; bit < state->stateSizeInBytes * 8; bit++)
                    if ((bytes[bit >> 3] & (1 << (bit & 7))) != 0) keys.Add(((Key)bit).ToString());
                fields.Add("keys"); fields.Add(ProbeLog.Q(string.Join(",", keys)));
            }
        }
        else if (e.IsA<TextEvent>())
        {
            fields.Add("char");
            fields.Add(TextEvent.From(e)->character.ToString());
        }
        ProbeLog.Add("event", fields.ToArray());
    }

    void FixedUpdate()
    {
        s_Phase = "fixed";
        if (Time.frameCount > EndFrame) return;
        ProbeLog.Add("fixed", "step", (m_FixedStep++).ToString(), "time", ProbeLog.F(Time.fixedTimeAsDouble),
            "unscaled", ProbeLog.F(Time.fixedUnscaledTimeAsDouble));
        Record();
    }

    void Update()
    {
        s_Phase = "update";
        m_FixedStep = 0;
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
        Record();
        frameHook?.Invoke(frame);
        // What is set here applies from the next frame on, as the input played now arrives then.
        Time.captureFramerate = Captured.TryGetValue(frame + 1, out var rate) ? rate : 60;
        if (frame + 1 == FixedInputFrom) InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
        if (frame + 1 == FixedInputTo) InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        if (driver != null)
            foreach (var step in Steps)
                if (step.frame == frame + 1) driver(step);
    }

    void LateUpdate()
    {
        s_Phase = "late";
        // Frames last at least 30 ms (the Editor's on Xvfb take about 34), so that what counts
        // real time (double clicks) sees the same input in both.
        while (Time.realtimeSinceStartupAsDouble - s_FrameStart < 0.03)
            System.Threading.Thread.Sleep(1);
    }

    /// <summary>The Input Manager's, the Input System's controls' and the actions' view of the
    /// frame, where anything is held, pressed, released or typed.</summary>
    void Record()
    {
        var legacy = new List<string>();
        foreach (var key in s_LegacyKeys)
        {
            var flags = (Input.GetKey(key) ? "H" : "") + (Input.GetKeyDown(key) ? "D" : "") + (Input.GetKeyUp(key) ? "U" : "");
            if (flags.Length > 0) legacy.Add($"{key}:{flags}");
        }
        for (int button = 0; button < 3; button++)
        {
            var flags = (Input.GetMouseButton(button) ? "H" : "") + (Input.GetMouseButtonDown(button) ? "D" : "") + (Input.GetMouseButtonUp(button) ? "U" : "");
            if (flags.Length > 0) legacy.Add($"button{button}:{flags}");
        }
        if (Input.anyKey) legacy.Add("anyKey");
        if (Input.anyKeyDown) legacy.Add("anyKeyDown");
        string typed = Input.inputString;
        if (legacy.Count > 0 || typed.Length > 0)
            ProbeLog.Add("legacy", "p", ProbeLog.Q(s_Phase), "v", ProbeLog.Q(string.Join(" ", legacy)), "inputString", ProbeLog.Q(typed));

        var controls = new List<string>();
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse != null)
        {
            Control(controls, mouse.leftButton);
            Control(controls, mouse.rightButton);
            Control(controls, mouse.middleButton);
        }
        if (keyboard != null)
        {
            Control(controls, keyboard.aKey);
            Control(controls, keyboard.bKey);
            Control(controls, keyboard.spaceKey);
            Control(controls, keyboard.enterKey);
            Control(controls, keyboard.leftShiftKey);
            Control(controls, keyboard.anyKey);
        }
        foreach (var action in m_Actions)
        {
            var flags = (action.WasPressedThisFrame() ? "P" : "") + (action.WasReleasedThisFrame() ? "R" : "") +
                (action.WasPerformedThisFrame() ? "F" : "") + (action.WasCompletedThisFrame() ? "C" : "") +
                (action.IsPressed() ? "H" : "") + (action.IsInProgress() ? "I" : "");
            if (flags.Length > 0) controls.Add($"{action.name}:{flags}");
        }
        if (controls.Count > 0) ProbeLog.Add("inputSystem", "p", ProbeLog.Q(s_Phase), "v", ProbeLog.Q(string.Join(" ", controls)));
    }

    static void Control(List<string> controls, ButtonControl control)
    {
        var flags = (control.isPressed ? "H" : "") + (control.wasPressedThisFrame ? "P" : "") + (control.wasReleasedThisFrame ? "R" : "");
        if (flags.Length > 0) controls.Add($"{control.device.name}/{control.name}:{flags}");
    }

    void OnGUI()
    {
        s_Phase = "gui";
        var e = Event.current;
        if (Time.frameCount > EndFrame || e.type == EventType.Layout || e.type == EventType.Repaint) return;
        ProbeLog.Add("gui", "type", ProbeLog.Q(e.type.ToString()), "button", e.button.ToString(), "clickCount", e.clickCount.ToString(),
            "keyCode", ProbeLog.Q(e.keyCode.ToString()), "char", ((int)e.character).ToString(), "modifiers", ProbeLog.Q(e.modifiers.ToString()),
            "pos", ProbeLog.V(e.mousePosition));
    }

    /// <summary>Records where in the frame the probe is, for the messages' records.</summary>
    public static string Phase => s_Phase;
}

/// <summary>The OnMouse* messages of the cube.</summary>
public class MouseMessages : MonoBehaviour
{
    void OnMouseEnter() => Log("OnMouseEnter");
    void OnMouseExit() => Log("OnMouseExit");
    void OnMouseDown() => Log("OnMouseDown");
    void OnMouseUp() => Log("OnMouseUp");
    void OnMouseUpAsButton() => Log("OnMouseUpAsButton");
    void OnMouseDrag() => Log("OnMouseDrag");

    static void Log(string message)
    {
        if (Time.frameCount <= InputFramesProbe.EndFrame) ProbeLog.Add("onMouse", "m", ProbeLog.Q(message), "p", ProbeLog.Q(InputFramesProbe.Phase));
    }
}

/// <summary>The uGUI pointer events of the button.</summary>
public class PointerEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerDown(PointerEventData e) => Log("down", e);
    public void OnPointerUp(PointerEventData e) => Log("up", e);
    public void OnPointerClick(PointerEventData e) => Log("click", e);
    public void OnPointerEnter(PointerEventData e) => Log("enter", e);
    public void OnPointerExit(PointerEventData e) => Log("exit", e);

    static void Log(string kind, PointerEventData e)
    {
        if (Time.frameCount <= InputFramesProbe.EndFrame)
            ProbeLog.Add("ui", "e", ProbeLog.Q(kind), "button", ProbeLog.Q(e.button.ToString()), "clickCount", e.clickCount.ToString(),
                "p", ProbeLog.Q(InputFramesProbe.Phase));
    }
}
