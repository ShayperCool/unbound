using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Records what the scene's baked lighting data gives the scene: each renderer's
/// lightmap index and scale-offset and its realtime ones (floats in round-trip text, so NaN and
/// infinities stay visible), the lightmaps, the ambient probe, the reflection probe's baked cubemap and the
/// default (sky) reflection, then renders the main camera at 160x90 (frame.png) and the sky
/// towards the baked sun (sun.png). Writes values.jsonl and the images into
/// LIGHTING_DATA_NAN_OUT and exits.</summary>
public sealed class LightingDataNanProbe : MonoBehaviour
{
    const int Width = 160, Height = 90;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("LightingDataNanProbe").AddComponent<LightingDataNanProbe>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Texture(Texture t) => t == null ? "null" : $"{t.name} {t.width} mips {t.mipmapCount} {t.dimension}";

    static void Capture(Camera camera, string path)
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(path, image.EncodeToPNG());
    }

    IEnumerator Start()
    {
        // A few frames in: the reflection probes are set up by then.
        for (int i = 0; i < 3; i++) yield return null;
        int code = 0;
        var dir = Environment.GetEnvironmentVariable("LIGHTING_DATA_NAN_OUT");
        try
        {
            Rec("lightmaps", LightmapSettings.lightmaps.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).OrderBy(r => r.name, StringComparer.Ordinal))
            {
                var st = r.lightmapScaleOffset;
                Rec($"renderer {r.name}", $"{r.lightmapIndex} {F(st.x)} {F(st.y)} {F(st.z)} {F(st.w)}");
                var rt = r.realtimeLightmapScaleOffset;
                Rec($"renderer {r.name} realtime", $"{r.realtimeLightmapIndex} {F(rt.x)} {F(rt.y)} {F(rt.z)} {F(rt.w)}");
            }
            var sh = RenderSettings.ambientProbe;
            Rec("ambientProbe", string.Join(" ", Enumerable.Range(0, 27).Select(i => F(sh[i / 9, i % 9]))));
            foreach (var p in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                Rec($"probe {p.name} baked", Texture(p.bakedTexture));
                Rec($"probe {p.name} texture", Texture(p.texture));
            }
            Rec("defaultReflection", Texture(ReflectionProbe.defaultTexture));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                var camera = Camera.main;
                Capture(camera, Path.Combine(dir, "frame.png"));
                // The sky towards the (baked) sun, its disk in view.
                var sun = FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(-sun.transform.forward));
                Capture(camera, Path.Combine(dir, "sun.png"));
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
            else Debug.Log(m_Out.ToString());
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
