// The probe runs in the Editor only: it loads the graphs through the AssetDatabase.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Records how the Visual Effect module draws the probe's graphs (see README.md): images of
/// effects seen by cameras of their own, with what each effect says of itself, as keyed values
/// (one JSON line each) into VFX_OUTPUTS_PROBE_OUT/values.jsonl, then exits Play mode.
/// </summary>
public sealed class VfxOutputsProbe : MonoBehaviour
{
    const int Size = 64;
    const int LastFrame = 50;

    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("VfxOutputsProbe").AddComponent<VfxOutputsProbe>();
    }

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");

    /// <summary>An effect, far from the others, and the cameras that look at it: each renders
    /// into a small texture of its own, saved at the frames named.</summary>
    sealed class Stage
    {
        public string Key;
        public VisualEffect Effect;
        public readonly List<(string Name, Camera Camera)> Eyes = new List<(string, Camera)>();
        public int[] Frames;
        /// <summary>What the frame does to the stage before it is drawn.</summary>
        public Action<int, Stage> Act;
        /// <summary>Whether the stage's images are saved.</summary>
        public bool Shots = true;
    }

    readonly List<Stage> m_Stages = new List<Stage>();
    Texture2D m_Shot;
    int m_Frame;
    bool m_Done;

    Stage AddStage(string key, string graph, params int[] frames)
    {
        var origin = new Vector3(1000f + 100f * m_Stages.Count, 0f, 0f);
        var go = new GameObject("stage " + key);
        go.transform.position = origin;
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/OutputGraphs/" + graph + ".vfx");
        var stage = new Stage { Key = key, Effect = effect, Frames = frames };
        m_Stages.Add(stage);
        return stage;
    }

    /// <summary>A camera of the stage at <paramref name="offset"/> from its effect, looking
    /// along <paramref name="forward"/>: orthographic, <paramref name="height"/> units high,
    /// its near plane <paramref name="near"/> units ahead.</summary>
    static Camera Eye(Stage stage, string name, Vector3 offset, Vector3 forward, float height = 3f, float near = 1f)
    {
        var eye = new GameObject("eye " + stage.Key + " " + name);
        eye.transform.position = stage.Effect.transform.position + offset;
        eye.transform.rotation = Quaternion.LookRotation(forward);
        var camera = eye.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.nearClipPlane = near;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        camera.targetTexture = new RenderTexture(Size, Size, 24);
        stage.Eyes.Add((name, camera));
        return camera;
    }

    static Stage Front(Stage stage)
    {
        Eye(stage, "front", new Vector3(0f, 0f, -10f), Vector3.forward);
        return stage;
    }

    void Start()
    {
        try
        {
            foreach (var route in k_Routes)
            {
                // Seen whole, in part (the rest out of the camera's view) and from inside the
                // cloud (particles behind the camera as well), by three cameras in each frame.
                // Every camera looks along the quads' normal: quads seen at an angle cross
                // each other, where the Editor's own graphs decide a pixel differently.
                var still = AddStage(route + " still", route + "Still", 3, 10);
                Front(still);
                Eye(still, "part", new Vector3(0.6f, 0.5f, -10f), Vector3.forward, 1.4f);
                Eye(still, "inside", new Vector3(0.2f, -0.1f, 0f), new Vector3(0.1f, 0.05f, 1f), 1.2f, 0.05f);

                Front(AddStage(route + " dying", route + "Dying", 12, 30, 45));
                // The list made for each camera (in the Editor, OpenGL Core, NVIDIA) shows a
                // different few of these particles in each capture: not shot (README.md).
                Front(AddStage(route + " many", route + "Many", 5)).Shots = route != "FrustumCulled";
                // Seen whole and in part in each frame.
                var glow = Front(AddStage(route + " glow", route + "Glow", 3, 10));
                Eye(glow, "part", new Vector3(0.3f, 0.2f, -10f), Vector3.forward, 1.4f);

                // A camera that looks away until frame 20, then at the effect.
                var away = AddStage(route + " away", route + "Away", 10, 19, 30);
                var eye = Eye(away, "front", new Vector3(0f, 0f, -10f), Vector3.back);
                away.Act = (frame, stage) =>
                {
                    if (frame == 20) eye.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                };
            }
            // Sorted for each camera: two cameras that see the cloud from different places.
            foreach (var name in new[] { "SortDistance", "SortDepth", "SortReverse", "SortCustom", "SortKeyed" })
            {
                var sorted = Front(AddStage(name, name, 3, 10));
                Eye(sorted, "offset", new Vector3(2f, 1.5f, -6f), Vector3.forward);
            }
            Front(AddStage("SortYoungest", "SortYoungest", 30, 45));
            // Sorted for each camera: the Editor's lists made for each camera of large systems
            // are not repeatable (README.md): not shot.
            Front(AddStage("SortMany", "SortMany", 5)).Shots = false;
            Front(AddStage("SortKeyedMany", "SortKeyedMany", 5));
            Front(AddStage("Priority", "Priority", 3));
            Front(AddStage("PriorityTies", "PriorityTies", 3));
            // Two effects over each other, the one made second farther from the camera: drawn
            // from the farther, against the order they were made in.
            var pair = Front(AddStage("Effects", "SortKeyed", 3));
            var farther = new GameObject("stage Effects farther").AddComponent<VisualEffect>();
            farther.transform.position = pair.Effect.transform.position + new Vector3(0.3f, 0.2f, 2f);
            farther.resetSeedOnPlay = false;
            farther.startSeed = 12;
            farther.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/OutputGraphs/SortDistance.vfx");
            // The other planar primitives (after the stages above: a stage's place is its rank).
            foreach (var name in new[] { "TriangleStill", "TriangleList", "TriangleMany", "OctagonStill", "OctagonList", "OctagonMany" })
                Front(AddStage(name, name, 3));
            StartCoroutine(EndOfFrames());
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfx outputs probe failed: " + e);
            Finish();
        }
    }

    void Update()
    {
        if (m_Done) return;
        m_Frame++;
        foreach (var stage in m_Stages) stage.Act?.Invoke(m_Frame, stage);
        // Effects made while the others play (after their draws have begun).
        if (m_Frame == 6)
            foreach (var route in k_Routes) Front(AddStage(route + " late", route + "Late", 10));
    }

    static readonly string[] k_Routes = { "UpdateList", "ComputeCulled", "FrustumCulled" };

    System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (!m_Done)
        {
            yield return wait;
            try
            {
                Shoot();
            }
            catch (Exception e)
            {
                Rec("failed", e.ToString());
                Debug.LogError("vfx outputs probe failed: " + e);
                m_Frame = LastFrame;
            }
            if (m_Frame >= LastFrame) Finish();
        }
    }

    static string RendererState(VisualEffect effect)
    {
        var renderer = effect.GetComponent<Renderer>();
        return renderer == null ? "none" : "enabled=" + (renderer.enabled ? 1 : 0) + " visible=" + (renderer.isVisible ? 1 : 0);
    }

    /// <summary>At the end of a frame: the stages whose images this frame is, with what the
    /// effect says of itself.</summary>
    void Shoot()
    {
        var dir = Environment.GetEnvironmentVariable("VFX_OUTPUTS_PROBE_OUT");
        foreach (var stage in m_Stages)
        {
            if (Array.IndexOf(stage.Frames, m_Frame) < 0) continue;
            string frame = "f" + m_Frame.ToString("000");
            Rec("draw/" + stage.Key + "/" + frame, "alive=" + stage.Effect.aliveParticleCount + " culled=" + (stage.Effect.culled ? 1 : 0)
                + " awake=" + (stage.Effect.HasAnySystemAwake() ? 1 : 0) + " renderer " + RendererState(stage.Effect));
            if (string.IsNullOrEmpty(dir) || !stage.Shots) continue;
            foreach (var (name, camera) in stage.Eyes)
            {
                // The colour only: the alpha an opaque output writes is its texture's, filtered,
                // which the Editor's own graphs give a unit apart (README.md).
                if (m_Shot == null) m_Shot = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                var active = RenderTexture.active;
                RenderTexture.active = camera.targetTexture;
                m_Shot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                m_Shot.Apply();
                RenderTexture.active = active;
                File.WriteAllBytes(Path.Combine(dir, "draw-" + stage.Key + "-" + name + "-" + frame + ".png"), m_Shot.EncodeToPNG());
            }
        }
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
        if (m_Done) return;
        m_Done = true;
        Rec("capture/graphs imported while playing", string.Join(", ", ImportsWhilePlaying.Graphs.OrderBy(p => p, StringComparer.Ordinal)));
        // A capture with such an import is not one to publish: it stays incomplete.
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfx outputs probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_OUTPUTS_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
