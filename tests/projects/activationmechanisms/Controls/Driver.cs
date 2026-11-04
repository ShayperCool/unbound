// Drives the probe's real scripts over the FAKE Unity API like a batchmode Editor would:
// -executeMethod ProbeRunner.Record, delayCall/update ticks, Play mode entry with a domain reload
// (a fresh load of the scripts' assembly; SessionState survives), SubsystemRegistration, a fake
// scene load (the committed scene's objects, component lists and recorder slots from SCENE.json,
// woken in document or hierarchy order), the AfterSceneLoad boot, then per frame pending Starts and
// one coroutine step, Play mode exit (every active behaviour gets OnDisable after the record is
// closed) with or without a domain reload, then one more update and play mode callback after Exit
// (shutdown: nothing may be written any more).
//
//     Driver SCENARIO PROBE.dll OUT_DIR SCENE.json      writes OUT_DIR/driver.json
//
// Scenarios choose the fake answers (none of them is Unity's): normal (off post-order, on add order,
// child-first wake, built-ins interleaved per object, exits at removal, removal-following reads,
// particles cleared on disable, scene woken in document order), revlevel, pre (pre-order both
// ways), firstawake, lastenable, builtinsfirst, scriptsfirst, deferred, noexit, flags, noparticle,
// nocontact, scenehier, noreload, pause, watchdog (the probe never boots), setupfault (an add
// fails), nooutput (the output variable is not set).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
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

    static void LoadScene(JsonElement scene, bool hierarchy)
    {
        var gos = scene.GetProperty("doc_go").EnumerateArray().Select(e => e.GetString()).ToList();
        var parents = scene.GetProperty("parents");
        var children = scene.GetProperty("children").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(e => e.GetString()).ToList());
        var slots = scene.GetProperty("components").EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.EnumerateArray().Select(e => e.GetString().Split('/')[1]).ToList());
        List<(string, string)> wake;
        if (hierarchy)
        {
            var order = new List<string>();
            void Visit(string n) { order.Add(n); foreach (var c in children[n]) Visit(c); }
            foreach (var r in scene.GetProperty("roots").EnumerateArray()) Visit(r.GetString());
            wake = order.SelectMany(n => slots[n].Select(s => (n, s))).ToList();
        }
        else wake = scene.GetProperty("doc_mb").EnumerateArray().Select(e => e.GetString().Split('/')).Select(p => (p[0], p[1])).ToList();
        var list = gos.Select(g => (g, parents.GetProperty(g).ValueKind == JsonValueKind.Null ? null : parents.GetProperty(g).GetString())).ToList();
        Fake.LoadScene(scripts.GetType("ActivationMechanismsProbe"), list, children, slots, wake);
    }

    static int Main(string[] args)
    {
        string scenario = args[0];
        image = File.ReadAllBytes(args[1]);
        string output = Path.GetFullPath(args[2]);
        var scene = JsonDocument.Parse(File.ReadAllText(args[3])).RootElement;
        Directory.CreateDirectory(output);
        const string variable = "UNBOUND_PROBE_ACTIVATIONMECHANISMS_OUT";
        if (scenario != "nooutput") Environment.SetEnvironmentVariable(variable, output);
        switch (scenario)
        {
            case "revlevel": Fake.OffOrder = "revlevel"; break;
            case "pre": Fake.OffOrder = "pre"; Fake.OnOrder = "pre"; break;
            case "firstawake": Fake.OnOrder = "firstawake"; break;
            case "lastenable": Fake.OnOrder = "lastenable"; break;
            case "builtinsfirst": Fake.BuiltinModel = "builtinsFirst"; break;
            case "scriptsfirst": Fake.BuiltinModel = "scriptsFirst"; break;
            case "deferred": Fake.ExitPlacement = "deferred"; break;
            case "noexit": Fake.ExitPlacement = "none"; break;
            case "flags": Fake.ReadsFollowFlags = true; break;
            case "noparticle": Fake.ParticlesChangeOnDisable = false; break;
            case "nocontact": Fake.ContactRange = 0; break;
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
                Fake.Context = "runtime";
                Guarded("reset", () => scripts.GetType("MechanismTrace").GetMethod("ResetTrace", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
                Guarded("scene", () => LoadScene(scene, scenario == "scenehier"));
                if (scenario != "watchdog")
                    Guarded("boot", () => scripts.GetType("MechanismDriver").GetMethod("Boot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
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
            + ",\"teardownCallbacks\":" + Fake.TeardownCallbacks
            + ",\"recordUnchangedByTeardown\":" + (unchanged ? "true" : "false")
            + ",\"handlersAtEnd\":" + (EditorApplication.UpdateHandlerCount + EditorApplication.StateHandlerCount)
            + ",\"logHandlersAtEnd\":" + UnityEngine.Application.HandlerCount
            + ",\"eventsWrittenAtShutdown\":" + (after - before)
            + ",\"simulationModeAtEnd\":" + Escape(UnityEngine.Physics2D.simulationMode.ToString())
            + ",\"callbacksOnDisableAtEnd\":" + (UnityEngine.Physics2D.callbacksOnDisable ? "true" : "false")
            + ",\"forbidden\":[" + string.Join(",", Fake.Forbidden.Concat(Violations).Select(Escape)) + "]"
            + ",\"transitions\":[" + string.Join(",", Fake.Transitions.Select(Escape)) + "]}\n");
        return 0;
    }
}
