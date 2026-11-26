using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An importer override as SetImporterOverride writes it into a `.meta` and ClearImporterOverride
    /// removes it (the Unity 6000.0.71f1 Editor, tests/projects/importselfload set_override.meta,
    /// spaced.meta, again.meta, clear.meta).
    /// </summary>
    [Collection("EditorState")]
    public sealed class ImporterOverrideTests
    {
        const string DefaultSection = "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        const string Plain = "fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\n" + DefaultSection;
        const string OverHash = "104ddd7902d5b6bcff8d6316c3d1f5bc";

        static string Override(string hash) =>
            "importerOverride:\n  nativeImporterType: 2089858483\n  scriptedImporterType:\n    serializedVersion: 2\n    Hash: " + hash + "\n";

        /// <summary>The type hash is Hash128 of "assembly:namespace:class": the measured hashes (an
        /// importer without a namespace, one in a namespace, a package's) are those of their names.</summary>
        [Fact]
        public void TheMeasuredTypeHashesAreTheirNamesHashes()
        {
            Assert.Equal(OverHash, Hash128.Compute("Assembly-CSharp-Editor::OverImporter").ToString());
            Assert.Equal("7f3acfa06d31e57fb7a5b8b20dd8aa20", Hash128.Compute("Assembly-CSharp-Editor:ProbeImporters:SpacedImporter").ToString());
            // A package's importer, a dotted namespace (tests/projects/toolapis Layers.psd.meta, the PSD importer's).
            Assert.Equal("b10b55a1aa6a1aa32521bc0cab59632e", Hash128.Compute("Unity.2D.Psdimporter.Editor:UnityEditor.U2D.PSD:PSDImporter").ToString());
            var type = typeof(TopLevelOverrideImporter);
            Assert.Equal(Hash128.Compute(type.Assembly.GetName().Name + ":" + type.Namespace + ":" + type.Name).ToString(), ScriptedImports.OverrideHash(type));
            // Nested and generic classes: their form is not measured.
            Assert.Null(ScriptedImports.OverrideHash(typeof(Nested)));
            Assert.Null(ScriptedImports.OverrideHash(null));
        }

        sealed class Nested : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        /// <summary>Set: the block goes before the importer's section, which stays; set again with
        /// another importer: the block is replaced; cleared: removed, the section kept; CRLF written
        /// as LF.</summary>
        [Fact]
        public void TheOverrideBlockIsWrittenBeforeTheImportersSection()
        {
            var set = AssetPaths.WithImporterOverride(Plain, OverHash);
            Assert.Equal("fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\n" + Override(OverHash) + DefaultSection, set);
            Assert.Equal(OverHash, AssetPaths.ImporterOverrideHash(set));
            var other = AssetPaths.WithImporterOverride(set, "7f3acfa06d31e57fb7a5b8b20dd8aa20");
            Assert.Equal("fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\n" + Override("7f3acfa06d31e57fb7a5b8b20dd8aa20") + DefaultSection, other);
            Assert.Equal(Plain, AssetPaths.WithImporterOverride(other, null));
            Assert.Equal(Plain, AssetPaths.WithImporterOverride(set.Replace("\n", "\r\n"), null));
            Assert.Null(AssetPaths.ImporterOverrideHash(Plain));
            // Labels stay before it, after the GUID line.
            var labelled = "fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\nlabels:\n- A\n" + DefaultSection;
            Assert.Equal("fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\nlabels:\n- A\n" + Override(OverHash) + DefaultSection,
                AssetPaths.WithImporterOverride(labelled, OverHash));
            // An override of another importer type names no ScriptedImporter.
            Assert.Null(AssetPaths.ImporterOverrideHash(set.Replace("2089858483", "1055")));
        }

        /// <summary>The importer's section written again (again.meta: a ScriptedImporter's in place
        /// of the DefaultImporter's) leaves the override's block in place.</summary>
        [Fact]
        public void TheImportersSectionIsWrittenAfterTheOverride()
        {
            var set = AssetPaths.WithImporterOverride(Plain, OverHash);
            const string scripted = "ScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n  userData: \n  assetBundleName: \n"
                + "  assetBundleVariant: \n  script: {fileID: 11500000, guid: 21242676002f4e71b99bfb1abfbd22b6, type: 3}\n";
            Assert.Equal("fileFormatVersion: 2\nguid: 89af164111494eaea0cf4f9a42c88594\n" + Override(OverHash) + scripted,
                AssetPaths.WithImporterSection(set, scripted));
        }

        /// <summary>SetImporterOverride's refusal of a path without an asset is Unity's own C#'s
        /// (missing.logs; an importer that cannot import the asset, invalid.logs, needs the native
        /// side's importer table: the probe).</summary>
        [Fact]
        public void AnOverrideIsRefusedForAMissingAssetOrAnUnregisteredImporter()
        {
            AssetDatabase.List("[{\"guid\":\"c0de00000000000000000000000000e1\",\"path\":\"Assets/Data/Mismatch.json\",\"kind\":\"text\"}]");
            var logs = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new Capture(logs);
            Stubs.Forget("AssetDatabase.SetImporterOverride");
            try
            {
                AssetDatabase.SetImporterOverride<TopLevelOverrideImporter>("Assets/Data/Missing.ovr2");
                Assert.Equal(new[] { "Error: Cannot set Importer override at \"Assets/Data/Missing.ovr2\". No Asset found at that path." }, logs);
                // An asset deleted in this session: no asset there, as Unity's OnlyExistingAssets says.
                logs.Clear();
                AssetDatabase.ApplyRefresh("{\"assets\":[{\"path\":\"Assets/Data/Gone.ovr2\",\"removed\":\"c0de00000000000000000000000000e2\"}]}");
                Assert.Equal("c0de00000000000000000000000000e2", AssetDatabase.AssetPathToGUID("Assets/Data/Gone.ovr2"));
                AssetDatabase.SetImporterOverride<TopLevelOverrideImporter>("Assets/Data/Gone.ovr2");
                Assert.Equal(new[] { "Error: Cannot set Importer override at \"Assets/Data/Gone.ovr2\". No Asset found at that path." }, logs);
                AssetDatabase.SetImporterOverrideInternal("Assets/Data/Gone.ovr2", typeof(TopLevelOverrideImporter));
                Assert.Single(logs);
                // A class registered as no project importer is not set: said.
                logs.Clear();
                AssetDatabase.SetImporterOverrideInternal("Assets/Data/Mismatch.json", typeof(TopLevelOverrideImporter));
                Assert.Contains(logs, l => l.Contains("sets only a project ScriptedImporter of a top-level class as an override"));
                Assert.Null(AssetDatabase.GetImporterOverride("Assets/Data/Missing.ovr2"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                AssetDatabase.Invalidate();
            }
        }

        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Lines;
            internal Capture(List<string> lines) => m_Lines = lines;
            public void LogException(System.Exception exception, Object context) => m_Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Lines.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
        }
    }

    [ScriptedImporter(1, "tlo")]
    public sealed class TopLevelOverrideImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx) { }
    }
}
