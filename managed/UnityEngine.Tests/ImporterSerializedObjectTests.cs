using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A SerializedObject over a TextureImporter iterates the importer's native tree over
    /// its .meta section, and an applied edit goes back into the section under the .meta's names
    /// (tests/projects/importerserialized measures the Unity 6000.0.71f1 Editor).</summary>
    [Collection("EditorState")]
    public sealed class ImporterSerializedObjectTests
    {
        const string SheetMeta =
            "TextureImporter:\n" +
            "  internalIDToNameTable:\n" +
            "  - first:\n      213: 1001\n    second: a\n" +
            "  externalObjects: {}\n" +
            "  serializedVersion: 13\n" +
            "  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n" +
            "  spriteMode: 2\n  spritePixelsToUnits: 100\n  textureType: 8\n" +
            "  spriteSheet:\n" +
            "    serializedVersion: 2\n" +
            "    sprites:\n" +
            "    - serializedVersion: 2\n      name: a\n      rect:\n        serializedVersion: 2\n        x: 0\n        y: 0\n        width: 32\n        height: 32\n" +
            "      alignment: 0\n      pivot: {x: 0.5, y: 0.5}\n      spriteID: 0123456789abcdef0123456789abcdef\n      internalID: 11\n" +
            "    - serializedVersion: 2\n      name: b\n      rect:\n        serializedVersion: 2\n        x: 32\n        y: 0\n        width: 32\n        height: 32\n" +
            "      alignment: 9\n      pivot: {x: 0.25, y: 0.75}\n      spriteID: fedcba9876543210fedcba9876543210\n      internalID: 22\n" +
            "    nameFileIdTable:\n      a: 11\n      b: 22\n" +
            "  userData: \n";

        static TextureImporter Sheet() => (TextureImporter)AssetImporter.FromMeta("Assets/Sheet.png", "9123456789abcdef0123456789abcdef", "TextureImporter",
            TestYaml.DocumentBody(SheetMeta));

        /// <summary>Importer type trees are measured for 6000.0 only: under another family a
        /// SerializedObject of an importer has none of their properties, and that is recorded as not
        /// implemented instead of passing silently; back under the measured family the same importer
        /// class reads its tree again. An importer class without a measured tree reflects in every
        /// family and is not reported.</summary>
        [Fact]
        public void AnotherFamilysImporterPropertiesAreUnmeasuredAndSaid()
        {
            static int Said(string type) => Unbound.Runtime.Stubs.HitReport()
                .Where(h => h.Key == Unbound.Runtime.ImporterTypeTree.UnmeasuredNotice(type)).Select(h => h.Value).SingleOrDefault();
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                int before = Said("TextureImporter");
                Assert.Null(new SerializedObject(Sheet()).FindProperty("m_SpritePixelsToUnits"));
                Assert.Equal(before + 1, Said("TextureImporter"));
                var other = AssetImporter.FromMeta("Assets/Other.bin", "a123456789abcdef0123456789abcdef", "DefaultImporter", TestYaml.DocumentBody("DefaultImporter:\n  externalObjects: {}\n  userData: \n"));
                new SerializedObject(other);
                Assert.Equal(0, Said("DefaultImporter"));
            }
            using (TestRelease.Pin(TestRelease.Measured))
            {
                int before = Said("TextureImporter");
                Assert.NotNull(new SerializedObject(Sheet()).FindProperty("m_SpritePixelsToUnits"));
                Assert.Equal(before, Said("TextureImporter"));
            }
        }

        /// <summary>Red before the importer's tree: the SerializedObject reflected over the managed
        /// importer and had no m_SpriteSheet (the 2D sprite data provider threw).</summary>
        [Fact]
        public void TheSpriteSheetAndGroupedFieldsReadUnderTheTreesNames()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var so = new SerializedObject(Sheet());
            var sprites = so.FindProperty("m_SpriteSheet.m_Sprites");
            Assert.NotNull(sprites);
            Assert.Equal(2, sprites.arraySize);
            Assert.Equal("b", so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Name").stringValue);
            Assert.Equal(new Rect(32, 0, 32, 32), so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Rect").rectValue);
            Assert.Equal(new Vector2(0.25f, 0.75f), so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Pivot").vector2Value);
            Assert.Equal(22L, so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_InternalID").longValue);
            Assert.Equal("fedcba9876543210fedcba9876543210", so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_SpriteID").stringValue);
            // A map with plain keys reads as pairs; a field of the .meta's mipmaps group at the top.
            Assert.Equal(2, so.FindProperty("m_SpriteSheet.m_NameFileIdTable").arraySize);
            Assert.Equal("a", so.FindProperty("m_SpriteSheet.m_NameFileIdTable.Array.data[0].first").stringValue);
            Assert.Equal(11L, so.FindProperty("m_SpriteSheet.m_NameFileIdTable.Array.data[0].second").longValue);
            Assert.Equal(1, so.FindProperty("m_EnableMipMap").intValue);
            // A pair written as its one entry ({213: 1001}).
            Assert.Equal(213, so.FindProperty("m_InternalIDToNameTable.Array.data[0].first.first").intValue);
            Assert.Equal(1001L, so.FindProperty("m_InternalIDToNameTable.Array.data[0].first.second").longValue);
            Assert.Equal("a", so.FindProperty("m_InternalIDToNameTable.Array.data[0].second").stringValue);
        }

        [Fact]
        public void AnAppliedEditGoesBackUnderTheMetasNames()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var importer = Sheet();
            var so = new SerializedObject(importer);
            so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Name").stringValue = "renamed";
            so.FindProperty("m_SpritePixelsToUnits").floatValue = 64;
            so.FindProperty("m_EnableMipMap").intValue = 0;
            so.FindProperty("m_InternalIDToNameTable.Array.data[0].second").stringValue = "first";
            Assert.True(so.ApplyModifiedPropertiesWithoutUndo());

            Assert.True(importer.m_SettingsChanged);
            Assert.Equal(64f, importer.spritePixelsPerUnit);
            var section = importer.m_Settings;
            Assert.Equal("renamed", MetaSettings.Find(section, "spriteSheet.sprites").Items[1].Get("name").AsString());
            // What the tree does not have stays: the elements' serializedVersion, the group.
            Assert.Equal("2", MetaSettings.Find(section, "spriteSheet.sprites").Items[1].Get("serializedVersion").AsString());
            Assert.Equal("0", MetaSettings.Find(section, "mipmaps.enableMipMap").AsString());
            Assert.Null(section.Get("enableMipMap"));
            var entry = MetaSettings.Find(section, "internalIDToNameTable").Items[0];
            Assert.Equal(new[] { "213" }, entry.Get("first").Keys);
            Assert.Equal("1001", entry.Get("first").Get("213").AsString());
            Assert.Equal("first", entry.Get("second").AsString());
            // Nothing under the tree's names and nothing of the import's output reaches the section.
            Assert.DoesNotContain(section.Keys, k => k.StartsWith("m_") || k == "output" || k == "usedFileIDs");
            Assert.Equal("renamed", new SerializedObject(importer).FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Name").stringValue);
        }
    }
}
