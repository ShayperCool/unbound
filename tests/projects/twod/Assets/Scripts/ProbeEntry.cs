using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs every scenario once the scene is loaded when <c>TWOD_PROBE_OUT</c> names the JSON file to
/// write, then quits. The same code runs in the Unity Editor's Play mode (the reference) and in
/// Unbound. <c>TWOD_PROBE_ONLY</c> limits the run to scenarios whose name starts with it.
/// </summary>
public static class ProbeEntry
{
    public static string Only => Environment.GetEnvironmentVariable("TWOD_PROBE_ONLY") ?? "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("TWOD_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        TilemapProbe.RunAll();
        File.WriteAllText(output, ProbeLog.Json());
        Debug.Log("2D probe written to " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    public static void Scenario(string name, Action body)
    {
        if (!name.StartsWith(Only)) return;
        ProbeLog.Begin(name);
        try
        {
            body();
        }
        catch (Exception e)
        {
            ProbeLog.Error(e);
            Debug.LogException(e);
        }
    }
}
