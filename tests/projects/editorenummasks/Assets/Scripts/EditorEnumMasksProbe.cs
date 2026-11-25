using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

#pragma warning disable 618 // the measured members are obsolete

/// <summary>
/// Records, in a game's OnGUI in Play mode, what the deprecated EditorGUI and EditorGUILayout
/// EnumMaskField and EnumMaskPopup answer: outside OnGUI; in Layout and Repaint (returned value
/// and its type, control ids taken, GUI.changed, layout rects) for flags and plain enums with no,
/// one, some and every flag set and a mixed value; and the log messages of the run. No clicks are
/// scripted: a click opens the flags' menu.
/// </summary>
public sealed class EditorEnumMasksProbe : MonoBehaviour
{
    [Flags] public enum Flags { None = 0, Red = 1, Green = 2, Blue = 4, Big = 8 }
    public enum Plain { First, Second, Third }

    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_ENUM_MASKS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorEnumMasksProbe");
        var probe = go.AddComponent<EditorEnumMasksProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        Application.logMessageReceived += (message, stack, type) => probe.m_Logs.Add(type + ": " + message);
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object Try(Func<object> read)
    {
        try
        {
            var value = read();
            return value is Enum e ? (object)(e.GetType().Name + ":" + Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture)) : value;
        }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 200, 18);
        Put("outside/editorGUI",
            Try(() => EditorGUI.EnumMaskField(r, Flags.Red)), Try(() => EditorGUI.EnumMaskField(r, "L", Flags.Red)),
            Try(() => EditorGUI.EnumMaskField(r, Flags.Red, EditorStyles.miniButton)),
            Try(() => EditorGUI.EnumMaskPopup(r, "L", Flags.Red)), Try(() => EditorGUI.EnumMaskPopup(r, new GUIContent("L"), Flags.Red, EditorStyles.miniButton)));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.EnumMaskField(Flags.Red)), Try(() => EditorGUILayout.EnumMaskField("L", Flags.Red)),
            Try(() => EditorGUILayout.EnumMaskPopup("L", Flags.Red)));
    }

    object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        return new object[] { value, after == -1 ? "used" : (object)(after - before - 1), changed };
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 18);
        var label = new GUIContent("Label");
        Put($"rect/{phase}/enumMaskField",
            Field(() => EditorGUI.EnumMaskField(r, Flags.None)), Field(() => EditorGUI.EnumMaskField(r, Flags.Red)),
            Field(() => EditorGUI.EnumMaskField(r, Flags.Red | Flags.Blue)), Field(() => EditorGUI.EnumMaskField(r, Flags.Red | Flags.Green | Flags.Blue | Flags.Big)),
            Field(() => EditorGUI.EnumMaskField(r, (Flags)(-1))), Field(() => EditorGUI.EnumMaskField(r, Plain.Second)),
            Field(() => EditorGUI.EnumMaskField(r, Flags.Green, EditorStyles.miniButton)),
            Field(() => EditorGUI.EnumMaskField(r, "Label", Flags.Green)), Field(() => EditorGUI.EnumMaskField(r, "Label", Flags.Green, EditorStyles.miniButton)),
            Field(() => EditorGUI.EnumMaskField(r, label, Flags.Blue)), Field(() => EditorGUI.EnumMaskField(r, label, Flags.Blue, EditorStyles.miniButton)));
        Put($"rect/{phase}/enumMaskPopup",
            Field(() => EditorGUI.EnumMaskPopup(r, "Label", Flags.Red)), Field(() => EditorGUI.EnumMaskPopup(r, "Label", Flags.Red | Flags.Big, EditorStyles.miniButton)),
            Field(() => EditorGUI.EnumMaskPopup(r, label, Flags.None)), Field(() => EditorGUI.EnumMaskPopup(r, label, (Flags)15, EditorStyles.miniButton)),
            Field(() => EditorGUI.EnumMaskPopup(r, "Label", Plain.Third)));
        EditorGUI.showMixedValue = true;
        Put($"rect/{phase}/mixed", Field(() => EditorGUI.EnumMaskField(r, Flags.Red)), Field(() => EditorGUI.EnumMaskPopup(r, "Label", Flags.Red)));
        EditorGUI.showMixedValue = false;
    }

    void RecordLayoutFields(string phase)
    {
        var rects = new List<object>();
        object[] L(Func<object> call)
        {
            var answer = Field(call);
            rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
            return answer;
        }
        GUILayout.BeginArea(new Rect(320, 0, 300, 600));
        Put($"layout/{phase}/fields",
            L(() => EditorGUILayout.EnumMaskField(Flags.Red)), L(() => EditorGUILayout.EnumMaskField(Flags.Red, EditorStyles.miniButton)),
            L(() => EditorGUILayout.EnumMaskField("Label", Flags.Green)), L(() => EditorGUILayout.EnumMaskField("Label", Flags.Green, EditorStyles.miniButton, GUILayout.Width(150))),
            L(() => EditorGUILayout.EnumMaskField(new GUIContent("Label"), Flags.Blue)), L(() => EditorGUILayout.EnumMaskField(new GUIContent("Label"), Flags.Blue, EditorStyles.miniButton)),
            L(() => EditorGUILayout.EnumMaskPopup("Label", Flags.Red)), L(() => EditorGUILayout.EnumMaskPopup("Label", Flags.Red, EditorStyles.miniButton)),
            L(() => EditorGUILayout.EnumMaskPopup(new GUIContent("Label"), Flags.Big, GUILayout.Width(120))), L(() => EditorGUILayout.EnumMaskPopup(new GUIContent("Label"), Flags.Big, EditorStyles.miniButton)));
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        string phase = type == EventType.Layout ? "layout" : "repaint";
        if (type == EventType.Layout && m_RecordedLayout) return;
        if (type == EventType.Repaint && (!m_RecordedLayout || m_RecordedRepaint)) return;
        RecordRectFields(phase);
        RecordLayoutFields(phase);
        if (type == EventType.Layout) { m_RecordedLayout = true; return; }
        m_RecordedRepaint = true;
        m_Done = true;
        Put("logs", m_Logs.Distinct().ToArray());
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
