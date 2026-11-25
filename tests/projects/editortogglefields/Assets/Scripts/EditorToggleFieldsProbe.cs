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
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's toggles,
/// property sliders (Slider/IntSlider of a SerializedProperty) and drop-shadow labels answer: outside
/// OnGUI; in Layout and Repaint (returned value, control ids taken, GUI.changed, layout rects, the
/// properties' values); and through a scripted sequence of mouse and keyboard events, one per
/// frame, given to controls at fixed rects (toggles clicked with the left and right button, on the
/// label and beside the box, Space on a focused toggle, sliders pressed and dragged, a property
/// field's toggle). The log messages the run gives are recorded too.
/// </summary>
public sealed class EditorToggleFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1;
    int m_Frames;
    readonly List<string> m_Logs = new List<string>();

    bool m_A, m_B = true, m_C;
    ToggleFieldsHolder m_Holder;
    SerializedObject m_Serialized;

    static readonly Rect T1 = new Rect(10, 300, 200, 18);
    static readonly Rect T2 = new Rect(10, 320, 300, 18);
    static readonly Rect T3 = new Rect(10, 340, 100, 18);
    static readonly Rect S1 = new Rect(10, 360, 300, 18);
    static readonly Rect S2 = new Rect(10, 380, 300, 18);
    static readonly Rect PF = new Rect(10, 400, 300, 18);
    static float s_LabelWidth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_TOGGLE_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorToggleFieldsProbe");
        var probe = go.AddComponent<EditorToggleFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        Application.logMessageReceived += (message, stack, type) => probe.m_Logs.Add(type + ": " + message);
        probe.m_Holder = ScriptableObject.CreateInstance<ToggleFieldsHolder>();
        probe.m_Serialized = new SerializedObject(probe.m_Holder);
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    SerializedProperty Prop(string name) => m_Serialized.FindProperty(name);

    object[] Values()
    {
        m_Serialized.Update();
        return new object[] { F(m_Holder.floatValue), m_Holder.intValue, m_Holder.boolValue };
    }

    void Reset()
    {
        m_Holder.floatValue = 2f;
        m_Holder.intValue = 3;
        m_Holder.boolValue = true;
        m_Serialized.Update();
    }

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 100, 18);
        Put("outside/editorGUI",
            Try(() => EditorGUI.Toggle(r, true)), Try(() => EditorGUI.Toggle(r, "L", true)), Try(() => EditorGUI.Toggle(r, false, EditorStyles.miniButton)),
            Try(() => EditorGUI.Toggle(r, new GUIContent("L"), false, EditorStyles.miniButton)),
            Try(() => { EditorGUI.Slider(r, Prop("floatValue"), 0f, 10f); return "ok"; }),
            Try(() => { EditorGUI.IntSlider(r, Prop("intValue"), 0, 10); return "ok"; }),
            Try(() => { EditorGUI.DropShadowLabel(r, "s"); return "ok"; }),
            Try(() => { EditorGUI.DropShadowLabel(r, "s", EditorStyles.label); return "ok"; }));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.Toggle(true)), Try(() => EditorGUILayout.Toggle("L", true)), Try(() => EditorGUILayout.Toggle(false, EditorStyles.miniButton)),
            Try(() => { EditorGUILayout.Slider(Prop("floatValue"), 0f, 10f); return "ok"; }),
            Try(() => { EditorGUILayout.IntSlider(Prop("intValue"), 0, 10); return "ok"; }));
        Put("outside/values", Values());
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
        Put($"rect/{phase}/toggle",
            Field(() => EditorGUI.Toggle(r, false)), Field(() => EditorGUI.Toggle(r, "Label", true)), Field(() => EditorGUI.Toggle(r, label, false)),
            Field(() => EditorGUI.Toggle(r, true, EditorStyles.miniButton)), Field(() => EditorGUI.Toggle(r, "Label", false, EditorStyles.miniButton)),
            Field(() => EditorGUI.Toggle(r, label, true, EditorStyles.miniButton)), Field(() => EditorGUI.Toggle(r, GUIContent.none, true)));
        EditorGUI.showMixedValue = true;
        Put($"rect/{phase}/toggleMixed",
            Field(() => EditorGUI.Toggle(r, true)), Field(() => EditorGUI.Toggle(r, "Label", true)), Field(() => EditorGUI.Toggle(r, true, EditorStyles.miniButton)));
        EditorGUI.showMixedValue = false;
        Put($"rect/{phase}/sliders",
            Field(() => { EditorGUI.Slider(r, Prop("floatValue"), 0f, 10f); return "ok"; }),
            Field(() => { EditorGUI.Slider(r, Prop("floatValue"), 0f, 10f, "Label"); return "ok"; }),
            Field(() => { EditorGUI.Slider(r, Prop("floatValue"), 5f, 10f, label); return "ok"; }),
            Field(() => { EditorGUI.IntSlider(r, Prop("intValue"), 0, 10); return "ok"; }),
            Field(() => { EditorGUI.IntSlider(r, Prop("intValue"), 0, 10, "Label"); return "ok"; }),
            Field(() => { EditorGUI.IntSlider(r, Prop("intValue"), 5, 10, label); return "ok"; }),
            Values());
        Reset();
        Put($"rect/{phase}/dropShadow",
            Field(() => { EditorGUI.DropShadowLabel(r, "text"); return "ok"; }),
            Field(() => { EditorGUI.DropShadowLabel(r, label); return "ok"; }),
            Field(() => { EditorGUI.DropShadowLabel(r, "text", EditorStyles.label); return "ok"; }),
            Field(() => { EditorGUI.DropShadowLabel(r, label, EditorStyles.boldLabel); return "ok"; }));
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
            L(() => EditorGUILayout.Toggle(false)), L(() => EditorGUILayout.Toggle("Label", true)),
            L(() => EditorGUILayout.Toggle(new GUIContent("Label"), false)),
            L(() => EditorGUILayout.Toggle(true, EditorStyles.miniButton)), L(() => EditorGUILayout.Toggle("Label", false, EditorStyles.miniButton)),
            L(() => EditorGUILayout.Toggle(new GUIContent("Label"), true, EditorStyles.miniButton, GUILayout.Width(120))),
            L(() => { EditorGUILayout.Slider(Prop("floatValue"), 0f, 10f); return "ok"; }),
            L(() => { EditorGUILayout.Slider(Prop("floatValue"), 0f, 10f, "Label"); return "ok"; }),
            L(() => { EditorGUILayout.IntSlider(Prop("intValue"), 0, 10); return "ok"; }),
            L(() => { EditorGUILayout.IntSlider(Prop("intValue"), 0, 10, new GUIContent("Label")); return "ok"; }));
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
        Reset();
    }

    static Event Mouse(EventType type, float x, float y, int button = 0) =>
        new Event { type = type, mousePosition = new Vector2(x, y), button = button, clickCount = 1 };
    static Event Key(string key) => Event.KeyboardEvent(key);
    static float Field2X(float offset) => 10 + s_LabelWidth + offset;

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        // An unlabelled toggle: the left button toggles and focuses it, the right does nothing,
        // Space toggles the focused one.
        ("t1.down", () => Mouse(EventType.MouseDown, T1.x + 5, T1.center.y)),
        ("t1.up", () => Mouse(EventType.MouseUp, T1.x + 5, T1.center.y)),
        ("t1.right.down", () => Mouse(EventType.MouseDown, T1.x + 5, T1.center.y, 1)),
        ("t1.right.up", () => Mouse(EventType.MouseUp, T1.x + 5, T1.center.y, 1)),
        ("t1.space", () => Key("space")),
        // A labelled toggle: its label, its box, beside its box.
        ("t2.label.down", () => Mouse(EventType.MouseDown, T2.x + 30, T2.center.y)),
        ("t2.label.up", () => Mouse(EventType.MouseUp, T2.x + 30, T2.center.y)),
        ("t2.box.down", () => Mouse(EventType.MouseDown, Field2X(5), T2.center.y)),
        ("t2.box.up", () => Mouse(EventType.MouseUp, Field2X(5), T2.center.y)),
        ("t2.beside.down", () => Mouse(EventType.MouseDown, Field2X(60), T2.center.y)),
        ("t2.beside.up", () => Mouse(EventType.MouseUp, Field2X(60), T2.center.y)),
        // A toggle with the mini button style.
        ("t3.down", () => Mouse(EventType.MouseDown, T3.x + 50, T3.center.y)),
        ("t3.up", () => Mouse(EventType.MouseUp, T3.x + 50, T3.center.y)),
        // The property sliders: pressed on the slider, dragged, released.
        ("s1.down", () => Mouse(EventType.MouseDown, Field2X(20), S1.center.y)),
        ("s1.drag", () => new Event { type = EventType.MouseDrag, mousePosition = new Vector2(Field2X(40), S1.center.y), delta = new Vector2(20, 0), button = 0 }),
        ("s1.up", () => Mouse(EventType.MouseUp, Field2X(40), S1.center.y)),
        ("s2.down", () => Mouse(EventType.MouseDown, Field2X(30), S2.center.y)),
        ("s2.up", () => Mouse(EventType.MouseUp, Field2X(30), S2.center.y)),
        // A property field's toggle.
        ("pf.down", () => Mouse(EventType.MouseDown, Field2X(5), PF.center.y)),
        ("pf.up", () => Mouse(EventType.MouseUp, Field2X(5), PF.center.y)),
    };

    /// <summary>The scripted controls, in the same order in every event so they keep their ids.</summary>
    object[] ScriptedFields()
    {
        s_LabelWidth = EditorGUIUtility.labelWidth;
        var a = Field(() => m_A = EditorGUI.Toggle(T1, m_A));
        var b = Field(() => m_B = EditorGUI.Toggle(T2, "Toggle", m_B));
        var c = Field(() => m_C = EditorGUI.Toggle(T3, m_C, EditorStyles.miniButton));
        var d = Field(() => { EditorGUI.Slider(S1, Prop("floatValue"), 0f, 10f); return "ok"; });
        var e = Field(() => { EditorGUI.IntSlider(S2, Prop("intValue"), 0, 10); return "ok"; });
        var f = Field(() => { EditorGUI.PropertyField(PF, Prop("boolValue")); return "ok"; });
        m_Serialized.ApplyModifiedProperties();
        return new object[]
        {
            m_A, m_B, m_C, Values(),
            new object[] { a[1], a[2], b[1], b[2], c[1], c[2], d[1], d[2], e[1], e[2], f[1], f[2] },
            GUIUtility.keyboardControl != 0, GUIUtility.hotControl != 0,
        };
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;

        if (m_Step < 0)
        {
            string phase = type == EventType.Layout ? "layout" : "repaint";
            if (type == EventType.Layout && m_RecordedLayout) return;
            if (type == EventType.Repaint && (!m_RecordedLayout || m_RecordedRepaint)) return;
            ScriptedFields();
            RecordRectFields(phase);
            RecordLayoutFields(phase);
            if (type == EventType.Layout) m_RecordedLayout = true;
            else { m_RecordedRepaint = true; m_Step = 0; Put("script/start", ScriptedFields()); }
            return;
        }

        // One scripted event per frame, given in place of the frame's Repaint.
        if (type != EventType.Repaint) { ScriptedFields(); return; }
        if (++m_Frames % 2 != 0) { ScriptedFields(); return; }
        var real = Event.current;
        var step = Script[m_Step];
        var scripted = step.Make();
        Event.current = scripted;
        object[] answer;
        try { answer = ScriptedFields(); }
        finally { Event.current = real; }
        Put("script/" + step.Name, answer, (int)scripted.type);
        if (++m_Step == Script.Length)
        {
            m_Done = true;
            Put("logs", m_Logs.Distinct().ToArray());
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
        }
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        float f => F(f),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
