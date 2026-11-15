using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Authors the probe's sources (<see cref="Create"/>) and records it (<see cref="Record"/>).
/// </summary>
public static class ProbeRunner
{
    /// <summary>Lightmap intensities along x (both ends of every encoding's range).</summary>
    public static readonly float[] Levels =
        { 0f, 0.002f, 0.01f, 0.03f, 0.07f, 0.12f, 0.2f, 0.33f, 0.5f, 0.7f, 1f, 1.4f, 2f, 2.9f, 4.5f, 9f };

    /// <summary>Lightmap row tints: grey, warm, green, blue, red, bright blue, bright orange, white x5.</summary>
    static readonly Color[] Tints =
    {
        new Color(1f, 1f, 1f), new Color(1f, 0.5f, 0.25f), new Color(0.25f, 1f, 0.5f), new Color(0.5f, 0.25f, 1f),
        new Color(1f, 0f, 0f), new Color(0.1f, 0.6f, 3f), new Color(3f, 1.5f, 0.2f), new Color(5f, 5f, 5f),
    };

    /// <summary>Cube face tints in the strip's order (+X, -X, +Y, -Y, +Z, -Z).</summary>
    static readonly Color[] Faces =
    {
        new Color(1f, 0.6f, 0.3f), new Color(0.3f, 0.6f, 1f), new Color(2f, 2f, 2f),
        new Color(0.2f, 0.2f, 0.2f), new Color(1f, 1f, 1f), new Color(6f, 3f, 1f),
    };

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Scenes");
        WriteExr("Assets/Resources/Lightmap.exr", 16, 8, (x, y) => Tints[y] * Levels[x]);
        var lightmap = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Lightmap.exr");
        lightmap.textureType = TextureImporterType.Lightmap;
        Exact(lightmap);
        // A 6:1 strip of 4x4 faces.
        WriteExr("Assets/Resources/Reflection.exr", 24, 4, (x, y) => Faces[x / 4] * Levels[x % 4 + 4 * y]);
        var reflection = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Reflection.exr");
        reflection.textureShape = TextureImporterShape.TextureCube;
        Exact(reflection);
        // An 8-bit lightmap: bytes 0, 17, ..., 255 along x, row tints as above (clamped).
        var ldr = new Texture2D(16, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 16; x++)
            {
                var c = Tints[y] * (x / 15f);
                ldr.SetPixel(x, y, new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f), 1f));
            }
        File.WriteAllBytes("Assets/Resources/LightmapLdr.png", ldr.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(ldr);
        AssetDatabase.ImportAsset("Assets/Resources/LightmapLdr.png");
        var ldrImporter = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/LightmapLdr.png");
        ldrImporter.textureType = TextureImporterType.Lightmap;
        Exact(ldrImporter);
        // Mipmapped float and 8-bit lightmaps (box-filtered levels, point sampled).
        WriteExr("Assets/Resources/LightmapMips.exr", 16, 8, (x, y) => Tints[y] * Levels[x]);
        var mips = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/LightmapMips.exr");
        mips.textureType = TextureImporterType.Lightmap;
        Mipmapped(mips);
        AssetDatabase.CopyAsset("Assets/Resources/LightmapLdr.png", "Assets/Resources/LightmapLdrMips.png");
        var ldrMips = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/LightmapLdrMips.png");
        ldrMips.textureType = TextureImporterType.Lightmap;
        Mipmapped(ldrMips);
        // The same HDR lightmap and cubemap with the importer's default compression.
        WriteExr("Assets/Resources/LightmapCompressed.exr", 16, 8, (x, y) => Tints[y] * Levels[x]);
        var compressed = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/LightmapCompressed.exr");
        compressed.textureType = TextureImporterType.Lightmap;
        compressed.SaveAndReimport();
        WriteExr("Assets/Resources/ReflectionCompressed.exr", 24, 4, (x, y) => Faces[x / 4] * Levels[x % 4 + 4 * y]);
        var compressedCube = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/ReflectionCompressed.exr");
        compressedCube.textureShape = TextureImporterShape.TextureCube;
        compressedCube.SaveAndReimport();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/Resources/Reflection.exr");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    /// <summary>Point sampled, clamped, one level, uncompressed: every texel reads back as stored.</summary>
    static void Exact(TextureImporter importer)
    {
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static void Mipmapped(TextureImporter importer)
    {
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static void WriteExr(string path, int width, int height, Func<int, int, Color> texel)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = texel(x, y);
                texture.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        File.WriteAllBytes(path, texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
    }

    /// <summary>An encoding quality Player Settings keeps for a platform (a non-public accessor).</summary>
    static object Quality(string name, BuildTarget platform)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        foreach (var method in typeof(PlayerSettings).GetMethods(flags))
        {
            if (method.Name != name || method.GetParameters().Length != 1) continue;
            var type = method.GetParameters()[0].ParameterType;
            if (type == typeof(BuildTarget)) return method.Invoke(null, new object[] { platform });
            if (type == typeof(BuildTargetGroup)) return method.Invoke(null, new object[] { BuildPipeline.GetBuildTargetGroup(platform) });
        }
        return "unavailable";
    }

    /// <summary>Writes the Editor's encoding settings for the active target (settings.json, not
    /// compared), then plays the probe scene.</summary>
    public static void Record()
    {
        var dir = Environment.GetEnvironmentVariable("LIGHTMAP_ENCODING_PROBE_OUT") ?? "/tmp/lightmapencoding";
        Directory.CreateDirectory(dir);
        var target = EditorUserBuildSettings.activeBuildTarget;
        var text = new StringBuilder();
        text.Append("{\"target\":\"").Append(target).Append('"');
        foreach (var platform in new[] { BuildTarget.StandaloneLinux64, BuildTarget.Android, BuildTarget.iOS, BuildTarget.WebGL })
        {
            text.Append(",\"lightmap").Append(platform).Append("\":\"").Append(Quality("GetLightmapEncodingQualityForPlatform", platform)).Append('"');
            text.Append(",\"cubemap").Append(platform).Append("\":\"").Append(Quality("GetHDRCubemapEncodingQualityForPlatform", platform)).Append('"');
        }
        text.Append(",\"colorSpace\":\"").Append(PlayerSettings.colorSpace.ToString()).Append("\"}\n");
        File.WriteAllText(Path.Combine(dir, "settings.json"), text.ToString());
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
