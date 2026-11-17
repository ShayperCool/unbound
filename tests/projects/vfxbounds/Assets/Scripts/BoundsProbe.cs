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
public sealed class BoundsProbe : MonoBehaviour
{
    const int k_LastFrame = 46;
    /// <summary>The frames whose rows are recorded.</summary>
    static readonly int[] k_FewRows = { 4, 10, 30 };
    static readonly int[] k_RowFrames = { 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 19, 20, 21, 22, 23, 24, 25, 26, 28, 30, 31, 32, 33, 34, 36, 40, 44 };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Stage> m_Stages = new List<Stage>();
    readonly List<string> m_Names = new List<string>();
    Texture2D m_Shot;
    Stage m_Padded, m_Stopped, m_Reinit, m_Moved, m_StoppedPadded;
    readonly System.Reflection.MethodInfo m_ComputedBounds = typeof(VisualEffect).GetMethod("GetComputedBounds",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
    readonly System.Reflection.MethodInfo m_BoundsPadding = typeof(VisualEffect).GetMethod("GetCurrentBoundsPadding",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
    int m_Frame;
    bool m_Done;

    sealed class Stage
    {
        public string Key;
        public VisualEffect Effect;
        public Camera Camera;
        public int[] Frames;
        /// <summary>Whether the effect's graph computes its particles' bounds.</summary>
        public bool Computes;
        /// <summary>The frames whose rows are recorded.</summary>
        public int[] Rows = k_RowFrames;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("BoundsProbe").AddComponent<BoundsProbe>();
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

    static string Text(Vector3 v) => v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        + "," + v.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static string Matrix(Matrix4x4 m)
    {
        var text = new StringBuilder();
        for (int i = 0; i < 16; i++) text.Append(i == 0 ? "" : ",").Append(m[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }

    static string Box(Bounds b) => "(" + Text(b.center) + ")/(" + Text(b.extents) + ")";

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");

    void Start()
    {
        try
        {
            Draw("auto", "BoundsAuto", 10, 30);
            // The padding set to another value at frame 20.
            m_Padded = Draw("padded", "BoundsPadded", 10, 30);
            // A system in world space; and one of an effect that is turned (three rows: its
            // box differs in the last places with the arithmetic of the shader compiler).
            Draw("world", "BoundsWorld", 10, 30);
            var turned = Draw("world turned", "BoundsWorldTurned", 10, 30);
            turned.Effect.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            turned.Rows = k_FewRows;
            Draw("dying", "BoundsDying", 10, 30);
            // The spawner stopped at frame 20: the last particles die, and none is left.
            m_Stopped = Draw("stopped", "BoundsDying", 10, 30, 40);
            // Particles that leave the view: the effect is culled once its bounds have.
            Draw("away", "BoundsAway", 6, 12, 30);
            Draw("manual away", "ManualAway", 6, 12, 30);
            // Started over at frame 20, and moved half a unit at frame 20.
            m_Reinit = Draw("reinit", "BoundsReinit", 10, 22, 30);
            m_Moved = Draw("moved", "BoundsMoved", 10, 22, 30);
            // Two effects of one graph whose particles never die, the second half a unit up.
            // Two effects of one graph whose particles never die, the second half a unit up
            // and with another seed (three rows each: the Editor gives the first the particles
            // of both and the second none).
            Draw("shared", "BoundsShared", 10).Rows = k_FewRows;
            var second = Draw("shared second", "BoundsShared", 10);
            second.Rows = k_FewRows;
            second.Effect.transform.position += new Vector3(0f, 0.5f, 0f);
            second.Effect.startSeed = 12;
            second.Effect.Reinit();
            // Local space on a turned effect; a padded system whose particles all die after its
            // spawner stops at frame 20; and more particles than one thread group.
            Draw("turned", "BoundsTurned", 10, 30).Effect.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            m_StoppedPadded = Draw("stopped padded", "BoundsDyingPadded", 10, 40);
            Draw("many", "BoundsMany", 10, 30);
            // What the turned effect is given of its transform.
            Rec("matrix/world turned/to world", Matrix(turned.Effect.transform.localToWorldMatrix));
            Rec("matrix/world turned/to local", Matrix(turned.Effect.transform.worldToLocalMatrix));
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
            Debug.LogError("vfxbounds probe failed: " + e);
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
        var stage = new Stage { Key = key, Effect = effect, Camera = camera, Frames = frames, Computes = graph != "ManualAway" };
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
            // What the effect answers, the bounds its renderer is culled by, and of each
            // particle system the bounds it answers, the bounds computed from its particles
            // and its padding.
            var row = new StringBuilder();
            var renderer = stage.Effect.GetComponent<Renderer>();
            row.Append("alive=").Append(stage.Effect.aliveParticleCount).Append(" culled=").Append(stage.Effect.culled ? 1 : 0)
                .Append(" renderer=").Append(Box(renderer.bounds));
            particle.Clear();
            stage.Effect.GetParticleSystemNames(particle);
            foreach (var name in particle)
            {
                int id = Shader.PropertyToID(name);
                row.Append(" | ").Append(name).Append(": bounds=").Append(Box(stage.Effect.GetParticleSystemInfo(name).bounds))
                    .Append(" computed=").Append(Box((Bounds)m_ComputedBounds.Invoke(stage.Effect, new object[] { id })));
                // The padding is asked of systems that compute their bounds only: the Editor
                // does not survive the question about another.
                if (stage.Computes) row.Append(" padding=").Append(Text((Vector3)m_BoundsPadding.Invoke(stage.Effect, new object[] { id })));
            }
            if (Array.IndexOf(stage.Rows, m_Frame) >= 0) Rec("sim/" + stage.Key + "/" + frame, row.ToString());
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
        switch (m_Frame)
        {
            case 20:
                m_Padded.Effect.SetVector3("Padding", new Vector3(0.1f, 1f, 0f));
                m_Stopped.Effect.Stop();
                m_StoppedPadded.Effect.Stop();
                m_Reinit.Effect.Reinit();
                m_Moved.Effect.transform.position += new Vector3(0.5f, 0f, 0f);
                break;
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
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfxbounds probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
