using System.Collections.Generic;
using System.IO;
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
            // A labels block is no section: a script's labelled .meta without one still gets it.
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- b\n" + section,
                AssetPaths.WithLabels("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- a\n", new[] { "b" }, section));
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
