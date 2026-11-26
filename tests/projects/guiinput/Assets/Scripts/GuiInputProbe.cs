using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>The order OnGUI behaviours get input events; see README.md. The controller counts
/// frames (Time.frameCount) in Update, before A and B; the steps are played as input (the Editor's
/// driver, Unbound's --move/--keys); each phase records the calls of its frames' input passes.</summary>
[DefaultExecutionOrder(-100)]
public class GuiInputProbe : MonoBehaviour
{
    /// <summary>Input played at a frame: "@x,y" moves the pointer (game pixels from the top
    /// left), "+Mouse0"/"-Mouse0", "+A"/"-A" press and release.</summary>
    public struct Step
    {
        public int frame;
        public string[] tokens;
        public override string ToString() => frame + ":" + string.Join(",", tokens);
    }

    static Step S(int frame, string tokens) => new Step { frame = frame, tokens = tokens.Split(' ') };

    public static readonly Step[] Steps =
    {
        // The pointer over the game view and a click that gives the view the focus.
        S(5, "@160,90"), S(10, "+Mouse0"), S(12, "-Mouse0"),
        // Each phase: a click and a key press and release within a frame.
        S(40, "+Mouse0 -Mouse0 +A -A"),
        S(70, "+Mouse0 -Mouse0 +A -A"),
        S(100, "+Mouse0 -Mouse0 +A -A"),
        S(130, "+Mouse0 -Mouse0"),
    };

    /// <summary>The phases: their first frame, name and what the behaviours do in them.</summary>
    internal static string Phase(int frame) =>
        frame < 30 ? null : frame < 60 ? "tie" : frame < 90 ? "aLower" : frame < 120 ? "bLower" : frame < 150 ? "used" : null;

    /// <summary>Set by the Editor's driver: plays a step.</summary>
    public static Action<Step> driver;

    static readonly List<string> s_Lines = new List<string>();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    internal static readonly List<string> Calls = new List<string>();
    internal static readonly StringBuilder Updates = new StringBuilder();
    static readonly Dictionary<string, StringBuilder> s_Phases = new Dictionary<string, StringBuilder>();

    static void Add(string key, object value)
    {
        if (!s_Keys.Add(key)) return;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture);
        s_Lines.Add("{\"k\": \"" + key + "\", \"v\": \"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
    }

    void Update()
    {
        int frame = Time.frameCount;
        // The calls since the last Repaint are this frame's input pass; A and B by the order of
        // their Updates (U1, U2: the scene's enable order is unspecified).
        int last = Calls.FindLastIndex(c => c.EndsWith(":Repaint", StringComparison.Ordinal));
        var input = Calls.GetRange(last + 1, Calls.Count - last - 1);
        Calls.Clear();
        string updates = Updates.ToString();
        Updates.Clear();
        string phase = Phase(frame);
        if (phase != null && input.Count > 0 && updates.Length == 2)
        {
            if (!s_Phases.TryGetValue(phase, out var log)) s_Phases[phase] = log = new StringBuilder();
            foreach (var call in input)
                log.Append(call[0] == updates[0] ? "U1" : "U2").Append(call.Substring(1)).Append(' ');
            Add(phase + ".lower", phase == "aLower" ? (updates[0] == 'A' ? "U1" : "U2") : phase == "bLower" ? (updates[0] == 'B' ? "U1" : "U2") : "none");
        }
        foreach (var step in Steps)
            if (step.frame == frame) driver?.Invoke(step);
        if (frame < 160) return;
        foreach (var name in new[] { "tie", "aLower", "bLower", "used" })
            Add(name + ".input", s_Phases.TryGetValue(name, out var log) ? log.ToString() : "");
        s_Lines.Add("{\"k\": \"complete\", \"v\": true}");
        string dir = Environment.GetEnvironmentVariable("GUI_INPUT_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), s_Lines);
        }
        enabled = false;
        finished?.Invoke();
#if !UNITY_EDITOR
        Application.Quit();
#endif
    }

    /// <summary>Set by the Editor's driver: the run is over.</summary>
    public static Action finished;
}
