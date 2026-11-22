using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class EditorLayoutProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done;
    GUIStyle m_MarginStyle;
    GUIStyle m_CustomScrollBg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_LAYOUT_OUT");
        if (string.IsNullOrEmpty(directory)) return;

        var go = new GameObject("EditorLayoutProbe");
        var probe = go.AddComponent<EditorLayoutProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.RecordOutsideGui(go);
    }

    void Put(string key, params object[] values)
    {
        m_Output.Append("{\"k\":").Append(Json(key))
            .Append(",\"v\":").Append(Json(values)).Append("}\n");
    }

    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    static object[] VecArr(Vector2 v) => new object[] { v.x, v.y };

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

    void RecordOutsideGui(GameObject go)
    {
        float ppp = EditorGUIUtility.pixelsPerPoint;
        var srcRect = new Rect(10f, 20f, 30f, 40f);
        var srcVec = new Vector2(12.5f, -7.25f);
        var pxRect = EditorGUIUtility.PointsToPixels(srcRect);
        var ptRect = EditorGUIUtility.PixelsToPoints(pxRect);
        var pxVec = EditorGUIUtility.PointsToPixels(srcVec);
        var ptVec = EditorGUIUtility.PixelsToPoints(pxVec);
        Put("util/pointsPixels/rect",
            pxRect.x / ppp, pxRect.y / ppp, pxRect.width / ppp, pxRect.height / ppp,
            ptRect.x, ptRect.y, ptRect.width, ptRect.height);
        Put("util/pointsPixels/vec",
            pxVec.x / ppp, pxVec.y / ppp,
            ptVec.x, ptVec.y);

        var pingNull = CaptureLogs(() => EditorGUIUtility.PingObject((UnityEngine.Object)null));
        var pingGo = CaptureLogs(() => EditorGUIUtility.PingObject(go));
        var pingId = CaptureLogs(() => EditorGUIUtility.PingObject(go.GetInstanceID()));
        var pingZero = CaptureLogs(() => EditorGUIUtility.PingObject(0));
        Put("util/pingObject",
            pingNull.error, pingNull.logs.Count,
            pingGo.error, pingGo.logs.Count,
            pingId.error, pingId.logs.Count,
            pingZero.error, pingZero.logs.Count);

        UnityEngine.Object loadedRel = null;
        var resRel = CaptureLogs(() => loadedRel = EditorGUIUtility.LoadRequired("ProbeText.txt"));
        Put("util/loadRequired/relative",
            resRel.error, resRel.logs, loadedRel != null, (loadedRel as TextAsset)?.text);

        UnityEngine.Object loadedFull = null;
        var resFull = CaptureLogs(() => loadedFull = EditorGUIUtility.LoadRequired("Assets/Editor Default Resources/ProbeText.txt"));
        Put("util/loadRequired/fullPath",
            resFull.error, resFull.logs, loadedFull != null, (loadedFull as TextAsset)?.text);

        UnityEngine.Object loadedMissing = null;
        var resMissing = CaptureLogs(() => loadedMissing = EditorGUIUtility.LoadRequired("NoSuchEditorLayoutResource.txt"));
        Put("util/loadRequired/missing",
            resMissing.error, resMissing.logs, loadedMissing == null);

        UnityEngine.Object loadedEmpty = null;
        var resEmpty = CaptureLogs(() => loadedEmpty = EditorGUIUtility.LoadRequired(""));
        Put("util/loadRequired/empty",
            resEmpty.error, resEmpty.logs, loadedEmpty == null);

        UnityEngine.Object loadedNull = null;
        var resNull = CaptureLogs(() => loadedNull = EditorGUIUtility.LoadRequired(null));
        Put("util/loadRequired/null",
            resNull.error, resNull.logs, loadedNull == null);

        Put("outsideGui/separator", ExceptionName(() => EditorGUILayout.Separator()));
        Put("outsideGui/space", ExceptionName(() => EditorGUILayout.Space()));
        Put("outsideGui/spaceExpandFalse", ExceptionName(() => EditorGUILayout.Space(10f, false)));
        Put("outsideGui/beginFadeGroup", ExceptionName(() => EditorGUILayout.BeginFadeGroup(1f)));
        var endFadeOut = CaptureLogs(() => EditorGUILayout.EndFadeGroup());
        Put("outsideGui/endFadeGroup", endFadeOut.error, endFadeOut.logs);
        Put("outsideGui/beginScrollView", ExceptionName(() => EditorGUILayout.BeginScrollView(Vector2.zero)));
        Put("outsideGui/beginScrollViewStyles", ExceptionName(() =>
            EditorGUILayout.BeginScrollView(Vector2.zero, false, false, GUIStyle.none, GUIStyle.none, GUIStyle.none)));
        Put("outsideGui/endScrollView", ExceptionName(() => EditorGUILayout.EndScrollView()));
    }

    void OnGUI()
    {
        if (m_Done) return;
        bool record = Event.current.type == EventType.Repaint;

        if (m_MarginStyle == null)
        {
            m_MarginStyle = new GUIStyle(GUIStyle.none) { margin = new RectOffset(0, 0, 4, 4) };
            m_CustomScrollBg = new GUIStyle(GUIStyle.none) { name = "ProbeCustomScroll" };
        }

        // 1. FadeGroup: value 0, 0.5, 1, FadeGroupScope, state save/restore, margin, contextWidth lock
        GUILayout.BeginArea(new Rect(0, 0, 420, 220));
        {
            var prevHierarchy = EditorGUIUtility.hierarchyMode;
            var prevLabelWidth = EditorGUIUtility.labelWidth;
            var prevEnabled = GUI.enabled;
            var prevColor = GUI.color;

            EditorGUIUtility.hierarchyMode = true;
            EditorGUIUtility.labelWidth = 0f;

            var beforeFade0 = GUILayoutUtility.GetRect(80, 10, m_MarginStyle);
            bool vis0 = EditorGUILayout.BeginFadeGroup(0f);
            var child0 = GUILayoutUtility.GetRect(80, 20, m_MarginStyle);
            EditorGUILayout.EndFadeGroup();
            var group0 = GUILayoutUtility.GetLastRect();
            var afterFade0 = GUILayoutUtility.GetRect(80, 10, m_MarginStyle);
            if (record)
            {
                Put("fade/zero/visible", vis0);
                Put("fade/zero/rects", RectArr(beforeFade0), RectArr(group0), RectArr(child0), RectArr(afterFade0));
            }

            GUI.enabled = true;
            GUI.color = new Color(0.25f, 0.5f, 0.75f, 1f);
            bool visHalf = EditorGUILayout.BeginFadeGroup(0.5f);
            GUI.enabled = false;
            GUI.color = Color.red;
            var childHalf = GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
            EditorGUILayout.EndFadeGroup();
            var groupHalf = GUILayoutUtility.GetLastRect();
            bool restoredEnabledHalf = GUI.enabled;
            Color restoredColorHalf = GUI.color;
            if (record)
            {
                Put("fade/half/visible", visHalf);
                Put("fade/half/rects", RectArr(groupHalf), RectArr(childHalf));
                Put("fade/half/restored", restoredEnabledHalf, restoredColorHalf.r, restoredColorHalf.g, restoredColorHalf.b, restoredColorHalf.a);
            }

            float lwBefore = EditorGUIUtility.labelWidth;
            bool visOne = EditorGUILayout.BeginFadeGroup(1f);
            GUI.BeginGroup(new Rect(0, 0, 120, 60));
            float lwLocked = EditorGUIUtility.labelWidth;
            GUI.EndGroup();
            var childOne = GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
            EditorGUILayout.EndFadeGroup();
            var groupOne = GUILayoutUtility.GetLastRect();
            GUI.BeginGroup(new Rect(0, 0, 120, 60));
            float lwUnlocked = EditorGUIUtility.labelWidth;
            GUI.EndGroup();
            if (record)
            {
                Put("fade/one/visible", visOne);
                Put("fade/one/rects", RectArr(groupOne), RectArr(childOne));
                Put("fade/contextWidthLock", lwBefore, lwLocked, lwUnlocked);
            }

            bool scopeVis0, scopeVisHalf, scopeVis1;
            Rect scopeRect0, scopeRectHalf, scopeRect1;
            using (var s0 = new EditorGUILayout.FadeGroupScope(0f))
            {
                scopeVis0 = s0.visible;
                GUILayoutUtility.GetRect(60, 20, GUIStyle.none);
            }
            scopeRect0 = GUILayoutUtility.GetLastRect();
            using (var sh = new EditorGUILayout.FadeGroupScope(0.5f))
            {
                scopeVisHalf = sh.visible;
                GUILayoutUtility.GetRect(60, 20, GUIStyle.none);
            }
            scopeRectHalf = GUILayoutUtility.GetLastRect();
            using (var s1 = new EditorGUILayout.FadeGroupScope(1f))
            {
                scopeVis1 = s1.visible;
                GUILayoutUtility.GetRect(60, 20, GUIStyle.none);
            }
            scopeRect1 = GUILayoutUtility.GetLastRect();
            if (record)
            {
                Put("fade/scope/visible", scopeVis0, scopeVisHalf, scopeVis1);
                Put("fade/scope/rects", RectArr(scopeRect0), RectArr(scopeRectHalf), RectArr(scopeRect1));
            }

            if (record)
            {
                var unmatched = CaptureLogs(() => EditorGUILayout.EndFadeGroup());
                Put("fade/unmatchedEnd", unmatched.error, unmatched.logs);
            }

            EditorGUIUtility.hierarchyMode = prevHierarchy;
            EditorGUIUtility.labelWidth = prevLabelWidth;
            GUI.enabled = prevEnabled;
            GUI.color = prevColor;
        }
        GUILayout.EndArea();

        // 2. ScrollView overloads & ScrollViewScope
        GUILayout.BeginArea(new Rect(0, 0, 420, 220));
        {
            var svOverPos = EditorGUILayout.BeginScrollView(new Vector2(15f, 25f), GUILayout.Width(100), GUILayout.Height(80));
            var svOverChild = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svOverRect = GUILayoutUtility.GetLastRect();

            var svSmallPos = EditorGUILayout.BeginScrollView(new Vector2(25f, 35f), false, false, GUILayout.Width(100), GUILayout.Height(80));
            var svSmallChild = GUILayoutUtility.GetRect(40, 30, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svSmallRect = GUILayoutUtility.GetLastRect();

            var svAlwaysPos = EditorGUILayout.BeginScrollView(new Vector2(25f, 35f), true, true, GUILayout.Width(100), GUILayout.Height(80));
            var svAlwaysChild = GUILayoutUtility.GetRect(40, 30, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svAlwaysRect = GUILayoutUtility.GetLastRect();

            var svBarsPos = EditorGUILayout.BeginScrollView(new Vector2(10f, 12f), GUIStyle.none, GUIStyle.none, GUILayout.Width(100), GUILayout.Height(80));
            var svBarsChild = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svBarsRect = GUILayoutUtility.GetLastRect();

            var svStylePos = EditorGUILayout.BeginScrollView(new Vector2(10f, 12f), m_CustomScrollBg, GUILayout.Width(100), GUILayout.Height(80));
            var svStyleChild = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svStyleRect = GUILayoutUtility.GetLastRect();

            var svFullPos = EditorGUILayout.BeginScrollView(new Vector2(10f, 12f), false, false,
                GUIStyle.none, GUIStyle.none, GUIStyle.none, GUILayout.Width(100), GUILayout.Height(80));
            var svFullChild = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
            EditorGUILayout.EndScrollView();
            var svFullRect = GUILayoutUtility.GetLastRect();

            bool scopeDefaultWheel;
            Vector2 scopePos;
            Rect scopeChild;
            using (var scope = new EditorGUILayout.ScrollViewScope(new Vector2(15f, 25f), GUILayout.Width(100), GUILayout.Height(80)))
            {
                scopeDefaultWheel = scope.handleScrollWheel;
                scope.handleScrollWheel = false;
                scopePos = scope.scrollPosition;
                scopeChild = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
            }
            var scopeRect = GUILayoutUtility.GetLastRect();

            if (record)
            {
                Put("scroll/overflow", VecArr(svOverPos), RectArr(svOverRect), RectArr(svOverChild));
                Put("scroll/smallClamp", VecArr(svSmallPos), RectArr(svSmallRect), RectArr(svSmallChild));
                Put("scroll/alwaysShow", VecArr(svAlwaysPos), RectArr(svAlwaysRect), RectArr(svAlwaysChild));
                Put("scroll/customBars", VecArr(svBarsPos), RectArr(svBarsRect), RectArr(svBarsChild));
                Put("scroll/styleFallback", VecArr(svStylePos), RectArr(svStyleRect), RectArr(svStyleChild));
                Put("scroll/fullNone", VecArr(svFullPos), RectArr(svFullRect), RectArr(svFullChild));
                Put("scroll/scope", scopeDefaultWheel, VecArr(scopePos), RectArr(scopeRect), RectArr(scopeChild));
            }
        }
        GUILayout.EndArea();

        // 2b. A scroll view with a maximum size in a larger area keeps the maximum: its
        // stretching content fills the view, and content wider than the view scrolls.
        GUILayout.BeginArea(new Rect(0, 0, 420, 220));
        {
            var stretch = new GUIStyle { stretchWidth = true, stretchHeight = true };
            EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.MaxWidth(200), GUILayout.MaxHeight(90));
            var maxChild = GUILayoutUtility.GetRect(40, 40, 20, 20, stretch);
            EditorGUILayout.EndScrollView();
            var maxRect = GUILayoutUtility.GetLastRect();

            EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.MaxWidth(150));
            var wideChild = GUILayoutUtility.GetRect(300, 300, 20, 20, stretch);
            EditorGUILayout.EndScrollView();
            var wideRect = GUILayoutUtility.GetLastRect();

            if (record)
            {
                Put("scroll/maxSize", RectArr(maxRect), RectArr(maxChild));
                Put("scroll/maxWidthWideContent", RectArr(wideRect), RectArr(wideChild));
            }
        }
        GUILayout.EndArea();

        // 2c. A background style whose name has scrollbar styles in the skin takes those:
        // "<name>VerticalScrollbar" and "<name>HorizontalScrollbar". Their sizes show in how far
        // the view scrolls (the content less the room the bars leave).
        GUILayout.BeginArea(new Rect(0, 0, 420, 220));
        {
            var previousSkin = GUI.skin;
            var skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.customStyles = new[]
            {
                new GUIStyle { name = "ProbeNamedVerticalScrollbar", fixedWidth = 31 },
                new GUIStyle { name = "ProbeNamedHorizontalScrollbar", fixedHeight = 17 },
                new GUIStyle { name = "ProbeHalfVerticalScrollbar", fixedWidth = 23 },
            };
            GUI.skin = skin;
            Vector2 namedPos, halfPos, plainPos;
            try
            {
                namedPos = EditorGUILayout.BeginScrollView(new Vector2(500f, 500f), new GUIStyle { name = "ProbeNamed" }, GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                EditorGUILayout.EndScrollView();
                // Only a vertical scrollbar style of that name: the horizontal one is the skin's.
                halfPos = EditorGUILayout.BeginScrollView(new Vector2(500f, 500f), new GUIStyle { name = "ProbeHalf" }, GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                EditorGUILayout.EndScrollView();
                plainPos = EditorGUILayout.BeginScrollView(new Vector2(500f, 500f), new GUIStyle { name = "ProbeNoSuch" }, GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                EditorGUILayout.EndScrollView();
            }
            finally
            {
                GUI.skin = previousSkin;
                UnityEngine.Object.DestroyImmediate(skin);
            }
            if (record) Put("scroll/barsByStyleName", VecArr(namedPos), VecArr(halfPos), VecArr(plainPos));
        }
        GUILayout.EndArea();

        // 3. Separator & Space in vertical and horizontal groups
        GUILayout.BeginArea(new Rect(0, 0, 420, 220));
        {
            GUILayout.BeginVertical(GUILayout.Width(160));
            EditorGUILayout.Separator();
            var vSep = GUILayoutUtility.GetLastRect();
            EditorGUILayout.Space();
            var vSpace = GUILayoutUtility.GetLastRect();
            EditorGUILayout.Space(12f);
            var vSpace12 = GUILayoutUtility.GetLastRect();
            EditorGUILayout.Space(12f, false);
            var vSpace12NoExp = GUILayoutUtility.GetLastRect();
            EditorGUILayout.Space(12f, true);
            var vSpace12Exp = GUILayoutUtility.GetLastRect();
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal(GUILayout.Width(160));
            EditorGUILayout.Separator();
            var hSep = GUILayoutUtility.GetLastRect();
            var hSepSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Width(160));
            EditorGUILayout.Space(12f, false);
            var hSpaceNoExp = GUILayoutUtility.GetLastRect();
            var hSpaceNoExpSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Width(160));
            EditorGUILayout.Space(12f, true);
            var hSpaceExp = GUILayoutUtility.GetLastRect();
            var hSpaceExpSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
            GUILayout.EndHorizontal();

            if (record)
            {
                Put("space/vertical", RectArr(vSep), RectArr(vSpace), RectArr(vSpace12), RectArr(vSpace12NoExp), RectArr(vSpace12Exp));
                Put("space/horizontal/separator", RectArr(hSep), RectArr(hSepSibling));
                Put("space/horizontal/noExpand", RectArr(hSpaceNoExp), RectArr(hSpaceNoExpSibling));
                Put("space/horizontal/expand", RectArr(hSpaceExp), RectArr(hSpaceExpSibling));
            }
        }
        GUILayout.EndArea();

        if (record)
        {
            m_Done = true;
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
        }
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
