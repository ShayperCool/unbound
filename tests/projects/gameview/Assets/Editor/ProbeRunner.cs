using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records the probe in a windowed Editor (an X server of its own, the GPU through VirtualGL):
/// with <c>SCREENREAD_PROBE_OUT</c> set, the Editor opens the probe scene at a 64x32 game view,
/// enters Play mode and quits when <see cref="GameViewProbe"/> has written its record (nonzero on
/// failure). A watchdog quits with code 4 when Play mode does not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    /// <summary>Makes the probe's empty scene (batchmode, -quit).</summary>
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        EditorApplication.update += () => {
            if (!File.Exists(Path.Combine(output, "values.jsonl"))) return;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.Exit(0);
        };
        if (SessionState.GetBool("GameViewProbe.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("GameViewProbe.code", 5));
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(Path.Combine(output, "watchdog.txt"), "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            GameViewProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("GameViewProbe.finished", true);
                SessionState.SetInt("GameViewProbe.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("GameViewProbe.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("GameViewProbe.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSettings.asyncShaderCompilation = false;
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var game = (EditorWindow)ScriptableObject.CreateInstance(type);
                game.titleContent = new GUIContent("GameViewProbe");
                game.ShowUtility();
                SessionState.SetInt("GameViewProbe.window",game.GetInstanceID());
                game.position = new Rect(20,20,640,360);
                game.Focus();
                PlayModeWindow.SetCustomRenderingResolution(320, 320, "GameViewProbe");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[screenread] entering Play mode");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(6);
            }
        };
        EditorApplication.update += wait;
    }
}
