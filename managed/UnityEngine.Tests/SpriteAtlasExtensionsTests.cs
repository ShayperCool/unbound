using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's sprite atlas extensions over an atlas read from its file, as the Unity
    /// 6000.0.71f1 Editor answers them (tests/projects/spriteatlaseditor): "Include in Build" from
    /// a V1 atlas's bindAsDefault (on when absent), packing and texture settings (defaults when
    /// absent or on a new atlas, serialized fields verbatim, legacy serializedVersion 1 fields,
    /// distinct V1 variant settings), no master for an atlas that is no variant, and
    /// ArgumentNullException for a null atlas. Packable resolution and V2 importer settings need
    /// the asset database and are covered by the probe.
    /// </summary>
    public sealed class SpriteAtlasExtensionsTests
    {
        static SpriteAtlas Read(string editorData, bool isVariant = false)
        {
            var body = TestYaml.DocumentBody("SpriteAtlas:\n  m_Name: Probe\n  m_EditorData:\n" + editorData
                + "  m_MasterAtlas: {fileID: 0}\n  m_Tag: Probe\n  m_IsVariant: " + (isVariant ? "1" : "0") + "\n");
            var atlas = new SpriteAtlas();
            atlas.ReadSerialized(body, null);
            return atlas;
        }

        [Fact]
        public void IncludeInBuildIsTheAtlassBindAsDefault()
        {
            Assert.False(Read("    packables: []\n    bindAsDefault: 0\n").IsIncludeInBuild());
            Assert.True(Read("    packables: []\n    bindAsDefault: 1\n").IsIncludeInBuild());
            // Control: an atlas file without the setting is included, as the importer's default.
            Assert.True(Read("    packables: []\n").IsIncludeInBuild());
        }

        [Fact]
        public void AnAtlasThatIsNoVariantHasNoMaster()
        {
            var atlas = Read("    packables:\n    - {fileID: 2800000, guid: 0f3c5a1b2d4e6f708192a3b4c5d6e7f8, type: 3}\n    bindAsDefault: 1\n");
            Assert.False(atlas.isVariant);
            Assert.Null(atlas.GetMasterAtlas());
        }

        [Fact]
        public void DefaultStructsAreZeroInitializedAndMutable()
        {
            var packing = default(SpriteAtlasPackingSettings);
            Assert.Equal(0, packing.blockOffset);
            Assert.Equal(0, packing.padding);
            Assert.False(packing.enableRotation);
            Assert.False(packing.enableTightPacking);
            Assert.False(packing.enableAlphaDilation);

            packing.blockOffset = 2;
            packing.padding = 8;
            packing.enableRotation = true;
            packing.enableTightPacking = true;
            packing.enableAlphaDilation = true;
            Assert.Equal(2, packing.blockOffset);
            Assert.Equal(8, packing.padding);
            Assert.True(packing.enableRotation);
            Assert.True(packing.enableTightPacking);
            Assert.True(packing.enableAlphaDilation);

            var texture = default(SpriteAtlasTextureSettings);
            Assert.Equal(0, texture.maxTextureSize);
            Assert.Equal(0, texture.anisoLevel);
            Assert.Equal(FilterMode.Point, texture.filterMode);
            Assert.False(texture.generateMipMaps);
            Assert.False(texture.readable);
            Assert.False(texture.sRGB);

            texture.anisoLevel = 4;
            texture.filterMode = FilterMode.Trilinear;
            texture.generateMipMaps = true;
            texture.readable = true;
            texture.sRGB = true;
            Assert.Equal(4, texture.anisoLevel);
            Assert.Equal(FilterMode.Trilinear, texture.filterMode);
            Assert.True(texture.generateMipMaps);
            Assert.True(texture.readable);
            Assert.True(texture.sRGB);
        }

        [Fact]
        public void NewAndOmittedSettingsUseEditorDefaults()
        {
            foreach (var atlas in new[] { new SpriteAtlas(), Read("    packables: []\n") })
            {
                var packing = atlas.GetPackingSettings();
                Assert.Equal(1, packing.blockOffset);
                Assert.Equal(4, packing.padding);
                Assert.True(packing.enableRotation);
                Assert.True(packing.enableTightPacking);
                Assert.False(packing.enableAlphaDilation);

                var texture = atlas.GetTextureSettings();
                Assert.Equal(2048, texture.maxTextureSize);
                Assert.Equal(1, texture.anisoLevel);
                Assert.Equal(FilterMode.Bilinear, texture.filterMode);
                Assert.False(texture.generateMipMaps);
                Assert.False(texture.readable);
                Assert.True(texture.sRGB);
            }
        }

        [Fact]
        public void ReadsModifiedPackingAndTextureSettings()
        {
            var atlas = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 4\n" +
                "      maxTextureSize: 1024\n" +
                "      filterMode: 2\n" +
                "      generateMipMaps: 1\n" +
                "      readable: 1\n" +
                "      sRGB: 0\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 0\n" +
                "      enableAlphaDilation: 1\n" +
                "    packables: []\n");

            var packing = atlas.GetPackingSettings();
            Assert.Equal(2, packing.blockOffset);
            Assert.Equal(8, packing.padding);
            Assert.False(packing.enableRotation);
            Assert.False(packing.enableTightPacking);
            Assert.True(packing.enableAlphaDilation);

            var texture = atlas.GetTextureSettings();
            Assert.Equal(1024, texture.maxTextureSize);
            Assert.Equal(4, texture.anisoLevel);
            Assert.Equal(FilterMode.Trilinear, texture.filterMode);
            Assert.True(texture.generateMipMaps);
            Assert.True(texture.readable);
            Assert.False(texture.sRGB);
        }

        [Fact]
        public void ReadsLegacyVersion1PackingAndTextureSettings()
        {
            var v1ZeroPower = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 1\n" +
                "      colorSpace: 1\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 1\n" +
                "      paddingPower: 0\n" +
                "    packables: []\n");
            Assert.Equal(2, v1ZeroPower.GetPackingSettings().padding);
            Assert.False(v1ZeroPower.GetTextureSettings().sRGB);

            var v1Power3 = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 1\n" +
                "      colorSpace: 0\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 1\n" +
                "      paddingPower: 3\n" +
                "    packables: []\n");
            Assert.Equal(8, v1Power3.GetPackingSettings().padding);
            Assert.True(v1Power3.GetTextureSettings().sRGB);
        }

        [Fact]
        public void PlatformSettingsUseEditorDefaultsWhenMissingOrNew()
        {
            foreach (var atlas in new[] { new SpriteAtlas(), Read("    platformSettings: []\n    packables: []\n") })
            {
                var def = atlas.GetPlatformSettings("DefaultTexturePlatform");
                Assert.Equal("DefaultTexturePlatform", def.name);
                Assert.False(def.overridden);
                Assert.False(def.ignorePlatformSupport);
                Assert.Equal(2048, def.maxTextureSize);
                Assert.Equal(TextureResizeAlgorithm.Mitchell, def.resizeAlgorithm);
                Assert.Equal(TextureImporterFormat.Automatic, def.format);
                Assert.Equal(TextureImporterCompression.Compressed, def.textureCompression);
                Assert.Equal(50, def.compressionQuality);
                Assert.False(def.crunchedCompression);
                Assert.False(def.allowsAlphaSplitting);
                Assert.Equal(AndroidETC2FallbackOverride.UseBuildSettings, def.androidETC2FallbackOverride);

                Assert.Equal("iOS", atlas.GetPlatformSettings("iPhone").name);
                Assert.Equal("Standalone", atlas.GetPlatformSettings("StandaloneWindows64").name);
                Assert.Equal("WindowsStoreApps", atlas.GetPlatformSettings("WSAPlayer").name);
                Assert.Equal("", atlas.GetPlatformSettings("").name);
                Assert.Equal("", atlas.GetPlatformSettings(null).name);
                Assert.Equal("CustomUnknown", atlas.GetPlatformSettings("CustomUnknown").name);
            }
        }

        [Fact]
        public void PlatformSettingsNormalizeAliasesAndDoNotFallbackToStoredDefault()
        {
            var atlas = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: DefaultTexturePlatform\n" +
                "      m_MaxTextureSize: 512\n" +
                "      m_ResizeAlgorithm: 1\n" +
                "      m_TextureFormat: 4\n" +
                "      m_TextureCompression: 2\n" +
                "      m_CompressionQuality: 75\n" +
                "      m_CrunchedCompression: 1\n" +
                "      m_AllowsAlphaSplitting: 1\n" +
                "      m_Overridden: 0\n" +
                "      m_IgnorePlatformSupport: 1\n" +
                "      m_AndroidETC2FallbackOverride: 2\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: iPhone\n" +
                "      m_MaxTextureSize: 256\n" +
                "      m_TextureFormat: 50\n" +
                "      m_CompressionQuality: 80\n" +
                "      m_Overridden: 1\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Standalone\n" +
                "      m_MaxTextureSize: 4096\n" +
                "      m_TextureFormat: 12\n" +
                "      m_CompressionQuality: 90\n" +
                "      m_Overridden: 1\n" +
                "    packables: []\n");

            var def = atlas.GetPlatformSettings("DefaultTexturePlatform");
            Assert.Equal(512, def.maxTextureSize);
            Assert.Equal(TextureResizeAlgorithm.Bilinear, def.resizeAlgorithm);
            Assert.Equal((TextureImporterFormat)4, def.format);
            Assert.Equal(TextureImporterCompression.CompressedHQ, def.textureCompression);
            Assert.Equal(75, def.compressionQuality);
            Assert.True(def.crunchedCompression);
            Assert.True(def.allowsAlphaSplitting);
            Assert.False(def.overridden);
            Assert.True(def.ignorePlatformSupport);
            Assert.Equal(AndroidETC2FallbackOverride.Quality16Bit, def.androidETC2FallbackOverride);

            // Non-BuildTarget lookup is case-sensitive and misses without inheriting stored DefaultTexturePlatform.
            var wrongCase = atlas.GetPlatformSettings("defaulttextureplatform");
            Assert.Equal("defaulttextureplatform", wrongCase.name);
            Assert.Equal(2048, wrongCase.maxTextureSize);
            Assert.Equal(50, wrongCase.compressionQuality);

            // An unstored platform gets fresh defaults, not the atlas's stored DefaultTexturePlatform.
            var unstored = atlas.GetPlatformSettings("WebGL");
            Assert.Equal("WebGL", unstored.name);
            Assert.False(unstored.overridden);
            Assert.Equal(2048, unstored.maxTextureSize);

            // iPhone / iOS aliases match and report canonical "iOS".
            foreach (var alias in new[] { "iPhone", "iOS", "ios", "iphone" })
            {
                var ios = atlas.GetPlatformSettings(alias);
                Assert.Equal("iOS", ios.name);
                Assert.True(ios.overridden);
                Assert.Equal(256, ios.maxTextureSize);
                Assert.Equal(80, ios.compressionQuality);
            }

            // Desktop standalone aliases resolve to the stored Standalone entry.
            foreach (var alias in new[] { "Standalone", "standalone", "StandaloneWindows", "StandaloneWindows64", "StandaloneOSX", "StandaloneLinux64", "Win", "Win64", "OSXUniversal", "Linux64" })
            {
                var desktop = atlas.GetPlatformSettings(alias);
                Assert.Equal("Standalone", desktop.name);
                Assert.True(desktop.overridden);
                Assert.Equal(4096, desktop.maxTextureSize);
            }

            // Mutating a returned object does not affect subsequent reads.
            var copy = atlas.GetPlatformSettings("iOS");
            copy.name = "Mutated";
            copy.maxTextureSize = 32;
            copy.overridden = false;
            var fresh = atlas.GetPlatformSettings("iOS");
            Assert.Equal("iOS", fresh.name);
            Assert.Equal(256, fresh.maxTextureSize);
            Assert.True(fresh.overridden);
        }

        [Fact]
        public void PlatformSettingsMigrateLegacySerializedVersionsAndRespectBc6hBc7Flag()
        {
            var v1 = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 1\n" +
                "      m_BuildTarget: DefaultTexturePlatform\n" +
                "      m_TextureFormat: 35\n" +
                "    - serializedVersion: 1\n" +
                "      m_BuildTarget: Android\n" +
                "      m_TextureFormat: 36\n" +
                "      m_Overridden: 1\n" +
                "    - serializedVersion: 2\n" +
                "      m_BuildTarget: Standalone\n" +
                "      m_TextureFormat: 25\n" +
                "      m_CompressionQuality: 50\n" +
                "      m_Overridden: 1\n" +
                "    packables: []\n");

            Assert.Equal(34, (int)v1.GetPlatformSettings("DefaultTexturePlatform").format);
            Assert.Equal(47, (int)v1.GetPlatformSettings("Android").format);
            Assert.Equal(100, v1.GetPlatformSettings("Standalone").compressionQuality);

            var v3 = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 3\n" +
                "      m_BuildTarget: Nintendo Switch\n" +
                "      m_MaxTextureSize: 256\n" +
                "      m_Overridden: 1\n" +
                "    - serializedVersion: 3\n" +
                "      m_BuildTarget: StandaloneWindows64\n" +
                "      m_MaxTextureSize: 256\n" +
                "      m_Overridden: 1\n" +
                "    packables: []\n");

            var sw = v3.GetPlatformSettings("Switch");
            Assert.Equal("Switch", sw.name);
            Assert.True(sw.overridden);
            Assert.Equal(256, sw.maxTextureSize);

            // Specific desktop target stored instead of "Standalone" does not match Standalone lookup.
            var desktop = v3.GetPlatformSettings("StandaloneWindows64");
            Assert.Equal("Standalone", desktop.name);
            Assert.False(desktop.overridden);
            Assert.Equal(2048, desktop.maxTextureSize);

            // Control: v4 BC6H/BC7 keeps stored quality when forceMaximumCompressionQuality_BC6H_BC7 is 0,
            // and forces 100 when it is 1.
            var v4 = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: PS4\n" +
                "      m_TextureFormat: 24\n" +
                "      m_CompressionQuality: 42\n" +
                "      m_Overridden: 1\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: PS5\n" +
                "      m_TextureFormat: 25\n" +
                "      m_CompressionQuality: 42\n" +
                "      m_Overridden: 1\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 1\n" +
                "    packables: []\n");
            Assert.Equal(42, v4.GetPlatformSettings("PS4").compressionQuality);
            Assert.Equal(100, v4.GetPlatformSettings("PS5").compressionQuality);

            // Control outside the probe's exact inputs: non-BC6H/BC7 format with the BC6H/BC7 flag keeps stored quality.
            var control = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: tvOS\n" +
                "      m_TextureFormat: 50\n" +
                "      m_CompressionQuality: 37\n" +
                "      m_Overridden: 1\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 1\n" +
                "    packables: []\n");
            Assert.Equal(37, control.GetPlatformSettings("AppleTV").compressionQuality);
            Assert.Equal("tvOS", control.GetPlatformSettings("AppleTV").name);
        }

        [Fact]
        public void SetIncludeInBuildTogglesValueAndTracksDirtinessOnlyOnChange()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);
            Assert.True(fresh.IsIncludeInBuild());
            Assert.True(EditorUtility.IsDirty(fresh));

            fresh.SetIncludeInBuild(true);
            Assert.True(fresh.IsIncludeInBuild());
            Assert.Equal(baseDirty, EditorUtility.GetDirtyCount(fresh));

            fresh.SetIncludeInBuild(false);
            Assert.False(fresh.IsIncludeInBuild());
            Assert.True(EditorUtility.IsDirty(fresh));
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

            fresh.SetIncludeInBuild(false);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

            fresh.SetIncludeInBuild(true);
            Assert.True(fresh.IsIncludeInBuild());
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));

            var loadedIncluded = Read("    packables: []\n    bindAsDefault: 1\n");
            Assert.True(loadedIncluded.IsIncludeInBuild());
            Assert.False(EditorUtility.IsDirty(loadedIncluded));
            Assert.Equal(0, EditorUtility.GetDirtyCount(loadedIncluded));

            loadedIncluded.SetIncludeInBuild(true);
            Assert.False(EditorUtility.IsDirty(loadedIncluded));
            Assert.Equal(0, EditorUtility.GetDirtyCount(loadedIncluded));

            loadedIncluded.SetIncludeInBuild(false);
            Assert.False(loadedIncluded.IsIncludeInBuild());
            Assert.True(EditorUtility.IsDirty(loadedIncluded));
            Assert.Equal(1, EditorUtility.GetDirtyCount(loadedIncluded));

            loadedIncluded.SetIncludeInBuild(false);
            Assert.Equal(1, EditorUtility.GetDirtyCount(loadedIncluded));

            var loadedExcluded = Read("    packables: []\n    bindAsDefault: 0\n");
            Assert.False(loadedExcluded.IsIncludeInBuild());
            Assert.False(EditorUtility.IsDirty(loadedExcluded));
            loadedExcluded.SetIncludeInBuild(false);
            Assert.False(EditorUtility.IsDirty(loadedExcluded));
            loadedExcluded.SetIncludeInBuild(true);
            Assert.True(loadedExcluded.IsIncludeInBuild());
            Assert.True(EditorUtility.IsDirty(loadedExcluded));
            Assert.Equal(1, EditorUtility.GetDirtyCount(loadedExcluded));
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1BindAsDefaultAndSkipsV2ImportedAtlas()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "    isAtlasV2: 0\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(v1Source);
            var atlas = Read("    packables: []\n    bindAsDefault: 1\n");
            atlas.SetIncludeInBuild(false);

            int unlocated = 0;
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            Assert.Equal(0, unlocated);
            Assert.Equal(v1Source.Replace("    bindAsDefault: 1\n", "    bindAsDefault: 0\n"), UnityYamlText.Write(docs));

            // Toggling back restores exact original YAML bytes.
            atlas.SetIncludeInBuild(true);
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            Assert.Equal(v1Source, UnityYamlText.Write(docs));

            // Omitted bindAsDefault is inserted right after packables.
            const string v1Omitted =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Omitted\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    packables: []\n" +
                "    isAtlasV2: 0\n" +
                "  m_IsVariant: 0\n";
            var omittedDocs = UnityYamlText.Parse(v1Omitted);
            var omittedAtlas = Read("    packables: []\n");
            omittedAtlas.SetIncludeInBuild(false);
            Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
            Assert.Equal(v1Omitted.Replace("    packables: []\n", "    packables: []\n    bindAsDefault: 0\n"), UnityYamlText.Write(omittedDocs));

            // Control: a V2 imported SpriteAtlas is not written through NativeDocuments.Write.
            const string v2Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!612988286 &1\n" +
                "SpriteAtlasAsset:\n" +
                "  m_Name: \n" +
                "  m_ImporterData:\n" +
                "    packables: []\n" +
                "  m_IsVariant: 0\n";
            var v2Docs = UnityYamlText.Parse(v2Source);
            var v2Atlas = new SpriteAtlas();
            v2Atlas.ReadSerialized(TestYaml.DocumentBody("SpriteAtlasAsset:\n  m_Name: \n  m_ImporterData:\n    packables: []\n  m_IsVariant: 0\n"), null);
            Assert.True(v2Atlas.IsFromImporter);
            Assert.False(NativeDocuments.Write(v2Docs[0], v2Atlas, null, null, ref unlocated));
            Assert.Equal(v2Source, UnityYamlText.Write(v2Docs));
        }

        [Fact]
        public void SetPackingSettingsCopiesVerbatimAndAlwaysDirties()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);
            var initial = fresh.GetPackingSettings();
            fresh.SetPackingSettings(initial);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

            var edge = new SpriteAtlasPackingSettings
            {
                blockOffset = -3,
                padding = -7,
                enableRotation = false,
                enableTightPacking = false,
                enableAlphaDilation = true,
            };
            fresh.SetPackingSettings(edge);
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));
            var readBack = fresh.GetPackingSettings();
            Assert.Equal(-3, readBack.blockOffset);
            Assert.Equal(-7, readBack.padding);
            Assert.False(readBack.enableRotation);
            Assert.False(readBack.enableTightPacking);
            Assert.True(readBack.enableAlphaDilation);

            var loaded = Read(
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 4\n" +
                "      blockOffset: 1\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n");
            Assert.False(EditorUtility.IsDirty(loaded));
            Assert.Equal(0, EditorUtility.GetDirtyCount(loaded));
            // Same-value SetPackingSettings still marks dirty (unlike SetIncludeInBuild).
            loaded.SetPackingSettings(loaded.GetPackingSettings());
            Assert.True(EditorUtility.IsDirty(loaded));
            Assert.Equal(1, EditorUtility.GetDirtyCount(loaded));
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1PackingSettingsUpgradesLegacyAndInsertsWhenOmitted()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    platformSettings: []\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 4\n" +
                "      blockOffset: 1\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(v1Source);
            var atlas = Read(
                "    platformSettings: []\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 4\n" +
                "      blockOffset: 1\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n");
            var original = atlas.GetPackingSettings();
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = -2,
                padding = 12,
                enableRotation = false,
                enableTightPacking = false,
                enableAlphaDilation = true,
            });

            int unlocated = 0;
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            string written = UnityYamlText.Write(docs);
            Assert.Contains("      padding: 12\n      blockOffset: -2\n      allowAlphaSplitting: 0\n      enableRotation: 0\n      enableTightPacking: 0\n      enableAlphaDilation: 1\n", written);

            // Roundtrip restores exact original YAML bytes.
            atlas.SetPackingSettings(original);
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            Assert.Equal(v1Source, UnityYamlText.Write(docs));

            // Legacy v1 packingSettings upgrades to v2 and preserves hidden allowAlphaSplitting.
            const string v1Legacy =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Legacy\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    platformSettings: []\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 1\n" +
                "      paddingPower: 3\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 0\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";
            var legacyDocs = UnityYamlText.Parse(v1Legacy);
            var legacyAtlas = Read(
                "    packingSettings:\n" +
                "      serializedVersion: 1\n" +
                "      paddingPower: 3\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 0\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n");
            var s = legacyAtlas.GetPackingSettings();
            Assert.Equal(8, s.padding);
            s.padding = 6;
            legacyAtlas.SetPackingSettings(s);
            Assert.True(NativeDocuments.Write(legacyDocs[0], legacyAtlas, null, null, ref unlocated));
            string legacyWritten = UnityYamlText.Write(legacyDocs);
            Assert.DoesNotContain("paddingPower", legacyWritten);
            Assert.Contains("      serializedVersion: 2\n      padding: 6\n      blockOffset: 2\n      allowAlphaSplitting: 1\n      enableRotation: 1\n      enableTightPacking: 0\n      enableAlphaDilation: 0\n", legacyWritten);

            // Control outside the probe: sparse V1 atlas with omitted packingSettings inserts the block after platformSettings when mutated.
            const string v1Omitted =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Omitted\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    platformSettings: []\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";
            var omittedDocs = UnityYamlText.Parse(v1Omitted);
            var omittedAtlas = Read("    platformSettings: []\n    packables: []\n    bindAsDefault: 1\n");
            omittedAtlas.SetPackingSettings(new SpriteAtlasPackingSettings { blockOffset = 4, padding = 10, enableRotation = true });
            Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
            string omittedWritten = UnityYamlText.Write(omittedDocs);
            Assert.Contains("    platformSettings: []\n    packingSettings:\n      serializedVersion: 2\n      padding: 10\n      blockOffset: 4\n", omittedWritten);
        }

        [Fact]
        public void SetTextureSettingsCopiesVerbatimAndAlwaysDirties()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);
            var initial = fresh.GetTextureSettings();
            fresh.SetTextureSettings(initial);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

            var copy = initial;
            copy.anisoLevel = 8;
            copy.filterMode = FilterMode.Trilinear;
            copy.generateMipMaps = true;
            copy.readable = true;
            copy.sRGB = false;
            fresh.SetTextureSettings(copy);
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));
            var readCopy = fresh.GetTextureSettings();
            Assert.Equal(2048, readCopy.maxTextureSize);
            Assert.Equal(8, readCopy.anisoLevel);
            Assert.Equal(FilterMode.Trilinear, readCopy.filterMode);
            Assert.True(readCopy.generateMipMaps);
            Assert.True(readCopy.readable);
            Assert.False(readCopy.sRGB);

            fresh.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                anisoLevel = -5,
                filterMode = (FilterMode)99,
                generateMipMaps = true,
                readable = false,
                sRGB = true,
            });
            Assert.Equal(baseDirty + 3, EditorUtility.GetDirtyCount(fresh));
            var readEdge = fresh.GetTextureSettings();
            Assert.Equal(0, readEdge.maxTextureSize);
            Assert.Equal(-5, readEdge.anisoLevel);
            Assert.Equal((FilterMode)99, readEdge.filterMode);
            Assert.True(readEdge.generateMipMaps);
            Assert.False(readEdge.readable);
            Assert.True(readEdge.sRGB);

            var loaded = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n" +
                "    packables: []\n");
            Assert.False(EditorUtility.IsDirty(loaded));
            Assert.Equal(0, EditorUtility.GetDirtyCount(loaded));
            // Same-value SetTextureSettings still marks dirty (unlike SetIncludeInBuild).
            loaded.SetTextureSettings(loaded.GetTextureSettings());
            Assert.True(EditorUtility.IsDirty(loaded));
            Assert.Equal(1, EditorUtility.GetDirtyCount(loaded));
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1TextureSettingsUpgradesLegacyAndInsertsWhenOmitted()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n" +
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Android\n" +
                "      m_MaxTextureSize: 1024\n" +
                "      m_Overridden: 1\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(v1Source);
            var atlas = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n" +
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Android\n" +
                "      m_MaxTextureSize: 1024\n" +
                "      m_Overridden: 1\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n");
            var original = atlas.GetTextureSettings();
            var edited = original;
            edited.anisoLevel = 6;
            edited.filterMode = FilterMode.Trilinear;
            edited.generateMipMaps = true;
            edited.readable = true;
            edited.sRGB = false;
            atlas.SetTextureSettings(edited);

            int unlocated = 0;
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            string written = UnityYamlText.Write(docs);
            Assert.Contains(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 6\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 2\n" +
                "      generateMipMaps: 1\n" +
                "      readable: 1\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 0\n",
                written);
            Assert.Contains("      m_BuildTarget: Android\n      m_MaxTextureSize: 1024\n      m_Overridden: 1\n", written);
            Assert.Contains("      padding: 8\n      blockOffset: 2\n      allowAlphaSplitting: 1\n", written);

            // Roundtrip restores exact original YAML bytes.
            atlas.SetTextureSettings(original);
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            Assert.Equal(v1Source, UnityYamlText.Write(docs));

            // Control outside the probe: legacy v1 colorSpace: 1 (linear -> sRGB: 0) upgrades to v2 and keeps hidden fields.
            const string v1Legacy =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: LegacyLinear\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 1\n" +
                "      anisoLevel: 3\n" +
                "      compressionQuality: 82\n" +
                "      maxTextureSize: 512\n" +
                "      textureCompression: 3\n" +
                "      filterMode: 0\n" +
                "      generateMipMaps: 1\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      colorSpace: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";
            var legacyDocs = UnityYamlText.Parse(v1Legacy);
            var legacyAtlas = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 1\n" +
                "      anisoLevel: 3\n" +
                "      compressionQuality: 82\n" +
                "      maxTextureSize: 512\n" +
                "      textureCompression: 3\n" +
                "      filterMode: 0\n" +
                "      generateMipMaps: 1\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      colorSpace: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n");
            var legacySettings = legacyAtlas.GetTextureSettings();
            Assert.False(legacySettings.sRGB);
            legacySettings.anisoLevel = 9;
            legacySettings.sRGB = true;
            legacyAtlas.SetTextureSettings(legacySettings);
            Assert.True(NativeDocuments.Write(legacyDocs[0], legacyAtlas, null, null, ref unlocated));
            string legacyWritten = UnityYamlText.Write(legacyDocs);
            Assert.DoesNotContain("colorSpace", legacyWritten);
            Assert.Contains(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 9\n" +
                "      compressionQuality: 82\n" +
                "      maxTextureSize: 512\n" +
                "      textureCompression: 3\n" +
                "      filterMode: 0\n" +
                "      generateMipMaps: 1\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n",
                legacyWritten);

            // Sparse V1 atlas with omitted textureSettings inserts the block right after serializedVersion when mutated.
            const string v1Omitted =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Omitted\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    platformSettings: []\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";
            var omittedDocs = UnityYamlText.Parse(v1Omitted);
            var omittedAtlas = Read("    platformSettings: []\n    packables: []\n    bindAsDefault: 1\n");
            var omittedSettings = omittedAtlas.GetTextureSettings();
            omittedSettings.anisoLevel = 3;
            omittedSettings.readable = true;
            omittedAtlas.SetTextureSettings(omittedSettings);
            Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
            string omittedWritten = UnityYamlText.Write(omittedDocs);
            Assert.Contains(
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 3\n" +
                "      compressionQuality: 50\n" +
                "      maxTextureSize: 2048\n" +
                "      textureCompression: 0\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 1\n" +
                "      crunchedCompression: 0\n" +
                "      sRGB: 1\n" +
                "    platformSettings: []\n",
                omittedWritten);
        }

        [Fact]
        public void SetPlatformSettingsNormalizesCopiesAndFollowsInsertReplaceRules()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);

            // Null src throws NullReferenceException without changing dirty count.
            Assert.Throws<NullReferenceException>(() => fresh.SetPlatformSettings(null));
            Assert.Equal(baseDirty, EditorUtility.GetDirtyCount(fresh));

            // Absent non-Default platform with overridden=false is not appended, but still marks dirty.
            var webOff = new TextureImporterPlatformSettings { name = "WebGL", overridden = false, maxTextureSize = 256 };
            fresh.SetPlatformSettings(webOff);
            Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));
            Assert.Equal(2048, fresh.GetPlatformSettings("WebGL").maxTextureSize);

            // Absent DefaultTexturePlatform with overridden=false IS appended and defensively copied.
            var defOff = new TextureImporterPlatformSettings { name = "DefaultTexturePlatform", overridden = false, maxTextureSize = 512, compressionQuality = 77 };
            fresh.SetPlatformSettings(defOff);
            defOff.maxTextureSize = 64;
            Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));
            var readDef = fresh.GetPlatformSettings("DefaultTexturePlatform");
            Assert.Equal(512, readDef.maxTextureSize);
            Assert.Equal(77, readDef.compressionQuality);
            Assert.False(readDef.overridden);

            // Alias normalization mutates src.name in place and stores a defensive copy.
            var ios = new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256, format = TextureImporterFormat.ASTC_6x6 };
            fresh.SetPlatformSettings(ios);
            Assert.Equal("iOS", ios.name);
            ios.maxTextureSize = 32;
            ios.name = "Mutated";
            Assert.Equal(baseDirty + 3, EditorUtility.GetDirtyCount(fresh));
            var readIos = fresh.GetPlatformSettings("iPhone");
            Assert.Equal("iOS", readIos.name);
            Assert.Equal(256, readIos.maxTextureSize);
            Assert.True(readIos.overridden);

            // Replacing an existing entry with overridden=false keeps the entry updated in place.
            var iosOff = fresh.GetPlatformSettings("iOS");
            iosOff.overridden = false;
            iosOff.maxTextureSize = 128;
            fresh.SetPlatformSettings(iosOff);
            Assert.Equal(baseDirty + 4, EditorUtility.GetDirtyCount(fresh));
            var readIosOff = fresh.GetPlatformSettings("iOS");
            Assert.False(readIosOff.overridden);
            Assert.Equal(128, readIosOff.maxTextureSize);

            // Loaded V1 atlas: same-value SetPlatformSettings still marks dirty, and Standalone aliases share one entry.
            var loaded = Read(
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Android\n" +
                "      m_MaxTextureSize: 1024\n" +
                "      m_Overridden: 1\n" +
                "    packables: []\n");
            Assert.False(EditorUtility.IsDirty(loaded));
            loaded.SetPlatformSettings(loaded.GetPlatformSettings("Android"));
            Assert.True(EditorUtility.IsDirty(loaded));
            Assert.Equal(1, EditorUtility.GetDirtyCount(loaded));

            var win = new TextureImporterPlatformSettings { name = "StandaloneWindows64", overridden = true, maxTextureSize = 4096 };
            loaded.SetPlatformSettings(win);
            Assert.Equal("Standalone", win.name);
            Assert.Equal(4096, loaded.GetPlatformSettings("StandaloneLinux64").maxTextureSize);
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1PlatformSettingsPreservesOrderUpgradesLegacyAndInsertsWhenOmitted()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n" +
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Android\n" +
                "      m_MaxTextureSize: 1024\n" +
                "      m_ResizeAlgorithm: 0\n" +
                "      m_TextureFormat: 47\n" +
                "      m_TextureCompression: 1\n" +
                "      m_CompressionQuality: 50\n" +
                "      m_CrunchedCompression: 0\n" +
                "      m_AllowsAlphaSplitting: 0\n" +
                "      m_Overridden: 1\n" +
                "      m_IgnorePlatformSupport: 0\n" +
                "      m_AndroidETC2FallbackOverride: 0\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: iOS\n" +
                "      m_MaxTextureSize: 512\n" +
                "      m_ResizeAlgorithm: 0\n" +
                "      m_TextureFormat: 50\n" +
                "      m_TextureCompression: 1\n" +
                "      m_CompressionQuality: 80\n" +
                "      m_CrunchedCompression: 0\n" +
                "      m_AllowsAlphaSplitting: 0\n" +
                "      m_Overridden: 1\n" +
                "      m_IgnorePlatformSupport: 0\n" +
                "      m_AndroidETC2FallbackOverride: 0\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(v1Source);
            var atlas = Read(
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 75\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 2\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 1\n" +
                "      sRGB: 1\n" +
                "    platformSettings:\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: Android\n" +
                "      m_MaxTextureSize: 1024\n" +
                "      m_ResizeAlgorithm: 0\n" +
                "      m_TextureFormat: 47\n" +
                "      m_TextureCompression: 1\n" +
                "      m_CompressionQuality: 50\n" +
                "      m_CrunchedCompression: 0\n" +
                "      m_AllowsAlphaSplitting: 0\n" +
                "      m_Overridden: 1\n" +
                "      m_IgnorePlatformSupport: 0\n" +
                "      m_AndroidETC2FallbackOverride: 0\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    - serializedVersion: 4\n" +
                "      m_BuildTarget: iOS\n" +
                "      m_MaxTextureSize: 512\n" +
                "      m_ResizeAlgorithm: 0\n" +
                "      m_TextureFormat: 50\n" +
                "      m_TextureCompression: 1\n" +
                "      m_CompressionQuality: 80\n" +
                "      m_CrunchedCompression: 0\n" +
                "      m_AllowsAlphaSplitting: 0\n" +
                "      m_Overridden: 1\n" +
                "      m_IgnorePlatformSupport: 0\n" +
                "      m_AndroidETC2FallbackOverride: 0\n" +
                "      m_ForceMaximumCompressionQuality_BC6H_BC7: 0\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 1\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n");

            // Replace iOS via iPhone alias (keeps index 1) and append Standalone via Win64 alias (index 2).
            var origIos = atlas.GetPlatformSettings("iOS");
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 33 });
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings { name = "Win64", overridden = true, maxTextureSize = 4096 });
            int unlocated = 0;
            Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
            string written = UnityYamlText.Write(docs);
            int idxAndroid = written.IndexOf("m_BuildTarget: Android", StringComparison.Ordinal);
            int idxIos = written.IndexOf("m_BuildTarget: iOS", StringComparison.Ordinal);
            int idxStandalone = written.IndexOf("m_BuildTarget: Standalone", StringComparison.Ordinal);
            Assert.True(idxAndroid > 0 && idxAndroid < idxIos && idxIos < idxStandalone);
            Assert.Contains("      m_BuildTarget: iOS\n      m_MaxTextureSize: 256\n", written);
            Assert.Contains("      anisoLevel: 2\n      compressionQuality: 75\n", written);
            Assert.Contains("      padding: 8\n      blockOffset: 2\n      allowAlphaSplitting: 1\n", written);

            // Restore iOS on a fresh parse of v1Source and confirm exact byte roundtrip.
            var roundtripDocs = UnityYamlText.Parse(v1Source);
            var roundtripAtlas = new SpriteAtlas();
            roundtripAtlas.ReadSerialized(TestYaml.DocumentBody(v1Source), null);
            roundtripAtlas.SetPlatformSettings(origIos);
            Assert.True(NativeDocuments.Write(roundtripDocs[0], roundtripAtlas, null, null, ref unlocated));
            Assert.Equal(v1Source, UnityYamlText.Write(roundtripDocs));

            // Control outside the probe: sparse V1 atlas with omitted platformSettings inserts it between textureSettings and packingSettings.
            const string v1Omitted =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Omitted\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 1\n" +
                "      compressionQuality: 50\n" +
                "      maxTextureSize: 2048\n" +
                "      textureCompression: 0\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 0\n" +
                "      sRGB: 1\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 4\n" +
                "      blockOffset: 1\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 1\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 0\n" +
                "    packables: []\n" +
                "    bindAsDefault: 1\n" +
                "  m_IsVariant: 0\n";
            var omittedDocs = UnityYamlText.Parse(v1Omitted);
            var omittedAtlas = Read("    packables: []\n    bindAsDefault: 1\n");
            omittedAtlas.SetPlatformSettings(new TextureImporterPlatformSettings { name = "tvOS", overridden = true, maxTextureSize = 512 });
            Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
            string omittedWritten = UnityYamlText.Write(omittedDocs);
            Assert.Contains("      sRGB: 1\n    platformSettings:\n    - serializedVersion: 4\n      m_BuildTarget: tvOS\n      m_MaxTextureSize: 512\n", omittedWritten);
            Assert.True(omittedWritten.IndexOf("platformSettings:", StringComparison.Ordinal) < omittedWritten.IndexOf("packingSettings:", StringComparison.Ordinal));
        }

        [Fact]
        public void AddValidatesInputsPreservesOrderAllowsDuplicatesAndAlwaysDirties()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);

            var warnings = new List<string>();
            Application.LogCallback capture = (msg, _, type) =>
            {
                if (msg != null && msg.StartsWith("Attempted to add ", StringComparison.Ordinal))
                    warnings.Add($"{type}:{msg}");
            };
            Application.logMessageReceived += capture;
            try
            {
                // Null and empty arrays log nothing, leave packables empty, and unconditionally mark dirty.
                fresh.Add(null);
                Assert.Empty(fresh.GetPackables());
                Assert.Empty(warnings);
                Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

                fresh.Add(Array.Empty<Object>());
                Assert.Empty(fresh.GetPackables());
                Assert.Empty(warnings);
                Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));

                var texA = new Texture2D(4, 4) { name = "TexA" };
                var spriteA = Sprite.Create(texA, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                spriteA.name = "SpriteA";
                var texB = new Texture2D(2, 2) { name = "TexB" };

                // Supported runtime types append in input order and allow duplicates within and across calls.
                fresh.Add(new Object[] { texA, spriteA, texA });
                Assert.Empty(warnings);
                Assert.Equal(baseDirty + 3, EditorUtility.GetDirtyCount(fresh));
                Assert.Equal(new Object[] { texA, spriteA, texA }, fresh.GetPackables());

                // Mixed array: null, GameObject, RenderTexture, SpriteAtlas, non-folder DefaultAsset are skipped with exact warnings,
                // while valid entries before and after them are still appended in order.
                var invalidGo = new GameObject("BadGo");
                var invalidRt = new RenderTexture(4, 4, 0) { name = "BadRt" };
                var unnamedAtlas = new SpriteAtlas();
                var nonFolderDefault = DefaultAsset.ForFolder("PlainFile");
                try
                {
                    fresh.Add(new Object[] { null, invalidGo, texB, invalidRt, unnamedAtlas, nonFolderDefault, spriteA });
                    Assert.Equal(baseDirty + 4, EditorUtility.GetDirtyCount(fresh));
                    Assert.Equal(new Object[] { texA, spriteA, texA, texB, spriteA }, fresh.GetPackables());
                    Assert.Equal(new[]
                    {
                        "Warning:Attempted to add null object into a Sprite Atlas.",
                        "Warning:Attempted to add invalid object into a Sprite Atlas. BadGo was a GameObject. Only Folder, Texture and Sprite are valid.",
                        "Warning:Attempted to add invalid object into a Sprite Atlas. BadRt was a RenderTexture. Only Folder, Texture and Sprite are valid.",
                        "Warning:Attempted to add invalid object into a Sprite Atlas.  was a SpriteAtlas. Only Folder, Texture and Sprite are valid.",
                        "Warning:Attempted to add invalid object into a Sprite Atlas. PlainFile was a DefaultAsset. Only Folder, Texture and Sprite are valid.",
                    }, warnings);

                    // Control outside the probe: a destroyed valid object is treated as null and skipped cleanly.
                    warnings.Clear();
                    var destroyedTex = new Texture2D(2, 2) { name = "GoneTex" };
                    Object.DestroyImmediate(destroyedTex);
                    fresh.Add(new Object[] { destroyedTex });
                    Assert.Equal(baseDirty + 5, EditorUtility.GetDirtyCount(fresh));
                    Assert.Equal(new[] { "Warning:Attempted to add null object into a Sprite Atlas." }, warnings);

                    // Destroying a previously added runtime packable reports null in its GetPackables slot (matches Unity 6000.0.71f1 Editor).
                    Object.DestroyImmediate(texB);
                    Assert.Equal(new Object[] { texA, spriteA, texA, null, spriteA }, fresh.GetPackables());
                }
                finally
                {
                    Object.DestroyImmediate(invalidGo);
                    Object.DestroyImmediate(invalidRt);
                    Object.DestroyImmediate(unnamedAtlas);
                    Object.DestroyImmediate(nonFolderDefault);
                    Object.DestroyImmediate(spriteA);
                    Object.DestroyImmediate(texA);
                }
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1PackablesPreservesUntouchedAndInsertsWhenOmitted()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    textureSettings:\n" +
                "      serializedVersion: 2\n" +
                "      anisoLevel: 2\n" +
                "      compressionQuality: 50\n" +
                "      maxTextureSize: 1024\n" +
                "      textureCompression: 0\n" +
                "      filterMode: 1\n" +
                "      generateMipMaps: 0\n" +
                "      readable: 0\n" +
                "      crunchedCompression: 0\n" +
                "      sRGB: 1\n" +
                "    platformSettings: []\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    variantMultiplier: 1\n" +
                "    packables:\n" +
                "    - {fileID: 21300000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}\n" +
                "    bindAsDefault: 1\n" +
                "    isAtlasV2: 0\n" +
                "  m_IsVariant: 0\n";

            var docs = UnityYamlText.Parse(v1Source);
            var atlas = new SpriteAtlas();
            atlas.ReadSerialized(TestYaml.DocumentBody(v1Source), null);
            Assert.False(EditorUtility.IsDirty(atlas));
            Assert.False(atlas.PackablesModified);

            // Adding a runtime Texture2D preserves the existing serialized ref and writes {fileID: 0} for the runtime object.
            var runtimeTex = new Texture2D(4, 4) { name = "RuntimeTex" };
            try
            {
                atlas.Add(new Object[] { runtimeTex });
                Assert.True(EditorUtility.IsDirty(atlas));
                Assert.True(atlas.PackablesModified);
                int unlocated = 0;
                Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
                string written = UnityYamlText.Write(docs);
                Assert.Contains(
                    "    variantMultiplier: 1\n" +
                    "    packables:\n" +
                    "    - {fileID: 21300000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}\n" +
                    "    - {fileID: 0}\n" +
                    "    bindAsDefault: 1\n",
                    written);
                Assert.Contains("      padding: 8\n      blockOffset: 2\n", written);
                Assert.Contains("      anisoLevel: 2\n      compressionQuality: 50\n", written);

                // Omitted packables inserts right after variantMultiplier, before bindAsDefault.
                const string v1Omitted =
                    "%YAML 1.1\n" +
                    "%TAG !u! tag:unity3d.com,2011:\n" +
                    "--- !u!687078895 &4343727234628468602\n" +
                    "SpriteAtlas:\n" +
                    "  m_Name: Omitted\n" +
                    "  m_EditorData:\n" +
                    "    serializedVersion: 2\n" +
                    "    variantMultiplier: 1\n" +
                    "    bindAsDefault: 1\n" +
                    "    isAtlasV2: 0\n" +
                    "  m_IsVariant: 0\n";
                var omittedDocs = UnityYamlText.Parse(v1Omitted);
                var omittedAtlas = new SpriteAtlas();
                omittedAtlas.ReadSerialized(TestYaml.DocumentBody(v1Omitted), null);
                omittedAtlas.Add(new Object[] { runtimeTex });
                Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
                Assert.Equal(
                    v1Omitted.Replace("    variantMultiplier: 1\n    bindAsDefault: 1\n",
                        "    variantMultiplier: 1\n    packables:\n    - {fileID: 0}\n    bindAsDefault: 1\n"),
                    UnityYamlText.Write(omittedDocs));
            }
            finally
            {
                Object.DestroyImmediate(runtimeTex);
            }
        }

        [Fact]
        public void RemoveUsesSwapBackOrderHandlesDuplicatesDestroyedAndDirtyCount()
        {
            var fresh = new SpriteAtlas();
            int baseDirty = EditorUtility.GetDirtyCount(fresh);

            var warnings = new List<string>();
            Application.LogCallback capture = (msg, _, type) =>
            {
                if (type == LogType.Warning || type == LogType.Error)
                    warnings.Add($"{type}:{msg}");
            };
            Application.logMessageReceived += capture;
            try
            {
                // Null and empty arrays log nothing, leave packables empty, and mark dirty once each.
                fresh.Remove(null);
                Assert.Empty(fresh.GetPackables());
                Assert.Empty(warnings);
                Assert.Equal(baseDirty + 1, EditorUtility.GetDirtyCount(fresh));

                fresh.Remove(Array.Empty<Object>());
                Assert.Empty(fresh.GetPackables());
                Assert.Empty(warnings);
                Assert.Equal(baseDirty + 2, EditorUtility.GetDirtyCount(fresh));

                var a = new Texture2D(4, 4) { name = "A" };
                var b = new Texture2D(4, 4) { name = "B" };
                var c = Sprite.Create(a, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                c.name = "C";
                var d = new Texture2D(2, 2) { name = "D" };
                var doomed = new Texture2D(2, 2) { name = "Doomed" };
                var invalidGo = new GameObject("BadGo");
                try
                {
                    // Swap-with-last removal of all duplicates: [A, B, C, B, D, doomed] removing B -> [A, doomed(null), C, D].
                    fresh.Add(new Object[] { a, b, c, b, d, doomed }); // +1 -> +3
                    Object.DestroyImmediate(doomed);
                    fresh.Remove(new Object[] { b }); // 1 item -> +2 -> +5
                    Assert.Empty(warnings);
                    Assert.Equal(baseDirty + 5, EditorUtility.GetDirtyCount(fresh));
                    Assert.Equal(new Object[] { a, null, c, d }, fresh.GetPackables());

                    // Missing, invalid, and null entries log nothing, do not remove destroyed objects, and dirty N + 1 times.
                    fresh.Remove(new Object[] { b, invalidGo, null }); // 3 items -> +4 -> +9
                    Assert.Empty(warnings);
                    Assert.Equal(baseDirty + 9, EditorUtility.GetDirtyCount(fresh));
                    Assert.Equal(new Object[] { a, null, c, d }, fresh.GetPackables());

                    // Passing the destroyed object reference matches by identity and swaps last element into its slot.
                    fresh.Remove(new Object[] { doomed }); // 1 item -> +2 -> +11
                    Assert.Empty(warnings);
                    Assert.Equal(baseDirty + 11, EditorUtility.GetDirtyCount(fresh));
                    Assert.Equal(new Object[] { a, d, c }, fresh.GetPackables());

                    // Control outside the probe: trailing consecutive duplicates [B, A, B, B] removing B re-checks swapped-in slots.
                    var trailing = new SpriteAtlas();
                    trailing.Add(new Object[] { b, a, b, b });
                    trailing.Remove(new Object[] { b });
                    Assert.Equal(new Object[] { a }, trailing.GetPackables());
                    Object.DestroyImmediate(trailing);

                    // Removing remaining items empties packables.
                    fresh.Remove(new Object[] { a, c, d }); // 3 items -> +4 -> +15
                    Assert.Empty(fresh.GetPackables());
                    Assert.Equal(baseDirty + 15, EditorUtility.GetDirtyCount(fresh));
                }
                finally
                {
                    Object.DestroyImmediate(invalidGo);
                    Object.DestroyImmediate(d);
                    Object.DestroyImmediate(c);
                    Object.DestroyImmediate(b);
                    Object.DestroyImmediate(a);
                }
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
        }

        [Fact]
        public void NativeDocumentsWriteUpdatesV1PackablesOnRemoveAndEmptiesToFlowSequence()
        {
            const string v1Source =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!687078895 &4343727234628468602\n" +
                "SpriteAtlas:\n" +
                "  m_Name: Sample\n" +
                "  m_EditorData:\n" +
                "    serializedVersion: 2\n" +
                "    packingSettings:\n" +
                "      serializedVersion: 2\n" +
                "      padding: 8\n" +
                "      blockOffset: 2\n" +
                "      allowAlphaSplitting: 0\n" +
                "      enableRotation: 0\n" +
                "      enableTightPacking: 1\n" +
                "      enableAlphaDilation: 1\n" +
                "    variantMultiplier: 1\n" +
                "    packables:\n" +
                "    - {fileID: 21300000, guid: b828055b6a1d50b968e77fc6406e2b5c, type: 3}\n" +
                "    bindAsDefault: 1\n" +
                "    isAtlasV2: 0\n" +
                "  m_IsVariant: 0\n";

            var runtimeTex = new Texture2D(4, 4) { name = "RuntimeTex" };
            try
            {
                // Adding and then removing a runtime object restores exact serialized packables and unrelated settings.
                var docs = UnityYamlText.Parse(v1Source);
                var atlas = new SpriteAtlas();
                atlas.ReadSerialized(TestYaml.DocumentBody(v1Source), null);
                atlas.Add(new Object[] { runtimeTex });
                atlas.Remove(new Object[] { runtimeTex });
                int unlocated = 0;
                Assert.True(NativeDocuments.Write(docs[0], atlas, null, null, ref unlocated));
                Assert.Equal(v1Source, UnityYamlText.Write(docs));

                // Removing from a sparse V1 atlas with omitted packables inserts `packables: []` after variantMultiplier.
                const string v1Omitted =
                    "%YAML 1.1\n" +
                    "%TAG !u! tag:unity3d.com,2011:\n" +
                    "--- !u!687078895 &4343727234628468602\n" +
                    "SpriteAtlas:\n" +
                    "  m_Name: Omitted\n" +
                    "  m_EditorData:\n" +
                    "    serializedVersion: 2\n" +
                    "    variantMultiplier: 1\n" +
                    "    bindAsDefault: 1\n" +
                    "    isAtlasV2: 0\n" +
                    "  m_IsVariant: 0\n";
                var omittedDocs = UnityYamlText.Parse(v1Omitted);
                var omittedAtlas = new SpriteAtlas();
                omittedAtlas.ReadSerialized(TestYaml.DocumentBody(v1Omitted), null);
                omittedAtlas.Remove(new Object[] { runtimeTex });
                Assert.True(NativeDocuments.Write(omittedDocs[0], omittedAtlas, null, null, ref unlocated));
                Assert.Equal(
                    v1Omitted.Replace("    variantMultiplier: 1\n    bindAsDefault: 1\n",
                        "    variantMultiplier: 1\n    packables: []\n    bindAsDefault: 1\n"),
                    UnityYamlText.Write(omittedDocs));
            }
            finally
            {
                Object.DestroyImmediate(runtimeTex);
            }
        }

        [Fact]
        public void ANullAtlasThrows()
        {
            SpriteAtlas none = null;
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackables()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.Add(Array.Empty<Object>())).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.Add(null)).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.Remove(Array.Empty<Object>())).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.Remove(null)).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackingSettings()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.SetPackingSettings(default)).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetTextureSettings()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.SetTextureSettings(default)).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPlatformSettings("Android")).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPlatformSettings(null)).ParamName);
            var src = new TextureImporterPlatformSettings { name = "iPhone", overridden = true };
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.SetPlatformSettings(src)).ParamName);
            Assert.Equal("iOS", src.name);
            Assert.Throws<NullReferenceException>(() => none.SetPlatformSettings(null));
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.SetIncludeInBuild(true)).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.SetIncludeInBuild(false)).ParamName);
            Assert.Throws<ArgumentNullException>(() => none.GetMasterAtlas());
            Assert.Throws<ArgumentNullException>(() => none.IsIncludeInBuild());
        }
    }
}
