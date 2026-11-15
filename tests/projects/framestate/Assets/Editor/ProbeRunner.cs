using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// With <c>FRAMESTATE_OUT</c> set, the Editor opens the probe scene and enters Play mode:
/// a batchmode Editor through <c>-executeMethod ProbeRunner.Record</c> (the probe exits it), a
/// windowed one by itself with the Game view focused, leaving Play mode and quitting when
/// <see cref="FrameStateProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("FRAMESTATE_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("FrameStateProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("FrameStateProbe.finished", false))
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
        if (!EditorApplication.isPlaying || !FrameStateProbe.Finished) return;
        SessionState.SetBool("FrameStateProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }
}
