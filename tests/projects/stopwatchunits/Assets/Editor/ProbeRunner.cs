using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// With <c>STOPWATCH_UNITS_OUT</c> set, a windowed Editor opens the scene, enters Play mode and
/// quits when <see cref="StopwatchProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("STOPWATCH_UNITS_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("StopwatchProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("StopwatchProbe.finished", false))
                EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }

    static void Begin()
    {
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
            var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            game.Focus();
            PlayModeWindow.SetPlayModeFocused(true);
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !StopwatchProbe.Finished) return;
        SessionState.SetBool("StopwatchProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }
}
