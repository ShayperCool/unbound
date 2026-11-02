// Drives the probe's real scripts over the fake Unity API like a batchmode Editor would:
// -executeMethod ProbeRunner.Record, delayCall/update ticks, Play mode entry with a domain reload
// (a fresh load of the scripts' assembly; SessionState survives), the runtime boot and the
// probe's coroutine one step per frame, Play mode exit with or without a domain reload, then one
// more update and play mode callback after Exit (shutdown: nothing may be written any more).
//
//     Driver SCENARIO PROBE.dll OUT_DIR      writes OUT_DIR/driver.json
//
// Scenarios: normal, noreload (no reload on leaving Play mode), pause (Error Pause during Play),
// watchdog (the probe never boots), fault (destroying case shader100's second new material throws:
// a probe fault), nooutput (the output variable is not set).
using System;
using System.Collections;
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

    /// <summary>Objects the probe made for its cases that are still alive (materials, textures, camera, quad).</summary>
    static int Made() => Fake.Live.Count(o => o is UnityEngine.Material || o is UnityEngine.Texture
        || o is UnityEngine.Camera || o is UnityEngine.Renderer);

    static string Escape(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static int Main(string[] args)
    {
        string scenario = args[0];
        image = File.ReadAllBytes(args[1]);
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        const string variable = "UNBOUND_PROBE_SHADERPASSCOUNT_OUT";
        if (scenario != "nooutput") Environment.SetEnvironmentVariable(variable, output);
        // Case 5 (shader100), its second shader: 4 cases x 4 shaders + 2.
        if (scenario == "fault") Fake.FaultAtFreshDestroy = 4 * 4 + 2;
        bool reloadOnExit = scenario != "noreload";
        Domain();
        Fake.Context = "executeMethod";
        scripts.GetType("ProbeRunner").GetMethod("Record").Invoke(null, null);
        IEnumerator coroutine = null;
        int ticks = 0, unpausedAt = -1, yields = 0, liveAtYield = 0;
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
                    scripts.GetType("ShaderPassCountProbe").GetMethod("Boot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                }
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.EnteredPlayMode);
            }
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                var probe = UnityEngine.GameObject.Added.LastOrDefault();
                Fake.Context = "coroutine";
                if (coroutine == null && probe != null)
                    coroutine = (IEnumerator)probe.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(probe, null);
                if (coroutine != null)
                {
                    bool more = coroutine.MoveNext();
                    if (more)
                    {
                        yields++;
                        if (Made() > 0) liveAtYield++;
                    }
                    else { coroutine = null; UnityEngine.GameObject.Added.Clear(); }
                }
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
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.ExitingPlayMode);
                EditorApplication.isPlaying = false;
                coroutine = null;
                UnityEngine.GameObject.Added.Clear();
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
        bool restored = UnityEngine.Shader.globalMaximumLOD == int.MaxValue
            && Fake.Loaded.Values.All(s => s.maximumLOD == -1) && Fake.Loaded.Count == 4;
        File.WriteAllText(Path.Combine(output, "driver.json"),
            "{\"scenario\":" + Escape(scenario) + ",\"exit\":" + (Fake.ExitCode?.ToString() ?? "null")
            + ",\"ticks\":" + ticks + ",\"paused\":" + (unpausedAt >= 0 ? "true" : "false")
            + ",\"stillPaused\":" + (EditorApplication.isPaused ? "true" : "false")
            + ",\"yields\":" + yields + ",\"madeAliveAtYield\":" + liveAtYield + ",\"madeAliveAtEnd\":" + Made()
            + ",\"lodRestored\":" + (restored ? "true" : "false")
            + ",\"asyncShaderCompilation\":" + (Fake.AsyncShaderCompilation ? "true" : "false")
            + ",\"handlersAtEnd\":" + (EditorApplication.UpdateHandlerCount + EditorApplication.StateHandlerCount)
            + ",\"logHandlersAtEnd\":" + UnityEngine.Application.HandlerCount
            + ",\"eventsWrittenAtShutdown\":" + (after - before)
            + ",\"forbidden\":[" + string.Join(",", Fake.Forbidden.Concat(Violations).Select(Escape)) + "]"
            + ",\"transitions\":[" + string.Join(",", Fake.Transitions.Select(Escape)) + "]}\n");
        return 0;
    }
}
