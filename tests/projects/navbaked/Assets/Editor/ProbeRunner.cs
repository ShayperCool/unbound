using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// <c>Unity -batchmode -projectPath tests/projects/navbaked -executeMethod ProbeRunner.Record</c>
/// with <c>NAVBAKED_OUT</c> set: opens the baked scene and enters Play mode, where
/// <see cref="BakedProbe"/> records and exits the Editor. A run not finished long after the Editor
/// started quits with 1 (the runner kills the Editor at 900 s).
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    static ProbeRunner()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVBAKED_OUT"))) return;
        EditorApplication.update += () =>
        {
            if ((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds < WatchdogSeconds) return;
            Debug.LogError($"[navbaked] not finished after {WatchdogSeconds} s; quitting");
            EditorApplication.Exit(1);
        };
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Baked.unity");
        EditorApplication.EnterPlaymode();
    }
}
