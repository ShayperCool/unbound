using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs every scenario once the scene is loaded when <c>PHYSICS_PROBE_OUT</c> names the JSON file
/// to write, then quits. The same code runs in the Unity Editor's Play mode (the reference) and in
/// Unbound.
/// </summary>
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Probe2D.RunAll();
        Probe3D.RunAll();
        File.WriteAllText(output, ProbeLog.Json());
        Debug.Log("physics probe written to " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
