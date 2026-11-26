using UnityEditor.AssetImporters;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Which importer imports a file whose .meta names a ScriptedImporter (Unity 6000.0.71f1 Editor,
    /// tests/projects/importselfload): the named one when it lists the file's extension among those it
    /// imports, or when the .meta sets an importer override; else the extension's own. The attribute's
    /// constructors are Unity's (Modules/AssetPipelineEditor/Public/ScriptedImporter.cs).
    /// </summary>
    public sealed class ScriptedImporterSelectionTests
    {
        [ScriptedImporter(1, "gen")]
        sealed class GenImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [ScriptedImporter(2, new[] { ".over" }, new[] { "ovr2" }, 7)]
        sealed class OverImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [ScriptedImporter(1, new[] { "dup", "only1" }, new[] { "ovr2", "gen" })]
        sealed class DupOneImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [ScriptedImporter(1, new[] { ".dup", "" })]
        sealed class DupTwoImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [ScriptedImporter(1, "json")]
        sealed class JsonImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        sealed class NoAttributeImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [Fact]
        public void TheAttributeKeepsItsExtensionsOverridesAndQueueOffset()
        {
            var attribute = (ScriptedImporterAttribute)typeof(OverImporter).GetCustomAttributes(typeof(ScriptedImporterAttribute), false)[0];
            Assert.Equal(2, attribute.version);
            Assert.Equal(new[] { ".over" }, attribute.fileExtensions);
            Assert.Equal(new[] { "ovr2" }, attribute.overrideFileExtensions);
            Assert.Equal(7, attribute.importQueuePriority);
            var single = new ScriptedImporterAttribute(3, "gen");
            Assert.Equal(new[] { "gen" }, single.fileExtensions);
            Assert.Null(single.overrideFileExtensions);
        }

        /// <summary>An extension two importers declare is rejected for both, with the error Unity's
        /// ScriptedImporter.RegisterScriptedImporters logs (its published C#); an override extension
        /// is no default; a plug-in's importer is listed apart.</summary>
        [Fact]
        public void AnExtensionTwoImportersDeclareIsRejectedForBoth()
        {
            var selection = ScriptedImports.Select(
                new[] { typeof(DupTwoImporter), typeof(GenImporter), typeof(DupOneImporter), typeof(OverImporter) },
                type => type != typeof(OverImporter));
            Assert.Equal(new[] { "gen", "only1" }, Sorted(selection.ByExtension.Keys));
            Assert.Equal(typeof(GenImporter), selection.ByExtension["gen"]);
            Assert.Equal(typeof(DupOneImporter), selection.ByExtension["only1"]);
            Assert.Equal(new[] { "dup" }, Sorted(selection.Rejected));
            var error = Assert.Single(selection.Errors);
            var module = typeof(DupOneImporter).Module.FullyQualifiedName;
            Assert.Equal("Multiple scripted importers are targeting the extension 'dup' and have all been rejected: "
                + $"{typeof(DupOneImporter).FullName} (assembly: {module}), {typeof(DupTwoImporter).FullName} (assembly: {module}) ", error);
            // OverImporter is a plug-in's here: its default extension apart, its overrides not offered.
            Assert.Equal(typeof(OverImporter), Assert.Single(selection.Plugin).Value);
            Assert.Equal("over", Assert.Single(selection.Plugin).Key);
            Assert.Equal(new[] { "gen", "ovr2" }, Sorted(selection.Overrides.Keys));
            Assert.Equal(new[] { typeof(DupOneImporter) }, selection.Overrides["ovr2"]);
        }

        /// <summary>An extension Unity imports natively that a project's importer declares stays the
        /// native importer's ("Scripted importers can't handle a file extension that Unity already
        /// natively handles", Unity 6000.0 Manual, ScriptedImporters), unless an override is set.</summary>
        [Fact]
        public void ANativeExtensionAnImporterDeclaresIsNotImportedByIt()
        {
            Assert.True(ScriptedImports.ImportsAsset(typeof(JsonImporter), "Assets/Data/a.json", overridden: false));
            ScriptedImports.SetNativeDeclared(new[] { "json" });
            try
            {
                Assert.False(ScriptedImports.ImportsAsset(typeof(JsonImporter), "Assets/Data/a.JSON", overridden: false));
                Assert.True(ScriptedImports.ImportsAsset(typeof(JsonImporter), "Assets/Data/a.json", overridden: true));
                Assert.True(ScriptedImports.ImportsAsset(typeof(GenImporter), "Assets/Data/Thing.gen", overridden: false));
            }
            finally
            {
                ScriptedImports.SetNativeDeclared(System.Array.Empty<string>());
            }
        }

        /// <summary>The Editor's own SpeedTree9Importer is one of the declarers: a project's importer
        /// declaring `.st9` too is rejected with it.</summary>
        [Fact]
        public void TheEditorsOwnImporterCollidesWithAProjectsOne()
        {
            var selection = ScriptedImports.Select(
                new[] { typeof(UnityEditor.SpeedTree.Importer.SpeedTree9Importer), typeof(St9Importer) },
                type => type == typeof(St9Importer));
            Assert.Equal(new[] { "st9" }, Sorted(selection.Rejected));
            Assert.Empty(selection.ByExtension);
        }

        [ScriptedImporter(1, "st9")]
        sealed class St9Importer : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        static string[] Sorted(System.Collections.Generic.IEnumerable<string> keys)
        {
            var list = new System.Collections.Generic.List<string>(keys);
            list.Sort(System.StringComparer.Ordinal);
            return list.ToArray();
        }

        [Fact]
        public void AnImporterImportsTheExtensionsItListsOrAnOverride()
        {
            Assert.True(ScriptedImports.ImportsAsset(typeof(GenImporter), "Assets/Data/Thing.gen", overridden: false));
            Assert.True(ScriptedImports.ImportsAsset(typeof(GenImporter), "Assets/Data/THING.GEN", overridden: false));
            Assert.False(ScriptedImports.ImportsAsset(typeof(GenImporter), "Assets/Data/Mismatch.json", overridden: false));
            // A listed extension with its dot, and an override extension without an override set.
            Assert.True(ScriptedImports.ImportsAsset(typeof(OverImporter), "Assets/Data/Own.over", overridden: false));
            Assert.False(ScriptedImports.ImportsAsset(typeof(OverImporter), "Assets/Data/Override.ovr2", overridden: false));
            Assert.True(ScriptedImports.ImportsAsset(typeof(OverImporter), "Assets/Data/Override.ovr2", overridden: true));
            // A class that cannot be judged keeps the .meta's importer.
            Assert.True(ScriptedImports.ImportsAsset((System.Type)null, "Assets/Data/Mismatch.json", overridden: false));
            Assert.True(ScriptedImports.ImportsAsset(typeof(NoAttributeImporter), "Assets/Data/Mismatch.json", overridden: false));
            // Another release keeps the .meta's importer (unmeasured).
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.True(ScriptedImports.ImportsAsset(typeof(GenImporter), "Assets/Data/Mismatch.json", overridden: false));
        }
    }
}
