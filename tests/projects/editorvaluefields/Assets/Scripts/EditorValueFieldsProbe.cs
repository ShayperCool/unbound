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
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's value fields
/// (number, text, password and delayed fields, sliders) answer: outside OnGUI; in Layout and
/// Repaint (returned value, control ids taken, GUI.changed, layout rects); and through a scripted
/// sequence of mouse and keyboard events, one per frame, given to fields at fixed rects (editing,
/// expressions, delayed commits, escape, slider clicks, dragging a label).
/// </summary>
public sealed class EditorValueFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1;
    int m_Frames;

    // Values the scripted fields edit.
    float m_Float = 1.5f;
    int m_Int = 7;
    float m_Delayed = 2f;
    string m_Text = "ab";
    float m_Slider = 0.5f;
    int m_IntSlider = 5;
    float m_Dragged = 10f;
    double m_Double = 0.25;
    long m_Long = 40;

    static readonly Rect FloatRect = new Rect(10, 300, 100, 18);
    static readonly Rect IntRect = new Rect(10, 320, 100, 18);
    static readonly Rect DelayedRect = new Rect(10, 340, 100, 18);
    static readonly Rect TextRect = new Rect(10, 360, 100, 18);
    static readonly Rect SliderRect = new Rect(10, 380, 200, 18);
    static readonly Rect IntSliderRect = new Rect(10, 400, 200, 18);
    static readonly Rect DragRect = new Rect(10, 420, 300, 18);
    static readonly Rect DoubleRect = new Rect(10, 440, 100, 18);
    static readonly Rect LongRect = new Rect(10, 460, 100, 18);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_VALUE_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorValueFieldsProbe");
        var probe = go.AddComponent<EditorValueFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string D(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { return "throws " + ex.GetType().Name; }
    }

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 100, 18);
        Put("outside/editorGUI",
            Try(() => EditorGUI.FloatField(r, 1f)), Try(() => EditorGUI.IntField(r, 1)), Try(() => EditorGUI.LongField(r, 1L)),
            Try(() => EditorGUI.DoubleField(r, 1.0)), Try(() => EditorGUI.TextField(r, "t")), Try(() => EditorGUI.PasswordField(r, "p")),
            Try(() => EditorGUI.DelayedTextField(r, "d")), Try(() => EditorGUI.DelayedFloatField(r, 1f)), Try(() => EditorGUI.DelayedIntField(r, 1)),
            Try(() => EditorGUI.DelayedDoubleField(r, 1.0)), Try(() => EditorGUI.Slider(r, 0.5f, 0f, 1f)), Try(() => EditorGUI.IntSlider(r, 5, 0, 10)),
            Try(() => EditorGUI.TextArea(r, "a")));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.FloatField(1f)), Try(() => EditorGUILayout.IntField(1)), Try(() => EditorGUILayout.LongField(1L)),
            Try(() => EditorGUILayout.DoubleField(1.0)), Try(() => EditorGUILayout.TextField("t")), Try(() => EditorGUILayout.PasswordField("p")),
            Try(() => EditorGUILayout.DelayedTextField("d")), Try(() => EditorGUILayout.DelayedFloatField(1f)), Try(() => EditorGUILayout.DelayedIntField(1)),
            Try(() => EditorGUILayout.DelayedDoubleField(1.0)), Try(() => EditorGUILayout.Slider(0.5f, 0f, 1f)), Try(() => EditorGUILayout.IntSlider(5, 0, 10)),
            Try(() => EditorGUILayout.TextArea("a")));
    }

    /// <summary>A field's answer in the current event: its value, the control ids it took,
    /// whether it set GUI.changed, and the keyboard focus after it.</summary>
    object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        return new object[] { value is float f ? F(f) : value is double d ? D(d) : value, after - before - 1, changed };
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 18);
        var label = new GUIContent("Label");
        Put($"rect/{phase}/number",
            Field(() => EditorGUI.FloatField(r, 1.25f)), Field(() => EditorGUI.FloatField(r, label, 1.25f)),
            Field(() => EditorGUI.IntField(r, 3)), Field(() => EditorGUI.IntField(r, "Label", 3)),
            Field(() => EditorGUI.LongField(r, 9L)), Field(() => EditorGUI.LongField(r, label, 9L)),
            Field(() => EditorGUI.DoubleField(r, 0.5)), Field(() => EditorGUI.DoubleField(r, label, 0.5)),
            Field(() => EditorGUI.FloatField(r, float.NaN)), Field(() => EditorGUI.FloatField(r, float.PositiveInfinity)));
        Put($"rect/{phase}/text",
            Field(() => EditorGUI.TextField(r, "abc")), Field(() => EditorGUI.TextField(r, label, "abc")),
            Field(() => EditorGUI.TextField(r, null)), Field(() => EditorGUI.PasswordField(r, "pw")),
            Field(() => EditorGUI.PasswordField(r, label, "pw")), Field(() => EditorGUI.TextArea(r, "a\nb")));
        Put($"rect/{phase}/delayed",
            Field(() => EditorGUI.DelayedTextField(r, "d")), Field(() => EditorGUI.DelayedTextField(r, label, "d")),
            Field(() => EditorGUI.DelayedFloatField(r, 4.5f)), Field(() => EditorGUI.DelayedFloatField(r, label, 4.5f)),
            Field(() => EditorGUI.DelayedIntField(r, 8)), Field(() => EditorGUI.DelayedIntField(r, label, 8)),
            Field(() => EditorGUI.DelayedDoubleField(r, 1.75)), Field(() => EditorGUI.DelayedDoubleField(r, label, 1.75)));
        Put($"rect/{phase}/slider",
            Field(() => EditorGUI.Slider(r, 0.5f, 0f, 1f)), Field(() => EditorGUI.Slider(r, label, 0.5f, 0f, 1f)),
            Field(() => EditorGUI.Slider(r, 3f, 0f, 1f)), Field(() => EditorGUI.Slider(r, 0.5f, 1f, 0f)),
            Field(() => EditorGUI.IntSlider(r, 5, 0, 10)), Field(() => EditorGUI.IntSlider(r, label, 5, 0, 10)),
            Field(() => EditorGUI.IntSlider(r, 20, 0, 10)));
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
            L(() => EditorGUILayout.FloatField(1.25f)), L(() => EditorGUILayout.FloatField("Label", 1.25f)),
            L(() => EditorGUILayout.IntField(3)), L(() => EditorGUILayout.LongField(9L)), L(() => EditorGUILayout.DoubleField(0.5)),
            L(() => EditorGUILayout.TextField("abc")), L(() => EditorGUILayout.TextField("Label", "abc")),
            L(() => EditorGUILayout.PasswordField("pw")), L(() => EditorGUILayout.TextArea("a\nb")),
            L(() => EditorGUILayout.DelayedTextField("d")), L(() => EditorGUILayout.DelayedFloatField(4.5f)),
            L(() => EditorGUILayout.DelayedIntField(8)), L(() => EditorGUILayout.DelayedDoubleField(1.75)),
            L(() => EditorGUILayout.Slider(0.5f, 0f, 1f)), L(() => EditorGUILayout.Slider("Label", 0.5f, 0f, 1f)),
            L(() => EditorGUILayout.IntSlider(5, 0, 10)), L(() => EditorGUILayout.IntSlider("Label", 5, 0, 10)));
        GUILayout.EndArea();
        if (phase == "repaint") Put("layout/repaint/rects", rects.ToArray());
    }

    // The scripted events: one per frame, given to the fields at fixed rects.
    static Event Mouse(EventType type, Vector2 position, Vector2 delta = default) =>
        new Event { type = type, mousePosition = position, delta = delta, button = 0, clickCount = 1 };

    static Event Key(string key) => Event.KeyboardEvent(key);

    static Event Char(char c) => new Event { type = EventType.KeyDown, character = c };

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        ("float.down", () => Mouse(EventType.MouseDown, FloatRect.center)),
        ("float.up", () => Mouse(EventType.MouseUp, FloatRect.center)),
        ("float.2", () => Char('2')),
        ("float.mul", () => Char('*')),
        ("float.3", () => Char('3')),
        ("float.return", () => Key("return")),
        ("int.down", () => Mouse(EventType.MouseDown, IntRect.center)),
        ("int.up", () => Mouse(EventType.MouseUp, IntRect.center)),
        ("int.9", () => Char('9')),
        ("int.x", () => Char('x')),
        ("int.escape", () => Key("escape")),
        ("delayed.down", () => Mouse(EventType.MouseDown, DelayedRect.center)),
        ("delayed.up", () => Mouse(EventType.MouseUp, DelayedRect.center)),
        ("delayed.4", () => Char('4')),
        ("delayed.dot", () => Char('.')),
        ("delayed.5", () => Char('5')),
        ("delayed.return", () => Key("return")),
        ("text.down", () => Mouse(EventType.MouseDown, TextRect.center)),
        ("text.up", () => Mouse(EventType.MouseUp, TextRect.center)),
        ("text.z", () => Char('z')),
        ("text.return", () => Key("return")),
        ("double.down", () => Mouse(EventType.MouseDown, DoubleRect.center)),
        ("double.up", () => Mouse(EventType.MouseUp, DoubleRect.center)),
        ("double.1", () => Char('1')),
        ("double.slash", () => Char('/')),
        ("double.8", () => Char('8')),
        ("double.tab", () => Key("tab")),
        ("long.down", () => Mouse(EventType.MouseDown, LongRect.center)),
        ("long.up", () => Mouse(EventType.MouseUp, LongRect.center)),
        ("long.minus", () => Char('-')),
        ("long.6", () => Char('6')),
        ("long.clickAway", () => Mouse(EventType.MouseDown, new Vector2(600, 500))),
        ("long.clickAwayUp", () => Mouse(EventType.MouseUp, new Vector2(600, 500))),
        ("slider.down", () => Mouse(EventType.MouseDown, new Vector2(SliderRect.x + 30, SliderRect.center.y))),
        ("slider.drag", () => Mouse(EventType.MouseDrag, new Vector2(SliderRect.x + 60, SliderRect.center.y), new Vector2(30, 0))),
        ("slider.up", () => Mouse(EventType.MouseUp, new Vector2(SliderRect.x + 60, SliderRect.center.y))),
        ("intSlider.down", () => Mouse(EventType.MouseDown, new Vector2(IntSliderRect.x + 100, IntSliderRect.center.y))),
        ("intSlider.up", () => Mouse(EventType.MouseUp, new Vector2(IntSliderRect.x + 100, IntSliderRect.center.y))),
        ("drag.down", () => Mouse(EventType.MouseDown, new Vector2(DragRect.x + 20, DragRect.center.y))),
        ("drag.move", () => Mouse(EventType.MouseDrag, new Vector2(DragRect.x + 30, DragRect.center.y), new Vector2(10, 0))),
        ("drag.move2", () => Mouse(EventType.MouseDrag, new Vector2(DragRect.x + 25, DragRect.center.y), new Vector2(-5, 0))),
        ("drag.up", () => Mouse(EventType.MouseUp, new Vector2(DragRect.x + 25, DragRect.center.y))),
    };

    /// <summary>The scripted fields, in the same order in every event so they keep their ids.</summary>
    object[] ScriptedFields()
    {
        var a = Field(() => m_Float = EditorGUI.FloatField(FloatRect, m_Float));
        var b = Field(() => m_Int = EditorGUI.IntField(IntRect, m_Int));
        var c = Field(() => m_Delayed = EditorGUI.DelayedFloatField(DelayedRect, m_Delayed));
        var d = Field(() => m_Text = EditorGUI.TextField(TextRect, m_Text));
        var e = Field(() => m_Slider = EditorGUI.Slider(SliderRect, m_Slider, 0f, 1f));
        var f = Field(() => m_IntSlider = EditorGUI.IntSlider(IntSliderRect, m_IntSlider, 0, 10));
        var g = Field(() => m_Dragged = EditorGUI.FloatField(DragRect, "Drag", m_Dragged));
        var h = Field(() => m_Double = EditorGUI.DoubleField(DoubleRect, m_Double));
        var i = Field(() => m_Long = EditorGUI.LongField(LongRect, m_Long));
        return new object[]
        {
            F(m_Float), m_Int, F(m_Delayed), m_Text, F(m_Slider), m_IntSlider, F(m_Dragged), D(m_Double), m_Long,
            new object[] { a[2], b[2], c[2], d[2], e[2], f[2], g[2], h[2], i[2] },
            GUIUtility.keyboardControl != 0, GUIUtility.hotControl != 0, EditorGUIUtility.editingTextField,
        };
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;

        if (m_Step < 0)
        {
            // Plain events first: every field in Layout, then in Repaint.
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
        double d => D(d),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
