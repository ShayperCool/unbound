using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs every scenario once the scene is loaded when <c>PLAYABLES_PROBE_OUT</c> names the JSON file
/// to write: the synchronous ones at once, then the frame scenarios over the following frames
/// (50 frames per second of game time), then writes the file and quits. The same code runs in the
/// Unity Editor's Play mode (the reference) and in Unbound. <c>PLAYABLES_PROBE_ONLY</c> restricts
/// the run to scenarios whose name starts with it.
/// </summary>
public static class ProbeEntry
{
    public static string Only => Environment.GetEnvironmentVariable("PLAYABLES_PROBE_ONLY") ?? "";

    public static bool Wanted(string scenario) => scenario.StartsWith(Only, StringComparison.Ordinal) || Only.StartsWith(scenario, StringComparison.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PLAYABLES_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Time.captureFramerate = 50;
        FramePhases.Install();
        ProbeCore.RunAll();
        ProbeAnimation.RunAll();
        ProbeAudio.RunAll();
        if (Wanted("timeline.evaluate")) ProbeTimeline.Evaluate();
        var runner = new GameObject("FrameRunner").AddComponent<FrameRunner>();
        foreach (var s in new FrameScenario[]
        {
            new ScriptFramesScenario(), new DestroyFramesScenario(), new DirectorFramesScenario(),
            new AnimFramesScenario(), new TimelinePlayScenario(), new RigFramesScenario(), new ShotsScenario(), new TimelineLoopScenario(),
        })
            if (Wanted(s.Name)) runner.Add(s);
        runner.Finished = () =>
        {
            File.WriteAllText(output, ProbeLog.Json());
            Debug.Log("playables probe written to " + output);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        };
    }
}
