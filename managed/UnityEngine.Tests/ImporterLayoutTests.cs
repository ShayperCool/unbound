using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An importer's settings written in the release's layout whatever layout the file had (Unity
    /// 6000.0.71f1 Editor, tests/projects/importermeta): keys in the layout's order, a missing key at
    /// its default, a key the layout lacks left out, per-platform entries laid out alike, the
    /// release's serialized versions and platform names.
    /// </summary>
    public class ImporterLayoutTests
    {
        const string Layout = "TextureImporter:\n"
            + "  internalIDToNameTable: []\n"
            + "  externalObjects: {}\n"
            + "  serializedVersion: 13\n"
            + "  mipmaps:\n"
            + "    mipMapMode: 0\n"
            + "    mipMapsPreserveCoverage: 0\n"
            + "    alphaTestReferenceValue: 0.5\n"
            + "  isReadable: 0\n"
            + "  spritePivot: {x: 0.5, y: 0.5}\n"
            + "  swizzle: 50462976\n"
            + "  platformSettings:\n"
            + "  spriteSheet:\n"
            + "    sprites: []\n"
            + "    spriteCustomMetadata:\n"
            + "      entries: []\n"
            + "  userData: \n";

        const string Entry = "  - serializedVersion: 4\n    buildTarget: (platform)\n    maxTextureSize: 2048\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n";

        static ImporterLayout Texture() => new ImporterLayout("TextureImporter", Layout, Entry);

        static string Written(YText root) => UnityYamlText.WriteTopLevel(root);

        static (YText Root, YText Section) Read(string section)
        {
            var root = UnityYamlText.ParseTopLevel("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + section);
            return (root, root.Get("TextureImporter"));
        }

        static string Arranged(string section)
        {
            var (root, read) = Read(section);
            var layout = Texture();
            var arranged = layout.Arrange(read, new List<string>());
            root.Set("TextureImporter", arranged);
            Assert.Empty(layout.Normalize(arranged));
            return Written(root);
        }

        [Fact]
        public void MissingKeysTakeTheirDefaultsInTheLayoutsPlaces()
        {
            var text = Arranged("TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 13\n"
                + "  mipmaps:\n    mipMapMode: 1\n    alphaTestReferenceValue: 0.25\n  isReadable: 1\n  spritePivot: {x: 0, y: 1}\n"
                + "  platformSettings:\n  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 512\n"
                + "  spriteSheet:\n    sprites: []\n  userData: kept\n");
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n"
                + "  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 13\n"
                + "  mipmaps:\n    mipMapMode: 1\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.25\n"
                + "  isReadable: 1\n  spritePivot: {x: 0, y: 1}\n  swizzle: 50462976\n"
                + "  platformSettings:\n  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 512\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
                + "  spriteSheet:\n    sprites: []\n    spriteCustomMetadata:\n      entries: []\n  userData: kept\n", text);
        }

        [Fact]
        public void KeysTheLayoutLacksAreLeftOutAndTheRestIsInItsOrder()
        {
            var text = Arranged("TextureImporter:\n  isReadable: 1\n  legacySetting: 3\n  internalIDToNameTable: []\n  externalObjects: {}\n"
                + "  serializedVersion: 13\n  mipmaps:\n    oldMipSetting: 1\n    alphaTestReferenceValue: 0.5\n    mipMapMode: 0\n    mipMapsPreserveCoverage: 1\n"
                + "  spritePivot: {x: 0.5, y: 0.5}\n  swizzle: 50462976\n"
                + "  platformSettings:\n  - serializedVersion: 4\n    buildTarget: Standalone\n    oldPlatformKey: 7\n    maxTextureSize: 2048\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
                + "  spriteSheet:\n    sprites: []\n    spriteCustomMetadata:\n      entries: []\n  userData: \n");
            Assert.DoesNotContain("legacySetting", text);
            Assert.DoesNotContain("oldMipSetting", text);
            Assert.DoesNotContain("oldPlatformKey", text);
            Assert.Contains("  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    mipMapsPreserveCoverage: 1\n    alphaTestReferenceValue: 0.5\n  isReadable: 1\n  spritePivot:", text);
        }

        [Fact]
        public void TheFilesOwnListsAndMapsAreKept()
        {
            var text = Arranged("TextureImporter:\n  internalIDToNameTable:\n  - first:\n      213: 7\n    second: Sprite_0\n"
                + "  externalObjects:\n  - first:\n      type: UnityEngine:Material\n    second: {fileID: 0}\n"
                + "  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n"
                + "  isReadable: 0\n  spritePivot: {x: 0.5, y: 0.5}\n  swizzle: 50462976\n  platformSettings: []\n"
                + "  spriteSheet:\n    sprites:\n    - serializedVersion: 2\n      name: Sprite_0\n    spriteCustomMetadata:\n      entries: []\n  userData: \n");
            Assert.Contains("  internalIDToNameTable:\n  - first:\n      213: 7\n    second: Sprite_0\n", text);
            Assert.Contains("    sprites:\n    - serializedVersion: 2\n      name: Sprite_0\n", text);
            Assert.Contains("  platformSettings: []\n", text);
            Assert.Contains("  externalObjects:\n  - first:\n      type: UnityEngine:Material\n    second: {fileID: 0}\n", text);
        }

        [Fact]
        public void AStructureTheLayoutIsNotMeasuredForKeepsTheFilesLayout()
        {
            // A value of another kind, no serialized version, no platform list: each keeps the
            // file as it is (patched in place), and is named.
            foreach (var (section, found) in new[]
            {
                ("TextureImporter:\n  serializedVersion: 13\n  mipmaps: 1\n  platformSettings: []\n  isReadable: 0\n", "mipmaps: a Scalar where the release has a Map"),
                ("TextureImporter:\n  mipmaps:\n    mipMapMode: 0\n  platformSettings: []\n  isReadable: 0\n", "serializedVersion: missing"),
                ("TextureImporter:\n  serializedVersion: 13\n  isReadable: 0\n", "platformSettings: missing"),
            })
            {
                var (root, read) = Read(section);
                var settings = TestYaml.DocumentBody(section.Replace("isReadable: 0", "isReadable: 1"));
                ImporterMeta.Patch(read, settings, "", new List<string>());
                var older = ImporterMeta.LayOut(root, "TextureImporter", Texture(), settings, _ => { }, new List<string>());
                Assert.Contains(found, older);
                Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + section.Replace("isReadable: 0", "isReadable: 1"), Written(root));
            }
        }

        [Fact]
        public void TheReleasesVersionsAndPlatformNamesAreWritten()
        {
            var (root, read) = Read("TextureImporter:\n  serializedVersion: 12\n  platformSettings:\n"
                + "  - serializedVersion: 3\n    buildTarget: Nintendo Switch\n    maxTextureSize: 2048\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n");
            var layout = Texture();
            var arranged = layout.Arrange(read, new List<string>());
            root.Set("TextureImporter", arranged);
            // The texture's own older version is measured; an entry's is not, and is reported.
            Assert.Equal(new List<string> { "platformSettings[0].serializedVersion 3 -> 4" }, layout.Normalize(arranged));
            var text = Written(root);
            Assert.Contains("  serializedVersion: 13\n", text);
            Assert.Contains("  - serializedVersion: 4\n    buildTarget: Switch\n", text);
        }

        [Fact]
        public void AnotherImportersOlderVersionIsReportedAsNotMeasured()
        {
            var layout = new ImporterLayout("AudioImporter", "AudioImporter:\n  externalObjects: {}\n  serializedVersion: 8\n  forceToMono: 0\n", null);
            var section = layout.Arrange(UnityYamlText.ParseTopLevel("AudioImporter:\n  serializedVersion: 6\n  forceToMono: 1\n").Get("AudioImporter"), new List<string>());
            Assert.Equal(new List<string> { "serializedVersion 6 -> 8" }, layout.Normalize(section));
            Assert.Equal("8", section.Get("serializedVersion").Text);
            Assert.Equal("1", section.Get("forceToMono").Text);
        }

        [Fact]
        public void AFileOfAnOlderVersionThanMeasuredKeepsItsLayout()
        {
            const string section = "TextureImporter:\n  serializedVersion: 11\n  textureSettings:\n    wrapMode: 1\n  isReadable: 0\n  platformSettings: []\n";
            var (root, read) = Read(section);
            var settings = TestYaml.DocumentBody("TextureImporter:\n  serializedVersion: 11\n  textureSettings:\n    wrapMode: 1\n  isReadable: 1\n");
            var unwritten = new List<string>();
            ImporterMeta.Patch(read, settings, "", unwritten);
            var older = ImporterMeta.LayOut(root, "TextureImporter", Texture(), settings, _ => { }, unwritten);
            Assert.Contains("serializedVersion 11 -> 13", older);
            // The legacy key the import reads stays, the change is in place.
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + section.Replace("isReadable: 0", "isReadable: 1"), Written(root));
        }

        [Fact]
        public void ALaidOutFileReportsOnlySettingsOfNeitherTheFileNorTheLayout()
        {
            string file = Layout.Replace("  isReadable: 0\n", "  isReadable: 0\n  legacySetting: 3\n");
            var (root, read) = Read(file);
            var settings = TestYaml.DocumentBody(file.Replace("  isReadable: 0\n", "  isReadable: 1\n").Replace("  userData: \n", "  userData: \n  unknownNew: 1\n"));
            var unwritten = new List<string>();
            ImporterMeta.Patch(read, settings, "", unwritten);
            Assert.Equal(new List<string> { "unknownNew" }, unwritten);
            Assert.Empty(ImporterMeta.LayOut(root, "TextureImporter", Texture(), settings, _ => { }, unwritten));
            Assert.Equal(new List<string> { "unknownNew" }, unwritten);
            var text = Written(root);
            Assert.Contains("  isReadable: 1\n", text);
            Assert.DoesNotContain("legacySetting", text);
        }

        [Fact]
        public void ACurrentFileLackingOnlyMeasuredKeysIsLaidOut()
        {
            // swizzle and the custom sprite metadata are measured missing (tests/projects/importermeta change.Missing).
            string file = Layout.Replace("  swizzle: 50462976\n", "").Replace("    spriteCustomMetadata:\n      entries: []\n", "");
            var (root, read) = Read(file);
            var settings = TestYaml.DocumentBody(file.Replace("  isReadable: 0\n", "  isReadable: 1\n"));
            ImporterMeta.Patch(read, settings, "", new List<string>());
            Assert.Empty(ImporterMeta.LayOut(root, "TextureImporter", Texture(), settings, _ => { }, new List<string>()));
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + Layout.Replace("  isReadable: 0\n", "  isReadable: 1\n"), Written(root));
        }

        [Fact]
        public void AKeyTheEditorIsNotMeasuredFillingKeepsTheFilesLayout()
        {
            // isReadable is not among the measured missing keys; a file of an older version
            // lacking a measured one is not measured either.
            foreach (var (file, found) in new[]
            {
                (Layout.Replace("  isReadable: 0\n", ""), "isReadable: missing"),
                (Layout.Replace("  serializedVersion: 13\n", "  serializedVersion: 12\n").Replace("  swizzle: 50462976\n", ""), "swizzle: missing"),
            })
            {
                var (root, read) = Read(file);
                var settings = TestYaml.DocumentBody(file);
                var older = ImporterMeta.LayOut(root, "TextureImporter", Texture(), settings, _ => { }, new List<string>());
                Assert.Contains(found, older);
                Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + file, Written(root));
            }
        }

        [Fact]
        public void ASettingTheFileLackedIsPlacedAfterTheLayout()
        {
            var (root, read) = Read("TextureImporter:\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n  isReadable: 0\n");
            var settings = TestYaml.DocumentBody("TextureImporter:\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    mipMapsPreserveCoverage: 1\n  isReadable: 1\n");
            var unwritten = new List<string>();
            ImporterMeta.Patch(read, settings, "", unwritten);
            Assert.Equal(new List<string> { "mipmaps.mipMapsPreserveCoverage" }, unwritten);
            var layout = Texture();
            var arranged = layout.Arrange(read, new List<string>());
            root.Set("TextureImporter", arranged);
            var placed = new List<string>();
            ImporterMeta.Patch(arranged, settings, "", placed);
            Assert.Empty(placed);
            var text = Written(root);
            Assert.Contains("    mipMapsPreserveCoverage: 1\n", text);
            Assert.Contains("  isReadable: 1\n", text);
        }
    }
}
