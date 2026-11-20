using UnityEditor;
using UnityEngine;

public sealed partial class ImguiSkinProbe
{
    void RecordEditorLayoutGroups(string phase)
    {
        void Write(string name, Rect rect) =>
            Put("editorLayout/" + phase + "/" + name,
                $"[{F(rect.x)},{F(rect.y)},{F(rect.width)},{F(rect.height)}]");

        GUILayout.BeginArea(new Rect(16, 20, 260, 132));
        try
        {
            Write("horizontal", EditorGUILayout.BeginHorizontal(GUILayout.Width(68)));
            Write("horizontal/first", GUILayoutUtility.GetRect(17, 8));
            Write("horizontal/second", GUILayoutUtility.GetRect(23, 10));
            EditorGUILayout.EndHorizontal();

            Write("vertical", EditorGUILayout.BeginVertical(GUILayout.Width(40)));
            Write("vertical/first", GUILayoutUtility.GetRect(19, 9));
            Write("vertical/second", GUILayoutUtility.GetRect(21, 11));
            EditorGUILayout.EndVertical();

            var style = new GUIStyle { padding = new RectOffset(3, 7, 2, 4), fixedWidth = 82, fixedHeight = 34 };
            using (var row = new EditorGUILayout.HorizontalScope(style, GUILayout.Width(62), GUILayout.Height(30)))
            {
                Write("styledRow", row.rect);
                Write("styledRow/first", GUILayoutUtility.GetRect(18, 8));
                using (var column = new EditorGUILayout.VerticalScope(GUILayout.Width(23)))
                {
                    Write("styledRow/column", column.rect);
                    Write("styledRow/column/first", GUILayoutUtility.GetRect(12, 7));
                    Write("styledRow/column/second", GUILayoutUtility.GetRect(14, 9));
                }
                Write("styledRow/last", GUILayoutUtility.GetRect(9, 6));
            }

            var padded = new GUIStyle { padding = new RectOffset(5, 4, 3, 2) };
            var outer = EditorGUILayout.BeginVertical(padded, GUILayout.Width(74));
            Write("styledColumn", outer);
            using (var inner = new EditorGUILayout.HorizontalScope(GUILayout.Width(41)))
            {
                Write("styledColumn/row", inner.rect);
                Write("styledColumn/row/first", GUILayoutUtility.GetRect(10, 6));
                Write("styledColumn/row/second", GUILayoutUtility.GetRect(13, 7));
                inner.Dispose();
                Write("styledColumn/row/disposed", inner.rect);
            }
            Write("styledColumn/last", GUILayoutUtility.GetRect(13, 6));
            EditorGUILayout.EndVertical();
            Write("afterGroups", GUILayoutUtility.GetRect(16, 5));
        }
        finally
        {
            GUILayout.EndArea();
        }
    }
}
