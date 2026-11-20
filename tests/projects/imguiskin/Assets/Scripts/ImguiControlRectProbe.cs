using UnityEditor;
using UnityEngine;

public sealed partial class ImguiSkinProbe
{
    void RecordEditorControlRects(string phase)
    {
        void Write(string name, Rect rect) =>
            Put("editorControlRect/" + phase + "/" + name,
                $"[{F(rect.x)},{F(rect.y)},{F(rect.width)},{F(rect.height)}]");

        void Row(string name, int width)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(width));
            try
            {
                Write(name + "/withoutLabel", EditorGUILayout.GetControlRect(false, 9));
                Write(name + "/withLabel", EditorGUILayout.GetControlRect(true, 11));
            }
            finally { GUILayout.EndHorizontal(); }
        }

        float originalLabelWidth = EditorGUIUtility.labelWidth;
        float originalFieldWidth = EditorGUIUtility.fieldWidth;
        GUILayout.BeginArea(new Rect(10, 12, 302, 140));
        try
        {
            EditorGUIUtility.labelWidth = 97;
            EditorGUIUtility.fieldWidth = 43;
            Write("default", EditorGUILayout.GetControlRect());
            Write("withoutLabel", EditorGUILayout.GetControlRect(false));
            Write("fixedOptions", EditorGUILayout.GetControlRect(true, 14, GUILayout.Width(39), GUILayout.Height(16)));

            var style = new GUIStyle { margin = new RectOffset(7, 4, 5, 1), stretchWidth = false };
            Write("styled", EditorGUILayout.GetControlRect(false, 12, style));
            Write("styledWithOption", EditorGUILayout.GetControlRect(true, 10, style, GUILayout.Width(52)));
            Row("narrow", 175);
            Row("wide", 270);

            EditorGUIUtility.labelWidth = 69;
            EditorGUIUtility.fieldWidth = 31;
            Write("differentWidths", EditorGUILayout.GetControlRect(true, 7));
            if (phase == "Layout")
            {
                string outcome = "returned";
                try { EditorGUILayout.GetControlRect(false, 7, (GUIStyle)null); }
                catch (System.Exception exception) { outcome = exception.GetType().Name; }
                Put("editorControlRect/Layout/nullStyle", Json(outcome));
            }
            Write("next", GUILayoutUtility.GetRect(10, 5));
        }
        finally
        {
            GUILayout.EndArea();
            EditorGUIUtility.labelWidth = originalLabelWidth;
            EditorGUIUtility.fieldWidth = originalFieldWidth;
        }

        GUILayout.BeginArea(new Rect(10, 12, 175, 100));
        try
        {
            var firstStyle = new GUIStyle { margin = new RectOffset(6, 4, 3, 2), stretchWidth = false };
            var secondStyle = new GUIStyle { margin = new RectOffset(4, 5, 6, 4), stretchWidth = false };
            Write("axis/vertical", EditorGUILayout.BeginVertical(GUILayout.Height(26)));
            try
            {
                Write("axis/verticalChild", GUILayoutUtility.GetRect(20, 7, firstStyle));
            }
            finally { EditorGUILayout.EndVertical(); }

            Write("axis/horizontal", EditorGUILayout.BeginHorizontal(GUILayout.Width(100)));
            try
            {
                Write("axis/horizontalFirst", GUILayoutUtility.GetRect(18, 7, firstStyle));
                Write("axis/horizontalSecond", GUILayoutUtility.GetRect(23, 11, secondStyle));
            }
            finally { EditorGUILayout.EndHorizontal(); }
            Write("axis/next", GUILayoutUtility.GetRect(10, 5));
        }
        finally { GUILayout.EndArea(); }
    }
}
