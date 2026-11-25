using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what IMGUI.Controls.SearchField and the internal
/// EditorGUI/EditorGUILayout search fields answer, and the Editor skin styles EditorStyles names:
/// outside OnGUI (the styles; the controls' exceptions); in Layout and Repaint (returned text,
/// control ids taken, GUI.changed, layout rects); and through a scripted sequence of mouse, key and
/// command events, one per frame, given to two search fields at fixed rects (a click and typing, the
/// arrow keys' callback, the cancel buttons, SetFocus and the Find command).
/// </summary>
public sealed class EditorSearchFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1;
    int m_Frames;

    SearchField m_Search;
    SearchField m_Toolbar;
    string m_SearchText = "";
    string m_ToolbarText = "query";
    int m_Arrows;

    static readonly Rect SearchRect = new Rect(10, 300, 200, 18);
    static readonly Rect ToolbarRect = new Rect(10, 330, 200, 18);

    // The EditorStyles members that are the Editor skin's style of that name.
    static readonly string[] Styles =
    {
        "whiteMiniLabel", "whiteLargeLabel", "whiteBoldLabel", "miniButtonMid", "miniButtonRight", "miniPullDown",
        "boldTextField", "toggleMixed", "titlebarFoldout", "foldoutPreDrop", "foldoutHeaderIcon", "optionsButtonStyle",
        "textFieldDropDown", "textFieldDropDownText", "overrideMargin", "contentToolbar", "toolbarButtonLeft",
        "toolbarButtonRight", "toolbarPopup", "toolbarPopupLeft", "toolbarPopupRight", "toolbarDropDownLeft",
        "toolbarDropDownRight", "toolbarDropDownToggle", "toolbarDropDownToggleButton",
        "toolbarDropDownToggleRight", "toolbarCreateAddNewDropDown", "toolbarTextField", "toolbarLabel",
        "toolbarSearchField", "toolbarSearchFieldPopup", "toolbarSearchFieldWithJumpSynced", "toolbarSearchFieldWithJumpPopupSynced",
        "toolbarSearchFieldWithJump", "toolbarSearchFieldWithJumpPopup", "toolbarSearchFieldJumpButton",
        "toolbarSearchFieldCancelButton", "toolbarSearchFieldCancelButtonEmpty",
        "toolbarSearchFieldCancelButtonWithJump", "toolbarSearchFieldCancelButtonWithJumpEmpty",
        "viewBackground", "inspectorBig", "inspectorTitlebar", "inspectorTitlebarFlat", "inspectorTitlebarText",
        "iconButton", "tooltip", "notificationText", "notificationBackground", "assetLabel", "assetLabelPartial",
        "assetLabelIcon", "searchField", "searchFieldCancelButton", "searchFieldCancelButtonEmpty", "toolbarSlider",
        "dropDownList", "dropDownToggleButton", "minMaxStateDropdown", "progressBarBack", "progressBarBar",
        "progressBarText", "scrollViewAlt",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_SEARCH_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorSearchFieldsProbe");
        var probe = go.AddComponent<EditorSearchFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };
    static object[] Offset(RectOffset o) => new object[] { o.left, o.right, o.top, o.bottom };
    static string Colour(Color c) => string.Join(" ", new[] { c.r, c.g, c.b, c.a }.Select(F));

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return "throws " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
        }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static object Call(Type type, string name, Type[] parameters, params object[] arguments) =>
        type.GetMethod(name, Static, null, parameters, null).Invoke(null, arguments);

    // The internal search fields, through reflection (a game's code cannot name them).
    static string ToolbarSearchField(Rect r, string text, bool popupArrow) =>
        (string)Call(typeof(EditorGUI), "ToolbarSearchField", new[] { typeof(Rect), typeof(string), typeof(bool) }, r, text, popupArrow);
    static string ToolbarSearchFieldModes(Rect r, string[] modes, int mode, string text) =>
        (string)Call(typeof(EditorGUI), "ToolbarSearchField", new[] { typeof(Rect), typeof(string[]), typeof(int).MakeByRefType(), typeof(string) }, r, modes, mode, text);
    static string EditorSearchField(Rect r, string text) =>
        (string)Call(typeof(EditorGUI), "SearchField", new[] { typeof(Rect), typeof(string) }, r, text);
    static string LayoutToolbarSearchField(string text) =>
        (string)Call(typeof(EditorGUILayout), "ToolbarSearchField", new[] { typeof(string), typeof(GUILayoutOption[]) }, text, new GUILayoutOption[0]);

    void RecordStyles()
    {
        foreach (var name in Styles)
        {
            var property = typeof(EditorStyles).GetProperty(name, Static);
            Put("style/" + name, Try(() =>
            {
                var s = (GUIStyle)property.GetValue(null);
                return new object[]
                {
                    s.name, F(s.fixedWidth), F(s.fixedHeight), Offset(s.padding), Offset(s.margin), Offset(s.border), Offset(s.overflow),
                    s.fontSize, s.fontStyle.ToString(), s.alignment.ToString(), s.wordWrap, s.clipping.ToString(), s.stretchWidth, s.stretchHeight,
                    F(s.contentOffset.x), F(s.contentOffset.y), s.imagePosition.ToString(), s.richText,
                    Colour(s.normal.textColor), s.normal.background != null, s.normal.scaledBackgrounds.Length,
                };
            }));
        }
    }

    void RecordOutsideGui()
    {
        RecordStyles();
        var r = new Rect(0, 0, 100, 18);
        var field = new SearchField();
        Put("outside/searchField", field.searchFieldControlID != 0, field.autoSetFocusOnFindCommand, field.HasFocus(),
            Try(() => field.OnGUI(r, "x")), Try(() => field.OnToolbarGUI(r, "x")),
            Try(() => field.OnGUI("x")), Try(() => field.OnToolbarGUI("x")),
            Try(() => field.OnGUI(r, "x", EditorStyles.textField, EditorStyles.miniButton, EditorStyles.miniButton)));
        Put("outside/internal",
            Try(() => ToolbarSearchField(r, "x", false)), Try(() => ToolbarSearchField(r, "", true)),
            Try(() => ToolbarSearchFieldModes(r, new[] { "All", "Name" }, 0, "")), Try(() => EditorSearchField(r, "x")),
            Try(() => LayoutToolbarSearchField("x")));
    }

    /// <summary>The control id a passive control gets at this point of the pass, by phase (kept
    /// apart from the controls' answers: the ids' numbering is the pass's).</summary>
    readonly List<object> m_Ids = new List<object>();

    /// <summary>A control's answer in the current event: its value, the control ids it took ("used"
    /// once the event is used: no id is given then) and whether it set GUI.changed.</summary>
    object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        m_Ids.Add(before);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        return new object[] { value, after == -1 ? "used" : (object)(after - before - 1), changed };
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 18);
        var field = new SearchField();
        Put($"rect/{phase}/searchField",
            Field(() => field.OnGUI(r, "")), Field(() => field.OnGUI(r, "text")),
            Field(() => field.OnToolbarGUI(r, "")), Field(() => field.OnToolbarGUI(r, "text")),
            Field(() => field.OnGUI(r, "text", EditorStyles.textField, EditorStyles.miniButton, EditorStyles.label)),
            Field(() => field.OnGUI(r, null)));
        Put($"rect/{phase}/internal",
            Field(() => ToolbarSearchField(r, "", false)), Field(() => ToolbarSearchField(r, "text", true)),
            Field(() => ToolbarSearchFieldModes(r, new[] { "All", "Name" }, 1, "")), Field(() => ToolbarSearchFieldModes(r, null, 0, "text")),
            Field(() => EditorSearchField(r, "")), Field(() => EditorSearchField(r, "text")));
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
        var field = new SearchField();
        GUILayout.BeginArea(new Rect(320, 0, 300, 600));
        Put($"layout/{phase}/fields",
            L(() => field.OnGUI("text")), L(() => field.OnGUI("text", GUILayout.Width(120))),
            L(() => field.OnToolbarGUI("text")), L(() => field.OnToolbarGUI("", GUILayout.ExpandWidth(true))),
            L(() => LayoutToolbarSearchField("text")));
        GUILayout.BeginHorizontal();
        Put($"layout/{phase}/horizontal", L(() => field.OnToolbarGUI("a")), L(() => field.OnGUI("b")));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
    }

    static Event Mouse(EventType type, Vector2 position, int clicks = 1) =>
        new Event { type = type, mousePosition = position, button = 0, clickCount = clicks };
    static Event Key(string key) => Event.KeyboardEvent(key);
    static Event Command(EventType type, string name) => new Event { type = type, commandName = name };

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        // A click into the first field, typing, the arrow keys' callback.
        ("search.click.down", () => Mouse(EventType.MouseDown, new Vector2(SearchRect.x + 50, SearchRect.center.y))),
        ("search.click.up", () => Mouse(EventType.MouseUp, new Vector2(SearchRect.x + 50, SearchRect.center.y))),
        ("search.type.a", () => Key("a")),
        ("search.type.b", () => Key("b")),
        ("search.down", () => Key("down")),
        ("search.up", () => Key("up")),
        // The first field's cancel button (the right end) clears it and drops the focus.
        ("search.cancel.up", () => Mouse(EventType.MouseUp, new Vector2(SearchRect.xMax - 4, SearchRect.center.y))),
        // The toolbar field's cancel button with its text, without focus.
        ("toolbar.cancel.up", () => Mouse(EventType.MouseUp, new Vector2(ToolbarRect.xMax - 4, ToolbarRect.center.y))),
        // Find: validated and executed (both fields take it; the last one drawn keeps the focus).
        ("find.validate", () => Command(EventType.ValidateCommand, "Find")),
        ("find.execute", () => Command(EventType.ExecuteCommand, "Find")),
        ("find.repaint", () => new Event { type = EventType.Repaint }),
        ("find.type.c", () => Key("c")),
        ("other.command", () => Command(EventType.ExecuteCommand, "Copy")),
    };

    /// <summary>The scripted controls, in the same order in every event so they keep their ids.</summary>
    object[] ScriptedFields()
    {
        if (m_Search == null)
        {
            m_Search = new SearchField();
            m_Search.downOrUpArrowKeyPressed += () => m_Arrows++;
            m_Toolbar = new SearchField();
        }
        var a = Field(() => m_SearchText = m_Search.OnGUI(SearchRect, m_SearchText));
        var b = Field(() => m_ToolbarText = m_Toolbar.OnToolbarGUI(ToolbarRect, m_ToolbarText));
        return new object[]
        {
            m_SearchText, m_ToolbarText, m_Arrows, m_Search.HasFocus(), m_Toolbar.HasFocus(),
            new object[] { a[1], a[2], b[1], b[2] },
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
            m_Ids.Clear();
            ScriptedFields();
            RecordRectFields(phase);
            RecordLayoutFields(phase);
            Put($"ids/{phase}", m_Ids.ToArray());
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
        m_Ids.Clear();
        try { answer = ScriptedFields(); }
        finally { Event.current = real; }
        Put("script/" + step.Name, answer, (int)scripted.type);
        Put("ids/script/" + step.Name, m_Ids.ToArray());
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
