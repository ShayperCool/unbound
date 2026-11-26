using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's
/// InspectorTitlebar answer: outside OnGUI; in Layout and Repaint (returned value, control ids
/// taken, GUI.changed, layout rects) for a behaviour, a disabled behaviour, a Transform, a
/// renderer, a ScriptableObject, a GameObject, mixed and an Editor; the targets' mini thumbnails;
/// and through scripted mouse and keyboard events, one per frame in place of the frame's Repaint
/// (the title clicked, arrow keys on the focused titlebar, the enabled toggle clicked for one and
/// for two behaviours). The titlebars it draws are captured in titlebars.png, and what the toggle and header
/// items depend on (GetObjectEnabled, PresetType, Help) is recorded.
/// </summary>
public sealed class EditorTitlebarProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1;
    int m_Frames;
    bool m_Fold, m_FoldMixed;

    TitlebarTarget m_Behaviour, m_Disabled;
    MeshRenderer m_Renderer;
    TitlebarAsset m_Asset;
    Editor m_Editor;

    static readonly Rect FoldRect = new Rect(10, 300, 300, 22);
    static readonly Rect MixedRect = new Rect(10, 330, 300, 22);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_TITLEBAR_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorTitlebarProbe");
        var probe = go.AddComponent<EditorTitlebarProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        var target = new GameObject("Titlebar Target");
        probe.m_Behaviour = target.AddComponent<TitlebarTarget>();
        probe.m_Disabled = target.AddComponent<TitlebarTarget>();
        probe.m_Disabled.enabled = false;
        target.AddComponent<MeshFilter>();
        probe.m_Renderer = target.AddComponent<MeshRenderer>();
        probe.m_Asset = ScriptableObject.CreateInstance<TitlebarAsset>();
        probe.m_Asset.name = "Titlebar Asset";
        probe.m_Editor = Editor.CreateEditor(probe.m_Behaviour);
        probe.RecordOutsideGui();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static object[] RectOffsetArr(RectOffset o) => new object[] { o.left, o.right, o.top, o.bottom };
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    Object[] Mixed => new Object[] { m_Behaviour, m_Disabled };

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 300, 22);
        Put("outside/editorGUI",
            Try(() => { EditorGUI.InspectorTitlebar(r, new Object[] { m_Behaviour }); return "ok"; }),
            Try(() => EditorGUI.InspectorTitlebar(r, false, m_Behaviour, true)),
            Try(() => EditorGUI.InspectorTitlebar(r, false, Mixed, true)),
            Try(() => EditorGUI.InspectorTitlebar(r, false, m_Editor)));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.InspectorTitlebar(false, m_Behaviour)),
            Try(() => EditorGUILayout.InspectorTitlebar(false, m_Behaviour, false)),
            Try(() => EditorGUILayout.InspectorTitlebar(false, Mixed)),
            Try(() => EditorGUILayout.InspectorTitlebar(false, Mixed, false)),
            Try(() => EditorGUILayout.InspectorTitlebar(false, m_Editor)),
            Try(() => { EditorGUILayout.InspectorTitlebar(new Object[] { m_Behaviour }); return "ok"; }));
        Put("thumbnails", new Object[] { m_Behaviour, m_Renderer, m_Behaviour.transform, m_Behaviour.gameObject, m_Asset }
            .Select(o => Try(() => AssetPreview.GetMiniThumbnail(o) is Texture2D t ? t.name + " " + t.width + "x" + t.height : "null")).ToArray());
        var objects = new Object[] { m_Behaviour, m_Renderer, m_Behaviour.transform, m_Behaviour.gameObject, m_Asset, m_Editor };
        // Behaviours with one event function each, on an inactive object (none of them runs).
        var functions = new GameObject("Titlebar Functions");
        functions.SetActive(false);
        Put("objectEnabled/functions", new[] { typeof(TitlebarWithAwake), typeof(TitlebarWithStart), typeof(TitlebarWithUpdate), typeof(TitlebarWithFixedUpdate), typeof(TitlebarWithLateUpdate), typeof(TitlebarWithOnGUI), typeof(TitlebarWithOnEnable), typeof(TitlebarWithOnDisable), typeof(TitlebarWithOnDestroy), typeof(TitlebarWithOnValidate), typeof(TitlebarWithReset), typeof(TitlebarWithOnDrawGizmos), typeof(TitlebarWithOnRenderObject), typeof(TitlebarWithOnBecameVisible), typeof(TitlebarWithOnApplicationQuit), typeof(TitlebarDerived) }
            .Select(t => (object)new object[] { t.Name, Try(() => EditorUtility.GetObjectEnabled(functions.AddComponent(t))) }).ToArray());
        // What the titlebar's enabled toggle and header items (preset and help buttons) depend on.
        Put("objectEnabled", objects.Select(o => Try(() => EditorUtility.GetObjectEnabled(o))).ToArray());
        Put("presetValid", objects.Select(o => Try(() => new UnityEditor.Presets.PresetType(o).IsValid())).ToArray());
        Put("hasHelp", objects.Select(o => Try(() => Help.HasHelpForObject(o))).ToArray());
        Put("style", Try(() => RectOffsetArr(TitleStyle.padding)), Try(() => TitleStyle.fixedHeight));
        Put("titles", new Object[] { m_Behaviour, m_Renderer, m_Behaviour.transform, m_Behaviour.gameObject, m_Asset }
            .Select(o => (object)ObjectNames.GetInspectorTitle(o)).ToArray());
    }

    /// <summary>A control's answer in the current event: its value, the control ids it took and
    /// whether it set GUI.changed. In a used event GetControlID answers -1: "used" when the call
    /// used the event, "used before" when it already was.</summary>
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

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 22);
        Put($"rect/{phase}/titlebar",
            Field(() => { EditorGUI.InspectorTitlebar(r, new Object[] { m_Behaviour }); return "ok"; }),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Behaviour, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, true, m_Behaviour, false)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Disabled, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Behaviour.transform, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Renderer, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Asset, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Behaviour.gameObject, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, true, Mixed, true)),
            Field(() => EditorGUI.InspectorTitlebar(r, false, m_Editor)));
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
        Put($"layout/{phase}/titlebar",
            L(() => EditorGUILayout.InspectorTitlebar(false, m_Behaviour)),
            L(() => EditorGUILayout.InspectorTitlebar(true, m_Behaviour, false)),
            L(() => EditorGUILayout.InspectorTitlebar(false, Mixed)),
            L(() => EditorGUILayout.InspectorTitlebar(true, Mixed, false)),
            L(() => EditorGUILayout.InspectorTitlebar(false, m_Editor)),
            L(() => { EditorGUILayout.InspectorTitlebar(new Object[] { m_Asset }); return "ok"; }));
        GUILayout.EndArea();
        Put($"layout/{phase}/rects", rects.ToArray());
    }

    static Event Mouse(EventType type, Vector2 position, int button = 0) =>
        new Event { type = type, mousePosition = position, button = button, clickCount = 1 };
    static Event Key(string key) => Event.KeyboardEvent(key);

    /// <summary>The enabled toggle of a titlebar: right of the icon (its style's padding, the 16-point
    /// icon and 4 points).</summary>
    static Vector2 Toggle(Rect titlebar) =>
        new Vector2(titlebar.x + TitleStyle.padding.left + 16 + 4 + 7, titlebar.center.y);

    /// <summary>The titlebar's style ("IN Title" of the Inspector skin; EditorStyles' field is internal).</summary>
    static GUIStyle TitleStyle => EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).GetStyle("IN Title");

    static readonly (string Name, Func<Event> Make)[] Script =
    {
        // The title clicked folds the titlebar out; the arrow keys fold it when it has the focus.
        ("title.down", () => Mouse(EventType.MouseDown, new Vector2(FoldRect.x + 150, FoldRect.center.y))),
        ("title.up", () => Mouse(EventType.MouseUp, new Vector2(FoldRect.x + 150, FoldRect.center.y))),
        ("title.left", () => Key("left")),
        ("title.right", () => Key("right")),
        ("title.away.down", () => Mouse(EventType.MouseDown, new Vector2(FoldRect.x + 150, FoldRect.center.y))),
        ("title.away.up", () => Mouse(EventType.MouseUp, new Vector2(FoldRect.x + 150, FoldRect.center.y + 60))),
        // The enabled toggle of one behaviour, then of two behaviours one of which is disabled.
        ("toggle.down", () => Mouse(EventType.MouseDown, Toggle(FoldRect))),
        ("toggle.up", () => Mouse(EventType.MouseUp, Toggle(FoldRect))),
        ("mixed.down", () => Mouse(EventType.MouseDown, Toggle(MixedRect))),
        ("mixed.up", () => Mouse(EventType.MouseUp, Toggle(MixedRect))),
        ("mixedTitle.down", () => Mouse(EventType.MouseDown, new Vector2(MixedRect.x + 150, MixedRect.center.y))),
        ("mixedTitle.up", () => Mouse(EventType.MouseUp, new Vector2(MixedRect.x + 150, MixedRect.center.y))),
    };

    /// <summary>The scripted controls, in the same order in every event so they keep their ids.</summary>
    object[] ScriptedFields()
    {
        var a = Field(() => m_Fold = EditorGUI.InspectorTitlebar(FoldRect, m_Fold, m_Behaviour, true));
        var b = Field(() => m_FoldMixed = EditorGUI.InspectorTitlebar(MixedRect, m_FoldMixed, Mixed, true));
        return new object[]
        {
            m_Fold, m_FoldMixed, m_Behaviour.enabled, m_Disabled.enabled,
            new object[] { a[1], a[2], b[1], b[2] },
            GUIUtility.keyboardControl != 0, GUIUtility.hotControl != 0,
        };
    }

    /// <summary>Titlebars drawn for titlebars.png, alone in the frames after the script (the scripted
    /// ones lie below the game view).</summary>
    void DrawShowcase()
    {
        EditorGUI.InspectorTitlebar(new Rect(10, 30, 300, 22), false, m_Behaviour, true);
        EditorGUI.InspectorTitlebar(new Rect(10, 55, 300, 22), true, m_Renderer, true);
        EditorGUI.InspectorTitlebar(new Rect(10, 80, 300, 22), false, m_Asset, false);
        EditorGUI.InspectorTitlebar(new Rect(10, 105, 300, 22), new Object[] { m_Behaviour.transform });
        EditorGUI.InspectorTitlebar(new Rect(10, 130, 300, 22), true, Mixed, true);
        EditorGUI.InspectorTitlebar(new Rect(10, 155, 300, 22), false, m_Behaviour.gameObject, true);
    }

    void OnGUI()
    {
        var type = Event.current.type;
        // After the script the titlebars stay drawn until the capture.
        if (m_Done) { if (type == EventType.Layout || type == EventType.Repaint) { ScriptedFields(); DrawShowcase(); } return; }
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
        if (type != EventType.Repaint) { ScriptedFields(); DrawShowcase(); return; }
        if (++m_Frames % 2 != 0) { ScriptedFields(); DrawShowcase(); return; }
        if (m_Step == Script.Length)
        {
            // The frame after the script: the titlebars as drawn, then the records.
            ScriptedFields();
            DrawShowcase();
            StartCoroutine(Finish());
            m_Done = true;
            return;
        }
        var real = Event.current;
        var step = Script[m_Step];
        var scripted = step.Make();
        Event.current = scripted;
        object[] answer;
        try { answer = ScriptedFields(); }
        finally { Event.current = real; }
        Put("script/" + step.Name, answer, (int)scripted.type, scripted.type == EventType.Used);
        m_Step++;
    }

    IEnumerator Finish()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        Put("screen", Screen.width, Screen.height);
        // The game view (320x180): only the showcase is drawn in this frame.
        var crop = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        crop.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        crop.Apply();
        Directory.CreateDirectory(m_Directory);
        File.WriteAllBytes(Path.Combine(m_Directory, "titlebars.png"), crop.EncodeToPNG());
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
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
