using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

/// <summary>
/// Plays the probe in the Editor with real input: with <c>INPUT_PROBE_DRIVE=1</c> the Editor (with
/// a window, on an X server of its own) opens the probe scene, enters Play mode, and
/// <see cref="ProbeDriver"/> plays <see cref="InputProbe.Steps"/> as X11 input with xdotool. The
/// Editor quits when the probe has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        // When Play mode counts as entered, against the first frame (the Input System resets its
        // devices then).
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) ProbeLog.Add("playMode", "state", ProbeLog.Q(state.ToString()));
        };
        if (Environment.GetEnvironmentVariable("INPUT_PROBE_DRIVE") != "1" || Application.isBatchMode) return;
        if (SessionState.GetBool("InputProbe.finished", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        if (SessionState.GetBool("InputProbe.started", false)) return;
        SessionState.SetBool("InputProbe.started", true);
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

    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("INPUT_PROBE_DRIVE") != "1") return;
        Guess();
        InputProbe.driver = Play;
        InputProbe.finished = () =>
        {
            // Quit from Edit mode: the Editor does not always quit from inside Play mode.
            SessionState.SetBool("InputProbe.finished", true);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
            };
            EditorApplication.ExitPlaymode();
        };
        InputProbe.frameHook = Calibrate;
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

    static Vector2 GameTopLeft() => new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y);

    /// <summary>The first two steps move the pointer (frames 3 and 5): where they land in the game
    /// refines the mapping before the scenario starts.</summary>
    static void Calibrate(int frame)
    {
        // A first move before the scenario corrects the guessed origin, so that the scenario's
        // own moves land where they should.
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

    static void Play(InputProbe.Step step)
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
            case "press":
            case "release":
            {
                string verb = step.kind == "press" ? "down" : "up";
                if (step.key.StartsWith("Mouse"))
                    Xdotool($"mouse{verb} {int.Parse(step.key.Substring(5)) + 1}");
                else
                    Xdotool($"key{verb} {KeySym(step.key)}");
                break;
            }
            case "scroll":
                Xdotool($"click {(step.y > 0 ? 4 : 5)}");
                break;
            case "type":
                Xdotool($"type --delay 1 {step.text}");
                break;
        }
    }

    static string KeySym(string key) => key switch
    {
        "Space" => "space",
        "Return" => "Return",
        "Escape" => "Escape",
        "LeftArrow" => "Left",
        "RightArrow" => "Right",
        "UpArrow" => "Up",
        "DownArrow" => "Down",
        "LeftShift" => "Shift_L",
        _ => key.ToLowerInvariant(),
    };

    public static void Xdotool(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false });
        process.WaitForExit();
    }
}
