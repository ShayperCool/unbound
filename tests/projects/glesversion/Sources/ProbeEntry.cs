using System;
using System.IO;
using UnityEngine;

/// <summary>Graphics.minOpenGLESVersion of the active build target, written when the scene has
/// loaded in Play mode.</summary>
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        string value;
        try { value = Graphics.minOpenGLESVersion.ToString(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ": " + e.Message; }
        File.WriteAllText(output, "{\"minOpenGLESVersion\": \"" + value + "\"}\n");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
