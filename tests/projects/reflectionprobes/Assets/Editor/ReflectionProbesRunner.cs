using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/reflectionprobes -executeMethod ReflectionProbesRunner.Setup`
/// wrote <c>Assets/Cubemaps</c> (strip images with their import settings) and <c>Assets/Scenes/Probe.unity</c>;
/// `-executeMethod ReflectionProbesRunner.Record` (with <c>REFLECTION_PROBES_OUT</c>) opens the scene and
/// enters Play mode, where <see cref="ReflectionProbesProbe"/> records and exits the Editor.
/// </summary>
public static class ReflectionProbesRunner
{
    const string Folder = "Assets/Cubemaps";

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    public static void Setup()
    {
        Directory.CreateDirectory(Folder);
        var noise32 = Strip("Noise32", 32, Noise, true, true, true);
        var noise16 = Strip("Noise16", 16, Noise, true, true, true);
        var srgb = Strip("NoiseSrgb32", 32, Noise, false, true, true);
        var linear = Strip("NoiseLinear32", 32, Noise, false, true, false);
        var environment = Strip("Environment64", 64, Environment, true, true, true);
        var sky = Strip("Sky32", 32, (f, x, y, n) => { var c = Noise(f, x, y, n); return new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f), 1f); }, true, false, true);

        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var probe = new GameObject("Probe").AddComponent<ReflectionProbesProbe>();
        probe.imported = new[] { noise32, noise16, srgb, linear };
        probe.environment = environment;
        probe.sky = sky;
        probe.constants = Shader.Find("Probe/SHDump");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    delegate Color Pattern(int face, int x, int y, int size);

    /// <summary>A 6:1 strip (faces in Unity's order, rows from the top) imported as a cubemap,
    /// readable, uncompressed, glossy-convolved when <paramref name="convolve"/>.</summary>
    static Cubemap Strip(string name, int size, Pattern pattern, bool hdr, bool convolve, bool srgb)
    {
        var texture = new Texture2D(size * 6, size, hdr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, true);
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = pattern(face, x, y, size);
                    if (!hdr) c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                    texture.SetPixel(face * size + x, size - 1 - y, c);
                }
        texture.Apply();
        string path = $"{Folder}/{name}.{(hdr ? "exr" : "png")}";
        File.WriteAllBytes(path, hdr ? texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP) : texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureShape = TextureImporterShape.TextureCube;
        importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.cubemapConvolution = convolve ? TextureImporterCubemapConvolution.Specular : TextureImporterCubemapConvolution.None;
        settings.mipmapEnabled = true;
        settings.readable = true;
        settings.sRGBTexture = srgb;
        settings.filterMode = FilterMode.Trilinear;
        importer.SetTextureSettings(settings);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var platform = importer.GetDefaultPlatformTextureSettings();
        platform.format = hdr ? TextureImporterFormat.RGBAFloat : TextureImporterFormat.RGBA32;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    /// <summary>Hashed texels in 0..1 with a few bright ones (up to 8) as HDR highlights.</summary>
    static Color Noise(int face, int x, int y, int size)
    {
        uint h = (uint)(face * 7919 + y * 131 + x * 31 + 17);
        h ^= h << 13; h ^= h >> 17; h ^= h << 5;
        float r = (h & 1023) / 1023f, g = ((h >> 10) & 1023) / 1023f, b = ((h >> 20) & 1023) / 1023f;
        float boost = (h >> 30) == 3 && ((h >> 7) & 7) == 0 ? 8f : 1f;
        return new Color(r * boost, g * boost, b * boost, 1f);
    }

    /// <summary>A sky gradient over a checkered ground with a bright sun on +Z.</summary>
    static Color Environment(int face, int x, int y, int size)
    {
        float sc = (x + 0.5f) / size * 2f - 1f, tc = (y + 0.5f) / size * 2f - 1f;
        Vector3 d = face switch
        {
            0 => new Vector3(1f, -tc, -sc),
            1 => new Vector3(-1f, -tc, sc),
            2 => new Vector3(sc, 1f, tc),
            3 => new Vector3(sc, -1f, -tc),
            4 => new Vector3(sc, -tc, 1f),
            _ => new Vector3(-sc, -tc, -1f),
        };
        d.Normalize();
        if (Vector3.Dot(d, new Vector3(0.3f, 0.5f, 0.81f).normalized) > 0.985f) return new Color(12f, 10f, 7f, 1f);
        if (d.y >= 0f) return Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(0.15f, 0.35f, 0.9f), d.y);
        bool check = ((Mathf.FloorToInt(d.x / -d.y * 2f) + Mathf.FloorToInt(d.z / -d.y * 2f)) & 1) == 0;
        return check ? new Color(0.5f, 0.35f, 0.2f) : new Color(0.1f, 0.12f, 0.08f);
    }
}
