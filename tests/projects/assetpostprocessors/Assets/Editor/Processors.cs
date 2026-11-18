using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// The probe's postprocessors. Each callback appends one line to the events file of the current
/// phase in the output folder (it survives domain reloads): the processor, the callback, the
/// asset path and what the callback saw. Some callbacks also change the import (see README.md).
/// </summary>
static class PPLog
{
    internal const string Variable = "ASSET_POSTPROCESSORS_OUT";

    internal static string Out => Environment.GetEnvironmentVariable(Variable);

    /// <summary>The phase the Editor-only steps set (no file: the import of the opened project).</summary>
    internal static string Phase
    {
        get
        {
            var file = Path.Combine(Out, "phase.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : "import";
        }
    }

    internal static void Event(object processor, string callback, string path, string detail)
    {
        if (string.IsNullOrEmpty(Out)) return;
        var name = processor is Type t ? t.Name : processor.GetType().Name;
        var playing = EditorApplication.isPlayingOrWillChangePlaymode ? "play" : "edit";
        var line = name + "\t" + callback + "\t" + path + "\t" + playing + "\t" + (detail ?? "").Replace("\n", " ").Replace("\t", " ") + "\n";
        File.AppendAllText(Path.Combine(Out, "events-" + Phase + ".txt"), line);
    }

    internal static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string N(Enum e) => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    static string B(bool b) => b ? "1" : "0";

    /// <summary>The settings an importer gives (the same for callbacks and for GetAtPath).</summary>
    internal static string Settings(AssetImporter importer)
    {
        if (importer == null) return "null";
        var head = importer.GetType().Name + " missing=" + B(importer.importSettingsMissing);
        switch (importer)
        {
            case TextureImporter t:
                return head + " type=" + N(t.textureType) + " max=" + t.maxTextureSize + " filter=" + N(t.filterMode)
                    + " wrap=" + N(t.wrapMode) + " mips=" + B(t.mipmapEnabled) + " readable=" + B(t.isReadable)
                    + " srgb=" + B(t.sRGBTexture) + " compression=" + N(t.textureCompression) + " npot=" + N(t.npotScale)
                    + " alphaIsTransparency=" + B(t.alphaIsTransparency) + " alphaSource=" + N(t.alphaSource)
                    + " aniso=" + t.anisoLevel + " spriteMode=" + N(t.spriteImportMode) + " ppu=" + F(t.spritePixelsPerUnit);
            case ModelImporter m:
                return head + " scale=" + F(m.globalScale) + " fileScale=" + B(m.useFileScale) + " anim=" + B(m.importAnimation)
                    + " materials=" + N(m.materialImportMode) + " readable=" + B(m.isReadable) + " collider=" + B(m.addCollider)
                    + " rig=" + N(m.animationType) + " blendShapes=" + B(m.importBlendShapes) + " meshCompression=" + N(m.meshCompression)
                    + " normals=" + N(m.importNormals) + " cameras=" + B(m.importCameras) + " lights=" + B(m.importLights)
                    + " uv2=" + B(m.generateSecondaryUV);
            case AudioImporter a:
                var s = a.defaultSampleSettings;
                return head + " mono=" + B(a.forceToMono) + " background=" + B(a.loadInBackground) + " ambisonic=" + B(a.ambisonic)
                    + " load=" + N(s.loadType) + " format=" + N(s.compressionFormat) + " quality=" + F(s.quality)
                    + " rate=" + N(s.sampleRateSetting) + " rateOverride=" + s.sampleRateOverride + " preload=" + B(s.preloadAudioData);
            default:
                return head;
        }
    }

    internal static string Context(AssetImportContext context) =>
        context == null ? "context=null" : "context=" + context.assetPath + " target=" + N(context.selectedBuildTarget);

    internal static string Texture(Texture2D t) =>
        t.name + " " + t.width + "x" + t.height + " format=" + N(t.format) + " mips=" + t.mipmapCount
        + " filter=" + N(t.filterMode) + " wrap=" + N(t.wrapMode) + " readable=" + B(t.isReadable);

    internal static string Hierarchy(Transform t)
    {
        var parts = t.Cast<Transform>().Select(c => c.name);
        return t.name + "[" + string.Join(",", parts) + "] components=" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name));
    }

    internal static string Clip(AudioClip c) =>
        c.name + " channels=" + c.channels + " frequency=" + c.frequency + " samples=" + c.samples + " load=" + N(c.loadType)
        + " ambisonic=" + B(c.ambisonic) + " preload=" + B(c.preloadAudioData) + " background=" + B(c.loadInBackground);
}

// Declared before PPEarly and the ties so that declaration order and name order differ from the
// postprocess order.
class PPLate : AssetPostprocessor
{
    public override int GetPostprocessOrder() => 5;

    void OnPreprocessAsset() => PPLog.Event(this, "OnPreprocessAsset", assetPath, PPLog.Context(context));

    void OnPreprocessTexture()
    {
        var importer = (TextureImporter)assetImporter;
        PPLog.Event(this, "OnPreprocessTexture", assetPath, PPLog.Settings(importer));
        if (assetPath.EndsWith("Shrink.png"))
        {
            importer.maxTextureSize = 32;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
        if (assetPath.EndsWith("Sprite.png")) importer.spritePixelsPerUnit = 50;
    }

    void OnPostprocessTexture(Texture2D texture)
    {
        var detail = PPLog.Texture(texture);
        if (assetPath.EndsWith("Invert.png")) detail += " first=" + texture.GetPixel(0, 0);
        PPLog.Event(this, "OnPostprocessTexture", assetPath, detail);
    }

    void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        PPLog.Event(this, "OnPreprocessModel", assetPath, PPLog.Settings(importer));
        if (assetPath.EndsWith("Materials.fbx"))
        {
            importer.addCollider = true;
            importer.isReadable = true;
        }
    }

    void OnPostprocessModel(GameObject root)
    {
        PPLog.Event(this, "OnPostprocessModel", assetPath, PPLog.Hierarchy(root.transform));
        if (assetPath.EndsWith("Materials.fbx"))
        {
            var added = new GameObject("Added");
            added.transform.SetParent(root.transform, false);
            added.transform.localPosition = new Vector3(1, 2, 3);
            added.AddComponent<BoxCollider>().size = new Vector3(4, 5, 6);
        }
    }

    void OnPreprocessAudio()
    {
        var importer = (AudioImporter)assetImporter;
        PPLog.Event(this, "OnPreprocessAudio", assetPath, PPLog.Settings(importer));
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        importer.defaultSampleSettings = settings;
    }

    void OnPostprocessAudio(AudioClip clip) => PPLog.Event(this, "OnPostprocessAudio", assetPath, PPLog.Clip(clip));
}

class PPTieB : AssetPostprocessor
{
    void OnPreprocessAsset() => PPLog.Event(this, "OnPreprocessAsset", assetPath, "");
    void OnPreprocessTexture() => PPLog.Event(this, "OnPreprocessTexture", assetPath, "");
}

class PPEarly : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -5;

    void OnPreprocessAsset() => PPLog.Event(this, "OnPreprocessAsset", assetPath, PPLog.Settings(assetImporter));

    void OnPreprocessTexture()
    {
        var importer = (TextureImporter)assetImporter;
        PPLog.Event(this, "OnPreprocessTexture", assetPath, PPLog.Context(context) + " same=" + (ReferenceEquals(importer, AssetImporter.GetAtPath(assetPath)) ? "1" : "0"));
        if (assetPath.EndsWith("Shrink.png"))
        {
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
        if (assetPath.EndsWith("Sprite.png"))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
        }
    }

    void OnPostprocessTexture(Texture2D texture)
    {
        PPLog.Event(this, "OnPostprocessTexture", assetPath, PPLog.Texture(texture));
        if (!assetPath.EndsWith("Invert.png")) return;
        // The Scripting API's example: every mip level's colours inverted.
        for (int m = 0; m < texture.mipmapCount; m++)
        {
            var c = texture.GetPixels(m);
            for (int i = 0; i < c.Length; i++)
            {
                c[i].r = 1 - c[i].r;
                c[i].g = 1 - c[i].g;
                c[i].b = 1 - c[i].b;
            }
            texture.SetPixels(c, m);
        }
    }

    void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        PPLog.Event(this, "OnPreprocessModel", assetPath, PPLog.Context(context));
        if (assetPath.EndsWith("Materials.fbx")) importer.globalScale = 2;
    }

    void OnPostprocessModel(GameObject root) => PPLog.Event(this, "OnPostprocessModel", assetPath, PPLog.Hierarchy(root.transform));

    void OnPreprocessAudio()
    {
        PPLog.Event(this, "OnPreprocessAudio", assetPath, PPLog.Context(context));
        ((AudioImporter)assetImporter).forceToMono = true;
    }

    void OnPostprocessAudio(AudioClip clip) => PPLog.Event(this, "OnPostprocessAudio", assetPath, PPLog.Clip(clip));
}

class PPTieA : AssetPostprocessor
{
    void OnPreprocessAsset() => PPLog.Event(this, "OnPreprocessAsset", assetPath, "");
    void OnPreprocessTexture() => PPLog.Event(this, "OnPreprocessTexture", assetPath, "");
}

/// <summary>The model callbacks between OnPreprocessModel and OnPostprocessModel, the sprite
/// callback and the ones the probe's files may not trigger (recorded when they do).</summary>
class PPParts : AssetPostprocessor
{
    void OnPostprocessMeshHierarchy(GameObject root)
    {
        PPLog.Event(this, "OnPostprocessMeshHierarchy", assetPath, PPLog.Hierarchy(root.transform));
        var renamed = root.transform.Find("Cube_NoMat");
        if (renamed != null) renamed.name = "Renamed";
    }

    Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        PPLog.Event(this, "OnAssignMaterialModel", assetPath, (material != null ? material.name : "null") + " " + (renderer != null ? renderer.name : "null"));
        return renderer != null && renderer.name == "Cube_Red" ? AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Override.mat") : null;
    }

    void OnPostprocessMaterial(Material material)
    {
        PPLog.Event(this, "OnPostprocessMaterial", assetPath, material.name + " shader=" + material.shader.name + " color=" + material.color);
        if (material.name == "M_Alpha") material.color = Color.green;
    }

    void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips) =>
        PPLog.Event(this, "OnPreprocessMaterialDescription", assetPath, description.materialName + " clips=" + clips.Length);

    void OnPreprocessAnimation() => PPLog.Event(this, "OnPreprocessAnimation", assetPath, "");

    void OnPostprocessAnimation(GameObject root, AnimationClip clip) =>
        PPLog.Event(this, "OnPostprocessAnimation", assetPath, root.name + " " + clip.name + " length=" + PPLog.F(clip.length));

    void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values) =>
        PPLog.Event(this, "OnPostprocessGameObjectWithUserProperties", assetPath, go.name + " " + string.Join(",", names));

    void OnPreprocessCameraDescription(CameraDescription description, Camera camera, AnimationClip[] clips) =>
        PPLog.Event(this, "OnPreprocessCameraDescription", assetPath, camera.name);

    void OnPreprocessLightDescription(LightDescription description, Light light, AnimationClip[] clips) =>
        PPLog.Event(this, "OnPreprocessLightDescription", assetPath, light.name);

    void OnPostprocessSprites(Texture2D texture, Sprite[] sprites) =>
        PPLog.Event(this, "OnPostprocessSprites", assetPath, texture.name + " " + string.Join(",", sprites.Select(s => s.name + ":" + PPLog.F(s.pixelsPerUnit))));

    void OnPostprocessCubemap(Cubemap texture) => PPLog.Event(this, "OnPostprocessCubemap", assetPath, texture.name);

    void OnPostprocessPrefab(GameObject root) => PPLog.Event(this, "OnPostprocessPrefab", assetPath, root.name);
}

class PPAllZ : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
    {
        PPLog.Event(typeof(PPAllZ), "OnPostprocessAllAssets", "", PostprocessorProbe.AllAssets(imported, deleted, moved, movedFrom) + " reload=" + (didDomainReload ? "1" : "0"));
        PostprocessorProbe.Timeline("PPAllZ.OnPostprocessAllAssets reload=" + (didDomainReload ? "1" : "0"));
    }
}

class PPAllA : AssetPostprocessor
{
    public override int GetPostprocessOrder() => 100;

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
    {
        PPLog.Event(typeof(PPAllA), "OnPostprocessAllAssets", "", PostprocessorProbe.AllAssets(imported, deleted, moved, movedFrom) + " reload=" + (didDomainReload ? "1" : "0"));
        PostprocessorProbe.Timeline("PPAllA.OnPostprocessAllAssets reload=" + (didDomainReload ? "1" : "0"));
    }
}

class PPAllFour : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -100;

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        PPLog.Event(typeof(PPAllFour), "OnPostprocessAllAssets", "", PostprocessorProbe.AllAssets(imported, deleted, moved, movedFrom));
        PostprocessorProbe.Timeline("PPAllFour.OnPostprocessAllAssets");
    }
}
