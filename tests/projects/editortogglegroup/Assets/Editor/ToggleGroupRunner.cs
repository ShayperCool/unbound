using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ToggleGroupRunner
{
    static EditorWindow s_GameView;
    static Vector2 s_Origin;
    static Vector2 s_Scale;
    static Vector2 s_WindowA;
    static Vector2 s_WindowB;
    static Vector2 s_GameA;

    [InitializeOnLoadMethod]
    static void InstallDriver()
    {
        if (Environment.GetEnvironmentVariable("EDITOR_TOGGLE_GROUP_NATIVE") == "1")
        {
            ToggleGroupProbe.Calibrate = Calibrate;
            ToggleGroupProbe.Drive = Send;
        }
    }

    public static void Record()
    {
        int updates = 0;
        EditorApplication.CallbackFunction open = null;
        open = () =>
        {
            if (++updates < 30) return;
            EditorApplication.update -= open;
            try { OpenGameView(); }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        };
        EditorApplication.update += open;
    }

    static void OpenGameView()
    {
        // The private X11 server has no window manager to arrange the Editor.
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var containerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
        foreach (var container in Resources.FindObjectsOfTypeAll(containerType))
        {
            var mode = containerType.GetProperty("showMode", members)?.GetValue(container);
            if (Convert.ToInt32(mode) != 4) continue;
            containerType.GetProperty("position", members).SetValue(container, new Rect(0, 0, 1280, 720));
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 240, "EditorToggleGroup");
        PlayModeWindow.SetPlayModeFocused(true);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).maximized = true;
        EditorApplication.EnterPlaymode();
    }

    static void Calibrate(int frame)
    {
        if (frame == 1)
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            s_GameView = EditorWindow.GetWindow(type);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = (Rect)type.GetProperty("viewInWindow", flags).GetValue(s_GameView);
            var target = (Rect)type.GetProperty("targetInView", flags).GetValue(s_GameView);
            s_Origin = s_GameView.position.position + viewport.position + target.position;
            s_Scale = new Vector2(target.width / 320f, target.height / 240f);
            s_WindowA = Move(80, 100);
            UnityEngine.Debug.Log($"Toggle group probe: Game view {s_GameView.position}, viewport {viewport}, target {target}, first move {s_WindowA}");
            return;
        }
        var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (frame == 3)
        {
            s_GameA = mouse;
            s_WindowB = Move(190, 148);
        }
        else if (frame == 5)
        {
            var delta = mouse - s_GameA;
            if (delta.x <= 1f || delta.y <= 1f)
                throw new InvalidOperationException($"Game view calibration moves {s_WindowA} and {s_WindowB} led to {s_GameA} and {mouse}.");
            var windowDelta = s_WindowB - s_WindowA;
            s_Scale = new Vector2(windowDelta.x / delta.x, windowDelta.y / delta.y);
            s_Origin = s_WindowA - Vector2.Scale(s_Scale, s_GameA);
        }
    }

    static Vector2 Move(int x, int y)
    {
        var point = s_Origin + Vector2.Scale(s_Scale, new Vector2(x, y));
        int screenX = Mathf.RoundToInt(point.x), screenY = Mathf.RoundToInt(point.y);
        Xdotool($"mousemove {screenX} {screenY}");
        return new Vector2(screenX, screenY);
    }

    static void Send(ToggleGroupProbe.Step step)
    {
        if (step.command == "move")
        {
            Move(step.x, step.y);
            return;
        }
        bool pressed = step.command == "press";
        Xdotool(step.key switch
        {
            "Mouse0" => pressed ? "mousedown 1" : "mouseup 1",
            "Mouse1" => pressed ? "mousedown 3" : "mouseup 3",
            "Space" => pressed ? "keydown space" : "keyup space",
            "LeftControl" => pressed ? "keydown Control_L" : "keyup Control_L",
            _ => throw new InvalidOperationException(step.key),
        });
    }

    static void Xdotool(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("xdotool", command) { UseShellExecute = false });
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(command);
    }
}
