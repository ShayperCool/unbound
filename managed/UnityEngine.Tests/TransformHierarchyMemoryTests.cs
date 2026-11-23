using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class TransformHierarchyMemoryTests
    {
        [Fact]
        public void UnmeasuredFamilyKeepsIndependentHierarchyCapacityAndSubtreeCount()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var parent = new GameObject("Unmeasured hierarchy");
            var child = new GameObject("Unmeasured child");
            try
            {
                child.transform.SetParent(parent.transform, false);
                parent.transform.hierarchyCapacity = 13;
                child.transform.hierarchyCapacity = 4;

                Assert.Equal(13, parent.transform.hierarchyCapacity);
                Assert.Equal(4, child.transform.hierarchyCapacity);
                Assert.Equal(2, parent.transform.hierarchyCount);
                Assert.Equal(1, child.transform.hierarchyCount);
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(parent.transform));
            }
            finally
            {
                if (child != null) Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
