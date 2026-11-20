using System;
using UnityEditor;
using UnityEngine;

public sealed partial class ToggleGroupProbe
{
    readonly GUIContent m_Prefix = new GUIContent("Property");
    readonly GUIContent m_PrefixHint = new GUIContent("", "Hint only");
    GUIStyle m_PrefixStyle;
    int m_PrefixInputCount;
    bool m_PrefixRecorded;
    bool m_PrefixStarted;
    readonly GUIContent m_ReusablePrefix = new GUIContent();
    int m_NextPrefixInputCount;
    bool m_NextPrefixRecorded;
    int m_HandlePrefixInputCount;
    bool m_HandlePrefixRecorded;

    void DrawPrefixLabels()
    {
        if (!m_PrefixStarted)
        {
            GUIUtility.hotControl = 0;
            m_PrefixStarted = true;
        }
        var e = Event.current;
        var type = e.type;
        bool down = type == EventType.MouseDown;
        bool observe = down || (type == EventType.KeyDown && e.keyCode == KeyCode.Space);
        bool recordRects = type == EventType.Repaint && !m_PrefixRecorded;
        int other = GUIUtility.GetControlID(FocusType.Keyboard);
        int normal = GUIUtility.GetControlID(FocusType.Keyboard);
        int passive = GUIUtility.GetControlID(FocusType.Passive);
        int styled = GUIUtility.GetControlID(FocusType.Keyboard);
        int disabled = GUIUtility.GetControlID(FocusType.Keyboard);
        if (down)
        {
            GUIUtility.keyboardControl = other;
            EditorGUIUtility.editingTextField = true;
        }

        int previousIndent = EditorGUI.indentLevel;
        float previousWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 1;
        EditorGUIUtility.labelWidth = 58;
        GUI.BeginGroup(new Rect(6, 5, 280, 200));
        try
        {
            string phase = recordRects ? "prefix/rect/" : null;
            m_PrefixStyle ??= new GUIStyle(EditorStyles.boldLabel) { fixedWidth = 32, fixedHeight = 5 };
            Position(phase, "normal", EditorGUI.PrefixLabel(new Rect(14, 18, 200, 33), normal, m_Prefix));
            Position(phase, "passive", EditorGUI.PrefixLabel(new Rect(14, 56, 200, 33), passive, m_Prefix));
            Position(phase, "styled", EditorGUI.PrefixLabel(new Rect(14, 94, 200, 33), styled, m_Prefix, m_PrefixStyle));
            using (new EditorGUI.DisabledScope(true))
                Position(phase, "disabled", EditorGUI.PrefixLabel(new Rect(14, 132, 200, 33), disabled, m_Prefix));
            Position(phase, "none", EditorGUI.PrefixLabel(new Rect(14, 170, 200, 28), other, GUIContent.none));
            Position(phase, "tooltipOnly", EditorGUI.PrefixLabel(new Rect(14, 170, 200, 28), other, m_PrefixHint));
            Position(phase, "narrow", EditorGUI.PrefixLabel(new Rect(14, 170, 41, 28), other, m_Prefix));
        }
        catch (Exception error)
        {
            if (recordRects) Put("prefix/rect/exception", $"\"{error.GetType().Name}\"");
            if (observe) Put("prefix/exception/" + m_PrefixInputCount, $"\"{error.GetType().Name}\"");
        }
        finally
        {
            GUI.EndGroup();
            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.labelWidth = previousWidth;
            if (recordRects) m_PrefixRecorded = true;
        }
        if (observe)
        {
            int focus = GUIUtility.keyboardControl;
            string focusName = focus == normal ? "normal" : focus == passive ? "passive" :
                focus == styled ? "styled" : focus == disabled ? "disabled" : focus == other ? "other" : "none";
            Put("prefix/input/" + m_PrefixInputCount++,
                $"[\"{(down ? "down" : "space")}\",{e.button},\"{focusName}\",{Flag(EditorGUIUtility.editingTextField)},{Flag(e.type == EventType.Used)}]");
        }
    }

    void DrawNextControlPrefixes()
    {
        bool down = Event.current.type == EventType.MouseDown;
        bool recordRects = Event.current.type == EventType.Repaint && !m_NextPrefixRecorded;
        int previousIndent = EditorGUI.indentLevel;
        float previousWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 1;
        EditorGUIUtility.labelWidth = 58;
        if (down) EditorGUIUtility.editingTextField = true;
        GUI.BeginGroup(new Rect(6, 5, 280, 230));
        try
        {
            string phase = recordRects ? "nextPrefix/rect/" : null;
            GUI.SetNextControlName("other");
            GUI.TextField(new Rect(225, 5, 44, 18), "");
            bool previousLabelIgnored = EditorGUIUtility.editingTextField;
            if (down)
            {
                GUI.FocusControl("other");
                EditorGUIUtility.editingTextField = true;
            }
            var firstRow = new Rect(14, 18, 200, 33);
            var standard = m_Frame >= 170
                ? EditorGUI.PrefixLabel(firstRow, m_Prefix, m_PrefixStyle)
                : EditorGUI.PrefixLabel(firstRow, m_Prefix);
            Position(phase, "default", standard);
            GUI.SetNextControlName("default");
            GUI.TextField(standard, "");
            var styled = EditorGUI.PrefixLabel(new Rect(14, 56, 200, 33), m_Prefix, m_PrefixStyle);
            Position(phase, "style", styled);
            GUI.SetNextControlName("style");
            GUI.TextField(styled, "");
            var passive = EditorGUI.PrefixLabel(new Rect(14, 94, 200, 18), 0, m_Prefix);
            Position(phase, "zero", passive);
            GUI.SetNextControlName("passive");
            GUI.Button(passive, "Passive");
            GUI.SetNextControlName("afterPassive");
            GUI.TextField(new Rect(80, 113, 100, 18), "");
            var image = EditorGUI.PrefixLabel(new Rect(14, 132, 200, 33), m_LeftIcon);
            Position(phase, "image", image);
            GUI.SetNextControlName("image");
            GUI.TextField(image, "");
            m_ReusablePrefix.text = "Copied label";
            var copied = EditorGUI.PrefixLabel(new Rect(14, 170, 200, 33), m_ReusablePrefix);
            Position(phase, "copied", copied);
            m_ReusablePrefix.text = "";
            GUI.SetNextControlName("copied");
            GUI.TextField(copied, "");

            // Leave a label at the end of each pass, after the last ID.
            Position(phase, "unpaired", EditorGUI.PrefixLabel(new Rect(14, 208, 200, 18), m_Prefix));
            if (down)
            {
                string name = GUI.GetNameOfFocusedControl();
                Put("nextPrefix/input/" + m_NextPrefixInputCount++,
                    $"[\"{name}\",{Flag(EditorGUIUtility.editingTextField)},{Flag(Event.current.type == EventType.Used)},{Flag(previousLabelIgnored)}]");
            }
        }
        catch (Exception error)
        {
            if (recordRects) Put("nextPrefix/rect/exception", $"\"{error.GetType().Name}\"");
            if (down) Put("nextPrefix/input/" + m_NextPrefixInputCount++, $"\"{error.GetType().Name}\"");
        }
        finally
        {
            GUI.EndGroup();
            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.labelWidth = previousWidth;
            if (recordRects) m_NextPrefixRecorded = true;
        }
    }

    void DrawHandledPrefixes()
    {
        bool down = Event.current.type == EventType.MouseDown;
        bool recordRepaint = Event.current.type == EventType.Repaint && !m_HandlePrefixRecorded;
        GUI.BeginGroup(new Rect(6, 5, 280, 180));
        try
        {
            int other = GUIUtility.GetControlID(FocusType.Keyboard);
            int basic = GUIUtility.GetControlID(FocusType.Keyboard);
            int nullStyle = GUIUtility.GetControlID(FocusType.Keyboard);
            if (down)
            {
                GUIUtility.keyboardControl = other;
                EditorGUIUtility.editingTextField = true;
            }

            // The custom label is outside the supplied total rectangle.
            var total = new Rect(100, 18, 142, 25);
            var label = new Rect(8, 18, 46, 13);
            if (m_Frame < 187)
                EditorGUI.HandlePrefixLabel(total, label, m_Prefix, basic);
            else
                EditorGUI.HandlePrefixLabel(total, label, m_Prefix, basic, m_PrefixStyle);
            EditorGUI.HandlePrefixLabel(new Rect(100, 94, 142, 25), new Rect(8, 94, 46, 13), m_Prefix);
            int next = GUIUtility.GetControlID(FocusType.Keyboard);
            try
            {
                EditorGUI.HandlePrefixLabel(new Rect(100, 132, 142, 25), new Rect(8, 132, 46, 13), m_Prefix, basic, null);
                if (recordRepaint) Put("handlePrefix/nullStyleRepaint", "\"ok\"");
            }
            catch (Exception error)
            {
                if (recordRepaint) Put("handlePrefix/nullStyleRepaint", $"\"{error.GetType().Name}\"");
            }
            try
            {
                EditorGUI.PrefixLabel(new Rect(130, 155, 110, 18), nullStyle, m_Prefix, null);
                if (recordRepaint) Put("handlePrefix/prefixNullStyleRepaint", "\"ok\"");
            }
            catch (Exception error)
            {
                if (recordRepaint) Put("handlePrefix/prefixNullStyleRepaint", $"\"{error.GetType().Name}\"");
            }

            if (down)
            {
                int focus = GUIUtility.keyboardControl;
                string name = focus == basic ? "basic" : focus == next ? "next" :
                    focus == nullStyle ? "nullStyle" : focus == other ? "other" : "none";
                Put("handlePrefix/input/" + m_HandlePrefixInputCount++,
                    $"[\"{name}\",{Flag(EditorGUIUtility.editingTextField)},{Flag(Event.current.type == EventType.Used)}]");
            }
        }
        finally
        {
            GUI.EndGroup();
            if (recordRepaint) m_HandlePrefixRecorded = true;
        }
    }
}
