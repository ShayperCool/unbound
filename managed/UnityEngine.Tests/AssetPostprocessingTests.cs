using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The importers' settings that AssetPostprocessors read and change, kept in the
    /// importer's copy of its .meta section, and the order OnPostprocessAllAssets runs in
    /// (tests/projects/assetpostprocessors measures the Editor).</summary>
    [Collection("EditorState")]
    public sealed class AssetPostprocessingTests
    {
        const string TextureMeta =
            "TextureImporter:\n" +
            "  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n" +
            "  isReadable: 0\n" +
            "  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n" +
            "  nPOTScale: 1\n  spriteMode: 0\n  spritePixelsToUnits: 100\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  textureType: 0\n  textureShape: 1\n" +
            "  platformSettings:\n" +
            "  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    textureCompression: 1\n" +
            "  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 512\n    textureCompression: 1\n" +
            "  userData: \n";

        static TextureImporter Texture() => (TextureImporter)AssetImporter.FromMeta("Assets/T.png", "0123456789abcdef0123456789abcdef", "TextureImporter",
            TestYaml.DocumentBody(TextureMeta));

        [Fact]
        public void TextureSettingsReadAndWriteTheMetaSectionTheDefaultPlatformsIncluded()
        {
            var importer = Texture();
            Assert.Equal(2048, importer.maxTextureSize);
            Assert.False(importer.m_SettingsChanged);
            importer.maxTextureSize = 32;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = true;
            Assert.True(importer.m_SettingsChanged);
            Assert.Equal(32, importer.maxTextureSize);
            // The default platform's entry changed; Standalone's override kept its own size.
            Assert.Equal(32, MetaSettings.Int(importer.m_Settings, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize"));
            Assert.Equal(512, MetaSettings.Int(importer.m_Settings, "platformSettings.[buildTarget=Standalone].maxTextureSize"));
            Assert.Equal(1, MetaSettings.Int(importer.m_Settings, "textureSettings.wrapV"));
            Assert.Equal(TextureWrapMode.Clamp, importer.wrapModeW);
            Assert.True(importer.isReadable);
            // The section's JSON keeps every key, the changed ones with their new values.
            var json = MetaSettings.ToJson(importer.m_Settings);
            Assert.Contains("\"filterMode\":\"0\"", json);
            Assert.Contains("\"buildTarget\":\"Standalone\"", json);
        }

        [Fact]
        public void ASpriteTypeSetsWhatTheTypeImplies()
        {
            // As the Editor's importer: a Default texture made a Sprite stops mip mapping, scaling to
            // powers of two and repeating, and its alpha becomes transparency.
            var importer = Texture();
            importer.textureType = TextureImporterType.Sprite;
            Assert.Equal(TextureImporterType.Sprite, importer.textureType);
            Assert.False(importer.mipmapEnabled);
            Assert.Equal(TextureImporterNPOTScale.None, importer.npotScale);
            Assert.Equal(TextureWrapMode.Clamp, importer.wrapMode);
            Assert.True(importer.alphaIsTransparency);
            // Settings the type does not imply are kept.
            Assert.Equal(FilterMode.Bilinear, importer.filterMode);
            Assert.Equal(100, importer.spritePixelsPerUnit);
        }

        [Fact]
        public void ModelAndAudioSettingsReadTheirSectionsWithTheReleasesDefaults()
        {
            var model = (ModelImporter)AssetImporter.FromMeta("Assets/M.fbx", "1123456789abcdef0123456789abcdef", "ModelImporter",
                TestYaml.DocumentBody("ModelImporter:\n  materials:\n    materialImportMode: 1\n  animations:\n    isReadable: 0\n  meshes:\n    globalScale: 1\n    addColliders: 0\n  animationType: 2\n  userData: \n"));
            Assert.Equal(ModelImporterMaterialImportMode.ImportStandard, model.materialImportMode);
            // Cameras and lights import unless the section says otherwise.
            Assert.True(model.importCameras);
            Assert.True(model.importLights);
            model.globalScale = 2;
            model.addCollider = true;
            model.isReadable = true;
            Assert.Equal(2f, MetaSettings.Float(model.m_Settings, "meshes.globalScale"));
            Assert.True(MetaSettings.Bool(model.m_Settings, "meshes.addColliders"));
            Assert.True(MetaSettings.Bool(model.m_Settings, "animations.isReadable"));

            var audio = (AudioImporter)AssetImporter.FromMeta("Assets/A.wav", "2123456789abcdef0123456789abcdef", "AudioImporter",
                TestYaml.DocumentBody("AudioImporter:\n  defaultSettings:\n    loadType: 1\n    compressionFormat: 1\n    quality: 0.5\n  forceToMono: 0\n  userData: \n"));
            var settings = audio.defaultSampleSettings;
            Assert.Equal(AudioClipLoadType.CompressedInMemory, settings.loadType);
            Assert.Equal(0.5f, settings.quality);
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            audio.defaultSampleSettings = settings;
            audio.forceToMono = true;
            Assert.Equal(AudioClipLoadType.DecompressOnLoad, audio.defaultSampleSettings.loadType);
            Assert.Equal(0, MetaSettings.Int(audio.m_Settings, "defaultSettings.compressionFormat", -1));
            Assert.True(audio.forceToMono);
            Assert.True(audio.m_SettingsChanged);
        }

        [Fact]
        public void SetAddsMissingKeysAndAMissingSequenceEntryStaysUnset()
        {
            var root = TestYaml.DocumentBody("TextureImporter:\n  platformSettings: []\n  userData: \n");
            MetaSettings.Set(root, "bumpmap.convertToNormalMap", "1");
            Assert.Equal(1, MetaSettings.Int(root, "bumpmap.convertToNormalMap"));
            MetaSettings.Set(root, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize", "64");
            Assert.Null(MetaSettings.Find(root, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize"));
        }

        static readonly List<string> s_AllAssetsCalls = new List<string>();

        // Declared out of name order, with postprocess orders that would reverse them.
        sealed class ZAllAssets : AssetPostprocessor
        {
            public override int GetPostprocessOrder() => -10;
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
                s_AllAssetsCalls.Add("Z " + string.Join(",", imported) + " " + didDomainReload);
        }

        sealed class AAllAssets : AssetPostprocessor
        {
            public override int GetPostprocessOrder() => 10;
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
                s_AllAssetsCalls.Add("A " + string.Join(",", imported));
        }

        [Fact]
        public void OnPostprocessAllAssetsRunsByClassNameWhateverThePostprocessOrder()
        {
            s_AllAssetsCalls.Clear();
            AssetPostprocessing.ImportedAssets(typeof(AssetPostprocessingTests).Assembly, new[] { "Assets/X.vfx" });
            Assert.Equal(new[] { "A Assets/X.vfx", "Z Assets/X.vfx False" }, s_AllAssetsCalls.FindAll(c => c.StartsWith("A ") || c.StartsWith("Z ")));
        }
    }
}
