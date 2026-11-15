using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors the probe's cubemaps (<see cref="Create"/>) and records it (<see cref="Record"/>).</summary>
public static class ProbeRunner
{
    /// <summary>Face size of every cubemap (a 6:1 strip of square faces).</summary>
    const int Face = 8;

    /// <summary>A texel's channel in 0..1 from its place: a hash, so that blocks hold varied
    /// colours, plus a gradient across the strip.</summary>
    static float Noise(int x, int y, int channel)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(channel * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995u;
        h ^= h >> 15;
        return 0.5f * (h & 1023u) / 1023f + 0.5f * (x + y) / (7f * Face);
    }

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Scenes");
        // Float faces, radiance up to about 6.
        Func<int, int, Color> hdr = (x, y) => new Color(6f * Noise(x, y, 0), 3f * Noise(x, y, 1), 0.5f * Noise(x, y, 2), 1f);
        WriteExr("Assets/Resources/HdrPlain.exr", hdr);
        Cube("Assets/Resources/HdrPlain.exr", TextureImporterCompression.Uncompressed);
        WriteExr("Assets/Resources/HdrCompressed.exr", hdr);
        Cube("Assets/Resources/HdrCompressed.exr", TextureImporterCompression.Compressed);
        WriteExr("Assets/Resources/HdrConvolved.exr", hdr);
        Cube("Assets/Resources/HdrConvolved.exr", TextureImporterCompression.Compressed, TextureImporterCubemapConvolution.Specular);
        // Byte faces: opaque, with alpha, and at high quality.
        Func<int, int, Color> ldr = (x, y) => new Color(Noise(x, y, 0), Noise(x, y, 1), Noise(x, y, 2), 1f);
        WritePng("Assets/Resources/LdrPlain.png", ldr);
        Cube("Assets/Resources/LdrPlain.png", TextureImporterCompression.Uncompressed);
        WritePng("Assets/Resources/LdrCompressed.png", ldr);
        Cube("Assets/Resources/LdrCompressed.png", TextureImporterCompression.Compressed);
        WritePng("Assets/Resources/LdrAlpha.png", (x, y) => new Color(Noise(x, y, 0), Noise(x, y, 1), Noise(x, y, 2), Noise(x, y, 3)));
        Cube("Assets/Resources/LdrAlpha.png", TextureImporterCompression.Compressed);
        WritePng("Assets/Resources/LdrHigh.png", ldr);
        Cube("Assets/Resources/LdrHigh.png", TextureImporterCompression.CompressedHQ);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    /// <summary>A cubemap from the strip, point sampled, clamped, mipmapped.</summary>
    static void Cube(string path, TextureImporterCompression compression,
        TextureImporterCubemapConvolution convolution = TextureImporterCubemapConvolution.None)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureShape = TextureImporterShape.TextureCube;
        importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.cubemapConvolution = convolution;
        importer.SetTextureSettings(settings);
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.textureCompression = compression;
        importer.SaveAndReimport();
    }

    static Texture2D Strip(Func<int, int, Color> texel, TextureFormat format, bool linear)
    {
        var texture = new Texture2D(6 * Face, Face, format, false, linear);
        for (int y = 0; y < Face; y++)
            for (int x = 0; x < 6 * Face; x++)
                texture.SetPixel(x, y, texel(x, y));
        return texture;
    }

    static void WriteExr(string path, Func<int, int, Color> texel)
    {
        var texture = Strip(texel, TextureFormat.RGBAFloat, true);
        File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
    }

    static void WritePng(string path, Func<int, int, Color> texel)
    {
        var texture = Strip(texel, TextureFormat.RGBA32, false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
    }
}
