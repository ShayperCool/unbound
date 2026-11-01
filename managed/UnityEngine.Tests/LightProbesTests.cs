using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
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

        /// <summary>A scene's baked probes with Unbound's cells of their own (as baked lighting data
        /// carries them).</summary>
        static LightProbes Tetrahedralized(Vector3[] positions, float dc)
        {
            var probes = Baked(positions, _ => dc);
            ProbeTetrahedralization.Build(probes.m_Self);
            return probes;
        }

        static Vector3[] MainBox => Box.Select(p => p - new Vector3(4f, 0f, 0f)).ToArray();

        /// <summary>Only the scenes a test makes are loaded while it runs; the registry's loaded
        /// list and active scene are restored after, and the test's scenes forgotten.</summary>
        sealed class Scenes : IDisposable
        {
            readonly SceneData[] m_Loaded = SceneRegistry.Loaded.ToArray();
            readonly SceneData m_Active = SceneRegistry.Active;
            readonly List<SceneData> m_Made = new List<SceneData>();

            internal Scenes()
            {
                SceneRegistry.Loaded.Clear();
                SceneRegistry.Active = null;
            }

            internal SceneData Loaded(string name)
            {
                var data = SceneRegistry.Create(name, "", -1);
                data.IsLoaded = true;
                data.LoadingState = Scene.LoadingState.Loaded;
                SceneRegistry.Loaded.Add(data);
                if (SceneRegistry.Active == null) SceneRegistry.Active = data;
                m_Made.Add(data);
                return data;
            }

            /// <summary>A scene a load has created, not loaded yet (as LoadScene leaves it).</summary>
            internal SceneData Loading(string name)
            {
                var data = SceneRegistry.Create(name, "", -1);
                data.LoadingState = Scene.LoadingState.Loading;
                m_Made.Add(data);
                return data;
            }

            public void Dispose()
            {
                foreach (var data in m_Made)
                    if (SceneRegistry.Get(data.Handle) != null) SceneRegistry.Forget(data);
                SceneRegistry.Loaded.Clear();
                SceneRegistry.Loaded.AddRange(m_Loaded);
                SceneRegistry.Active = m_Active;
            }
        }

        /// <summary>The public probe state at a point, in the form tests/projects/lightprobes'
        /// lifecycle witness records it: the global counts, and the scene's validity, isLoaded and
        /// shared probes.</summary>
        static string State(SceneData data, LightProbes own)
        {
            var g = LightmapSettings.lightProbes;
            var scene = new Scene(data.Handle);
            var shared = LightProbes.GetSharedLightProbesForScene(scene);
            string Name(LightProbes p) => p == null ? "null" : p == own ? "own" : p == g ? "global" : "other";
            return $"global {(g == null ? "null" : $"{g.count} {g.countSelf} {g.cellCount} {g.cellCountSelf}")} | "
                + $"valid {scene.IsValid()} loaded {scene.isLoaded} shared {Name(shared)}";
        }

        [Fact]
        public void SceneProbesJoinAndLeaveOnlyWhenTetrahedralized()
        {
            var events = new List<string>();
            Action need = () => events.Add("need"), done = () => events.Add("done"), updated = () => events.Add("updated");
            LightProbes.needsRetetrahedralization += need;
            LightProbes.tetrahedralizationCompleted += done;
            LightProbes.lightProbesUpdated += updated;
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                var extraScene = scenes.Loaded("Extra");
                var main = Baked(MainBox, _ => 1f);
                var extra = Baked(Box, _ => 2f);

                // The first scene's probes become the current ones, without an event.
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                Assert.Same(main, LightmapSettings.lightProbes);
                Assert.Empty(events);

                // Another scene's wait for the next tetrahedralization.
                LightProbes.SceneLoaded(extraScene.Handle.Value, extra);
                Assert.Equal(new[] { "need" }, events);
                Assert.Equal(8, main.count);
                Assert.Same(extra, LightProbes.GetSharedLightProbesForScene(new Scene(extraScene.Handle)));

                LightProbes.Tetrahedralize();
                Assert.Equal(new[] { "need", "done" }, events);
                Assert.Equal(16, main.count);
                Assert.Equal(8, main.countSelf);
                Assert.True(main.cellCount > 0);
                Assert.Equal(16, main.positions.Length);
                Assert.Equal(8, main.GetPositionsSelf().Length);

                // An unloaded scene's probes stay until the next tetrahedralization.
                LightProbes.SceneUnloaded(extraScene.Handle.Value);
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
                scenes.Dispose();
            }
        }

        /// <summary>
        /// UnloadSceneAsync of an additive scene, in the relative order Unity 6000.0.71f1 gives
        /// (tests/projects/lightprobes Reference lifecycle.jsonl, seq 10-17): needsRetetrahedralization
        /// before sceneUnloaded; in both the scene is valid, no longer loaded, and its own probes
        /// are its shared probes; the global probes keep the combined set until Tetrahedralize; the
        /// scene is forgotten after its sceneUnloaded. Unity raises this group after
        /// UnloadSceneAsync has returned; Unbound within the call, which this test does not
        /// settle (the relative order and the state are what it checks).
        /// </summary>
        [Fact]
        public void AdditiveUnloadRaisesNeedsBeforeSceneUnloadedAndKeepsTheCombinedSet()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            SceneData extraScene = null;
            LightProbes extra = null;
            Action need = () => events.Add("needs: " + State(extraScene, extra));
            Action done = () => events.Add("done");
            UnityEngine.Events.UnityAction<Scene> unloaded = s => events.Add($"sceneUnloaded {s.name}: " + State(extraScene, extra));
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                extraScene = scenes.Loaded("Extra");
                var main = Tetrahedralized(MainBox, 1f);
                extra = Tetrahedralized(Box, 2f);
                int mainCells = main.cellCountSelf, extraCells = extra.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.SceneLoaded(extraScene.Handle.Value, extra);
                LightProbes.Tetrahedralize();
                int combinedCells = main.cellCount;
                Assert.Equal(16, main.count);
                Assert.NotEqual(mainCells, combinedCells);

                LightProbes.needsRetetrahedralization += need;
                LightProbes.tetrahedralizationCompleted += done;
                SceneManager.sceneUnloaded += unloaded;
                var operation = SceneManagerInternal.Unload(extraScene);
                Assert.NotNull(operation);

                string during = $"global 16 8 {combinedCells} {mainCells} | valid True loaded False shared own";
                Assert.Equal(new[] { "needs: " + during, "sceneUnloaded Extra: " + during }, events);
                // Forgotten after its sceneUnloaded: invalid, no shared probes (as Unity's completed
                // callback sees it); the stale combined set stays.
                Assert.Equal($"global 16 8 {combinedCells} {mainCells} | valid False loaded False shared null", State(extraScene, extra));
                Assert.Same(main, LightmapSettings.lightProbes);
                Assert.Same(main, LightProbes.GetSharedLightProbesForScene(new Scene(mainScene.Handle)));
                // The unloaded scene's object keeps its own set.
                Assert.Equal(8, extra.count);
                Assert.Equal(extraCells, extra.cellCount);

                events.Clear();
                LightProbes.Tetrahedralize();
                Assert.Equal(new[] { "done" }, events);
                Assert.Equal(8, main.count);
                Assert.Equal(mainCells, main.cellCount);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightProbes.tetrahedralizationCompleted -= done;
                SceneManager.sceneUnloaded -= unloaded;
                LightmapSettings.Reset();
                scenes.Dispose();
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
    }
}
