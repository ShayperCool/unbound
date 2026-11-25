using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A texture importer's single-sprite pivot and border, as the Unity 6000.0.71f1
    /// Editor reads and sets them (tests/projects/projectwrites 08.importer.pivot).</summary>
    public class TextureImporterSpriteValuesTests
    {
        static TextureImporter Importer(string settings) => (TextureImporter)AssetImporter.FromMeta("Assets/Pivot.png", "8123456789abcdef0123456789abcdef",
            "TextureImporter", TestYaml.DocumentBody("TextureImporter:\n  serializedVersion: 13\n" + settings + "  userData: \n"));

        [Fact]
        public void ThePivotAndBorderAreTheMetasAndSetOnes()
        {
            var importer = Importer("  spritePivot: {x: 0.3, y: 0.6}\n  spriteBorder: {x: 1, y: 2, z: 3, w: 4}\n");
            Assert.Equal(new Vector2(0.3f, 0.6f), importer.spritePivot);
            Assert.Equal(new Vector4(1, 2, 3, 4), importer.spriteBorder);
            importer.spritePivot = new Vector2(0.25f, 0.75f);
            importer.spriteBorder = new Vector4(5, 6, 7, 8);
            Assert.Equal(new Vector2(0.25f, 0.75f), importer.spritePivot);
            Assert.Equal(new Vector4(5, 6, 7, 8), importer.spriteBorder);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.Equal(new Vector2(0.25f, 0.75f), settings.spritePivot);
            Assert.Equal(new Vector4(5, 6, 7, 8), settings.spriteBorder);
            settings.spritePivot = new Vector2(0, 1);
            importer.SetTextureSettings(settings);
            Assert.Equal(new Vector2(0, 1), importer.spritePivot);
            Assert.Equal(new Vector4(5, 6, 7, 8), importer.spriteBorder);
        }

        /// <summary>Without the keys: the centre and no border, as a new texture's .meta has them.</summary>
        [Fact]
        public void AMetaWithoutThemReadsTheNewTexturesValues()
        {
            var importer = Importer("");
            Assert.Equal(new Vector2(0.5f, 0.5f), importer.spritePivot);
            Assert.Equal(Vector4.zero, importer.spriteBorder);
        }
    }
}
