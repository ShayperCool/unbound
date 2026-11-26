using UnityEditor;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A sprite's first import (Unity 6000.0.71f1 Editor, tests/projects/spritemeta): the sprite ID
    /// the Editor gives a sprite, and a sprite's NPOT scaling read as none whatever its .meta holds.
    /// </summary>
    public sealed class SpriteImportsTests
    {
        static TextureImporter Sprite(string npot) => (TextureImporter)AssetImporter.FromMeta("Assets/Npot.png", "6123456789abcdef0123456789abcdef", "TextureImporter",
            TestYaml.DocumentBody("TextureImporter:\n  textureType: 8\n  spriteMode: 1\n  nPOTScale: " + npot + "\n"));

        [Fact]
        public void SpriteIdsAreTheEditors()
        {
            // A single sprite's, whatever the texture (sprites.EmptyId), and a 2D project's
            // sliced sprite NewGaps_0 of file id -2708432719997058213 (open.NewGaps).
            Assert.Equal("5e97eb03825dee720800000000000000", SpriteImports.SingleSpriteId);
            Assert.Equal("b53c7828ac6b96ad0800000000000000", SpriteImports.SpriteId(-2708432719997058213));
            Assert.Equal("f7a13a38428dff560800000000000000", SpriteImports.SpriteId(7349830768230865535));
        }

        [Fact]
        public void ASpritesNpotScalingReadsAsNone()
        {
            var sprite = Sprite("1");
            Assert.Equal(TextureImporterNPOTScale.None, sprite.npotScale);
            var settings = new TextureImporterSettings();
            sprite.ReadTextureSettings(settings);
            Assert.Equal(TextureImporterNPOTScale.None, settings.npotScale);
            // A Default texture reads what its .meta holds.
            var texture = (TextureImporter)AssetImporter.FromMeta("Assets/D.png", "7123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureType: 0\n  nPOTScale: 1\n"));
            Assert.Equal(TextureImporterNPOTScale.ToNearest, texture.npotScale);
            // Another release reads the sprite's too (unmeasured).
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.Equal(TextureImporterNPOTScale.ToNearest, Sprite("1").npotScale);
        }
    }
}
