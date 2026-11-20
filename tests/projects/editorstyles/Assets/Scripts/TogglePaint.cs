using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class EditorStylesProbe
{
    const int PaintWidth = 288;
    const int PaintHeight = 132;
    GUIStyle m_ToggleCopy;
    GUIStyle m_RenamedCopy;
    GUIStyle m_NamedStyle;
    GUIStyle m_CustomBackground;
    string m_PaintError;
    bool m_PointerInView;

    void DrawTogglePaint()
    {
        try
        {
            m_PointerInView = new Rect(80, 45, 160, 90).Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(new Rect(0, 0, PaintWidth, PaintHeight), new Color32(100, 130, 160, 255));
            var style = EditorStyles.toggle;
            m_ToggleCopy ??= new GUIStyle(style);
            m_RenamedCopy ??= new GUIStyle(style) { name = "Custom toggle" };
            m_NamedStyle ??= new GUIStyle { name = style.name };
            if (m_CustomBackground == null)
            {
                m_CustomBackground = new GUIStyle(style);
                m_CustomBackground.normal.background = Texture2D.whiteTexture;
            }

            DrawStates(style, 8);
            EditorGUI.BeginDisabledGroup(true);
            try { DrawStates(style, 34); }
            finally { EditorGUI.EndDisabledGroup(); }

            DrawAt(m_ToggleCopy, 0, 60);
            DrawAt(m_RenamedCopy, 1, 60);
            DrawAt(m_NamedStyle, 2, 60);
            DrawAt(m_CustomBackground, 3, 60);
            var position = new Rect(4 * 34 + 8, 60, 6, 5);
            style.Draw(position, GUIContent.none, false, false, true, false);
            GUI.BeginGroup(new Rect(5 * 34 + 8, 60, 8, 9));
            try { style.Draw(new Rect(0, 0, 26, 18), GUIContent.none, false, false, true, false); }
            finally { GUI.EndGroup(); }
            style.Draw(new Rect(6 * 34 + 8, 60, 26, 30), GUIContent.none, false, false, true, false);
            Color tint = GUI.color;
            try
            {
                GUI.color = new Color(1f, .5f, .75f, .5f);
                DrawAt(style, 7, 60, on: true);
            }
            finally { GUI.color = tint; }

            int hot = GUIUtility.hotControl;
            try
            {
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                DrawAt(style, 0, 100, hover: true);
                DrawAt(style, 1, 100, hover: true, on: true);
            }
            finally { GUIUtility.hotControl = hot; }

            for (int i = 0; i < 3; i++)
            {
                float fraction = (i + 1) * 0.25f;
                var fractional = new Rect((i + 2) * 34 + 8 + fraction, 100 + fraction, 26, 18);
                style.Draw(fractional, GUIContent.none, false, false, true, false);
            }
            DrawAt(style, 5, 100, active: true, focus: true);
            DrawAt(style, 6, 100, active: true, on: true, focus: true);
            DrawAt(style, 7, 100, hover: true, active: true, on: true, focus: true);
        }
        catch (Exception error) { m_PaintError = error.GetType().Name; }
    }

    static void DrawStates(GUIStyle style, int y)
    {
        for (int column = 0; column < 8; column++)
        {
            int state = column % 4;
            DrawAt(style, column, y, state == 1 || state == 2, state == 2, column >= 4, state == 3);
        }
    }

    static void DrawAt(GUIStyle style, int column, int y, bool hover = false, bool active = false, bool on = false, bool focus = false)
    {
        var rect = new Rect(column * 34 + 8, y, 26, 18);
        style.Draw(rect, GUIContent.none, hover, active, on, focus);
    }

    void RecordTogglePaint()
    {
        if (m_PaintError != null)
            Put("togglePaint/exception", Quote(m_PaintError));
        Put("togglePaint/size", $"[{Screen.width},{Screen.height}]");
        Put("togglePaint/hoverInput", Flag(m_PointerInView));
        var image = new Texture2D(PaintWidth, PaintHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, Screen.height - PaintHeight, PaintWidth, PaintHeight), 0, 0);
        File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT"), "toggle-paint.png"), image.EncodeToPNG());
        Destroy(image);
    }
}
