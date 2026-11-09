using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// What a screen read returns before the frame's first camera has drawn: on the first rendered
/// frame, after the Game view's rendering resolution changes (64x32 to 80x40 and back) and on
/// ordinary frames.
///
/// One camera renders to the screen. Before it renders (<c>Camera.onPreRender</c>) the probe reads
/// the screen with <c>Texture2D.ReadPixels</c> (<c>pre.r{k}</c>, k counting rendered frames from 0)
/// and again at the end of the frame (<c>eof.r{k}</c>). The camera clears to
/// <c>(k/8, 0.5, 0.75, 0.6)</c>, except on the frames listed in <see cref="Keeps"/>, where it clears
/// only depth, so the end-of-frame read shows what the screen started with. Each read records the
/// screen size and its distinct RGBA32 colors with their pixel counts. The rendering resolution is
/// set at the end of the frames in <see cref="Resizes"/> with
/// <c>UnityEditor.PlayModeWindow.SetCustomRenderingResolution</c>, recording <c>Screen</c>'s size
/// right after the call (<c>resize.r{k}</c>).
/// </summary>
public class ScreenReadProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int Frames = 10;
    /// <summary>Rendered frames at whose end the rendering resolution changes, and the size.</summary>
    static readonly Dictionary<int, Vector2Int> Resizes = new Dictionary<int, Vector2Int>
    {
        { 2, new Vector2Int(80, 40) },
        { 5, new Vector2Int(64, 32) },
    };
    /// <summary>Rendered frames whose camera keeps the screen's color (clears only depth).</summary>
    static readonly HashSet<int> Keeps = new HashSet<int> { 0, 2, 3, 6, 8 };

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    string directory;
    Camera cam;
    int renders, readBefore = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT") == null) return;
        new GameObject("ScreenReadProbe").AddComponent<ScreenReadProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);

    void Awake()
    {
        directory = Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT");
        Directory.CreateDirectory(directory);
        var go = new GameObject("Camera");
        cam = go.AddComponent<Camera>();
        cam.allowMSAA = false;
        cam.allowHDR = false;
        cam.orthographic = true;
        Camera.onPreRender += PreRender;
        Application.logMessageReceived += OnLog;
    }

    void PreRender(Camera c)
    {
        // The Editor renders its first frame twice: only the first render of a frame is read.
        if (c != cam || renders >= Frames || readBefore == renders) return;
        readBefore = renders;
        Read("pre.r" + renders);
        bool keep = Keeps.Contains(renders);
        cam.clearFlags = keep ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color((renders % 8) / 8f, 0.5f, 0.75f, 0.6f);
    }

    IEnumerator Start()
    {
        while (renders < Frames)
        {
            yield return new WaitForEndOfFrame();
            Read("eof.r" + renders);
            if (Resizes.TryGetValue(renders, out var size))
            {
#if UNITY_EDITOR
                UnityEditor.PlayModeWindow.SetCustomRenderingResolution((uint)size.x, (uint)size.y, "ScreenReadProbe");
#endif
                Value("resize.r" + renders, "{\"size\":[" + size.x + "," + size.y + "],\"screen\":[" + Screen.width + "," + Screen.height + "]}");
            }
            renders++;
        }
        Camera.onPreRender -= PreRender;
        Application.logMessageReceived -= OnLog;
        Value("complete", "true");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
        if (finished != null) finished(0);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit(0);
#endif
        }
    }

    void Read(string key)
    {
        logs.Clear();
        int w = Screen.width, h = Screen.height;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        texture.Apply();
        var counts = new Dictionary<Color32, int>();
        foreach (var p in texture.GetPixels32())
            counts[p] = counts.TryGetValue(p, out int n) ? n + 1 : 1;
        Destroy(texture);
        var s = new StringBuilder("{\"screen\":[").Append(w).Append(',').Append(h).Append("],\"colors\":[");
        bool first = true;
        foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key.r).ThenBy(p => p.Key.g).ThenBy(p => p.Key.b).ThenBy(p => p.Key.a))
        {
            s.Append(first ? "" : ",").Append('[').Append(pair.Key.r).Append(',').Append(pair.Key.g).Append(',').Append(pair.Key.b).Append(',').Append(pair.Key.a).Append(',').Append(pair.Value).Append(']');
            first = false;
        }
        s.Append(']');
        if (logs.Count > 0) s.Append(",\"logs\":[").Append(string.Join(",", logs.Select(Q))).Append(']');
        Value(key, s.Append('}').ToString());
    }
}
