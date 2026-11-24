using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bakes the probe's scene with Lightmapping.Bake (`APV_OUT` names the folder) and keeps
/// what the bake wrote into the project: every new or changed file under Assets/ (its bytes, in
/// `written/` by its path) and the bake's events in their order (`log.txt`).</summary>
public static class ApvRecord
{
    static Dictionary<string, DateTime> Files() =>
        Directory.GetFiles("Assets", "*", SearchOption.AllDirectories).ToDictionary(f => f.Replace('\\', '/'), File.GetLastWriteTimeUtc);

    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("APV_OUT");
        var log = new List<string>();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Apv.unity");
            if (Camera.main != null)
            {
                var rt = new RenderTexture(16, 16, 16);
                Camera.main.targetTexture = rt;
                Camera.main.Render();
                Camera.main.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            log.Add($"prv initialized={ProbeReferenceVolume.instance.isInitialized}");
            Lightmapping.bakeStarted += () => log.Add("bakeStarted");
            Lightmapping.lightingDataUpdated += () => { if (log.LastOrDefault() != "lightingDataUpdated") log.Add("lightingDataUpdated"); };
            Lightmapping.bakeCompleted += () => log.Add("bakeCompleted");
            var before = Files();
            bool baked = false;
            for (int attempt = 0; attempt < 3 && !baked; attempt++)
                baked = Lightmapping.Bake();
            log.Add("bake " + baked);
            AssetDatabase.SaveAssets();
            foreach (var path in Files().Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                var time = File.GetLastWriteTimeUtc(path);
                if (before.TryGetValue(path, out var was) && was == time) continue;
                var target = Path.Combine(output, "written", path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(path, target, true);
                log.Add("wrote " + path);
            }
            File.WriteAllLines(Path.Combine(output, "log.txt"), log);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            log.Add("exception " + e);
            File.WriteAllLines(Path.Combine(output, "failed.txt"), log);
            EditorApplication.Exit(1);
        }
    }
}
