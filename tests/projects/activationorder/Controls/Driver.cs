// Drives the probe's real scripts over the fake Unity API like a batchmode Editor would:
// -executeMethod ProbeRunner.Record, delayCall/update ticks, Play mode entry with a domain reload
// (a fresh load of the scripts' assembly; SessionState survives), the runtime boot, then per frame
// pending Starts and one coroutine step, Play mode exit (every active behaviour gets OnDisable
// after the record is closed) with or without a domain reload, then one more update and play mode
// callback after Exit (shutdown: nothing may be written any more).
//
//     Driver SCENARIO PROBE.dll OUT_DIR      writes OUT_DIR/driver.json
//
// Scenarios choose the fake answers: normal (pre-order everywhere, parent-first interleaved wake),
// post, revpre, separate (plain pre-order, batch post-order), phased (parent-first phased wake),
// childwake (child-first interleaved wake), noreload (no reload on leaving Play mode), pause
// (Error Pause during Play), watchdog (the probe never boots), setupfault (the third recorder
// cannot be added), nooutput (the output variable is not set).
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
        const string variable = "UNBOUND_PROBE_ACTIVATIONORDER_OUT";
        if (scenario != "nooutput") Environment.SetEnvironmentVariable(variable, output);
        switch (scenario)
        {
            case "post": Fake.PlainOff = Fake.PlainOn = Fake.BatchOff = Fake.BatchOn = "post"; break;
            case "revpre": Fake.PlainOff = Fake.PlainOn = Fake.BatchOff = Fake.BatchOn = "revpre"; break;
            case "separate": Fake.BatchOff = Fake.BatchOn = "post"; break;
            case "phased": Fake.WakePhased = true; break;
            case "childwake": Fake.WakeParentFirst = false; break;
            case "setupfault": Fake.FailAddAt = 1 + 3; break;    // the driver, then the third recorder
        }
        bool reloadOnExit = scenario != "noreload";
        Domain();
        Fake.Context = "executeMethod";
        scripts.GetType("ProbeRunner").GetMethod("Record").Invoke(null, null);
        int ticks = 0, unpausedAt = -1;
        string values = Path.Combine(output, "values.jsonl");
        string valuesAtFinish = null;
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
                EditorApplication.isPlaying = true;
                if (scenario != "watchdog")
                {
                    Fake.Context = "runtime";
                    scripts.GetType("ActivationOrderProbe").GetMethod("Boot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                }
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.EnteredPlayMode);
            }
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                Fake.Context = "player";
                Fake.RunStarts();
                Fake.StepCoroutines();
                Fake.NextFrame();
            }
            if (scenario == "pause" && EditorApplication.isPlaying && unpausedAt < 0 && ticks > 3)
            {
                EditorApplication.isPaused = true;
                unpausedAt = ticks;
            }
            if (Fake.PendingExit)
            {
                Fake.PendingExit = false;
                valuesAtFinish = File.Exists(values) ? File.ReadAllText(values) : null;
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.ExitingPlayMode);
                Fake.Context = "teardown";
                Fake.TearDown();
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
        bool unchanged = valuesAtFinish == (File.Exists(values) ? File.ReadAllText(values) : null);
        File.WriteAllText(Path.Combine(output, "driver.json"),
            "{\"scenario\":" + Escape(scenario) + ",\"exit\":" + (Fake.ExitCode?.ToString() ?? "null")
            + ",\"ticks\":" + ticks + ",\"paused\":" + (unpausedAt >= 0 ? "true" : "false")
            + ",\"stillPaused\":" + (EditorApplication.isPaused ? "true" : "false")
            + ",\"nativeLive\":" + Fake.NativeLive
            + ",\"teardownCallbacks\":" + Fake.TeardownCallbacks
            + ",\"recordUnchangedByTeardown\":" + (unchanged ? "true" : "false")
            + ",\"handlersAtEnd\":" + (EditorApplication.UpdateHandlerCount + EditorApplication.StateHandlerCount)
            + ",\"logHandlersAtEnd\":" + UnityEngine.Application.HandlerCount
            + ",\"eventsWrittenAtShutdown\":" + (after - before)
            + ",\"forbidden\":[" + string.Join(",", Fake.Forbidden.Concat(Violations).Select(Escape)) + "]"
            + ",\"transitions\":[" + string.Join(",", Fake.Transitions.Select(Escape)) + "]}\n");
        return 0;
    }
}
