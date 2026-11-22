using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>What a bake's lightmap layout takes from the scene's files: a renderer's Receive
    /// Global Illumination and the Lighting Settings' Fixed Lightmap Size.</summary>
    public class BakedAtlasInputTests
    {
        [Fact]
        public void AMeshRendererReadsAndCopiesReceiveGlobalIllumination()
        {
            var go = new GameObject("lit by probes");
            var other = new GameObject("copy");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.ReadSerialized(YNode.MakeMap(("m_ReceiveGI", YNode.MakeScalar("2"))), null);
                Assert.Equal(ReceiveGI.LightProbes, renderer.receiveGI);
                var copy = other.AddComponent<MeshRenderer>();
                copy.CopyFrom(renderer, o => o);
                Assert.Equal(ReceiveGI.LightProbes, copy.receiveGI);
                renderer.ReadSerialized(YNode.MakeMap(), null);
                Assert.Equal(ReceiveGI.Lightmaps, renderer.receiveGI);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(other);
            }
        }

        [Fact]
        public void LightingSettingsReadFixedLightmapSize()
        {
            var settings = new LightingSettings();
            Assert.False(settings.LightmapSizeFixed);
            settings.ReadSerialized(YNode.MakeMap(("m_LightmapSizeFixed", YNode.MakeScalar("1"))), null);
            Assert.True(settings.LightmapSizeFixed);
        }
    }
}
