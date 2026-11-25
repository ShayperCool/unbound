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
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's vector, rect and
/// bounds fields and MultiFloatField/MultiIntField answer: outside OnGUI; in Layout and Repaint
/// (value, control ids, GUI.changed, layout rects); and through scripted clicks, typing and label
/// drags at positions inside their sub-fields, which show where each sub-field lies.
/// </summary>
public sealed class EditorMultiFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done, m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1, m_Frames;

    Vector3 m_V3 = new Vector3(1, 2, 3);
    Vector2 m_V2 = new Vector2(4, 5);
    Rect m_Rect = new Rect(1, 2, 3, 4);
    Bounds m_Bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6));
    Vector3Int m_V3i = new Vector3Int(7, 8, 9);
    float[] m_Multi = { 10, 20 };

    static readonly Rect V3Rect = new Rect(10, 300, 300, 18);
    static readonly Rect V2Rect = new Rect(10, 330, 300, 40);
    static readonly Rect RectRect = new Rect(10, 380, 300, 40);
    static readonly Rect BoundsRect = new Rect(10, 430, 300, 40);
    static readonly Rect V3iRect = new Rect(10, 480, 300, 18);
    static readonly Rect MultiRect = new Rect(10, 500, 300, 18);
    static readonly GUIContent[] AB = { new GUIContent("A"), new GUIContent("Bee") };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_MULTI_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorMultiFieldsProbe");
        var probe = go.AddComponent<EditorMultiFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string S(object v) => v switch
    {
        Vector2 a => F(a.x) + " " + F(a.y),
        Vector3 a => F(a.x) + " " + F(a.y) + " " + F(a.z),
        Vector4 a => F(a.x) + " " + F(a.y) + " " + F(a.z) + " " + F(a.w),
        Vector2Int a => a.x + " " + a.y,
        Vector3Int a => a.x + " " + a.y + " " + a.z,
        Rect a => F(a.x) + " " + F(a.y) + " " + F(a.width) + " " + F(a.height),
        RectInt a => a.x + " " + a.y + " " + a.width + " " + a.height,
        Bounds a => S(a.center) + " / " + S(a.extents),
        BoundsInt a => S(a.position) + " / " + S(a.size),
        float[] a => string.Join(" ", a.Select(F)),
        int[] a => string.Join(" ", a),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    };
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object Try(Func<object> read)
    {
        try { return S(read()); }
        catch (Exception ex) { return "throws " + ex.GetType().Name; }
    }

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 300, 40);
        Put("outside/editorGUI",
            Try(() => EditorGUI.Vector2Field(r, "L", Vector2.one)), Try(() => EditorGUI.Vector3Field(r, "L", Vector3.one)),
            Try(() => EditorGUI.Vector4Field(r, "L", Vector4.one)), Try(() => EditorGUI.Vector2IntField(r, "L", Vector2Int.one)),
            Try(() => EditorGUI.Vector3IntField(r, "L", Vector3Int.one)), Try(() => EditorGUI.RectField(r, new Rect(1, 2, 3, 4))),
            Try(() => EditorGUI.RectIntField(r, new RectInt(1, 2, 3, 4))), Try(() => EditorGUI.BoundsField(r, new Bounds())),
            Try(() => EditorGUI.BoundsIntField(r, new BoundsInt())),
            Try(() => { var v = new float[] { 1, 2 }; EditorGUI.MultiFloatField(r, AB, v); return v; }),
            Try(() => { var v = new int[] { 1, 2 }; EditorGUI.MultiIntField(r, AB, v); return v; }));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.Vector2Field("L", Vector2.one)), Try(() => EditorGUILayout.Vector3Field("L", Vector3.one)),
            Try(() => EditorGUILayout.RectField(new Rect(1, 2, 3, 4))), Try(() => EditorGUILayout.BoundsField(new Bounds())),
            Try(() => EditorGUILayout.Vector3IntField("L", Vector3Int.one)), Try(() => EditorGUILayout.BoundsIntField(new BoundsInt())));
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
        return new object[] { value, after - before - 1, changed };
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 40);
        Put($"rect/{phase}/vectors",
            Field(() => EditorGUI.Vector2Field(r, "Label", new Vector2(1, 2))), Field(() => EditorGUI.Vector2Field(r, "", new Vector2(1, 2))),
            Field(() => EditorGUI.Vector3Field(r, "Label", new Vector3(1, 2, 3))), Field(() => EditorGUI.Vector4Field(r, "Label", new Vector4(1, 2, 3, 4))),
            Field(() => EditorGUI.Vector2IntField(r, "Label", new Vector2Int(1, 2))), Field(() => EditorGUI.Vector3IntField(r, "Label", new Vector3Int(1, 2, 3))));
        Put($"rect/{phase}/rectsAndBounds",
            Field(() => EditorGUI.RectField(r, new Rect(1, 2, 3, 4))), Field(() => EditorGUI.RectField(r, "Label", new Rect(1, 2, 3, 4))),
            Field(() => EditorGUI.RectIntField(r, new RectInt(1, 2, 3, 4))), Field(() => EditorGUI.RectIntField(r, "Label", new RectInt(1, 2, 3, 4))),
            Field(() => EditorGUI.BoundsField(r, new Bounds(Vector3.one, Vector3.one))), Field(() => EditorGUI.BoundsField(r, "Label", new Bounds(Vector3.one, Vector3.one))),
            Field(() => EditorGUI.BoundsIntField(r, new BoundsInt(1, 2, 3, 4, 5, 6))), Field(() => EditorGUI.BoundsIntField(r, "Label", new BoundsInt(1, 2, 3, 4, 5, 6))));
        Put($"rect/{phase}/multi",
            Field(() => { var v = new float[] { 1, 2 }; EditorGUI.MultiFloatField(r, AB, v); return v; }),
            Field(() => { var v = new float[] { 1, 2 }; EditorGUI.MultiFloatField(r, new GUIContent("Label"), AB, v); return v; }),
            Field(() => { var v = new int[] { 1, 2 }; EditorGUI.MultiIntField(r, AB, v); return v; }));
        bool wide = EditorGUIUtility.wideMode;
        EditorGUIUtility.wideMode = true;
        Put($"rect/{phase}/wide",
            Field(() => EditorGUI.Vector2Field(r, "Label", new Vector2(1, 2))), Field(() => EditorGUI.Vector3Field(r, "Label", new Vector3(1, 2, 3))),
            Field(() => EditorGUI.BoundsField(r, "Label", new Bounds(Vector3.one, Vector3.one))));
        EditorGUIUtility.wideMode = wide;
        Put($"rect/{phase}/wideMode", wide);
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
            L(() => EditorGUILayout.Vector2Field("Label", new Vector2(1, 2))), L(() => EditorGUILayout.Vector2Field("", new Vector2(1, 2))),
            L(() => EditorGUILayout.Vector3Field("Label", new Vector3(1, 2, 3))), L(() => EditorGUILayout.Vector4Field("Label", new Vector4(1, 2, 3, 4))),
            L(() => EditorGUILayout.Vector2IntField("Label", new Vector2Int(1, 2))), L(() => EditorGUILayout.Vector3IntField("Label", new Vector3Int(1, 2, 3))),
            L(() => EditorGUILayout.RectField(new Rect(1, 2, 3, 4))), L(() => EditorGUILayout.RectField("Label", new Rect(1, 2, 3, 4))),
            L(() => EditorGUILayout.RectIntField(new RectInt(1, 2, 3, 4))), L(() => EditorGUILayout.BoundsField(new Bounds(Vector3.one, Vector3.one))),
            L(() => EditorGUILayout.BoundsField("Label", new Bounds(Vector3.one, Vector3.one))), L(() => EditorGUILayout.BoundsIntField(new BoundsInt(1, 2, 3, 4, 5, 6))));
        GUILayout.EndArea();
        if (phase == "repaint") Put("layout/repaint/rects", rects.ToArray());

        // Wide mode: the labelled forms' reserved heights.
        rects.Clear();
        bool wide = EditorGUIUtility.wideMode;
        EditorGUIUtility.wideMode = true;
        GUILayout.BeginArea(new Rect(320, 0, 300, 600));
        Put($"layout/{phase}/wideFields",
            L(() => EditorGUILayout.Vector2Field("Label", new Vector2(1, 2))), L(() => EditorGUILayout.Vector3Field("Label", new Vector3(1, 2, 3))),
            L(() => EditorGUILayout.RectField("Label", new Rect(1, 2, 3, 4))), L(() => EditorGUILayout.BoundsField("Label", new Bounds(Vector3.one, Vector3.one))),
            L(() => EditorGUILayout.BoundsIntField("Label", new BoundsInt(1, 2, 3, 4, 5, 6))), L(() => EditorGUILayout.BoundsField(new Bounds(Vector3.one, Vector3.one))));
        GUILayout.EndArea();
        EditorGUIUtility.wideMode = wide;
        if (phase == "repaint") Put("layout/repaint/wideRects", rects.ToArray());
    }

    static Event Mouse(EventType type, Vector2 position, Vector2 delta = default) =>
        new Event { type = type, mousePosition = position, delta = delta, button = 0, clickCount = 1 };
    static Event Key(string key) => Event.KeyboardEvent(key);
    static Event Char(char c) => new Event { type = EventType.KeyDown, character = c };

    // Clicks at a sub-field's position, types a digit and commits it.
    static IEnumerable<(string, Func<Event>)> Edit(string name, Vector2 at, char digit) => new (string, Func<Event>)[]
    {
        (name + ".down", () => Mouse(EventType.MouseDown, at)),
        (name + ".up", () => Mouse(EventType.MouseUp, at)),
        (name + ".type", () => Char(digit)),
        (name + ".return", () => Key("return")),
    };

    static IEnumerable<(string, Func<Event>)> Drag(string name, Vector2 at) => new (string, Func<Event>)[]
    {
        (name + ".down", () => Mouse(EventType.MouseDown, at)),
        (name + ".move", () => Mouse(EventType.MouseDrag, at + new Vector2(10, 0), new Vector2(10, 0))),
        (name + ".move2", () => Mouse(EventType.MouseDrag, at + new Vector2(25, 0), new Vector2(15, 0))),
        (name + ".up", () => Mouse(EventType.MouseUp, at + new Vector2(25, 0))),
    };

    static readonly (string Name, Func<Event> Make)[] Script = new[]
    {
        // Vector3 without a label: three columns of the whole width; probe the middle of each column
        // and positions near the column edges.
        Edit("v3.y", new Vector2(10 + 150, 309), '5'),
        Edit("v3.z", new Vector2(10 + 290, 309), '6'),
        Edit("v3.xEdge", new Vector2(10 + 22, 309), '7'),
        Edit("v3.yStart", new Vector2(10 + 104, 309), '8'),
        Drag("v3.yLabel", new Vector2(10 + 104, 309)),
        // Vector2 with a label (narrow mode: the fields on the second line).
        Edit("v2.y", new Vector2(10 + 280, 330 + 29), '3'),
        Edit("v2.firstLine", new Vector2(10 + 280, 330 + 9), '9'),
        // Rect: X Y on the first line, W H on the second.
        Edit("rect.h", new Vector2(10 + 280, 380 + 29), '2'),
        Edit("rect.x", new Vector2(10 + 120, 380 + 9), '4'),
        // Bounds: Center and Extent labels, three columns after them.
        Edit("bounds.extentZ", new Vector2(10 + 290, 430 + 29), '1'),
        Edit("bounds.centerX", new Vector2(10 + 75, 430 + 9), '0'),
        Edit("bounds.label", new Vector2(10 + 30, 430 + 9), '5'),
        // Vector3Int and MultiFloatField with sub-labels of different widths.
        Edit("v3i.x", new Vector2(10 + 80, 489), '4'),
        Edit("multi.bee", new Vector2(10 + 180, 509), '6'),
        Drag("multi.beeLabel", new Vector2(10 + 158, 509)),
    }.SelectMany(x => x).ToArray();

    object[] ScriptedFields()
    {
        var a = Field(() => m_V3 = EditorGUI.Vector3Field(V3Rect, "", m_V3));
        var b = Field(() => m_V2 = EditorGUI.Vector2Field(V2Rect, "Pos", m_V2));
        var c = Field(() => m_Rect = EditorGUI.RectField(RectRect, m_Rect));
        var d = Field(() => m_Bounds = EditorGUI.BoundsField(BoundsRect, m_Bounds));
        var e = Field(() => m_V3i = EditorGUI.Vector3IntField(V3iRect, "", m_V3i));
        var f = Field(() => { EditorGUI.MultiFloatField(MultiRect, AB, m_Multi); return m_Multi; });
        return new object[]
        {
            S(m_V3), S(m_V2), S(m_Rect), S(m_Bounds), S(m_V3i), S(m_Multi),
            new object[] { a[2], b[2], c[2], d[2], e[2], f[2] },
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
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
