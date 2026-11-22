using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class FixedComponentMemoryTests
    {
        sealed class ScriptComponent : MonoBehaviour { }

        [Theory]
        [InlineData("6000.0.0f1", 288, 120, 456)]
        [InlineData(TestRelease.Unmeasured, 0, 0, 0)]
        public void AttachedComponentCostsFollowTheMeasuredFamily(string version, long collider, long meshFilter, long script)
        {
            using var release = TestRelease.Pin(version);
            var owner = new GameObject("Components");
            try
            {
                Assert.Equal(collider, Profiler.GetRuntimeMemorySizeLong(owner.AddComponent<BoxCollider>()));
                Assert.Equal(meshFilter, Profiler.GetRuntimeMemorySizeLong(owner.AddComponent<MeshFilter>()));
                Assert.Equal(script, Profiler.GetRuntimeMemorySizeLong(owner.AddComponent<ScriptComponent>()));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void AComponentAddedToAnAssetOwnerRetainsTheUnmeasuredPath(bool persistent, bool prefab)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var owner = new GameObject("Asset owner") { IsPersistentAsset = persistent, m_IsPrefabAsset = prefab };
            try
            {
                var collider = owner.AddComponent<BoxCollider>();
                Assert.False(collider.IsPersistentAsset);
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(collider));
            }
            finally
            {
                owner.IsPersistentAsset = false;
                owner.m_IsPrefabAsset = false;
                Object.DestroyImmediate(owner);
            }
        }
    }
}
