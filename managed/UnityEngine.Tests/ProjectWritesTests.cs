using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The rules Unbound writes a project's files by, as the Editor writes them
    /// (tests/projects/projectwrites): free names, which paths are the project's own assets, and
    /// .meta and settings text edits that keep everything else of the file.</summary>
    public class ProjectWritesTests
    {
        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        /// <summary>A path a refresh could not import (its .meta lost the GUID line) keeps its
        /// listing entry and is reported; a control path gone from disk is dropped from the
        /// listing and still answered as recently deleted.</summary>
        [Fact]
        public void ARefreshKeepsWhatItCouldNotImportAndDropsWhatIsGone()
        {
            const string broken = "0000000000000000000000000000000a", gone = "0000000000000000000000000000000b";
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                UnityEditor.AssetDatabase.List("[{\"guid\":\"" + broken + "\",\"path\":\"Assets/Broken.txt\",\"kind\":\"text\"},"
                    + "{\"guid\":\"" + gone + "\",\"path\":\"Assets/Gone.txt\",\"kind\":\"text\"}]");
                UnityEditor.AssetDatabase.ApplyRefresh("{\"assets\":[{\"path\":\"Assets/Broken.txt\",\"guid\":\"\",\"error\":\"Assets/Broken.txt.meta has no guid\"},"
                    + "{\"path\":\"Assets/Gone.txt\",\"guid\":\"\",\"removed\":\"" + gone + "\"}]}");
                Assert.Equal(broken, UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Broken.txt", UnityEditor.AssetPathToGUIDOptions.OnlyExistingAssets));
                Assert.Equal("Assets/Broken.txt", UnityEditor.AssetDatabase.GUIDToAssetPath(broken));
                Assert.Equal(new[] { "Warning: Unbound: Assets/Broken.txt.meta has no guid" }, logs.Lines);
                Assert.Equal("", UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Gone.txt", UnityEditor.AssetPathToGUIDOptions.OnlyExistingAssets));
                Assert.Equal(gone, UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Gone.txt"));
                Assert.Equal("Assets/Gone.txt", UnityEditor.AssetDatabase.GUIDToAssetPath(gone));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                UnityEditor.AssetDatabase.Invalidate();
            }
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(System.Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(logType + ": " + string.Format(format, args));
        }

        [Fact]
        public void UniqueNamesTakeTheFirstFreeNumberAndReplaceANumberTheNameHas()
        {
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Assets/A/Data.asset", "Assets/A/Data 1.asset", "Assets/A/Data2.asset" };
            Assert.Equal("Assets/A/Fresh.asset", AssetPaths.Unique("Assets/A", "Fresh.asset", taken.Contains));
            Assert.Equal("Assets/A/Data 2.asset", AssetPaths.Unique("Assets/A", "Data.asset", taken.Contains));
            Assert.Equal("Assets/A/Data 2.asset", AssetPaths.Unique("Assets/A", "Data 1.asset", taken.Contains));
            // No space before the number: the number is part of the name.
            Assert.Equal("Assets/A/Data2 1.asset", AssetPaths.Unique("Assets/A", "Data2.asset", taken.Contains));
            // Case is ignored, the name keeps its own.
            Assert.Equal("Assets/A/data 2.ASSET", AssetPaths.Unique("Assets/A", "data.ASSET", taken.Contains));
            // A folder has no extension.
            Assert.Equal("Assets/A 1", AssetPaths.Unique("Assets", "A", new HashSet<string> { "Assets/A" }.Contains));
        }

        [Fact]
        public void OnlyTheProjectsOwnAssetsAreWritable()
        {
            var roots = new[] { ("Assets", true), ("Packages/com.my.embedded", true), ("Packages/com.unity.ugui", false) };
            Assert.True(AssetPaths.Editable(roots, "Assets/X.asset"));
            Assert.True(AssetPaths.Editable(roots, "assets/x.asset"));
            Assert.True(AssetPaths.Editable(roots, "Packages/com.my.embedded/Runtime/X.cs"));
            // Roots are no assets; a read-only package is not written.
            Assert.False(AssetPaths.Editable(roots, "Assets"));
            Assert.False(AssetPaths.Editable(roots, "Packages/com.my.embedded"));
            Assert.False(AssetPaths.Editable(roots, "Packages/com.unity.ugui/Runtime/X.cs"));
            Assert.False(AssetPaths.Editable(roots, "Packages/com.unknown/X.cs"));
            Assert.False(AssetPaths.Editable(roots, "Elsewhere/X.cs"));
            // The project's settings, a path escaping Assets and an absolute path are no assets.
            Assert.False(AssetPaths.Editable(roots, "ProjectSettings/TagManager.asset"));
            Assert.False(AssetPaths.Editable(roots, "Assets/../ProjectSettings/TagManager.asset"));
            Assert.False(AssetPaths.Editable(roots, "Assets/./X.asset"));
            Assert.False(AssetPaths.Editable(roots, "/project/ProjectSettings/TagManager.asset"));
            // Hidden names, and what is inside a folder plug-in (the plug-in itself is an asset).
            Assert.False(AssetPaths.Editable(roots, "Assets/.git/X.txt"));
            Assert.False(AssetPaths.Editable(roots, "Assets/Art/.DS_Store"));
            Assert.False(AssetPaths.Editable(roots, "Assets/Backup~/X.png"));
            Assert.True(AssetPaths.Editable(roots, "Assets/Plugins/X.bundle"));
            Assert.False(AssetPaths.Editable(roots, "Assets/Plugins/X.bundle/Contents/Info.plist"));
        }

        [Fact]
        public void AGuidIsReplacedAndTheFileWrittenWithLf()
        {
            const string old = "0123456789abcdef0123456789abcdef", fresh = "fedcba9876543210fedcba9876543210";
            Assert.Equal($"fileFormatVersion: 2\nguid: {fresh}\nDefaultImporter:\n", AssetPaths.WithGuid($"fileFormatVersion: 2\nguid: {old}\nDefaultImporter:\n", fresh));
            // A CRLF file is written back with LF, as the Editor writes it.
            Assert.Equal($"fileFormatVersion: 2\nguid: {fresh}\nTextScriptImporter:\n", AssetPaths.WithGuid($"fileFormatVersion: 2\r\nguid: {old}\r\nTextScriptImporter:\r\n", fresh));
            // A script's .meta ends after its GUID.
            Assert.Equal($"fileFormatVersion: 2\nguid: {fresh}", AssetPaths.WithGuid($"fileFormatVersion: 2\nguid: {old}", fresh));
            Assert.Null(AssetPaths.WithGuid("fileFormatVersion: 2\nfolderAsset: yes\n", fresh));
        }

        [Fact]
        public void LabelsAreWrittenAfterTheGuidInOrdinalOrderWithRepeats()
        {
            const string meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n  userData: \n";
            var labelled = AssetPaths.WithLabels(meta, new[] { "Zeta", "alpha", "Zeta" });
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- Zeta\n- Zeta\n- alpha\nTextureImporter:\n  userData: \n", labelled);
            // New labels replace the old ones; none removes the block.
            Assert.Equal(meta.Replace("TextureImporter:", "labels:\n- b\nTextureImporter:"), AssetPaths.WithLabels(labelled, new[] { "b" }));
            Assert.Equal(meta, AssetPaths.WithLabels(labelled, new string[0]));
            var crlf = meta.Replace("\n", "\r\n");
            Assert.Equal(meta.Replace("TextureImporter:", "labels:\n- a\nTextureImporter:"), AssetPaths.WithLabels(crlf, new[] { "a" }));
            Assert.Equal(meta, AssetPaths.WithLabels(crlf, null));
            // Labels are written as given, unquoted, as the Editor writes them.
            Assert.Contains("- #x\n- a: b\n", AssetPaths.WithLabels(meta, new[] { "a: b", "#x" }));
        }

        /// <summary>A script's .meta ends after its GUID; written again it gets its importer's
        /// section (tests/projects/projectwrites 09.labels.script).</summary>
        [Fact]
        public void AScriptsMetaWrittenAgainGetsItsSection()
        {
            const string script = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef";
            const string section = "MonoImporter:\n  externalObjects: {}\n";
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- script\n" + section,
                AssetPaths.WithLabels(script, new[] { "script" }, section));
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + section, AssetPaths.WithLabels(script, null, section));
            // A .meta that has its section keeps it as it is.
            var textMeta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextScriptImporter:\n  userData: \n";
            Assert.Equal(textMeta, AssetPaths.WithLabels(textMeta, null, section));
        }

        /// <summary>A plug-in's new .meta has its GUID alone, as a script's (tests/projects/projectwrites
        /// exit.Assets/Startup/Plugins/*): no importer section, also once labelled.</summary>
        [Fact]
        public void APluginsGuidOnlyMetaHasNoImporterSection()
        {
            const string plugin = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef";
            Assert.False(AssetPaths.HasImporterSection(plugin));
            Assert.False(AssetPaths.HasImporterSection(AssetPaths.WithLabels(plugin, new[] { "a" })));
            Assert.True(AssetPaths.HasImporterSection(plugin + "\nPluginImporter:\n  externalObjects: {}\n"));
            Assert.True(AssetPaths.HasImporterSection("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- a\nDefaultImporter:\n"));
        }

        [Fact]
        public void TheMainObjectIsNamedAfterItsFileAndNothingElseChanges()
        {
            const string text = "%YAML 1.1\r\n%TAG !u! tag:unity3d.com,2011:\r\n--- !u!114 &-5\r\nMonoBehaviour:\r\n  m_Name: Sub\r\n  number: 1\r\n--- !u!114 &11400000\r\nMonoBehaviour:\r\n  m_Name: Data\r\n  number: 2\r\n";
            Assert.Equal(text.Replace("m_Name: Data", "m_Name: Copy"), AssetPaths.WithMainObjectName(text, "11400000", "Copy"));
            // A name YAML would read as something else is quoted.
            Assert.Contains("m_Name: '[Old]'", AssetPaths.WithMainObjectName(text, "11400000", "[Old]"));
            Assert.Null(AssetPaths.WithMainObjectName(text, "2100000", "Copy"));
        }

        /// <summary>The Editor's .meta files (the new-asset templates measured in
        /// tests/projects/projectwrites) read and write back byte for byte, and with CRLF endings
        /// as the same YAML.</summary>
        [Theory]
        [InlineData("texture_importer.yaml")]
        [InlineData("model_importer.yaml")]
        [InlineData("audio_importer.yaml")]
        public void MetaFilesRoundTrip(string template)
        {
            var body = File.ReadAllText(Path.Combine(Repo(), "crates", "unbound-assets", "src", "new_meta", template))
                .Replace("{platforms}", File.ReadAllText(Path.Combine(Repo(), "crates", "unbound-assets", "src", "new_meta", "texture_platform.yaml")).Replace("{platform}", "Standalone"));
            var meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- a\n" + body;
            Assert.Equal(meta, UnityYamlText.WriteTopLevel(UnityYamlText.ParseTopLevel(meta)));
            Assert.Equal(meta, UnityYamlText.WriteTopLevel(UnityYamlText.ParseTopLevel(meta.Replace("\n", "\r\n"))));
        }

        [Fact]
        public void ImporterSettingsArePatchedInPlaceAndUnplacedOnesAreReported()
        {
            const string meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n  mipmaps:\n    enableMipMap: 1\n  isReadable: 0\n  spritePivot: {x: 0.5, y: 0.5}\n  platformSettings:\n  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n  userData: \n";
            var root = UnityYamlText.ParseTopLevel(meta);
            var settings = root.Get("TextureImporter").ToNode();
            MetaSettings.SetBool(settings, "isReadable", true);
            MetaSettings.SetInt(settings, "mipmaps.enableMipMap", 0);
            MetaSettings.SetFloat(settings, "spritePivot.x", 0.25f);
            MetaSettings.SetInt(settings, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize", 64);
            MetaSettings.SetInt(settings, "notInTheFile", 3);
            var unwritten = new List<string>();
            ImporterMeta.Patch(root.Get("TextureImporter"), settings, "", unwritten);
            Assert.Equal(meta.Replace("enableMipMap: 1", "enableMipMap: 0").Replace("isReadable: 0", "isReadable: 1")
                .Replace("{x: 0.5, y: 0.5}", "{x: 0.25, y: 0.5}").Replace("maxTextureSize: 2048", "maxTextureSize: 64"),
                UnityYamlText.WriteTopLevel(root));
            Assert.Equal(new[] { "notInTheFile" }, unwritten);
        }

        static string LabelSnapshots() =>
            Path.Combine(Repo(), "tests", "projects", "scenesave", "Reference", "unity-6000.0.71f1", "label-metas", "snapshots");

        static string NewMetaTemplate(string name) => File.ReadAllText(Path.Combine(Repo(), "crates", "unbound-assets", "src", "new_meta", name));

        /// <summary>The engine's texture section at its defaults with the platforms of the Mac
        /// Editor that recorded label-metas (its Android module installed).</summary>
        static string TextureTemplate() => NewMetaTemplate("texture_importer.yaml").Replace("{platforms}",
            string.Concat(new[] { "DefaultTexturePlatform", "Standalone", "Android" }.Select(p => NewMetaTemplate("texture_platform.yaml").Replace("{platform}", p))));

        /// <summary>The settings of a .meta as the importer holds them, the platforms it imports for
        /// added (those of the Mac Editor that recorded label-metas; none for a release family whose
        /// files are not measured, <paramref name="template"/> ""), changed, and written.</summary>
        static string Rewrite(string text, Action<YNode> change, List<string> unwritten, string template = null)
        {
            var saved = UnityYamlText.ParseTopLevel(text).Get("TextureImporter").ToNode();
            var settings = MetaSettings.Clone(saved);
            UnityEditor.TextureImporter.AddMissingPlatforms(settings, template == "" ? null : new[] { "Standalone", "Android" });
            change(saved);
            change(settings);
            return ImporterMeta.Rewritten(text, "TextureImporter", settings, saved, ("", "", ""), template ?? TextureTemplate(), unwritten);
        }

        const string SingleSpriteId = "5e97eb03825dee720800000000000000";

        /// <summary>A spritemeta texture's .meta written by Unbound after <paramref name="change"/> of
        /// its importer, as the importer holds it (the platforms it imports for added, Standalone and
        /// the Mac Editor's Android module, which the comparison leaves out as the probe does);
        /// <paramref name="older"/>, <paramref name="laidOut"/> and <paramref name="unwritten"/> as
        /// Rewritten gives them.</summary>
        static string RewriteSpriteMeta(string name, Action<YNode> change, (string, string, string) common,
            out List<string> older, out bool laidOut, List<string> unwritten)
        {
            var text = File.ReadAllText(Path.Combine(Repo(), "tests", "projects", "spritemeta", "Assets", "Textures", name + ".png.meta"));
            var saved = UnityYamlText.ParseTopLevel(text).Get("TextureImporter").ToNode();
            var settings = MetaSettings.Clone(saved);
            UnityEditor.TextureImporter.AddMissingPlatforms(settings, new[] { "Standalone", "Android" });
            change(saved);
            change(settings);
            var written = ImporterMeta.Rewritten(text, "TextureImporter", settings, saved, common, ImporterLayout.ForTexture(TextureTemplate()),
                unwritten, out older, out laidOut);
            if (written == null) return null;
            written = System.Text.RegularExpressions.Regex.Replace(written, "(?m)^guid: .*$", "guid: (guid)");
            return System.Text.RegularExpressions.Regex.Replace(written, @"(?m)^( *)- serializedVersion: [0-9]+\n\1  buildTarget: Android\n(?:\1  .*\n)*", "");
        }

        static string SpriteMetaReference(string key)
        {
            foreach (var line in File.ReadLines(Path.Combine(Repo(), "tests", "projects", "spritemeta", "Reference", "unity-6000.0.71f1", "values.jsonl")))
                if (line.StartsWith("{\"k\":\"" + key + "\",", StringComparison.Ordinal))
                    return System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("v").GetString();
            throw new InvalidOperationException("no reference row " + key);
        }

        static void FirstImport(YNode settings)
        {
            MetaSettings.Set(settings, "spriteSheet.spriteID", SingleSpriteId);
            MetaSettings.SetInt(settings, "nPOTScale", 0);
        }

        static void RemoveEntry(YNode settings, string target)
        {
            var list = MetaSettings.Find(settings, "platformSettings");
            list.Items = list.Items.Where(e => e.Get("buildTarget")?.AsString() != target).ToArray();
        }

        /// <summary>A sprite's texture section without a serialized version (the oldest layout) is
        /// converted whole by any write as the Editor writes it (tests/projects/spritemeta): its
        /// first import (end.Bare, BareWrap, BarePpu, BareFull, BareSize, BareSheet, BareTag) and a
        /// script's save of one that has its sprite ID (save.SaveData, SaveNpot, SaveClear,
        /// SaveBundle): the keys the release lacks left out, applyGammaDecoding and cookieLightType
        /// 1, textureSettings without their own version and missing keys at the defaults, after the
        /// file's platform entries (one cleared left out) a default one and the installed platforms it
        /// lacks, uncompressed, of the file's maximum size and compression quality; the change in it.</summary>
        [Theory]
        [InlineData("Bare", "first")]
        [InlineData("BareWrap", "first")]
        [InlineData("BarePpu", "first")]
        [InlineData("BareFull", "first")]
        [InlineData("BareSize", "first")]
        [InlineData("BareSheet", "first")]
        [InlineData("BareTag", "first")]
        [InlineData("SaveData", "data")]
        [InlineData("SaveNpot", "npot")]
        [InlineData("SaveClear", "clear")]
        [InlineData("SaveBundle", "bundle")]
        public void AnOldestSpriteSectionIsConvertedByAnyWriteAsTheEditorWritesIt(string name, string change)
        {
            var previous = ImporterMeta.s_TextureImportPlatformsForTests;
            ImporterMeta.s_TextureImportPlatformsForTests = new[] { "Standalone", "Android" };
            try
            {
                var unwritten = new List<string>();
                var written = RewriteSpriteMeta(name, settings =>
                {
                    if (change == "first") FirstImport(settings);
                    else if (change == "npot") MetaSettings.SetInt(settings, "nPOTScale", (int)UnityEditor.TextureImporterNPOTScale.ToLarger);
                    else if (change == "clear") RemoveEntry(settings, "Standalone");
                }, change == "data" ? ("data", "", "") : change == "bundle" ? ("", "mixed-bundle", "") : ("", "", ""),
                    out var older, out bool laidOut, unwritten);
                Assert.Empty(older);
                Assert.True(laidOut);
                Assert.Empty(unwritten);
                Assert.Equal(SpriteMetaReference(change == "first" ? "end/" + name : "save/" + name + "/meta"), written);
            }
            finally { ImporterMeta.s_TextureImportPlatformsForTests = previous; }
        }

        /// <summary>The oldest layout's conversion where it is not measured, or of no change: an
        /// unchanged save writes nothing (spritemeta save.SaveKeep); a section of an older texture
        /// format (BareLegacy, whose alpha usage the Editor changes), with a platform entry of no
        /// serialized version (BarePlat), a Default texture's (BareDefault) or one of other older
        /// textureSettings keys keeps the file's layout, said, the change patched in; an entry the
        /// importer sets that the file lacks keeps it too; a textureSettings key the importer
        /// changes from the file's is written, the others read as defaults.</summary>
        [Fact]
        public void TheOldestLayoutIsConvertedOnlyWhereItIsMeasured()
        {
            var previous = ImporterMeta.s_TextureImportPlatformsForTests;
            ImporterMeta.s_TextureImportPlatformsForTests = new[] { "Standalone", "Android" };
            try
            {
                Assert.Null(RewriteSpriteMeta("SaveKeep", _ => { }, ("", "", ""), out _, out _, new List<string>()));
                foreach (var name in new[] { "BareLegacy", "BarePlat" })
                {
                    var unwritten = new List<string>();
                    RewriteSpriteMeta(name, FirstImport, ("", "", ""), out var older, out bool laidOut, unwritten);
                    Assert.False(laidOut);
                    Assert.Contains("serializedVersion: missing", older);
                    // The sprite sheet the file lacks is said unwritten: nothing is dropped silently.
                    Assert.Contains("spriteSheet", unwritten);
                }
                // A platform the importer sets that the file lacks (unmeasured).
                var setUnwritten = new List<string>();
                RewriteSpriteMeta("Bare", settings =>
                {
                    FirstImport(settings);
                    var entry = new UnityEditor.TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256 }.ToYText(prefixed: false).ToNode();
                    MetaSettings.Set(entry, "buildTarget", "iPhone");
                    settings.Keys = settings.Keys.Append("platformSettings").ToArray();
                    settings.Values = settings.Values.Append(new YNode { Kind = YKind.Seq, Items = new[] { entry } }).ToArray();
                }, ("", "", ""), out var setOlder, out bool setLaidOut, setUnwritten);
                Assert.False(setLaidOut);
                Assert.Contains(setOlder, o => o.Contains("iPhone set or changed"));
                // A textureSettings key changed from the file's is written; one held as the file has it
                // reads as the default (BareWrap's wrapU 1).
                var wrapped = RewriteSpriteMeta("BareWrap", settings =>
                {
                    FirstImport(settings);
                    MetaSettings.SetInt(settings, "textureSettings.wrapV", 1);
                }, ("", "", ""), out var wrapOlder, out bool wrapLaidOut, new List<string>());
                Assert.True(wrapLaidOut);
                Assert.Contains("    wrapU: 0\n    wrapV: 1\n    wrapW: 0\n", wrapped);

                const string head = "fileFormatVersion: 2\nguid: 0c118e8ca72b420ea96177cff6d779f0\nTextureImporter:\n";
                Assert.True(ImporterLayout.IsOldestSprite(UnityYamlText.ParseTopLevel(head + "  textureType: 8\n  textureSettings:\n    wrapU: 1\n").Get("TextureImporter")));
                Assert.False(ImporterLayout.IsOldestSprite(UnityYamlText.ParseTopLevel(head + "  textureType: 0\n").Get("TextureImporter")));
                Assert.False(ImporterLayout.IsOldestSprite(UnityYamlText.ParseTopLevel(head + "  textureType: 8\n  textureSettings:\n    filterMode: 0\n").Get("TextureImporter")));
                Assert.False(ImporterLayout.IsOldestSprite(UnityYamlText.ParseTopLevel(head + "  serializedVersion: 13\n  textureType: 8\n").Get("TextureImporter")));
                var layout = ImporterLayout.ForTexture(TextureTemplate());
                // An unversioned sprite sheet holding other than the defaults (whether the Editor keeps
                // its values is not measured).
                Assert.True(layout.ConvertsOldest(UnityYamlText.ParseTopLevel(head + "  textureType: 8\n  spriteSheet:\n    sprites: []\n").Get("TextureImporter")));
                Assert.False(layout.ConvertsOldest(UnityYamlText.ParseTopLevel(head + "  textureType: 8\n  spriteSheet:\n    spriteID: abc\n").Get("TextureImporter")));

                // A change of a Default texture's: the file's layout kept (the older settings' place
                // in the release's is not measured), patched in place.
                var defaultText = head + "  textureType: 0\n  maxTextureSize: 2048\n";
                var defaultSaved = UnityYamlText.ParseTopLevel(defaultText).Get("TextureImporter").ToNode();
                MetaSettings.SetInt(defaultSaved, "maxTextureSize", 512);
                var written = ImporterMeta.Rewritten(defaultText, "TextureImporter", MetaSettings.Clone(defaultSaved), defaultSaved, ("", "", ""), TextureTemplate(), new List<string>());
                Assert.Equal(head + "  textureType: 0\n  maxTextureSize: 512\n", written);
            }
            finally { ImporterMeta.s_TextureImportPlatformsForTests = previous; }
        }

        /// <summary>A texture's section of the current version that lacks settings and a platform
        /// (tests/projects/scenesave label-metas, whose files are the input and the expected
        /// output) is written whole: the missing settings at their defaults where the Editor
        /// writes them, the missing platform after the listed ones, the changed values patched in.</summary>
        [Theory]
        [InlineData("start.PartialKept.png.meta", "kept.PartialKept.png.meta", "mipmaps.enableMipMap", 0)]
        [InlineData("start.Partial.png.meta", "missingSetting.Partial.png.meta", "streamingMipmaps", 1)]
        public void ATextureSectionLackingSettingsIsWrittenWholeAsTheEditorWritesIt(string start, string saved, string setting, int value)
        {
            var unwritten = new List<string>();
            var written = Rewrite(File.ReadAllText(Path.Combine(LabelSnapshots(), start)), s => MetaSettings.SetInt(s, setting, value), unwritten);
            Assert.Empty(unwritten);
            Assert.Equal(File.ReadAllText(Path.Combine(LabelSnapshots(), saved)), written);
        }

        /// <summary>What is not filled writes nothing new: no change at all (projectwrites 06.crlf
        /// unchanged), a missing setting given the value its default reads, and a missing setting of
        /// a section of another version, which stays reported; a changed setting there is written
        /// with the platforms the importer added.</summary>
        [Fact]
        public void ATextureSectionIsFilledOnlyForAChangeOfASectionOfTheCurrentVersion()
        {
            var partial = File.ReadAllText(Path.Combine(LabelSnapshots(), "start.Partial.png.meta"));
            var unwritten = new List<string>();
            Assert.Null(Rewrite(partial, s => { }, unwritten));
            Assert.Empty(unwritten);
            // A missing setting given its default: nothing written, still reported (unmeasured).
            Assert.Null(Rewrite(partial, s => MetaSettings.SetInt(s, "streamingMipmaps", 0), unwritten));
            Assert.Equal(new[] { "streamingMipmaps (given the value its default reads)" }, unwritten);
            unwritten.Clear();
            const string old = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n  serializedVersion: 12\n  mipmaps:\n    enableMipMap: 1\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n  userData: \n";
            Assert.Null(Rewrite(old, s => MetaSettings.SetInt(s, "streamingMipmaps", 1), unwritten));
            Assert.Equal(new[] { "streamingMipmaps" }, unwritten);
            unwritten.Clear();
            var changed = Rewrite(old, s => MetaSettings.SetInt(s, "mipmaps.enableMipMap", 0), unwritten);
            Assert.Empty(unwritten);
            Assert.StartsWith(old.Replace("enableMipMap: 1", "enableMipMap: 0").Replace("  userData: \n", ""), changed);
            // The platforms the importer added, in the file's layout (not measured for older layouts).
            Assert.EndsWith("    buildTarget: Standalone\n    maxTextureSize: 2048\n  - serializedVersion: 3\n    buildTarget: Android\n    maxTextureSize: 2048\n  userData: \n", changed);
            // A release family whose files are not measured: no template, nothing filled.
            Assert.Null(Rewrite(partial, s => MetaSettings.SetInt(s, "streamingMipmaps", 1), unwritten, template: ""));
        }

        /// <summary>SetLabels gives a .meta holding the GUID alone its importer's section; one with
        /// other lines keeps them and gets the labels alone (a folder's .meta with folderAsset but no
        /// section would otherwise get the key twice).</summary>
        [Fact]
        public void OnlyAMetaHoldingItsGuidAloneGetsTheLabelsSection()
        {
            var guidOnly = File.ReadAllText(Path.Combine(LabelSnapshots(), "start.Folder.meta"));
            Assert.True(AssetPaths.HoldsGuidAlone(guidOnly));
            Assert.True(AssetPaths.HoldsGuidAlone(guidOnly.Replace("\n", "\r\n")));
            Assert.True(AssetPaths.HoldsGuidAlone(AssetPaths.WithLabels(guidOnly, new[] { "a" })));
            var section = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            Assert.Equal(File.ReadAllText(Path.Combine(LabelSnapshots(), "labelled.Folder.meta")), AssetPaths.WithLabels(guidOnly, new[] { "probe", "Alpha" }, section));
            var folder = guidOnly + "folderAsset: yes\n";
            Assert.False(AssetPaths.HasImporterSection(folder));
            Assert.False(AssetPaths.HoldsGuidAlone(folder));
            Assert.False(AssetPaths.HoldsGuidAlone(guidOnly + "timeCreated: 1731677241"));
            Assert.False(AssetPaths.HoldsGuidAlone(guidOnly + section));
            Assert.False(AssetPaths.HoldsGuidAlone("fileFormatVersion: 2\n"));
            var guidLine = guidOnly.Split('\n')[1] + "\n";
            Assert.False(AssetPaths.HoldsGuidAlone(guidOnly + guidLine));
            Assert.False(AssetPaths.HoldsGuidAlone("fileFormatVersion: 2\n" + guidOnly));
        }

        [Fact]
        public void ANewBuildTargetsDefinesGoInOrdinalOrder()
        {
            var map = YText.Map();
            SettingsAssets.SetInOrdinalOrder(map, "Standalone", YText.Scalar("A"));
            SettingsAssets.SetInOrdinalOrder(map, "iPhone", YText.Scalar("B"));
            SettingsAssets.SetInOrdinalOrder(map, "Android", YText.Scalar("C"));
            SettingsAssets.SetInOrdinalOrder(map, "Standalone", YText.Scalar("D"));
            Assert.Equal(new[] { "Android", "Standalone", "iPhone" }, map.Keys);
            Assert.Equal("D", map.Get("Standalone").Raw);
        }
    }
}
