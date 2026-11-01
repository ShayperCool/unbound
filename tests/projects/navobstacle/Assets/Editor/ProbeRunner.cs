using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// <c>Unity -batchmode -projectPath tests/projects/navobstacle -executeMethod ProbeRunner.Record</c>
/// with <c>NAVOBSTACLE_OUT</c> set: opens the probe scene and enters Play mode, where
/// <see cref="ObstacleProbe"/> records every lane and exits the Editor. A run not finished long
/// after the Editor started quits with 1 (the runner kills the Editor at 900 s).
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    static ProbeRunner()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVOBSTACLE_OUT"))) return;
        EditorApplication.update += () =>
        {
            if ((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds < WatchdogSeconds) return;
            Debug.LogError($"[navobstacle] not finished after {WatchdogSeconds} s; quitting");
            EditorApplication.Exit(1);
        };
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
