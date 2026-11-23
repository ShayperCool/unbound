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
public sealed class StripProbe : MonoBehaviour
{
    const int k_LastFrame = 72;
    /// <summary>The frames whose rows are recorded: the first ones, and around the alive counts' second answer.</summary>
    static readonly int[] k_RowFrames = { 2, 3, 4, 5, 6, 7, 8, 10, 20, 30, 31, 32, 33, 34, 35, 36, 37, 40, 45, 50, 54, 55, 56, 57, 58, 60, 62, 63, 64, 65, 66, 70 };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Stage> m_Stages = new List<Stage>();
    readonly List<string> m_Names = new List<string>();
    Texture2D m_Shot;
    Stage m_Index, m_Reinit, m_TrailsReinit, m_Toggled, m_Stopped, m_Paused, m_AloneReinit, m_DyingReinit, m_WakesFirst, m_WakesSecond, m_WakesThird;
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
        new GameObject("StripProbe").AddComponent<StripProbe>();
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
            Draw("single", "StripSingle", 4, 10, 20, 40, 66);
            Draw("keep", "StripKeep", 10, 20, 40, 66);
            Draw("trails", "StripTrails", 4, 10, 20, 40, 66);
            Draw("trails keep", "StripTrailsKeep", 10, 20, 40, 66);
            Draw("both", "StripBoth", 10, 20, 40);
            Draw("taper", "StripTaper", 10, 20, 40);
            Draw("image", "StripImage", 10, 20, 40);
            Draw("image segments", "StripImageSegments", 10, 20, 40);
            Draw("image swapped", "StripImageSwapped", 10, 20, 40);
            // The strip the particles go into changed: the third at frame 20, one the system
            // has not at frame 40, the second at frame 50.
            m_Index = Draw("index", "StripIndex", 10, 19, 24, 30, 39, 44, 49, 54, 60);
            // The same graphs started over at frame 30.
            m_Reinit = Draw("reinit", "StripSingle", 20, 29, 32, 40, 66);
            m_TrailsReinit = Draw("trails reinit", "StripTrails", 20, 29, 32, 40, 66);
            // One whose object is inactive from frame 30 to frame 34.
            m_Toggled = Draw("toggled", "StripSingle", 20, 29, 36, 50, 66);
            // One whose spawner is stopped at frame 30 and plays again at frame 55: its strip
            // empties between.
            m_Stopped = Draw("stopped", "StripSingle", 20, 29, 32, 40, 50, 54, 58, 66);
            // Two effects of an asset that is not instanced, and two of an instanced graph
            // without strips: one of each started over at frame 30. Whether the other's alive
            // count changes with it.
            Draw("alone", "StripAlone", 20, 40);
            m_AloneReinit = Draw("alone reinit", "StripAlone", 20, 40);
            Draw("dying", "Dying", 20, 40);
            m_DyingReinit = Draw("dying reinit", "Dying", 20, 40);
            // A third effect of that graph, whose component does not allow instancing.
            Draw("dying apart", null, 20, 40);
            // Effects of an instanced graph: one started over at frame 30 and one at frame 31
            // (and a fourth, below, at frame 32), so that they wake one frame after the other.
            Draw("wakes", "Waking", 20, 40);
            m_WakesFirst = Draw("wakes first", "Waking", 20, 40);
            m_WakesSecond = Draw("wakes second", "Waking", 20, 40);
            Draw("alpha", "StripAlpha", 10, 20, 40, 66);
            Draw("keep two", "StripKeepTwo", 10, 20, 40, 66);
            // Strips spawned by GPU events, paused from frame 20 to frame 36.
            m_Paused = Draw("paused", "StripTrails", 10, 19, 21, 30, 35, 37, 40, 50);
            // A fourth effect of the graph whose effects wake one frame after the other,
            // started over at frame 32.
            m_WakesThird = Draw("wakes third", "Waking", 20, 40);
            // Heads that die, each kept until the strip it spawned is empty (strips whose
            // particles die, and strips whose particles never die).
            Draw("trails alive", "StripTrailsAlive", 4, 8, 12, 16, 20, 24, 28, 32, 40, 50, 66);
            Draw("trails alive keep", "StripTrailsAliveKeep", 4, 8, 12, 16, 20, 24, 28, 32, 40, 50, 66);
            // Strips drawn from lists that are not sorted: through an indirect buffer (with the
            // particles as quads from a list culled in a compute pass), and culled by the
            // camera's frustum.
            Draw("listed", "StripListed", 10, 20, 40, 66);
            Draw("culled", "StripCulled", 10, 20, 40, 66);
            // Lit strips under a sun that casts no shadow (the other stages are unlit) and a
            // point light of their own, in front and to the right.
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            foreach (var (key, graph) in new[] { ("lit", "StripLit"), ("lit bent", "StripLitBent"), ("meshes", "StripMeshes"), ("meshes lit", "StripMeshesLit") })
            {
                var lit = Draw(key, graph, 10, 20, 40, 66);
                var lamp = new GameObject("lamp " + key).AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.6f, 0.3f);
                lamp.range = 4f;
                lamp.intensity = 2f;
                lamp.shadows = LightShadows.None;
                lamp.transform.position = lit.Effect.transform.position + new Vector3(0.8f, 0.6f, -1f);
            }
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
            Debug.LogError("vfxstrips probe failed: " + e);
            Finish();
        }
    }

    Stage Draw(string key, string graph, params int[] frames)
    {
        var origin = new Vector3(1000f + 100f * m_Stages.Count, 0f, 0f);
        VisualEffect effect;
        if (graph == null)
        {
            // The effect as a prefab holds it, with its graph and seed.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Objects/DyingApart.prefab");
            effect = Instantiate(prefab, origin, Quaternion.identity).GetComponent<VisualEffect>();
        }
        else
        {
            var go = new GameObject("draw " + key);
            go.transform.position = origin;
            effect = go.AddComponent<VisualEffect>();
            effect.resetSeedOnPlay = false;
            effect.startSeed = 11;
            effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Graphs/" + graph + ".vfx");
        }
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
        switch (m_Frame)
        {
            case 20:
                m_Index.Effect.SetUInt("Strip", 2u);
                m_Paused.Effect.pause = true;
                break;
            case 30:
                m_Reinit.Effect.Reinit();
                m_TrailsReinit.Effect.Reinit();
                m_AloneReinit.Effect.Reinit();
                m_DyingReinit.Effect.Reinit();
                m_WakesFirst.Effect.Reinit();
                m_Stopped.Effect.Stop();
                m_Toggled.Effect.gameObject.SetActive(false);
                break;
            case 31: m_WakesSecond.Effect.Reinit(); break;
            case 32: m_WakesThird.Effect.Reinit(); break;
            case 34: m_Toggled.Effect.gameObject.SetActive(true); break;
            case 36: m_Paused.Effect.pause = false; break;
            case 40: m_Index.Effect.SetUInt("Strip", 7u); break;
            case 50: m_Index.Effect.SetUInt("Strip", 1u); break;
            case 55: m_Stopped.Effect.Play(); break;
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
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfxstrips probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
