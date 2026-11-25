using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 7/8 import callbacks, which the Editor makes only as a SpeedTree 7
    /// (.spm) or 8 (.st) asset imports (Unity 6000.0 Scripting API,
    /// AssetPostprocessor.OnPreprocessSpeedTree, OnPostprocessSpeedTree) and Unbound never: a
    /// postprocessor declaring one is reported only where such an asset could call it.</summary>
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
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static string Key<T>(string message) => typeof(T).FullName + "." + message;

        [Fact]
        public void SpeedTreeAssetsAreThoseOfVersions7And8()
        {
            Assert.Equal(new[] { "Assets/Trees/Oak.st", "Packages/p/Old.SPM" },
                AssetPostprocessing.SpeedTreeAssets(new[] { "Assets/Trees/Oak.st", "Assets/Trees/Calm.st9", "Packages/p/Old.SPM", "Assets/st", "Assets/Oak.st.txt" }));
        }

        [Fact]
        public void ACallbackOfSpeedTreeImportsIsReportedOnlyWhereOneCanImport()
        {
            // No SpeedTree 7 or 8 asset: the SpeedTree callbacks are left out, the others reported.
            ScriptMessages.WarnNotSent(typeof(TreeCallbacks), AssetPostprocessing.SpeedTreeCallbacks);
            Assert.Equal(0, Hits(Key<TreeCallbacks>("OnPostprocessSpeedTree")));
            Assert.Equal(0, Hits(Key<TreeCallbacks>("OnPreprocessSpeedTree")));
            Assert.Equal(1, Hits(Key<TreeCallbacks>("OnPostprocessTexture3D")));
            // With one, every callback is reported.
            ScriptMessages.WarnNotSent(typeof(OtherTreeCallbacks));
            Assert.Equal(1, Hits(Key<OtherTreeCallbacks>("OnPostprocessSpeedTree")));
        }
    }
}
