using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Halos and lens flares in the built-in pipeline: what a camera with a FlareLayer draws for the
/// scene's Halo components, lights with a halo, LensFlare components and lights with a flare,
/// read back from a float target frame by frame (fixed 1/30 s frames).
/// </summary>
public partial class FlareProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int W = 128, H = 128;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    bool capturing;
    string directory;
    Camera cam;
    RenderTexture target;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("FLARES_PROBE_OUT") == null) return;
        new GameObject("FlareProbe").AddComponent<FlareProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string N(UnityEngine.Object o) => o == null ? "null" : Q(o.name);

    void OnLog(string condition, string stack, LogType type)
    {
        if (capturing) logs.Add(type + ": " + condition);
    }

    void BeginLogs() { logs.Clear(); capturing = true; }
    void EndLogs(string key)
    {
        capturing = false;
        var s = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) s.Append(i > 0 ? "," : "").Append(Q(logs[i]));
        Value(key + ".log", s.Append("]").ToString());
        logs.Clear();
    }

    void Read(string key, RenderTexture rt, int width, int height)
    {
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = saved;
        var pixels = image.GetPixels();
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var c in pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        Value(key + ".pixels", pixels.Length.ToString());
        DestroyImmediate(image);
    }

    void Flush()
    {
        File.AppendAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
        records.Clear();
    }

    static GameObject SceneObject(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        throw new Exception("scene object not found: " + name);
    }

    void Start()
    {
        directory = Environment.GetEnvironmentVariable("FLARES_PROBE_OUT");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), "");
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    void MakeCamera()
    {
        target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
        target.Create();
        cam = new GameObject("ProbeCamera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, 0f, -5f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 100f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.targetTexture = target;
        cam.gameObject.AddComponent<FlareLayer>();
    }

    /// <summary>Waits for the end of <paramref name="frames"/> frames and reads the camera's image.</summary>
    IEnumerator Frames(string key, int frames, int every = 0)
    {
        for (int i = 1; i <= frames; i++)
        {
            BeginLogs();
            yield return new WaitForEndOfFrame();
            EndLogs(key + ".f" + i);
            if (i == frames || (every > 0 && i % every == 0)) Read(key + ".f" + i, target, W, H);
        }
        Flush();
    }

    IEnumerator Run()
    {
        int code = 0;
        Time.captureDeltaTime = 1f / 30f;
        yield return null;
        // Nested cases run from one stack, so an exception anywhere ends the probe with a record.
        var stack = new Stack<IEnumerator>();
        stack.Push(Probe());
        while (stack.Count > 0)
        {
            object current;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception e)
            {
                Value("fatal", Q(e.ToString()));
                code = 3;
                break;
            }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        Value("complete", "true");
        Flush();
        Application.logMessageReceived -= OnLog;
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
