using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The light probe API's own state: scene probe sets joining and leaving the current
    /// probes, occlusion by shadow mask channel, queries keeping their probes and the property
    /// block probe arrays (the behaviours tests/projects/lightprobes records in the Editor).</summary>
    public class LightProbesTests
    {
        /// <summary>The 8 probes of tests/projects/lightprobes' Extra bake.</summary>
        static readonly Vector3[] Box =
        {
            new Vector3(4.17f, 0.28f, -0.85999995f), new Vector3(3.82f, 0.35999998f, 1.1f), new Vector3(3.87f, 2.33f, -0.82f),
            new Vector3(3.9299998f, 2.3799999f, 0.89f), new Vector3(6.15f, 0.26f, -1.16f), new Vector3(6.12f, 0.32999998f, 0.91999996f),
            new Vector3(6.0699997f, 2.46f, -1.01f), new Vector3(5.8599997f, 2.3999999f, 1.04f),
        };

        /// <summary>A scene's baked probes as its lighting data gives them, without cells.</summary>
        static LightProbes Baked(Vector3[] positions, Func<Vector3, float> dc)
        {
            var json = JsonSerializer.Serialize(new
            {
                positions = positions.SelectMany(p => new[] { p.x, p.y, p.z }),
                coefficients = positions.SelectMany(p => Enumerable.Range(0, 27).Select(k => k == 0 ? dc(p) : 0f)),
                hullRays = Array.Empty<float>(),
                tetrahedra = Array.Empty<object>(),
            });
            return LightProbes.FromJson(JsonDocument.Parse(json).RootElement);
        }

        [Fact]
        public void ProbeOcclusionIsByShadowMaskChannel()
        {
            Assert.Equal(new Vector4(0.25f, 0.5f, 1f, 1f),
                ProbeSet.ProbeOcclusion(new[] { 1, 0, -1, -1 }, new[] { 0.25f, 0.5f, 0f, 0f }, new[] { 0, 1, -1, -1 }));
            Assert.Equal(new Vector4(1f, 1f, 0.75f, 1f),
                ProbeSet.ProbeOcclusion(new[] { 0, -1, -1, -1 }, new[] { 0.75f, 0f, 0f, 0f }, new[] { 2, -1, -1, -1 }));
            Assert.Equal(Vector4.one,
                ProbeSet.ProbeOcclusion(new[] { -1, -1, -1, -1 }, new[] { 0f, 0f, 0f, 0f }, new[] { -1, -1, -1, -1 }));
        }

        [Fact]
        public void SceneProbesJoinAndLeaveOnlyWhenTetrahedralized()
        {
            var events = new List<string>();
            Action need = () => events.Add("need"), done = () => events.Add("done"), updated = () => events.Add("updated");
            LightProbes.needsRetetrahedralization += need;
            LightProbes.tetrahedralizationCompleted += done;
            LightProbes.lightProbesUpdated += updated;
            try
            {
                LightmapSettings.Reset();
                var main = Baked(Box.Select(p => p - new Vector3(4f, 0f, 0f)).ToArray(), _ => 1f);
                var extra = Baked(Box, _ => 2f);

                // The first scene's probes become the current ones, without an event.
                LightProbes.SceneLoaded(101, main);
                Assert.Same(main, LightmapSettings.lightProbes);
                Assert.Empty(events);

                // Another scene's wait for the next tetrahedralization.
                LightProbes.SceneLoaded(102, extra);
                Assert.Equal(new[] { "need" }, events);
                Assert.Equal(8, main.count);
                Assert.Same(extra, LightProbes.GetSharedLightProbesForScene(new Scene { m_Handle = 102 }));

                LightProbes.Tetrahedralize();
                Assert.Equal(new[] { "need", "done" }, events);
                Assert.Equal(16, main.count);
                Assert.Equal(8, main.countSelf);
                Assert.True(main.cellCount > 0);
                Assert.Equal(16, main.positions.Length);
                Assert.Equal(8, main.GetPositionsSelf().Length);

                // An unloaded scene's probes stay until the next tetrahedralization.
                LightProbes.SceneUnloaded(102);
                Assert.Equal(new[] { "need", "done", "need" }, events);
                Assert.Equal(16, main.count);
                LightProbes.SceneUnloaded(999);
                Assert.Equal(3, events.Count);

                LightProbes.Tetrahedralize();
                Assert.Equal(8, main.count);
                Assert.Equal(main.cellCountSelf, main.cellCount);
                Assert.Equal(new[] { "need", "done", "need", "done" }, events);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightProbes.tetrahedralizationCompleted -= done;
                LightProbes.lightProbesUpdated -= updated;
                LightmapSettings.Reset();
            }
        }

        [Fact]
        public void AQueryKeepsTheProbesItWasCreatedWith()
        {
            LightmapSettings.Reset();
            var probes = Baked(Box, p => p.x);
            ProbeTetrahedralization.Build(probes.m_Self);
            LightProbes.SceneLoaded(1, probes);
            var query = new LightProbesQuery(Allocator.Temp);
            try
            {
                var inside = new Vector3(5f, 1.2f, 0.1f);
                int index = -1;
                query.CalculateInterpolatedLightAndOcclusionProbe(inside, ref index, out var sh, out var occlusion);
                Assert.True(index >= 0);
                Assert.Equal(5f, sh[0, 0], 4);
                Assert.Equal(Vector4.one, occlusion);

                var changed = probes.bakedProbes;
                for (int i = 0; i < changed.Length; i++) changed[i][0, 0] = 0f;
                probes.bakedProbes = changed;
                LightProbes.GetInterpolatedProbe(inside, null, out var now);
                Assert.Equal(0f, now[0, 0], 4);

                // Any hint out of range starts the walk anew; the query still has the old probes.
                index = int.MaxValue;
                query.CalculateInterpolatedLightAndOcclusionProbe(inside, ref index, out sh, out _);
                Assert.True(index >= 0 && index < probes.cellCount);
                Assert.Equal(5f, sh[0, 0], 4);

                // Outside the hull: the ambient probe, occlusion 1 and index -1.
                index = 3;
                query.CalculateInterpolatedLightAndOcclusionProbe(new Vector3(20f, 1f, 0f), ref index, out sh, out occlusion);
                Assert.Equal(-1, index);
                Assert.Equal(RenderSettings.ambientProbe[0, 0], sh[0, 0]);
                Assert.Equal(Vector4.one, occlusion);
            }
            finally
            {
                query.Dispose();
                LightmapSettings.Reset();
            }
            Assert.False(query.IsCreated);
            Assert.Throws<ObjectDisposedException>(() => query.Dispose());
        }

        [Fact]
        public void PropertyBlocksTakeProbesAsShaderConstantArrays()
        {
            var probes = new SphericalHarmonicsL2[3];
            for (int i = 0; i < probes.Length; i++)
            {
                probes[i][0, 0] = i + 1f;
                probes[i][1, 3] = 0.5f * i;
                probes[i][2, 8] = -0.25f * i;
            }
            var ids = new[] { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };
            Vector4[] Constants(SphericalHarmonicsL2 sh)
            {
                sh.ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
                return new[] { ar, ag, ab, br, bg, bb, c };
            }

            // A block without the arrays gets arrays of the copied count.
            var block = new MaterialPropertyBlock();
            block.CopySHCoefficientArraysFrom(probes);
            for (int k = 0; k < ids.Length; k++)
            {
                var array = block.GetVectorArray(ids[k]);
                Assert.Equal(3, array.Length);
                for (int i = 0; i < probes.Length; i++) Assert.Equal(Constants(probes[i])[k], array[i]);
            }

            // Arrays it has keep their length; a range is written from destStart.
            var existing = new MaterialPropertyBlock();
            existing.SetVectorArray("unity_SHAr", new Vector4[5]);
            existing.CopySHCoefficientArraysFrom(new List<SphericalHarmonicsL2>(probes), 1, 3, 2);
            var shar = existing.GetVectorArray("unity_SHAr");
            Assert.Equal(5, shar.Length);
            Assert.Equal(Vector4.zero, shar[0]);
            Assert.Equal(Constants(probes[1])[0], shar[3]);
            Assert.Equal(Constants(probes[2])[0], shar[4]);

            var occlusion = new MaterialPropertyBlock();
            occlusion.CopyProbeOcclusionArrayFrom(new[] { new Vector4(1f, 0.5f, 1f, 1f), Vector4.one });
            Assert.Equal(new[] { new Vector4(1f, 0.5f, 1f, 1f), Vector4.one }, occlusion.GetVectorArray("unity_ProbesOcclusion"));

            // The Editor's checks and messages; nothing is written on an error.
            var failed = new MaterialPropertyBlock();
            Assert.Equal("lightProbes", Assert.Throws<ArgumentNullException>(() => failed.CopySHCoefficientArraysFrom((SphericalHarmonicsL2[])null)).ParamName);
            var dest = Assert.Throws<ArgumentOutOfRangeException>(() => failed.CopySHCoefficientArraysFrom(probes, 0, -1, 1));
            Assert.Equal("sourceStart", dest.ParamName);
            Assert.StartsWith("Argument destStart must not be negative.", dest.Message);
            Assert.Throws<ArgumentOutOfRangeException>(() => failed.CopySHCoefficientArraysFrom(probes, 2, 0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => failed.CopyProbeOcclusionArrayFrom(new Vector4[1], 0, 0, 2));
            Assert.True(failed.isEmpty);
        }

        /// <summary>A mesh renderer with LightProbeUsage.CustomProvided is lit by the first probe of
        /// its own property block (set with Renderer.SetPropertyBlock), which its draws carry in a
        /// material copy rather than as draw properties.</summary>
        [Fact]
        public void CustomProvidedRenderersTakeTheirBlocksFirstProbe()
        {
            var probes = new SphericalHarmonicsL2[2];
            probes[0][0, 0] = 0.5f;
            probes[0][1, 2] = 0.25f;
            probes[0][2, 8] = -0.125f;
            probes[1][0, 0] = 2f;
            var go = new GameObject("Custom");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                var block = new MaterialPropertyBlock();
                block.CopySHCoefficientArraysFrom(probes, 0, 0, 1);
                block.CopyProbeOcclusionArrayFrom(new[] { new Vector4(0.5f, 1f, 1f, 1f) });
                renderer.SetPropertyBlock(block);

                var properties = BakedLighting.Apply(renderer, null, new List<string>());
                Assert.NotNull(properties);
                probes[0].ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
                var names = new[] { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };
                var expected = new[] { ar, ag, ab, br, bg, bb, c };
                for (int k = 0; k < names.Length; k++) Assert.Equal(expected[k], (Vector4)properties[Shader.PropertyToID(names[k])]);
                Assert.Equal(new Vector4(0.5f, 1f, 1f, 1f), (Vector4)properties[Shader.PropertyToID("unity_ProbesOcclusion")]);

                // Without a block (or arrays) nothing is provided: the draw keeps what it had.
                renderer.SetPropertyBlock(null);
                Assert.Null(BakedLighting.Apply(renderer, null, new List<string>()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
