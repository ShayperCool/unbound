// Drives the probe's real scripts over the fake Unity API like a batchmode Editor would:
// -executeMethod ProbeRunner.Record, delayCall/update ticks, Play mode entry with a domain reload
// (a fresh load of the scripts' assembly; SessionState survives), the AfterSceneLoad boot, one
// fake PlayerLoop frame per Editor tick, Play mode exit (every object destroyed) with or without a
// domain reload, then one more update and play mode callback after Exit (shutdown: nothing may be
// written any more).
//
//     Driver SCENARIO PROBE.dll OUT_DIR      writes OUT_DIR/driver.json
//
// Scenarios: normal (rule delayed), noreload (no reload on leaving Play mode), pause (Error Pause
// during Play), watchdog (the probe never boots), nooutput (no output variable), fault
// (FindObjectsByType throws from frame 12: a probe fault), loopbroken (the read-back loop lost a
// leaf), skipleaf (the PreUpdate group, markers included, skipped once), fixedtwice (two FixedUpdate iterations on odd
// frames), reenterthrows (the reentrant Destroy throws), nowfeof (WaitForEndOfFrame coroutines
// never resume), wfeofafter (they resume after the probe's teofc point, before the next leaf), and
// rule-drains / rule-immediate / rule-nullnext (other toy Destroy rules, see FakeUnity.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEditor;

static class Driver
{
    static Assembly scripts;
    static byte[] image;
    static readonly List<string> Violations = new List<string>();

    static void Domain()
    {
        EditorApplication.DropDomain();
        UnityEngine.Application.ClearLogHandlers();
        scripts = new AssemblyLoadContext("domain", isCollectible: true).LoadFromStream(new MemoryStream(image));
        Fake.Context = "domain-load";
        RuntimeHelpers.RunClassConstructor(scripts.GetType("ProbeRunner").TypeHandle);
    }

    static void Guarded(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { Violations.Add(what + ": " + (e.InnerException ?? e).Message); }
    }

    static string Escape(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static int Main(string[] args)
    {
        string scenario = args[0];
        image = File.ReadAllBytes(args[1]);
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        const string variable = "UNBOUND_PROBE_DESTROYCONTRACT_OUT";
        if (scenario != "nooutput") Environment.SetEnvironmentVariable(variable, output);
        if (scenario.StartsWith("rule-")) Fake.Rule = scenario.Substring(5);
        if (scenario == "fault") Fake.FaultAtFrame = 12;
        if (scenario == "loopbroken") Fake.ReadBackDropsLeaf = true;
        if (scenario == "skipleaf") Fake.SkipLeafAtFrame = 9;
        if (scenario == "fixedtwice") Fake.FixedOnOddFrames = 2;
        if (scenario == "reenterthrows") Fake.ReentrantThrows = true;
        if (scenario == "nowfeof") Fake.EndOfFrameAt = "never";
        if (scenario == "wfeofafter") Fake.EndOfFrameAt = "afterMarker";
        bool reloadOnExit = scenario != "noreload";
        Domain();
        Fake.Context = "executeMethod";
        scripts.GetType("ProbeRunner").GetMethod("Record").Invoke(null, null);
        int ticks = 0, unpausedAt = -1, frames = 0;
        bool restored = false, installedOnce = true;
        var perFrame = new List<string>();
        var defaultLeaves = Fake.Flatten(Fake.DefaultLoop()).native;
        for (; ticks < 40000 && Fake.ExitCode == null; ticks++)
        {
            Fake.EditorTime += 0.02;
            Fake.Context = "delayCall";
            var calls = EditorApplication.delayCall;
            EditorApplication.delayCall = null;
            Guarded("delayCall", () => calls?.Invoke());
            if (Fake.PendingEnter)
            {
                Fake.PendingEnter = false;
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.ExitingEditMode);
                Domain();
                Fake.ResetPlay();
                EditorApplication.isPlaying = true;
                if (scenario != "watchdog")
                {
                    Fake.Context = "runtime";
                    Guarded("boot", () => scripts.GetType("DestroyContractProbe").GetMethod("Boot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
                    var (native, managed) = Fake.Flatten(Fake.Current);
                    installedOnce = Fake.SetCount == 1 && native.SequenceEqual(defaultLeaves) && managed == 2 * defaultLeaves.Count;
                }
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.EnteredPlayMode);
            }
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                Fake.Context = "runtime";
                Fake.NativeVisits.Clear();
                Guarded("frame", Fake.RunFrame);
                frames++;
                // Every native leaf once per frame (FixedUpdate leaves as often as the group ran).
                var odd = defaultLeaves.Where(l => !l.StartsWith("FixedUpdate.") && (Fake.NativeVisits.TryGetValue(l, out var n) ? n : 0) != 1).ToList();
                if (odd.Count > 0) perFrame.Add(Fake.Frame + ":" + string.Join("|", odd));
            }
            if (scenario == "pause" && EditorApplication.isPlaying && unpausedAt < 0 && ticks > 3)
            {
                EditorApplication.isPaused = true;
                unpausedAt = ticks;
            }
            if (Fake.PendingExit)
            {
                Fake.PendingExit = false;
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.ExitingPlayMode);
                Fake.Context = "runtime";
                Guarded("exit destroy", Fake.DestroyAll);
                var (native, managed) = Fake.Flatten(Fake.Current);
                restored = native.SequenceEqual(defaultLeaves) && managed == 0;
                EditorApplication.isPlaying = false;
                if (reloadOnExit) Domain();
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.EnteredEditMode);
            }
            Fake.Context = "update";
            Guarded("update", () => EditorApplication.update?.Invoke());
        }
        // Shutdown: one more update and play mode callback; the runner must not write any more.
        string events = Path.Combine(output, "runner-events.jsonl");
        long before = File.Exists(events) ? new FileInfo(events).Length : -1;
        Fake.Context = "shutdown";
        Guarded("shutdown update", () => EditorApplication.update?.Invoke());
        Guarded("shutdown state", () => EditorApplication.Fire(PlayModeStateChange.ExitingEditMode));
        long after = File.Exists(events) ? new FileInfo(events).Length : -1;
        File.WriteAllText(Path.Combine(output, "driver.json"),
            "{\"scenario\":" + Escape(scenario) + ",\"rule\":" + Escape(Fake.Rule) + ",\"exit\":" + (Fake.ExitCode?.ToString() ?? "null")
            + ",\"ticks\":" + ticks + ",\"frames\":" + frames + ",\"paused\":" + (unpausedAt >= 0 ? "true" : "false")
            + ",\"stillPaused\":" + (EditorApplication.isPaused ? "true" : "false")
            + ",\"installedOnce\":" + (installedOnce ? "true" : "false")
            + ",\"loopRestored\":" + (restored ? "true" : "false")
            + ",\"leafAnomalies\":[" + string.Join(",", perFrame.Select(Escape)) + "]"
            + ",\"handlersAtEnd\":" + (EditorApplication.UpdateHandlerCount + EditorApplication.StateHandlerCount)
            + ",\"logHandlersAtEnd\":" + UnityEngine.Application.HandlerCount
            + ",\"eventsWrittenAtShutdown\":" + (after - before)
            + ",\"forbidden\":[" + string.Join(",", Fake.Forbidden.Concat(Violations).Select(Escape)) + "]"
            + ",\"transitions\":[" + string.Join(",", Fake.Transitions.Select(Escape)) + "]}\n");
        return 0;
    }
}
