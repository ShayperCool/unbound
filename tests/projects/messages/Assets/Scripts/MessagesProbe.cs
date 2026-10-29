using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Records engine messages and player-loop behaviour frame by frame, for a scripted input sequence
/// (<see cref="Steps"/>) and scripted changes: OnMouse* (3D, 2D, triggers, rigidbodies, layers,
/// event masks, cameras), OnApplicationFocus/Pause, the cursor lock, visibility (frustum, layers,
/// cull distances, shadows), parent changes, fixed steps after a slow frame, coroutines on disabled
/// behaviours, DontSave roots across a scene load, Debug.Break, Application.Quit. In the Editor the
/// input is played as real X11 input (ProbeDriver, Editor/ProbeRunner.cs); in Unbound by the run's
/// scripted input options.
/// </summary>
public class MessagesProbe : MonoBehaviour
{
    public const int EndFrame = 420;
    public const int VisibilityEnd = 385;

    /// <summary>Where in the frame the engine is, for the messages' records.</summary>
    public static string Phase = "boot";

    public struct Step
    {
        public int frame;
        public string kind; // move, press, release, motion (relative), unfocus
        public int x, y;
        public string key;

        public override string ToString() => kind switch
        {
            "move" => $"{frame}:move {x},{y}",
            "motion" => $"{frame}:motion {x},{y}",
            "unfocus" => $"{frame}:unfocus",
            _ => $"{frame}:{kind} {key}",
        };
    }

    static Step Move(int f, int x, int y) => new Step { frame = f, kind = "move", x = x, y = y };
    static Step Motion(int f, int x, int y) => new Step { frame = f, kind = "motion", x = x, y = y };
    static Step Press(int f) => new Step { frame = f, kind = "press", key = "Mouse0" };
    static Step Release(int f) => new Step { frame = f, kind = "release", key = "Mouse0" };

    // Screen 960x540; the camera at the origin looks along +z (60° vertical field of view): at
    // z = 10 a unit is 46.77 pixels, the centre is (480, 270).
    const int Ax = 199, Row1 = 176, Row2 = 364;
    public static readonly Step[] Steps =
    {
        // Two pointer moves the Editor driver calibrates with, then a click on empty space.
        Move(3, 200, 150), Move(5, 700, 400),
        Move(10, 900, 40), Press(11), Release(13),
        // Over A, press, drag on it and off it, release elsewhere.
        Move(20, Ax, Row1), Press(25), Move(28, Ax + 16, Row1 + 10), Move(31, 900, 40), Release(34),
        // A click on A.
        Move(40, Ax, Row1), Press(42), Release(44),
        // A trigger (queriesHitTriggers turned off at 55, on at 58).
        Move(50, 340, Row1),
        // A collider under a rigidbody, clicked.
        Move(62, 480, Row1), Press(64), Release(66),
        // Ignore Raycast layer; a layer outside the camera's event mask.
        Move(70, 620, Row1), Move(75, 761, Row1),
        // 2D: a collider, one under a Rigidbody2D, a 3D and a 2D collider at the same place.
        Move(80, Ax, Row2), Move(85, 340, Row2), Move(90, 480, Row2),
        // An object only the overlay camera sees.
        Move(95, 620, Row2),
        // Back over A (a render-texture camera is on from 103 to 108; A is inactive from 112 to 116).
        Move(100, Ax, Row1),
        // Pressed on A, its collider turned off (125), released (130).
        Press(122), Release(130),
        Move(135, 900, 40),
        // The Editor loses the focus for a while.
        new Step { frame = 140, kind = "unfocus" },
        // Relative pointer motion while the cursor is locked (200..225).
        Move(195, 600, 300), Motion(205, 10, 5), Motion(210, -20, 0), Motion(215, 0, -8), Motion(235, 6, 6),
    };

    /// <summary>Plays a step as real input (the Editor's driver); null in Unbound.</summary>
    public static Action<Step> driver;
    public static Action<int> frameHook;
    public static Action finished;

    readonly Dictionary<string, Renderer> m_Watched = new Dictionary<string, Renderer>();
    readonly Dictionary<string, bool> m_LastVisible = new Dictionary<string, bool>();
    Camera m_Main, m_Overlay, m_RT;
    GameObject m_A, m_V, m_Toggle;
    Collider m_ACollider;
    Transform m_P1, m_P2, m_C, m_G;
    CoroutineHost m_Host, m_DisabledHost, m_InactiveHost;
    GameObject m_Keep, m_KeepChild, m_Plain;
    int m_FixedThisFrame;
    double m_FirstFixed = -1, m_LastFixed = -1;
    bool m_Done, m_CaptureLogs, m_LastFocused;
    string m_LastCursor;

    struct FrameStart { }
    struct PreUpdateStart { }

    void Awake()
    {
        ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"Awake\"", "isFocused", Application.isFocused ? "true" : "false");
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        DontDestroyOnLoad(gameObject);
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ProbeDriver")).FirstOrDefault(t => t != null);
        type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i].type;
            var subs = loop.subSystemList[i].subSystemList.ToList();
            if (phase == typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                subs.Insert(0, new PlayerLoopSystem { type = typeof(FrameStart), updateDelegate = () => Phase = "early" });
            else if (phase == typeof(UnityEngine.PlayerLoop.PreUpdate))
                subs.Insert(0, new PlayerLoopSystem { type = typeof(PreUpdateStart), updateDelegate = () => Phase = "preupdate" });
            else continue;
            loop.subSystemList[i].subSystemList = subs.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
        Camera.onPreCull += c => Phase = "cull:" + c.name;
        Camera.onPostRender += c => Phase = "rendered:" + c.name;
        Application.logMessageReceived += OnLog;
        Build();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad() => ProbeLog.Add("init", "v", "\"BeforeSceneLoad\"", "isFocused", Application.isFocused ? "true" : "false");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad() => ProbeLog.Add("init", "v", "\"AfterSceneLoad\"");

    void OnEnable() => ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"OnEnable\"");

    void Start()
    {
        ProbeLog.Add("life", "o", ProbeLog.Q(name), "m", "\"Start\"");
        ProbeLog.Add("meta", "unity", ProbeLog.Q(Application.unityVersion), "steps", ProbeLog.Q(string.Join(" ", Steps.Select(s => s.ToString()))),
            "cameras", ProbeLog.Q(string.Join(",", Camera.allCameras.Select(c => c.name))), "screen", ProbeLog.V(new Vector2(Screen.width, Screen.height)));
        StartCoroutine(EndOfFrames());
    }

    void OnApplicationFocus(bool focus) => ProbeLog.Add("app", "o", ProbeLog.Q(name), "m", "\"Focus\"", "v", focus ? "true" : "false",
        "p", ProbeLog.Q(Phase), "isFocused", Application.isFocused ? "true" : "false");
    void OnApplicationPause(bool pause) => ProbeLog.Add("app", "o", ProbeLog.Q(name), "m", "\"Pause\"", "v", pause ? "true" : "false",
        "p", ProbeLog.Q(Phase), "isFocused", Application.isFocused ? "true" : "false");

    IEnumerator EndOfFrames()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            Phase = "eof";
        }
    }

    void OnLog(string message, string stackTrace, LogType type)
    {
        if (!m_CaptureLogs || message.StartsWith("[probe]") || message.Contains("Unbound")) return;
        ProbeLog.Add("log", "type", ProbeLog.Q(type.ToString()), "message", ProbeLog.Q(message.Split('\n')[0]));
    }

    static GameObject Cube(string name, Vector3 position, float size = 1.5f, int layer = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = layer;
        go.transform.position = position;
        go.transform.localScale = Vector3.one * size;
        return go;
    }

    static GameObject Box2D(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider2D>().size = new Vector2(1.5f, 1.5f);
        go.AddComponent<MouseTarget>();
        return go;
    }

    GameObject Watched(GameObject go)
    {
        if (go.GetComponent<Collider>() != null) DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<VisTarget>();
        m_Watched[go.name] = go.GetComponent<Renderer>();
        return go;
    }

    void Build()
    {
        // Cameras, made in an order that is not their depth order.
        m_RT = new GameObject("RT").AddComponent<Camera>();
        m_RT.depth = 2;
        m_RT.targetTexture = new RenderTexture(960, 540, 16);
        m_RT.clearFlags = CameraClearFlags.SolidColor;
        m_RT.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        m_RT.enabled = false;
        m_Overlay = new GameObject("Overlay").AddComponent<Camera>();
        m_Overlay.depth = 1;
        m_Overlay.clearFlags = CameraClearFlags.Depth;
        m_Overlay.cullingMask = 1 << 9;
        m_Main = new GameObject("Main").AddComponent<Camera>();
        m_Main.tag = "MainCamera";
        m_Main.depth = 0;
        m_Main.clearFlags = CameraClearFlags.SolidColor;
        m_Main.backgroundColor = new Color(0.3f, 0.3f, 0.35f, 1f);
        m_Main.cullingMask = ~((1 << 9) | (1 << 11));
        m_Main.eventMask = ~(1 << 8);
        var distances = new float[32];
        distances[12] = 40f;
        distances[13] = 35f;
        m_Main.layerCullDistances = distances;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Hard;
        sun.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Mouse targets.
        m_A = Cube("A", new Vector3(-6f, 2f, 10f));
        m_A.AddComponent<MouseTarget>();
        m_ACollider = m_A.GetComponent<Collider>();
        var trigger = Cube("Trigger", new Vector3(-3f, 2f, 10f));
        trigger.GetComponent<Collider>().isTrigger = true;
        trigger.AddComponent<MouseTarget>();
        var body = new GameObject("Body");
        body.transform.position = new Vector3(0f, 2f, 10f);
        body.AddComponent<Rigidbody>().isKinematic = true;
        body.AddComponent<MouseTarget>();
        var bodyChild = Cube("BodyChild", new Vector3(0f, 2f, 10f));
        bodyChild.transform.SetParent(body.transform, true);
        bodyChild.AddComponent<MouseTarget>();
        Cube("Ignore", new Vector3(3f, 2f, 10f), 1.5f, 2).AddComponent<MouseTarget>();
        Cube("Masked", new Vector3(6f, 2f, 10f), 1.5f, 8).AddComponent<MouseTarget>();
        Box2D("Box2D", new Vector3(-6f, -2f, 10f));
        var body2D = new GameObject("Body2D");
        body2D.transform.position = new Vector3(-3f, -2f, 10f);
        body2D.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        body2D.AddComponent<MouseTarget>();
        Box2D("Body2DChild", new Vector3(-3f, -2f, 10f)).transform.SetParent(body2D.transform, true);
        Cube("Mixed3D", new Vector3(0f, -2f, 10f)).AddComponent<MouseTarget>();
        Box2D("Mixed2D", new Vector3(0f, -2f, 10f));
        Cube("OverlayOnly", new Vector3(3f, -2f, 10f), 1.5f, 9).AddComponent<MouseTarget>();

        // Visibility.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -4f, 20f);
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        Watched(ground);
        m_V = Watched(Cube("Mover", new Vector3(-30f, 0f, 10f), 1f));
        Watched(Cube("Behind", new Vector3(0f, 0f, -10f), 1f));
        Watched(Cube("CulledLayer", new Vector3(2f, 0f, 10f), 1f, 11));
        Watched(Cube("ShadowOnly", new Vector3(-4f, -1f, 12f), 1f)).GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        Watched(Cube("Above", new Vector3(0f, 9.5f, 15f), 1f));
        Watched(Cube("AboveNoShadow", new Vector3(3f, 9.5f, 15f), 1f)).GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        Watched(Cube("Far", new Vector3(0f, 1f, 60f), 1f, 12));
        Watched(Cube("Side", new Vector3(25f, 1f, 30f), 1f, 13));
        m_Toggle = Watched(Cube("Toggle", new Vector3(4f, -1f, 12f), 1f));
    }

    void FixedUpdate()
    {
        m_FixedThisFrame++;
        if (m_FirstFixed < 0) m_FirstFixed = Time.fixedTimeAsDouble;
        m_LastFixed = Time.fixedTimeAsDouble;
    }

    void LateUpdate()
    {
        Phase = "late";
        if (Time.frameCount <= 3 || Time.frameCount == 400) ProbeLog.Add("lateUpdate");
    }

    void Update()
    {
        Phase = "update";
        int frame = Time.frameCount;
        if (frame <= 3) ProbeLog.Add("update");
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
        if (Application.isFocused != m_LastFocused)
        {
            m_LastFocused = Application.isFocused;
            ProbeLog.Add("isFocused", "v", m_LastFocused ? "true" : "false");
        }
        if (frame < VisibilityEnd) Visibility(frame);
        Actions(frame);
        if (frame >= 195 && frame <= 240) CursorState();
        if (frame <= 4 || (frame >= 366 && frame <= 380))
            ProbeLog.Add("time", "fixedSteps", m_FixedThisFrame.ToString(), "time", ProbeLog.F(Time.timeAsDouble),
                "fixedTime", ProbeLog.F(Time.fixedTimeAsDouble), "first", ProbeLog.F(m_FirstFixed), "last", ProbeLog.F(m_LastFixed),
                "deltaTime", ProbeLog.F(Time.deltaTime), "maximumDeltaTime", ProbeLog.F(Time.maximumDeltaTime));
        m_FixedThisFrame = 0;
        m_FirstFixed = m_LastFixed = -1;
        frameHook?.Invoke(frame);
        // Input played during this frame arrives with the next one.
        if (driver != null)
            foreach (var step in Steps)
                if (step.frame == frame + 1) driver(step);
    }

    void Visibility(int frame)
    {
        foreach (var pair in m_Watched)
        {
            bool visible = pair.Value != null && pair.Value.isVisible;
            if (m_LastVisible.TryGetValue(pair.Key, out var last) && last == visible) continue;
            m_LastVisible[pair.Key] = visible;
            ProbeLog.Add("isVisible", "o", ProbeLog.Q(pair.Key), "v", visible ? "true" : "false");
        }
    }

    void CursorState()
    {
        var position = (Vector2)Input.mousePosition;
        var line = ProbeLog.V(position) + Input.GetAxisRaw("Mouse X") + "," + Input.GetAxisRaw("Mouse Y") + Cursor.lockState + Cursor.visible;
        if (line == m_LastCursor) return;
        m_LastCursor = line;
        ProbeLog.Add("cursor", "pos", ProbeLog.V(position), "axis", ProbeLog.V(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"))),
            "lock", ProbeLog.Q(Cursor.lockState.ToString()), "visible", Cursor.visible ? "true" : "false");
    }

    void Actions(int frame)
    {
        switch (frame)
        {
            case 10:
            {
                // Objects made later, in the scene and kept across loads: where they come in the
                // application messages' order.
                new GameObject("LateListener").AddComponent<AppMessages>();
                var kept = new GameObject("LateDDOL");
                kept.AddComponent<AppMessages>();
                DontDestroyOnLoad(kept);
                break;
            }
            case 55: Physics.queriesHitTriggers = false; break;
            case 58: Physics.queriesHitTriggers = true; break;
            case 103:
                m_RT.enabled = true;
                ProbeLog.Add("cameras", "v", ProbeLog.Q(string.Join(",", Camera.allCameras.Select(c => c.name))));
                break;
            case 108: m_RT.enabled = false; break;
            case 112: m_A.SetActive(false); break;
            case 116: m_A.SetActive(true); break;
            case 125: m_ACollider.enabled = false; break;
            case 132: m_ACollider.enabled = true; break;
            case 200:
                Cursor.lockState = CursorLockMode.Locked;
                ProbeLog.Add("cursorSet", "lock", ProbeLog.Q(Cursor.lockState.ToString()), "visible", Cursor.visible ? "true" : "false");
                break;
            case 225: Cursor.lockState = CursorLockMode.Confined; break;
            case 228: Cursor.lockState = CursorLockMode.None; break;
            case 230: Cursor.visible = false; break;
            case 232: Cursor.visible = true; break;
            case 300: m_V.transform.position = new Vector3(8f, 0f, 10f); break;
            case 310: m_V.transform.position = new Vector3(-30f, 0f, 10f); break;
            case 320:
            {
                var distances = m_Main.layerCullDistances;
                distances[12] = 0f;
                m_Main.layerCullDistances = distances;
                break;
            }
            case 330: m_Main.layerCullSpherical = true; break;
            case 340: m_Toggle.GetComponent<Renderer>().enabled = false; break;
            case 345: m_Toggle.GetComponent<Renderer>().enabled = true; break;
            case 350: m_Toggle.SetActive(false); break;
            case 355: m_Toggle.SetActive(true); break;
            case 358:
            {
                m_P1 = new GameObject("P1").AddComponent<ParentLog>().transform;
                m_P2 = new GameObject("P2").AddComponent<ParentLog>().transform;
                m_C = new GameObject("C").AddComponent<ParentLog>().transform;
                m_C.SetParent(m_P1);
                m_G = new GameObject("G").AddComponent<ParentLog>().transform;
                m_G.SetParent(m_C);
                var h = new GameObject("H").AddComponent<ParentLog>().transform;
                h.SetParent(m_C);
                h.gameObject.SetActive(false);
                ProbeLog.Add("step", "v", "\"parents built\"");
                break;
            }
            case 360:
                ProbeLog.Add("step", "v", "\"C to P2\"");
                m_C.SetParent(m_P2);
                ProbeLog.Add("step", "v", "\"C to P2 again\"");
                m_C.SetParent(m_P2);
                ProbeLog.Add("step", "v", "\"C to root\"");
                m_C.SetParent(null);
                ProbeLog.Add("step", "v", "\"G to P1\"");
                m_G.SetParent(m_P1, false);
                ProbeLog.Add("step", "v", "\"P2 inactive, C to P2\"");
                m_P2.gameObject.SetActive(false);
                m_C.SetParent(m_P2);
                ProbeLog.Add("step", "v", "\"done\"");
                break;
            case 370:
                System.Threading.Thread.Sleep(700);
                break;
            case 374:
                Time.maximumDeltaTime = 0.1f;
                System.Threading.Thread.Sleep(300);
                break;
            case 377: Time.maximumDeltaTime = 0.33333334f; break;
            case 379:
            {
                m_CaptureLogs = true;
                m_Host = new GameObject("Host").AddComponent<CoroutineHost>();
                m_DisabledHost = new GameObject("DisabledHost").AddComponent<CoroutineHost>();
                m_DisabledHost.enabled = false;
                m_InactiveHost = new GameObject("InactiveHost").AddComponent<CoroutineHost>();
                m_InactiveHost.gameObject.SetActive(false);
                break;
            }
            case 380:
                Start("enabled", m_Host);
                Start("disabled", m_DisabledHost);
                Start("inactive", m_InactiveHost);
                break;
            case 381:
                // A running coroutine whose behaviour is disabled.
                m_Host.enabled = false;
                ProbeLog.Add("step", "v", "\"host disabled\"");
                break;
            case 386:
            {
                // SendMessage to an inactive object, a disabled behaviour; BroadcastMessage from
                // an inactive root.
                var sleeper = new GameObject("Sleeper");
                sleeper.AddComponent<PingLog>();
                sleeper.SetActive(false);
                var disabled = new GameObject("DisabledPing");
                disabled.AddComponent<PingLog>().enabled = false;
                var root = new GameObject("InactiveRoot");
                root.AddComponent<PingLog>();
                var child = new GameObject("ActiveSelfChild");
                child.AddComponent<PingLog>();
                child.transform.SetParent(root.transform);
                root.SetActive(false);
                ProbeLog.Add("step", "v", "\"SendMessage inactive\"");
                sleeper.SendMessage("Ping", SendMessageOptions.DontRequireReceiver);
                ProbeLog.Add("step", "v", "\"SendMessage disabled\"");
                disabled.SendMessage("Ping", SendMessageOptions.DontRequireReceiver);
                ProbeLog.Add("step", "v", "\"BroadcastMessage inactive root\"");
                root.BroadcastMessage("Ping", SendMessageOptions.DontRequireReceiver);
                ProbeLog.Add("step", "v", "\"done\"");
                break;
            }
            case 388:
            {
                foreach (var flags in new[] { HideFlags.DontSaveInEditor, HideFlags.DontSaveInBuild, HideFlags.DontUnloadUnusedAsset, HideFlags.HideAndDontSave, HideFlags.HideInHierarchy })
                    new GameObject("Flags" + (int)flags) { hideFlags = flags };
                m_Keep = new GameObject("Keep") { hideFlags = HideFlags.DontSave };
                m_KeepChild = new GameObject("KeepChild");
                m_KeepChild.transform.SetParent(m_Keep.transform);
                m_Plain = new GameObject("Plain");
                var editorOnly = new GameObject("NotEditable") { hideFlags = HideFlags.NotEditable };
                ProbeLog.Add("step", "v", "\"load Second\"", "keepScene", ProbeLog.Q(m_Keep.scene.name));
                SceneManager.LoadScene("Second");
                break;
            }
            case 389:
            case 391:
            {
                var notEditable = GameObject.Find("NotEditable");
                var survivors = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name.StartsWith("Flags")).Select(g => g.name).OrderBy(n => n);
                ProbeLog.Add("flags", "kept", ProbeLog.Q(string.Join(",", survivors)));
                ProbeLog.Add("dontSave", "keep", (m_Keep != null) ? "true" : "false", "keepChild", (m_KeepChild != null) ? "true" : "false",
                    "plain", (m_Plain != null) ? "true" : "false", "notEditable", (notEditable != null) ? "true" : "false",
                    "keepScene", ProbeLog.Q(m_Keep != null ? m_Keep.scene.name : null), "active", ProbeLog.Q(SceneManager.GetActiveScene().name));
                break;
            }
            case 400:
                ProbeLog.Add("step", "v", "\"Debug.Break\"");
                Debug.Break();
                break;
            case 401:
            case 402:
                ProbeLog.Add("tick");
                break;
            case 405:
                Application.wantsToQuit += () =>
                {
                    ProbeLog.Add("wantsToQuit", "p", ProbeLog.Q(Phase));
                    return true;
                };
                Application.quitting += () => ProbeLog.Add("quitting", "p", ProbeLog.Q(Phase));
                ProbeLog.Add("step", "v", "\"Application.Quit\"");
                Application.Quit();
                ProbeLog.Add("step", "v", "\"after Application.Quit\"");
                break;
            case 406:
            case 407:
                ProbeLog.Add("tick");
                break;
            case 410:
                m_CaptureLogs = false;
                break;
        }
    }

    void Start(string label, CoroutineHost host)
    {
        try
        {
            var routine = host.StartCoroutine(host.Routine(label));
            ProbeLog.Add("startCoroutine", "label", ProbeLog.Q(label), "result", routine != null ? "\"coroutine\"" : "null");
        }
        catch (Exception e)
        {
            ProbeLog.Add("startCoroutine", "label", ProbeLog.Q(label), "exception", ProbeLog.Q(e.GetType().Name));
        }
    }

    void OnApplicationQuit() => ProbeLog.Add("app", "o", ProbeLog.Q(name), "m", "\"Quit\"");
}
