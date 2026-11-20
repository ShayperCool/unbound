using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

public sealed partial class ToggleGroupProbe
{
    readonly GUIContent m_LeftContent = new GUIContent("Custom label");
    GUIStyle m_LeftStyle;
    GUIStyle m_LeftIconStyle;
    GUIContent m_LeftIcon;
    int m_LeftInputCount;
    bool m_LeftRecordedLayout;
    bool m_LeftPlain;
    bool m_LeftStyled;
    bool m_LeftDisabled = true;
    bool m_LeftMixed = true;
    bool m_LeftLayout;
    bool m_LeftLayoutExplicit;

    void DrawToggleLeft()
    {
        var type = Event.current.type;
        var key = Event.current.keyCode;
        var button = Event.current.button;
        bool recordInput = type == EventType.MouseDown || type == EventType.MouseUp ||
            (type == EventType.KeyDown && key == KeyCode.Space);
        bool recordLayout = type == EventType.Repaint && !m_LeftRecordedLayout && m_Frame >= 57;
        EditorGUI.BeginChangeCheck();
        try
        {
            m_LeftStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                margin = new RectOffset(7, 9, 21, 23),
                fixedWidth = 45,
                fixedHeight = 38
            };

            GUI.BeginGroup(new Rect(12, 12, 272, 107));
            try
            {
                GUI.SetNextControlName("plain-toggle");
                m_LeftPlain = EditorGUI.ToggleLeft(new Rect(4, 6, 215, 18), "Plain", m_LeftPlain);
                EditorGUI.indentLevel++;
                try
                {
                    m_LeftStyled = EditorGUI.ToggleLeft(new Rect(4, 32, 215, 20), m_LeftContent, m_LeftStyled, m_LeftStyle);
                }
                finally { EditorGUI.indentLevel--; }
                using (new EditorGUI.DisabledScope(true))
                    m_LeftDisabled = EditorGUI.ToggleLeft(new Rect(4, 56, 215, 18), "Disabled", m_LeftDisabled, m_LeftStyle);
                EditorGUI.showMixedValue = true;
                try
                {
                    m_LeftMixed = EditorGUI.ToggleLeft(new Rect(4, 80, 215, 18), m_MixedLabel, m_LeftMixed);
                }
                finally { EditorGUI.showMixedValue = false; }
            }
            finally { GUI.EndGroup(); }

            GUILayout.BeginArea(new Rect(12, 126, 272, 105));
            try
            {
                m_LeftLayout = EditorGUILayout.ToggleLeft("Automatic", m_LeftLayout);
                Position(recordLayout ? "left/layout/" : null, "string", GUILayoutUtility.GetLastRect());
                m_LeftLayoutExplicit = EditorGUILayout.ToggleLeft(m_LeftContent, m_LeftLayoutExplicit, GUILayout.Width(110), GUILayout.Height(20));
                Position(recordLayout ? "left/layout/" : null, "contentSized", GUILayoutUtility.GetLastRect());
                EditorGUILayout.ToggleLeft("Automatic style", true, m_LeftStyle);
                Position(recordLayout ? "left/layout/" : null, "stringStyle", GUILayoutUtility.GetLastRect());
                EditorGUILayout.ToggleLeft(m_LeftContent, false, m_LeftStyle, GUILayout.Width(90), GUILayout.Height(24));
                Position(recordLayout ? "left/layout/" : null, "contentStyleSized", GUILayoutUtility.GetLastRect());
            }
            finally { GUILayout.EndArea(); }
            DrawLeftPaint();
        }
        catch (Exception error)
        {
            if (recordLayout) Put("left/layout/exception", $"\"{error.GetType().Name}\"");
            if (recordInput) Put("left/inputException/" + m_LeftInputCount, $"\"{error.GetType().Name}\"");
        }
        finally
        {
            bool changed = EditorGUI.EndChangeCheck();
            if (recordLayout)
            {
                m_LeftRecordedLayout = true;
                Put("left/repaint", LeftValues());
                StartCoroutine(RecordLeftPaint());
            }
            if (recordInput)
            {
                bool used = Event.current.type == EventType.Used;
                bool keyboard = GUIUtility.keyboardControl != 0;
                bool plainFocused = GUI.GetNameOfFocusedControl() == "plain-toggle";
                bool hot = GUIUtility.hotControl != 0;
                Put("left/input/" + m_LeftInputCount++,
                    $"[\"{type}\",{button},\"{key}\",{LeftValues()},{Flag(changed)},{Flag(used)},{Flag(keyboard)},{Flag(hot)},{Flag(plainFocused)}]");
            }
        }
    }

    string LeftValues() => $"[{Flag(m_LeftPlain)},{Flag(m_LeftStyled)},{Flag(m_LeftDisabled)},{Flag(m_LeftMixed)},{Flag(m_LeftLayout)},{Flag(m_LeftLayoutExplicit)}]";

    const int LeftPaintTop = 220;
    const int LeftPaintHeight = 20;
    const int LeftPaintWidth = 100;

    void DrawLeftPaint()
    {
        if (m_LeftIcon == null)
        {
            var marker = new Texture2D(5, 5, TextureFormat.RGBA32, false);
            for (int y = 0; y < marker.height; y++)
                for (int x = 0; x < marker.width; x++)
                    marker.SetPixel(x, y, Color.cyan);
            marker.Apply();
            m_LeftIcon = new GUIContent(marker);
            m_LeftIconStyle = new GUIStyle(EditorStyles.label) { imagePosition = ImagePosition.ImageOnly };
        }

        EditorGUI.DrawRect(new Rect(0, LeftPaintTop, LeftPaintWidth * 2, LeftPaintHeight), Color.black);
        EditorGUI.ToggleLeft(new Rect(0, LeftPaintTop, LeftPaintWidth, LeftPaintHeight), m_LeftIcon, false, m_LeftIconStyle);
        EditorGUI.ToggleLeft(new Rect(LeftPaintWidth, LeftPaintTop, LeftPaintWidth, LeftPaintHeight), m_LeftIcon, true, m_LeftIconStyle);
    }

    IEnumerator RecordLeftPaint()
    {
        yield return new WaitForEndOfFrame();
        var capture = new Texture2D(LeftPaintWidth * 2, LeftPaintHeight, TextureFormat.RGBA32, false);
        capture.ReadPixels(new Rect(0, Screen.height - LeftPaintTop - LeftPaintHeight, capture.width, capture.height), 0, 0);
        var pixels = capture.GetPixels32();
        int firstIcon = LeftPaintWidth;
        int lastIcon = -1;
        int firstDifference = LeftPaintWidth;
        int lastDifference = -1;
        for (int y = 0; y < LeftPaintHeight; y++)
            for (int x = 0; x < LeftPaintWidth; x++)
            {
                int off = y * capture.width + x;
                Color32 pixel = pixels[off];
                if (pixel.r == 0 && pixel.g == 255 && pixel.b == 255)
                {
                    firstIcon = Math.Min(firstIcon, x);
                    lastIcon = Math.Max(lastIcon, x);
                }
                if (!pixel.Equals(pixels[off + LeftPaintWidth]))
                {
                    firstDifference = Math.Min(firstDifference, x);
                    lastDifference = Math.Max(lastDifference, x);
                }
            }
        Put("left/paint/iconExtent", $"[{firstIcon},{lastIcon}]");
        Put("left/paint/glyphChangeExtent", $"[{firstDifference},{lastDifference}]");
        Destroy(capture);
    }
}
