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
    }
}
