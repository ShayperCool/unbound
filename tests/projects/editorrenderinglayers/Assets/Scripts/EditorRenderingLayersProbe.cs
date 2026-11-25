using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's
/// RenderingLayerMaskField answer with three named rendering layers (Default, Lights, Shadows):
/// outside OnGUI; in Layout and Repaint (returned mask, control ids taken, GUI.changed, a
/// property's value, layout rects); and the log messages of the run.
/// </summary>
public sealed class EditorRenderingLayersProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    readonly List<string> m_Logs = new List<string>();
    RenderingLayersHolder m_Holder;
    SerializedObject m_Serialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_RENDERING_LAYERS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorRenderingLayersProbe");
        var probe = go.AddComponent<EditorRenderingLayersProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        Application.logMessageReceived += (message, stack, type) => probe.m_Logs.Add(type + ": " + message);
        probe.m_Holder = ScriptableObject.CreateInstance<RenderingLayersHolder>();
        probe.m_Serialized = new SerializedObject(probe.m_Holder);
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
            return value is RenderingLayerMask m ? (object)m.value : value;
        }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    SerializedProperty Mask => m_Serialized.FindProperty("mask");

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 200, 18);
        RenderingLayerMask m = 3;
        Put("outside/editorGUI",
            Try(() => EditorGUI.RenderingLayerMaskField(r, "L", m)), Try(() => EditorGUI.RenderingLayerMaskField(r, new GUIContent("L"), m, EditorStyles.miniButton)),
            Try(() => { EditorGUI.RenderingLayerMaskField(r, "L", Mask); return "ok"; }));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.RenderingLayerMaskField("L", 3u)), Try(() => EditorGUILayout.RenderingLayerMaskField("L", m)),
            Try(() => { EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), Mask); return "ok"; }));
        Put("outside/names", RenderingLayerMask.GetDefinedRenderingLayerNames(), RenderingLayerMask.GetDefinedRenderingLayerValues(),
            RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue());
    }

    /// <summary>A control's answer in the current event: its value, the control ids it took ("used"
    /// once the event is used) and whether it set GUI.changed.</summary>
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
        Put($"rect/{phase}/masks",
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)1)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)3)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)7)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)0)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)uint.MaxValue)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)(1u << 20))),
            Field(() => EditorGUI.RenderingLayerMaskField(r, label, (RenderingLayerMask)2)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)5, EditorStyles.miniButton)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, label, (RenderingLayerMask)6, EditorStyles.miniButton)),
            Field(() => EditorGUI.RenderingLayerMaskField(r, (GUIContent)null, (RenderingLayerMask)1, EditorStyles.popup)));
        EditorGUI.showMixedValue = true;
        Put($"rect/{phase}/mixed", Field(() => EditorGUI.RenderingLayerMaskField(r, "Label", (RenderingLayerMask)3)));
        EditorGUI.showMixedValue = false;
        Put($"rect/{phase}/property",
            Field(() => { EditorGUI.RenderingLayerMaskField(r, "Label", Mask); return "ok"; }),
            Field(() => { EditorGUI.RenderingLayerMaskField(r, label, Mask); return "ok"; }),
            m_Holder.mask.value);
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
        RenderingLayerMask m = 3;
        GUILayout.BeginArea(new Rect(320, 0, 300, 600));
        Put($"layout/{phase}/fields",
            L(() => EditorGUILayout.RenderingLayerMaskField("Label", 3u)),
            L(() => EditorGUILayout.RenderingLayerMaskField("Label", 5u, EditorStyles.miniButton)),
            L(() => EditorGUILayout.RenderingLayerMaskField(new GUIContent("Label"), 6u)),
            L(() => EditorGUILayout.RenderingLayerMaskField(new GUIContent("Label"), 1u, EditorStyles.miniButton, GUILayout.Width(120))),
            L(() => EditorGUILayout.RenderingLayerMaskField("Label", m)),
            L(() => EditorGUILayout.RenderingLayerMaskField("Label", m, EditorStyles.miniButton)),
            L(() => EditorGUILayout.RenderingLayerMaskField(new GUIContent("Label"), m, GUILayout.Width(150))),
            L(() => EditorGUILayout.RenderingLayerMaskField(new GUIContent("Label"), m, EditorStyles.miniButton, GUILayout.Width(150))),
            L(() => { EditorGUILayout.RenderingLayerMaskField(new GUIContent("Label"), Mask); return "ok"; }),
            L(() => EditorGUILayout.RenderingLayerMaskField("Label", (RenderingLayerMask)(1u << 20))));
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
