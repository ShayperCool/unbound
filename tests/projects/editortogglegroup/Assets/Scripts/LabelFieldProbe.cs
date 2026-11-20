using System.Collections;
using UnityEditor;
using UnityEngine;

public sealed partial class ToggleGroupProbe
{
    const int LabelPaintTop = 157;
    const int LabelPaintHeight = 83;
    const int LabelPaintWidth = 300;
    const string WrapValue = "One two three four five six seven";
    GUIContent m_LabelFieldIcon;
    GUIContent m_LabelFieldPrefix;
    GUIContent m_LabelFieldWrapContent;
    GUIStyle m_LabelFieldStyle;
    GUIStyle m_LabelFieldWrap;
    bool m_LabelFieldRecorded;

    void DrawLabelFields()
    {
        if (m_LabelFieldIcon == null)
        {
            var marker = new Texture2D(5, 5, TextureFormat.RGBA32, false);
            for (int y = 0; y < marker.height; y++)
                for (int x = 0; x < marker.width; x++)
                    marker.SetPixel(x, y, Color.cyan);
            marker.Apply();
            m_LabelFieldIcon = new GUIContent(marker);
            m_LabelFieldPrefix = new GUIContent("Name");
            m_LabelFieldWrapContent = new GUIContent(WrapValue);
            m_LabelFieldStyle = new GUIStyle(EditorStyles.label)
            {
                fixedHeight = 34,
                padding = new RectOffset(9, 3, 1, 0),
                margin = new RectOffset(7, 5, 9, 6)
            };
            m_LabelFieldWrap = new GUIStyle(m_LabelFieldStyle) { wordWrap = true, fixedHeight = 0 };
        }

        if (Event.current.type == EventType.MouseDown)
        {
            Event.current.Use();
            try
            {
                EditorGUILayout.LabelField(m_LabelFieldPrefix, m_LabelFieldWrapContent, m_LabelFieldWrap);
                Put("labelField/input/used", Flag(Event.current.type == EventType.Used));
            }
            catch (System.ArgumentException)
            {
                Put("labelField/input/used", "false");
            }
            return;
        }

        bool record = Event.current.type == EventType.Repaint && !m_LabelFieldRecorded && m_Frame >= 225;
        string phase = record ? "labelField/layout/" : null;
        int previousIndent = EditorGUI.indentLevel;
        float previousWidth = EditorGUIUtility.labelWidth;
        try
        {
            EditorGUI.indentLevel = 1;
            EditorGUIUtility.labelWidth = 62;
            GUILayout.BeginArea(new Rect(0, 0, 146, LabelPaintTop));
            try
            {
                EditorGUILayout.LabelField("Single");
                Position(phase, "string", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField(m_LabelFieldIcon);
                Position(phase, "content", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField("Name", "Value");
                Position(phase, "strings", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField(m_LabelFieldPrefix, m_LabelFieldIcon);
                Position(phase, "contents", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField("Styled", m_LabelFieldStyle);
                Position(phase, "stringStyle", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField(m_LabelFieldIcon, m_LabelFieldStyle, GUILayout.Width(96), GUILayout.Height(22));
                Position(phase, "contentOptions", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField("Name", "Value", m_LabelFieldStyle);
                Position(phase, "stringsStyle", GUILayoutUtility.GetLastRect());
            }
            finally { GUILayout.EndArea(); }

            GUILayout.BeginArea(new Rect(150, 0, 150, LabelPaintTop));
            try
            {
                EditorGUILayout.LabelField(m_LabelFieldPrefix, m_LabelFieldIcon, m_LabelFieldStyle, GUILayout.Height(25));
                Position(phase, "contentsStyle", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField("Name", WrapValue, m_LabelFieldWrap, GUILayout.Width(83));
                Position(phase, "wrapped", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField(WrapValue, m_LabelFieldWrap, GUILayout.Width(123));
                Position(phase, "wrappedSingle", GUILayoutUtility.GetLastRect());
                EditorGUILayout.LabelField(m_LabelFieldPrefix, m_LabelFieldIcon, m_LabelFieldWrap, GUILayout.Width(44), GUILayout.Height(27));
                Position(phase, "wrapOptions", GUILayoutUtility.GetLastRect());
                Position(phase, "afterWrap", GUILayoutUtility.GetRect(12, 6));
            }
            finally { GUILayout.EndArea(); }

            if (record)
            {
                Put("labelField/wrapControl/height83", m_LabelFieldWrap.CalcHeight(m_LabelFieldWrapContent, 83)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
                Put("labelField/wrapControl/height123", m_LabelFieldWrap.CalcHeight(m_LabelFieldWrapContent, 123)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            GUILayout.BeginArea(new Rect(150, 0, 150, LabelPaintTop));
            try
            {
                GUILayout.BeginHorizontal();
                try
                {
                    GUILayoutUtility.GetRect(55, EditorGUIUtility.singleLineHeight, m_LabelFieldWrap, GUILayout.ExpandWidth(false));
                    GUILayoutUtility.GetRect(m_LabelFieldPrefix, m_LabelFieldWrap, GUILayout.Width(83), GUILayout.Height(27));
                }
                finally { GUILayout.EndHorizontal(); }
                Position(phase, "controlNarrowGroup", GUILayoutUtility.GetLastRect());
                GUILayoutUtility.GetRect(12, 6);
            }
            finally { GUILayout.EndArea(); }

            DrawLabelFieldPaint();
        }
        finally
        {
            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.labelWidth = previousWidth;
        }

        if (record)
        {
            m_LabelFieldRecorded = true;
            StartCoroutine(RecordLabelFieldPaint());
        }
    }

    void DrawLabelFieldPaint()
    {
        EditorGUI.DrawRect(new Rect(0, LabelPaintTop, LabelPaintWidth, LabelPaintHeight), Color.black);
        EditorGUI.LabelField(new Rect(8, 160, 140, 19), m_LabelFieldIcon);
        EditorGUI.LabelField(new Rect(8, 180, 140, 19), m_LabelFieldPrefix, m_LabelFieldIcon);
        EditorGUI.LabelField(new Rect(8, 200, 140, 19), m_LabelFieldPrefix, m_LabelFieldIcon, m_LabelFieldStyle);
        EditorGUI.LabelField(new Rect(8, 220, 140, 19), new GUIContent("", "Tooltip"), m_LabelFieldIcon, m_LabelFieldStyle);
        EditorGUIUtility.labelWidth = 94;
        EditorGUI.LabelField(new Rect(152, 180, 140, 19), m_LabelFieldPrefix, m_LabelFieldIcon);
        EditorGUI.LabelField(new Rect(152, 200, 140, 19), m_LabelFieldIcon, m_LabelFieldStyle);
        EditorGUI.LabelField(new Rect(152, 220, 140, 19), new GUIContent("Name", m_LabelFieldIcon.image), m_LabelFieldIcon);
    }

    IEnumerator RecordLabelFieldPaint()
    {
        yield return new WaitForEndOfFrame();
        var capture = new Texture2D(LabelPaintWidth, LabelPaintHeight, TextureFormat.RGBA32, false);
        capture.ReadPixels(new Rect(0, Screen.height - LabelPaintTop - LabelPaintHeight, capture.width, capture.height), 0, 0);
        var pixels = capture.GetPixels32();
        Destroy(capture);
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                int first = -1;
                int last = -1;
                int count = 0;
                for (int x = column * 150; x < (column + 1) * 150; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        var pixel = pixels[(LabelPaintHeight - 4 - row * 20 - y) * LabelPaintWidth + x];
                        if (pixel.r != 0 || pixel.g != 255 || pixel.b != 255) continue;
                        if (first < 0) first = x;
                        last = x;
                        count++;
                    }
                }
                Put($"labelField/paint/{row}/{column}", $"[{first},{last},{count}]");
            }
        }
    }
}
