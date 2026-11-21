using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Diagnostics;

namespace ForceCrashProbe
{
    /// <summary>
    /// Utils.ForceCrash with the category FORCE_CRASH_CATEGORY names, in Play mode after the scene
    /// loads: the probe first writes "forcing &lt;category&gt;" to FORCE_CRASH_OUT/before.txt and logs
    /// it, then crashes; if the call returns, it writes after.txt and exits normally.
    /// </summary>
    public static class ForceCrashProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Go()
        {
            var dir = Environment.GetEnvironmentVariable("FORCE_CRASH_OUT");
            var name = Environment.GetEnvironmentVariable("FORCE_CRASH_CATEGORY");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
            var category = int.TryParse(name, out int raw)
                ? (ForcedCrashCategory)raw
                : (ForcedCrashCategory)Enum.Parse(typeof(ForcedCrashCategory), name);
            File.WriteAllText(Path.Combine(dir, "before.txt"), "forcing " + category);
            Debug.Log("ForceCrashProbe: forcing " + category);
            Utils.ForceCrash(category);
            File.WriteAllText(Path.Combine(dir, "after.txt"), "returned");
            EditorApplication.Exit(0);
        }
    }
}
