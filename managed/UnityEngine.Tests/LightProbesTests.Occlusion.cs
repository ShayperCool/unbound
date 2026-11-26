using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    public partial class LightProbesTests
    {
        static ProbeSet SparseOcclusion()
        {
            var set = ProbeSet.FromJson(JsonDocument.Parse(SparseOcclusionJson()).RootElement);
            ProbeTetrahedralization.Build(set);
            return set;
        }

        static string SparseOcclusionJson()
        {
            var positions = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            var json = JsonSerializer.Serialize(new
            {
                positions = positions.SelectMany(p => new[] { p.x, p.y, p.z }),
                coefficients = new float[108],
                hullRays = new float[0],
                tetrahedra = new object[0],
                occlusion = new[]
                {
                    new { lights = new[] { 0, -1, -1, -1 }, values = new[] { 0.25f, 0f, 0f, 0f }, channels = new[] { 0, -1, -1, -1 } },
                    new { lights = new[] { 1, -1, -1, -1 }, values = new[] { 0.5f, 0f, 0f, 0f }, channels = new[] { 1, -1, -1, -1 } },
                    new { lights = new[] { 0, -1, -1, -1 }, values = new[] { 0.75f, 0f, 0f, 0f }, channels = new[] { 0, -1, -1, -1 } },
                    new { lights = new[] { -1, -1, -1, -1 }, values = new float[4], channels = new[] { -1, -1, -1, -1 } },
                },
            });
            return json;
        }

        [Fact]
        public void OcclusionBlendsOnlyChannelsPresentInEachVertex()
        {
            var set = SparseOcclusion();
            int hint = -1;
            Assert.True(set.Evaluate(new Vector3(0.25f, 0.25f, 0.25f), ref hint, out _, out var occ));
            Assert.Equal(new Vector4(0.25f, 0.125f, 1f, 1f), occ);
            // A vertex with no channel still sees the cell's channel presence, even when the
            // contributing vertices have zero weight. Channels absent from the cell stay one.
            Assert.True(set.Evaluate(Vector3.forward, ref hint, out _, out occ));
            Assert.Equal(new Vector4(0f, 0f, 1f, 1f), occ);
        }

        [Fact]
        public void OcclusionChannelPresenceSurvivesSnapshotsAndSceneCombinations()
        {
            var set = SparseOcclusion();
            var combined = ProbeTetrahedralization.Combine(new List<ProbeSet> { set });
            foreach (var candidate in new[] { set.Snapshot(), combined })
            {
                int hint = -1;
                Assert.True(candidate.Evaluate(new Vector3(0.125f, 0.5f, 0.125f), ref hint, out _, out var occ));
                Assert.Equal(new Vector4(0.4375f, 0.0625f, 1f, 1f), occ);
            }
        }

        [Fact]
        public void ARenderersOcclusionComesFromItsOwnInterpolationAndIsOneOutsideTheBounds()
        {
            var set = SparseOcclusion();
            // Inside: the same blend the readout gives, from the cell the walk keeps.
            int cell = -1, hint = -1;
            var inside = new Vector3(0.25f, 0.25f, 0.25f);
            Assert.True(set.Interpolate(inside, ref cell, out var sh, out var occ));
            Assert.True(set.Evaluate(inside, ref hint, out var readSh, out var readOcc));
            Assert.Equal(readOcc, occ);
            Assert.Equal(readSh, sh);
            Assert.Equal(hint, cell);
            // Outside the probes' bounds the lighting is extrapolated but nothing is occluded
            // (measured: tests/projects/shadowmaskmix, the far shadowed sphere).
            var outside = new Vector3(-0.5f, 0.1f, 0.1f);
            Assert.True(set.Interpolate(outside, ref cell, out _, out occ));
            Assert.Equal(Vector4.one, occ);
            hint = -1;
            Assert.False(set.Evaluate(outside, ref hint, out _, out readOcc));
            Assert.Equal(Vector4.one, readOcc);
        }

        /// <summary>LightProbeUsage.CustomProvided (Scripting API 6000.0): "Property
        /// unity_ProbesOcclusion will be calculated as in normal lighting if it is not part of the
        /// MaterialPropertyBlock"; a block that holds one wins.</summary>
        [Fact]
        public void ACustomProvidedRendererWithoutOcclusionInItsBlockTakesTheProbes()
        {
            var saved = LightmapSettings.lightProbes;
            var go = new GameObject("Custom occlusion");
            var lightObject = new GameObject("Mixed light");
            try
            {
                var probes = LightProbes.FromJson(JsonDocument.Parse(SparseOcclusionJson()).RootElement);
                ProbeTetrahedralization.Build(probes.m_Self);
                LightmapSettings.lightProbes = probes;
                go.transform.position = new Vector3(0.25f, 0.25f, 0.25f);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.probeAnchor = go.transform;
                var light = lightObject.AddComponent<Light>();
                light.bakingOutput = new LightBakingOutput
                {
                    isBaked = true, lightmapBakeType = LightmapBakeType.Mixed, mixedLightingMode = MixedLightingMode.Shadowmask,
                    occlusionMaskChannel = 1, probeOcclusionLightIndex = 1,
                };
                int occlusionId = Shader.PropertyToID("unity_ProbesOcclusion");

                // SH only: the occlusion is the probes', blended at the renderer.
                var block = new MaterialPropertyBlock();
                block.CopySHCoefficientArraysFrom(new SphericalHarmonicsL2[1], 0, 0, 1);
                renderer.SetPropertyBlock(block);
                var properties = BakedLighting.Apply(renderer, null, new List<string>());
                Assert.Equal(new Vector4(0.25f, 0.125f, 1f, 1f), (Vector4)properties[occlusionId]);
                Assert.Equal(0.125f, BakedLighting.ProbeOcclusion(renderer, light));

                // Its own occlusion wins over the probes'.
                block.CopyProbeOcclusionArrayFrom(new[] { new Vector4(0.75f, 0.5f, 1f, 1f) });
                renderer.SetPropertyBlock(block);
                properties = BakedLighting.Apply(renderer, null, new List<string>());
                Assert.Equal(new Vector4(0.75f, 0.5f, 1f, 1f), (Vector4)properties[occlusionId]);
                Assert.Equal(0.5f, BakedLighting.ProbeOcclusion(renderer, light));
            }
            finally
            {
                LightmapSettings.lightProbes = saved;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        [Fact]
        public void OcclusionSlotsSharingAChannelAccumulate()
        {
            Assert.Equal(new Vector4(1f, 1f, 0.75f, 1f), ProbeSet.ProbeOcclusion(
                new[] { 2, 7, -1, -1 }, new[] { 0.25f, 0.5f, 0f, 0f }, new[] { 2, 2, -1, -1 }));
        }
    }
}
