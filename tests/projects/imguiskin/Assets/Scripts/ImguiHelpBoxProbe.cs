using System;
using UnityEditor;
using UnityEngine;

public sealed partial class ImguiSkinProbe
{
    /// <summary>EditorGUI.HelpBox and EditorGUILayout.HelpBox: each overload's control ID count
    /// and layout rect for every MessageType, wide/narrow mode, wrapping, empty text and
    /// indentation.</summary>
    void RecordHelpBoxes(string phase)
    {
        const string LongMessage = "Long wrapping help message that spans enough lines inside the inspector column to grow taller than the icon height when wide is true and grow even taller when wide is false and label width is reserved on the left.";

        void Write(string name, Action draw, bool layout)
        {
            int before = GUIUtility.GetControlID(FocusType.Passive);
            string error = null;
            try { draw(); }
            catch (Exception exception) { error = exception.GetType().Name; }
            int after = GUIUtility.GetControlID(FocusType.Passive);
            string rect = "null";
            if (layout && phase == "Repaint")
            {
                var r = GUILayoutUtility.GetLastRect();
                rect = $"[{F(r.x)},{F(r.y)},{F(r.width)},{F(r.height)}]";
            }
            Put("helpBox/" + phase + "/" + name, $"[{(error == null ? "null" : Json(error))},{after - before},{rect}]");
        }

        float labelWidth = EditorGUIUtility.labelWidth;
        GUILayout.BeginArea(new Rect(6, 130, 300, 400));
        try
        {
            EditorGUIUtility.labelWidth = 80;
            Write("layout/none", () => EditorGUILayout.HelpBox("Plain message", MessageType.None), true);
            Write("layout/info", () => EditorGUILayout.HelpBox("Info message", MessageType.Info), true);
            Write("layout/warningWide", () => EditorGUILayout.HelpBox("Warning message", MessageType.Warning, true), true);
            Write("layout/errorNarrow", () => EditorGUILayout.HelpBox("Error message", MessageType.Error, false), true);
            Write("layout/wrapWide", () => EditorGUILayout.HelpBox(LongMessage, MessageType.Info, true), true);
            Write("layout/wrapNarrow", () => EditorGUILayout.HelpBox(LongMessage, MessageType.Warning, false), true);
            Write("layout/contentWide", () => EditorGUILayout.HelpBox(new GUIContent("Content wide"), true), true);
            Write("layout/contentNarrow", () => EditorGUILayout.HelpBox(new GUIContent("Content narrow"), false), true);
            Write("layout/emptyNone", () => EditorGUILayout.HelpBox("", MessageType.None), true);
            Write("layout/emptyWarning", () => EditorGUILayout.HelpBox("", MessageType.Warning), true);
            EditorGUI.indentLevel = 2;
            Write("layout/indentedWide", () => EditorGUILayout.HelpBox("Indented wide", MessageType.Info, true), true);
            Write("layout/indentedNarrow", () => EditorGUILayout.HelpBox("Indented narrow", MessageType.Info, false), true);
            EditorGUI.indentLevel = 0;

            var at = new Rect(10, 300, 200, 38);
            Write("rect/none", () => EditorGUI.HelpBox(at, "Rect none", MessageType.None), false);
            Write("rect/info", () => EditorGUI.HelpBox(at, "Rect info", MessageType.Info), false);
            Write("rect/warning", () => EditorGUI.HelpBox(at, "Rect warning", MessageType.Warning), false);
            Write("rect/error", () => EditorGUI.HelpBox(at, "Rect error", MessageType.Error), false);
            Write("rect/content", () => EditorGUI.HelpBox(at, new GUIContent("Rect content")), false);
            EditorGUI.indentLevel = 2;
            Write("rect/indented", () => EditorGUI.HelpBox(at, "Rect indented", MessageType.Info), false);
            EditorGUI.indentLevel = 0;
        }
        finally
        {
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = labelWidth;
            GUILayout.EndArea();
        }
    }
}
