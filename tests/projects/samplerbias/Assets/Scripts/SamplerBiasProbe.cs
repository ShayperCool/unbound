using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Reuses the existing UI fixture's scene/component GUID. This script replaces its behaviour.
public class UitkProbe : MonoBehaviour
{
    public UnityEngine.UIElements.UIDocument document;
    public Texture2D picture;
    void Start()
    {
        var output = Environment.GetEnvironmentVariable("SAMPLER_BIAS_OUT");
        Directory.CreateDirectory(output);
        var rows = new StringBuilder();
        var material = new Material(Shader.Find("Probe/SamplerBias"));
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            foreach (TextureFormat format in new[] { TextureFormat.RGBA32, TextureFormat.RGBAHalf, TextureFormat.RFloat })
            foreach (bool readable in new[] { true, false })
            foreach (bool beforeUpload in new[] { true, false })
            foreach (FilterMode filter in new[] { FilterMode.Point, FilterMode.Trilinear })
            foreach (int aniso in new[] { 1, 7 })
            foreach (float bias in new[] { -1f, -0.75f, -0.5f, -0.25f, 0f, 0.125f, 0.25f, 0.375f, 0.75f })
            {
                var texture = new Texture2D(4, 2, format, true, true);
                texture.filterMode = filter;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = aniso;
                if (beforeUpload) texture.mipMapBias = bias;
                for (int mip = 0; mip < 3; mip++)
                {
                    int n = Math.Max(1, 4 >> mip) * Math.Max(1, 2 >> mip);
                    var pixels = new Color[n];
                    for (int i = 0; i < n; i++) pixels[i] = new Color((mip + 1) * 0.25f, 0, 0, 1);
                    texture.SetPixels(pixels, mip);
                }
                texture.Apply(false, !readable);
                if (!beforeUpload) texture.mipMapBias = bias;
                string meta = "state/" + format + "/" + readable + "/" + beforeUpload + "/" + filter + "/" + aniso + "/" + F(bias);
                rows.Append("{\"k\":\"").Append(meta).Append("\",\"v\":[").Append((int)texture.filterMode).Append(',').Append(texture.anisoLevel).Append(',').Append(F(texture.mipMapBias)).Append("]}\n");
                material.SetTexture("_ModernTex", texture);
                var levels = format == TextureFormat.RFloat && readable && !beforeUpload && filter == FilterMode.Trilinear && aniso == 1
                    ? new[] { -0.5f, 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f, 1.25f, 1.5f, 1.75f, 2f }
                    : new[] { -0.5f, 0f, 0.5f, 1f, 2f };
                for (int mode = 0; mode < 7; mode++)
                foreach (float level in levels)
                {
                    material.SetFloat("_Mode", mode);
                    material.SetFloat("_Level", level);
                    Graphics.Blit(texture, target, material);
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); readback.Apply();
                    RenderTexture.active = previous;
                    var value = readback.GetPixel(0, 0);
                    string key = format + "/" + readable + "/" + beforeUpload + "/" + filter + "/" + aniso + "/" + F(bias) + "/" + mode + "/" + F(level);
                    rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[")
                        .Append(F(value.r)).Append(',').Append(F(value.g)).Append(',').Append(F(value.b)).Append(',').Append(F(value.a)).Append("]}\n");
                }
                DestroyImmediate(texture);
            }
            File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
            File.WriteAllText(Path.Combine(output, "gpu.txt"), SystemInfo.graphicsDeviceName);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString());
            Debug.LogException(e);
        }
        DestroyImmediate(material); DestroyImmediate(readback); target.Release(); DestroyImmediate(target);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}
