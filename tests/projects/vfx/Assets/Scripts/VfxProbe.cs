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
            Bundled();
            SpawnerStates(m_Running[0].Effect);
            AffineInverse();
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

    void LateUpdate() => ReadTimed("2 late update");

    System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (m_Running.Count != 0)
        {
            yield return wait;
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
