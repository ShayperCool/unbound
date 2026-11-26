using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>What IMGUI calls do outside OnGUI, before the first OnGUI and between passes; see README.md.</summary>
public class GuiOutsideProbe : MonoBehaviour
{
    readonly List<string> m_Lines = new List<string>();
    int m_OnGuiCalls;
    int m_Frame;

    void Add(string key, string value) =>
        m_Lines.Add("{\"k\": \"" + key + "\", \"v\": \"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");

    void Try(string key, Func<object> call)
    {
        string text;
        try
        {
            var value = call();
            text = value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) { text = "throws " + e.GetType().Name + ": " + e.Message; }
        Add(key, text);
    }

    /// <summary>The calls, with keys under <paramref name="at"/>.</summary>
    void Record(string at)
    {
        Try(at + ".eventCurrent", () => Event.current == null ? "null" : Event.current.type.ToString());
        Try(at + ".skin", () => GUI.skin == null ? "null" : GUI.skin.name);
        Try(at + ".color", () => GUI.color);
        Try(at + ".depth", () => GUI.depth);
        Try(at + ".enabled", () => GUI.enabled);
        Try(at + ".matrix", () => GUI.matrix.isIdentity);
        Try(at + ".label", () => { GUI.Label(new Rect(0, 0, 50, 20), "x"); return "ok"; });
        Try(at + ".button", () => GUI.Button(new Rect(0, 0, 50, 20), "x"));
        Try(at + ".layoutLabel", () => { GUILayout.Label("x"); return "ok"; });
        Try(at + ".beginGroup", () => { GUI.BeginGroup(new Rect(0, 0, 10, 10)); GUI.EndGroup(); return "ok"; });
        Try(at + ".controlId", () => GUIUtility.GetControlID(FocusType.Passive) > 0 ? "positive" : "other");
        Try(at + ".hotControl", () => GUIUtility.hotControl);
        Try(at + ".calcSize", () => GUI.skin.label.CalcSize(new GUIContent("abc")).x > 0 ? "positive" : "zero");
    }

    void Start() => Record("start");

    void OnGUI() => m_OnGuiCalls++;

    void Update()
    {
        if (++m_Frame != 5) return;
        Add("onGuiCalls.positive", (m_OnGuiCalls > 0).ToString());
        Record("between");
        m_Lines.Add("{\"k\": \"complete\", \"v\": true}");
        string dir = Environment.GetEnvironmentVariable("GUI_OUTSIDE_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), m_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
