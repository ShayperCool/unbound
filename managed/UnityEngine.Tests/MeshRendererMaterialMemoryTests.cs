using Unbound.Runtime;
using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MeshRendererMaterialMemoryTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ImporterClonesKeepRuntimeSourcePropertyBlocks(bool underAssetParent)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var owner = new GameObject("Runtime renderer source");
            GameObject parent = null;
            MeshRenderer clone = null;
            try
            {
                var source = owner.AddComponent<MeshRenderer>();
                var block = new MaterialPropertyBlock();
                block.SetFloat("_ProbeValue", 3.75f);
                source.SetPropertyBlock(block);
                source.SetPropertyBlock(block, 0);
                using (ScriptedImports.Scope())
                {
                    if (underAssetParent) parent = new GameObject("Importer-owned parent");
                    clone = parent == null ? Object.Instantiate(source) : Object.Instantiate(source, parent.transform);
                }
                Assert.True(clone.gameObject.m_IsPrefabAsset);
                var saved = new MaterialPropertyBlock();
                clone.GetPropertyBlock(saved);
                Assert.Equal(3.75f, saved.GetFloat("_ProbeValue"));
                clone.GetPropertyBlock(saved, 0);
                Assert.Equal(3.75f, saved.GetFloat("_ProbeValue"));
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone.gameObject, true);
                if (parent != null) Object.DestroyImmediate(parent, true);
                Object.DestroyImmediate(owner);
            }
        }

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
