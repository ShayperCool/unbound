using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Light probe proxy volumes as the Unity 6000.0.71f1 Editor builds them
    /// (tests/projects/lppv).</summary>
    public class LightProbeProxyVolumeTests
    {
        /// <summary>The probes per axis: the rounded count's nearest power of two, a tie upwards
        /// (lppv res*, corner*, ties, d*: 3, 6, 12 and 24 go up; 5, 9 and 17 down).</summary>
        [Theory]
        [InlineData(0.15f, 1)]
        [InlineData(0.8f, 1)]
        [InlineData(1.25f, 1)]
        [InlineData(1.5f, 2)]
        [InlineData(2.5f, 4)]
        [InlineData(3f, 4)]
        [InlineData(5f, 4)]
        [InlineData(5.625f, 8)]
        [InlineData(7f, 8)]
        [InlineData(9f, 8)]
        [InlineData(12f, 16)]
        [InlineData(17f, 16)]
        [InlineData(24f, 32)]
        [InlineData(31f, 32)]
        [InlineData(40f, 32)]
        public void CellsRoundToTheNearestPowerOfTwo(float probes, int cells) =>
            Assert.Equal(cells, LightProbeProxyVolume.Cells(probes));

        [Fact]
        public void DefaultsAreTheAddedComponentsOfTheEditor()
        {
            var go = new GameObject("lppv defaults");
            try
            {
                var volume = go.AddComponent<LightProbeProxyVolume>();
                Assert.Equal(LightProbeProxyVolume.DataFormat.Float, volume.dataFormat);
                Assert.Equal(LightProbeProxyVolume.ProbePositionMode.CellCorner, volume.probePositionMode);
                // The custom resolution is answered in Automatic mode too.
                Assert.Equal((2, 2, 2), (volume.gridResolutionX, volume.gridResolutionY, volume.gridResolutionZ));
                Assert.Equal(0.25f, volume.probeDensity);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>A custom box around the custom origin in the object's space: Cell Corner grows
        /// it by a cell (lppv custom, rotatedcustom); its probes are the ambient probe without any
        /// in the scene, in unity_SHAr's layout, then the occlusion.</summary>
        [Fact]
        public void ACustomBoxIsFilledAtItsCellsWithTheProbes()
        {
            var go = new GameObject("lppv custom");
            var (mode, ambient) = (RenderSettings.ambientMode, RenderSettings.ambientProbe);
            try
            {
                var given = new SphericalHarmonicsL2();
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) given[c, i] = 0.01f * (c + 1) * (i + 1);
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = given;
                var probe = RenderSettings.ambientProbe;
                Assert.NotEqual(0f, probe[0, 3]);
                go.transform.position = new Vector3(1f, 2f, 3f);
                var volume = go.AddComponent<LightProbeProxyVolume>();
                volume.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
                volume.sizeCustom = new Vector3(3f, 1f, 2f);
                volume.originCustom = new Vector3(0.5f, 0f, 0f);
                volume.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
                volume.gridResolutionX = 4; volume.gridResolutionY = 1; volume.gridResolutionZ = 2;
                volume.Update();
                var bounds = volume.boundsGlobal;
                Assert.Equal(new Vector3(1.5f, 2f, 3f), bounds.center);
                Assert.Equal(new Vector3(3f, 1f, 2f), bounds.size);
                var properties = new Dictionary<int, object>();
                volume.Apply(properties);
                // Local space, 16 texels wide; the box grown to 4, 1 and 4 (x 4/3, z 2/1).
                Assert.Equal(new Vector4(1f, 1f, 1f / 16f, 0f), properties[Shader.PropertyToID("unity_ProbeVolumeParams")]);
                Assert.Equal(new Vector4(0.25f, 1f, 0.25f, 0f), properties[Shader.PropertyToID("unity_ProbeVolumeSizeInv")]);
                Assert.Equal(new Vector4(-2f, -0.5f, -2f, 0f), properties[Shader.PropertyToID("unity_ProbeVolumeMin")]);
                Assert.Equal(new Vector3(0f, 0f, 0f), ((Matrix4x4)properties[Shader.PropertyToID("unity_ProbeVolumeWorldToObject")]).MultiplyPoint3x4(new Vector3(1.5f, 2f, 3f)));
                var texture = (Texture3D)properties[Shader.PropertyToID("unity_ProbeVolumeSH")];
                Assert.Equal((16, 1, 2), (texture.width, texture.height, texture.depth));
                var texels = texture.GetPixels();
                for (int z = 0; z < 2; z++)
                    for (int x = 0; x < 4; x++)
                        for (int c = 0; c < 3; c++)
                        {
                            var texel = texels[z * 16 + c * 4 + x];
                            Assert.Equal(new Color(probe[c, 3], probe[c, 1], probe[c, 2], probe[c, 0] - probe[c, 6]), texel);
                        }
            }
            finally
            {
                RenderSettings.ambientProbe = ambient;
                RenderSettings.ambientMode = mode;
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Automatic bounds enclose the renderers that use the volume; the resolution
        /// follows their size in the world, at most one probe a unit (lppv ownlocal).</summary>
        [Fact]
        public void AutomaticBoundsEncloseTheRenderersThatUseTheVolume()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.localScale = new Vector3(6f, 2f, 0.3f);
                var volume = go.AddComponent<LightProbeProxyVolume>();
                volume.probeDensity = 4f;
                volume.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
                // A renderer on Blend Probes is not enclosed: a unit box.
                volume.Update();
                var properties = new Dictionary<int, object>();
                volume.Apply(properties);
                Assert.Equal(new Vector4(1f, 1f, 1f, 0f), properties[Shader.PropertyToID("unity_ProbeVolumeSizeInv")]);
                Assert.Equal(new Vector4(1f, 1f, 1f / 32f, 0f), properties[Shader.PropertyToID("unity_ProbeVolumeParams")]);
                var texture = (Texture3D)properties[Shader.PropertyToID("unity_ProbeVolumeSH")];
                Assert.Equal((32, 2, 1), (texture.width, texture.height, texture.depth));
                var renderer = go.GetComponent<Renderer>();
                renderer.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                Assert.Same(volume, LightProbeProxyVolume.Of(renderer));
            }
            finally { Object.DestroyImmediate(go); }
        }
        sealed class NoVolumesPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => new NoVolumesPipeline();
        }

        sealed class NoVolumesPipeline : RenderPipeline
        {
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
        }

        /// <summary>A renderer that uses a built volume draws through it in the built-in pipeline;
        /// under a render pipeline that does not support volumes (URP) it keeps the probes Blend
        /// Probes gives (tests/projects/urp per-object-constants.jsonl, the Proxy Sphere).</summary>
        [Fact]
        public void OnlyPipelinesWithVolumesDrawThroughThem()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var asset = ScriptableObject.CreateInstance<NoVolumesPipelineAsset>();
            var saved = GraphicsSettings.defaultRenderPipeline;
            bool supported = SupportedRenderingFeatures.active.lightProbeProxyVolumes;
            var texture = Shader.PropertyToID("unity_ProbeVolumeSH");
            try
            {
                var renderer = go.GetComponent<Renderer>();
                renderer.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                var volume = go.AddComponent<LightProbeProxyVolume>();
                // Not built yet: nothing to draw through.
                Assert.Null(LightProbeProxyVolume.Of(renderer));
                volume.Update();
                var keywords = new List<string>();
                var drawn = BakedLighting.Apply(renderer, null, keywords);
                Assert.True(drawn.ContainsKey(texture));
                GraphicsSettings.defaultRenderPipeline = asset;
                SupportedRenderingFeatures.active.lightProbeProxyVolumes = false;
                Assert.Null(LightProbeProxyVolume.Of(renderer));
                drawn = BakedLighting.Apply(renderer, null, keywords);
                Assert.True(drawn == null || !drawn.ContainsKey(texture));
                SupportedRenderingFeatures.active.lightProbeProxyVolumes = true;
                Assert.Same(volume, LightProbeProxyVolume.Of(renderer));
            }
            finally
            {
                SupportedRenderingFeatures.active.lightProbeProxyVolumes = supported;
                GraphicsSettings.defaultRenderPipeline = saved;
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>unity_OcclusionMaskSelector: the light's baked occlusion channel, the first
        /// without one (tests/projects/lppv selector.scene, .channel2, .realtime). Programs built
        /// with proxy volumes weigh even unbaked lights with it.</summary>
        [Fact]
        public void TheOcclusionMaskSelectorIsTheLightsChannel()
        {
            var go = new GameObject("selector light");
            try
            {
                var light = go.AddComponent<Light>();
                Assert.Equal(new Vector4(1f, 0f, 0f, 0f), (Vector4)ForwardLights.OcclusionMaskSelector(null));
                light.bakingOutput = new LightBakingOutput { isBaked = false, lightmapBakeType = LightmapBakeType.Realtime, occlusionMaskChannel = -1 };
                Assert.Equal(new Vector4(1f, 0f, 0f, 0f), (Vector4)ForwardLights.OcclusionMaskSelector(light));
                light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed, mixedLightingMode = MixedLightingMode.Shadowmask, occlusionMaskChannel = 2 };
                Assert.Equal(new Vector4(0f, 0f, 1f, 0f), (Vector4)ForwardLights.OcclusionMaskSelector(light));
                var into = new Dictionary<int, object>();
                ForwardLights.AddPassProperties(light, 1f, into);
                Assert.Equal(new Vector4(0f, 0f, 1f, 0f), into[Shader.PropertyToID("unity_OcclusionMaskSelector")]);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
