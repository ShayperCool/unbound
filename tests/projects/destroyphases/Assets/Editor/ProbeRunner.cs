using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT") == null && Environment.GetEnvironmentVariable("RENDER_LOOP_OUT") == null && Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT") == null) return;
        if (SessionState.GetBool("DestroyPhases.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("DestroyPhases.finished", false)) EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }
    static void Begin()
    {
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () => {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                view.Focus();
                PlayModeWindow.SetPlayModeFocused(true);
                EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
    static string focusedWindow;
    static Process focusWindow;
    static int focusTicks;
    static string Xdotool(string args)
    {
        var process = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false, RedirectStandardOutput = true });
        string output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("xdotool failed: " + args);
        return output.Trim();
    }
    static void Tick()
    {
        if (EditorApplication.isPlaying && Environment.GetEnvironmentVariable("FOCUS_LOOP_OUT") != null)
        {
            if (FocusLoopProbe.Step == 1 && focusWindow == null)
            {
                focusedWindow = Xdotool("getwindowfocus");
                focusWindow = Process.Start(new ProcessStartInfo("xmessage", "-title q161-focus -geometry +1000+600 focus") { UseShellExecute = false });
                Xdotool("search --sync --onlyvisible --name q161-focus windowfocus %@");
            }
            if (FocusLoopProbe.Step == 2 && ++focusTicks == 20) Xdotool("windowfocus " + focusedWindow);
            if (FocusLoopProbe.Finished && focusWindow != null && !focusWindow.HasExited) focusWindow.Kill();
        }
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !(DestroyPhases.Finished || RenderLoopProbe.Finished || FocusLoopProbe.Finished)) return;
        SessionState.SetBool("DestroyPhases.finished", true);
        EditorApplication.ExitPlaymode();
    }
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, .1f));
        AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .04f, functionName = "OnAnimationEvent" } });
        AssetDatabase.CreateAsset(clip, "Assets/Resources/Event.anim");
        AssetDatabase.SaveAssets();
    }
}
