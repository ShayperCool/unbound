using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> applies the importer settings of
/// Assets/Editor/TextureCases.txt (written by make_textures.py) to the PNGs in Assets/Textures,
/// makes the Texture3D, Texture2DArray and CubemapArray <c>.asset</c> cases, the sampling
/// materials and the probe scene, then exits (1 on failure).
/// <c>-executeMethod ProbeRunner.Record</c> with <c>TEXTURE_ARRAY_PROBE_OUT</c> set opens the
/// scene and enters Play mode, where the probe records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    static readonly string[] Samples =
    {
        "ArrayNoMips 0", "ArrayNoMips 5", "VolumeWrapClamp 2", "VolumeWrapClamp 9", "VolumeWrapClamp -2",
        "VolumeWrapRepeat 1", "VolumeWrapRepeat 9", "VolumeWrapRepeat -2", "AssetVolumeRgba32 2",
        "AssetArrayRgba32 1", "AssetArrayLinear 1",
        // Lower levels of a volume deeper than wide, and of an array with as many slices.
        "VolumeDeep 5 2", "VolumeDeep 9 4", "ArrayDeep 5 1", "ArrayDeep 5 2", "ArrayDeep 5 4",
    };

    public static void Create()
    {
        try
        {
            foreach (var line in File.ReadAllLines("Assets/Editor/TextureCases.txt"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/" + parts[0] + ".png");
                Apply(importer, parts.Skip(1).ToArray());
                importer.SaveAndReimport();
            }
            CreateAssets();
            var arrayMaterial = new Material(Shader.Find("Probe/SampleArray"));
            AssetDatabase.CreateAsset(arrayMaterial, "Assets/Shaders/SampleArray.mat");
            var volumeMaterial = new Material(Shader.Find("Probe/SampleVolume"));
            AssetDatabase.CreateAsset(volumeMaterial, "Assets/Shaders/SampleVolume.mat");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var probe = new GameObject("TextureArrayProbe").AddComponent<TextureArrayProbe>();
            probe.textures = AssetDatabase.FindAssets("t:Texture", new[] { "Assets/Textures" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadMainAssetAtPath)
                .OfType<Texture>()
                .ToArray();
            probe.arrayMaterial = arrayMaterial;
            probe.volumeMaterial = volumeMaterial;
            probe.samples = Samples;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ProbeRunner.Create failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    // Deterministic bytes (the same LCG as make_textures.py).
    sealed class Lcg
    {
        uint m_State;
        public Lcg(uint seed) { m_State = seed; }
        public byte Byte()
        {
            m_State = (m_State * 1103515245u + 12345u) & 0x7fffffffu;
            return (byte)((m_State >> 16) & 0xff);
        }
    }

    static Color32[] Noise(Lcg rng, int count)
    {
        var pixels = new Color32[count];
        for (int i = 0; i < count; i++) pixels[i] = new Color32(rng.Byte(), rng.Byte(), rng.Byte(), rng.Byte());
        return pixels;
    }

    static void Save(UnityEngine.Object texture, string name, bool readable = true)
    {
        var path = "Assets/Textures/" + name + ".asset";
        // Assets made by an earlier run keep their GUID and (deterministic) contents.
        if (File.Exists(path)) return;
        AssetDatabase.CreateAsset(texture, path);
        if (!readable)
        {
            var serialized = new SerializedObject(texture);
            serialized.FindProperty("m_IsReadable").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(texture);
        }
    }

    static void CreateAssets()
    {
        var rng = new Lcg(31);

        var volume = new Texture3D(8, 8, 4, TextureFormat.RGBA32, true)
        {
            filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Clamp,
            wrapModeV = TextureWrapMode.Repeat, wrapModeW = TextureWrapMode.Mirror,
        };
        volume.SetPixels32(Noise(rng, 8 * 8 * 4));
        volume.Apply(true, false);
        Save(volume, "AssetVolumeRgba32");

        var half = new Texture3D(4, 4, 4, TextureFormat.RGBAHalf, false) { wrapModeW = TextureWrapMode.Clamp };
        var colors = new Color[64];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Color(rng.Byte() / 64f, rng.Byte() / 128f - 1f, rng.Byte() / 255f, rng.Byte() / 32f);
        half.SetPixels(colors);
        half.Apply(false, false);
        Save(half, "AssetVolumeHalf");

        var srgbVolume = new Texture3D(8, 4, 2, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.MipChain)
        {
            filterMode = FilterMode.Trilinear, anisoLevel = 3,
        };
        srgbVolume.SetPixels32(Noise(rng, 8 * 4 * 2));
        srgbVolume.Apply(true, false);
        Save(srgbVolume, "AssetVolumeSrgb");

        var unreadableVolume = new Texture3D(4, 4, 2, TextureFormat.RGBA32, false);
        unreadableVolume.SetPixels32(Noise(rng, 4 * 4 * 2));
        unreadableVolume.Apply(false, false);
        Save(unreadableVolume, "AssetVolumeUnreadable", readable: false);

        var array = new Texture2DArray(16, 8, 3, TextureFormat.RGBA32, true, false)
        {
            filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Mirror, wrapModeV = TextureWrapMode.Clamp,
        };
        for (int e = 0; e < 3; e++) array.SetPixels32(Noise(rng, 16 * 8), e);
        array.Apply(true, false);
        Save(array, "AssetArrayRgba32");

        var linear = new Texture2DArray(8, 8, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        for (int e = 0; e < 2; e++) linear.SetPixels32(Noise(rng, 8 * 8), e);
        linear.Apply(false, false);
        Save(linear, "AssetArrayLinear");

        var unreadable = new Texture2DArray(8, 8, 2, TextureFormat.RGBA32, false, true);
        for (int e = 0; e < 2; e++) unreadable.SetPixels32(Noise(rng, 8 * 8), e);
        unreadable.Apply(false, false);
        Save(unreadable, "AssetArrayUnreadable", readable: false);

        var r8 = new Texture2DArray(8, 4, 2, TextureFormat.R8, false, true);
        for (int e = 0; e < 2; e++) r8.SetPixels32(Noise(rng, 8 * 4), e);
        r8.Apply(false, false);
        Save(r8, "AssetArrayR8");

        var cubes = new CubemapArray(8, 2, TextureFormat.RGBA32, true) { filterMode = FilterMode.Point };
        for (int e = 0; e < 2; e++)
            for (int f = 0; f < 6; f++) cubes.SetPixels32(Noise(rng, 8 * 8), (CubemapFace)f, e);
        cubes.Apply(true, false);
        Save(cubes, "AssetCubeArray");

        var halfCubes = new CubemapArray(4, 1, TextureFormat.RGBAHalf, false, true);
        for (int f = 0; f < 6; f++)
        {
            var face = new Color[16];
            for (int i = 0; i < 16; i++) face[i] = new Color(rng.Byte() / 32f, rng.Byte() / 255f, rng.Byte() / 128f, 1);
            halfCubes.SetPixels(face, (CubemapFace)f, 0);
        }
        halfCubes.Apply(false, false);
        Save(halfCubes, "AssetCubeArrayHalf");
        AssetDatabase.SaveAssets();
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static TextureWrapMode Wrap(string s) => s switch
    {
        "clamp" => TextureWrapMode.Clamp,
        "mirror" => TextureWrapMode.Mirror,
        "mirroronce" => TextureWrapMode.MirrorOnce,
        _ => TextureWrapMode.Repeat,
    };

    static void Apply(TextureImporter importer, string[] settings)
    {
        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        int maxSize = 2048;
        foreach (var setting in settings)
            if (setting.StartsWith("maxsize=")) maxSize = int.Parse(setting.Substring("maxsize=".Length));
        var standard = importer.GetDefaultPlatformTextureSettings();
        standard.maxTextureSize = maxSize;
        importer.SetPlatformTextureSettings(standard);
        foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
        {
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = platform,
                overridden = true,
                maxTextureSize = maxSize,
                format = TextureImporterFormat.RGBA32,
            });
        }
        var shape = TextureImporterShape.Texture2D;
        int columns = 1, rows = 1;
        foreach (var setting in settings)
        {
            var kv = setting.Split('=');
            var value = kv[1];
            var args = value.Split(',');
            switch (kv[0])
            {
                case "type":
                    importer.textureType = value == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    break;
                case "shape":
                    shape = value == "array" ? TextureImporterShape.Texture2DArray
                        : value == "volume" ? TextureImporterShape.Texture3D
                        : value == "cube" ? TextureImporterShape.TextureCube
                        : TextureImporterShape.Texture2D;
                    break;
                case "cells":
                    columns = int.Parse(args[0]);
                    rows = int.Parse(args[1]);
                    break;
                case "srgb": importer.sRGBTexture = value == "1"; break;
                case "mips": importer.mipmapEnabled = value == "1"; break;
                case "readable": importer.isReadable = value == "1"; break;
                case "filter":
                    importer.mipmapFilter = value == "kaiser" ? TextureImporterMipFilter.KaiserFilter : TextureImporterMipFilter.BoxFilter;
                    break;
                case "filtermode":
                    importer.filterMode = value == "point" ? FilterMode.Point : value == "trilinear" ? FilterMode.Trilinear : FilterMode.Bilinear;
                    break;
                case "wrap":
                    importer.wrapModeU = Wrap(args[0]);
                    importer.wrapModeV = Wrap(args[1]);
                    importer.wrapModeW = Wrap(args[2]);
                    break;
                case "fade":
                    importer.fadeout = true;
                    importer.mipmapFadeDistanceStart = int.Parse(args[0]);
                    importer.mipmapFadeDistanceEnd = int.Parse(args[1]);
                    break;
                case "border": importer.borderMipmap = value == "1"; break;
                case "coverage":
                    importer.mipMapsPreserveCoverage = true;
                    importer.alphaTestReferenceValue = F(value);
                    break;
                case "transparency": importer.alphaIsTransparency = value == "1"; break;
                case "npot":
                    importer.npotScale = value == "none" ? TextureImporterNPOTScale.None
                        : value == "larger" ? TextureImporterNPOTScale.ToLarger
                        : value == "smaller" ? TextureImporterNPOTScale.ToSmaller
                        : TextureImporterNPOTScale.ToNearest;
                    break;
                case "maxsize": break;
                default: throw new ArgumentException("unknown texture case setting " + setting);
            }
        }
        importer.textureShape = shape;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.flipbookColumns = columns;
        textureSettings.flipbookRows = rows;
        importer.SetTextureSettings(textureSettings);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
