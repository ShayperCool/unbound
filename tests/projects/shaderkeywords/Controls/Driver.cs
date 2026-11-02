// Drives the probe's real scripts over the fake Unity API like a batchmode Editor would:
// -executeMethod ProbeRunner.Record, delayCall/update ticks, Play mode entry with a domain reload
// (a fresh load of the scripts' assembly; SessionState survives), the runtime boot and the
// probe's coroutine one step per frame, Play mode exit with or without a domain reload.
//
//     Driver SCENARIO PROBE.dll OUT_DIR      writes OUT_DIR/driver.json
//
// Scenarios: normal, noreload (no reload on leaving Play mode), pause (Error Pause during Play),
// watchdog (the probe never boots), fatal (a probe shader lookup throws outside a case), nooutput
// (the output variable is not set).
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

    static void Domain()
    {
        EditorApplication.DropDomain();
        UnityEngine.Application.ClearLogHandlers();
        scripts = new AssemblyLoadContext("domain", isCollectible: true).LoadFromStream(new MemoryStream(image));
        Fake.Context = "domain-load";
        RuntimeHelpers.RunClassConstructor(scripts.GetType("ProbeRunner").TypeHandle);
    }

    static string Escape(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static int Main(string[] args)
    {
        string scenario = args[0];
        image = File.ReadAllBytes(args[1]);
        string output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        if (scenario != "nooutput") Environment.SetEnvironmentVariable("UNBOUND_PROBE_SHADERKEYWORDS_OUT", output);
        if (scenario == "fatal") Fake.ThrowFind = "Probe/KwPlain";
        bool reloadOnExit = scenario != "noreload";
        Domain();
        Fake.Context = "executeMethod";
        scripts.GetType("ProbeRunner").GetMethod("Record").Invoke(null, null);
        IEnumerator coroutine = null;
        int ticks = 0, unpausedAt = -1;
        for (; ticks < 40000 && Fake.ExitCode == null; ticks++)
        {
            Fake.Time += 0.02;
            Fake.Context = "delayCall";
            var calls = EditorApplication.delayCall;
            EditorApplication.delayCall = null;
            calls?.Invoke();
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
                    scripts.GetType("ShaderKeywordsProbe").GetMethod("Boot", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                }
                Fake.Context = "state";
                EditorApplication.Fire(PlayModeStateChange.EnteredPlayMode);
            }
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                var probe = UnityEngine.GameObject.Added.LastOrDefault();
                if (coroutine == null && probe != null)
                    coroutine = (IEnumerator)probe.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(probe, null);
                Fake.Context = "coroutine";
                if (coroutine != null && !coroutine.MoveNext()) { coroutine = null; UnityEngine.GameObject.Added.Clear(); }
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
            EditorApplication.update?.Invoke();
        }
        File.WriteAllText(Path.Combine(output, "driver.json"),
            "{\"scenario\":" + Escape(scenario) + ",\"exit\":" + (Fake.ExitCode?.ToString() ?? "null")
            + ",\"ticks\":" + ticks + ",\"paused\":" + (unpausedAt >= 0 ? "true" : "false")
            + ",\"stillPaused\":" + (EditorApplication.isPaused ? "true" : "false")
            + ",\"asyncShaderCompilation\":" + (EditorSettings.asyncShaderCompilation ? "true" : "false")
            + ",\"forbidden\":[" + string.Join(",", Fake.Forbidden.Select(Escape)) + "]"
            + ",\"transitions\":[" + string.Join(",", Fake.Transitions.Select(Escape)) + "]}\n");
        return 0;
    }
}
