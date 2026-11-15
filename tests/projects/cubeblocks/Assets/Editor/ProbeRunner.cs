using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Create: imports every source of Assets/Editor/TextureCases.txt as a cube (6:1 strip) with the
/// case's settings and writes the scene. Record: plays the scene (CubeCompressionProbe writes and
/// quits).
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var line in File.ReadAllLines("Assets/Editor/TextureCases.txt"))
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var path = File.Exists("Assets/Textures/" + parts[0] + ".hdr")
                ? "Assets/Textures/" + parts[0] + ".hdr"
                : "Assets/Textures/" + parts[0] + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Apply(importer, parts.Skip(1).ToArray());
            importer.SaveAndReimport();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("CubeCompressionProbe").AddComponent<CubeCompressionProbe>();
        probe.cubes = AssetDatabase.FindAssets("t:Cubemap", new[] { "Assets/Textures" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Cubemap>)
            .ToArray();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static void Apply(TextureImporter importer, string[] settings)
    {
        string Get(string key) => settings.Where(s => s.StartsWith(key + "=")).Select(s => s.Substring(key.Length + 1)).FirstOrDefault();
        foreach (var setting in settings)
        {
            var key = setting.Split('=')[0];
            if (!new[] { "compression", "convolution", "mips", "srgb" }.Contains(key))
                throw new ArgumentException("unknown texture case setting " + setting);
        }
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.TextureCube;
        var s = new TextureImporterSettings();
        importer.ReadTextureSettings(s);
        s.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
        s.cubemapConvolution = Get("convolution") == "specular" ? TextureImporterCubemapConvolution.Specular : TextureImporterCubemapConvolution.None;
        importer.SetTextureSettings(s);
        importer.isReadable = true;
        importer.mipmapEnabled = Get("mips") != "0";
        importer.sRGBTexture = Get("srgb") != "0";
        var compression = Get("compression");
        var standard = importer.GetDefaultPlatformTextureSettings();
        standard.textureCompression = compression == "high" ? TextureImporterCompression.CompressedHQ
            : compression == "low" ? TextureImporterCompression.CompressedLQ
            : compression == "normal" ? TextureImporterCompression.Compressed
            : TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(standard);
        importer.ClearPlatformTextureSettings("Standalone");
        importer.ClearPlatformTextureSettings("Android");
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
