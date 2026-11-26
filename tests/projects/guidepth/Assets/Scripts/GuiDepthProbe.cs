using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>GUI.depth inside and outside OnGUI with two OnGUI behaviours; see README.md.
/// The controller counts frames in Update; the behaviours log the depth each reads as its OnGUI
/// starts, per frame and event, and set it on given frames.</summary>
// Before A and B: it takes the previous frame's orders before they log the new frame.
[DefaultExecutionOrder(-100)]
public class GuiDepthProbe : MonoBehaviour
{
    static readonly List<string> s_Lines = new List<string>();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    internal static int Frame;
    /// <summary>The order of the frame's Updates, Layouts and Repaints of A and B, and the depth
    /// each left at the end of its Layout.</summary>
    internal static readonly System.Text.StringBuilder Updates = new System.Text.StringBuilder();
    internal static readonly System.Text.StringBuilder Layouts = new System.Text.StringBuilder();
    internal static readonly System.Text.StringBuilder Repaints = new System.Text.StringBuilder();
    internal static readonly int[] LayoutDepth = new int[2];

    /// <summary>The previous frame's order, apart from which of A and B the scene happens to
    /// enable first (unspecified): whether the Layouts follow the Updates; the Repaints by name
    /// where the depths differ, else whether they reverse the Layouts.</summary>
    static void Orders(int frame)
    {
        string updates = Updates.ToString(), layouts = Layouts.ToString(), repaints = Repaints.ToString();
        Add("f" + frame + ".layoutsAsUpdates", layouts == updates);
        if (LayoutDepth[0] != LayoutDepth[1]) Add("f" + frame + ".repaints", repaints);
        else
        {
            var reversed = layouts.ToCharArray();
            System.Array.Reverse(reversed);
            Add("f" + frame + ".repaintsReverseLayouts", repaints == new string(reversed));
        }
    }

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
        if (Frame > 1) Orders(Frame - 1);
        Updates.Clear();
        Layouts.Clear();
        Repaints.Clear();
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
