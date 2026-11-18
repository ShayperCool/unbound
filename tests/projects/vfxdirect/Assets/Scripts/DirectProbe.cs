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
public sealed class DirectProbe : MonoBehaviour
{
    const int k_LastFrame = 46;
    /// <summary>The frames whose rows are recorded.</summary>
    static readonly int[] k_RowFrames = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 20, 21, 22, 23, 25, 26, 27, 28, 30, 31, 32, 33, 34, 36, 40, 44 };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Stage> m_Stages = new List<Stage>();
    readonly List<string> m_Names = new List<string>();
    Texture2D m_Shot;
    Stage m_Hit, m_Keep, m_OnPlay, m_Both, m_Two, m_Paused;
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
        new GameObject("DirectProbe").AddComponent<DirectProbe>();
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

    static VFXEventAttribute Attribute(Stage stage, float count, Vector3 position, Color color)
    {
        var attribute = stage.Effect.CreateVFXEventAttribute();
        attribute.SetFloat("spawnCount", count);
        attribute.SetVector3("position", position);
        attribute.SetVector3("color", new Vector3(color.r, color.g, color.b));
        return attribute;
    }

    /// <summary>Sends the event `Hit` asking for <paramref name="count"/> particles at a place
    /// and of a colour.</summary>
    static void Send(Stage stage, float count, Vector3 position, Color color) => stage.Effect.SendEvent("Hit", Attribute(stage, count, position, color));

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");

    void Start()
    {
        try
        {
            // What each effect is sent, and when, is in Update below.
            m_Hit = Draw("hit", "DirectHit", 6, 11, 16, 21, 26, 34);
            m_Keep = Draw("keep", "DirectKeep", 6, 11, 16, 21);
            m_OnPlay = Draw("on play", "DirectOnPlay", 4, 11, 16, 22, 34);
            m_Both = Draw("both", "DirectAndSpawner", 6, 11, 13, 17, 20);
            m_Two = Draw("two", "DirectTwo", 6, 11);
            m_Paused = Draw("paused", "DirectHit", 9, 12, 17, 20);
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
            Debug.LogError("vfxdirect probe failed: " + e);
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
        switch (m_Frame)
        {
            case 5:
                // Without attributes: the asset's own (a count of one, at the origin).
                m_Hit.Effect.SendEvent("Hit");
                Send(m_Keep, 3f, new Vector3(-0.8f, 0.6f, 0f), Color.white);
                Send(m_Both, 4f, new Vector3(0.8f, 0.6f, 0f), Color.red);
                Send(m_Two, 3f, new Vector3(0.5f, 0.5f, 0f), Color.green);
                break;
            case 8: m_Paused.Effect.pause = true; break;
            case 12:
                // An event in a frame the spawn context spawns in too (it spawns one every
                // six frames, from frame 6 on): three, of a colour and at a place of their own.
                Send(m_Both, 3f, new Vector3(-0.8f, -0.6f, 0f), Color.green);
                break;
            case 10:
                Send(m_Hit, 4f, new Vector3(0.8f, 0.6f, 0f), Color.red);
                Send(m_Keep, 3f, new Vector3(0.8f, 0.6f, 0f), Color.red);
                // The effect's start event again, by its name.
                m_OnPlay.Effect.SendEvent("OnPlay");
                Send(m_Paused, 4f, new Vector3(0.8f, 0.6f, 0f), Color.red);
                break;
            case 15:
                // Two events in one frame, each with attributes of its own.
                Send(m_Hit, 2f, new Vector3(-0.8f, 0.6f, 0f), Color.green);
                Send(m_Hit, 3f, new Vector3(-0.8f, -0.6f, 0f), Color.blue);
                // More than the room left: three asked for, two fit.
                Send(m_Keep, 3f, new Vector3(-0.8f, -0.6f, 0f), Color.green);
                // The start event through Play, with a count.
                m_OnPlay.Effect.Play(Attribute(m_OnPlay, 3f, new Vector3(0.6f, 0f, 0f), Color.yellow));
                break;
            case 16: m_Paused.Effect.pause = false; break;
            case 20:
                // A count that is not whole; a system without room; an effect started over.
                Send(m_Hit, 2.7f, new Vector3(0.8f, -0.6f, 0f), Color.yellow);
                Send(m_Keep, 3f, new Vector3(0.8f, -0.6f, 0f), Color.blue);
                m_OnPlay.Effect.Reinit();
                break;
            case 25:
                // More than the system's room.
                Send(m_Hit, 100f, new Vector3(0f, 0.9f, 0f), Color.cyan);
                // On the system that kills nothing, which counts what it spawns at once and
                // has room for seven more since it started over: a count that is not whole.
                m_OnPlay.Effect.SendEvent("OnPlay", Attribute(m_OnPlay, 2.7f, new Vector3(-0.6f, 0.5f, 0f), Color.yellow));
                break;
            case 28:
                // Less than one.
                m_OnPlay.Effect.SendEvent("OnPlay", Attribute(m_OnPlay, 0.5f, new Vector3(-0.6f, -0.5f, 0f), Color.magenta));
                break;
            case 30:
                // Less than none.
                m_OnPlay.Effect.SendEvent("OnPlay", Attribute(m_OnPlay, -1f, new Vector3(0.6f, -0.5f, 0f), Color.cyan));
                break;
            case 32:
                // Less than one.
                Send(m_Hit, 0.5f, new Vector3(0f, -0.9f, 0f), Color.magenta);
                break;
            case 33:
                // Less than none.
                Send(m_Hit, -1f, new Vector3(0f, -0.9f, 0f), Color.magenta);
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
        if (ImportsWhilePlaying.Graphs.Count != 0) Debug.LogError("vfxdirect probe: graphs were imported while playing; the capture is not complete");
        else m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
    }
}
#endif
