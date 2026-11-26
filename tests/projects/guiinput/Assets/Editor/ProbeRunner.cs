using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Makes the probe's scene (<see cref="Create"/>) and records it in the windowed Editor
/// (<see cref="Record"/>): the Editor's window fills the private display, a maximized Game view
/// shows the 320x180 game, and <see cref="ProbeDriver"/> plays <see cref="GuiInputProbe.Steps"/>
/// with xdotool.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Controller").AddComponent<GuiInputProbe>();
        new GameObject("A").AddComponent<GuiInputBehaviour>();
        new GameObject("B").AddComponent<GuiInputBehaviour>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        // Without a window manager the Editor's window opens small: make it fill the screen.
        var containerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
        foreach (var container in Resources.FindObjectsOfTypeAll(containerType))
        {
            var showMode = containerType.GetProperty("showMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (showMode != null && Convert.ToInt32(showMode.GetValue(container)) != 4) continue; // ShowMode.MainWindow
            containerType.GetProperty("position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(container, new Rect(0, 0, 1280, 720));
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "Probe");
        PlayModeWindow.SetPlayModeFocused(true);
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.maximized = true;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
        };
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>Plays the probe's steps as X11 input (xdotool), each step's tokens in one run so that
/// they all reach the Editor before its next frame; game pixels are mapped to the screen from the
/// Game view's layout.</summary>
[InitializeOnLoad]
public static class ProbeDriver
{
    static Vector2 s_Origin, s_Scale = Vector2.one;

    static ProbeDriver()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode || Application.isBatchMode) return;
        GuiInputProbe.driver = Play;
        GuiInputProbe.finished = () => EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }

    static void Locate()
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var views = Resources.FindObjectsOfTypeAll(type);
        if (views.Length == 0) return;
        var view = (EditorWindow)views[0];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var viewInWindow = (Rect)type.GetProperty("viewInWindow", flags).GetValue(view);
        var targetInView = (Rect)type.GetProperty("targetInView", flags).GetValue(view);
        s_Origin = view.position.position + viewInWindow.position + targetInView.position;
        s_Scale = new Vector2(targetInView.width / 320f, targetInView.height / 180f);
    }

    static void Play(GuiInputProbe.Step step)
    {
        if (step.frame == GuiInputProbe.Steps[0].frame) Locate();
        var commands = new List<string>();
        foreach (var token in step.tokens)
        {
            if (token[0] == '@')
            {
                var xy = token.Substring(1).Split(',');
                var p = s_Origin + Vector2.Scale(s_Scale, new Vector2(int.Parse(xy[0]), int.Parse(xy[1])));
                commands.Add($"mousemove {Mathf.Round(p.x)} {Mathf.Round(p.y)}");
                continue;
            }
            string verb = token[0] == '+' ? "down" : "up";
            string key = token.Substring(1);
            commands.Add(key == "Mouse0" ? $"mouse{verb} 1" : $"key{verb} --delay 0 {key.ToLowerInvariant()}");
        }
        Xdotool(string.Join(" ", commands));
    }

    public static void Xdotool(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false });
        process.WaitForExit();
    }
}
