using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Tests;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Creation defaults measured by tests/projects/defaultstate with their controls in
    /// tests/projects/defaultcontrols (Unity 6000.0.71f1), and the path other releases keep.</summary>
    [Collection("Project release")]
    public class CreationDefaultsTests
    {
        const ulong EditorDefaultSceneCullingMask = 0xE000000000000000UL;

        [Fact]
        public void CommandBufferStartsUnnamedAndKeepsTheNameSet()
        {
            using var buffer = new CommandBuffer();
            Assert.Equal("Unnamed command buffer", buffer.name);
            buffer.name = "probe";
            Assert.Equal("probe", buffer.name);
            buffer.Clear();
            Assert.Equal("probe", buffer.name);
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                using var other = new CommandBuffer();
                Assert.Equal("", other.name);
            }
        }

        [Fact]
        public void BatchFilterSettingsUseTheEditorMaskUntilSet()
        {
            BatchFilterSettings settings = default;
            Assert.Equal(EditorDefaultSceneCullingMask, settings.sceneCullingMask);
            Assert.Equal(EditorDefaultSceneCullingMask, default(BatchDrawRange).filterSettings.sceneCullingMask);
            settings.sceneCullingMask = 0;
            Assert.Equal(0UL, settings.sceneCullingMask);
            settings.sceneCullingMask = 5;
            Assert.Equal(5UL, settings.sceneCullingMask);
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.Equal(ulong.MaxValue, default(BatchFilterSettings).sceneCullingMask);
        }

        [Fact]
        public void GameObjectsHaveTheEditorSceneCullingMask()
        {
            var active = new GameObject("MaskActive");
            var inactive = new GameObject("MaskInactive");
            try
            {
                inactive.SetActive(false);
                Assert.Equal(EditorDefaultSceneCullingMask, active.sceneCullingMask);
                Assert.Equal(EditorDefaultSceneCullingMask, inactive.sceneCullingMask);
                using (TestRelease.Pin(TestRelease.Unmeasured))
                    Assert.Equal(ulong.MaxValue, active.sceneCullingMask);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(active);
                UnityEngine.Object.DestroyImmediate(inactive);
            }
        }

        [Fact]
        public void DefaultReadbackRequestIsDone()
        {
            Assert.True(default(AsyncGPUReadbackRequest).done);
            Assert.True(default(AsyncGPUReadbackRequest).hasError);
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.False(default(AsyncGPUReadbackRequest).done);
        }

        [Fact]
        public void DefaultCullingResultsThrow()
        {
            CullingResults results = default;
            var invalid = Assert.Throws<InvalidOperationException>(() => results.lightIndexCount);
            Assert.Equal("The CullingResults instance is invalid. This can happen if you construct an instance using the default constructor.", invalid.Message);
            Assert.Throws<InvalidOperationException>(() => results.reflectionProbeIndexCount);
            Assert.Throws<InvalidOperationException>(() => results.lightAndReflectionProbeIndexCount);
            Assert.Throws<InvalidOperationException>(() => results.GetLightIndexMap(Unity.Collections.Allocator.Temp));
            Assert.Throws<InvalidOperationException>(() => results.GetShadowCasterBounds(0, out _));
            Assert.Throws<NullReferenceException>(() => results.visibleLights);
            Assert.Throws<NullReferenceException>(() => results.visibleOffscreenVertexLights);
            Assert.Throws<NullReferenceException>(() => results.visibleReflectionProbes);
            Assert.True(results.Equals(default(CullingResults)));
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                Assert.Equal(0, default(CullingResults).lightIndexCount);
                Assert.Equal(0, default(CullingResults).visibleLights.Length);
            }
        }

#pragma warning disable CS0618
        [Fact]
        public void TerrainDetailUnsupportedIsAlwaysTrue()
        {
            var features = new SupportedRenderingFeatures();
            Assert.True(features.terrainDetailUnsupported);
            features.terrainDetailUnsupported = false;
            Assert.True(features.terrainDetailUnsupported);
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.False(features.terrainDetailUnsupported);
        }
#pragma warning restore CS0618

        [Fact]
        public void AddedLightProbeGroupHasTheCubeCorners()
        {
            var g = new GameObject("Probes");
            try
            {
                var group = g.AddComponent<LightProbeGroup>();
                var positions = group.probePositions;
                Assert.Equal(new[]
                {
                    new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(1, -1, 1), new Vector3(1, -1, -1),
                    new Vector3(-1, 1, 1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                }, positions);
                positions[0] = Vector3.zero;
                Assert.Equal(new Vector3(1, 1, 1), group.probePositions[0]);
                group.probePositions = new Vector3[0];
                Assert.Empty(group.probePositions);
                using (TestRelease.Pin(TestRelease.Unmeasured))
                    Assert.Empty(g.AddComponent<LightProbeGroup>().probePositions);
            }
            finally { UnityEngine.Object.DestroyImmediate(g); }
        }

        [Fact]
        public void DescriptorColorFormatFollowsSamplingWithoutAColorFormat()
        {
            RenderTextureDescriptor d = default;
            Assert.Equal(RenderTextureFormat.Shadowmap, d.colorFormat);
            d.shadowSamplingMode = ShadowSamplingMode.None;
            Assert.Equal(RenderTextureFormat.Depth, d.colorFormat);
            d.shadowSamplingMode = ShadowSamplingMode.RawDepth;
            Assert.Equal(RenderTextureFormat.Shadowmap, d.colorFormat);

            var depth = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24);
            Assert.Equal(RenderTextureFormat.Depth, depth.colorFormat);
            depth.shadowSamplingMode = ShadowSamplingMode.RawDepth;
            Assert.Equal(RenderTextureFormat.Shadowmap, depth.colorFormat);
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.Equal(RenderTextureFormat.Depth, depth.colorFormat);

            var color = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0);
            Assert.Equal(RenderTextureFormat.ARGB32, color.colorFormat);
        }

        [Fact]
        public void DescriptorDepthFormatFollowsSampling()
        {
            RenderTextureDescriptor compared = default;
            compared.depthBufferBits = 24;
            Assert.False(GraphicsFormatUtility.IsStencilFormat(compared.depthStencilFormat));
            Assert.True(GraphicsFormatUtility.IsDepthFormat(compared.depthStencilFormat));

            var plain = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0);
            plain.depthBufferBits = 24;
            Assert.Equal(GraphicsFormatUtility.DepthStencilFormatForBits(24), plain.depthStencilFormat);

            var shadow = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24);
            shadow.colorFormat = RenderTextureFormat.Shadowmap;
            Assert.Equal(ShadowSamplingMode.CompareDepths, shadow.shadowSamplingMode);
            Assert.Equal(GraphicsFormatUtility.ShadowmapDepthFormatForBits(shadow.depthBufferBits), shadow.depthStencilFormat);
        }

        [Fact]
        public void AttachmentOfADepthFormatIsDepth()
        {
            Assert.Equal(RenderTextureFormat.Depth, new AttachmentDescriptor(RenderTextureFormat.Depth).format);
            Assert.True(GraphicsFormatUtility.IsDepthStencilFormat(new AttachmentDescriptor(RenderTextureFormat.Depth).graphicsFormat));
            Assert.Equal(RenderTextureFormat.Depth, new AttachmentDescriptor(GraphicsFormat.D32_SFloat).format);
            Assert.Equal(RenderTextureFormat.ARGB32, new AttachmentDescriptor(RenderTextureFormat.ARGB32).format);
        }
    }
}
