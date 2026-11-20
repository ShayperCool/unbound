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
}
