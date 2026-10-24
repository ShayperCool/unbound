using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Renders the main camera into a 320x180 render texture and writes it to
/// <c>BAKED_PROBE_OUT</c> (a PNG), with what the scene's lighting data gives the renderers
/// (lightmap indices and scale-offsets, probe SH at the dynamic sphere) next to it as JSON lines,
/// then quits.</summary>
public class BakedProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BakedProbe").AddComponent<BakedProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        var output = Environment.GetEnvironmentVariable("BAKED_PROBE_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            var camera = Camera.main;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            File.WriteAllBytes(output, texture.EncodeToPNG());
            File.WriteAllText(Path.ChangeExtension(output, ".jsonl"), Describe());
            Debug.Log("baked probe written to " + output);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"{{\"key\":\"lightmaps\",\"value\":{LightmapSettings.lightmaps.Length}}}\n");
        sb.Append($"{{\"key\":\"lightmapsMode\",\"value\":\"{LightmapSettings.lightmapsMode}\"}}\n");
        foreach (var map in LightmapSettings.lightmaps)
        {
            var color = map.lightmapColor;
            sb.Append($"{{\"key\":\"lightmap\",\"value\":\"{color?.name} {color?.width}x{color?.height} {color?.format} {color?.graphicsFormat} dir {map.lightmapDir != null} mask {map.shadowMask != null}\"}}\n");
        }
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).OrderBy(r => r.name).ThenBy(r => r.transform.position.x))
        {
            var st = r.lightmapScaleOffset;
            sb.Append($"{{\"key\":\"renderer {r.name}\",\"value\":[{r.lightmapIndex},{F(st.x)},{F(st.y)},{F(st.z)},{F(st.w)}]}}\n");
        }
        var sphere = GameObject.Find("Dynamic Sphere");
        LightProbes.GetInterpolatedProbe(sphere.transform.position, sphere.GetComponent<Renderer>(), out var sh);
        var coefficients = Enumerable.Range(0, 27).Select(i => F(sh[i / 9, i % 9]));
        sb.Append($"{{\"key\":\"probe at sphere\",\"value\":[{string.Join(",", coefficients)}]}}\n");
        sb.Append($"{{\"key\":\"probes\",\"value\":{LightmapSettings.lightProbes?.count ?? 0}}}\n");
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None).OrderBy(l => l.name))
        {
            var o = l.bakingOutput;
            sb.Append($"{{\"key\":\"bakingOutput {l.name}\",\"value\":\"{o.probeOcclusionLightIndex} {o.occlusionMaskChannel} {o.lightmapBakeType} {o.mixedLightingMode} {o.isBaked}\"}}\n");
        }
        return sb.ToString();
    }
}
