using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records the probe in a windowed Editor (an X server of its own, the GPU through VirtualGL):
/// with <c>STRIP_SHADOW_OUT</c> set, the Editor opens the probe scene at a 96x64 game view,
/// enters Play mode and quits when <see cref="StripShadowProbe"/> has written its record (nonzero on
/// failure). A watchdog quits with code 4 when Play mode does not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;
    static bool s_BeginPending;

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("STRIP_SHADOW_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("StripShadowProbe.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("StripShadowProbe.code", 5));
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
            StripShadowProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("StripShadowProbe.finished", true);
                SessionState.SetInt("StripShadowProbe.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        if (s_BeginPending || EditorApplication.isPlayingOrWillChangePlaymode) return;
        s_BeginPending = true;
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
                PlayModeWindow.SetCustomRenderingResolution(64, 64, "StripShadowProbe");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[stripshadows] entering Play mode");
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
