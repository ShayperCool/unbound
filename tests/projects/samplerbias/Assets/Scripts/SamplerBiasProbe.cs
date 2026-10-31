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
        bool bilinearControl = Environment.GetEnvironmentVariable("SAMPLER_BIAS_BILINEAR") == "1";
        bool liveControl = Environment.GetEnvironmentVariable("SAMPLER_BIAS_LIVE") == "1";
        bool cacheControl = Environment.GetEnvironmentVariable("SAMPLER_BIAS_CACHE") == "1";
        try
        {
            foreach (int width in liveControl || cacheControl ? Array.Empty<int>() : new[] { 4, 64 })
            foreach (TextureFormat format in new[] { TextureFormat.RGBA32, TextureFormat.RGBAHalf, TextureFormat.RFloat })
            foreach (bool readable in new[] { true, false })
            foreach (bool beforeUpload in new[] { true, false })
            foreach (FilterMode filter in bilinearControl ? new[] { FilterMode.Bilinear } : new[] { FilterMode.Point, FilterMode.Trilinear })
            foreach (int aniso in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })
            foreach (float bias in new[] { -1f, -0.75f, -0.5f, -0.25f, 0f, 0.125f, 0.25f, 0.375f, 0.75f })
            {
                if (bilinearControl && !(width == 4 && format == TextureFormat.RFloat && readable && !beforeUpload)) continue;
                if (width != 4 && !(format == TextureFormat.RFloat && readable && !beforeUpload && filter == FilterMode.Trilinear && bias == 0f && (aniso == 1 || aniso == 2 || aniso == 4 || aniso == 8 || aniso == 16))) continue;
                if (aniso != 1 && aniso != 7 && !(format == TextureFormat.RFloat && readable && !beforeUpload && filter != FilterMode.Point && bias == 0f)) continue;
                var texture = new Texture2D(width, width / 2, format, true, true);
                texture.filterMode = filter;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = aniso;
                if (beforeUpload) texture.mipMapBias = bias;
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    int n = Math.Max(1, width >> mip) * Math.Max(1, (width / 2) >> mip);
                    var pixels = new Color[n];
                    for (int i = 0; i < n; i++) pixels[i] = new Color((mip + 1) * (width == 4 ? 0.25f : 0.125f), 0, 0, 1);
                    texture.SetPixels(pixels, mip);
                }
                texture.Apply(false, !readable);
                if (!beforeUpload) texture.mipMapBias = bias;
                string size = width == 4 ? "" : "size64x32/";
                string meta = "state/" + size + format + "/" + readable + "/" + beforeUpload + "/" + filter + "/" + aniso + "/" + F(bias);
                rows.Append("{\"k\":\"").Append(meta).Append("\",\"v\":[").Append((int)texture.filterMode).Append(',').Append(texture.anisoLevel).Append(',').Append(F(texture.mipMapBias)).Append("]}\n");
                material.SetTexture("_ModernTex", texture);
                var levels = width != 4
                    ? new[] { 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 1f, 1.125f, 1.25f, 1.5f, 2f, 2.125f, 2.25f, 2.5f, 3f, 3.125f, 3.25f, 3.5f, 4f, 4.125f, 4.25f, 4.5f, 5f, 5.125f, 5.25f, 5.5f, 6f }
                    : format == TextureFormat.RFloat && readable && !beforeUpload && filter != FilterMode.Point
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
                    string key = size + format + "/" + readable + "/" + beforeUpload + "/" + filter + "/" + aniso + "/" + F(bias) + "/" + mode + "/" + F(level);
                    rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[")
                        .Append(F(value.r)).Append(',').Append(F(value.g)).Append(',').Append(F(value.b)).Append(',').Append(F(value.a)).Append("]}\n");
                }
                DestroyImmediate(texture);
            }
            if (liveControl) RecordLiveControls(rows, material, target, readback);
            if (cacheControl) RecordCacheControls(rows, material, target, readback);
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

    static void RecordLiveControls(StringBuilder rows, Material material, RenderTexture target, Texture2D readback)
    {
        var texture = new Texture2D(4, 2, TextureFormat.RFloat, true, true);
        for (int mip = 0; mip < texture.mipmapCount; mip++)
        {
            var pixels = new Color[Math.Max(1, 4 >> mip) * Math.Max(1, 2 >> mip)];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color((mip + 1) * .25f, 0, 0, 1);
            texture.SetPixels(pixels, mip);
        }
        texture.Apply(false, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        material.SetTexture("_ModernTex", texture);
        var original = QualitySettings.anisotropicFiltering;
        Texture.SetGlobalAnisotropicFilteringLimits(2, 16);
        int sequence = 0;
        foreach (var policy in new[] { AnisotropicFiltering.ForceEnable, AnisotropicFiltering.Disable, AnisotropicFiltering.Enable, AnisotropicFiltering.ForceEnable, AnisotropicFiltering.Disable })
        {
            QualitySettings.anisotropicFiltering = policy;
            foreach (int aniso in new[] { 1, 2, 7, 16 })
            foreach (var filter in new[] { FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear })
            {
                texture.anisoLevel = aniso;
                texture.filterMode = filter;
                int change = 0;
                foreach (float bias in new[] { 0f, -.5f, .125f, 0f, .75f, -.25f })
                {
                    texture.mipMapBias = bias;
                    string prefix = "live/" + sequence + "/" + policy + "/" + aniso + "/" + filter + "/" + change + "/" + F(bias);
                    rows.Append("{\"k\":\"state/").Append(prefix).Append("\",\"v\":[")
                        .Append((int)texture.filterMode).Append(',').Append(texture.anisoLevel).Append(',').Append(F(texture.mipMapBias)).Append("]}\n");
                    for (int mode = 0; mode < 7; mode++)
                    foreach (float level in new[] { .5f, 1f, 1.5f })
                    {
                        material.SetFloat("_Mode", mode); material.SetFloat("_Level", level);
                        Graphics.Blit(texture, target, material);
                        var previous = RenderTexture.active; RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); readback.Apply(); RenderTexture.active = previous;
                        var value = readback.GetPixel(0, 0);
                        rows.Append("{\"k\":\"").Append(prefix).Append('/').Append(mode).Append('/').Append(F(level)).Append("\",\"v\":[")
                            .Append(F(value.r)).Append(',').Append(F(value.g)).Append(',').Append(F(value.b)).Append(',').Append(F(value.a)).Append("]}\n");
                    }
                    change++;
                }
            }
            sequence++;
        }
        QualitySettings.anisotropicFiltering = original;
        DestroyImmediate(texture);
    }

    static void RecordCacheControls(StringBuilder rows, Material material, RenderTexture target, Texture2D readback)
    {
        var original = QualitySettings.anisotropicFiltering;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        var texture = new Texture2D(4, 2, TextureFormat.RGBAFloat, true, true);
        var quad = new Mesh();
        var commands = new UnityEngine.Rendering.CommandBuffer();
        try
        {
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 1;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int w = Math.Max(1, 4 >> mip), h = Math.Max(1, 2 >> mip);
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) pixels[y * w + x] = new Color((x + 1f) / (w + 1f), mip * .25f, y * .25f, 1);
                texture.SetPixels(pixels, mip);
            }
            texture.Apply(false, false);
            quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            quad.uv = new[] { new Vector2(1.125f, .5f), new Vector2(1.125f, .5f), new Vector2(1.125f, .5f), new Vector2(1.125f, .5f) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            material.SetTexture("_ModernTex", texture);
            material.SetFloat("_Mode", 4); material.SetFloat("_Level", .5f);
            commands.SetRenderTarget(target);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
            void Capture(string label)
            {
                Graphics.ExecuteCommandBuffer(commands);
                var previous = RenderTexture.active; RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                RenderTexture.active = previous;
                // CPU read only: no Apply or material edit between the cached draws.
                var value = readback.GetPixel(0, 0);
                rows.Append("{\"k\":\"cache/").Append(label).Append("\",\"v\":[")
                    .Append(F(value.r)).Append(',').Append(F(value.g)).Append(',').Append(F(value.b)).Append(',').Append(F(value.a)).Append("]}\n");
                rows.Append("{\"k\":\"state/cache/").Append(label).Append("\",\"v\":[")
                    .Append((int)texture.filterMode).Append(',').Append((int)texture.wrapMode).Append(',').Append(F(texture.mipMapBias)).Append("]}\n");
            }
            Capture("warm");
            int sequence = 0;
            foreach (var filter in new[] { FilterMode.Trilinear, FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear })
            {
                texture.mipMapBias = 0; Capture("filter/" + sequence + "/armed");
                texture.filterMode = filter; Capture("filter/" + sequence + "/changed");
                sequence++;
            }
            sequence = 0;
            foreach (var wrap in new[] { TextureWrapMode.Repeat, TextureWrapMode.Clamp, TextureWrapMode.Mirror, TextureWrapMode.Clamp })
            {
                texture.mipMapBias = 0; Capture("wrap/" + sequence + "/armed");
                texture.wrapMode = wrap; Capture("wrap/" + sequence + "/changed");
                sequence++;
            }
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                var pixels = new Color[Math.Max(1, 4 >> mip) * Math.Max(1, 2 >> mip)];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.blue;
                texture.SetPixels(pixels, mip);
            }
            texture.filterMode = FilterMode.Point;
            Capture("pixels/unapplied");
            texture.Apply(false, false);
            Capture("pixels/applied");
        }
        finally
        {
            commands.Release(); DestroyImmediate(quad); DestroyImmediate(texture);
            QualitySettings.anisotropicFiltering = original;
        }
    }

    static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}
