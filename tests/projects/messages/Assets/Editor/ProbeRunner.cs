using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Plays the probe in the Editor with real input: with <c>MESSAGES_PROBE_DRIVE=1</c> the Editor
/// (with a window, on an X server of its own) opens the probe scene, enters Play mode, and
/// <see cref="ProbeDriver"/> plays <see cref="MessagesProbe.Steps"/> as X11 input with xdotool. A
/// pause (Debug.Break) is recorded and undone after a few Editor updates. The Editor quits when
/// the probe has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    /// <summary>Makes the probe's scenes: Probe (the probe and a second listener) and an empty Second.</summary>
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Probe").AddComponent<MessagesProbe>();
        new GameObject("Listener").AddComponent<AppMessages>();
        new GameObject("DisabledListener").AddComponent<AppMessages>().enabled = false;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(second, "Assets/Scenes/Second.unity");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true),
        };
        AssetDatabase.SaveAssets();
    }

    static int s_PausedUpdates = -1;

    static ProbeRunner()
    {
        EditorApplication.pauseStateChanged += state =>
        {
            if (!EditorApplication.isPlaying) return;
            ProbeLog.Add("pauseState", "v", ProbeLog.Q(state.ToString()), "isPaused", EditorApplication.isPaused ? "true" : "false");
            s_PausedUpdates = state == PauseState.Paused ? 0 : -1;
        };
        EditorApplication.update += () =>
        {
            if (s_PausedUpdates < 0 || !EditorApplication.isPaused) return;
            if (++s_PausedUpdates < 10) return;
            s_PausedUpdates = -1;
            ProbeLog.Add("unpause");
            EditorApplication.isPaused = false;
        };
        if (Environment.GetEnvironmentVariable("MESSAGES_PROBE_DRIVE") != "1" || Application.isBatchMode) return;
        if (SessionState.GetBool("MessagesProbe.finished", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        if (SessionState.GetBool("MessagesProbe.started", false)) return;
        SessionState.SetBool("MessagesProbe.started", true);
        // Without a window manager the Editor's window opens small: make it fill the screen.
        int pid = Process.GetCurrentProcess().Id;
        ProbeDriver.Xdotool($"search --onlyvisible --pid {pid} windowmove %@ 0 0 windowsize %@ 1920 1080");
        var containerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
        foreach (var container in Resources.FindObjectsOfTypeAll(containerType))
        {
            var showMode = containerType.GetProperty("showMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (showMode != null && Convert.ToInt32(showMode.GetValue(container)) != 4) continue; // ShowMode.MainWindow
            containerType.GetProperty("position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(container, new Rect(0, 0, 1920, 1080));
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
        // A maximized game view shows the 960x540 game at 1:1.
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.maximized = true;
        ProbeDriver.RememberEditorWindow();
        Debug.Log("[probe] entering Play mode");
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>Plays the probe's steps as X11 input (xdotool), mapping game-view pixels to the screen
/// by measuring where two pointer moves land.</summary>
public static class ProbeDriver
{
    // screen = origin + scale * game (game: pixels from the game view's top left).
    static Vector2 s_Origin, s_Scale = Vector2.one;
    static string s_EditorWindow;

    public static void RememberEditorWindow() => s_EditorWindow = Output("getwindowfocus").Trim();

    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("MESSAGES_PROBE_DRIVE") != "1") return;
        if (string.IsNullOrEmpty(s_EditorWindow)) RememberEditorWindow();
        Guess();
        MessagesProbe.driver = Play;
        MessagesProbe.finished = () =>
        {
            // Quit from Edit mode: the Editor does not always quit from inside Play mode.
            SessionState.SetBool("MessagesProbe.finished", true);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
            };
            EditorApplication.ExitPlaymode();
        };
        MessagesProbe.frameHook = Calibrate;
    }

    /// <summary>A first mapping from the game view's layout.</summary>
    static void Guess()
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var views = Resources.FindObjectsOfTypeAll(type);
        if (views.Length == 0) return;
        var view = (EditorWindow)views[0];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var viewInWindow = (Rect)type.GetProperty("viewInWindow", flags).GetValue(view);
        var targetInView = (Rect)type.GetProperty("targetInView", flags).GetValue(view);
        s_Origin = view.position.position + viewInWindow.position + targetInView.position;
        s_Scale = new Vector2(targetInView.width / 960f, targetInView.height / 540f);
        Debug.Log($"[probe] game view {view.position} view {viewInWindow} target {targetInView}");
    }

    static Vector2 ToScreen(float x, float y) => s_Origin + Vector2.Scale(s_Scale, new Vector2(x, y));

    static Vector2 GameTopLeft() => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

    /// <summary>The first two steps move the pointer (frames 3 and 5): where they land in the game
    /// refines the mapping before the scenario starts.</summary>
    static void Calibrate(int frame)
    {
        if (frame == 1)
        {
            s_ScreenA = ToScreen(100, 100);
            s_ScreenA = new Vector2(Mathf.Round(s_ScreenA.x), Mathf.Round(s_ScreenA.y));
            Xdotool($"mousemove {s_ScreenA.x} {s_ScreenA.y}");
        }
        if (frame == 2) s_Origin = s_ScreenA - Vector2.Scale(s_Scale, GameTopLeft());
        if (frame == 4) s_MeasuredA = GameTopLeft();
        if (frame != 6) return;
        var b = GameTopLeft();
        var a = s_MeasuredA;
        if (Mathf.Abs(b.x - a.x) > 1 && Mathf.Abs(b.y - a.y) > 1)
        {
            s_Scale = new Vector2((s_ScreenB.x - s_ScreenA.x) / (b.x - a.x), (s_ScreenB.y - s_ScreenA.y) / (b.y - a.y));
            s_Origin = s_ScreenA - Vector2.Scale(s_Scale, a);
        }
        ProbeLog.Add("calibration", "screenA", ProbeLog.V(s_ScreenA), "gameA", ProbeLog.V(a), "screenB", ProbeLog.V(s_ScreenB),
            "gameB", ProbeLog.V(b), "origin", ProbeLog.V(s_Origin), "scale", ProbeLog.V(s_Scale));
    }

    static Vector2 s_MeasuredA, s_ScreenA, s_ScreenB;

    static void Play(MessagesProbe.Step step)
    {
        switch (step.kind)
        {
            case "move":
            {
                var p = ToScreen(step.x, step.y);
                p = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
                if (step.frame == 3) s_ScreenA = p;
                if (step.frame == 5) s_ScreenB = p;
                Xdotool($"mousemove {p.x} {p.y}");
                break;
            }
            case "motion":
                Xdotool($"mousemove_relative -- {step.x} {step.y}");
                break;
            case "press":
                Xdotool("mousedown 1");
                break;
            case "release":
                Xdotool("mouseup 1");
                break;
            case "unfocus":
            {
                // Another window takes the focus, and gives it back half a second later (the
                // Editor may stop updating Play mode meanwhile).
                var other = Output("search --name ^probe-focus$").Trim().Split('\n')[0];
                Debug.Log($"[probe] focus to {other}, back to {s_EditorWindow}");
                Process.Start(new ProcessStartInfo("sh", $"-c \"xdotool windowfocus {other}; sleep 0.5; xdotool windowfocus {s_EditorWindow}\"") { UseShellExecute = false });
                break;
            }
        }
    }

    public static void Xdotool(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false });
        process.WaitForExit();
    }

    static string Output(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false, RedirectStandardOutput = true });
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
