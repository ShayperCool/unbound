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
