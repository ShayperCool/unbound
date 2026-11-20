using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EditorStylesRunner
{
    [InitializeOnLoadMethod]
    static void PositionPointerInPlayMode()
    {
        if (Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT") == null ||
            Environment.GetEnvironmentVariable("EDITOR_STYLES_SET_ENGINE_THEME") == "1") return;
        EditorApplication.update += TryMovePointer;
    }

    static void TryMovePointer()
    {
        if (!EditorApplication.isPlaying) return;
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var view = EditorWindow.GetWindow(type);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var viewport = (Rect)type.GetProperty("viewInWindow", flags).GetValue(view);
        var target = (Rect)type.GetProperty("targetInView", flags).GetValue(view);
        if (target.width <= 0f || target.height <= 0f) return;
        var center = view.position.position + viewport.position + target.center;
        using var pointer = Process.Start(new ProcessStartInfo("xdotool",
            $"mousemove {Mathf.RoundToInt(center.x)} {Mathf.RoundToInt(center.y)}") { UseShellExecute = false });
        pointer.WaitForExit();
        if (pointer.ExitCode != 0) throw new InvalidOperationException("xdotool could not place the pointer in the Game view.");
        EditorApplication.update -= TryMovePointer;
    }

    public static void Record()
    {
        int skin = Environment.GetEnvironmentVariable("EDITOR_STYLES_THEME") == "dark" ? 1 : 0;
        EditorPrefs.SetInt("UserSkin", skin);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(EditorGUIUtility).GetProperty("skinIndex", flags).SetValue(null, skin);
        typeof(EditorStyles).GetMethod("UpdateSkinCache", flags, null, Type.EmptyTypes, null).Invoke(null, null);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "EditorStyles");
        PlayModeWindow.SetPlayModeFocused(true);
        EditorApplication.EnterPlaymode();
    }
}
