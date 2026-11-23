using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MeshRendererMaterialMemoryTests
    {
        [Fact]
        public void UnmeasuredFamilyKeepsOverridesThroughResizeAndClone()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var owner = new GameObject("Unmeasured renderer");
            MeshRenderer clone = null;
            try
            {
                var renderer = owner.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[2];
                var block = new MaterialPropertyBlock();
                block.SetFloat("_ProbeValue", 3.75f);
                renderer.SetPropertyBlock(block, 1);
                renderer.sharedMaterials = new Material[1];
                renderer.sharedMaterials = new Material[2];
                clone = Object.Instantiate(renderer);
                var saved = new MaterialPropertyBlock();
                clone.GetPropertyBlock(saved, 1);
                Assert.Equal(3.75f, saved.GetFloat("_ProbeValue"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(renderer));
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone.gameObject);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
