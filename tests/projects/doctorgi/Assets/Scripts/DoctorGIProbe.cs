using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

public sealed class DoctorGIProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCTOR_GI_OUT"))) return;
        var go = new GameObject("DoctorGIProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<DoctorGIProbe>();
    }

    static int Contributors() => FindObjectsByType<Renderer>(FindObjectsSortMode.None)
        .Count(r => r.realtimeLightmapIndex >= 0 && r.realtimeLightmapIndex < 65534);

    static Color32[] Pixels()
    {
        var target = new RenderTexture(160, 90, 24);
        var camera = Camera.main;
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(160, 90, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 160, 90), 0, 0);
        image.Apply();
        var pixels = image.GetPixels32();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Destroy(image);
        target.Release();
        Destroy(target);
        return pixels;
    }

    IEnumerator Start()
    {
        var output = Environment.GetEnvironmentVariable("DOCTOR_GI_OUT");
        Directory.CreateDirectory(output);
        yield return null;
        yield return null;
        var settings = new LightingSettings { bakedGI = false, realtimeGI = false };
        Lightmapping.lightingSettings = settings;
        var before = Pixels();
        settings.realtimeGI = true;
        for (int i = 0; i < 120; i++) yield return null;
        var after = Pixels();
        int changed = before.Zip(after, (a, b) => a.Equals(b) ? 0 : 1).Sum();
        string lines = "{\"case\":\"no-precompute\",\"enabled\":" + (settings.realtimeGI ? "true" : "false")
            + ",\"contributors\":" + Contributors() + ",\"changedPixels\":" + changed + "}\n";
        SceneManager.LoadScene("Realtime");
        yield return null;
        yield return null;
        lines += "{\"case\":\"precomputed\",\"contributors\":" + Contributors() + "}\n";
        File.WriteAllText(Path.Combine(output, "values.jsonl"), lines + "{\"case\":\"end\"}\n");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
