using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Plays the probe in a windowed Editor: with <c>CULLINGGROUP_PROBE_DRIVE=1</c> the Editor (on an
/// X server of its own) opens the probe scene, enters Play mode with a maximized 960x540 Game view
/// (so the game camera renders every frame and no Scene view camera does), and quits when the
/// probe has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    /// <summary>How long after the Editor's start a run that has not finished counts as failed
    /// (the launcher kills the Editor at 900 s).</summary>
    const double WatchdogSeconds = 780;

    /// <summary>Makes the probe's scene (batchmode: <c>-executeMethod ProbeRunner.Create -quit</c>).</summary>
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Probe").AddComponent<CullingProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        if (Environment.GetEnvironmentVariable("CULLINGGROUP_PROBE_DRIVE") != "1" || Application.isBatchMode) return;
        EditorApplication.update += () =>
        {
            if (SessionState.GetBool("CullingProbe.finished", false)) return;
            if ((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds < WatchdogSeconds) return;
            Debug.LogError($"[probe] not finished after {WatchdogSeconds} s; quitting");
            EditorApplication.Exit(1);
        };
        if (SessionState.GetBool("CullingProbe.finished", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        if (SessionState.GetBool("CullingProbe.started", false)) return;
        SessionState.SetBool("CullingProbe.started", true);
        // Without a window manager the Editor's window opens small: make it fill the screen.
        int pid = Process.GetCurrentProcess().Id;
        ProbeDriver.Xdotool($"search --onlyvisible --pid {pid} windowmove %@ 0 0 windowsize %@ 1280 720");
        var containerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
        foreach (var container in Resources.FindObjectsOfTypeAll(containerType))
        {
            var showMode = containerType.GetProperty("showMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (showMode != null && Convert.ToInt32(showMode.GetValue(container)) != 4) continue; // ShowMode.MainWindow
            containerType.GetProperty("position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(container, new Rect(0, 0, 1280, 720));
        }
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            Enter();
        };
        EditorApplication.update += wait;
    }

    static void Enter()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(960, 540, "Probe");
        PlayModeWindow.SetPlayModeFocused(true);
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.maximized = true;
        Debug.Log("[probe] entering Play mode");
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>Quits the Editor once the probe has written its record.</summary>
public static class ProbeDriver
{
    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("CULLINGGROUP_PROBE_DRIVE") != "1") return;
        CullingProbe.finished = () =>
        {
            // Quit from Edit mode: the Editor does not always quit from inside Play mode.
            SessionState.SetBool("CullingProbe.finished", true);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
            };
            EditorApplication.ExitPlaymode();
        };
    }

    public static void Xdotool(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false });
        process.WaitForExit();
    }
}
