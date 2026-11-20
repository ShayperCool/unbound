using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class EditorStylesProbe
{
    const int MixedTop = PaintHeight;
    const int MixedHeight = 48;

    GUIStyle m_MixedStyle;
    string m_MixedError;
    bool m_MixedEnabled;
    bool m_MixedChildEnabled;
    bool m_MixedRestored;

    void DrawMixedToggleControls()
    {
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(new Rect(0, MixedTop, PaintWidth, MixedHeight), new Color32(100, 130, 160, 255));
            m_MixedStyle ??= new GUIStyle { name = "ToggleMixed" };
            DrawStates(m_MixedStyle, MixedTop + 2);
        }

        DrawMixedGroup(8, parentEnabled: true);
        DrawMixedGroup(64, parentEnabled: false);
    }

    void DrawMixedGroup(int x, bool parentEnabled)
    {
        GUILayout.BeginArea(new Rect(x, MixedTop + 24, 54, 24));
        EditorGUI.BeginDisabledGroup(!parentEnabled);
        bool oldMixed = EditorGUI.showMixedValue;
        try
        {
            EditorGUI.showMixedValue = true;
            using (var group = new EditorGUILayout.ToggleGroupScope(GUIContent.none, true))
            {
                if (parentEnabled)
                {
                    m_MixedEnabled = group.enabled;
                    m_MixedChildEnabled = GUI.enabled;
                }
            }
            if (parentEnabled) m_MixedRestored = GUI.enabled;
        }
        catch (Exception error) { m_MixedError = error.GetType().Name; }
        finally
        {
            EditorGUI.showMixedValue = oldMixed;
            EditorGUI.EndDisabledGroup();
            GUILayout.EndArea();
        }
    }

    void RecordMixedTogglePaint()
    {
        if (m_MixedError != null) Put("mixedToggle/exception", Quote(m_MixedError));
        Put("mixedToggle/state", $"[{Flag(m_MixedEnabled)},{Flag(m_MixedChildEnabled)},{Flag(m_MixedRestored)}]");
        var image = new Texture2D(PaintWidth, MixedHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, Screen.height - MixedTop - MixedHeight, PaintWidth, MixedHeight), 0, 0);
        File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT"), "toggle-mixed.png"), image.EncodeToPNG());
        Destroy(image);
    }
}
