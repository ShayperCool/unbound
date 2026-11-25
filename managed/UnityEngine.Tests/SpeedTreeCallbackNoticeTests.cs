using System;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 7/8 import callbacks, which the Editor makes only as a SpeedTree 7
    /// (.spm) or 8 (.st) asset imports (Unity 6000.0 Scripting API,
    /// AssetPostprocessor.OnPreprocessSpeedTree, OnPostprocessSpeedTree) and Unbound only for
    /// SpeedTree 8 trees: a postprocessor declaring one is reported only where a SpeedTree 7 asset
    /// could call it, and such an asset is reported itself.</summary>
    public class SpeedTreeCallbackNoticeTests
    {
        class TreeCallbacks : AssetPostprocessor
        {
            void OnPostprocessSpeedTree(GameObject tree) { }
            void OnPreprocessSpeedTree() { }
            void OnPostprocessTexture3D(Texture3D texture) { }
        }

        class OtherTreeCallbacks : AssetPostprocessor
        {
            void OnPostprocessSpeedTree(GameObject tree) { }
            void OnPreprocessSpeedTree() { }
        }

        const string AssetNotice = "SpeedTree 7 assets";

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static string Key<T>(string message) => typeof(T).FullName + "." + message;

        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"11111111111111111111111111111111","path":"Assets/Lib.st","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"22222222222222222222222222222222","path":"Assets/Trees/Oak.st","kind":"unknown","importer":"DefaultImporter","labels":[]},
              {"guid":"33333333333333333333333333333333","path":"Assets/Trees/Calm.st9","kind":"unknown","importer":"DefaultImporter","labels":[]},
              {"guid":"44444444444444444444444444444444","path":"Packages/com.vendor.trees/Old.SPM","kind":"unknown","importer":"DefaultImporter","labels":[]},
              {"guid":"55555555555555555555555555555555","path":"Assets/Oak.st.txt","kind":"unknown","importer":"TextScriptImporter","labels":[]}
            ]
            """;

        [Fact]
        public void TheProjectsUnimportedSpeedTreeAssetsAreItsSpeedTree7FilesNotFolders()
        {
            AssetDatabase.List(Table);
            try
            {
                // A SpeedTree 8 tree (Oak.st) imports: only the SpeedTree 7 file is left.
                Assert.Equal(new[] { "Packages/com.vendor.trees/Old.SPM" }, AssetPostprocessing.ProjectSpeedTreeAssets());
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void SpeedTreeCallbacksAreReportedOnlyWhereASpeedTreeAssetCanCallThem()
        {
            // Counted as increments: other tests' editor domain loads report these classes too.
            string[] keys =
            {
                AssetNotice, Key<TreeCallbacks>("OnPostprocessSpeedTree"), Key<TreeCallbacks>("OnPreprocessSpeedTree"),
                Key<TreeCallbacks>("OnPostprocessTexture3D"), Key<OtherTreeCallbacks>("OnPostprocessSpeedTree"),
                Key<OtherTreeCallbacks>("OnPreprocessSpeedTree"),
            };
            var before = keys.ToDictionary(k => k, Hits);
            int Added(string key) => Hits(key) - before[key];
            // No SpeedTree 7 asset: the SpeedTree callbacks are left out, the others reported, and
            // no asset is reported.
            AssetPostprocessing.ReportUnsupported(new[] { typeof(TreeCallbacks) }, Array.Empty<string>());
            Assert.Equal(0, Added(Key<TreeCallbacks>("OnPostprocessSpeedTree")));
            Assert.Equal(0, Added(Key<TreeCallbacks>("OnPreprocessSpeedTree")));
            Assert.Equal(1, Added(Key<TreeCallbacks>("OnPostprocessTexture3D")));
            Assert.Equal(0, Added(AssetNotice));
            // With one: the asset is reported, and both SpeedTree callbacks.
            AssetPostprocessing.ReportUnsupported(new[] { typeof(OtherTreeCallbacks) }, new[] { "Assets/Trees/Old.spm" });
            Assert.Equal(1, Added(AssetNotice));
            Assert.Equal(1, Added(Key<OtherTreeCallbacks>("OnPostprocessSpeedTree")));
            Assert.Equal(1, Added(Key<OtherTreeCallbacks>("OnPreprocessSpeedTree")));
        }
    }
}
