using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// With <c>EOF_STARTUP_OUT</c> set, a windowed Editor opens the scene, enters Play mode and
/// quits when <see cref="StartupProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static readonly bool s_EngineEditSession = Environment.GetEnvironmentVariable("EOF_STARTUP_EDIT_SESSION") == "1";

    static ProbeRunner()
    {
        if (Environment.GetEnvironmentVariable("EOF_STARTUP_OUT") == null) return;
        if (!s_EngineEditSession &&
            (Application.isBatchMode || typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null)) return;
        if (SessionState.GetBool("StartupProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("StartupProbe.finished", false))
                EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }

    static void Begin()
    {
        bool settled = Environment.GetEnvironmentVariable("EOF_STARTUP_SETTLED_SCENE") == "1";
        if (settled) OpenScene();
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            if (!settled) OpenScene();
            if (!s_EngineEditSession)
            {
                foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
                var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                game.Focus();
                PlayModeWindow.SetPlayModeFocused(true);
            }
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }

    static void OpenScene()
    {
        const string path = "Assets/Scenes/Probe.unity";
        if (Environment.GetEnvironmentVariable("EOF_STARTUP_RESTORE_SETUP") == "1")
            EditorSceneManager.RestoreSceneManagerSetup(new[] { new SceneSetup { path = path, isLoaded = true, isActive = true } });
        else
            EditorSceneManager.OpenScene(path);
    }

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !StartupProbe.Finished) return;
        SessionState.SetBool("StartupProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }
}
