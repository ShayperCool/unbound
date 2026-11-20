using System;
using UnityEditor.U2D;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's sprite atlas extensions over an atlas read from its file, as the Unity
    /// 6000.0.71f1 Editor answers them (tests/projects/spriteatlaseditor): "Include in Build" from
    /// a V1 atlas's bindAsDefault (on when absent), no master for an atlas that is no variant, and
    /// ArgumentNullException for a null atlas. Packable resolution needs the asset database and is
    /// covered by the probe.
    /// </summary>
    public sealed class SpriteAtlasExtensionsTests
    {
        static SpriteAtlas Read(string editorData)
        {
            var body = TestYaml.DocumentBody("SpriteAtlas:\n  m_Name: Probe\n  m_EditorData:\n" + editorData
                + "  m_MasterAtlas: {fileID: 0}\n  m_Tag: Probe\n  m_IsVariant: 0\n");
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
        public void ANullAtlasThrows()
        {
            SpriteAtlas none = null;
            Assert.Equal("spriteAtlas", Assert.Throws<ArgumentNullException>(() => none.GetPackables()).ParamName);
            Assert.Throws<ArgumentNullException>(() => none.GetMasterAtlas());
            Assert.Throws<ArgumentNullException>(() => none.IsIncludeInBuild());
        }
    }
}
