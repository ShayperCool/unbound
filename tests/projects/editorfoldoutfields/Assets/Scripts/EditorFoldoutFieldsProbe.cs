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
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's foldouts, int
/// popups, min-max sliders and selectable labels answer: outside OnGUI; in Layout and Repaint
/// (returned value, control ids taken, GUI.changed, layout rects); and through a scripted sequence
/// of mouse and keyboard events, one per frame, given to controls at fixed rects (a foldout's arrow
/// and label clicked, with and without toggleOnLabelClick, arrow keys on a focused foldout, a
/// min-max slider's thumb, ends and track).
/// </summary>
public sealed class EditorFoldoutFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1;
    int m_Frames;

    bool m_Fold;
    bool m_FoldLabel;
    float m_Min = 2f, m_Max = 6f;
    int m_Popup = 20;

    static readonly Rect FoldRect = new Rect(10, 300, 300, 18);
    static readonly Rect FoldLabelRect = new Rect(10, 320, 300, 18);
    static readonly Rect MinMaxRect = new Rect(10, 340, 200, 18);
    static readonly Rect PopupRect = new Rect(10, 360, 200, 18);

    static readonly string[] Names = { "Ten", "Twenty", "Thirty" };
    static readonly int[] Values = { 10, 20, 30 };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_FOLDOUT_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorFoldoutFieldsProbe");
        var probe = go.AddComponent<EditorFoldoutFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
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

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 100, 18);
        float min = 1f, max = 2f;
        Put("outside/editorGUI",
            Try(() => EditorGUI.Foldout(r, false, "F")), Try(() => EditorGUI.Foldout(r, true, new GUIContent("F"), true)),
            Try(() => EditorGUI.IntPopup(r, 20, Names, Values)), Try(() => EditorGUI.IntPopup(r, "L", 20, Names, Values)),
            Try(() => { EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f); return "ok"; }),
            Try(() => { EditorGUI.SelectableLabel(r, "s"); return "ok"; }));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.Foldout(false, "F")), Try(() => EditorGUILayout.Foldout(true, "F", true)),
            Try(() => EditorGUILayout.IntPopup(20, Names, Values)), Try(() => EditorGUILayout.IntPopup("L", 20, Names, Values)),
            Try(() => { EditorGUILayout.MinMaxSlider(ref min, ref max, 0f, 10f); return "ok"; }),
            Try(() => { EditorGUILayout.SelectableLabel("s"); return "ok"; }));
    }

    /// <summary>A control's answer in the current event: its value, the control ids it took and
    /// whether it set GUI.changed.</summary>
    object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        return new object[] { value is float f ? F(f) : value, after - before - 1, changed };
    }

    object MinMax(Func<(float, float)> call)
    {
        var answer = Field(() => { var (a, b) = call(); return new object[] { F(a), F(b) }; });
        return answer;
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 18);
        var label = new GUIContent("Label");
        var options = Names.Select(n => new GUIContent(n)).ToArray();
        Put($"rect/{phase}/foldout",
            Field(() => EditorGUI.Foldout(r, false, "Fold")), Field(() => EditorGUI.Foldout(r, true, "Fold")),
            Field(() => EditorGUI.Foldout(r, false, "Fold", true)), Field(() => EditorGUI.Foldout(r, false, label)),
            Field(() => EditorGUI.Foldout(r, true, label, true)), Field(() => EditorGUI.Foldout(r, false, label, EditorStyles.foldoutHeader)),
            Field(() => EditorGUI.Foldout(r, true, "Fold", false, EditorStyles.foldout)), Field(() => EditorGUI.Foldout(r, false, GUIContent.none)));
        Put($"rect/{phase}/intPopup",
            Field(() => EditorGUI.IntPopup(r, 20, Names, Values)), Field(() => EditorGUI.IntPopup(r, 25, Names, Values)),
            Field(() => EditorGUI.IntPopup(r, 1, Names, null)), Field(() => EditorGUI.IntPopup(r, "Label", 30, Names, Values)),
            Field(() => EditorGUI.IntPopup(r, label, 10, options, Values)), Field(() => EditorGUI.IntPopup(r, 20, options, Values)),
            Field(() => EditorGUI.IntPopup(r, 20, Names, Values, EditorStyles.miniButton)));
        float a = 2f, b = 6f;
        Put($"rect/{phase}/minMax",
            MinMax(() => { EditorGUI.MinMaxSlider(r, ref a, ref b, 0f, 10f); return (a, b); }),
            MinMax(() => { float x = 8f, y = 3f; EditorGUI.MinMaxSlider(r, ref x, ref y, 0f, 10f); return (x, y); }),
            MinMax(() => { float x = -5f, y = 20f; EditorGUI.MinMaxSlider(r, ref x, ref y, 0f, 10f); return (x, y); }),
            MinMax(() => { float x = 2f, y = 6f; EditorGUI.MinMaxSlider(r, "Label", ref x, ref y, 0f, 10f); return (x, y); }),
            MinMax(() => { float x = 2f, y = 6f; EditorGUI.MinMaxSlider(r, label, ref x, ref y, 0f, 10f); return (x, y); }));
        Put($"rect/{phase}/selectable",
            Field(() => { EditorGUI.SelectableLabel(r, "text"); return "ok"; }),
            Field(() => { EditorGUI.SelectableLabel(r, "text", EditorStyles.miniLabel); return "ok"; }));
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
        float a = 2f, b = 6f;
        Put($"layout/{phase}/fields",
            L(() => EditorGUILayout.Foldout(false, "Fold")), L(() => EditorGUILayout.Foldout(true, "Fold", true)),
            L(() => EditorGUILayout.Foldout(false, new GUIContent("Fold"), EditorStyles.foldout)),
            L(() => EditorGUILayout.IntPopup(20, Names, Values)), L(() => EditorGUILayout.IntPopup("Label", 30, Names, Values)),
            L(() => EditorGUILayout.IntPopup(new GUIContent("Label"), 10, Names.Select(n => new GUIContent(n)).ToArray(), Values)),
            L(() => { EditorGUILayout.MinMaxSlider(ref a, ref b, 0f, 10f); return new object[] { F(a), F(b) }; }),
            L(() => { EditorGUILayout.MinMaxSlider("Label", ref a, ref b, 0f, 10f); return new object[] { F(a), F(b) }; }),
            L(() => { EditorGUILayout.SelectableLabel("text"); return "ok"; }),
            L(() => { EditorGUILayout.SelectableLabel("text", EditorStyles.miniLabel, GUILayout.Height(40)); return "ok"; }));
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
    }

    static Event Mouse(EventType type, Vector2 position, Vector2 delta = default, int clicks = 1) =>
        new Event { type = type, mousePosition = position, delta = delta, button = 0, clickCount = clicks };
    static Event Key(string key) => Event.KeyboardEvent(key);

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        // The arrow (left of the label) toggles; the label does not without toggleOnLabelClick.
        ("fold.arrow.down", () => Mouse(EventType.MouseDown, new Vector2(FoldRect.x + 5, FoldRect.center.y))),
        ("fold.arrow.up", () => Mouse(EventType.MouseUp, new Vector2(FoldRect.x + 5, FoldRect.center.y))),
        ("fold.left", () => Key("left")),
        ("fold.right", () => Key("right")),
        ("fold.right2", () => Key("right")),
        ("fold.label.down", () => Mouse(EventType.MouseDown, new Vector2(FoldRect.x + 80, FoldRect.center.y))),
        ("fold.label.up", () => Mouse(EventType.MouseUp, new Vector2(FoldRect.x + 80, FoldRect.center.y))),
        ("fold.away.down", () => Mouse(EventType.MouseDown, new Vector2(FoldRect.x + 5, FoldRect.center.y))),
        ("fold.away.up", () => Mouse(EventType.MouseUp, new Vector2(FoldRect.x + 200, FoldRect.center.y + 40))),
        ("foldLabel.down", () => Mouse(EventType.MouseDown, new Vector2(FoldLabelRect.x + 80, FoldLabelRect.center.y))),
        ("foldLabel.up", () => Mouse(EventType.MouseUp, new Vector2(FoldLabelRect.x + 80, FoldLabelRect.center.y))),
        // The min-max slider over 0..10 on a 200-point track: the thumb (2..6), its ends, the track.
        ("minMax.thumb.down", () => Mouse(EventType.MouseDown, new Vector2(MinMaxRect.x + 80, MinMaxRect.center.y))),
        ("minMax.thumb.drag", () => Mouse(EventType.MouseDrag, new Vector2(MinMaxRect.x + 100, MinMaxRect.center.y), new Vector2(20, 0))),
        ("minMax.thumb.up", () => Mouse(EventType.MouseUp, new Vector2(MinMaxRect.x + 100, MinMaxRect.center.y))),
        // After the thumb step (about 3.05..7.05, 19 points per unit) the thumb spans x 67..155:
        // its 6-point ends resize it.
        ("minMax.maxEnd.down", () => Mouse(EventType.MouseDown, new Vector2(MinMaxRect.x + 142, MinMaxRect.center.y))),
        ("minMax.maxEnd.drag", () => Mouse(EventType.MouseDrag, new Vector2(MinMaxRect.x + 162, MinMaxRect.center.y), new Vector2(20, 0))),
        ("minMax.maxEnd.up", () => Mouse(EventType.MouseUp, new Vector2(MinMaxRect.x + 162, MinMaxRect.center.y))),
        ("minMax.minEnd.down", () => Mouse(EventType.MouseDown, new Vector2(MinMaxRect.x + 60, MinMaxRect.center.y))),
        ("minMax.minEnd.drag", () => Mouse(EventType.MouseDrag, new Vector2(MinMaxRect.x + 40, MinMaxRect.center.y), new Vector2(-20, 0))),
        ("minMax.minEnd.up", () => Mouse(EventType.MouseUp, new Vector2(MinMaxRect.x + 40, MinMaxRect.center.y))),
        // A press beside the thumb (on the track).
        ("minMax.beside.down", () => Mouse(EventType.MouseDown, new Vector2(MinMaxRect.x + 175, MinMaxRect.center.y))),
        ("minMax.beside.drag", () => Mouse(EventType.MouseDrag, new Vector2(MinMaxRect.x + 185, MinMaxRect.center.y), new Vector2(10, 0))),
        ("minMax.beside.up", () => Mouse(EventType.MouseUp, new Vector2(MinMaxRect.x + 185, MinMaxRect.center.y))),
        ("minMax.track.down", () => Mouse(EventType.MouseDown, new Vector2(MinMaxRect.x + 10, MinMaxRect.center.y))),
        ("minMax.track.up", () => Mouse(EventType.MouseUp, new Vector2(MinMaxRect.x + 10, MinMaxRect.center.y))),
        ("popup.key", () => Key("down")),
    };

    /// <summary>The scripted controls, in the same order in every event so they keep their ids.</summary>
    object[] ScriptedFields()
    {
        var a = Field(() => m_Fold = EditorGUI.Foldout(FoldRect, m_Fold, "Fold"));
        var b = Field(() => m_FoldLabel = EditorGUI.Foldout(FoldLabelRect, m_FoldLabel, "Fold label", true));
        var c = Field(() => { EditorGUI.MinMaxSlider(MinMaxRect, ref m_Min, ref m_Max, 0f, 10f); return "ok"; });
        var d = Field(() => m_Popup = EditorGUI.IntPopup(PopupRect, m_Popup, Names, Values));
        return new object[]
        {
            m_Fold, m_FoldLabel, F(m_Min), F(m_Max), m_Popup,
            new object[] { a[2], b[2], c[2], d[2] },
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
        // The popup step starts from the slider's initial values, so it is compared on its own.
        if (step.Name == "popup.key") { m_Min = 2f; m_Max = 6f; }
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
