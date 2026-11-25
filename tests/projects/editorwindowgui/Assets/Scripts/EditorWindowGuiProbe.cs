using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class EditorWindowGuiProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    bool m_RecordedLayout;
    EditorWindowGuiProbeWindow m_Opened;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_WINDOW_GUI_OUT");
        if (string.IsNullOrEmpty(directory)) return;

        var go = new GameObject("EditorWindowGuiProbe");
        var probe = go.AddComponent<EditorWindowGuiProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.RecordOutsideGui(go);
    }

    void Put(string key, params object[] values)
    {
        m_Output.Append("{\"k\":").Append(Json(key))
            .Append(",\"v\":").Append(Json(values)).Append("}\n");
    }

    /// <summary>A name with its digits as '#': the root's name counts the Editor's windows.</summary>
    static string Masked(string name) => name == null ? null : System.Text.RegularExpressions.Regex.Replace(name, "[0-9]+", "#");

    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static string ExceptionName(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    static (string error, List<string> logs) CaptureLogs(Action action)
    {
        var logs = new List<string>();
        Application.LogCallback handler = (condition, _, type) => logs.Add(type + ":" + condition);
        Application.logMessageReceived += handler;
        string error = null;
        try { action(); }
        catch (Exception ex) { error = ex.GetType().Name; }
        finally { Application.logMessageReceived -= handler; }
        return (error, logs);
    }

    /// <summary>Whether the object's icon is <paramref name="expected"/> (null: none), or the exception's name.</summary>
    static object IconIs(UnityEngine.Object obj, Texture2D expected)
    {
        try
        {
            var icon = EditorGUIUtility.GetIconForObject(obj);
            return expected == null ? (object)(icon == null) : ReferenceEquals(icon, expected);
        }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    void RecordOutsideGui(GameObject go)
    {
        Section("icons", () => RecordObjectIcons(go));
        Section("window", RecordRootVisualElement);
        Section("cursor", RecordCursorRectOutsideGui);
        Section("handles", RecordHandlesOutsideGui);
        Section("reorderable", RecordReorderableOutsideGui);
    }

    /// <summary>Runs one part; an exception it throws is recorded and the next part runs.</summary>
    void Section(string name, Action record)
    {
        try { record(); }
        catch (Exception ex) { Put(name + "/error", ex.GetType().Name, ex.Message.Split('\n')[0]); }
    }

    void RecordObjectIcons(GameObject go)
    {
        var so = ScriptableObject.CreateInstance<EditorWindowGuiProbeData>();
        var tex1 = new Texture2D(4, 4) { name = "icon-one" };
        var tex2 = new Texture2D(4, 4) { name = "icon-two" };

        Put("icons/initial", IconIs(null, null), IconIs(go, null), IconIs(so, null), IconIs(go.transform, null));

        var setNull = CaptureLogs(() => EditorGUIUtility.SetIconForObject(null, tex1));
        Put("icons/setNull", setNull.error, setNull.logs.Count);

        var setGo1 = CaptureLogs(() => EditorGUIUtility.SetIconForObject(go, tex1));
        Put("icons/setGo", setGo1.error, setGo1.logs.Count, IconIs(go, tex1));

        var overwrite = CaptureLogs(() => EditorGUIUtility.SetIconForObject(go, tex2));
        Put("icons/overwriteGo", overwrite.error, overwrite.logs.Count, IconIs(go, tex2));

        var clear = CaptureLogs(() => EditorGUIUtility.SetIconForObject(go, null));
        Put("icons/clearGo", clear.error, clear.logs.Count, IconIs(go, null));

        var setSo = CaptureLogs(() => EditorGUIUtility.SetIconForObject(so, tex1));
        var setComponent = CaptureLogs(() => EditorGUIUtility.SetIconForObject(go.transform, tex2));
        Put("icons/soAndComponent", setSo.error, setSo.logs.Count, setComponent.error, setComponent.logs.Count,
            IconIs(so, tex1), IconIs(go.transform, tex2), IconIs(go, null));

        // Destroy the icon texture while assigned
        var tempTex = new Texture2D(2, 2) { name = "icon-temp" };
        var tempGo = new GameObject("icon-target");
        EditorGUIUtility.SetIconForObject(tempGo, tempTex);
        var hadBeforeDestroy = IconIs(tempGo, tempTex);
        DestroyImmediate(tempTex);
        Put("icons/afterTexDestroy", hadBeforeDestroy, IconIs(tempGo, null));

        // Destroy the target object
        EditorGUIUtility.SetIconForObject(tempGo, tex1);
        DestroyImmediate(tempGo);
        var getDestroyed = CaptureLogs(() => _ = EditorGUIUtility.GetIconForObject(tempGo));
        var setDestroyed = CaptureLogs(() => EditorGUIUtility.SetIconForObject(tempGo, tex2));
        Put("icons/afterObjDestroy",
            getDestroyed.error, getDestroyed.logs.Count, IconIs(tempGo, null),
            setDestroyed.error, setDestroyed.logs.Count);

        DestroyImmediate(so);
        DestroyImmediate(tex1);
        DestroyImmediate(tex2);
    }

    void RecordRootVisualElement()
    {
        var unshown = ScriptableObject.CreateInstance<EditorWindowGuiProbeWindow>();
        var r1 = unshown.rootVisualElement;
        var r2 = unshown.rootVisualElement;
        Put("window/unshown",
            r1 != null,
            ReferenceEquals(r1, r2),
            r1?.GetType().Name,
            Masked(r1?.name),
            r1?.childCount,
            r1?.parent == null,
            r1?.panel != null,
            r1?.panel?.contextType.ToString(),
            r1 != null ? r1.GetClasses().ToArray() : Array.Empty<string>(),
            unshown.Events.ToArray());

        var child = new VisualElement { name = "probe-child" };
        r1.Add(child);
        Put("window/unshownAddChild",
            r1.childCount,
            ReferenceEquals(r1.Q("probe-child"), child),
            child.panel != null);

        unshown.Show();
        var rAfterShow = unshown.rootVisualElement;
        Put("window/afterShow",
            ReferenceEquals(r1, rAfterShow),
            rAfterShow.childCount,
            rAfterShow.panel != null,
            rAfterShow.panel?.contextType.ToString(),
            unshown.Events.ToArray());

        unshown.Close();
        Put("window/afterClose",
            unshown == null,
            r1.childCount,
            r1.parent == null,
            r1.panel != null);

        var opened = EditorWindow.GetWindow<EditorWindowGuiProbeWindow>("RootProbe");
        var ro = opened.rootVisualElement;
        Put("window/getWindow",
            ro != null,
            ro?.GetType().Name,
            Masked(ro?.name),
            ro?.childCount,
            ro?.panel != null,
            ro?.panel?.contextType.ToString(),
            opened.Events.ToArray());
        // Kept open: OnGUI, in a later frame, focuses it again.
        m_Opened = opened;
    }

    /// <summary>A window focused in an earlier frame: GetWindow and Focus again, then Close.</summary>
    void RecordLaterFocus()
    {
        if (m_Opened == null) return;
        var window = m_Opened;
        m_Opened = null;
        int before = window.Events.Count;
        var again = EditorWindow.GetWindow<EditorWindowGuiProbeWindow>();
        var afterGetWindow = window.Events.Skip(before).ToArray();
        before = window.Events.Count;
        window.Focus();
        var afterFocus = window.Events.Skip(before).ToArray();
        window.Close();
        Put("window/laterFrame", ReferenceEquals(again, window), afterGetWindow, afterFocus);
    }

    void RecordCursorRectOutsideGui()
    {
        var c2 = CaptureLogs(() => EditorGUIUtility.AddCursorRect(new Rect(0, 0, 20, 20), MouseCursor.Link));
        var c3 = CaptureLogs(() => EditorGUIUtility.AddCursorRect(new Rect(0, 0, 20, 20), MouseCursor.ResizeHorizontal, 7));
        Put("cursor/outsideGui", c2.error, c2.logs.Count, c3.error, c3.logs.Count);
    }

    void RecordHandlesOutsideGui()
    {
        var pts = new[] { Vector3.zero, Vector3.right, Vector3.up };
        var cols = new[] { Color.red, Color.green, Color.blue };
        var segs = new[] { Vector3.zero, Vector3.right };
        var idx = new[] { 0, 1 };
        var tex = Texture2D.whiteTexture;
        var rect = new Rect(0, 0, 10, 10);
        var quad = new[] { Vector3.zero, Vector3.right, Vector3.right + Vector3.up, Vector3.up };

        Put("handles/outsideGui/aaPoly",
            ExceptionName(() => Handles.DrawAAPolyLine(pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(tex, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(tex, 2f, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, 2, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(cols, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, cols, pts)));

        Put("handles/outsideGui/shapes",
            ExceptionName(() => Handles.DrawPolyLine(pts)),
            ExceptionName(() => Handles.DrawLines(segs)),
            ExceptionName(() => Handles.DrawLines(pts, idx)),
            ExceptionName(() => Handles.DrawDottedLine(Vector3.zero, Vector3.right, 4f)),
            ExceptionName(() => Handles.DrawDottedLines(segs, 4f)),
            ExceptionName(() => Handles.DrawDottedLines(pts, idx, 4f)),
            ExceptionName(() => Handles.DrawBezier(Vector3.zero, Vector3.right, Vector3.up, Vector3.down, Color.white, tex, 2f)),
            ExceptionName(() => Handles.DrawWireCube(Vector3.zero, Vector3.one)),
            ExceptionName(() => Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f)),
            ExceptionName(() => Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f, 2f)),
            ExceptionName(() => Handles.DrawWireArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f)),
            ExceptionName(() => Handles.DrawWireArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f, 2f)),
            ExceptionName(() => Handles.DrawSolidDisc(Vector3.zero, Vector3.forward, 1f)),
            ExceptionName(() => Handles.DrawSolidArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f)),
            ExceptionName(() => Handles.DrawAAConvexPolygon(quad)),
            ExceptionName(() => Handles.DrawSolidRectangleWithOutline(rect, Color.white, Color.black)),
            ExceptionName(() => Handles.DrawSolidRectangleWithOutline(quad, Color.white, Color.black)));

        Put("handles/outsideGui/labelAndRepaint",
            ExceptionName(() => Handles.Label(Vector3.zero, tex)),
            ExceptionName(() => Handles.Label(Vector3.zero, new GUIContent("hi"))),
            ExceptionName(() => Handles.Label(Vector3.zero, "hi", GUIStyle.none)),
            ExceptionName(() => Handles.Label(Vector3.zero, new GUIContent("hi"), GUIStyle.none)),
            ExceptionName(() => HandleUtility.Repaint()));
    }

    void RecordReorderableOutsideGui()
    {
        var list = new ReorderableList(new List<int> { 1, 2 }, typeof(int));
        Put("reorderable/outsideGui",
            ExceptionName(() => list.DoList(new Rect(0, 0, 200, 100))),
            ExceptionName(() => list.DoList(new Rect(0, 0, 200, 100), new Rect(0, 0, 200, 100))),
            ExceptionName(() => list.DoLayoutList()));
    }

    void OnGUI()
    {
        if (m_Done) return;
        var evt = Event.current.type;
        if (evt != EventType.Layout && evt != EventType.Repaint) return;
        string phase = evt == EventType.Layout ? "layout" : "repaint";
        if (evt == EventType.Layout && m_RecordedLayout) return;
        if (evt == EventType.Layout) m_RecordedLayout = true;

        if (evt == EventType.Layout) Section("window", RecordLaterFocus);

        // 1. AddCursorRect inside OnGUI
        bool prevChanged = GUI.changed;
        GUI.changed = false;
        var c2 = CaptureLogs(() => EditorGUIUtility.AddCursorRect(new Rect(10, 10, 40, 20), MouseCursor.Link));
        var c3 = CaptureLogs(() => EditorGUIUtility.AddCursorRect(new Rect(10, 10, 40, 20), MouseCursor.ResizeHorizontal, 123));
        Put("cursor/" + phase, c2.error, c2.logs.Count, c3.error, c3.logs.Count, GUI.changed);
        GUI.changed = prevChanged;

        // 2. Handles inside OnGUI (normal + edge cases)
        var pts = new[] { Vector3.zero, Vector3.right, Vector3.up };
        var cols = new[] { Color.red, Color.green, Color.blue };
        var shortCols = new[] { Color.red };
        var segs = new[] { Vector3.zero, Vector3.right };
        var idx = new[] { 0, 1 };
        var tex = Texture2D.whiteTexture;
        var rect = new Rect(0, 0, 10, 10);
        var quad = new[] { Vector3.zero, Vector3.right, Vector3.right + Vector3.up, Vector3.up };

        Put("handles/" + phase + "/aaPoly",
            ExceptionName(() => Handles.DrawAAPolyLine(pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(tex, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(tex, 2f, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, 2, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(cols, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, cols, pts)));

        Put("handles/" + phase + "/aaPolyEdges",
            ExceptionName(() => Handles.DrawAAPolyLine((Vector3[])null)),
            ExceptionName(() => Handles.DrawAAPolyLine(Array.Empty<Vector3>())),
            ExceptionName(() => Handles.DrawAAPolyLine(new[] { Vector3.zero })),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, -1, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(2f, 10, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine(shortCols, pts)),
            ExceptionName(() => Handles.DrawAAPolyLine((Color[])null, pts)));

        Put("handles/" + phase + "/shapes",
            ExceptionName(() => Handles.DrawPolyLine(pts)),
            ExceptionName(() => Handles.DrawLines(segs)),
            ExceptionName(() => Handles.DrawLines(pts, idx)),
            ExceptionName(() => Handles.DrawDottedLine(Vector3.zero, Vector3.right, 4f)),
            ExceptionName(() => Handles.DrawDottedLines(segs, 4f)),
            ExceptionName(() => Handles.DrawDottedLines(pts, idx, 4f)),
            ExceptionName(() => Handles.DrawBezier(Vector3.zero, Vector3.right, Vector3.up, Vector3.down, Color.white, tex, 2f)),
            ExceptionName(() => Handles.DrawWireCube(Vector3.zero, Vector3.one)),
            ExceptionName(() => Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f)),
            ExceptionName(() => Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f, 2f)),
            ExceptionName(() => Handles.DrawWireArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f)),
            ExceptionName(() => Handles.DrawWireArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f, 2f)),
            ExceptionName(() => Handles.DrawSolidDisc(Vector3.zero, Vector3.forward, 1f)),
            ExceptionName(() => Handles.DrawSolidArc(Vector3.zero, Vector3.forward, Vector3.right, 90f, 1f)),
            ExceptionName(() => Handles.DrawAAConvexPolygon(quad)),
            ExceptionName(() => Handles.DrawSolidRectangleWithOutline(rect, Color.white, Color.black)),
            ExceptionName(() => Handles.DrawSolidRectangleWithOutline(quad, Color.white, Color.black)));

        Put("handles/" + phase + "/labelAndRepaint",
            ExceptionName(() => Handles.Label(Vector3.zero, tex)),
            ExceptionName(() => Handles.Label(Vector3.zero, new GUIContent("hi"))),
            ExceptionName(() => Handles.Label(Vector3.zero, "hi", GUIStyle.none)),
            ExceptionName(() => Handles.Label(Vector3.zero, new GUIContent("hi"), GUIStyle.none)),
            ExceptionName(() => HandleUtility.Repaint()));

        // 3. ReorderableList DoList / DoLayoutList
        RecordReorderableInGui(phase);

        if (evt == EventType.Repaint)
        {
            m_Done = true;
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
        }
    }

    /// <summary>The exception's name (or null) and the log lines (first line each) of a draw.</summary>
    static object[] Drawn(Action action)
    {
        var (error, logs) = CaptureLogs(action);
        return new object[] { error, logs.Select(l => l.Split('\n')[0]).ToArray() };
    }

    void RecordReorderableInGui(string phase)
    {
        var trace = new List<object>();
        var list = new ReorderableList(new List<int> { 10, 20, 30 }, typeof(int), true, true, true, true);
        list.index = 1;
        list.drawHeaderCallback = r => trace.Add(new object[] { "header", r.x, r.y, r.width, r.height });
        list.drawFooterCallback = r => trace.Add(new object[] { "footer", r.x, r.y, r.width, r.height });
        list.drawElementBackgroundCallback = (r, i, a, f) => trace.Add(new object[] { "bg", i, a, f, r.x, r.y, r.width, r.height });
        list.drawElementCallback = (r, i, a, f) => trace.Add(new object[] { "elem", i, a, f, r.x, r.y, r.width, r.height });
        list.drawNoneElementCallback = r => trace.Add(new object[] { "none", r.x, r.y, r.width, r.height });

        var err1 = Drawn(() => list.DoList(new Rect(10, 20, 200, list.GetHeight())));
        Put("reorderable/" + phase + "/three", err1, trace.ToArray());

        // Empty list
        trace.Clear();
        var empty = new ReorderableList(new List<int>(), typeof(int), true, true, true, true);
        empty.drawHeaderCallback = r => trace.Add(new object[] { "header", r.x, r.y, r.width, r.height });
        empty.drawFooterCallback = r => trace.Add(new object[] { "footer", r.x, r.y, r.width, r.height });
        empty.drawElementBackgroundCallback = (r, i, a, f) => trace.Add(new object[] { "bg", i, a, f, r.x, r.y, r.width, r.height });
        empty.drawElementCallback = (r, i, a, f) => trace.Add(new object[] { "elem", i, a, f, r.x, r.y, r.width, r.height });
        empty.drawNoneElementCallback = r => trace.Add(new object[] { "none", r.x, r.y, r.width, r.height });
        var errEmpty = Drawn(() => empty.DoList(new Rect(10, 20, 200, empty.GetHeight())));
        Put("reorderable/" + phase + "/empty", errEmpty, trace.ToArray());

        // Hidden header and buttons
        trace.Clear();
        var bare = new ReorderableList(new List<int> { 1, 2 }, typeof(int), false, false, false, false);
        bare.drawHeaderCallback = r => trace.Add(new object[] { "header", r.x, r.y, r.width, r.height });
        bare.drawFooterCallback = r => trace.Add(new object[] { "footer", r.x, r.y, r.width, r.height });
        bare.drawElementCallback = (r, i, a, f) => trace.Add(new object[] { "elem", i, a, f, r.x, r.y, r.width, r.height });
        var errBare = Drawn(() => bare.DoList(new Rect(10, 20, 200, bare.GetHeight())));
        Put("reorderable/" + phase + "/bare", errBare, trace.ToArray());

        // Visible rect culling
        trace.Clear();
        var culled = new ReorderableList(new List<int> { 0, 1, 2, 3, 4 }, typeof(int), true, true, true, true);
        culled.drawHeaderCallback = r => trace.Add(new object[] { "header", r.x, r.y, r.width, r.height });
        culled.drawFooterCallback = r => trace.Add(new object[] { "footer", r.x, r.y, r.width, r.height });
        culled.drawElementCallback = (r, i, a, f) => trace.Add(new object[] { "elem", i, r.y, r.height });
        var errCulled = Drawn(() => culled.DoList(new Rect(0, 0, 200, culled.GetHeight()), new Rect(0, 45, 200, 30)));
        Put("reorderable/" + phase + "/culled", errCulled, trace.ToArray());

        // DoLayoutList inside an Area
        trace.Clear();
        GUILayout.BeginArea(new Rect(5, 15, 220, 200));
        var layoutList = new ReorderableList(new List<int> { 1, 2 }, typeof(int), true, true, true, true);
        layoutList.drawHeaderCallback = r => trace.Add(new object[] { "header", r.x, r.y, r.width, r.height });
        layoutList.drawFooterCallback = r => trace.Add(new object[] { "footer", r.x, r.y, r.width, r.height });
        layoutList.drawElementCallback = (r, i, a, f) => trace.Add(new object[] { "elem", i, r.x, r.y, r.width, r.height });
        var errLayout = Drawn(() => layoutList.DoLayoutList());
        var lastRect = GUILayoutUtility.GetLastRect();
        GUILayout.EndArea();
        Put("reorderable/" + phase + "/layout", errLayout, RectArr(lastRect), trace.ToArray());
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
