using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed partial class ImguiSkinProbe
{
    /// <summary>EditorGUI.Popup and EditorGUILayout.Popup: each overload's answer, the control IDs
    /// it takes, its rect; mixed and out-of-range selections; and the menu's choice coming back as
    /// the PopupMenuChanged command.</summary>
    void RecordPopups(string phase)
    {
        string[] names = { "First", "Second &a", "Third %g" };
        var contents = new[] { new GUIContent("One"), new GUIContent("Two"), new GUIContent("Three") };
        var style = new GUIStyle(EditorStyles.popup) { margin = new RectOffset(5, 3, 4, 2) };

        void Write(string name, Func<int> popup, bool layout)
        {
            int before = GUIUtility.GetControlID(FocusType.Passive);
            string value;
            try { value = popup().ToString(Inv); }
            catch (Exception exception) { value = exception.GetType().Name; }
            int after = GUIUtility.GetControlID(FocusType.Passive);
            string rect = "null";
            if (layout && phase == "Repaint")
            {
                var r = GUILayoutUtility.GetLastRect();
                rect = $"[{F(r.x)},{F(r.y)},{F(r.width)},{F(r.height)}]";
            }
            Put("popup/" + phase + "/" + name, $"[{Json(value)},{after - before},{rect}]");
        }

        float labelWidth = EditorGUIUtility.labelWidth;
        // (Below the rows the text paint image reads.)
        GUILayout.BeginArea(new Rect(6, 130, 300, 400));
        try
        {
            EditorGUIUtility.labelWidth = 80;
            Write("layout/plain", () => EditorGUILayout.Popup(1, names), true);
            Write("layout/styled", () => EditorGUILayout.Popup(2, names, style), true);
            Write("layout/contents", () => EditorGUILayout.Popup(0, contents, GUILayout.Width(90)), true);
            Write("layout/contentsStyled", () => EditorGUILayout.Popup(1, contents, style), true);
            Write("layout/label", () => EditorGUILayout.Popup("Pick", 1, names), true);
            Write("layout/labelContent", () => EditorGUILayout.Popup(new GUIContent("Pick"), 2, names), true);
            Write("layout/labelStyled", () => EditorGUILayout.Popup("Pick", 0, names, style, GUILayout.Width(200)), true);
            Write("layout/labelContents", () => EditorGUILayout.Popup(new GUIContent("Pick"), 1, contents), true);
            Write("layout/labelContentsStyled", () => EditorGUILayout.Popup(new GUIContent("Pick"), 2, contents, style), true);
            Write("layout/outOfRange", () => EditorGUILayout.Popup(7, names), true);
            Write("layout/negative", () => EditorGUILayout.Popup(-1, names), true);
            EditorGUI.showMixedValue = true;
            Write("layout/mixed", () => EditorGUILayout.Popup("Pick", 1, names), true);
            EditorGUI.showMixedValue = false;
            Write("layout/nullOptions", () => EditorGUILayout.Popup(0, (string[])null), true);

            var at = new Rect(10, 300, 200, 18);
            Write("rect/plain", () => EditorGUI.Popup(at, 1, names), false);
            Write("rect/styled", () => EditorGUI.Popup(at, 2, names, style), false);
            Write("rect/contents", () => EditorGUI.Popup(at, 0, contents), false);
            Write("rect/label", () => EditorGUI.Popup(at, "Pick", 1, names), false);
            Write("rect/labelContents", () => EditorGUI.Popup(at, new GUIContent("Pick"), 2, contents), false);
            EditorGUI.indentLevel = 2;
            Write("rect/indented", () => EditorGUI.Popup(at, "Pick", 1, names), false);
            EditorGUI.indentLevel = 0;

            // The menu's choice comes back to the control whose menu it was.
            var info = typeof(EditorGUI).GetNestedType("PopupCallbackInfo", BindingFlags.NonPublic);
            foreach (var (name, chosen, mixed) in new[] { ("changed", 2, false), ("same", 1, false), ("mixedSame", 1, true) })
            {
                int before = GUIUtility.GetControlID(FocusType.Passive);
                var instance = Activator.CreateInstance(info, before + 1);
                // (The menu's choice as its callback keeps it; the callback itself would also send
                // the command to the view at once.)
                info.GetField("m_SelectedIndex", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, chosen);
                info.GetField("instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, instance);
                var original = Event.current;
                var command = new Event { type = EventType.ExecuteCommand, commandName = "PopupMenuChanged" };
                Event.current = command;
                bool changed = GUI.changed;
                int value;
                try
                {
                    GUI.changed = false;
                    EditorGUI.showMixedValue = mixed;
                    value = EditorGUI.Popup(at, 1, names);
                }
                finally
                {
                    EditorGUI.showMixedValue = false;
                    Event.current = original;
                }
                bool reported = GUI.changed;
                GUI.changed = changed;
                var left = info.GetField("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                Put("popup/" + phase + "/command/" + name,
                    $"[{value},{(reported ? "true" : "false")},{Json(command.type.ToString())},{(left == null ? "true" : "false")}]");
            }
        }
        finally
        {
            EditorGUI.showMixedValue = false;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = labelWidth;
            GUILayout.EndArea();
        }
    }
}
