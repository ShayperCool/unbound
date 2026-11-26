using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors the probe's textures and scene (<see cref="Create"/>) and records it
/// (<see cref="Record"/>); see README.md.</summary>
public static class ProbeSetup
{
    const string Resources = "Assets/Resources/";

    /// <summary>A distinct colour per mip (and per array slice).</summary>
    public static Color32 MipColor(int mip, int slice = 0)
    {
        var palette = new[]
        {
            new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 255),
            new Color32(255, 0, 255, 255), new Color32(0, 255, 255, 255), new Color32(255, 128, 0, 255), new Color32(128, 0, 255, 255),
        };
        var c = palette[mip % palette.Length];
        return slice == 0 ? c : new Color32((byte)(c.r / 2), (byte)(c.g / 2), (byte)(c.b / 2), 255);
    }

    /// <summary>Layered checkers: one layer of each period 1, 2, 4 and 8 texels, so each box
    /// filtered mip loses the finest layer left.</summary>
    static Texture2D Checker()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color32[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int v = 128;
                for (int k = 0; k < 4; k++)
                    v += ((((x >> k) + (y >> k)) & 1) == 0 ? 1 : -1) * (64 >> k);
                pixels[y * 64 + x] = new Color32((byte)v, (byte)(255 - v), (byte)(x * 4), 255);
            }
        t.SetPixels32(pixels);
        t.Apply();
        return t;
    }

    static void Png(string name, bool mips, bool ignoreLimit, bool compressed = false)
    {
        string path = Resources + name + ".png";
        File.WriteAllBytes(path, Checker().EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = mips;
        importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = compressed ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        importer.sRGBTexture = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.ignoreMipmapLimit = ignoreLimit;
        importer.SaveAndReimport();
    }

    static void Asset(string name, int width, int height, int mipCount, bool ignoreLimit)
    {
        var t = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, false);
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            int w = Mathf.Max(1, width >> mip), h = Mathf.Max(1, height >> mip);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = MipColor(mip);
            t.SetPixels32(pixels, mip);
        }
        t.Apply(false);
        t.filterMode = FilterMode.Point;
        t.ignoreMipmapLimit = ignoreLimit;
        AssetDatabase.CreateAsset(t, Resources + name + ".asset");
    }

    static void ArrayAsset(string name, bool ignoreLimit)
    {
        var t = new Texture2DArray(64, 64, 2, TextureFormat.RGBA32, true);
        for (int slice = 0; slice < 2; slice++)
            for (int mip = 0; mip < t.mipmapCount; mip++)
            {
                int s = Mathf.Max(1, 64 >> mip);
                var pixels = new Color32[s * s];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = MipColor(mip, slice);
                t.SetPixels32(pixels, slice, mip);
            }
        t.Apply(false);
        t.filterMode = FilterMode.Point;
        t.ignoreMipmapLimit = ignoreLimit;
        AssetDatabase.CreateAsset(t, Resources + name + ".asset");
    }

    public static void Create()
    {
        Directory.CreateDirectory(Resources);
        Directory.CreateDirectory("Assets/Scenes");
        Png("png", true, false);
        Png("pngIgnore", true, true);
        Png("pngNoMips", false, false);
        Png("pngCompressed", true, false, true);
        Asset("asset", 64, 64, -1, false);
        Asset("assetIgnore", 64, 64, -1, true);
        Asset("small16", 16, 16, -1, false);
        Asset("small8", 8, 8, -1, false);
        Asset("small4", 4, 4, -1, false);
        Asset("small2", 2, 2, -1, false);
        Asset("chain3", 64, 64, 3, false);
        Asset("chain5", 64, 64, 5, false);
        Asset("wide", 64, 8, -1, false);
        Asset("tall", 8, 64, -1, false);
        ArrayAsset("array", false);
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Probe").AddComponent<MipmapLimitProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
