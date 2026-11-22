using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

public sealed partial class ImguiSkinProbe
{
    const string CalcWrapText = "One two three four five six seven";
    static readonly int Q671HintA = "Q671_Probe_HintA".GetHashCode();
    static readonly int Q671HintB = "Q671_Probe_HintB".GetHashCode();
    static readonly int Q671HintC = "Q671_Probe_HintC".GetHashCode();
    static readonly int Q671HintD = "Q671_Probe_HintD".GetHashCode();
    static readonly int Q671HintUsed = "Q671_Probe_HintUsed".GetHashCode();

    Texture2D m_CalcTex5;
    Texture2D m_CalcTex20;
    Texture2D m_CalcTex30;
    Texture2D m_CalcTex40;
    Texture2D m_CalcTex40x1500;
    Texture2D m_CalcTex40x2000;
    GUIStyle m_CalcWrapStyle;
    GUIStyle m_CalcPlainStyle;
    int m_Q671IdA1;
    int m_Q671IdB1;
    int m_Q671IdC1;
    int m_Q671LabelId1;
    int m_Q671TextFieldId1;
    int m_Q671ToggleLeftId1;

    sealed class LogCapture : ILogHandler
    {
        public readonly List<string> Entries = new List<string>();
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            string msg = (args != null && args.Length > 0) ? string.Format(format, args) : format;
            Entries.Add(logType + ":" + msg);
        }
        public void LogException(Exception exception, UnityEngine.Object context) =>
            Entries.Add("Exception:" + exception.GetType().Name + ":" + exception.Message);
    }

    static string RectJson(Rect rect) => $"[{F(rect.x)},{F(rect.y)},{F(rect.width)},{F(rect.height)}]";

    void EnsureCalcAssets()
    {
        if (m_CalcTex5 != null) return;
        m_CalcTex5 = new Texture2D(5, 5, TextureFormat.RGBA32, false);
        m_CalcTex20 = new Texture2D(20, 20, TextureFormat.RGBA32, false);
        m_CalcTex30 = new Texture2D(30, 30, TextureFormat.RGBA32, false);
        m_CalcTex40 = new Texture2D(40, 40, TextureFormat.RGBA32, false);
        m_CalcTex40x1500 = new Texture2D(40, 1500, TextureFormat.RGBA32, false);
        m_CalcTex40x2000 = new Texture2D(40, 2000, TextureFormat.RGBA32, false);
        m_CalcPlainStyle = new GUIStyle(EditorStyles.label)
        {
            padding = new RectOffset(9, 3, 1, 0),
            margin = new RectOffset(7, 5, 9, 6),
            wordWrap = false,
            fixedHeight = 0
        };
        m_CalcWrapStyle = new GUIStyle(m_CalcPlainStyle) { wordWrap = true };
    }

    void RecordLayoutAndCalc(string phase)
    {
        EnsureCalcAssets();
        RecordNullTextLabels(phase);
        RecordUsedLayoutOutsideGroup(phase);
        RecordNarrowParentLayout(phase);
        RecordCalcHeightAndFallbacks(phase);
    }

    void RecordNullTextLabels(string phase)
    {
        string prefix = "nullTextLabel/" + phase + "/";
        void WriteRect(string name, Rect rect) => Put(prefix + name, RectJson(rect));

        string CaptureAction(Action action)
        {
            var capture = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            string outcome = "ok";
            try { action(); }
            catch (Exception ex) { outcome = ex.GetType().Name; }
            finally { Debug.unityLogger.logHandler = previous; }
            return "[" + Json(outcome) + ",[" + string.Join(",", capture.Entries.Select(Json)) + "]]";
        }

        int prevIndent = EditorGUI.indentLevel;
        float prevLabelWidth = EditorGUIUtility.labelWidth;
        try
        {
            EditorGUI.indentLevel = 1;
            EditorGUIUtility.labelWidth = 62;
            var pos = new Rect(10, 10, 140, 18);
            int id = GUIUtility.GetControlID("NullTextPrefixId".GetHashCode(), FocusType.Keyboard, pos);

            // EditorGUI.PrefixLabel: null-text GUIContent, null GUIContent, tooltip-only, GUIContent.none, normal
            Rect rNullText = Rect.zero;
            Put(prefix + "prefix/nullTextOutcome", CaptureAction(() =>
                rNullText = EditorGUI.PrefixLabel(pos, id, new GUIContent((string)null))));
            WriteRect("prefix/nullTextRect", rNullText);

            Rect rNullContent = Rect.zero;
            Put(prefix + "prefix/nullContentOutcome", CaptureAction(() =>
                rNullContent = EditorGUI.PrefixLabel(pos, id, (GUIContent)null)));
            WriteRect("prefix/nullContentRect", rNullContent);

            // Pending prefix (id == 0) with null GUIContent and null-text GUIContent
            Rect rPendingNullContent = Rect.zero;
            Put(prefix + "prefix/pendingNullContentOutcome", CaptureAction(() =>
                rPendingNullContent = EditorGUI.PrefixLabel(pos, (GUIContent)null)));
            WriteRect("prefix/pendingNullContentRect", rPendingNullContent);

            Rect rPendingNullText = Rect.zero;
            Put(prefix + "prefix/pendingNullTextOutcome", CaptureAction(() =>
            {
                rPendingNullText = EditorGUI.PrefixLabel(pos, new GUIContent((string)null));
                GUIUtility.GetControlID("NullTextPendingConsumer".GetHashCode(), FocusType.Keyboard, pos);
            }));
            WriteRect("prefix/pendingNullTextRect", rPendingNullText);

            WriteRect("prefix/tooltipOnlyRect", EditorGUI.PrefixLabel(pos, id, new GUIContent("", "Tip")));
            WriteRect("prefix/noneRect", EditorGUI.PrefixLabel(pos, id, GUIContent.none));
            WriteRect("prefix/normalRect", EditorGUI.PrefixLabel(pos, id, new GUIContent("Name")));

            // EditorGUI.HandlePrefixLabel with null-text and null GUIContent
            Put(prefix + "handlePrefix/nullTextOutcome", CaptureAction(() =>
                EditorGUI.HandlePrefixLabel(pos, new Rect(10, 10, 60, 18), new GUIContent((string)null), id)));
            Put(prefix + "handlePrefix/nullContentOutcome", CaptureAction(() =>
                EditorGUI.HandlePrefixLabel(pos, new Rect(10, 10, 60, 18), (GUIContent)null, id)));

            // EditorGUI.LabelField with null strings / null-text GUIContent
            Put(prefix + "guiLabel/nullSingle", CaptureAction(() =>
                EditorGUI.LabelField(pos, (string)null)));
            Put(prefix + "guiLabel/nullPrefixString", CaptureAction(() =>
                EditorGUI.LabelField(pos, (string)null, "Value")));
            Put(prefix + "guiLabel/nullValueString", CaptureAction(() =>
                EditorGUI.LabelField(pos, "Name", (string)null)));
            Put(prefix + "guiLabel/nullTextContent", CaptureAction(() =>
                EditorGUI.LabelField(pos, new GUIContent((string)null), new GUIContent("Value"))));

            // EditorGUILayout.PrefixLabel & EditorGUILayout.LabelField with null text / null GUIContent
            GUILayout.BeginArea(new Rect(0, 0, 150, 240));
            try
            {
                Put(prefix + "layoutPrefix/nullStringOutcome", CaptureAction(() =>
                    EditorGUILayout.PrefixLabel((string)null)));
                WriteRect("layoutPrefix/nullStringRect", GUILayoutUtility.GetLastRect());

                EditorGUILayout.PrefixLabel("");
                WriteRect("layoutPrefix/emptyStringRect", GUILayoutUtility.GetLastRect());

                Put(prefix + "layoutPrefix/nullTextContentOutcome", CaptureAction(() =>
                    EditorGUILayout.PrefixLabel(new GUIContent((string)null))));
                WriteRect("layoutPrefix/nullTextContentRect", GUILayoutUtility.GetLastRect());

                EditorGUILayout.PrefixLabel(GUIContent.none);
                WriteRect("layoutPrefix/noneRect", GUILayoutUtility.GetLastRect());

                EditorGUILayout.PrefixLabel("Name");
                WriteRect("layoutPrefix/normalRect", GUILayoutUtility.GetLastRect());

                Put(prefix + "layoutLabel/nullSingleOutcome", CaptureAction(() =>
                    EditorGUILayout.LabelField((string)null)));
                WriteRect("layoutLabel/nullSingleRect", GUILayoutUtility.GetLastRect());

                Put(prefix + "layoutLabel/nullPrefixWrapOutcome", CaptureAction(() =>
                    EditorGUILayout.LabelField((string)null, CalcWrapText, m_CalcWrapStyle, GUILayout.Width(83))));
                WriteRect("layoutLabel/nullPrefixWrapRect", GUILayoutUtility.GetLastRect());

                EditorGUILayout.LabelField("", CalcWrapText, m_CalcWrapStyle, GUILayout.Width(83));
                WriteRect("layoutLabel/emptyPrefixWrapRect", GUILayoutUtility.GetLastRect());

                Put(prefix + "layoutLabel/nullTextContentWrapOutcome", CaptureAction(() =>
                    EditorGUILayout.LabelField(new GUIContent((string)null), new GUIContent(CalcWrapText), m_CalcWrapStyle, GUILayout.Width(83))));
                WriteRect("layoutLabel/nullTextContentWrapRect", GUILayoutUtility.GetLastRect());

                Put(prefix + "layoutLabel/nullValueWrapOutcome", CaptureAction(() =>
                    EditorGUILayout.LabelField("Name", (string)null, m_CalcWrapStyle, GUILayout.Width(83))));
                WriteRect("layoutLabel/nullValueWrapRect", GUILayoutUtility.GetLastRect());

                WriteRect("layoutLabel/afterAll", GUILayoutUtility.GetRect(12, 6));
            }
            finally { GUILayout.EndArea(); }
        }
        finally
        {
            EditorGUI.indentLevel = prevIndent;
            EditorGUIUtility.labelWidth = prevLabelWidth;
        }
    }

    void RecordUsedLayoutOutsideGroup(string phase)
    {
        string prefix = "usedOutsideGroup/" + phase + "/";
        void Write(string name, Rect rect) => Put(prefix + name, RectJson(rect));
        var wrapContent = new GUIContent(CalcWrapText);
        var plainContent = new GUIContent("Plain");

        // 1) Directly inside BeginArea (no nested horizontal/vertical group).
        GUILayout.BeginArea(new Rect(14, 18, 160, 120));
        try
        {
            Write("area/before", GUILayoutUtility.GetRect(18, 7, m_CalcPlainStyle));
            if (phase == "Repaint")
            {
                var current = Event.current;
                try
                {
                    current.type = EventType.MouseDown;
                    current.Use();
                    Write("area/usedContentWrap", GUILayoutUtility.GetRect(wrapContent, m_CalcWrapStyle));
                    Write("area/usedContentPlain", GUILayoutUtility.GetRect(plainContent, m_CalcPlainStyle));
                    Write("area/usedAspect", GUILayoutUtility.GetAspectRect(2f));
                    Write("area/usedLastRect", GUILayoutUtility.GetLastRect());
                    EditorGUILayout.LabelField("UsedSingle");
                    Write("area/usedLabelSingleLast", GUILayoutUtility.GetLastRect());
                    EditorGUILayout.LabelField("UsedPrefix", "UsedValue");
                    Write("area/usedLabelPairLast", GUILayoutUtility.GetLastRect());
                    EditorGUILayout.LabelField(new GUIContent("UsedPrefix"), wrapContent, m_CalcWrapStyle);
                    Write("area/usedLabelWrapLast", GUILayoutUtility.GetLastRect());
                    EditorGUILayout.PrefixLabel("UsedPrefixOnly");
                    Write("area/usedPrefixLast", GUILayoutUtility.GetLastRect());
                }
                finally { current.type = EventType.Repaint; }
            }
            Write("area/next", GUILayoutUtility.GetRect(21, 5, m_CalcPlainStyle));
            if (phase == "Layout")
                GUILayoutUtility.GetRect(39, 13, m_CalcPlainStyle);
        }
        finally { GUILayout.EndArea(); }

        // 2) BeginArea called while Event.current.type == EventType.Used (before an arranged BeginArea).
        if (phase == "Repaint")
        {
            var current = Event.current;
            try
            {
                current.type = EventType.MouseDown;
                current.Use();
                GUILayout.BeginArea(new Rect(30, 30, 90, 60));
                try
                {
                    Write("usedArea/insideNumeric", GUILayoutUtility.GetRect(50, 12));
                    Write("usedArea/insideLast", GUILayoutUtility.GetLastRect());
                }
                finally { GUILayout.EndArea(); }
            }
            finally { current.type = EventType.Repaint; }
        }
        GUILayout.BeginArea(new Rect(18, 22, 130, 70));
        try
        {
            Write("usedArea/afterFirst", GUILayoutUtility.GetRect(25, 9, m_CalcPlainStyle));
        }
        finally { GUILayout.EndArea(); }

        // 3) At the root OnGUI layout level (outside BeginArea and any explicit group).
        Write("root/before", GUILayoutUtility.GetRect(16, 6, m_CalcPlainStyle));
        if (phase == "Repaint")
        {
            var current = Event.current;
            try
            {
                current.type = EventType.MouseDown;
                current.Use();
                Write("root/usedNumeric", GUILayoutUtility.GetRect(71, 15));
                Write("root/usedContentWrap", GUILayoutUtility.GetRect(wrapContent, m_CalcWrapStyle));
                Write("root/usedAspect", GUILayoutUtility.GetAspectRect(2f));
                Write("root/usedLastRect", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField("RootUsed");
                Write("root/usedLabelLast", GUILayoutUtility.GetLastRect());
            }
            finally { current.type = EventType.Repaint; }
        }
        Write("root/next", GUILayoutUtility.GetRect(19, 8, m_CalcPlainStyle));
        if (phase == "Layout")
            GUILayoutUtility.GetRect(41, 14, m_CalcPlainStyle);
    }

    void RecordNarrowParentLayout(string phase)
    {
        string prefix = "narrowParent/" + phase + "/";
        void Write(string name, Rect rect) => Put(prefix + name, RectJson(rect));

        var stretchX = new GUIStyle { stretchWidth = true, stretchHeight = false, margin = new RectOffset(4, 2, 1, 1) };
        var fixedX = new GUIStyle { stretchWidth = false, stretchHeight = false, margin = new RectOffset(4, 2, 1, 1) };
        var stretchY = new GUIStyle { stretchWidth = false, stretchHeight = true, margin = new RectOffset(1, 1, 3, 2) };
        var fixedY = new GUIStyle { stretchWidth = false, stretchHeight = false, margin = new RectOffset(1, 1, 3, 2) };

        void VerticalParentCase(string name, float width)
        {
            GUILayout.BeginArea(new Rect(0, 0, width, 160));
            try
            {
                Write(name + "/stretchEntry", GUILayoutUtility.GetRect(130, 180, 10, 10, stretchX));
                Write(name + "/fixedEntry", GUILayoutUtility.GetRect(130, 180, 10, 10, fixedX));
                Write(name + "/maxWidthEntry", GUILayoutUtility.GetRect(130, 180, 10, 10, stretchX, GUILayout.MaxWidth(150)));

                GUILayout.BeginHorizontal();
                try
                {
                    Write(name + "/rowStretch/a", GUILayoutUtility.GetRect(55, 14, m_CalcWrapStyle, GUILayout.ExpandWidth(false)));
                    Write(name + "/rowStretch/b", GUILayoutUtility.GetRect(new GUIContent("Name"), m_CalcWrapStyle, GUILayout.Width(83), GUILayout.Height(14)));
                }
                finally { GUILayout.EndHorizontal(); }
                Write(name + "/rowStretch/group", GUILayoutUtility.GetLastRect());

                GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
                try
                {
                    Write(name + "/rowNoExpand/a", GUILayoutUtility.GetRect(55, 14, m_CalcWrapStyle, GUILayout.ExpandWidth(false)));
                    Write(name + "/rowNoExpand/b", GUILayoutUtility.GetRect(new GUIContent("Name"), m_CalcWrapStyle, GUILayout.Width(83), GUILayout.Height(14)));
                }
                finally { GUILayout.EndHorizontal(); }
                Write(name + "/rowNoExpand/group", GUILayoutUtility.GetLastRect());

                GUILayout.BeginHorizontal(GUILayout.MaxWidth(120));
                try
                {
                    Write(name + "/rowMaxW/a", GUILayoutUtility.GetRect(55, 14, m_CalcWrapStyle, GUILayout.ExpandWidth(false)));
                    Write(name + "/rowMaxW/b", GUILayoutUtility.GetRect(new GUIContent("Name"), m_CalcWrapStyle, GUILayout.Width(83), GUILayout.Height(14)));
                }
                finally { GUILayout.EndHorizontal(); }
                Write(name + "/rowMaxW/group", GUILayoutUtility.GetLastRect());
            }
            finally { GUILayout.EndArea(); }
        }

        VerticalParentCase("vNarrow100", 100);
        VerticalParentCase("vWide200", 200);

        void HorizontalParentCase(string name, float height)
        {
            GUILayout.BeginArea(new Rect(0, 0, 260, height));
            try
            {
                GUILayout.BeginHorizontal();
                try
                {
                    Write(name + "/stretchY", GUILayoutUtility.GetRect(15, 15, 45, 65, stretchY));
                    Write(name + "/fixedY", GUILayoutUtility.GetRect(15, 15, 45, 65, fixedY));
                    Write(name + "/maxHeightY", GUILayoutUtility.GetRect(15, 15, 45, 65, stretchY, GUILayout.MaxHeight(55)));

                    GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                    try
                    {
                        Write(name + "/colStretch/a", GUILayoutUtility.GetRect(20, 24, fixedY));
                        Write(name + "/colStretch/b", GUILayoutUtility.GetRect(20, 24, fixedY));
                    }
                    finally { GUILayout.EndVertical(); }
                    Write(name + "/colStretch/group", GUILayoutUtility.GetLastRect());

                    GUILayout.BeginVertical(GUILayout.ExpandHeight(false));
                    try
                    {
                        Write(name + "/colNoExpand/a", GUILayoutUtility.GetRect(20, 24, fixedY));
                        Write(name + "/colNoExpand/b", GUILayoutUtility.GetRect(20, 24, fixedY));
                    }
                    finally { GUILayout.EndVertical(); }
                    Write(name + "/colNoExpand/group", GUILayoutUtility.GetLastRect());

                    GUILayout.BeginVertical(GUILayout.MaxHeight(40));
                    try
                    {
                        Write(name + "/colMaxH/a", GUILayoutUtility.GetRect(20, 24, fixedY));
                        Write(name + "/colMaxH/b", GUILayoutUtility.GetRect(20, 24, fixedY));
                    }
                    finally { GUILayout.EndVertical(); }
                    Write(name + "/colMaxH/group", GUILayoutUtility.GetLastRect());
                }
                finally { GUILayout.EndHorizontal(); }
            }
            finally { GUILayout.EndArea(); }
        }

        HorizontalParentCase("hNarrow30", 30);
        HorizontalParentCase("hTall80", 80);

        // Directly height-constrained horizontal group so SetVertical runs with available < minHeight (30 < 45).
        void ConstrainedHorizontalCase(string name, float height)
        {
            var constrainedStyle = new GUIStyle { fixedHeight = height };
            GUILayout.BeginArea(new Rect(0, 0, 260, 120));
            try
            {
                GUILayout.BeginHorizontal(constrainedStyle);
                try
                {
                    Write(name + "/stretchY", GUILayoutUtility.GetRect(15, 15, 45, 65, stretchY));
                    Write(name + "/fixedY", GUILayoutUtility.GetRect(15, 15, 45, 65, fixedY));
                    Write(name + "/maxHeightY", GUILayoutUtility.GetRect(15, 15, 45, 65, stretchY, GUILayout.MaxHeight(55)));
                }
                finally { GUILayout.EndHorizontal(); }
                Write(name + "/group", GUILayoutUtility.GetLastRect());
            }
            finally { GUILayout.EndArea(); }
        }

        ConstrainedHorizontalCase("hConstrained30", 30);
        ConstrainedHorizontalCase("hConstrained80", 80);

        // MinWidth on a group raises an unset maxWidth to minWidth, clearing stretchWidth.
        GUILayout.BeginArea(new Rect(0, 0, 200, 80));
        try
        {
            GUILayout.BeginHorizontal(GUILayout.MinWidth(50));
            try
            {
                Write("minWidthGroup/child", GUILayoutUtility.GetRect(30, 120, 12, 12, stretchX));
            }
            finally { GUILayout.EndHorizontal(); }
            Write("minWidthGroup/group", GUILayoutUtility.GetLastRect());
        }
        finally { GUILayout.EndArea(); }

        // Scroll views with MaxWidth / MaxHeight options must honor their maximum instead of stretching.
        GUILayout.BeginArea(new Rect(0, 0, 400, 100));
        try
        {
            GUILayout.BeginScrollView(Vector2.zero, GUILayout.MaxWidth(200));
            try
            {
                Write("scrollMaxWidth/label", GUILayoutUtility.GetRect(new GUIContent("Short"), m_CalcPlainStyle));
                Write("scrollMaxWidth/skinLabel", GUILayoutUtility.GetRect(new GUIContent("Short"), GUI.skin.label));
                Write("scrollMaxWidth/stretch", GUILayoutUtility.GetRect(40, 40, 20, 20, stretchX));
                GUILayout.BeginHorizontal();
                Write("scrollMaxWidth/rowLeft", GUILayoutUtility.GetRect(20, 20, 20, 20, stretchX));
                Write("scrollMaxWidth/rowRight", GUILayoutUtility.GetRect(20, 20, 20, 20, stretchX));
                GUILayout.EndHorizontal();
            }
            finally { GUILayout.EndScrollView(); }
            Write("scrollMaxWidth/group", GUILayoutUtility.GetLastRect());
        }
        finally { GUILayout.EndArea(); }

        GUILayout.BeginArea(new Rect(0, 0, 200, 400));
        try
        {
            GUILayout.BeginScrollView(Vector2.zero, GUILayout.MaxHeight(120));
            try
            {
                Write("scrollMaxHeight/entry", GUILayoutUtility.GetRect(40, 40, 20, 20, stretchY));
            }
            finally { GUILayout.EndScrollView(); }
            Write("scrollMaxHeight/group", GUILayoutUtility.GetLastRect());
        }
        finally { GUILayout.EndArea(); }

        GUILayout.BeginArea(new Rect(0, 0, 400, 100));
        try
        {
            GUILayout.BeginScrollView(Vector2.zero, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
            try
            {
                Write("scrollMinMaxWidth/entry", GUILayoutUtility.GetRect(300, 300, 20, 20, stretchX));
            }
            finally { GUILayout.EndScrollView(); }
            Write("scrollMinMaxWidth/group", GUILayoutUtility.GetLastRect());
        }
        finally { GUILayout.EndArea(); }
    }

    void RecordCalcHeightAndFallbacks(string phase)
    {
        // Fractional rounding & accumulation check runs in both Layout and Repaint.
        var wrapContent = new GUIContent(CalcWrapText);
        GUILayout.BeginArea(new Rect(0, 0, 140, 240));
        try
        {
            for (int i = 0; i < 4; i++)
                Put($"calcHeight/stack/{phase}/{i}", RectJson(GUILayoutUtility.GetRect(wrapContent, m_CalcWrapStyle, GUILayout.Width(83))));
            Put($"calcHeight/stack/{phase}/after", RectJson(GUILayoutUtility.GetRect(12, 6)));
        }
        finally { GUILayout.EndArea(); }

        // A maximum size option bounds a non-wrapping content's own size: its image gives way.
        var imageLeft = new GUIStyle(m_CalcPlainStyle) { imagePosition = ImagePosition.ImageLeft, stretchWidth = false };
        var imageAbove = new GUIStyle(m_CalcPlainStyle) { imagePosition = ImagePosition.ImageAbove, stretchWidth = false };
        var pictured = new GUIContent("Hi there", m_CalcTex40);
        GUILayout.BeginArea(new Rect(0, 0, 300, 600));
        try
        {
            void Bound(string name, GUIStyle style, params GUILayoutOption[] options) =>
                Put($"calcText/constraint/{phase}/{name}", RectJson(GUILayoutUtility.GetRect(pictured, style, options)));
            Bound("left/free", imageLeft);
            Bound("left/maxWidth70", imageLeft, GUILayout.MaxWidth(70));
            Bound("left/maxHeight20", imageLeft, GUILayout.MaxHeight(20));
            Bound("left/maxWidth70Height20", imageLeft, GUILayout.MaxWidth(70), GUILayout.MaxHeight(20));
            Bound("left/maxWidth90Height10", imageLeft, GUILayout.MaxWidth(90), GUILayout.MaxHeight(10));
            Bound("above/free", imageAbove);
            Bound("above/maxWidth30", imageAbove, GUILayout.MaxWidth(30));
            Bound("above/maxHeight30", imageAbove, GUILayout.MaxHeight(30));
            Bound("above/maxWidth30Height30", imageAbove, GUILayout.MaxWidth(30), GUILayout.MaxHeight(30));
            Bound("above/maxWidth20Height45", imageAbove, GUILayout.MaxWidth(20), GUILayout.MaxHeight(45));
        }
        finally { GUILayout.EndArea(); }

        if (phase != "Repaint") return;

        // OS fallbacks and LegacyRuntime TextCore FontAsset face info on Linux.
        var getOsFallbacks = typeof(Font).GetMethod("GetOSFallbacks", BindingFlags.NonPublic | BindingFlags.Static);
        var osFallbacks = (string[])getOsFallbacks.Invoke(null, null);
        Put("calcHeight/osFallbacks", "[" + string.Join(",", osFallbacks.Select(Json)) + "]");
        var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var legacyAsset = legacyFont != null ? FontAsset.CreateFontAsset(legacyFont) : null;
        if (legacyAsset != null)
        {
            var fi = legacyAsset.faceInfo;
            Put("calcHeight/legacyAssetFace",
                $"[{Json(fi.familyName)},{fi.pointSize},{F(fi.ascentLine)},{F(fi.descentLine)},{F(fi.lineHeight)}]");
        }
        else
        {
            Put("calcHeight/legacyAssetFace", "null");
        }

        // CalcHeight matrix.
        void CH(string name, GUIStyle style, GUIContent content, float width) =>
            Put("calcHeight/value/" + name, F(style.CalcHeight(content, width)));

        foreach (float w in new[] { 83f, 123f, 40f, 300f, 0f, -5f })
            CH("wrap/w" + w.ToString(Inv), m_CalcWrapStyle, wrapContent, w);
        CH("plain/w83", m_CalcPlainStyle, wrapContent, 83f);
        CH("plain/w300", m_CalcPlainStyle, wrapContent, 300f);
        CH("empty", m_CalcWrapStyle, new GUIContent(""), 83f);
        CH("nullText", m_CalcWrapStyle, new GUIContent((string)null), 83f);
        CH("spaces", m_CalcWrapStyle, new GUIContent("   "), 83f);
        CH("multiline", m_CalcWrapStyle, new GUIContent("Line1\nLine2\nLine3"), 200f);

        var fixedHStyle = new GUIStyle(m_CalcWrapStyle) { fixedHeight = 34 };
        CH("fixedHeight", fixedHStyle, wrapContent, 83f);

        var prevIconSize = EditorGUIUtility.GetIconSize();
        try
        {
            var c5 = new GUIContent(CalcWrapText, m_CalcTex5);
            var c20 = new GUIContent(CalcWrapText, m_CalcTex20);
            var cImgOnly = new GUIContent(m_CalcTex20);

            foreach (var pos in new[] { ImagePosition.ImageLeft, ImagePosition.ImageAbove, ImagePosition.ImageOnly, ImagePosition.TextOnly })
            {
                var st = new GUIStyle(m_CalcWrapStyle) { imagePosition = pos };
                EditorGUIUtility.SetIconSize(Vector2.zero);
                CH($"img/{pos}/t5", st, c5, 83f);
                CH($"img/{pos}/t20", st, c20, 83f);
                CH($"img/{pos}/imgOnly20", st, cImgOnly, 83f);

                EditorGUIUtility.SetIconSize(new Vector2(16, 16));
                CH($"img/{pos}/icon16_t20", st, c20, 83f);

                EditorGUIUtility.SetIconSize(new Vector2(0, 24));
                CH($"img/{pos}/icon0x24_t5", st, c5, 83f);
            }

            // Discriminating ImageLeft controls: iconSize.x vs image.width vs fixed subtraction, plus narrow/tall scaling.
            var leftStyle = new GUIStyle(m_CalcWrapStyle) { imagePosition = ImagePosition.ImageLeft };
            var c30 = new GUIContent(CalcWrapText, m_CalcTex30);
            var c40 = new GUIContent(CalcWrapText, m_CalcTex40);
            var longWrap = string.Join(" ", Enumerable.Repeat(CalcWrapText, 40));
            var cLongNoImg = new GUIContent(longWrap);
            var cTall1500Short = new GUIContent("Short", m_CalcTex40x1500);
            var cTall2000Long = new GUIContent(longWrap, m_CalcTex40x2000);

            foreach (float w in new[] { 95f, 110f, 125f })
            {
                EditorGUIUtility.SetIconSize(Vector2.zero);
                CH($"imgDiscrim/w{w.ToString(Inv)}/icon0_t30", leftStyle, c30, w);
                EditorGUIUtility.SetIconSize(new Vector2(10, 10));
                CH($"imgDiscrim/w{w.ToString(Inv)}/icon10_t30", leftStyle, c30, w);
                EditorGUIUtility.SetIconSize(new Vector2(50, 10));
                CH($"imgDiscrim/w{w.ToString(Inv)}/icon50_t30", leftStyle, c30, w);
            }

            EditorGUIUtility.SetIconSize(Vector2.zero);
            CH("imgDiscrim/narrow35_t40", leftStyle, c40, 35f);
            CH("imgDiscrim/tall1500_short_w100", leftStyle, cTall1500Short, 100f);
            CH("imgDiscrim/tall2000_long_w110", leftStyle, cTall2000Long, 110f);
            CH("imgDiscrim/longNoImg_w83", m_CalcWrapStyle, cLongNoImg, 83f);

            // Font-size fallback and lineHeight controls.
            var defaultSize0 = new GUIStyle { fontSize = 0, wordWrap = true };
            var editorSize0 = new GUIStyle(EditorStyles.label) { fontSize = 0, wordWrap = true };
            var editorSize16 = new GUIStyle(EditorStyles.label) { fontSize = 16, wordWrap = true };
            var editorSize24 = new GUIStyle(EditorStyles.label) { fontSize = 24, wordWrap = false };
            CH("fontSize/default0_w83", defaultSize0, wrapContent, 83f);
            CH("fontSize/editor0_w83", editorSize0, wrapContent, 83f);
            CH("fontSize/editor16_w83", editorSize16, wrapContent, 83f);
            Put("calcText/lineHeight/default0", F(defaultSize0.lineHeight));
            Put("calcText/lineHeight/editor0", F(editorSize0.lineHeight));
            Put("calcText/lineHeight/editor16", F(editorSize16.lineHeight));
            Put("calcText/lineHeight/editor24", F(editorSize24.lineHeight));

            // Unified TextCore CalcSize, CalcMinMaxWidth, and cursor placement controls.
            void CS(string name, GUIStyle style, GUIContent content)
            {
                var s = style.CalcSize(content);
                Put("calcText/size/" + name, $"[{F(s.x)},{F(s.y)}]");
            }
            void CMM(string name, GUIStyle style, GUIContent content)
            {
                style.CalcMinMaxWidth(content, out float minW, out float maxW);
                Put("calcText/minMax/" + name, $"[{F(minW)},{F(maxW)}]");
            }

            EditorGUIUtility.SetIconSize(Vector2.zero);
            CS("plain/hello", m_CalcPlainStyle, new GUIContent("Hello"));
            CS("plain/wrapText", m_CalcPlainStyle, wrapContent);
            CS("wrap/wrapText", m_CalcWrapStyle, wrapContent);
            CS("plain/empty", m_CalcPlainStyle, new GUIContent(""));
            CS("plain/nullText", m_CalcPlainStyle, new GUIContent((string)null));
            CS("plain/multiline", m_CalcPlainStyle, new GUIContent("Line1\nLine2"));
            CS("plain/fixedW50", new GUIStyle(m_CalcPlainStyle) { fixedWidth = 50 }, new GUIContent("Hello"));
            CS("plain/fixedH30", new GUIStyle(m_CalcPlainStyle) { fixedHeight = 30 }, new GUIContent("Hello"));
            CS("imgLeft/t20", leftStyle, c20);
            EditorGUIUtility.SetIconSize(new Vector2(16, 16));
            CS("imgLeft/icon16_t20", leftStyle, c20);
            EditorGUIUtility.SetIconSize(Vector2.zero);
            CS("imgAbove/t20", new GUIStyle(m_CalcWrapStyle) { imagePosition = ImagePosition.ImageAbove }, c20);
            CS("imgOnly/t20", new GUIStyle(m_CalcWrapStyle) { imagePosition = ImagePosition.ImageOnly }, c20);
            CS("textOnly/t20", new GUIStyle(m_CalcWrapStyle) { imagePosition = ImagePosition.TextOnly }, c20);

            CMM("plain/wrapText", m_CalcPlainStyle, wrapContent);
            CMM("wrap/wrapText", m_CalcWrapStyle, wrapContent);
            CMM("wrap/longWord", m_CalcWrapStyle, new GUIContent("Short Supercalifragilistic"));
            CMM("wrap/empty", m_CalcWrapStyle, new GUIContent(""));
            CMM("wrap/imgLeft_t20", leftStyle, c20);
            CMM("wrap/fixedW50", new GUIStyle(m_CalcWrapStyle) { fixedWidth = 50 }, wrapContent);

            // Widths of single characters and of pairs tell glyph advances from pair adjustments.
            var bare = new GUIStyle(m_CalcPlainStyle) { padding = new RectOffset() };
            foreach (var t in new[] { "H", "e", "l", "o", " ", "He", "el", "ll", "lo", "ee", "oo", "Te", "AV", "n ", " t", "One", "two", "seven", "Hello", "Hello World" })
                Put("calcText/width/" + t.Replace(' ', '_'), F(bare.CalcSize(new GUIContent(t)).x));
            var bare13 = new GUIStyle(bare) { fontSize = 13 };
            var bare24 = new GUIStyle(bare) { fontSize = 24 };
            var bareBold = new GUIStyle(bare) { fontStyle = FontStyle.Bold };
            CS("width13/hello", bare13, new GUIContent("Hello World"));
            CS("width24/hello", bare24, new GUIContent("Hello World"));
            CS("widthBold/hello", bareBold, new GUIContent("Hello World"));

            // Cursor positions of a drawn single line. GUIStyle.GetCursorPixelPosition gave no
            // number in this capture (its row keeps that visible: the text's offset in the position,
            // also recorded, is what is not a number), so the text handle's own cursor positions
            // are recorded, with the index it finds at each.
            const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
            var fieldStyle = GUI.skin.textField;
            var fieldContent = new GUIContent("Hello World");
            var fieldRect = new Rect(30, 40, 150, 20);
            fieldStyle.Draw(fieldRect, fieldContent, false, false, false, false);
            var publicCursor = fieldStyle.GetCursorPixelPosition(fieldRect, fieldContent, 5);
            Put("calcText/cursor/public", $"[{F(publicCursor.x)},{F(publicCursor.y)}]");
            var handleType = typeof(GUIStyle).Assembly.GetType("UnityEngine.IMGUITextHandle");
            var getHandle = handleType.GetMethod("GetTextHandle", Hidden, null, new[] { typeof(GUIStyle), typeof(Rect), typeof(string), typeof(Color32) }, null);
            var fieldText = (string)typeof(GUIContent).GetProperty("textWithWhitespace", Hidden).GetValue(fieldContent);
            var handle = getHandle.Invoke(null, new object[] { fieldStyle, fieldStyle.padding.Remove(fieldRect), fieldText, (Color32)Color.white });
            var preferred = (Vector2)handle.GetType().GetProperty("preferredSize", Hidden).GetValue(handle);
            var offset = (Vector2)typeof(GUIStyle).GetMethod("Internal_GetTextRectOffset", Hidden).Invoke(fieldStyle, new object[] { fieldRect, fieldContent, preferred });
            Put("calcText/cursor/text", $"[{F(preferred.x)},{F(preferred.y)},{F(offset.x)},{F(offset.y)},{F(fieldStyle.lineHeight)}]");
            var cursorAt = handle.GetType().GetMethod("GetCursorPositionFromStringIndexUsingLineHeight", Hidden);
            var indexAt = handle.GetType().GetMethod("GetCursorIndexFromPosition", Hidden);
            for (int idx = 0; idx <= fieldContent.text.Length; idx++)
            {
                var p = (Vector2)cursorAt.Invoke(handle, new object[] { idx, false, true });
                int found = (int)indexAt.Invoke(handle, new object[] { p, true });
                Put($"calcText/cursor/handle/{idx}", $"[{F(p.x)},{F(p.y)},{found.ToString(Inv)}]");
            }

            // Characters whose glyphs take ligature and mark records where a font asset reads them.
            foreach (var t in new[] { "fi", "ffl", "e\u0301", "a\u0308\u0301" })
                Put("calcText/width/features/" + string.Join("-", t.Select(c => ((int)c).ToString("X4", Inv))), F(bare.CalcSize(new GUIContent(t)).x));

            // Fonts other than the default: a project font, and fonts no text can be generated
            // with (one without a face, one naming a family that is not installed). Sizes, and
            // what measuring and drawing them logs.
            void FontCase(string name, Font font)
            {
                var capture = new LogCapture();
                var previous = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = capture;
                string values;
                try
                {
                    var style = new GUIStyle { font = font, fontSize = 14 };
                    var wrapping = new GUIStyle(style) { wordWrap = true };
                    var size = style.CalcSize(new GUIContent("Hello World"));
                    style.CalcMinMaxWidth(new GUIContent("Hello World"), out float minWidth, out float maxWidth);
                    float height = wrapping.CalcHeight(wrapContent, 83f);
                    values = $"[{F(style.lineHeight)},{F(size.x)},{F(size.y)},{F(minWidth)},{F(maxWidth)},{F(height)}]";
                    style.Draw(new Rect(0, 0, 100, 20), new GUIContent("Hello World"), false, false, false, false);
                    style.Draw(new Rect(0, 0, 100, 20), new GUIContent("Hello World"), false, false, false, false);
                }
                finally { Debug.unityLogger.logHandler = previous; }
                Put("calcText/font/" + name, values);
                Put("calcText/font/" + name + "/log", "[" + string.Join(",", capture.Entries.Select(Json)) + "]");
            }
            FontCase("project", Resources.Load<Font>("ProbeSerif"));
            FontCase("empty", new Font());
            FontCase("unknownFamily", new Font("No Such Font Family"));

            // The font asset IMGUI generates the default font's text with: its face and the
            // metrics of a few glyphs (the widths above are sums of these advances).
            RecordImguiFontAsset();
        }
        finally { EditorGUIUtility.SetIconSize(prevIconSize); }
    }

    void RecordImguiFontAsset()
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        try
        {
            var handleType = typeof(GUIStyle).Assembly.GetType("UnityEngine.IMGUITextHandle");
            var settings = (TextSettings)handleType.GetField("s_EditorTextSettings", Hidden).GetValue(null);
            Put("calcText/imguiAsset/settings", settings == null ? "null" : Json(settings.GetType().Name));
            var font = (Font)typeof(GUIStyle).GetMethod("GetDefaultFont", Hidden).Invoke(null, null);
            Put("calcText/imguiAsset/defaultFont", font == null ? "null" : $"[{Json(font.name)},{font.fontSize},{(font.dynamic ? "true" : "false")}]");
            if (settings == null || font == null) return;
            var asset = (FontAsset)typeof(TextSettings).GetMethod("GetCachedFontAsset", Hidden).Invoke(settings, new object[] { font });
            if (asset == null)
            {
                Put("calcText/imguiAsset/face", "null");
                return;
            }
            var face = asset.faceInfo;
            Put("calcText/imguiAsset/face",
                $"[{Json(face.familyName)},{Json(face.styleName)},{F(face.pointSize)},{F(face.scale)},{F(face.ascentLine)},{F(face.descentLine)},{F(face.lineHeight)},{F(face.tabWidth)}]");
            Put("calcText/imguiAsset/atlas",
                $"[{Json(asset.atlasRenderMode.ToString())},{asset.atlasPadding},{asset.atlasWidth},{asset.atlasHeight},{Json(asset.atlasPopulationMode.ToString())},{Json(asset.material != null && asset.material.shader != null ? asset.material.shader.name : "null")}]");
            foreach (char c in "Helo WAVT1")
            {
                if (!asset.characterLookupTable.TryGetValue(c, out var character) || character.glyph == null)
                {
                    Put("calcText/imguiAsset/glyph/" + (int)c, "null");
                    continue;
                }
                var m = character.glyph.metrics;
                Put("calcText/imguiAsset/glyph/" + (int)c,
                    $"[{F(m.horizontalAdvance)},{F(m.horizontalBearingX)},{F(m.horizontalBearingY)},{F(m.width)},{F(m.height)},{F(character.glyph.scale)},{F(character.scale)}]");
            }
            var table = typeof(FontAsset).GetProperty("fontFeatureTable", Hidden)?.GetValue(asset);
            string Records(string property) =>
                table?.GetType().GetProperty(property, Hidden)?.GetValue(table) is System.Collections.ICollection records ? records.Count.ToString(Inv) : "null";
            Put("calcText/imguiAsset/featureRecords",
                $"[{Records("glyphPairAdjustmentRecords")},{Records("ligatureRecords")},{Records("MarkToBaseAdjustmentRecords")},{Records("MarkToMarkAdjustmentRecords")}]");
        }
        catch (Exception error)
        {
            Put("calcText/imguiAsset/error", Json(error.GetType().Name + ": " + error.Message));
        }
    }

    static bool ReflectCanHaveKeyboardFocus(int id)
    {
        var method = typeof(EditorGUIUtility).GetMethod("CanHaveKeyboardFocus", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { id });
    }

    static int ReflectGetSearchIndex()
    {
        var method = typeof(EditorGUIUtility).GetMethod("GetSearchIndexOfControlIDList", BindingFlags.NonPublic | BindingFlags.Static);
        return (int)method.Invoke(null, null);
    }

    void RecordFocusIdPass1(string phase)
    {
        string prefix = "focusId/pass1/" + phase + "/";
        var r = new Rect(0, 0, 80, 18);
        int idA = GUIUtility.GetControlID(Q671HintA, FocusType.Passive, r);
        int idB = GUIUtility.GetControlID(Q671HintB, FocusType.Keyboard, r);
        int idC = GUIUtility.GetControlID(Q671HintC, FocusType.Passive, r);

        if (phase == "Layout")
        {
            m_Q671IdA1 = idA;
            m_Q671IdB1 = idB;
            m_Q671IdC1 = idC;
        }

        Put(prefix + "sameAsLayout", $"[{(idA == m_Q671IdA1 ? "true" : "false")},{(idB == m_Q671IdB1 ? "true" : "false")},{(idC == m_Q671IdC1 ? "true" : "false")}]");
        Put(prefix + "canFocus", $"[{(ReflectCanHaveKeyboardFocus(idA) ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(idB) ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(idC) ? "true" : "false")}]");

        // Control hints: record IDs assigned to LabelField, TextField, ToggleLeft so Pass 2 checks reuse when swapped.
        int beforeLabel = ReflectGetSearchIndex();
        EditorGUI.LabelField(r, "L");
        int afterLabel = ReflectGetSearchIndex();
        int beforeText = ReflectGetSearchIndex();
        GUI.SetNextControlName("q671_tf");
        GUI.TextField(r, "abc");
        int afterText = ReflectGetSearchIndex();
        int beforeToggle = ReflectGetSearchIndex();
        EditorGUI.ToggleLeft(r, "T", false);
        int afterToggle = ReflectGetSearchIndex();
        Put(prefix + "searchSteps", $"[{afterLabel - beforeLabel},{afterText - beforeText},{afterToggle - beforeToggle}]");

        int prevKb = GUIUtility.keyboardControl;
        try
        {
            GUI.FocusControl("q671_tf");
            int tfId = GUIUtility.keyboardControl;
            if (phase == "Layout") m_Q671TextFieldId1 = tfId;
            Put(prefix + "namedTf", $"[{(tfId != 0 ? "true" : "false")},{(tfId == m_Q671TextFieldId1 ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(tfId) ? "true" : "false")},{Json(GUI.GetNameOfFocusedControl())}]");

            // EventType.Used must return -1 without advancing search index.
            if (phase == "Repaint")
            {
                var current = Event.current;
                int idxBefore = ReflectGetSearchIndex();
                int usedId;
                int idxAfter;
                try
                {
                    current.type = EventType.MouseDown;
                    current.Use();
                    usedId = GUIUtility.GetControlID(Q671HintUsed, FocusType.Keyboard, r);
                    idxAfter = ReflectGetSearchIndex();
                }
                finally { current.type = EventType.Repaint; }
                Put(prefix + "usedGetControlId", $"[{usedId},{idxAfter - idxBefore}]");
            }
        }
        finally { GUIUtility.keyboardControl = prevKb; }

        // Named control IDs for LabelField and ToggleLeft in a clean tail section.
        GUI.SetNextControlName("q671_lf");
        EditorGUI.LabelField(r, "L2");
        GUI.SetNextControlName("q671_tl");
        EditorGUI.ToggleLeft(r, "T2", false);
        prevKb = GUIUtility.keyboardControl;
        try
        {
            GUI.FocusControl("q671_lf");
            int lfId = GUIUtility.keyboardControl;
            GUI.FocusControl("q671_tl");
            int tlId = GUIUtility.keyboardControl;
            if (phase == "Layout")
            {
                m_Q671LabelId1 = lfId;
                m_Q671ToggleLeftId1 = tlId;
            }
            Put(prefix + "namedEditorControls",
                $"[{(lfId != 0 && lfId == m_Q671LabelId1 ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(lfId) ? "true" : "false")},{(tlId != 0 && tlId == m_Q671ToggleLeftId1 ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(tlId) ? "true" : "false")}]");
        }
        finally { GUIUtility.keyboardControl = prevKb; }
    }

    void RecordFocusIdPass2(string phase)
    {
        string prefix = "focusId/pass2/" + phase + "/";
        var r = new Rect(0, 0, 80, 18);

        // Swap FocusType for the exact same hints A (was Passive) and B (was Keyboard), and introduce new hint D.
        int idA2 = GUIUtility.GetControlID(Q671HintA, FocusType.Keyboard, r);
        int idB2 = GUIUtility.GetControlID(Q671HintB, FocusType.Passive, r);
        int idD2 = GUIUtility.GetControlID(Q671HintD, FocusType.Keyboard, r);

        Put(prefix + "reused", $"[{(idA2 == m_Q671IdA1 ? "true" : "false")},{(idB2 == m_Q671IdB1 ? "true" : "false")},{(idD2 != m_Q671IdC1 && idD2 != m_Q671IdA1 && idD2 != m_Q671IdB1 ? "true" : "false")}]");
        Put(prefix + "canFocus", $"[{(ReflectCanHaveKeyboardFocus(idA2) ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(idB2) ? "true" : "false")},{(ReflectCanHaveKeyboardFocus(idD2) ? "true" : "false")}]");

        // Also test PrefixLabel click focus eligibility with the swapped IDs on Repaint (via synthetic MouseDown).
        if (phase == "Repaint")
        {
            int prevKb = GUIUtility.keyboardControl;
            var current = Event.current;
            var prevPos = current.mousePosition;
            int prevBtn = current.button;
            try
            {
                current.type = EventType.MouseDown;
                current.button = 0;
                current.mousePosition = new Vector2(12, 8);

                GUIUtility.keyboardControl = 0;
                EditorGUI.PrefixLabel(new Rect(0, 0, 140, 18), idA2, new GUIContent("A"));
                bool clickedA = GUIUtility.keyboardControl == idA2;

                GUIUtility.keyboardControl = 0;
                EditorGUI.PrefixLabel(new Rect(0, 0, 140, 18), idB2, new GUIContent("B"));
                bool clickedB = GUIUtility.keyboardControl == idB2;

                GUIUtility.keyboardControl = 0;
                EditorGUI.PrefixLabel(new Rect(0, 0, 140, 18), idD2, new GUIContent("D"));
                bool clickedD = GUIUtility.keyboardControl == idD2;

                Put(prefix + "prefixClickFocused", $"[{(clickedA ? "true" : "false")},{(clickedB ? "true" : "false")},{(clickedD ? "true" : "false")}]");
            }
            finally
            {
                current.type = EventType.Repaint;
                current.mousePosition = prevPos;
                current.button = prevBtn;
                GUIUtility.keyboardControl = prevKb;
            }
        }
    }
}
