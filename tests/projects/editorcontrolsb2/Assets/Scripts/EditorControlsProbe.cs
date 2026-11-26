using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EditorControlsData : ScriptableObject
{
    public int number = 7;
    public float amount = 1.5f;
    public string text = "abc";
    public int choice = 2;
}

/// <summary>
/// Records, in a game's OnGUI in Play mode, what a set of EditorGUI / EditorGUILayout /
/// EditorGUIUtility members answer: in Layout and Repaint (value, control ids, GUI.changed,
/// layout rects), outside OnGUI, and through scripted mouse and key events, one per frame in place
/// of the frame's Repaint. Members: EditorGUI.ProgressBar, Popup and IntPopup of a property,
/// the delayed int, float and text fields of a property (EditorGUI and EditorGUILayout),
/// DropdownButton, BeginFoldoutHeaderGroup / EndFoldoutHeaderGroup, BeginDisabled / EndDisabled,
/// IsEditingTextField, LogarithmicIntSlider, EditorGUILayout.LinkButton and DropdownButton,
/// EditorGUIUtility.CommandEvent, SetWantsMouseJumping and the object
/// picker's GetObjectPickerObject / GetObjectPickerControlID.
/// </summary>
public sealed class EditorControlsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    int m_Step = -1;
    int m_Frames;
    bool m_Done;
    bool m_RecordedLayout;
    SerializedObject m_Object;
    bool m_Fold = true, m_FoldLayout;
    readonly List<string> m_Calls = new List<string>();

    static readonly MethodInfo s_Popup = typeof(EditorGUI).GetMethod("Popup", BindingFlags.NonPublic | BindingFlags.Static, null,
        new[] { typeof(Rect), typeof(SerializedProperty), typeof(GUIContent[]), typeof(GUIContent) }, null);
    static readonly MethodInfo s_BeginDisabled = typeof(EditorGUI).GetMethod("BeginDisabled", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo s_EndDisabled = typeof(EditorGUI).GetMethod("EndDisabled", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo s_IsEditing = typeof(EditorGUI).GetMethod("IsEditingTextField", BindingFlags.NonPublic | BindingFlags.Static);

    static readonly Rect ProgressRect = new Rect(10, 300, 200, 18);
    static readonly Rect DropdownRect = new Rect(10, 330, 200, 18);
    static readonly Rect HeaderRect = new Rect(10, 360, 200, 22);
    static readonly Rect DelayedRect = new Rect(10, 400, 200, 18);
    static readonly Rect LogRect = new Rect(10, 430, 300, 18);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_CONTROLS_B2_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("EditorControlsProbe").AddComponent<EditorControlsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.m_Object = new SerializedObject(ScriptableObject.CreateInstance<EditorControlsData>());
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (TargetInvocationException ex) { return "throws " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message; }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    SerializedProperty Prop(string name) => m_Object.FindProperty(name);

    static readonly GUIContent[] s_Options = { new GUIContent("Zero"), new GUIContent("One"), new GUIContent("Two") };
    static readonly int[] s_Values = { 0, 2, 4 };

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 100, 18);
        Put("outside",
            Try(() => { EditorGUI.ProgressBar(r, 0.5f, "Half"); return "ok"; }),
            Try(() => EditorGUI.DropdownButton(r, new GUIContent("Drop"), FocusType.Keyboard)),
            Try(() => EditorGUI.BeginFoldoutHeaderGroup(r, true, "Header")),
            Try(() => { EditorGUI.EndFoldoutHeaderGroup(); return "ok"; }),
            Try(() => { EditorGUI.DelayedIntField(r, Prop("number")); return "ok"; }),
            Try(() => EditorGUILayout.LinkButton("Link")),
            Try(() => EditorGUI.LogarithmicIntSlider(r, "Log", 10, 1, 1000, 10, 1, 1000)));
        var e = EditorGUIUtility.CommandEvent("Copy");
        Put("commandEvent", e.type.ToString(), e.commandName, e.button, e.clickCount, e.modifiers.ToString(), e.keyCode.ToString(), e.character == 0);
        Put("mouseJumping", Try(() => { EditorGUIUtility.SetWantsMouseJumping(1); EditorGUIUtility.SetWantsMouseJumping(0); return "ok"; }));
        Put("picker", Try(() => EditorGUIUtility.GetObjectPickerObject()?.name ?? "null"), Try(() => EditorGUIUtility.GetObjectPickerControlID()));
    }

    /// <summary>A control's answer in the current event: its value, the control ids it took and
    /// whether it set GUI.changed ("used" when the call used the event, "used before" when it already was).</summary>
    static object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        object ids = before < 0 ? "used before" : after < 0 ? "used" : (object)(after - before - 1);
        return new object[] { value, ids, changed };
    }

    void RecordFields(string phase)
    {
        var r = new Rect(10, 10, 200, 18);
        m_Object.Update();
        Put($"rect/{phase}",
            Field(() => { EditorGUI.ProgressBar(r, 0.25f, "Quarter"); return "ok"; }),
            Field(() => { s_Popup.Invoke(null, new object[] { r, Prop("choice"), s_Options, new GUIContent("Popup") }); return Prop("choice").intValue; }),
            Field(() => { EditorGUI.IntPopup(r, Prop("choice"), s_Options, s_Values, new GUIContent("IntPopup")); return Prop("choice").intValue; }),
            Field(() => { EditorGUI.IntPopup(r, Prop("choice"), s_Options, s_Values); return Prop("choice").intValue; }),
            Field(() => { EditorGUI.DelayedIntField(r, Prop("number")); return Prop("number").intValue; }),
            Field(() => { EditorGUI.DelayedIntField(r, Prop("number"), new GUIContent("Int")); return Prop("number").intValue; }),
            Field(() => { EditorGUI.DelayedFloatField(r, Prop("amount"), new GUIContent("Float")); return Prop("amount").floatValue; }),
            Field(() => { EditorGUI.DelayedTextField(r, Prop("text"), new GUIContent("Text")); return Prop("text").stringValue; }),
            Field(() => EditorGUI.DropdownButton(r, new GUIContent("Drop"), FocusType.Keyboard)),
            Field(() => EditorGUI.DropdownButton(r, new GUIContent("Drop"), FocusType.Passive)),
            Field(() => { var b = EditorGUI.BeginFoldoutHeaderGroup(r, true, "Header"); EditorGUI.EndFoldoutHeaderGroup(); return b; }),
            Field(() => { var b = EditorGUI.BeginFoldoutHeaderGroup(r, false, new GUIContent("Header"), null, rect => { }); EditorGUI.EndFoldoutHeaderGroup(); return b; }),
            Field(() =>
            {
                // A second header inside the first: an error box, false.
                EditorGUI.BeginFoldoutHeaderGroup(r, true, "Outer");
                var inner = EditorGUI.BeginFoldoutHeaderGroup(r, true, "Inner");
                EditorGUI.EndFoldoutHeaderGroup();
                EditorGUI.EndFoldoutHeaderGroup();
                return inner;
            }),
            Field(() => EditorGUI.LogarithmicIntSlider(r, "Log", 10, 1, 1000, 10, 1, 1000)),
            Field(() => EditorGUI.LogarithmicIntSlider(r, new GUIContent("Log"), 5000, 1, 1000, 2, 1, 10000)),
            Field(() =>
            {
                var states = new List<object> { GUI.enabled };
                s_BeginDisabled.Invoke(null, new object[] { true });
                states.Add(GUI.enabled);
                s_BeginDisabled.Invoke(null, new object[] { false });
                states.Add(GUI.enabled);
                s_EndDisabled.Invoke(null, null);
                states.Add(GUI.enabled);
                s_EndDisabled.Invoke(null, null);
                states.Add(GUI.enabled);
                s_EndDisabled.Invoke(null, null);
                states.Add(GUI.enabled);
                return states.ToArray();
            }),
            Field(() => s_IsEditing.Invoke(null, null)));
        m_Object.ApplyModifiedPropertiesWithoutUndo();
    }

    void RecordLayout(string phase)
    {
        var rects = new List<object>();
        object[] L(Func<object> call)
        {
            var answer = Field(call);
            rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
            return answer;
        }
        m_Object.Update();
        GUILayout.BeginArea(new Rect(320, 0, 300, 600));
        Put($"layout/{phase}",
            L(() => { EditorGUILayout.IntPopup(Prop("choice"), s_Options, s_Values); return Prop("choice").intValue; }),
            L(() => { EditorGUILayout.IntPopup(Prop("choice"), s_Options, s_Values, new GUIContent("IntPopup")); return Prop("choice").intValue; }),
            L(() => { EditorGUILayout.DelayedIntField(Prop("number"), new GUIContent("Int")); return Prop("number").intValue; }),
            L(() => EditorGUILayout.DropdownButton(new GUIContent("Drop"), FocusType.Keyboard)),
            L(() => EditorGUILayout.DropdownButton(new GUIContent("Drop"), FocusType.Keyboard, EditorStyles.miniButton)),
            L(() => { var b = EditorGUILayout.BeginFoldoutHeaderGroup(true, "Header"); EditorGUILayout.EndFoldoutHeaderGroup(); return b; }),
            L(() => { var b = EditorGUILayout.BeginFoldoutHeaderGroup(false, new GUIContent("Header")); EditorGUILayout.EndFoldoutHeaderGroup(); return b; }),
            L(() => EditorGUILayout.LinkButton("Link")),
            L(() => EditorGUILayout.LinkButton(new GUIContent("Link"), GUILayout.Width(80))));
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
        m_Object.ApplyModifiedPropertiesWithoutUndo();
    }

    static Event Mouse(EventType type, Vector2 position) => new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
    static Event Key(string key) => Event.KeyboardEvent(key);
    static Event Typed(char c) => new Event { type = EventType.KeyDown, character = c };

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        // The progress bar uses a mouse down on it.
        ("progress.down", () => Mouse(EventType.MouseDown, ProgressRect.center)),
        // The drop-down button: true on mouse down.
        ("dropdown.down", () => Mouse(EventType.MouseDown, DropdownRect.center)),
        ("dropdown.up", () => Mouse(EventType.MouseUp, DropdownRect.center)),
        // The foldout header: a click folds it, the arrow keys on the focused one, the menu icon.
        ("header.down", () => Mouse(EventType.MouseDown, HeaderRect.center)),
        ("header.up", () => Mouse(EventType.MouseUp, HeaderRect.center)),
        ("header.right", () => Key("right")),
        ("header.left", () => Key("left")),
        ("header.menu", () => Mouse(EventType.MouseDown, new Vector2(HeaderRect.xMax - 8, HeaderRect.y + 8))),
        // A delayed int field: typing changes nothing until Return.
        ("delayed.down", () => Mouse(EventType.MouseDown, new Vector2(DelayedRect.xMax - 20, DelayedRect.center.y))),
        ("delayed.up", () => Mouse(EventType.MouseUp, new Vector2(DelayedRect.xMax - 20, DelayedRect.center.y))),
        ("delayed.selectAll", () => Key("^a")),
        ("delayed.type4", () => Typed('4')),
        ("delayed.type2", () => Typed('2')),
        ("delayed.return", () => Key("return")),
        // The logarithmic slider's thumb track clicked.
        ("log.down", () => Mouse(EventType.MouseDown, new Vector2(LogRect.x + 200, LogRect.center.y))),
        ("log.up", () => Mouse(EventType.MouseUp, new Vector2(LogRect.x + 200, LogRect.center.y))),
    };

    int m_Log = 10;

    object[] ScriptedFields()
    {
        m_Object.Update();
        var progress = Field(() => { EditorGUI.ProgressBar(ProgressRect, 0.5f, "Half"); return "ok"; });
        var dropdown = Field(() => EditorGUI.DropdownButton(DropdownRect, new GUIContent("Drop"), FocusType.Keyboard));
        var header = Field(() =>
        {
            m_Fold = EditorGUI.BeginFoldoutHeaderGroup(HeaderRect, m_Fold, "Header", null, rect => m_Calls.Add("menu " + rect.x + "," + rect.y + "," + rect.width + "," + rect.height));
            EditorGUI.EndFoldoutHeaderGroup();
            return m_Fold;
        });
        var delayed = Field(() => { EditorGUI.DelayedIntField(DelayedRect, Prop("number"), new GUIContent("Number")); return Prop("number").intValue; });
        var log = Field(() => m_Log = EditorGUI.LogarithmicIntSlider(LogRect, "Log", m_Log, 1, 1000, 10, 1, 1000));
        bool editing = (bool)s_IsEditing.Invoke(null, null);
        m_Object.ApplyModifiedPropertiesWithoutUndo();
        return new object[]
        {
            progress, dropdown, header, delayed, log, editing, m_Calls.ToArray(),
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
            if (type == EventType.Layout && m_RecordedLayout) return;
            if (type == EventType.Repaint && !m_RecordedLayout) return;
            string phase = type == EventType.Layout ? "layout" : "repaint";
            RecordFields(phase);
            RecordLayout(phase);
            ScriptedFields();
            if (type == EventType.Layout) m_RecordedLayout = true;
            else { m_Step = 0; m_Calls.Clear(); Put("script/start", ScriptedFields()); }
            return;
        }
        if (type != EventType.Repaint || ++m_Frames % 2 != 0) { ScriptedFields(); return; }
        if (m_Step == Script.Length)
        {
            m_Done = true;
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
            return;
        }
        var real = Event.current;
        var step = Script[m_Step];
        var scripted = step.Make();
        Event.current = scripted;
        m_Calls.Clear();
        object[] answer;
        try { answer = ScriptedFields(); }
        finally { Event.current = real; }
        Put("script/" + step.Name, answer, scripted.type == EventType.Used);
        m_Step++;
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
