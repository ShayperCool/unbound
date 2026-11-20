using System;
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
        public void ANullAtlasThrows()
        {
            SpriteAtlas none = null;
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackables()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackingSettings()).ParamName);
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetTextureSettings()).ParamName);
            Assert.Throws<ArgumentNullException>(() => none.GetMasterAtlas());
            Assert.Throws<ArgumentNullException>(() => none.IsIncludeInBuild());
        }
    }
}
