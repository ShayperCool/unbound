using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Records Unity's Edit-mode behaviour for Lightmapping.GetLightingDataAssetForScene,
/// AdditionalBakeDelegate stepping, and Lightmapping.Cancel() into values.jsonl.
/// </summary>
[InitializeOnLoad]
public static class LightmappingHooksProbe
{
    const string UnbakedScene = "Assets/Scenes/EnvNoSky.unity";
    const string BakedScene = "Assets/Scenes/EnvBakedNoSky.unity";

    static readonly List<string> s_Lines = new List<string>();

    static LightmappingHooksProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LM_HOOKS_OUT"))) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LM_HOOKS_AUTO"))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 2) return;
            EditorApplication.update -= wait;
            Record();
        };
        EditorApplication.update += wait;
    }

    static void Row(string key, string value)
    {
        string escaped = (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        s_Lines.Add("{\"k\":\"" + key + "\",\"v\":\"" + escaped + "\"}");
    }

    static string AssetDesc(UnityEngine.Object obj) =>
        ReferenceEquals(obj, null) ? "null"
        : obj == null ? "destroyed"
        : obj.name + "|" + AssetDatabase.GetAssetPath(obj);

    static string TryDesc(Func<UnityEngine.Object> fn)
    {
        try { return AssetDesc(fn()); }
        catch (Exception ex) { return "EX:" + ex.GetType().Name + ":" + ex.Message; }
    }

    static string CollapseUpdatedRuns(List<string> events)
    {
        var outList = new List<string>();
        foreach (var e in events)
        {
            if (e.StartsWith("U:", StringComparison.Ordinal) && outList.Count > 0 && outList[outList.Count - 1] == e)
                continue;
            outList.Add(e);
        }
        return string.Join(",", outList);
    }

    public static void Record()
    {
        string outPath = Environment.GetEnvironmentVariable("LM_HOOKS_OUT");
        if (string.IsNullOrEmpty(outPath))
        {
            Debug.LogError("LM_HOOKS_OUT is not set");
            EditorApplication.Exit(1);
            return;
        }
        s_Lines.Clear();
        try
        {
            // 1. GetLightingDataAssetForScene
            Row("lda.default", TryDesc(() => Lightmapping.GetLightingDataAssetForScene(default)));

            var unbaked = EditorSceneManager.OpenScene(UnbakedScene, OpenSceneMode.Single);
            Row("lda.unbakedActive", TryDesc(() => Lightmapping.GetLightingDataAssetForScene(unbaked)));

            var baked = EditorSceneManager.OpenScene(BakedScene, OpenSceneMode.Additive);
            var nonActiveLda = Lightmapping.GetLightingDataAssetForScene(baked);
            Row("lda.bakedNonActive", AssetDesc(nonActiveLda));
            Row("lda.activeStillUnbaked", AssetDesc(Lightmapping.lightingDataAsset));

            SceneManager.SetActiveScene(baked);
            var activeLda = Lightmapping.GetLightingDataAssetForScene(baked);
            Row("lda.bakedActive", AssetDesc(activeLda) + "|same=" + ReferenceEquals(activeLda, Lightmapping.lightingDataAsset));

            SceneManager.SetActiveScene(unbaked);
            EditorSceneManager.CloseScene(baked, removeScene: false);
            Row("lda.bakedClosedKeep", TryDesc(() => Lightmapping.GetLightingDataAssetForScene(baked)));

            EditorSceneManager.CloseScene(baked, removeScene: true);
            Row("lda.bakedRemoved", TryDesc(() => Lightmapping.GetLightingDataAssetForScene(baked)));

            // 2. AdditionalBakeDelegate holder
            var defaultDel = Lightmapping.GetAdditionalBakeDelegate();
            float p0 = 0f;
            bool d0 = false;
            defaultDel(ref p0, ref d0);
            Row("delegate.default", string.Format(CultureInfo.InvariantCulture, "notNull={0}|p={1:0.#}|done={2}", defaultDel != null, p0, d0));

            Lightmapping.AdditionalBakeDelegate dummy = (ref float p, ref bool d) => { p = 1f; d = true; };
            Lightmapping.SetAdditionalBakeDelegate(dummy);
            Row("delegate.set", ReferenceEquals(Lightmapping.GetAdditionalBakeDelegate(), dummy).ToString());
            Lightmapping.SetAdditionalBakeDelegate(null);
            Row("delegate.setNull", ReferenceEquals(Lightmapping.GetAdditionalBakeDelegate(), defaultDel).ToString());
            Lightmapping.SetAdditionalBakeDelegate(dummy);
            Lightmapping.ResetAdditionalBakeDelegate();
            Row("delegate.reset", ReferenceEquals(Lightmapping.GetAdditionalBakeDelegate(), defaultDel).ToString());

            // 3. Cancel outside bake
            {
                int cancelled = 0;
                var logs = new List<string>();
                Action onCancel = () => cancelled++;
                Application.LogCallback onLog = (m, s, t) => logs.Add(t + ":" + m);
                Lightmapping.bakeCancelled += onCancel;
                Application.logMessageReceived += onLog;
                string exStr = "ok";
                try { Lightmapping.Cancel(); }
                catch (Exception ex) { exStr = "EX:" + ex.GetType().Name; }
                Application.logMessageReceived -= onLog;
                Lightmapping.bakeCancelled -= onCancel;
                Row("cancel.outside", $"{exStr}|cancelled={cancelled}|running={Lightmapping.isRunning}|logs={logs.Count}");
            }

            // 4. Multi-step AdditionalBakeDelegate
            RunBakeCase("bake.multistep", (events, refStep) =>
            {
                Lightmapping.AdditionalBakeDelegate stepDel = (ref float p, ref bool d) =>
                {
                    int n = ++refStep[0];
                    events.Add(string.Format(CultureInfo.InvariantCulture, "A{0}:{1:0.#}:{2}:{3}", n, p, d, Lightmapping.isRunning));
                    p = n * 0.5f;
                    d = n >= 2;
                };
                Lightmapping.SetAdditionalBakeDelegate(stepDel);
            }, cancelInStarted: false, cancelInUpdated: false);

            // 5. Cancel inside AdditionalBakeDelegate
            RunBakeCase("bake.cancelInAdditional", (events, refStep) =>
            {
                Lightmapping.AdditionalBakeDelegate cancelDel = (ref float p, ref bool d) =>
                {
                    int n = ++refStep[0];
                    events.Add($"A{n}:{Lightmapping.isRunning}");
                    Lightmapping.Cancel();
                    d = false;
                };
                Lightmapping.SetAdditionalBakeDelegate(cancelDel);
            }, cancelInStarted: false, cancelInUpdated: false);

            // 6. Cancel inside bakeStarted
            RunBakeCase("bake.cancelInStarted", (events, refStep) =>
            {
                Lightmapping.AdditionalBakeDelegate stepDel = (ref float p, ref bool d) =>
                {
                    int n = ++refStep[0];
                    events.Add($"A{n}:{Lightmapping.isRunning}");
                    d = true;
                };
                Lightmapping.SetAdditionalBakeDelegate(stepDel);
            }, cancelInStarted: true, cancelInUpdated: false);

            // 7. Cancel inside lightingDataUpdated
            RunBakeCase("bake.cancelInUpdated", (events, refStep) =>
            {
                Lightmapping.AdditionalBakeDelegate stepDel = (ref float p, ref bool d) =>
                {
                    int n = ++refStep[0];
                    events.Add($"A{n}:{Lightmapping.isRunning}");
                    d = true;
                };
                Lightmapping.SetAdditionalBakeDelegate(stepDel);
            }, cancelInStarted: false, cancelInUpdated: true);

            Lightmapping.ResetAdditionalBakeDelegate();
            s_Lines.Add("{\"k\":\"complete\",\"v\":true}");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllLines(outPath, s_Lines);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LM_HOOKS_EXIT")))
                EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LM_HOOKS_EXIT")))
                EditorApplication.Exit(1);
            throw;
        }
    }

    static void RunBakeCase(string prefix, Action<List<string>, int[]> setupDelegate, bool cancelInStarted, bool cancelInUpdated)
    {
        var events = new List<string>();
        int[] steps = { 0 };
        bool cancelledOnce = false;
        Action onStarted = () =>
        {
            events.Add("S:" + Lightmapping.isRunning);
            setupDelegate(events, steps);
            if (cancelInStarted) Lightmapping.Cancel();
        };
        Action onUpdated = () =>
        {
            events.Add("U:" + Lightmapping.isRunning);
            if (cancelInUpdated && !cancelledOnce)
            {
                cancelledOnce = true;
                Lightmapping.Cancel();
            }
        };
        Action onCompleted = () => events.Add("C:" + Lightmapping.isRunning);
        Action onCancelled = () => events.Add("X:" + Lightmapping.isRunning);

        Lightmapping.bakeStarted += onStarted;
        Lightmapping.lightingDataUpdated += onUpdated;
        Lightmapping.bakeCompleted += onCompleted;
        Lightmapping.bakeCancelled += onCancelled;
        bool result;
        try
        {
            result = Lightmapping.Bake();
        }
        finally
        {
            Lightmapping.bakeStarted -= onStarted;
            Lightmapping.lightingDataUpdated -= onUpdated;
            Lightmapping.bakeCompleted -= onCompleted;
            Lightmapping.bakeCancelled -= onCancelled;
            Lightmapping.ResetAdditionalBakeDelegate();
        }
        Row(prefix + ".result", result.ToString());
        Row(prefix + ".steps", steps[0].ToString(CultureInfo.InvariantCulture));
        Row(prefix + ".events", CollapseUpdatedRuns(events));
        Row(prefix + ".runningAfter", Lightmapping.isRunning.ToString());
    }

    public static void RecordPlay()
    {
        EditorSceneManager.OpenScene(UnbakedScene, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
}
