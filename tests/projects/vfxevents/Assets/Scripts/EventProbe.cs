// The probe runs in the Editor only (it loads its graphs through the asset database).
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
/// Draws the probe's effects, each in front of a camera of its own, and records per frame
/// what each of their systems answers and, at the frames named, an image: into VFX_PROBE_OUT,
/// then exits Play mode.
/// </summary>
public sealed class EventProbe : MonoBehaviour
{
    const int k_LastFrame = 72;
    /// <summary>The frames whose rows are recorded: the first ones, and around the alive counts' second answer.</summary>
    static readonly int[] k_RowFrames = { 2, 3, 4, 5, 6, 10, 20, 30, 31, 32, 33, 34, 40, 60, 62, 63, 64, 65, 66, 70 };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Stage> m_Stages = new List<Stage>();
    readonly List<string> m_Names = new List<string>();
    Texture2D m_Shot;
    Stage m_Reinit, m_Stopped;
    int m_Frame;
    bool m_Done;

    sealed class Stage
    {
        public string Key;
        public VisualEffect Effect;
        public Camera Camera;
        public int[] Frames;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("EventProbe").AddComponent<EventProbe>();
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

    void Start()
    {
        try
        {
            Draw("on die", "EventOnDie", 10, 20, 40, 66);
            Draw("always", "EventAlways", 4, 10, 20, 40);
            Draw("rate", "EventRate", 4, 10, 20, 40);
            Draw("distance", "EventDistance", 4, 10, 20, 40);
            Draw("at birth", "EventAtBirth", 10, 20, 40, 66);
            Draw("two", "EventTwo", 10, 20, 40, 66);
            Draw("chain", "EventChain", 10, 20, 40, 66);
            Draw("keep", "EventKeep", 10, 20, 40, 66);
            Draw("keep room", "EventKeepRoom", 10, 20, 40, 66);
            // The same graph started over at frame 30: its counts start over with it.
            m_Reinit = Draw("reinit", "EventOnDie", 20, 29, 32, 40, 66);
            // And one stopped at frame 30: what was spawned lives on, nothing more is.
            m_Stopped = Draw("stopped", "EventRate", 20, 29, 32, 40, 66);
            foreach (var stage in m_Stages)
            {
                m_Names.Clear();
                stage.Effect.GetSystemNames(m_Names);
                var particle = new List<string>();
                stage.Effect.GetParticleSystemNames(particle);
                Rec("systems/" + stage.Key, string.Join(", ", m_Names) + " | particle: " + string.Join(", ", particle));
            }
            StartCoroutine(EndOfFrames());
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfxevents probe failed: " + e);
            Finish();
        }
    }

    Stage Draw(string key, string graph, params int[] frames)
    {
        var origin = new Vector3(1000f + 100f * m_Stages.Count, 0f, 0f);
        var go = new GameObject("draw " + key);
        go.transform.position = origin;
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Graphs/" + graph + ".vfx");
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
        var stage = new Stage { Key = key, Effect = effect, Camera = camera, Frames = frames };
        m_Stages.Add(stage);
        return stage;
    }

    System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (!m_Done)
        {
            yield return wait;
            Shoot();
        }
    }

    void Shoot()
    {
        string frame = "f" + m_Frame.ToString("000");
        var particle = new List<string>();
        foreach (var stage in m_Stages)
        {
            // Every frame: what the effect and each of its particle systems answer.
            var row = new StringBuilder();
            row.Append("alive=").Append(stage.Effect.aliveParticleCount).Append(" culled=").Append(stage.Effect.culled ? 1 : 0)
                .Append(" awake=").Append(stage.Effect.HasAnySystemAwake() ? 1 : 0);
            particle.Clear();
            stage.Effect.GetParticleSystemNames(particle);
            foreach (var name in particle)
            {
                try
                {
                    var info = stage.Effect.GetParticleSystemInfo(name);
                    row.Append(" | ").Append(name).Append(": alive=").Append(info.aliveCount).Append(" capacity=").Append(info.capacity).Append(" sleeping=").Append(info.sleeping ? 1 : 0);
                }
                catch (ArgumentException e)
                {
                    row.Append(" | ").Append(name).Append(": ").Append(e.GetType().Name);
                }
            }
            if (Array.IndexOf(k_RowFrames, m_Frame) >= 0) Rec("sim/" + stage.Key + "/" + frame, row.ToString());
            if (Array.IndexOf(stage.Frames, m_Frame) < 0) continue;
            var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
            if (string.IsNullOrEmpty(dir)) continue;
            if (m_Shot == null) m_Shot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var active = RenderTexture.active;
            RenderTexture.active = stage.Camera.targetTexture;
            m_Shot.ReadPixels(new Rect(0f, 0f, 64f, 64f), 0, 0);
            m_Shot.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(Path.Combine(dir, "draw-" + stage.Key + "-" + frame + ".png"), m_Shot.EncodeToPNG());
        }
    }

    void Update()
    {
        if (m_Done) return;
        m_Frame++;
        if (m_Frame == 30)
        {
            m_Reinit.Effect.Reinit();
            m_Stopped.Effect.Stop();
        }
        if (m_Frame < k_LastFrame) return;
        Finish();
    }

    /// <summary>The graphs imported while the probe plays. None must be: the capture lets the
    /// Editor settle before it plays (Assets/Editor/ProbeRunner.cs).</summary>
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
        m_Done = true;
        Rec("capture/graphs imported while playing", string.Join(", ", ImportsWhilePlaying.Graphs.OrderBy(p => p, StringComparer.Ordinal)));
        // A capture with such an import is not one to publish: it stays incomplete.
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfxevents probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
