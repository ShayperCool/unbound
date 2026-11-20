using System;
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
        public void ANullAtlasThrows()
        {
            SpriteAtlas none = null;
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackables()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackingSettings()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetTextureSettings()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPlatformSettings("Android")).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPlatformSettings(null)).ParamName);
            Assert.Throws<ArgumentNullException>(() => none.GetMasterAtlas());
            Assert.Throws<ArgumentNullException>(() => none.IsIncludeInBuild());
        }
    }
}
