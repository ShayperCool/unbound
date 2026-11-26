using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>GUI.depth inside and outside OnGUI with two OnGUI behaviours; see README.md.
/// The controller counts frames in Update; the behaviours log the depth each reads as its OnGUI
/// starts, per frame and event, and set it on given frames.</summary>
public class GuiDepthProbe : MonoBehaviour
{
    static readonly List<string> s_Lines = new List<string>();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    internal static int Frame;
    internal static readonly System.Text.StringBuilder Order = new System.Text.StringBuilder();

    internal static void Add(string key, object value)
    {
        if (!s_Keys.Add(key)) return;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture);
        s_Lines.Add("{\"k\": \"" + key + "\", \"v\": \"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
    }

    void Awake() => Add("awake.depth", GUI.depth);

    void Start() => Add("start.depth", GUI.depth);

    void Update()
    {
        Frame++;
        if (Frame > 1) Add("f" + (Frame - 1) + ".order", Order.ToString());
        Order.Clear();
        Add("f" + Frame + ".update.depth", GUI.depth);
        // Set from outside OnGUI on frame 6.
        if (Frame == 6)
        {
            GUI.depth = 7;
            Add("f6.update.afterSet", GUI.depth);
        }
        if (Frame < 14) return;
        Add("complete", true);
        s_Lines.RemoveAll(l => l.StartsWith("{\"k\": \"complete\""));
        s_Lines.Add("{\"k\": \"complete\", \"v\": true}");
        string dir = Environment.GetEnvironmentVariable("GUI_DEPTH_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), s_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
