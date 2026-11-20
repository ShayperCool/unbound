using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ToggleGroupProbe : MonoBehaviour
{
    [Serializable] public class Step
    {
        public int frame;
        public string command;
        public int x, y;
        public string key;
    }

    [Serializable] class Steps { public Step[] steps; }
    public static Action<int> Calibrate;
    public static Action<Step> Drive;
    readonly StringBuilder m_Values = new StringBuilder();
    readonly GUIContent m_InnerLabel = new GUIContent("Inner label");
    readonly GUIContent m_MixedLabel = new GUIContent("Mixed group");
    readonly GUIContent m_DisabledLabel = new GUIContent("Already disabled");
    Step[] m_Steps;
    int m_Frame;
    int m_NextStep;
    int m_InputCount;
    int m_ChildClicks;
    bool m_Layout;
    bool m_Repaint;
    bool m_Enabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("EDITOR_TOGGLE_GROUP_OUT") != null)
            new GameObject("EditorToggleGroup").AddComponent<ToggleGroupProbe>();
    }

    void Awake() => m_Steps = JsonUtility.FromJson<Steps>(Resources.Load<TextAsset>("ToggleGroupSteps").text).steps;

    void Update()
    {
        m_Frame++;
        if (m_Frame == 1 || m_Frame == 3 || m_Frame == 5) Calibrate?.Invoke(m_Frame);
        if (Drive != null && m_NextStep < m_Steps.Length && m_Steps[m_NextStep].frame == m_Frame)
            Drive(m_Steps[m_NextStep++]);
        if (m_Frame != 54) return;

        Put("input/count", m_InputCount.ToString(CultureInfo.InvariantCulture));
        Put("input/final", $"[{Flag(m_Enabled)},{m_ChildClicks}]");
        Put("complete", "true");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_TOGGLE_GROUP_OUT"), "values.jsonl"), m_Values.ToString());
        EditorApplication.Exit(0);
    }

    void OnGUI()
    {
        var e = Event.current;
        var type = e.type;
        int button = e.button;
        bool observeInput = type == EventType.MouseDown || type == EventType.MouseUp ||
            (type == EventType.KeyDown && e.keyCode == KeyCode.Space);
        DrawInteractiveGroup(observeInput, type, button);
        if (type == EventType.Layout && !m_Layout)
        {
            m_Layout = true;
            DrawGroups("Layout/");
        }
        else if (type == EventType.Repaint && !m_Repaint)
        {
            m_Repaint = true;
            DrawGroups("Repaint/");
        }
        else
        {
            DrawGroups(null);
        }
    }

    void DrawInteractiveGroup(bool observe, EventType type, int button)
    {
        GUILayout.BeginArea(new Rect(12, 12, 270, 70));
        try
        {
            EditorGUI.BeginChangeCheck();
            bool childEnabled;
            using (var group = new EditorGUILayout.ToggleGroupScope("Enable children", m_Enabled))
            {
                m_Enabled = group.enabled;
                childEnabled = GUI.enabled;
                var child = GUILayoutUtility.GetRect(85, 14);
                if (GUI.Button(child, "Child")) m_ChildClicks++;
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (observe)
            {
                bool used = Event.current.type == EventType.Used;
                bool focus = GUIUtility.keyboardControl != 0;
                bool hot = GUIUtility.hotControl != 0;
                string kind = type == EventType.MouseDown ? "down" : type == EventType.MouseUp ? "up" : "space";
                Put("input/" + m_InputCount++,
                    $"[\"{kind}\",{button},{Flag(m_Enabled)},{Flag(childEnabled)},{Flag(GUI.enabled)},{Flag(changed)},{Flag(used)},{Flag(focus)},{Flag(hot)},{m_ChildClicks}]");
            }
        }
        catch (Exception error)
        {
            if (observe) Put("input/" + m_InputCount++, $"\"{error.GetType().Name}\"");
        }
        finally { GUILayout.EndArea(); }
    }

    void DrawGroups(string phase)
    {
        GUILayout.BeginArea(new Rect(12, 86, 275, 145));
        try
        {
            bool outer = EditorGUILayout.BeginToggleGroup("Outer", false);
            State(phase, "outer", outer);
            Position(phase, "outerChild", GUILayoutUtility.GetRect(12, 6));
            using (var inner = new EditorGUILayout.ToggleGroupScope(m_InnerLabel, true))
            {
                State(phase, "inner", inner.enabled);
                Position(phase, "innerChild", GUILayoutUtility.GetRect(15, 8));
            }
            State(phase, "afterInner", outer);
            EditorGUILayout.EndToggleGroup();
            State(phase, "afterOuter", outer);

            var group = new EditorGUILayout.ToggleGroupScope("Enabled", true);
            State(phase, "enabled", group.enabled);
            Position(phase, "enabledChild", GUILayoutUtility.GetRect(16, 10));
            group.Dispose();
            State(phase, "disposed", group.enabled);
            group.Dispose();
            State(phase, "disposedAgain", group.enabled);
            Position(phase, "afterGroups", GUILayoutUtility.GetRect(17, 7));

            EditorGUI.BeginDisabledGroup(true);
            try
            {
                bool disabled = EditorGUILayout.BeginToggleGroup(m_DisabledLabel, true);
                State(phase, "disabledParent", disabled);
                EditorGUILayout.EndToggleGroup();
                State(phase, "restoredDisabledParent", disabled);
            }
            finally { EditorGUI.EndDisabledGroup(); }

            EditorGUI.showMixedValue = true;
            try
            {
                bool mixed = EditorGUILayout.BeginToggleGroup(m_MixedLabel, true);
                State(phase, "mixed", mixed);
                EditorGUILayout.EndToggleGroup();
                State(phase, "afterMixed", mixed);
            }
            finally { EditorGUI.showMixedValue = false; }
        }
        catch (Exception error)
        {
            if (phase != null) Put(phase + "exception", $"\"{error.GetType().Name}\"");
        }
        finally { GUILayout.EndArea(); }
    }

    void State(string phase, string name, bool value)
    {
        if (phase != null) Put(phase + name, $"[{Flag(value)},{Flag(GUI.enabled)}]");
    }

    void Position(string phase, string name, Rect rect)
    {
        if (phase != null) Put(phase + name, $"[{Number(rect.x)},{Number(rect.y)},{Number(rect.width)},{Number(rect.height)}]");
    }

    void Put(string key, string value) =>
        m_Values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    static string Flag(bool value) => value ? "true" : "false";
    static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
