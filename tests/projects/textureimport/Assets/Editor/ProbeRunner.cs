using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> applies the importer settings of
/// Assets/Editor/TextureCases.txt (written by make_textures.py) to the textures in Assets/Textures
/// and makes the probe scene: a <see cref="TextureImportProbe"/> holding every texture.
/// <c>-executeMethod ProbeRunner.Record</c> with <c>TEXTURE_IMPORT_PROBE_OUT</c> set opens it and
/// enters Play mode, where the probe records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var line in File.ReadAllLines("Assets/Editor/TextureCases.txt"))
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var path = "Assets/Textures/" + parts[0] + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Apply(importer, parts.Skip(1));
            importer.SaveAndReimport();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("TextureImportProbe").AddComponent<TextureImportProbe>();
        probe.textures = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .ToArray();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static void Apply(TextureImporter importer, System.Collections.Generic.IEnumerable<string> settings)
    {
        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        int maxSize = 2048;
        var resize = TextureResizeAlgorithm.Mitchell;
        foreach (var setting in settings)
        {
            if (!setting.StartsWith("maxsize=")) continue;
            var args = setting.Substring("maxsize=".Length).Split(',');
            maxSize = int.Parse(args[0]);
            if (args.Length > 1 && args[1] == "bilinear") resize = TextureResizeAlgorithm.Bilinear;
        }
        var standard = importer.GetDefaultPlatformTextureSettings();
        standard.maxTextureSize = maxSize;
        standard.resizeAlgorithm = resize;
        importer.SetPlatformTextureSettings(standard);
        foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
        {
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = platform,
                overridden = true,
                maxTextureSize = maxSize,
                resizeAlgorithm = resize,
                format = TextureImporterFormat.RGBA32,
            });
        }
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
                case "srgb": importer.sRGBTexture = value == "1"; break;
                case "mips": importer.mipmapEnabled = value == "1"; break;
                case "filter":
                    importer.mipmapFilter = value == "kaiser" ? TextureImporterMipFilter.KaiserFilter : TextureImporterMipFilter.BoxFilter;
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
                case "alpha":
                    importer.alphaSource = value == "none" ? TextureImporterAlphaSource.None
                        : value == "gray" ? TextureImporterAlphaSource.FromGrayScale
                        : TextureImporterAlphaSource.FromInput;
                    break;
                case "npot":
                    importer.npotScale = value == "none" ? TextureImporterNPOTScale.None
                        : value == "larger" ? TextureImporterNPOTScale.ToLarger
                        : value == "smaller" ? TextureImporterNPOTScale.ToSmaller
                        : TextureImporterNPOTScale.ToNearest;
                    break;
                case "grayscale":
                    importer.convertToNormalmap = true;
                    importer.normalmapFilter = args[0] == "sobel" ? TextureImporterNormalFilter.Sobel : TextureImporterNormalFilter.Standard;
                    importer.heightmapScale = F(args[1]);
                    break;
                case "flipgreen": importer.flipGreenChannel = value == "1"; break;
                case "maxsize": break;
                default: throw new ArgumentException("unknown texture case setting " + setting);
            }
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
