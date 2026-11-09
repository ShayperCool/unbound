using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> applies the importer settings of
/// Assets/Editor/TextureCases.txt (written by make_textures.py) to the textures in Assets/Textures
/// and makes the probe scene: a <see cref="TextureCompressionProbe"/> holding every texture.
/// <c>-executeMethod ProbeRunner.Record</c> with <c>TEXTURE_COMPRESSION_PROBE_OUT</c> set opens it
/// and enters Play mode, where the probe records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var line in File.ReadAllLines("Assets/Editor/TextureCases.txt"))
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var path = File.Exists("Assets/Textures/" + parts[0] + ".exr")
                ? "Assets/Textures/" + parts[0] + ".exr"
                : "Assets/Textures/" + parts[0] + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Apply(importer, parts.Skip(1).ToArray());
            importer.SaveAndReimport();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("TextureCompressionProbe").AddComponent<TextureCompressionProbe>();
        probe.textures = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
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
            if (!new[] { "compression", "quality", "format", "type", "channel", "srgb", "mips", "readable", "alpha", "npot" }.Contains(key))
                throw new ArgumentException("unknown texture case setting " + setting);
        }
        importer.textureType = Get("type") == "normal" ? TextureImporterType.NormalMap
            : Get("type") == "single" ? TextureImporterType.SingleChannel
            : TextureImporterType.Default;
        if (Get("channel") != null)
        {
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.singleChannelComponent = Get("channel") == "alpha" ? TextureImporterSingleChannelComponent.Alpha : TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(s);
        }
        importer.isReadable = Get("readable") != "0";
        importer.mipmapEnabled = Get("mips") != "0";
        importer.sRGBTexture = Get("srgb") != "0";
        importer.alphaSource = Get("alpha") == "none" ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
        importer.npotScale = Get("npot") == "none" ? TextureImporterNPOTScale.None : TextureImporterNPOTScale.ToNearest;
        var compression = Get("compression");
        int quality = Get("quality") != null ? int.Parse(Get("quality")) : 50;
        var standard = importer.GetDefaultPlatformTextureSettings();
        standard.textureCompression = compression == "high" ? TextureImporterCompression.CompressedHQ
            : compression == "low" ? TextureImporterCompression.CompressedLQ
            : compression == "normal" ? TextureImporterCompression.Compressed
            : TextureImporterCompression.Uncompressed;
        standard.compressionQuality = quality;
        standard.crunchedCompression = false;
        importer.SetPlatformTextureSettings(standard);
        var format = Get("format");
        if (format != null)
        {
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 2048,
                format = (TextureImporterFormat)Enum.Parse(typeof(TextureImporterFormat), format),
                compressionQuality = quality,
                textureCompression = TextureImporterCompression.Compressed,
            });
        }
        else
        {
            importer.ClearPlatformTextureSettings("Standalone");
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
