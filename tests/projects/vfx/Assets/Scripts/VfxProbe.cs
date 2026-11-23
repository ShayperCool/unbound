// The probe runs in the Editor only: a player build of the project (the AssetBundle the authoring
// step builds compiles one) has none of the editor classes it uses.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

/// <summary>
/// Records what the Visual Effect module answers about the probe's graphs (see README.md):
/// keyed values, one JSON line each, into VFX_PROBE_OUT/values.jsonl, then exits Play mode.
/// </summary>
public sealed class VfxProbe : MonoBehaviour
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] k_Graphs = { "Empty", "Basic", "Exposed", "Exposed2", "Events", "Attributes", "Attributes2" };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("VfxProbe").AddComponent<VfxProbe>();
    }

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");
    void Rec(string key, bool value) => Rec(key, value ? "true" : "false");
    void Rec(string key, long value) => Rec(key, value.ToString(CultureInfo.InvariantCulture));
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string V(Vector4 v) => F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w);

    /// <summary>Runs an action and records what it logged (type and text) and threw.</summary>
    void Logged(string key, Action action)
    {
        m_Logs.Clear();
        Application.LogCallback handler = (text, stack, type) => m_Logs.Add(type + ": " + text);
        Application.logMessageReceived += handler;
        string thrown = "";
        try { action(); }
        // The runtime words a null argument's message its own way: its parameter is the fact.
        // Another argument exception: the first line of its message, then its parameter.
        catch (ArgumentNullException e) { thrown = e.GetType().Name + " " + e.ParamName; }
        catch (ArgumentException e) { thrown = e.GetType().Name + ": " + e.Message.Split('\n')[0] + (e.ParamName == null ? "" : " [" + e.ParamName + "]"); }
        catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message; }
        Application.logMessageReceived -= handler;
        Rec(key + "/logs", string.Join(" | ", m_Logs));
        Rec(key + "/thrown", thrown);
    }

    void Start()
    {
        try
        {
            Tables();
            foreach (var name in k_Graphs) Asset(name);
            foreach (var name in new[] { "Plain", "Overridden", "Events", "NoAsset" }) Component("scene/" + name, GameObject.Find(name).GetComponent<VisualEffect>());
            Scripted();
            Attributes();
            RuntimeData();
            foreach (var filter in new[] { "t:VisualEffectAsset", "t:VisualEffectObject", "t:VisualEffectSubgraphOperator", "t:Shader", "t:ComputeShader", "t:Material" })
                Rec("find/" + filter, string.Join(", ", AssetDatabase.FindAssets(filter, new[] { "Assets/Editor", "Assets/Graphs", "Assets/Objects", "Assets/Scenes", "Assets/Scripts" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)));
            Rec("manager/fixedTimeStep", F(VFXManager.fixedTimeStep));
            Rec("manager/maxDeltaTime", F(VFXManager.maxDeltaTime));
            BeginSimulation();
            BeginDrawing();
            BeginCameras();
            BeginSoftParticles();
            BeginOutputEvents();
            BeginOperations();
            Bundled();
            SpawnerStates(m_Running[0].Effect);
            AffineInverse();
            BeginMeshOperations();
            BeginNoise();
            m_EventReads = Effect("event reads", "Assets/OpGraphs/OpsEvent.vfx", new Vector3(0f, 10f, 0f));
            BeginShapes();
            BeginSkinned();
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfx probe failed: " + e);
            Finish();
        }
    }

    /// <summary>Where in a frame a property set from script is read back: one effect given a
    /// value in the first frame, one in the second.</summary>
    VisualEffect m_Timed, m_TimedLater;

    void ReadTimed(string where)
    {
        if (m_Timed == null || m_Frame > 5) return;
        Rec("sim/override timing/f" + m_Frame.ToString("000") + " " + where, F(m_Timed.GetFloat("A")) + " " + (m_TimedLater != null ? F(m_TimedLater.GetFloat("A")) : "none"));
    }

    void LateUpdate()
    {
        ReadTimed("2 late update");
        m_Phase = "after late update";
    }

    System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (m_Running.Count != 0)
        {
            yield return wait;
            m_Phase = "end of frame";
            ReadTimed("3 end of frame");
            Shoot();
        }
    }

    // --------------------------------------------------------------------------- drawing

    /// <summary>An effect that is drawn, far from the others, in front of a camera of its own
    /// that renders into a small texture: the frames named are saved as images.</summary>
    sealed class Stage
    {
        public string Key;
        public VisualEffect Effect;
        public Camera Camera;
        public int[] Frames;
    }

    readonly List<Stage> m_Stages = new List<Stage>();
    Texture2D m_Shot;

    /// <summary>Gives the sphere of the `collide` control its frame.</summary>
    static void SetBall(VisualEffect effect, Vector3 position, Vector3 angles, Vector3 scale)
    {
        effect.SetVector3("Ball_position", position);
        effect.SetVector3("Ball_angles", angles);
        effect.SetVector3("Ball_scale", scale);
    }

    void Draw(string key, string graph, params int[] frames)
    {
        var origin = new Vector3(1000f + 100f * m_Stages.Count, 0f, 0f);
        var go = new GameObject("draw " + key);
        go.transform.position = origin;
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/DrawGraphs/" + graph + ".vfx");
        var eye = new GameObject("eye " + key);
        eye.transform.position = origin + new Vector3(0f, 0f, -10f);
        var camera = eye.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1.5f;
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        camera.targetTexture = new RenderTexture(64, 64, 24);
        m_Stages.Add(new Stage { Key = key, Effect = effect, Camera = camera, Frames = frames });
    }

    void BeginDrawing()
    {
        Draw("opaque", "DrawOpaque", 3, 10);
        Draw("additive", "DrawAdditive", 12, 30);
        Draw("many", "DrawMany", 5);
        Draw("textured", "DrawTextured", 5);
        // Over each particle's life by a curve and a gradient; the curve is another from frame 25.
        Draw("over life", "DrawOverLife", 12, 24, 40, 55, 70);
        Draw("hdr", "DrawHdr", 40);
        // Curves that loop after their last key only, sampled just below their end.
        Draw("loop end", "DrawLoopEnd", 20);
    }

    /// <summary>The shape controls, after every other stage (a stage's place comes from how
    /// many there are before it).</summary>
    void BeginShapes()
    {
        // A cone of two equal radii.
        Draw("cylinder", "DrawCylinder", 5);
        // Killed inside a sphere whose frame changes: flat from frame 6, uneven from frame 14,
        // even and elsewhere from frame 22.
        Draw("collide", "DrawCollide", 4, 12, 20, 28);
    }


    // --------------------------------------------------------------------------- cameras

    /// <summary>A camera that renders into a texture of its own, cleared to the stages' grey.</summary>
    static Camera Eye(string name, Vector3 position, Quaternion rotation, int width, int height)
    {
        var eye = new GameObject(name);
        eye.transform.SetPositionAndRotation(position, rotation);
        var camera = eye.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        camera.targetTexture = new RenderTexture(width, height, 24);
        return camera;
    }

    /// <summary>An opaque, unlit board of a colour (a quad without a collider).</summary>
    static void Board(Vector3 position, Quaternion rotation, Vector3 scale, Color color)
    {
        var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(board.GetComponent<Collider>());
        board.transform.SetPositionAndRotation(position, rotation);
        board.transform.localScale = scale;
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", color);
        board.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static VisualEffect Effect(string name, string path, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
        return effect;
    }

    void AddStage(string key, VisualEffect effect, Camera camera, params int[] frames) =>
        m_Stages.Add(new Stage { Key = key, Effect = effect, Camera = camera, Frames = frames });

    /// <summary>The cameras the camera controls look through: the main camera (the scene's
    /// camera gives up its tag), an orthographic one that becomes the main camera for a while,
    /// and the scene's.</summary>
    Camera m_MainEye, m_OrthoEye, m_SceneEye;
    readonly List<string> m_ManagerLogs = new List<string>();

    void ManagerLog(string text, string stack, LogType type)
    {
        if (text.Contains("main camera") || text.Contains("bufferType")) m_ManagerLogs.Add("f" + m_Frame.ToString("000") + " " + type + ": " + text);
    }

    /// <summary>The camera controls (README.md, "Cameras"): what an effect's graph reads of the
    /// main camera, which buffers of which camera the module asks for, what the camera entry
    /// points answer.</summary>
    void BeginCameras()
    {
        Rec("camera/activateVFX", typeof(VFXManager).GetProperty("activateVFX", Any).GetValue(null).ToString());
        var origin = new Vector3(3000f, 0f, 0f);
        m_SceneEye = GameObject.Find("Main Camera").GetComponent<Camera>();
        m_MainEye = Eye("camera main", origin + new Vector3(0f, 0f, -10f), Quaternion.identity, 64, 48);
        m_MainEye.fieldOfView = 40f;
        m_MainEye.nearClipPlane = 0.5f;
        m_MainEye.farClipPlane = 30f;
        m_SceneEye.tag = "Untagged";
        m_MainEye.tag = "MainCamera";
        m_OrthoEye = Eye("camera ortho", origin + new Vector3(1f, 2f, -12f), Quaternion.Euler(5f, -3f, 0f), 40, 40);
        m_OrthoEye.orthographic = true;
        m_OrthoEye.orthographicSize = 3.5f;
        m_OrthoEye.nearClipPlane = 1f;
        m_OrthoEye.farClipPlane = 40f;
        // The pipeline hands an effect the buffers of a camera that keeps a history (one with
        // the pipeline's camera data).
        m_MainEye.GetUniversalAdditionalCameraData();
        m_OrthoEye.GetUniversalAdditionalCameraData();
        // A board the main camera sees slanted.
        Board(origin + new Vector3(0f, 0f, 2f), Quaternion.Euler(0f, 30f, 0f), new Vector3(14f, 10f, 1f), new Color(0.25f, 0.3f, 0.35f, 1f));
        Effect("camera values", "Assets/CameraGraphs/CamValues.vfx", origin);
        Effect("camera depth", "Assets/CameraGraphs/CamDepth.vfx", origin);
        Application.logMessageReceived += ManagerLog;

        var other = Eye("camera destroyed", origin, Quaternion.identity, 8, 8);
        DestroyImmediate(other.gameObject);
        Logged("camera/api/IsCameraBufferNeeded null", () => VFXManager.IsCameraBufferNeeded(null));
        Logged("camera/api/IsCameraBufferNeeded destroyed", () => VFXManager.IsCameraBufferNeeded(other));
        Logged("camera/api/SetCameraBuffer null", () => VFXManager.SetCameraBuffer(null, VFXCameraBufferTypes.Depth, null, 0, 0, 1, 1));
        Logged("camera/api/PrepareCamera null", () => VFXManager.PrepareCamera(null));
        Logged("camera/api/PrepareCamera destroyed", () => VFXManager.PrepareCamera(other));
        Logged("camera/api/ProcessCameraCommand null", () => VFXManager.ProcessCameraCommand(null, null, new VFXCameraXRSettings { viewTotal = 1, viewCount = 1 }, default(CullingResults)));
        Logged("camera/api/PrepareCamera", () => VFXManager.PrepareCamera(m_OrthoEye));
        Logged("camera/api/ProcessCameraCommand", () => VFXManager.ProcessCameraCommand(m_OrthoEye, null, new VFXCameraXRSettings { viewTotal = 1, viewCount = 1 }, default(CullingResults)));
    }

    /// <summary>Each frame: which buffers each camera is asked for; the main camera changes
    /// (its lens at frame 6, none from frame 8, the orthographic one from frame 10, the first
    /// again from frame 12); buffers handed over the wrong way at frame 4.</summary>
    void CameraFrame()
    {
        if (m_Frame <= 20)
            Rec("camera/needed/f" + m_Frame.ToString("000"), "main " + VFXManager.IsCameraBufferNeeded(m_MainEye) + ", ortho " + VFXManager.IsCameraBufferNeeded(m_OrthoEye)
                + ", scene " + VFXManager.IsCameraBufferNeeded(m_SceneEye));
        if (m_Frame == 4)
        {
            var buffer = new RenderTexture(16, 16, 0);
            Logged("camera/api/SetCameraBuffer two types", () => VFXManager.SetCameraBuffer(m_MainEye, VFXCameraBufferTypes.Depth | VFXCameraBufferTypes.Color, buffer, 0, 0, 10, 10));
            Logged("camera/api/SetCameraBuffer none", () => VFXManager.SetCameraBuffer(m_MainEye, VFXCameraBufferTypes.None, buffer, 0, 0, 11, 11));
            Logged("camera/api/SetCameraBuffer not asked for", () => VFXManager.SetCameraBuffer(m_MainEye, VFXCameraBufferTypes.Color, buffer, 1, 2, 12, 9));
            Logged("camera/api/SetCameraBuffer other camera", () => VFXManager.SetCameraBuffer(m_OrthoEye, VFXCameraBufferTypes.Depth, buffer, 0, 0, 13, 13));
        }
        if (m_Frame == 6)
        {
            m_MainEye.fieldOfView = 55f;
            m_MainEye.nearClipPlane = 0.7f;
            m_MainEye.farClipPlane = 25f;
            m_MainEye.aspect = 1.25f;
            m_MainEye.transform.SetPositionAndRotation(new Vector3(3000.5f, 0.25f, -9f), Quaternion.Euler(2f, -3f, 1f));
        }
        if (m_Frame == 8) m_MainEye.tag = "Untagged";
        if (m_Frame == 10) m_OrthoEye.tag = "MainCamera";
        if (m_Frame == 12)
        {
            m_OrthoEye.tag = "Untagged";
            m_MainEye.tag = "MainCamera";
        }
        if (m_Frame == 21)
        {
            // What the script's block read when the spawn context played and in its first
            // updates; what the module logged about the main camera and its buffers.
            foreach (var line in CameraInputs.Logs.TryGetValue("camera values", out var log) ? log : new List<string>())
            {
                var parts = line.Split(' ');
                foreach (var part in parts)
                    if (part.Contains("=")) Rec("camera/values/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), part.Substring(part.IndexOf('=') + 1));
            }
            Rec("camera/values/lines", CameraInputs.Logs.TryGetValue("camera values", out var all) ? all.Count : -1);
            Rec("camera/manager logs", string.Join(" | ", m_ManagerLogs));
            Application.logMessageReceived -= ManagerLog;
        }
    }

    /// <summary>Particles added to what is behind that fade where they come near what is
    /// opaque behind them (soft particles), among boards at several depths: seen by a camera
    /// with a depth texture, an orthographic one with a depth texture, and one without.</summary>
    void BeginSoftParticles()
    {
        var origin = new Vector3(4000f, 0f, 0f);
        var effect = Effect("soft", "Assets/CameraGraphs/SoftAdditive.vfx", origin);
        Board(origin + new Vector3(-0.9f, 0f, 0.1f), Quaternion.Euler(0f, 60f, 0f), new Vector3(1.6f, 2.4f, 1f), new Color(0.5f, 0.1f, 0.1f, 1f));
        Board(origin + new Vector3(0.8f, -0.2f, -0.1f), Quaternion.Euler(-50f, 0f, 20f), new Vector3(2f, 1.6f, 1f), new Color(0.1f, 0.1f, 0.5f, 1f));
        Board(origin + new Vector3(0f, 0.9f, 0.4f), Quaternion.identity, new Vector3(4f, 0.6f, 1f), new Color(0.1f, 0.4f, 0.1f, 1f));
        Camera Seen(string key, Vector3 position, bool depthTexture)
        {
            var camera = Eye("eye " + key, origin + position, Quaternion.LookRotation(-position), 64, 64);
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 20f;
            camera.GetUniversalAdditionalCameraData().requiresDepthOption = depthTexture ? CameraOverrideOption.On : CameraOverrideOption.Off;
            AddStage(key, effect, camera, 10, 20);
            return camera;
        }
        Seen("soft", new Vector3(0f, 0f, -6f), true);
        var ortho = Seen("soft ortho", new Vector3(2.5f, 1.5f, -6f), true);
        ortho.orthographic = true;
        ortho.orthographicSize = 1.6f;
        Seen("soft no depth", new Vector3(-0.5f, -0.3f, -6f), false);
    }

    // ---------------------------------------------------------------------- output events

    /// <summary>Where in the frame the probe is (an output event says where it arrives).</summary>
    string m_Phase = "start";
    readonly Dictionary<VisualEffect, VFXEventAttribute> m_LastEventAttribute = new Dictionary<VisualEffect, VFXEventAttribute>();
    readonly Dictionary<string, int> m_EventsThisFrame = new Dictionary<string, int>();
    VisualEffect m_EventsPaused, m_EventsLate, m_EventsSeen;
    VFXEventAttribute m_KeptEventAttribute;
    readonly List<string> m_ListenerLogs = new List<string>();

    static string EventName(int id) =>
        id == Shader.PropertyToID("Tick") ? "Tick" : id == Shader.PropertyToID("Burst") ? "Burst" : id == Shader.PropertyToID("Never") ? "Never"
        : id == Shader.PropertyToID("Fast") ? "Fast" : "id " + id;

    void Listen(VisualEffect effect) => effect.outputEventReceived += args => OutputEventReceived(effect, args);

    /// <summary>Each event an effect raises in frames 1 to 20: where in the frame, its name, the
    /// attributes it carries (with which of them its layout has) and whether it is the same
    /// attribute set as the effect's previous event.</summary>
    void OutputEventReceived(VisualEffect effect, VFXOutputEventArgs args)
    {
        if (m_Frame > 20) return;
        var a = args.eventAttribute;
        string key = effect.name + "/f" + m_Frame.ToString("000");
        m_EventsThisFrame.TryGetValue(key, out int index);
        m_EventsThisFrame[key] = index + 1;
        m_LastEventAttribute.TryGetValue(effect, out var previous);
        m_LastEventAttribute[effect] = a;
        string Value(string name, Func<string> read) => a.HasFloat(name) || a.HasVector3(name) ? read() : "none";
        Rec("events/" + key + "/" + index, m_Phase + " " + EventName(args.nameId)
            + " spawnCount=" + Value("spawnCount", () => F(a.GetFloat("spawnCount")))
            + " color=" + Value("color", () => V(a.GetVector3("color")))
            + " size=" + Value("size", () => F(a.GetFloat("size")))
            + " lifetime=" + Value("lifetime", () => F(a.GetFloat("lifetime")))
            + (previous == null ? " first" : ReferenceEquals(previous, a) ? " same" : " other"));
        if (effect == m_EventsSeen) m_KeptEventAttribute = a;
    }

    /// <summary>Output events (README.md, "Output events"): an effect seen by the camera, one
    /// behind it, one paused from frame 5 to frame 12, one listened to from frame 10 only, one
    /// whose first listener throws once, one whose only listener removes itself.</summary>
    void BeginOutputEvents()
    {
        const string Graph = "Assets/EventGraphs/OutEvents.vfx";
        m_EventsSeen = Effect("events", Graph, new Vector3(0f, -2f, 0f));
        var behind = Effect("events behind", Graph, new Vector3(0f, 0f, -20f));
        m_EventsPaused = Effect("events paused", Graph, new Vector3(1.5f, -2f, 0f));
        m_EventsLate = Effect("events late", Graph, new Vector3(-1.5f, -2f, 0f));
        // A listener that throws at the first event of frame 7 (a frame of several), before the
        // probe's own listener of the same effect.
        var throwing = Effect("events throwing", Graph, new Vector3(3f, -2f, 0f));
        bool thrown = false;
        throwing.outputEventReceived += args =>
        {
            if (m_Frame != 7 || thrown) return;
            thrown = true;
            throw new InvalidOperationException("probe listener");
        };
        Application.logMessageReceived += (text, stack, type) =>
        {
            if ((text.Contains("probe listener") || text.Contains("NullReferenceException")) && m_Frame <= 20)
                m_ListenerLogs.Add("f" + m_Frame.ToString("000") + " " + m_Phase + " " + type + ": " + text.Split('\n')[0]);
        };
        // An effect whose only listener removes itself at the first Tick of frame 3 (the first
        // event of a step that raises three).
        var once = Effect("events once", Graph, new Vector3(4.5f, -2f, 0f));
        Action<VFXOutputEventArgs> oneShot = null;
        oneShot = args =>
        {
            OutputEventReceived(once, args);
            if (m_Frame == 3 && EventName(args.nameId) == "Tick") once.outputEventReceived -= oneShot;
        };
        once.outputEventReceived += oneShot;
        Listen(m_EventsSeen);
        Listen(behind);
        Listen(m_EventsPaused);
        Listen(throwing);
        var names = new List<string>();
        m_EventsSeen.GetOutputEventNames(names);
        Rec("events/names", string.Join(", ", names));
    }

    void OutputEventFrame()
    {
        if (m_Frame == 5) m_EventsPaused.pause = true;
        if (m_Frame == 12) m_EventsPaused.pause = false;
        if (m_Frame == 10) Listen(m_EventsLate);
        if (m_Frame == 21) Rec("events/listener logs", string.Join(" | ", m_ListenerLogs));
        if (m_Frame == 21 && m_KeptEventAttribute != null)
            Rec("events/kept attribute", "spawnCount=" + F(m_KeptEventAttribute.GetFloat("spawnCount")) + " color=" + V(m_KeptEventAttribute.GetVector3("color")));
    }

    // ------------------------------------------------------------------------ operations

    VisualEffect m_Operations;
    GraphicsBuffer m_OperationsBuffer, m_OperationsBufferB;
    Texture2D m_OperationsImage;
    RenderTexture m_OperationsTarget;

    /// <summary>The operation controls (README.md, "Operations"): an effect of OpsValues whose
    /// script block reads what the operators give. From frame 3 one step a frame changes its
    /// colours, its frame and its camera; its texture and buffer change, are released or
    /// destroyed while set, and are set to none; a buffer released before it is set.</summary>
    void BeginOperations() => m_Operations = Effect("operations", "Assets/OpGraphs/OpsValues.vfx", new Vector3(0f, 4f, 0f));

    /// <summary>A camera for one step of the controls: its frame, whether it is orthographic,
    /// its field of view (radians), near and far planes, orthographic size, aspect ratio and lens
    /// shift, and the viewport point taken into the world.</summary>
    readonly struct OperationEye
    {
        public readonly Matrix4x4 Frame;
        public readonly bool Ortho;
        public readonly float Fov, Near, Far, Size, Aspect;
        public readonly Vector2 Shift;
        public readonly Vector3 Viewport;

        public OperationEye(Matrix4x4 frame, bool ortho, float fov, float near, float far, float size, float aspect, Vector2 shift, Vector3 viewport) =>
            (Frame, Ortho, Fov, Near, Far, Size, Aspect, Shift, Viewport) = (frame, ortho, fov, near, far, size, aspect, shift, viewport);
    }

    void OperationFrame()
    {
        if (m_Frame == 3)
        {
            m_OperationsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 7, 12);
            m_Operations.SetGraphicsBuffer("Buffer", m_OperationsBuffer);
            m_Operations.SetVector4("A", new Vector4(-2f, 0.5f, 9f, -1f));
        }
        int step = m_Frame - 3;
        if (step >= 0 && step < 12)
        {
            var pos = new Vector3(10f, 20f, 30f);
            var rot = Quaternion.Euler(30f, -120f, 200f);
            var sheared = Matrix4x4.TRS(pos, rot, new Vector3(2f, 0.5f, 3f));
            sheared.m01 += 0.75f;
            sheared.m12 -= 0.4f;
            var nan = Matrix4x4.TRS(pos, rot, Vector3.one);
            nan.m11 = float.NaN;
            // Hues past either end of the circle, saturation and value past one, a hue on a
            // sixth's edge and one of no saturation; colours of every sixth, past one and below zero.
            var hues = new[]
            {
                new Vector3(0.25f, 0.6f, 0.9f), new Vector3(0.45f, 0.6f, 0.9f), new Vector3(0.55f, 0.6f, 0.9f), new Vector3(0.9f, 0.6f, 0.9f),
                new Vector3(1.25f, 0.6f, 0.9f), new Vector3(-0.25f, 0.6f, 0.9f), new Vector3(0.5f, 1.5f, 2f), new Vector3(1f / 6f, 1f, 1f),
                new Vector3(3.71f, 0.6f, 0.9f), new Vector3(0.3f, 0f, 0.5f), new Vector3(1f, 0.6f, 0.9f), new Vector3(-1.3f, 0.6f, 0.9f),
            };
            var tints = new[]
            {
                new Vector3(0.3f, 0.8f, 0.6f), new Vector3(0.3f, 0.6f, 0.8f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(2f, 0.5f, 4f),
                new Vector3(-0.5f, 0.2f, 0.1f), new Vector3(0.7f, 0.7f, 0.2f), new Vector3(0.2f, 0.7f, 0.7f), new Vector3(0.7f, 0.2f, 0.7f),
                new Vector3(0.6f, 0.3f, 0.8f), new Vector3(0.8f, 0.6f, 0.3f), new Vector3(-0.2f, -0.5f, -0.1f), new Vector3(0.1f, 0.9f, 0.4f),
            };
            // Mirrored, flattened, sheared and turned to the poles; frames without a rotation
            // (of zeros, of a zero or tiny scale) and one holding a NaN.
            var frames = new[]
            {
                Matrix4x4.TRS(pos, rot, new Vector3(-2f, 0.5f, 3f)), Matrix4x4.TRS(pos, rot, new Vector3(2f, 0f, 3f)), Matrix4x4.zero, sheared,
                Matrix4x4.TRS(pos, rot, new Vector3(-2f, -0.5f, 3f)), Matrix4x4.TRS(pos, rot, Vector3.zero), Matrix4x4.TRS(pos, Quaternion.Euler(90f, 45f, 0f), Vector3.one),
                Matrix4x4.TRS(pos, Quaternion.Euler(-90f, 10f, 20f), new Vector3(2f, 2f, 2f)), Matrix4x4.TRS(pos, rot, new Vector3(1e-4f, 1e-4f, 1e-4f)),
                Matrix4x4.TRS(pos, rot, new Vector3(2f, 0.5f, -3f)), Matrix4x4.TRS(pos, rot, new Vector3(0f, 0f, 3f)), nan,
            };
            // Perspective and orthographic cameras, scaled and turned, with planes and sizes at
            // and past their limits: projections without an inverse among them (a near plane of
            // zero, equal planes, a size or an aspect of zero).
            var eyeFrame = Matrix4x4.TRS(new Vector3(1f, 2f, -10f), Quaternion.Euler(10f, 20f, 5f), Vector3.one);
            var turned = Matrix4x4.TRS(new Vector3(-3f, 0.5f, 4f), Quaternion.Euler(90f, -30f, 0f), new Vector3(2f, 2f, 2f));
            var eyes = new[]
            {
                new OperationEye(eyeFrame, false, 1.1f, 0.3f, 1000f, 5f, 16f / 9f, new Vector2(0.1f, -0.2f), new Vector3(0.25f, 0.75f, 6f)),
                new OperationEye(eyeFrame, true, 1.1f, 0.3f, 1000f, 5f, 16f / 9f, Vector2.zero, new Vector3(0.9f, 0.1f, 3f)),
                new OperationEye(turned, false, 0.3f, 0.01f, 50000f, 5f, 0.5f, Vector2.zero, new Vector3(0.5f, 0.5f, 100f)),
                new OperationEye(turned, true, 1f, -10f, 10f, 0.5f, 1.25f, new Vector2(0.5f, 0.5f), new Vector3(0f, 1f, -2f)),
                new OperationEye(eyeFrame, false, 2f, 1f, 1f, 5f, 1f, Vector2.zero, new Vector3(0.3f, 0.6f, 2f)),
                new OperationEye(eyeFrame, true, 1f, 0.3f, 1000f, 0f, 1f, Vector2.zero, new Vector3(0.3f, 0.6f, 2f)),
                new OperationEye(eyeFrame, false, Mathf.PI, 0.3f, 1000f, 5f, 1f, Vector2.zero, new Vector3(0.3f, 0.6f, 2f)),
                new OperationEye(sheared, false, 0.8f, 0.5f, 200f, 5f, 2.5f, new Vector2(1f, 1f), new Vector3(1.5f, -0.5f, -4f)),
                new OperationEye(eyeFrame, false, 1f, 0.3f, 1000f, 5f, 0f, Vector2.zero, new Vector3(0.3f, 0.6f, 2f)),
                new OperationEye(eyeFrame, false, 1f, 0f, 1000f, 5f, 1f, Vector2.zero, new Vector3(0.3f, 0.6f, 2f)),
                new OperationEye(turned, true, 1f, 50f, 2f, 3f, 0.75f, Vector2.zero, new Vector3(0.2f, 0.4f, 7f)),
                new OperationEye(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, -1f, 1f)), false, 0.5f, 0.1f, 10f, 5f, 1f, new Vector2(-0.3f, 0f), new Vector3(0.75f, 0.25f, 1f)),
            };
            m_Operations.SetVector3("Hue", hues[step]);
            m_Operations.SetVector4("Tint", new Vector4(tints[step].x, tints[step].y, tints[step].z, 1f));
            m_Operations.SetMatrix4x4("N", frames[step]);
            var eye = eyes[step];
            m_Operations.SetMatrix4x4("EyeFrame", eye.Frame);
            m_Operations.SetBool("EyeOrtho", eye.Ortho);
            m_Operations.SetFloat("EyeFov", eye.Fov);
            m_Operations.SetFloat("EyeNear", eye.Near);
            m_Operations.SetFloat("EyeFar", eye.Far);
            m_Operations.SetFloat("EyeSize", eye.Size);
            m_Operations.SetFloat("EyeAspect", eye.Aspect);
            m_Operations.SetVector2("EyeShift", eye.Shift);
            m_Operations.SetVector3("Viewport", eye.Viewport);
        }
        // A texture's size: a new one, a render texture, the first again and then destroyed
        // while set. A buffer's count: released while set, set to none, another, one released
        // before it is set.
        if (m_Frame == 5) m_Operations.SetTexture("Image", m_OperationsImage = new Texture2D(16, 4));
        if (m_Frame == 9) m_Operations.SetTexture("Image", m_OperationsTarget = new RenderTexture(32, 8, 0));
        if (m_Frame == 9) m_OperationsBuffer.Release();
        if (m_Frame == 11) m_Operations.SetTexture("Image", m_OperationsImage);
        if (m_Frame == 11) m_Operations.SetGraphicsBuffer("Buffer", null);
        if (m_Frame == 12) DestroyImmediate(m_OperationsImage);
        if (m_Frame == 13) m_Operations.SetGraphicsBuffer("Buffer", m_OperationsBufferB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 4));
        if (m_Frame == 15)
        {
            var released = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 9, 4);
            released.Release();
            m_Operations.SetGraphicsBuffer("Buffer", released);
        }
        if (m_Frame != 20) return;
        foreach (var line in OpsInputs.Logs.TryGetValue("operations", out var log) ? log : new List<string>())
        {
            var parts = line.Split(' ');
            foreach (var part in parts)
                if (part.Contains("=")) Rec("operations/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), part.Substring(part.IndexOf('=') + 1));
        }
        Rec("operations/lines", OpsInputs.Logs.TryGetValue("operations", out var all) ? all.Count : -1);
        m_OperationsBufferB?.Release();
        if (m_OperationsTarget != null) m_OperationsTarget.Release();
    }

    // ------------------------------------------------------------------------------ noise

    VisualEffect m_Noise;

    /// <summary>A float's bits, or <c>nan</c> for any NaN (the sign of one is not compared).</summary>
    static string N(float f) => float.IsNaN(f) ? "nan" : F(f);

    static string NoiseText(object o) => o switch
    {
        float f => N(f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        Vector2 a => N(a.x) + "," + N(a.y),
        Vector3 b => N(b.x) + "," + N(b.y) + "," + N(b.z),
        Vector4 c => N(c.x) + "," + N(c.y) + "," + N(c.z) + "," + N(c.w),
        _ => o.ToString(),
    };

    /// <summary>Coordinates at and around what the noises' lattices repeat over, huge, tiny,
    /// infinite and absent.</summary>
    static readonly float[] k_NoiseEdges =
    {
        0f, 1f, -1f, 0.5f, -0.5f, 70f, 70.5f, 71f, 68f, 68.5f, 69f, 142f, -71f, -69f, 1e6f, -1e6f, 1e9f, 3.0000002f, 2.9999998f,
        float.NaN, float.PositiveInfinity, 1e-30f, -1e-30f,
    };

    const int NoiseCalls = 140, NoiseNear = 60, NoiseFar = 90, NoiseFarthest = 110;

    /// <summary>The noise controls (README.md, "Noise"). The noise the graph compiler folds
    /// over constants, called for a fixed sequence of coordinates and settings: near, far and
    /// farthest coordinates, then the edges; every fifth call with a frequency, a count of
    /// octaves, a persistence and a lacunarity of its own (negative and zero among them). And an
    /// effect of OpsNoise whose script block reads what the noise operators give.</summary>
    void BeginNoise()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.VFX.VFXExpressionNoise")).FirstOrDefault(t => t != null);
        var methods = type == null ? new MethodInfo[0] : type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name.StartsWith("Generate") && !m.Name.EndsWith("_Injected")).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
        Rec("noise/api/functions", string.Join(" ", methods.Select(m => m.ReturnType.Name + ":" + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")")));
        uint seed = 12345;
        float Next(float range)
        {
            seed = seed * 1664525u + 1013904223u;
            return ((seed >> 8) / 16777216f * 2f - 1f) * range;
        }
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            for (int n = 0; n < NoiseCalls; n++)
            {
                float range = n < NoiseNear ? 8f : n < NoiseFar ? 300f : 5000f;
                bool edges = n >= NoiseFarthest, plain = n % 5 != 0;
                float Coordinate() => edges ? k_NoiseEdges[(int)(Mathf.Abs(Next(1f)) * k_NoiseEdges.Length) % k_NoiseEdges.Length] : Next(range);
                var args = new object[parameters.Length];
                for (int k = 0; k < args.Length; k++)
                {
                    var kind = parameters[k].ParameterType;
                    string name = parameters[k].Name;
                    if (k == 0) args[k] = kind == typeof(float) ? Coordinate() : kind == typeof(Vector2) ? new Vector2(Coordinate(), Coordinate()) : (object)new Vector3(Coordinate(), Coordinate(), Coordinate());
                    else if (kind == typeof(int)) args[k] = plain ? (n % 97 == 3 ? -1 : 1) : (int)(Mathf.Abs(Next(1f)) * 6f);
                    else if (name == "frequency") args[k] = plain ? 1f : Next(3f);
                    else if (name == "persistence") args[k] = plain ? 0.5f : Next(1.5f);
                    else if (name == "lacunarity") args[k] = plain ? 2f : Next(3f);
                    // The Voronoi noise's warp and smoothness: within zero to one, at their ends and past them.
                    else if (name == "smoothness") args[k] = new[] { 0f, 1f, 2f, 0.5f, -1f, 0.25f, 0.9f }[n % 7];
                    else args[k] = n % 3 == 0 ? Next(2f) : Mathf.Abs(Next(1f));
                }
                string result;
                try { result = NoiseText(method.Invoke(null, args)); }
                catch (Exception e) { result = "throws " + (e.InnerException ?? e).GetType().Name; }
                Rec("noise/api/" + method.Name + "/" + n.ToString("000"), string.Join(" ", args.Select(NoiseText)) + " = " + result);
            }
        }
        m_Noise = Effect("noise", "Assets/OpGraphs/OpsNoise.vfx", new Vector3(0f, 12f, 0f));
    }

    /// <summary>What the effect of the noise controls is given, one step a frame from frame 3:
    /// a coordinate on a line, in a plane and in space, a frequency, a count of octaves, a
    /// roughness (the persistence) and a lacunarity. The operators' slots clamp the last three
    /// (octaves within one to eight, roughness within zero to one, lacunarity from zero) before
    /// the noise reads them.</summary>
    static readonly (float Line, Vector2 Plane, Vector3 Point, float Frequency, int Octaves, float Roughness, float Lacunarity)[] k_NoiseSteps =
    {
        (-3.3331528f, new Vector2(2.1938133f, -2.454937f), new Vector3(-7.822835f, -3.9788828f, -1.3851948f), 1f, 1, 0.5f, 2f),
        (7.9678164f, new Vector2(-2.4826965f, 6.8431616f), new Vector3(1.1180258f, -5.0225363f, -4.881197f), 1f, 1, 0.5f, 2f),
        (0.25f, new Vector2(0.75f, 0.125f), new Vector3(0.5f, 0.5f, 0.5f), 2.5f, 4, 0.5f, 2f),
        (0f, new Vector2(0f, 0f), new Vector3(0f, 0f, 0f), 1f, 8, 0.75f, 1.9f),
        // The lattices repeat over 71 cells (one and two coordinates) and over 69 (three).
        (70.5f, new Vector2(70.5f, -0.5f), new Vector3(68.5f, 68.25f, -68.75f), 1f, 2, 0.5f, 2f),
        (71f, new Vector2(142f, -71f), new Vector3(69f, -69f, 138f), 1f, 1, 0.5f, 2f),
        (-1234.567f, new Vector2(4321.5f, -987.25f), new Vector3(2500.75f, -3999.5f, 123.456f), 0.37f, 3, 0.3f, 2.7f),
        // A negative frequency; a negative roughness and lacunarity, and a lacunarity of zero
        // (the operators' slots keep a roughness within zero to one and a lacunarity from zero).
        (3.7f, new Vector2(-1.3f, 4.4f), new Vector3(2.2f, 0.6f, -5.1f), -1.7f, 5, -0.6f, -1.3f),
        (3.7f, new Vector2(-1.3f, 4.4f), new Vector3(2.2f, 0.6f, -5.1f), 1.2f, 4, 1f, 0f),
        // No octaves and a negative count: the operators' slots keep the count within one to
        // eight, so these rows are a single octave's.
        (3.7f, new Vector2(-1.3f, 4.4f), new Vector3(2.2f, 0.6f, -5.1f), 1f, 0, 0.5f, 2f),
        (3.7f, new Vector2(-1.3f, 4.4f), new Vector3(2.2f, 0.6f, -5.1f), 1f, -2, 0.5f, 2f),
        // More octaves than the operators' slots keep: eight are summed.
        (3.7f, new Vector2(-1.3f, 4.4f), new Vector3(2.2f, 0.6f, -5.1f), 1f, 12, 0.5f, 2f),
        // Huge, infinite and absent coordinates.
        (1e9f, new Vector2(1e6f, -1e9f), new Vector3(1e9f, 3f, -1e6f), 1f, 1, 0.5f, 2f),
        (float.PositiveInfinity, new Vector2(float.NaN, 1f), new Vector3(1f, float.NegativeInfinity, 2f), 1f, 1, 0.5f, 2f),
    };

    void NoiseFrame()
    {
        int step = m_Frame - 3;
        if (step >= 0 && step < k_NoiseSteps.Length)
        {
            var given = k_NoiseSteps[step];
            m_Noise.SetFloat("Line", given.Line);
            m_Noise.SetVector2("Plane", given.Plane);
            m_Noise.SetVector3("Point", given.Point);
            m_Noise.SetFloat("Frequency", given.Frequency);
            m_Noise.SetInt("Octaves", given.Octaves);
            m_Noise.SetFloat("Roughness", given.Roughness);
            m_Noise.SetFloat("Lacunarity", given.Lacunarity);
        }
        if (m_Frame != 20) return;
        foreach (var line in OpsInputs.Logs.TryGetValue("noise", out var log) ? log : new List<string>())
        {
            var parts = line.Split(' ');
            foreach (var part in parts)
                if (part.Contains("=")) Rec("noise/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), NoiseBits(part.Substring(part.IndexOf('=') + 1)));
        }
        Rec("noise/lines", OpsInputs.Logs.TryGetValue("noise", out var all) ? all.Count : -1);
    }

    /// <summary>The floats of a row of the script block, each NaN written <c>nan</c>.</summary>
    static string NoiseBits(string row) => string.Join(",", row.Split(',').Select(bits => N(BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToInt32(bits, 16)), 0))));

    // ------------------------------------------------------------------ mesh operations

    VisualEffect m_MeshOperations;
    Mesh m_MeshMixed, m_MeshWide, m_MeshLocked, m_Unreadable, m_Made, m_Held, m_VerticesOnly, m_ShapeA;
    Texture3D m_Volume;
    Texture2DArray m_Layers;
    CubemapArray m_Cubes;
    RenderTexture m_VolumeTarget;
    readonly List<GraphicsBuffer> m_MeshBuffers = new List<GraphicsBuffer>();

    /// <summary>The mesh controls (README.md, "Mesh operations"): an effect of OpsMesh whose
    /// script block reads what the mesh and texture operators give, and drawn effects whose
    /// particles take their places from meshes and their colours from a buffer or a mesh.</summary>
    void BeginMeshOperations()
    {
        m_MeshMixed = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/MeshMixed.asset");
        m_MeshWide = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/MeshWide.asset");
        m_MeshLocked = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/MeshLocked.asset");
        m_ShapeA = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/ShapeA.asset");
        // Copies: one only held by an effect whose graph reads nothing of it (Exposed's Shape),
        // one only read by its vertices (the `mesh stride` drawing).
        m_Held = Instantiate(m_MeshMixed);
        m_VerticesOnly = Instantiate(m_MeshMixed);
        foreach (var (name, mesh) in new[] { ("mixed", m_MeshMixed), ("wide", m_MeshWide), ("locked", m_MeshLocked), ("shape a", m_ShapeA), ("held", m_Held), ("vertices only", m_VerticesOnly) })
            Rec("mesh operations/" + name + "/before", MeshState(mesh));
        Effect("mesh holder", "Assets/Graphs/Exposed.vfx", new Vector3(0f, 8f, 0f)).SetMesh("Shape", m_Held);
        m_MeshOperations = Effect("mesh operations", "Assets/OpGraphs/OpsMesh.vfx", new Vector3(0f, 6f, 0f));
        // Colours by a buffer of vectors (the stride the graph reads), by one of another stride
        // (none read), from the vertices and from points on the triangles of each mesh.
        var colours = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 16);
        colours.SetData(new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f), new Vector4(0f, 0f, 1f, 1f), new Vector4(1f, 1f, 0f, 1f), new Vector4(0.5f, 0.5f, 1f, 1f) });
        var narrow = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 12);
        narrow.SetData(new[] { Vector3.one, Vector3.one, Vector3.one, Vector3.one, Vector3.one });
        m_MeshBuffers.Add(colours);
        m_MeshBuffers.Add(narrow);
        foreach (var (key, graph, mesh, buffer) in new[]
                 {
                     ("mesh", "DrawMesh", m_MeshMixed, colours), ("mesh stride", "DrawMesh", m_VerticesOnly, narrow),
                     ("mesh wide", "DrawMesh", m_MeshWide, colours), ("mesh locked", "DrawMesh", m_MeshLocked, colours),
                     ("mesh surface", "DrawMeshSurface", m_MeshMixed, null), ("mesh surface wide", "DrawMeshSurface", m_MeshWide, null),
                 })
        {
            Draw(key, graph, 5);
            var effect = m_Stages[m_Stages.Count - 1].Effect;
            effect.SetMesh("Mesh", mesh);
            if (buffer != null) effect.SetGraphicsBuffer("Buffer", buffer);
        }
    }

    /// <summary>The upper bones of the skinned columns (README.md, "Skinned meshes"), turned in
    /// each update.</summary>
    readonly List<Transform> m_SkinnedBones = new List<Transform>();
    readonly List<(string Key, SkinnedMeshRenderer Renderer)> m_Skins = new List<(string, SkinnedMeshRenderer)>();

    /// <summary>A column of two by five vertices, two units high, skinned to two bones: the
    /// lower half to the first, the upper half to the second, the middle row to both. Its first
    /// texture coordinates run across and up the column. In one stream, or (<paramref
    /// name="streams"/>) positions and normals in the first stream, the texture coordinates in a
    /// second (the bone weights where the mesh puts them).</summary>
    static Mesh SkinnedColumn(bool streams, Matrix4x4[] bindposes)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 2; column++)
            {
                vertices.Add(new Vector3(column == 0 ? -0.3f : 0.3f, -1f + 0.5f * row, 0f));
                normals.Add(Vector3.back);
                uvs.Add(new Vector2(column, row / 4f));
                weights.Add(row == 2
                    ? new BoneWeight { boneIndex0 = 1, weight0 = 0.5f, boneIndex1 = 0, weight1 = 0.5f }
                    : new BoneWeight { boneIndex0 = row < 2 ? 0 : 1, weight0 = 1f });
            }
        var mesh = new Mesh { name = streams ? "SkinnedStreams" : "SkinnedColumn" };
        if (streams)
            mesh.SetVertexBufferParams(vertices.Count,
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float32, 3, 0),
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal, UnityEngine.Rendering.VertexAttributeFormat.Float32, 3, 0),
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.TexCoord0, UnityEngine.Rendering.VertexAttributeFormat.Float32, 2, 1));
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        var triangles = new List<int>();
        for (int row = 0; row < 4; row++)
        {
            int at = row * 2;
            triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
        }
        mesh.SetTriangles(triangles, 0);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>The skinned mesh renderer controls (README.md, "Skinned meshes"): for each, a
    /// column skinned to two bones stands beside the effect, turned and moved from the stage, its
    /// root bone below it turned too; its upper bone turns in each update.</summary>
    void BeginSkinned()
    {
        var dark = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        dark.SetColor("_BaseColor", new Color(0.25f, 0.25f, 0.25f, 1f));
        foreach (var (key, graph, streams) in new[]
                 {
                     ("skinned", "DrawSkinned", false), ("skinned streams", "DrawSkinned", true), ("skinned world", "DrawSkinnedWorld", false),
                     ("skinned previous", "DrawSkinnedPrevious", false), ("skinned surface", "DrawSkinnedSurface", false),
                     ("skinned values", "DrawSkinnedValues", false),
                 })
        {
            Draw(key, graph, 4, 8, 16);
            var effect = m_Stages[m_Stages.Count - 1].Effect;
            var holder = new GameObject("skin " + key);
            holder.transform.SetPositionAndRotation(effect.transform.position + new Vector3(0.4f, 0.1f, 0f), Quaternion.Euler(0f, 0f, 15f));
            var lower = new GameObject("lower").transform;
            lower.SetParent(holder.transform, false);
            lower.localPosition = new Vector3(0f, -1f, 0f);
            lower.localRotation = Quaternion.Euler(0f, 0f, -10f);
            var upper = new GameObject("upper").transform;
            upper.SetParent(lower, false);
            upper.localPosition = new Vector3(0f, 1f, 0f);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            var bones = new[] { lower, upper };
            var bindposes = bones.Select(b => b.worldToLocalMatrix * holder.transform.localToWorldMatrix).ToArray();
            renderer.sharedMesh = SkinnedColumn(streams, bindposes);
            renderer.bones = bones;
            renderer.rootBone = lower;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterial = dark;
            m_SkinnedBones.Add(upper);
            m_Skins.Add((key, renderer));
            if (key == "skinned") Effect("skinned operations", "Assets/OpGraphs/OpsSkinned.vfx", new Vector3(0f, 12f, 0f)).SetSkinnedMeshRenderer("Skin", renderer);
            Rec("skinned/" + key + "/target before", ((int)renderer.vertexBufferTarget).ToString(CultureInfo.InvariantCulture));
            effect.SetSkinnedMeshRenderer("Skin", renderer);
        }
    }

    /// <summary>Turns the skinned columns' upper bones by six degrees an update.</summary>
    void SkinnedFrame()
    {
        foreach (var bone in m_SkinnedBones) bone.localRotation = Quaternion.Euler(0f, 0f, 6f * m_Frame);
        // The skin buffer as a script reads it in an update: its layout and its first vertices
        // (the last frame's skin), and the renderer's buffer target once the effect sampled it.
        if (m_Frame == 20)
        {
            foreach (var line in OpsInputs.Logs.TryGetValue("skinned operations", out var log) ? log : new List<string>())
            {
                var parts = line.Split(' ');
                foreach (var part in parts)
                    if (part.Contains("=")) Rec("skinned operations/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), part.Substring(part.IndexOf('=') + 1));
            }
            Rec("skinned operations/lines", OpsInputs.Logs.TryGetValue("skinned operations", out var all) ? all.Count : -1);
        }
        if (m_Frame != 8 && m_Frame != 16) return;
        foreach (var (key, renderer) in m_Skins)
        {
            Rec("skinned/" + key + "/target f" + m_Frame.ToString("000"), ((int)renderer.vertexBufferTarget).ToString(CultureInfo.InvariantCulture));
            using var buffer = renderer.GetVertexBuffer();
            if (buffer == null)
            {
                Rec("skinned/" + key + "/buffer f" + m_Frame.ToString("000"), "null");
                continue;
            }
            var words = new float[Math.Min(buffer.count * buffer.stride / 4, 24)];
            buffer.GetData(words);
            Rec("skinned/" + key + "/buffer f" + m_Frame.ToString("000"), "count=" + buffer.count + " stride=" + buffer.stride + " " + string.Join(" ", words.Select(F)));
        }
    }

    static string MeshState(Mesh mesh) => "readable=" + (mesh.isReadable ? 1 : 0) + " vertexBufferTarget=" + (int)mesh.vertexBufferTarget + " indexBufferTarget=" + (int)mesh.indexBufferTarget;

    /// <summary>From frame 3 one step a frame: the vertex, triangle and index read change and
    /// fall outside the mesh, the mesh becomes another of other formats, a copy whose data was
    /// given up, one destroyed while it is set, one made by script and then changed, one without
    /// read/write access; the volume and the arrays are set, the volume is replaced by a volume
    /// render texture, and all three are destroyed while they are set.</summary>
    void MeshOperationFrame()
    {
        if (m_MeshOperations == null) return;
        var effect = m_MeshOperations;
        switch (m_Frame)
        {
            case 3:
                effect.SetMesh("Mesh", m_MeshMixed);
                effect.SetTexture("Volume", m_Volume = new Texture3D(4, 2, 3, TextureFormat.RGBA32, false));
                effect.SetTexture("Layers", m_Layers = new Texture2DArray(2, 2, 5, TextureFormat.RGBA32, false));
                effect.SetTexture("Cubes", m_Cubes = new CubemapArray(4, 2, TextureFormat.RGBA32, false));
                break;
            case 4:
                effect.SetUInt("Vertex", 7);
                effect.SetUInt("Triangle", 5);
                effect.SetUInt("Index", 20);
                effect.SetVector2("Bary", new Vector2(0.6f, 0.7f));
                break;
            case 5:
                effect.SetMesh("Mesh", m_MeshWide);
                effect.SetUInt("Vertex", 1);
                effect.SetUInt("Triangle", 2);
                effect.SetUInt("Index", 7);
                effect.SetVector2("Bary", new Vector2(0.1f, 0.8f));
                m_VolumeTarget = new RenderTexture(8, 4, 0) { dimension = TextureDimension.Tex3D, volumeDepth = 6 };
                m_VolumeTarget.Create();
                effect.SetTexture("Volume", m_VolumeTarget);
                break;
            case 6:
                m_Unreadable = Instantiate(m_MeshMixed);
                m_Unreadable.UploadMeshData(true);
                effect.SetMesh("Mesh", m_Unreadable);
                break;
            case 7:
                // None cannot be set: a mesh and textures destroyed while they are set stand
                // for none.
                Logged("mesh operations/set none", () => effect.SetMesh("Mesh", null));
                Logged("mesh operations/set no texture", () => effect.SetTexture("Volume", null));
                var gone = Instantiate(m_MeshMixed);
                effect.SetMesh("Mesh", gone);
                DestroyImmediate(gone);
                var goneVolume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false);
                effect.SetTexture("Volume", goneVolume);
                DestroyImmediate(goneVolume);
                DestroyImmediate(m_Layers);
                DestroyImmediate(m_Cubes);
                break;
            case 8:
                m_Made = new Mesh { vertices = new[] { new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f) }, triangles = new[] { 0, 2, 1 } };
                effect.SetMesh("Mesh", m_Made);
                effect.SetUInt("Vertex", 1);
                effect.SetUInt("Triangle", 0);
                effect.SetUInt("Index", 1);
                break;
            case 9:
                m_Made.vertices = new[] { new Vector3(-1f, -2f, -3f), new Vector3(-4f, -5f, -6f), new Vector3(-7f, -8f, -9f) };
                break;
            case 10:
                m_Made.colors32 = new[] { new Color32(10, 20, 30, 40), new Color32(50, 60, 70, 80), new Color32(90, 100, 110, 120) };
                break;
            case 11:
                effect.SetMesh("Mesh", m_MeshLocked);
                effect.SetUInt("Vertex", 3);
                effect.SetUInt("Triangle", 2);
                effect.SetUInt("Index", 5);
                break;
        }
        if (m_Frame != 20) return;
        foreach (var (name, mesh) in new[] { ("mixed", m_MeshMixed), ("wide", m_MeshWide), ("locked", m_MeshLocked), ("shape a", m_ShapeA), ("held", m_Held), ("vertices only", m_VerticesOnly), ("unreadable", m_Unreadable), ("made", m_Made) })
            Rec("mesh operations/" + name + "/after", mesh == null ? "none" : MeshState(mesh));
        foreach (var line in OpsInputs.Logs.TryGetValue("mesh operations", out var log) ? log : new List<string>())
        {
            var parts = line.Split(' ');
            foreach (var part in parts)
                if (part.Contains("=")) Rec("mesh operations/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), part.Substring(part.IndexOf('=') + 1));
        }
        Rec("mesh operations/lines", OpsInputs.Logs.TryGetValue("mesh operations", out var all) ? all.Count : -1);
        foreach (var buffer in m_MeshBuffers) buffer.Release();
        if (m_VolumeTarget != null) m_VolumeTarget.Release();
    }

    // --------------------------------------------------- event attributes read in a spawn context

    VisualEffect m_EventReads;

    /// <summary>The controls of event attributes read in a spawn context (README.md): from frame
    /// 3 the exposed lifetime changes; at frame 4 the second spawn context starts with a short
    /// lifetime of its own; at frame 5 the effect plays again with attributes of its own; at
    /// frame 6 the seed and alive change; at frame 7 the second spawn context's lifetime
    /// changes; at frame 8 it stops; at frame 10 it plays again without attributes; at frame 12
    /// the second context starts again without attributes. The rows are written at frame 20.</summary>
    void EventReadFrame()
    {
        if (m_EventReads == null) return;
        var effect = m_EventReads;
        switch (m_Frame)
        {
            case 3:
                effect.SetFloat("L", 3.5f);
                break;
            case 4:
                // Loops of a tenth of a second: several end before frame 20.
                var loop = effect.CreateVFXEventAttribute();
                loop.SetFloat("lifetime", 0.1f);
                effect.SendEvent("Loop", loop);
                break;
            case 5:
                var payload = effect.CreateVFXEventAttribute();
                payload.SetFloat("lifetime", 11f);
                payload.SetVector3("color", new Vector3(9f, 8f, 7f));
                payload.SetUint("seed", 77);
                payload.SetBool("alive", false);
                effect.SendEvent("OnPlay", payload);
                break;
            case 6:
                effect.SetUInt("S", 12);
                effect.SetBool("A", false);
                break;
            case 7:
                effect.SetFloat("L2", 0.5f);
                break;
            case 8:
                effect.SendEvent("OnStop");
                break;
            case 10:
                effect.SendEvent("OnPlay");
                break;
            case 12:
                effect.SendEvent("Loop");
                break;
        }
        if (m_Frame != 20) return;
        foreach (var line in OpsInputs.Logs.TryGetValue("event reads", out var log) ? log : new List<string>())
        {
            var parts = line.Split(' ');
            foreach (var part in parts)
                if (part.Contains("=")) Rec("event reads/" + parts[0] + "/" + part.Substring(0, part.IndexOf('=')), part.Substring(part.IndexOf('=') + 1));
        }
        Rec("event reads/lines", OpsInputs.Logs.TryGetValue("event reads", out var all) ? all.Count : -1);
    }

    // ---------------------------------------------------------------------------- bundle

    VisualEffect m_Bundled;

    /// <summary>The asset of a graph as an AssetBundle holds it (built by the authoring step,
    /// without the graph file): what loads, what it answers, and an effect that uses it.</summary>
    void Bundled()
    {
        var bundle = AssetBundle.LoadFromFile(Path.Combine(Application.streamingAssetsPath, "VfxProbe/vfxgraphs"));
        Rec("bundle/loaded", bundle != null);
        if (bundle == null) return;
        Rec("bundle/names", string.Join(", ", bundle.GetAllAssetNames().OrderBy(n => n, StringComparer.Ordinal)));
        var asset = bundle.LoadAsset<VisualEffectAsset>("Assets/BundleGraphs/Bundled.vfx");
        Rec("bundle/asset", asset == null ? "null" : asset.GetType().Name + " " + asset.name);
        if (asset == null) return;
        Rec("bundle/asset/is the project's", asset == AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/BundleGraphs/Bundled.vfx"));
        var events = new List<string>();
        asset.GetEvents(events);
        Rec("bundle/asset/events", string.Join(", ", events));
        var exposed = new List<VFXExposedProperty>();
        asset.GetExposedProperties(exposed);
        Rec("bundle/asset/exposed", string.Join(", ", exposed.Select(e => e.type.Name + " " + e.name)));
        var systems = new List<string>();
        var go = new GameObject("bundled");
        go.transform.position = new Vector3(0f, 0f, 0f);
        m_Bundled = go.AddComponent<VisualEffect>();
        m_Bundled.resetSeedOnPlay = false;
        m_Bundled.startSeed = 7;
        m_Bundled.visualEffectAsset = asset;
        Rec("bundle/effect/asset", m_Bundled.visualEffectAsset == asset);
        m_Bundled.GetSystemNames(systems);
        Rec("bundle/effect/systems", string.Join(", ", systems));
    }

    /// <summary>At the end of a frame: the stages whose image this frame is, with what the
    /// effect says of itself.</summary>
    void Shoot()
    {
        // The size curve of `over life` is another from frame 25, one of a single key from
        // frame 45, and from frame 60 one whose keys start before zero and end after one.
        foreach (var stage in m_Stages)
        {
            if (stage.Key != "over life") continue;
            if (m_Frame == 25) stage.Effect.SetAnimationCurve("Size", new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0.02f)));
            if (m_Frame == 45) stage.Effect.SetAnimationCurve("Size", new AnimationCurve(new Keyframe(0.5f, 0.25f)));
            if (m_Frame == 60) stage.Effect.SetAnimationCurve("Size", new AnimationCurve(new Keyframe(-0.37f, 0.05f), new Keyframe(1.3f, 0.4f)));
        }
        foreach (var stage in m_Stages)
        {
            if (stage.Key != "collide") continue;
            // Flat along an axis that is turned: a column of the frame is zero, no row is.
            if (m_Frame == 6) SetBall(stage.Effect, Vector3.zero, new Vector3(0f, 0f, 40f), new Vector3(0f, 1f, 1f));
            if (m_Frame == 14) SetBall(stage.Effect, new Vector3(-0.2f, 0.1f, 0f), new Vector3(0f, 0f, 40f), new Vector3(1.2f, 0.3f, 1f));
            if (m_Frame == 22) SetBall(stage.Effect, new Vector3(0.9f, 0.9f, 0f), new Vector3(20f, 0f, 0f), new Vector3(0.6f, 0.6f, 0.6f));
        }
        if (m_Bundled != null && (m_Frame == 3 || m_Frame == 20))
            Rec("bundle/effect/f" + m_Frame.ToString("000"), "culled=" + (m_Bundled.culled ? 1 : 0) + " awake=" + (m_Bundled.HasAnySystemAwake() ? 1 : 0) + " renderer " + RendererState(m_Bundled));
        foreach (var stage in m_Stages)
        {
            if (Array.IndexOf(stage.Frames, m_Frame) < 0) continue;
            string name = "draw-" + stage.Key + "-f" + m_Frame.ToString("000");
            Rec("draw/" + stage.Key + "/f" + m_Frame.ToString("000"), "alive=" + stage.Effect.aliveParticleCount + " culled=" + (stage.Effect.culled ? 1 : 0)
                + " awake=" + (stage.Effect.HasAnySystemAwake() ? 1 : 0) + " renderer " + RendererState(stage.Effect));
            var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
            if (string.IsNullOrEmpty(dir)) continue;
            if (m_Shot == null) m_Shot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var active = RenderTexture.active;
            RenderTexture.active = stage.Camera.targetTexture;
            m_Shot.ReadPixels(new Rect(0f, 0f, 64f, 64f), 0, 0);
            m_Shot.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), m_Shot.EncodeToPNG());
        }
    }

    void Update()
    {
        if (m_Running.Count == 0) return;
        try
        {
            m_Frame++;
            CameraInputs.Frame = m_Frame;
            m_Phase = "update";
            CameraFrame();
            OutputEventFrame();
            OperationFrame();
            MeshOperationFrame();
            NoiseFrame();
            EventReadFrame();
            SkinnedFrame();
            Simulate();
            if (m_Frame < k_LastFrame) return;
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfx probe failed: " + e);
        }
        m_Running.Clear();
        Finish();
    }

    static string RendererState(VisualEffect effect)
    {
        var renderer = effect.GetComponent<Renderer>();
        return renderer == null ? "none" : "enabled=" + (renderer.enabled ? 1 : 0) + " visible=" + (renderer.isVisible ? 1 : 0);
    }

    /// <summary>The graphs imported while the probe plays. None must be: an import in the
    /// middle of the first frames makes every effect anew from what was saved (the packages
    /// import a new project's graphs again in the Editor's first update, which is why the
    /// capture lets the Editor settle before it plays: Assets/Editor/ProbeRunner.cs).</summary>
    sealed class ImportsWhilePlaying : AssetPostprocessor
    {
        public static readonly List<string> Graphs = new List<string>();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!EditorApplication.isPlaying) return;
            foreach (var path in imported)
                if (path.EndsWith(".vfx", StringComparison.Ordinal)) Graphs.Add(path);
        }
    }

    void Finish()
    {
        Rec("capture/graphs imported while playing", string.Join(", ", ImportsWhilePlaying.Graphs.OrderBy(p => p, StringComparer.Ordinal)));
        // A capture with such an import is not one to publish: it stays incomplete.
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfx probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }

    // ------------------------------------------------------------------------ simulation

    /// <summary>An effect the simulation controls run: made by the probe in front of the camera
    /// (or behind it), with a fixed seed, recorded each frame until its last.</summary>
    sealed class Running
    {
        public string Key;
        public VisualEffect Effect;
        public int Last;
        /// <summary>What the frame does to the effect before it is recorded.</summary>
        public Action<int, VisualEffect> Act;
        /// <summary>Whether the alive count is recorded each frame (a system whose particles
        /// die reads its count back from the GPU some frames late: recorded where it is settled).</summary>
        public bool Alive = true;
        /// <summary>Event attributes of the first spawn system recorded each frame.</summary>
        public string[] Attributes;
        /// <summary>A float property read back each frame.</summary>
        public string Property;
    }

    const int k_LastFrame = 170;
    readonly List<Running> m_Running = new List<Running>();
    readonly List<string> m_Names = new List<string>();
    int m_Frame;

    Running Run(string key, string graph, int last, uint seed = 7, bool behind = false, Action<int, VisualEffect> act = null)
    {
        var go = new GameObject("sim " + key);
        // In front of the scene's camera (at z = -10, looking along +z), or behind it.
        go.transform.position = new Vector3(0f, 0f, behind ? -30f : 0f);
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = seed;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>((graph.StartsWith("Sim") ? "Assets/SimGraphs/" : "Assets/Graphs/") + graph + ".vfx");
        var running = new Running { Key = "sim/" + key, Effect = effect, Last = last, Act = act };
        m_Running.Add(running);
        return running;
    }

    void BeginSimulation()
    {
        Run("rate", "SimRate", 44);
        Run("rate delta", "SimRateDelta", 44);
        Run("rate exact", "SimRateExact", 44);
        Run("rate prewarm", "SimRatePrewarm", 6);
        Run("burst", "SimBurst", 36);
        Run("random", "SimRandom", 44);
        Run("random other seed", "SimRandom", 44, seed: 8);
        Run("loop", "SimLoop", 40);
        Run("loop random", "SimLoopRandom", 44);
        Run("behind", "SimRate", 12, behind: true);
        Run("behind no cull", "SimRateNoCull", 12, behind: true);
        Run("behind bounds only", "SimRateBoundsOnly", 12, behind: true);
        // Stopped, played, paused, advanced a frame, at another play rate, stepped by hand and
        // started over.
        Run("control", "SimRate", 40, act: (frame, effect) =>
        {
            switch (frame)
            {
                case 6: effect.Stop(); break;
                case 10: effect.Play(); break;
                case 14: effect.pause = true; break;
                case 17: effect.AdvanceOneFrame(); break;
                case 20: effect.pause = false; break;
                case 22: effect.playRate = 2f; break;
                case 25: effect.playRate = 1f; break;
                case 26: effect.Simulate(0.1f, 3); break;
                case 29: effect.Simulate(0.05f, 0); break;
                case 30: effect.Reinit(); break;
                case 34: effect.Simulate(0.05f, 2); effect.Stop(); break;
                case 37: effect.playRate = 0f; break;
            }
        });
        // Custom events start and stop the first spawner; an event the graph does not have.
        var events = Run("events", "Events", 16, act: (frame, effect) =>
        {
            switch (frame)
            {
                case 3: effect.SendEvent("Fire"); break;
                case 6: effect.SendEvent("Cease"); break;
                case 9: effect.SendEvent("Fire"); effect.SendEvent("Cease"); break;
                case 12: effect.SendEvent("Cease"); effect.SendEvent("Fire"); effect.SendEvent("Nothing"); break;
            }
        });
        events.Alive = false;
        // Operators over exposed properties read by a script's spawner block (what it read is
        // recorded at frame 12), stopped at frame 5 and played again at frame 7.
        Action<int, VisualEffect> again = (frame, effect) =>
        {
            if (frame == 5) effect.Stop();
            if (frame == 7) effect.Play();
        };
        Run("expressions", "SimExpressions", 12, act: again).Attributes = new[] { "lifetime", "color", "spawnCount" };
        // With other values, set in the frame the effect is made and again at frame 4. In the
        // frames between and after, the curve the graph samples has slopes at its keys, and is
        // sampled between two keys, past its last key (where it loops), on its last key and
        // before its first one (where it ping-pongs).
        var others = Run("expressions others", "SimExpressions", 12, act: (frame, effect) =>
        {
            if (frame == 2) Sloped(effect, 1.04f);
            if (frame == 3) Sloped(effect, 3.87f);
            if (frame == 4) OtherValues(effect);
            if (frame == 6) Sloped(effect, 2f);
            if (frame == 8) Sloped(effect, -3.7f);
            again(frame, effect);
        });
        others.Attributes = new[] { "lifetime", "color" };
        others.Property = "A";
        OtherValues(others.Effect);
        others.Effect.playRate = 0.5f;
        // The scene's effect of that graph (saved with C = 0.5), given another A in the first frame,
        // and a copy of it made in the first frame and given another A at once.
        var sceneExpressions = new Running { Key = "sim/expressions scene", Effect = GameObject.Find("Expressions").GetComponent<VisualEffect>(), Last = 12, Act = again, Property = "A" };
        m_Running.Add(sceneExpressions);
        var copy = Instantiate(sceneExpressions.Effect.gameObject);
        copy.name = "Expressions copy";
        var copied = new Running { Key = "sim/expressions copy", Effect = copy.GetComponent<VisualEffect>(), Last = 12, Act = again, Property = "A" };
        m_Running.Add(copied);
        sceneExpressions.Effect.SetFloat("A", 1.25f);
        copied.Effect.SetFloat("A", 1.5f);
        // One effect given a value in the first frame and one given it in the second, read back
        // at three points of each of the first frames.
        m_Timed = Run("override first frame", "SimExpressions", 5).Effect;
        m_Timed.SetFloat("A", 7f);
        StartCoroutine(EndOfFrames());
        // Spawn contexts that start and stop another.
        Run("chain", "SimChain", 16);
        // A lifetime read from a texture on the GPU: the graph's image; another set in the first
        // frame; and another set at frame 4 of an effect started over at frame 5.
        Run("texture", "SimTexture", k_LastFrame).Alive = false;
        var green = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Objects/ImageB.png");
        var otherTexture = Run("texture other", "SimTexture", k_LastFrame);
        otherTexture.Alive = false;
        otherTexture.Effect.SetTexture("Image", green);
        Run("texture later", "SimTexture", k_LastFrame, act: (frame, effect) =>
        {
            if (frame == 4) effect.SetTexture("Image", green);
            if (frame == 5) effect.Reinit();
        }).Alive = false;
        // Disabled and enabled again, by its component and by its object.
        Run("toggle", "SimRate", 16, act: (frame, effect) =>
        {
            if (frame == 6) effect.enabled = false;
            if (frame == 10) effect.enabled = true;
            // The effect's renderer, while the effect is off and once it is on again.
            if (frame == 8 || frame == 12) Rec("sim/toggle/renderer/f" + frame.ToString("000"), RendererState(effect));
        });
        Run("toggle object", "SimRate", 16, act: (frame, effect) =>
        {
            if (frame == 6) effect.gameObject.SetActive(false);
            if (frame == 10) effect.gameObject.SetActive(true);
            if (frame == 8 || frame == 12) Rec("sim/toggle object/renderer/f" + frame.ToString("000"), RendererState(effect));
        });
        // Disabled and enabled in one frame: the way scripts start an effect over.
        Run("toggle at once", "SimRate", 12, act: (frame, effect) =>
        {
            if (frame == 6) { effect.enabled = false; effect.enabled = true; }
        });
        Run("toggle object at once", "SimRate", 12, act: (frame, effect) =>
        {
            if (frame == 6) { effect.gameObject.SetActive(false); effect.gameObject.SetActive(true); }
        });
        // The effects the manager lists: not a disabled one, nor one on an inactive object.
        var disabled = Run("disabled", "SimRate", 3);
        disabled.Effect.enabled = false;
        var inactive = Run("inactive", "SimRate", 3);
        inactive.Effect.gameObject.SetActive(false);
        var listed = VFXManager.GetComponents();
        Rec("sim/manager/components", "disabled " + (Array.IndexOf(listed, disabled.Effect) >= 0 ? 1 : 0) + " inactive " + (Array.IndexOf(listed, inactive.Effect) >= 0 ? 1 : 0)
            + " enabled " + (Array.IndexOf(listed, m_Running[0].Effect) >= 0 ? 1 : 0) + " without asset " + (Array.IndexOf(listed, GameObject.Find("NoAsset").GetComponent<VisualEffect>()) >= 0 ? 1 : 0));
        // The scene's own effects, as loaded (their particles die: no counts).
        foreach (var name in new[] { "Plain", "Events", "Overridden" })
            m_Running.Add(new Running { Key = "sim/scene " + name, Effect = GameObject.Find(name).GetComponent<VisualEffect>(), Last = 8, Alive = false });
        // Particles that die: counted where the count is settled.
        Run("kill", "SimKill", k_LastFrame).Alive = false;
        Run("keep", "SimKeep", k_LastFrame).Alive = false;

        var effect = m_Running[0].Effect;
        Logged("sim/errors/particle info of a spawner", () => effect.GetParticleSystemInfo("System"));
        Logged("sim/errors/particle info of nothing", () => effect.GetParticleSystemInfo("Nothing"));
        Logged("sim/errors/spawn info of a particle system", () => effect.GetSpawnSystemInfo("System (1)"));
        Logged("sim/errors/spawn info of nothing", () => effect.GetSpawnSystemInfo("Nothing"));
        Logged("sim/errors/spawn info into null", () => effect.GetSpawnSystemInfo(Shader.PropertyToID("System"), null));
    }

    /// <summary>A spawner state's values; the spawn count only of a system's state (a state
    /// made with <c>new</c> has no event attributes to hold one).</summary>
    static string State(VFXSpawnerState s, bool count = true) =>
        s.loopState + " playing=" + (s.playing ? 1 : 0) + " new=" + (s.newLoop ? 1 : 0) + (count ? " count=" + F(s.spawnCount) : "") + " dt=" + F(s.deltaTime) + " t=" + F(s.totalTime)
        + " before=" + F(s.delayBeforeLoop) + " loop=" + F(s.loopDuration) + " after=" + F(s.delayAfterLoop) + " index=" + s.loopIndex + " of=" + s.loopCount;

    void Record(Running running, string key)
    {
        var effect = running.Effect;
        var row = new StringBuilder();
        if (running.Alive) row.Append("alive=").Append(effect.aliveParticleCount).Append(' ');
        row.Append("culled=").Append(effect.culled ? 1 : 0);
        m_Names.Clear();
        effect.GetSpawnSystemNames(m_Names);
        foreach (var name in m_Names) row.Append(" | ").Append(name).Append(": ").Append(State(effect.GetSpawnSystemInfo(name)));
        if (running.Attributes != null)
        {
            var attribute = effect.GetSpawnSystemInfo(m_Names[0]).vfxEventAttribute;
            foreach (var name in running.Attributes)
                row.Append(" | ").Append(name).Append('=').Append(attribute.HasFloat(name) ? F(attribute.GetFloat(name)) : attribute.HasVector3(name) ? V(attribute.GetVector3(name)) : "none");
        }
        if (running.Property != null) row.Append(" | ").Append(running.Property).Append('=').Append(F(effect.GetFloat(running.Property)));
        Rec(key, row.ToString());
    }

    /// <summary>Gives the graph a curve with slopes at its keys, that ping-pongs before them and
    /// loops after them, and the time it samples it at.</summary>
    static void Sloped(VisualEffect effect, float when)
    {
        var curve = new AnimationCurve(new Keyframe(-1f, 3f, 0f, -2.5f), new Keyframe(0.7f, -1f, 1.25f, 0.6f), new Keyframe(2f, 5f, 3f, 0f))
        {
            preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop,
        };
        effect.SetAnimationCurve("Curve", curve);
        effect.SetFloat("When", when);
    }

    /// <summary>Other values for the expression graph's properties: a NaN, zero divisors, other signs.</summary>
    static void OtherValues(VisualEffect effect)
    {
        effect.SetFloat("A", float.NaN);
        effect.SetFloat("B", 3.5f);
        effect.SetFloat("C", -0.25f);
        effect.SetFloat("Z", -0f);
        // (The smallest int divided by -1 stops the Editor: not among the values.)
        effect.SetInt("I", int.MaxValue);
        effect.SetInt("J", 0);
        effect.SetInt("K", -4);
        effect.SetUInt("U", 7u);
        effect.SetUInt("V", 3u);
        effect.SetUInt("W", 5u);
        effect.SetAnimationCurve("Curve", new AnimationCurve(new Keyframe(-1f, 3f), new Keyframe(0f, -1f), new Keyframe(2f, 5f)));
        var ramp = new Gradient { mode = GradientMode.Fixed };
        ramp.SetKeys(new[] { new GradientColorKey(Color.green, 0.25f), new GradientColorKey(Color.yellow, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 1f) });
        effect.SetGradient("Ramp", ramp);
        // A time between the gradient's keys, where a fixed one and a blended one differ.
        effect.SetFloat("When", 0.6f);
        effect.SetBool("P", false);
        effect.SetBool("Q", false);
        effect.SetVector3("T", new Vector3(-1f, 0f, 2.5f));
        effect.SetVector3("Frame_angles", new Vector3(0f, 90f, 0f));
        effect.SetVector3("Frame_scale", new Vector3(1f, 1f, 0f));
    }

    void Simulate()
    {
        // A longer frame than effects step by, then a slower clock.
        if (m_Frame == 37) Time.captureFramerate = 10;
        if (m_Frame == 40) Time.captureFramerate = 30;
        if (m_Frame == 41) Time.timeScale = 0.5f;
        if (m_Frame == 45) Time.timeScale = 1f;
        ReadTimed("1 update");
        if (m_Frame == 2)
        {
            m_TimedLater = Run("override second frame", "SimExpressions", 5).Effect;
            m_TimedLater.SetFloat("A", 9f);
            ReadTimed("1 update after the set");
        }
        // Effects made while the others run: of a graph in use; of a graph in use with a property
        // set at once; of a graph no effect used yet, with a property set at once; and a second
        // of that graph four frames later.
        if (m_Frame == 20)
        {
            Run("late", "SimRate", 30);
            var late = Run("expressions late", "SimExpressions", 30);
            late.Property = "A";
            late.Effect.SetFloat("A", 1.75f);
            var fresh = Run("late fresh", "Exposed2", 30);
            fresh.Alive = false;
            fresh.Property = "Rate";
            fresh.Effect.SetFloat("Rate", 30f);
        }
        if (m_Frame == 24)
        {
            var second = Run("late second", "Exposed2", 30);
            second.Alive = false;
            second.Property = "Rate";
            second.Effect.SetFloat("Rate", 60f);
        }
        Rec("sim/time/f" + m_Frame.ToString("000"), F(Time.deltaTime));
        for (int i = 0; i < m_Running.Count; i++)
        {
            var running = m_Running[i];
            if (m_Frame > running.Last) continue;
            running.Act?.Invoke(m_Frame, running.Effect);
            if (running.Alive) Record(running, running.Key + "/f" + m_Frame.ToString("000"));
            else if (running.Last < k_LastFrame || m_Frame <= 1) Record(running, running.Key + "/f" + m_Frame.ToString("000"));
        }
        if (m_Frame == 12 || m_Frame == 30)
        {
            // What the script's block read (at each play, in the updates after it, at each stop), per effect.
            foreach (var (name, key) in m_Frame == 30 ? new[] { ("sim expressions late", "expressions late") }
                : new[] { ("sim expressions", "expressions"), ("sim expressions others", "expressions others"), ("Expressions", "expressions scene"), ("Expressions copy", "expressions copy") })
            {
                int row = 0;
                uint firstFrameIndex = 0;
                bool hasFrameIndex = false;
                foreach (var line in ProbeInputs.Logs.TryGetValue(name, out var log) ? log : new List<string>())
                {
                    // One value a row: a difference names the input.
                    var parts = line.Split(' ');
                    if (parts[0] == "missing" || parts[0] == "float" || parts[0] == "other") { Rec("sim/" + key + "/inputs/" + (row++).ToString("00") + " " + parts[0], line); continue; }
                    foreach (var part in parts)
                    {
                        if (!part.Contains("=")) continue;
                        string input = part.Substring(0, part.IndexOf('=')), value = part.Substring(part.IndexOf('=') + 1);
                        // The manager's update count started before the probe: how far it moved since the first play.
                        if (input == "u19")
                        {
                            uint index = uint.Parse(value);
                            if (!hasFrameIndex) { firstFrameIndex = index; hasFrameIndex = true; }
                            value = "+" + (index - firstFrameIndex);
                        }
                        Rec("sim/" + key + "/inputs/" + parts[0] + "/" + input, value);
                    }
                }
                Rec("sim/" + key + "/inputs/lines", ProbeInputs.Logs.TryGetValue(name, out var all) ? all.Count : -1);
            }
        }
        if (m_Frame == 1 || m_Frame == 100 || m_Frame == k_LastFrame)
        {
            foreach (var key in new[] { "sim/kill", "sim/keep", "sim/texture", "sim/texture other", "sim/texture later" })
            {
                var effect = m_Running.First(r => r.Key == key).Effect;
                var info = effect.GetParticleSystemInfo("System (1)");
                Rec(key + "/settled/f" + m_Frame.ToString("000"), "alive=" + effect.aliveParticleCount + " info=" + info.aliveCount + "/" + info.capacity
                    + " sleeping=" + (info.sleeping ? 1 : 0) + " awake=" + (effect.HasAnySystemAwake() ? 1 : 0));
            }
            var rate = m_Running[0].Effect;
            var rateInfo = rate.GetParticleSystemInfo("System (1)");
            Rec("sim/rate/info/f" + m_Frame.ToString("000"), rateInfo.aliveCount + "/" + rateInfo.capacity + " sleeping=" + (rateInfo.sleeping ? 1 : 0) + " awake=" + (rate.HasAnySystemAwake() ? 1 : 0));
        }
    }

    /// <summary>The scripting API's inverse of an affine matrix, with the matrix the expression
    /// controls invert (a graph's inverse of a transform gives the same numbers) and with one
    /// that has no inverse.</summary>
    static Matrix4x4 Thin(float x, float z)
    {
        var matrix = Matrix4x4.identity;
        matrix.m00 = x;
        matrix.m22 = z;
        matrix.m03 = 10f;
        matrix.m13 = 20f;
        matrix.m23 = 30f;
        return matrix;
    }

    void AffineInverse()
    {
        foreach (var (name, matrix) in new[]
        {
            ("frame", Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.Euler(30f, 45f, 60f), new Vector3(2f, 3f, 4f))),
            ("flat", Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.Euler(0f, 90f, 0f), new Vector3(1f, 1f, 0f))),
            // Determinants on either side of where a matrix stops having an inverse, of each
            // sign, a scale small in every direction, and a cell that is not a number.
            ("thin", Thin(1f, 3.2e-13f)), ("thinner", Thin(1f, 3.1e-13f)), ("thin mirrored", Thin(-1f, 3.2e-13f)), ("thinner mirrored", Thin(-1f, 3.1e-13f)),
            ("small", Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.identity, new Vector3(1e-4f, 1e-4f, 1e-4f))),
            ("smaller", Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.identity, new Vector3(5e-5f, 5e-5f, 5e-5f))),
            ("not a number", Thin(float.NaN, 1f)),
        })
        {
            var result = Matrix4x4.identity;
            bool inverted = Matrix4x4.Inverse3DAffine(matrix, ref result);
            var cells = new string[16];
            // Which not-a-number a cell holds is not compared.
            for (int i = 0; i < 16; i++) cells[i] = float.IsNaN(result[i]) ? "nan" : F(result[i]);
            Rec("math/inverse affine/" + name, (inverted ? "1 " : "0 ") + string.Join(",", cells));
        }
    }

    /// <summary>A spawner state of its own (no system behind it), and a copy of a system's:
    /// what their setters do.</summary>
    void SpawnerStates(VisualEffect effect)
    {
        var state = new VFXSpawnerState();
        Rec("spawner state/new", State(state, false));
        state.loopDuration = 1f;
        state.playing = true;
        Rec("spawner state/playing set", State(state, false));
        state.totalTime = 0.5f;
        state.loopState = VFXSpawnerLoopState.DelayingAfterLoop;
        Rec("spawner state/after loop of no delay", State(state, false));
        state.delayBeforeLoop = 0.25f;
        state.loopCount = 3;
        state.loopState = VFXSpawnerLoopState.DelayingBeforeLoop;
        state.totalTime = 0.125f;
        Rec("spawner state/before loop", State(state, false));
        state.loopState = VFXSpawnerLoopState.DelayingBeforeLoop;
        Rec("spawner state/same state set", State(state, false));
        state.delayAfterLoop = 0.5f;
        state.loopState = VFXSpawnerLoopState.DelayingAfterLoop;
        Rec("spawner state/after loop", State(state, false));
        state.deltaTime = 0.75f;
        state.loopIndex = 9;
        Rec("spawner state/values set", State(state, false));
        state.playing = false;
        Rec("spawner state/playing cleared", State(state, false));
        state.Dispose();

        var copy = effect.GetSpawnSystemInfo("System");
        Rec("spawner state/copy", State(copy));
        Rec("spawner state/copy/attribute", copy.vfxEventAttribute == null ? "null" : "spawnCount " + (copy.vfxEventAttribute.HasFloat("spawnCount") ? F(copy.vfxEventAttribute.GetFloat("spawnCount")) : "none"));
        copy.spawnCount = 2.5f;
        copy.playing = true;
        Rec("spawner state/copy/set", State(copy) + " attribute " + F(copy.vfxEventAttribute.GetFloat("spawnCount")));
        // The copy is the reader's own: the system keeps its state.
        Rec("spawner state/copy/system", State(effect.GetSpawnSystemInfo("System")));
        var into = new VFXSpawnerState();
        effect.GetSpawnSystemInfo(Shader.PropertyToID("System"), into);
        Rec("spawner state/filled", State(into));
        copy.Dispose();
        into.Dispose();
    }

    /// <summary>The graph compiler's tables: the type every expression operation produces (for
    /// each type a data word can name), the size of every value type and its texture dimension.</summary>
    void Tables()
    {
        var module = typeof(VisualEffect).Assembly;
        var editor = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.VFX.VFXExpressionHelper")).First(t => t != null);
        var operation = module.GetType("UnityEngine.VFX.VFXExpressionOperation");
        var valueType = module.GetType("UnityEngine.VFX.VFXValueType");
        var typeOf = editor.GetMethod("GetTypeOfOperation", Any);
        var sizeOf = editor.GetMethod("GetSizeOfType", Any);
        var dimension = editor.GetMethod("GetTextureDimension", Any);
        var operations = Enum.GetValues(operation);
        var types = Enum.GetValues(valueType);
        foreach (var op in operations)
        {
            var row = new StringBuilder();
            foreach (var type in types)
            {
                var result = typeOf.Invoke(null, new object[] { op, 0, 0, 0, Convert.ToInt32(type) });
                row.Append(Convert.ToInt32(result)).Append(' ');
            }
            Rec("tables/type/" + op, row.ToString().TrimEnd());
        }
        foreach (var type in types)
        {
            Logged("tables/size/" + type, () => Rec("tables/size/" + type, Convert.ToInt32(sizeOf.Invoke(null, new object[] { type }))));
            Rec("tables/dimension/" + type, dimension.Invoke(null, new object[] { type }).ToString());
        }
        // Outside the enums.
        Rec("tables/type/outside", Convert.ToInt32(typeOf.Invoke(null, new object[] { Enum.ToObject(operation, 155), 0, 0, 0, 1 })));
        Logged("tables/size/outside", () => Rec("tables/size/outside", Convert.ToInt32(sizeOf.Invoke(null, new object[] { Enum.ToObject(valueType, 21) }))));
        Rec("tables/dimension/outside", dimension.Invoke(null, new object[] { Enum.ToObject(valueType, 21) }).ToString());
        Rec("tables/runtime data version", typeof(VisualEffectAsset).GetProperty("currentRuntimeDataVersion", Any).GetValue(null).ToString());
    }

    void Asset(string name)
    {
        var path = "Assets/Graphs/" + name + ".vfx";
        var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
        var key = "asset/" + name;
        Rec(key + "/loaded", asset != null);
        if (asset == null) return;
        Rec(key + "/name", asset.name);
        Rec(key + "/main", AssetDatabase.LoadMainAssetAtPath(path) == asset);
        var exposed = new List<VFXExposedProperty>();
        asset.GetExposedProperties(exposed);
        Rec(key + "/exposed", string.Join(", ", exposed.Select(p => p.name + ":" + (p.type == null ? "<null>" : p.type.Name))));
        foreach (var p in exposed)
        {
            Rec(key + "/dimension/" + p.name, asset.GetTextureDimension(p.name).ToString());
            Rec(key + "/space/" + p.name, asset.GetExposedSpace(p.name).ToString());
        }
        Rec(key + "/dimension/<missing>", asset.GetTextureDimension("NoSuchProperty").ToString());
        Rec(key + "/space/<missing>", asset.GetExposedSpace("NoSuchProperty").ToString());
        var events = new List<string>();
        asset.GetEvents(events);
        Rec(key + "/events", string.Join(", ", events));
        // The objects the import made, besides the graph's own models.
        var subs = new List<string>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o == null || o is ScriptableObject) continue;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id);
            subs.Add(o.GetType().Name + " '" + o.name + "' " + id + (o is Material m ? " shader '" + m.shader.name + "'" : ""));
        }
        subs.Sort(StringComparer.Ordinal);
        Rec(key + "/objects", string.Join(" | ", subs));
        Rec(key + "/objects/scripts", AssetDatabase.LoadAllAssetsAtPath(path).Count(o => o is ScriptableObject));
        Rec(key + "/objects/representations", string.Join(" | ", AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Select(o => o.GetType().Name).OrderBy(n => n, StringComparer.Ordinal)));
        foreach (var kind in new[] { "GetSystemNames", "GetParticleSystemNames", "GetSpawnSystemNames", "GetOutputEventNames" })
        {
            var names = new List<string>();
            typeof(VisualEffectAsset).GetMethod(kind, Any).Invoke(asset, new object[] { names });
            Rec(key + "/" + kind, string.Join(", ", names));
        }
    }

    static VisualEffectAsset Graph(string name) => AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Graphs/" + name + ".vfx");

    static string M(Matrix4x4 m)
    {
        var b = new StringBuilder();
        for (int i = 0; i < 16; i++) b.Append(F(m[i])).Append(i < 15 ? " " : "");
        return b.ToString();
    }

    static string Keys(AnimationCurve c) => c == null ? "<null>" : string.Join(";", c.keys.Select(k => F(k.time) + ":" + F(k.value)));

    static string Keys(Gradient g) => g == null ? "<null>" :
        string.Join(";", g.colorKeys.Select(k => F(k.time) + ":" + V(k.color))) + " / " + string.Join(";", g.alphaKeys.Select(k => F(k.time) + ":" + F(k.alpha)));

    static string Name(UnityEngine.Object o) => o == null ? "<null>" : o.GetType().Name + " " + o.name;

    /// <summary>Which of the typed Has* a name answers true to, as a string of 0 and 1 in the
    /// order: Bool Int UInt Float Vector2 Vector3 Vector4 Matrix4x4 Texture AnimationCurve
    /// Gradient Mesh SkinnedMeshRenderer GraphicsBuffer.</summary>
    static string Has(VisualEffect e, string n)
    {
        var bits = new[]
        {
            e.HasBool(n), e.HasInt(n), e.HasUInt(n), e.HasFloat(n), e.HasVector2(n), e.HasVector3(n), e.HasVector4(n), e.HasMatrix4x4(n),
            e.HasTexture(n), e.HasAnimationCurve(n), e.HasGradient(n), e.HasMesh(n), e.HasSkinnedMeshRenderer(n), e.HasGraphicsBuffer(n),
        };
        return string.Concat(bits.Select(b => b ? '1' : '0'));
    }

    /// <summary>The value of a property read with the getter of its listed type.</summary>
    static string Value(VisualEffect e, string n, Type type)
    {
        if (type == typeof(bool)) return e.GetBool(n) ? "true" : "false";
        if (type == typeof(int)) return e.GetInt(n).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(uint)) return e.GetUInt(n).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(float)) return F(e.GetFloat(n));
        if (type == typeof(Vector2)) return V(e.GetVector2(n));
        if (type == typeof(Vector3)) return V(e.GetVector3(n));
        if (type == typeof(Vector4)) return V(e.GetVector4(n));
        if (type == typeof(Matrix4x4)) return M(e.GetMatrix4x4(n));
        if (type == typeof(Texture)) return Name(e.GetTexture(n));
        if (type == typeof(AnimationCurve)) return Keys(e.GetAnimationCurve(n));
        if (type == typeof(Gradient)) return Keys(e.GetGradient(n));
        if (type == typeof(Mesh)) return Name(e.GetMesh(n));
        if (type == typeof(SkinnedMeshRenderer)) return Name(e.GetSkinnedMeshRenderer(n));
        return "<" + (type == null ? "null" : type.Name) + ">";
    }

    void Component(string key, VisualEffect e)
    {
        Rec(key + "/asset", e.visualEffectAsset == null ? "<null>" : e.visualEffectAsset.name);
        Rec(key + "/components", string.Join(", ", e.GetComponents<Component>().Select(c => c.GetType().Name)));
        Rec(key + "/pause", e.pause);
        Rec(key + "/playRate", F(e.playRate));
        Rec(key + "/startSeed", e.startSeed);
        Rec(key + "/resetSeedOnPlay", e.resetSeedOnPlay);
        Rec(key + "/initialEventName", e.initialEventName);
        Rec(key + "/initialEventID", e.initialEventID == Shader.PropertyToID(e.initialEventName ?? ""));
        var names = new List<string>();
        e.GetSystemNames(names);
        Rec(key + "/GetSystemNames", string.Join(", ", names));
        e.GetParticleSystemNames(names);
        Rec(key + "/GetParticleSystemNames", string.Join(", ", names));
        e.GetSpawnSystemNames(names);
        Rec(key + "/GetSpawnSystemNames", string.Join(", ", names));
        e.GetOutputEventNames(names);
        Rec(key + "/GetOutputEventNames", string.Join(", ", names));
        Rec(key + "/HasSystem/System", e.HasSystem("System"));
        Rec(key + "/HasSystem/System (1)", e.HasSystem("System (1)"));
        Rec(key + "/HasSystem/Nope", e.HasSystem("Nope"));
        var exposed = new List<VFXExposedProperty>();
        if (e.visualEffectAsset != null) e.visualEffectAsset.GetExposedProperties(exposed);
        foreach (var p in exposed)
        {
            Rec(key + "/has/" + p.name, Has(e, p.name));
            Logged(key + "/get/" + p.name, () => Rec(key + "/get/" + p.name, Value(e, p.name, p.type)));
            Rec(key + "/dimension/" + p.name, e.GetTextureDimension(p.name).ToString());
        }
        Rec(key + "/has/<missing>", Has(e, "NoSuchProperty"));
        Rec(key + "/dimension/<missing>", e.GetTextureDimension("NoSuchProperty").ToString());
        Logged(key + "/get/<missing>/float", () => Rec(key + "/get/<missing>/float", F(e.GetFloat("NoSuchProperty"))));
        Logged(key + "/get/<missing>/texture", () => Rec(key + "/get/<missing>/texture", Name(e.GetTexture("NoSuchProperty"))));
        Logged(key + "/get/<missing>/curve", () => Rec(key + "/get/<missing>/curve", Keys(e.GetAnimationCurve("NoSuchProperty"))));
        Logged(key + "/get/<missing>/gradient", () => Rec(key + "/get/<missing>/gradient", Keys(e.GetGradient("NoSuchProperty"))));
        var attribute = e.CreateVFXEventAttribute();
        Rec(key + "/attribute", attribute == null ? "<null>" : "made");
    }

    /// <summary>Effects made and changed from script: every setter with the right and a wrong
    /// type, overrides reset, an asset swap, a clone.</summary>
    void Scripted()
    {
        var go = new GameObject("Scripted");
        var e = go.AddComponent<VisualEffect>();
        Component("scripted/new", e);
        e.visualEffectAsset = Graph("Exposed");
        Component("scripted/assigned", e);

        var image = new Texture2D(2, 2) { name = "ProbeImage" };
        var volume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false) { name = "ProbeVolume" };
        var mesh = new Mesh { name = "ProbeMesh" };
        const string k = "scripted/set";
        Logged(k + "/Rate", () => e.SetFloat("Rate", 20.5f));
        Logged(k + "/Count", () => e.SetInt("Count", 41));
        Logged(k + "/uCount", () => e.SetUInt("uCount", 4000000000u));
        Logged(k + "/Flag", () => e.SetBool("Flag", false));
        Logged(k + "/Pair", () => e.SetVector2("Pair", new Vector2(0.5f, 0.25f)));
        Logged(k + "/Triple", () => e.SetVector3("Triple", new Vector3(9f, 8f, 7f)));
        Logged(k + "/Quad", () => e.SetVector4("Quad", new Vector4(6f, 5f, 4f, 3f)));
        Logged(k + "/Tint", () => e.SetVector4("Tint", new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        Logged(k + "/Where_position", () => e.SetVector3("Where_position", new Vector3(-1f, -2f, -3f)));
        Logged(k + "/Curve", () => e.SetAnimationCurve("Curve", AnimationCurve.Linear(0f, 3f, 2f, 4f)));
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.green, 0.25f), new GradientColorKey(Color.yellow, 0.5f) },
            new[] { new GradientAlphaKey(0.5f, 0.125f), new GradientAlphaKey(0.25f, 0.75f) });
        Logged(k + "/Ramp", () => e.SetGradient("Ramp", ramp));
        Logged(k + "/Image", () => e.SetTexture("Image", image));
        // A texture of another dimension than the property's.
        Logged(k + "/Volume/2D", () => e.SetTexture("Volume", image));
        Logged(k + "/Sky/3D", () => e.SetTexture("Sky", volume));
        Logged(k + "/Shape", () => e.SetMesh("Shape", mesh));
        // Wrong types and unknown names.
        Logged(k + "/wrong/Count/float", () => e.SetFloat("Count", 1f));
        Logged(k + "/wrong/Rate/int", () => e.SetInt("Rate", 1));
        Logged(k + "/wrong/Tint/vector3", () => e.SetVector3("Tint", Vector3.one));
        Logged(k + "/wrong/Image/mesh", () => e.SetMesh("Image", mesh));
        Logged(k + "/wrong/Shape/texture", () => e.SetTexture("Shape", image));
        Logged(k + "/wrong/missing", () => e.SetFloat("NoSuchProperty", 1f));
        Logged(k + "/wrong/get/Count/float", () => Rec(k + "/wrong/get/Count/float", F(e.GetFloat("Count"))));
        Logged(k + "/wrong/get/Rate/vector2", () => Rec(k + "/wrong/get/Rate/vector2", V(e.GetVector2("Rate"))));
        Logged(k + "/wrong/get/Image/mesh", () => Rec(k + "/wrong/get/Image/mesh", Name(e.GetMesh("Image"))));
        Component("scripted/set/after", e);

        // The curve and gradient set are copies: later changes to ours do not reach the property.
        ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        Rec("scripted/copy/Ramp", Keys(e.GetGradient("Ramp")));
        var read = e.GetAnimationCurve("Curve");
        read.AddKey(5f, 5f);
        Rec("scripted/copy/Curve", Keys(e.GetAnimationCurve("Curve")));

        // A clone carries the overrides.
        var clone = Instantiate(go).GetComponent<VisualEffect>();
        Rec("scripted/clone/Rate", F(clone.GetFloat("Rate")));
        Rec("scripted/clone/Count", clone.GetInt("Count"));
        Rec("scripted/clone/Image", Name(clone.GetTexture("Image")));
        e.SetFloat("Rate", 30f);
        Rec("scripted/clone/Rate/after source change", F(clone.GetFloat("Rate")));

        // Overrides reset: back to the asset's values; nothing for an unknown or untouched name.
        Logged("scripted/reset/Rate", () => e.ResetOverride("Rate"));
        Logged("scripted/reset/Curve", () => e.ResetOverride("Curve"));
        Logged("scripted/reset/Image", () => e.ResetOverride("Image"));
        Logged("scripted/reset/untouched", () => e.ResetOverride("Zeta"));
        Logged("scripted/reset/missing", () => e.ResetOverride("NoSuchProperty"));
        Rec("scripted/reset/Rate/value", F(e.GetFloat("Rate")));
        Rec("scripted/reset/Curve/value", Keys(e.GetAnimationCurve("Curve")));
        Rec("scripted/reset/Image/value", Name(e.GetTexture("Image")));
        Rec("scripted/reset/Count/kept", e.GetInt("Count"));

        // Another asset with some of the same names: do overrides follow?
        e.SetFloat("Rate", 50f);
        e.visualEffectAsset = Graph("Exposed2");
        Rec("scripted/swap/Rate", F(e.GetFloat("Rate")));
        Rec("scripted/swap/Count/has", Has(e, "Count"));
        Rec("scripted/swap/Count", F(e.GetFloat("Count")));
        Rec("scripted/swap/Extra", F(e.GetFloat("Extra")));
        e.SetFloat("Rate", 60f);
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/back/Rate", F(e.GetFloat("Rate")));
        Rec("scripted/swap/back/Count", e.GetInt("Count"));
        // The same asset assigned again.
        e.SetFloat("Rate", 70f);
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/same/Rate", F(e.GetFloat("Rate")));
        e.visualEffectAsset = null;
        Rec("scripted/swap/null/has", Has(e, "Rate"));
        Logged("scripted/swap/null/get", () => Rec("scripted/swap/null/get", F(e.GetFloat("Rate"))));
        // And an asset again after none: are the overrides still there?
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/after null/Rate", F(e.GetFloat("Rate")));
        Rec("scripted/swap/after null/Count", e.GetInt("Count"));
        // Object overrides set after the clone saved others, through an asset that has no such
        // property: the current ones do not come back, the saved ones do.
        e.SetTexture("Image", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Objects/ImageB.png"));
        e.SetMesh("Shape", AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Objects/ShapeB.asset"));
        Rec("scripted/swap/objects/Image/set", Name(e.GetTexture("Image")));
        Rec("scripted/swap/objects/Shape/set", Name(e.GetMesh("Shape")));
        e.visualEffectAsset = Graph("Exposed2");
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/objects/Image", Name(e.GetTexture("Image")));
        Rec("scripted/swap/objects/Shape", Name(e.GetMesh("Shape")));
        e.ResetOverride("Image");
        e.ResetOverride("Shape");
        Rec("scripted/swap/objects/Image/reset", Name(e.GetTexture("Image")));
        Rec("scripted/swap/objects/Shape/reset", Name(e.GetMesh("Shape")));

        // State set from script.
        e.pause = true;
        e.playRate = 2.5f;
        e.startSeed = 99;
        e.resetSeedOnPlay = false;
        e.initialEventName = "Fire";
        Component("scripted/state", e);
        e.initialEventID = Shader.PropertyToID("Cease");
        Rec("scripted/state/initialEventName/by id", e.initialEventName);
        e.initialEventName = "";
        Rec("scripted/state/initialEventName/empty", e.initialEventName);
        e.visualEffectAsset = Graph("Events");
        Rec("scripted/state/initialEventName/with asset", e.initialEventName);
    }

    static readonly string[] k_Attributes =
    {
        "position", "velocity", "color", "alpha", "size", "lifetime", "age", "spawnCount", "direction", "targetPosition", "seed",
        "particleId", "alive", "angle", "scale", "mass", "texIndex", "NoSuchAttribute",
    };

    static string Has(VFXEventAttribute a, string n)
    {
        var bits = new[] { a.HasBool(n), a.HasInt(n), a.HasUint(n), a.HasFloat(n), a.HasVector2(n), a.HasVector3(n), a.HasVector4(n), a.HasMatrix4x4(n) };
        return string.Concat(bits.Select(b => b ? '1' : '0'));
    }

    /// <summary>Event attributes: the layout each asset's attributes have, their starting values,
    /// writes and reads by the right and a wrong type, copies.</summary>
    void Attributes()
    {
        foreach (var graph in k_Graphs)
        {
            var e = new GameObject("Attributes " + graph).AddComponent<VisualEffect>();
            e.visualEffectAsset = Graph(graph);
            var a = e.CreateVFXEventAttribute();
            var key = "attributes/" + graph;
            Rec(key, a == null ? "<null>" : "made");
            if (a == null) continue;
            foreach (var n in k_Attributes)
            {
                Rec(key + "/has/" + n, Has(a, n));
                Rec(key + "/get/" + n, F(a.GetFloat(n)) + " | " + V(a.GetVector3(n)) + " | " + a.GetUint(n) + " | " + a.GetInt(n) + " | " + (a.GetBool(n) ? "true" : "false"));
            }
        }
        {
            var e = new GameObject("Attributes set").AddComponent<VisualEffect>();
            e.visualEffectAsset = Graph("Events");
            var a = e.CreateVFXEventAttribute();
            const string key = "attributes/set";
            Logged(key + "/spawnCount", () => a.SetFloat("spawnCount", 7.5f));
            Logged(key + "/color", () => a.SetVector3("color", new Vector3(0.25f, 0.5f, 0.75f)));
            Logged(key + "/wrong/spawnCount/int", () => a.SetInt("spawnCount", 3));
            Logged(key + "/wrong/color/float", () => a.SetFloat("color", 1f));
            Logged(key + "/wrong/missing", () => a.SetFloat("NoSuchAttribute", 1f));
            Rec(key + "/get/spawnCount", F(a.GetFloat("spawnCount")));
            Rec(key + "/get/color", V(a.GetVector3("color")));
            Rec(key + "/get/wrong/spawnCount/int", a.GetInt("spawnCount"));
            Rec(key + "/get/wrong/color/vector4", V(a.GetVector4("color")));
            var copy = new VFXEventAttribute(a);
            a.SetFloat("spawnCount", 1f);
            Rec(key + "/copy/spawnCount", F(copy.GetFloat("spawnCount")));
            Rec(key + "/copy/color", V(copy.GetVector3("color")));
            var other = e.CreateVFXEventAttribute();
            other.CopyValuesFrom(copy);
            Rec(key + "/copied/spawnCount", F(other.GetFloat("spawnCount")));
            Rec(key + "/distinct", !ReferenceEquals(other, e.CreateVFXEventAttribute()));
            Logged(key + "/copy/null", () => new VFXEventAttribute(null));
            Logged(key + "/copied/null", () => other.CopyValuesFrom(null));
            // Values copied between attributes of assets with other layouts.
            var basic = new GameObject("Attributes basic").AddComponent<VisualEffect>();
            basic.visualEffectAsset = Graph("Basic");
            var b = basic.CreateVFXEventAttribute();
            Logged(key + "/copied/other asset", () => b.CopyValuesFrom(copy));
            Rec(key + "/copied/other asset/spawnCount", F(b.GetFloat("spawnCount")));
            Rec(key + "/copied/other asset/color", Has(b, "color") + " " + V(b.GetVector3("color")));
            a.Dispose();
            Logged(key + "/disposed/get", () => Rec(key + "/disposed/get", F(a.GetFloat("spawnCount"))));
        }
        {
            // Layouts with several attributes of different types, and copies between them.
            var first = new GameObject("Attributes first").AddComponent<VisualEffect>();
            first.visualEffectAsset = Graph("Attributes");
            var second = new GameObject("Attributes second").AddComponent<VisualEffect>();
            second.visualEffectAsset = Graph("Attributes2");
            var a = first.CreateVFXEventAttribute();
            var b = second.CreateVFXEventAttribute();
            const string key = "attributes/layouts";
            a.SetFloat("spawnCount", 5f);
            a.SetFloat("lifetime", 9f);
            a.SetVector3("velocity", new Vector3(1f, 2f, 3f));
            a.SetVector3("color", new Vector3(0.125f, 0.25f, 0.375f));
            a.SetBool("alive", false);
            a.SetUint("seed", 4000000001u);
            Rec(key + "/first", Layout(a));
            // A wrong type for a name the layout has: nothing written, the default read.
            Logged(key + "/wrong/alive/int", () => a.SetInt("alive", 7));
            Logged(key + "/wrong/seed/int", () => a.SetInt("seed", 7));
            Logged(key + "/wrong/velocity/vector4", () => a.SetVector4("velocity", Vector4.one));
            Rec(key + "/first/after wrong writes", Layout(a));
            Rec(key + "/first/wrong reads", (a.GetInt("alive") + " " + a.GetInt("seed") + " " + V(a.GetVector4("velocity")) + " " + V(a.GetVector2("color"))));
            b.SetFloat("spawnCount", 6f);
            b.SetFloat("lifetime", 2f);
            b.SetFloat("size", 4f);
            b.SetVector3("color", new Vector3(0.875f, 0.75f, 0.625f));
            Rec(key + "/second", Layout(b));
            // Into the second layout from the first, and into a fresh first from the second.
            var intoSecond = new VFXEventAttribute(b);
            Logged(key + "/second from first", () => intoSecond.CopyValuesFrom(a));
            Rec(key + "/second from first", Layout(intoSecond));
            var intoFirst = first.CreateVFXEventAttribute();
            Rec(key + "/first/fresh", Layout(intoFirst));
            Logged(key + "/first from second", () => intoFirst.CopyValuesFrom(b));
            Rec(key + "/first from second", Layout(intoFirst));
            // A copy made by the constructor keeps its source's layout.
            var copy = new VFXEventAttribute(a);
            Rec(key + "/first/copy", Layout(copy));
        }
    }

    /// <summary>Every attribute an attribute set has among the probed names, with its value by
    /// the getter of the type it answers to.</summary>
    static string Layout(VFXEventAttribute a)
    {
        var b = new StringBuilder();
        foreach (var n in k_Attributes)
        {
            if (a.HasFloat(n)) b.Append(n).Append('=').Append(F(a.GetFloat(n))).Append("; ");
            else if (a.HasVector2(n)) b.Append(n).Append('=').Append(V(a.GetVector2(n))).Append("; ");
            else if (a.HasVector3(n)) b.Append(n).Append('=').Append(V(a.GetVector3(n))).Append("; ");
            else if (a.HasVector4(n)) b.Append(n).Append('=').Append(V(a.GetVector4(n))).Append("; ");
            else if (a.HasBool(n)) b.Append(n).Append('=').Append(a.GetBool(n) ? "true" : "false").Append("; ");
            else if (a.HasUint(n)) b.Append(n).Append('=').Append(a.GetUint(n)).Append("u; ");
            else if (a.HasInt(n)) b.Append(n).Append('=').Append(a.GetInt(n)).Append("; ");
            else if (a.HasMatrix4x4(n)) b.Append(n).Append('=').Append(M(a.GetMatrix4x4(n))).Append("; ");
        }
        return b.ToString().TrimEnd();
    }

    /// <summary>Compiled graphs handed to a resource by hand (the cases of the editor assembly's
    /// RuntimeDataCases): what each logs and throws.</summary>
    void RuntimeData()
    {
        var cases = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RuntimeDataCases")).First(t => t != null);
        var run = cases.GetMethod("Run", Any);
        var kept = cases.GetMethod("ShaderSourcesKept", Any);
        foreach (var name in (string[])cases.GetMethod("Names", Any).Invoke(null, null))
        {
            Logged("runtime data/" + name, () =>
            {
                try { run.Invoke(null, new object[] { name }); }
                catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            });
            // Each case hands one shader source over: whether the resource holds it afterwards.
            Rec("runtime data/" + name + "/shader sources kept", (int)kept.Invoke(null, null));
        }
    }
}
#endif
