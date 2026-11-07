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
    /// <summary>Reports its OnDisable and OnDestroy (an outgoing scene's object in a Single load).</summary>
    public class ProbeTeardownWitness : MonoBehaviour
    {
        internal Action onDisable, onDestroy;

        void OnDisable() => onDisable?.Invoke();
        void OnDestroy() => onDestroy?.Invoke();
    }

    /// <summary>The light probe API's own state: scene probe sets joining and leaving the current
    /// probes, occlusion by shadow mask channel, queries keeping their probes and the property
    /// block probe arrays (the behaviours tests/projects/lightprobes records in the Editor).</summary>
    public partial class LightProbesTests
    {
        /// <summary>The 8 probes of tests/projects/lightprobes' Extra bake.</summary>
        static readonly Vector3[] Box =
        {
            new Vector3(4.17f, 0.28f, -0.85999995f), new Vector3(3.82f, 0.35999998f, 1.1f), new Vector3(3.87f, 2.33f, -0.82f),
            new Vector3(3.9299998f, 2.3799999f, 0.89f), new Vector3(6.15f, 0.26f, -1.16f), new Vector3(6.12f, 0.32999998f, 0.91999996f),
            new Vector3(6.0699997f, 2.46f, -1.01f), new Vector3(5.8599997f, 2.3999999f, 1.04f),
        };

        /// <summary>A scene's baked probes as its lighting data gives them, without cells.</summary>
        static LightProbes Baked(Vector3[] positions, Func<Vector3, float> dc) =>
            LightProbes.FromJson(JsonDocument.Parse(ProbesJson(positions, dc)).RootElement);

        /// <summary>A scene's baked probes as the lighting data reply gives them, without cells.</summary>
        static string ProbesJson(Vector3[] positions, Func<Vector3, float> dc) => JsonSerializer.Serialize(new
        {
            positions = positions.SelectMany(p => new[] { p.x, p.y, p.z }),
            coefficients = positions.SelectMany(p => Enumerable.Range(0, 27).Select(k => k == 0 ? dc(p) : 0f)),
            hullRays = Array.Empty<float>(),
            tetrahedra = Array.Empty<object>(),
        });

        [Fact]
        public void BakesWithUnusedNaNValuesLoad()
        {
            // The Editor's bakes hold NaN where a value is unused (the hull ray of a probe off the
            // hull, an outer cell's first matrix rows); the lighting data reply writes it as null.
            const string json = @"{
                ""positions"": [0,0,0, 1,0,0, 0,1,0, 0,0,1],
                ""coefficients"": [" + "0.5,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0," + @"
                    0.5,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0," + @"
                    0.5,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0," + @"
                    0.5,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,0],
                ""hullRays"": [null,null,null, 1,0,0, 0,1,0, 0,0,1],
                ""tetrahedra"": [{ ""indices"": [1,2,3,-1], ""neighbors"": [-1,-1,-1,-1],
                    ""matrix"": [null,null,null,null, null,null,null,null, 1,2,3,4] }]
            }";
            var probes = LightProbes.FromJson(JsonDocument.Parse(json).RootElement);
            Assert.Equal(4, probes.count);
            Assert.True(float.IsNaN(probes.m_Self.HullRays[0].x));
            Assert.Equal(new Vector3(1, 0, 0), probes.m_Self.HullRays[1]);
            Assert.True(float.IsNaN(probes.m_Self.Cells[0].R0.x));
            Assert.Equal(new Vector3(1, 2, 3), probes.m_Self.Cells[0].R2);
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
        /// list and active scene are restored after, and the test's scenes forgotten. The probes
        /// objects kept by Lighting Data identity (which LightmapSettings.Reset leaves) start empty
        /// and are restored too.</summary>
        internal sealed class Scenes : IDisposable
        {
            readonly SceneData[] m_Loaded = SceneRegistry.Loaded.ToArray();
            readonly SceneData m_Active = SceneRegistry.Active;
            readonly List<SceneData> m_Made = new List<SceneData>();
            readonly KeyValuePair<(string Guid, long FileId), WeakReference<LightProbes>>[] m_Sources = LightProbes.SaveSources();

            internal Scenes()
            {
                SceneRegistry.Loaded.Clear();
                SceneRegistry.Active = null;
                LightProbes.RestoreSources(Array.Empty<KeyValuePair<(string Guid, long FileId), WeakReference<LightProbes>>>());
            }

            internal SceneData Loaded(string name, string path = "")
            {
                var data = SceneRegistry.Create(name, path, -1);
                data.IsLoaded = true;
                data.LoadingState = Scene.LoadingState.Loaded;
                SceneRegistry.Loaded.Add(data);
                if (SceneRegistry.Active == null) SceneRegistry.Active = data;
                m_Made.Add(data);
                return data;
            }

            /// <summary>A scene a load has created, not loaded yet (as LoadScene leaves it).</summary>
            internal SceneData Loading(string name, string path = "")
            {
                var data = SceneRegistry.Create(name, path, -1);
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
                LightProbes.RestoreSources(m_Sources);
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

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference<LightProbes> UnloadUnretainedOwnProbes(Scenes scenes)
        {
            var extraScene = scenes.Loaded("Extra");
            var extra = Baked(Box, _ => 2f);
            LightProbes.SceneLoaded(extraScene.Handle.Value, extra);
            var weak = new WeakReference<LightProbes>(extra);
            SceneManagerInternal.Unload(extraScene);
            return weak;
        }

        [Fact]
        public void ForgottenSceneDoesNotKeepItsOwnProbeObjectAlive()
        {
            using var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                LightProbes.SceneLoaded(mainScene.Handle.Value, Baked(MainBox, _ => 1f));
                // The main probes stay current. Only the scene lookup could otherwise retain
                // this additive scene's own object, even across repeated unloads.
                var weak = UnloadUnretainedOwnProbes(scenes);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Assert.False(weak.TryGetTarget(out _));
            }
            finally { LightmapSettings.Reset(); }
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

        /// <summary>
        /// A SceneManager Single load replacing one scene, as Unity 6000.0.71f1 does it
        /// (tests/projects/lightprobes Reference lifecycle.jsonl, seq 20-24): no
        /// needsRetetrahedralization; at the outgoing sceneUnloaded the global probes (the outgoing
        /// scene's own) have lost their combined set (count and cellCount 0) but keep their own
        /// counts, and the incoming scene, created but not loaded, has no shared probes; the
        /// outgoing scene is forgotten after. The incoming scene's lighting data then resets the set
        /// (RenderSettings.ReadLightingData for a first scene) and its probes become global, without
        /// an event. That this precedes activeSceneChanged follows from SceneManagerInternal.Perform
        /// reading the scene (SceneLoader.LoadInto) before raising it; this unit test does not load
        /// a scene file. Several outgoing scenes are not measured and not tested.
        /// </summary>
        [Fact]
        public void SingleLoadClearsTheCombinedSetBeforeSceneUnloadedWithoutNeeds()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            SceneData mainScene = null, extraScene = null;
            LightProbes main = null;
            Action need = () => events.Add("needs");
            Action done = () => events.Add("done");
            Action updated = () => events.Add("updated");
            UnityEngine.Events.UnityAction<Scene> unloaded = s => events.Add(
                $"sceneUnloaded {s.name}: {State(mainScene, main)} | incoming {State(extraScene, null)}");
            try
            {
                LightmapSettings.Reset();
                mainScene = scenes.Loaded("Main");
                main = Tetrahedralized(MainBox, 1f);
                int mainCells = main.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.Tetrahedralize();
                Assert.Equal($"global 8 8 {mainCells} {mainCells} | valid True loaded True shared own", State(mainScene, main));

                // LoadScene("Extra", Single) has returned: the scene exists, not loaded, without probes.
                extraScene = scenes.Loading("Extra");
                Assert.Null(LightProbes.GetSharedLightProbesForScene(new Scene(extraScene.Handle)));

                LightProbes.needsRetetrahedralization += need;
                LightProbes.tetrahedralizationCompleted += done;
                LightProbes.lightProbesUpdated += updated;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(extraScene, sceneManagerLoad: true);

                Assert.Equal(new[]
                {
                    $"sceneUnloaded Main: global 0 8 0 {mainCells} | valid True loaded False shared own"
                        + " | incoming global 0 8 0 " + mainCells + " | valid True loaded False shared null",
                }, events);
                Assert.False(new Scene(mainScene.Handle).IsValid());
                Assert.Null(LightProbes.GetSharedLightProbesForScene(new Scene(mainScene.Handle)));

                // The incoming scene's lighting data: a first scene's resets the set, its probes
                // become the global ones.
                var extra = Tetrahedralized(Box, 2f);
                LightmapSettings.Reset();
                LightProbes.SceneLoaded(extraScene.Handle.Value, extra);
                Assert.Same(extra, LightmapSettings.lightProbes);
                Assert.Equal($"global 8 8 {extra.cellCountSelf} {extra.cellCountSelf} | valid True loaded False shared own", State(extraScene, extra));
                Assert.Single(events);

                // An explicit Tetrahedralize keeps the incoming scene's own set.
                LightProbes.Tetrahedralize();
                Assert.Equal(new[] { "done" }, events.Skip(1));
                Assert.Equal(8, extra.count);
                Assert.Equal(extra.cellCountSelf, extra.cellCount);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightProbes.tetrahedralizationCompleted -= done;
                LightProbes.lightProbesUpdated -= updated;
                SceneManager.sceneUnloaded -= unloaded;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>Outgoing roots whose OnDisable and OnDestroy report the probe state, and whose
        /// OnDestroy makes a root (it goes to the incoming scene) and reports again.</summary>
        static void AddTeardownWitnesses(List<GameObject> made, List<string> events, Func<string> state, params string[] names)
        {
            foreach (var name in names)
            {
                var go = new GameObject(name);
                made.Add(go);
                var witness = go.AddComponent<ProbeTeardownWitness>();
                witness.onDisable = () => events.Add($"OnDisable {name}: {state()}");
                witness.onDestroy = () =>
                {
                    events.Add($"OnDestroy {name}: {state()}");
                    var created = new GameObject("MadeIn" + name);
                    made.Add(created);
                    events.Add($"made in OnDestroy {name} ({created.scene.name}): {state()}");
                };
            }
        }

        /// <summary>Destroys what a test made; witnesses that are still alive (a test that failed
        /// before the load) report nothing more.</summary>
        static void DestroyAll(List<GameObject> made)
        {
            foreach (var go in made.ToArray())
            {
                if (go == null) continue;
                var witness = go.GetComponent<ProbeTeardownWitness>();
                if (witness != null) witness.onDisable = witness.onDestroy = null;
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Unbound keeps the global probes whole while the outgoing scene's objects are
        /// destroyed: their OnDisable and OnDestroy, and a root made in OnDestroy, see
        /// the combined set; the reset (count and cellCount 0, own counts kept, no
        /// needsRetetrahedralization) comes after, by the outgoing sceneUnloaded (measured, Unity
        /// 6000.0.71f1: tests/projects/lightprobes Reference creation-ownership.jsonl t1.a.iv,
        /// global "18 18 73 73" with the scene already not loaded, and sceneUnloaded "0 18 0 73";
        /// lifecycle.jsonl row 22). Native destroys the roots in a varying order and observed one
        /// of them; that the reset follows the last is Unbound's placement within that window.
        /// </summary>
        [Fact]
        public void SingleLoadKeepsTheSetWhileTheOutgoingObjectsAreDestroyed()
        {
            var events = new List<string>();
            var made = new List<GameObject>();
            var scenes = new Scenes();
            SceneData mainScene = null;
            LightProbes main = null;
            Action need = () => events.Add("needs");
            UnityEngine.Events.UnityAction<Scene> unloaded = s => events.Add($"sceneUnloaded {s.name}: {State(mainScene, main)}");
            try
            {
                LightmapSettings.Reset();
                mainScene = scenes.Loaded("Main");
                main = Tetrahedralized(MainBox, 1f);
                int cells = main.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.Tetrahedralize();
                var incoming = scenes.Loading("Extra");
                AddTeardownWitnesses(made, events, () => State(mainScene, main), "First", "Last");

                LightProbes.needsRetetrahedralization += need;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);

                string whole = $"global 8 8 {cells} {cells} | valid True loaded False shared own";
                string cleared = $"global 0 8 0 {cells} | valid True loaded False shared own";
                Assert.Equal(new[]
                {
                    "OnDisable First: " + whole,
                    "OnDestroy First: " + whole,
                    "made in OnDestroy First (Extra): " + whole,
                    "OnDisable Last: " + whole,
                    "OnDestroy Last: " + whole,
                    "made in OnDestroy Last (Extra): " + whole,
                    "sceneUnloaded Main: " + cleared,
                }, events);
                Assert.Same(main, LightmapSettings.lightProbes);
                Assert.Equal((0, 8, 0, cells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));
                Assert.Null(SceneRegistry.Teardown);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                SceneManager.sceneUnloaded -= unloaded;
                try { DestroyAll(made); }
                finally
                {
                    LightmapSettings.Reset();
                    scenes.Dispose();
                }
            }
        }

        /// <summary>
        /// Several outgoing scenes and a scene a sceneUnloaded handler makes: Unity's order is not
        /// measured. This pins Unbound's choice, not Unity's: one reset for the whole load, after
        /// the first closed scene's objects are destroyed; the later scenes' objects then see the
        /// cleared set, and no closed scene raises needsRetetrahedralization.
        /// </summary>
        [Fact]
        public void SingleLoadResetsOnceForSeveralOutgoingAndHandlerMadeScenes()
        {
            var events = new List<string>();
            var made = new List<GameObject>();
            var scenes = new Scenes();
            SceneData mainScene = null;
            LightProbes main = null;
            Scene handlerMade = default;
            Action need = () => events.Add("needs");
            Func<string> current = () =>
            {
                var g = LightmapSettings.lightProbes;
                return g == null ? "null" : $"{(g == main ? "main" : "other")} {g.count} {g.countSelf} {g.cellCount} {g.cellCountSelf}";
            };
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                events.Add($"sceneUnloaded {s.name}: {current()}");
                if (!handlerMade.IsValid() && s.name == "Main") handlerMade = SceneManager.CreateScene("Made");
            };
            try
            {
                LightmapSettings.Reset();
                mainScene = scenes.Loaded("Main");
                var secondScene = scenes.Loaded("Second");
                main = Tetrahedralized(MainBox, 1f);
                var second = Tetrahedralized(Box, 2f);
                int mainCells = main.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.SceneLoaded(secondScene.Handle.Value, second);
                LightProbes.Tetrahedralize();
                int combinedCells = main.cellCount;
                Assert.Equal(16, main.count);
                var incoming = scenes.Loading("Extra");
                AddTeardownWitnesses(made, events, current, "InMain", "InSecond");
                SceneManager.MoveGameObjectToScene(made[1], new Scene(secondScene.Handle));

                LightProbes.needsRetetrahedralization += need;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);

                string whole = $"main 16 8 {combinedCells} {mainCells}";
                string cleared = $"main 0 8 0 {mainCells}";
                Assert.Equal(new[]
                {
                    "OnDisable InMain: " + whole,
                    "OnDestroy InMain: " + whole,
                    "made in OnDestroy InMain (Extra): " + whole,
                    "sceneUnloaded Main: " + cleared,
                    "OnDisable InSecond: " + cleared,
                    "OnDestroy InSecond: " + cleared,
                    "made in OnDestroy InSecond (Extra): " + cleared,
                    "sceneUnloaded Second: " + cleared,
                    "sceneUnloaded Made: " + cleared,
                }, events);
                Assert.Equal((8, 8), (second.count, second.countSelf));
                Assert.False(handlerMade.IsValid());
                Assert.Empty(SceneRegistry.Loaded);
                Assert.Null(SceneRegistry.Teardown);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                SceneManager.sceneUnloaded -= unloaded;
                try { DestroyAll(made); }
                finally
                {
                    if (handlerMade.Data != null) SceneRegistry.Forget(handlerMade.Data);
                    LightmapSettings.Reset();
                    scenes.Dispose();
                }
            }
        }

        /// <summary>A Single load that closes no scene still makes its one reset, when its teardown
        /// ends, as it did before the reset waited for the outgoing objects: probes registered for
        /// a scene that is not loaded leave the set without needsRetetrahedralization. Not
        /// measured; it keeps the reset once per load.</summary>
        [Fact]
        public void SingleLoadWithoutOutgoingScenesStillResetsOnce()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            Action need = () => events.Add("needs");
            try
            {
                LightmapSettings.Reset();
                var stale = scenes.Loading("Stale");
                var probes = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(stale.Handle.Value, probes);
                LightProbes.Tetrahedralize();
                Assert.Equal(8, probes.count);
                var incoming = scenes.Loading("Extra");
                Assert.Empty(SceneRegistry.Loaded);

                LightProbes.needsRetetrahedralization += need;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);

                Assert.Empty(events);
                Assert.Equal((0, 8, 0), (probes.count, probes.countSelf, probes.cellCount));
                Assert.True(probes.cellCountSelf > 0);
                Assert.Null(SceneRegistry.Teardown);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>A sceneUnloaded handler that throws runs after the reset; the exception leaves
        /// the reset made once and the teardown ended (its creation target and listing restored).</summary>
        [Fact]
        public void SingleLoadResetPrecedesAThrowingSceneUnloadedHandler()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            LightProbes main = null;
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                events.Add($"sceneUnloaded {s.name}: {main.count} {main.countSelf}");
                throw new InvalidOperationException("handler");
            };
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                main = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.Tetrahedralize();
                var incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                Assert.Throws<InvalidOperationException>(() => SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true));

                Assert.Equal(new[] { "sceneUnloaded Main: 0 8" }, events);
                Assert.Equal((0, 8, 0), (main.count, main.countSelf, main.cellCount));
                Assert.Null(SceneRegistry.Teardown);
                Assert.Null(SceneRegistry.CreationTarget);
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>A scene's LightmapSettings document as a scene file has it (no lighting data).</summary>
        const string LightmapSettingsDocument = @"--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_LightingDataAsset: {fileID: 0}
";

        /// <summary>
        /// A SceneManager Single load keeps the outgoing active scene active through its
        /// sceneUnloaded: GetActiveScene there answers it, valid and no longer loaded, and creates
        /// no untitled scene (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// lifecycle.jsonl, branch t3/light-probes-lifecycle-2, commit f8516528, rows 22-23:
        /// active scene0 in sceneUnloaded, then activeSceneChanged from none). The incoming scene
        /// is then the only loaded one, so its LightmapSettings (SceneSettings.Apply, the
        /// production first-scene decision) resets the lighting and its probes (BakedLighting.Read
        /// of the lighting data reply) become the global ones, without an event. Only reading the
        /// scene file and the lighting data reply needs the native side; this test supplies them.
        /// </summary>
        [Fact]
        public void SingleLoadKeepsTheOutgoingSceneActiveThroughSceneUnloaded()
        {
            // SceneSettings.Apply reads the LightmapSettings document and clears these globals.
            // Keep the lighting state that belonged to the surrounding test process.
            var bakedProbe = RenderSettings.s_BakedProbe;
            var environmentProbe = RenderSettings.s_EnvironmentProbe;
            var bakedReflectionField = typeof(RenderSettings).GetField("s_BakedReflection",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var bakedReflection = bakedReflectionField.GetValue(null);
            var events = new List<string>();
            var scenes = new Scenes();
            SceneData mainScene = null;
            Action need = () => events.Add("needs");
            Action updated = () => events.Add("updated");
            UnityEngine.Events.UnityAction<Scene, Scene> changed = (a, b) => events.Add($"activeSceneChanged {a.name} {b.name}");
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                var active = SceneManager.GetActiveScene();
                events.Add($"sceneUnloaded {s.name}: active {active.name} same {active == new Scene(mainScene.Handle)}"
                    + $" valid {active.IsValid()} loaded {active.isLoaded} | untitled {SceneRegistry.Loaded.Exists(x => x.Name == "")}");
            };
            try
            {
                LightmapSettings.Reset();
                mainScene = scenes.Loaded("Main");
                var main = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                LightProbes.Tetrahedralize();
                var extraScene = scenes.Loading("Extra");

                LightProbes.needsRetetrahedralization += need;
                LightProbes.lightProbesUpdated += updated;
                SceneManager.activeSceneChanged += changed;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(extraScene, sceneManagerLoad: true);

                Assert.Equal(new[] { "sceneUnloaded Main: active Main same True valid True loaded False | untitled False" }, events);
                // Cleared after the handlers, before the incoming scene is activated; no untitled scene.
                Assert.Null(SceneRegistry.Active);
                Assert.Empty(SceneRegistry.Loaded);
                Assert.False(new Scene(mainScene.Handle).IsValid());

                // SceneManagerInternal.Perform then adds the incoming scene and reads its file.
                SceneRegistry.Loaded.Add(extraScene);
                extraScene.IsLoaded = true;
                SceneSettings.Apply(UnityYaml.Parse(LightmapSettingsDocument)[0], null, extraScene);
                Assert.Null(LightmapSettings.lightProbes);
                var reply = JsonDocument.Parse($"{{\"probes\": {ProbesJson(Box, _ => 2f)}}}").RootElement;
                BakedLighting.Read(reply, null, extraScene.Handle.Value);

                var extra = LightmapSettings.lightProbes;
                Assert.NotNull(extra);
                Assert.NotSame(main, extra);
                Assert.Same(extra, LightProbes.GetSharedLightProbesForScene(new Scene(extraScene.Handle)));
                Assert.Equal((8, 8), (extra.count, extra.countSelf));
                Assert.Equal(extra.cellCountSelf, extra.cellCount);
                LightProbes.Tetrahedralize();
                Assert.Same(extra, LightmapSettings.lightProbes);
                Assert.Equal((8, 8), (extra.count, extra.countSelf));
                Assert.Equal(extra.cellCountSelf, extra.cellCount);
                Assert.Single(events);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightProbes.lightProbesUpdated -= updated;
                SceneManager.activeSceneChanged -= changed;
                SceneManager.sceneUnloaded -= unloaded;
                try
                {
                    LightmapSettings.Reset();
                    scenes.Dispose();
                }
                finally
                {
                    RenderSettings.s_BakedProbe = bakedProbe;
                    RenderSettings.s_EnvironmentProbe = environmentProbe;
                    bakedReflectionField.SetValue(null, bakedReflection);
                }
            }
        }

        /// <summary>The outgoing scene stays active only while no handler chose another: a scene a
        /// sceneUnloaded handler made active is kept through that handler and through its own
        /// sceneUnloaded, and the same Single load then unloads it, before the incoming scene is
        /// activated (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl, t2: activeSceneChanged Extra -> Made, then sceneUnloaded(Made)
        /// with Made still active). The handler makes its scene once: a handler that makes one on
        /// every sceneUnloaded is SingleLoadTeardownStopsLoudlyAtItsSceneLimit.</summary>
        [Fact]
        public void SingleLoadKeepsAnActiveSceneAHandlerChose()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            Scene made = default;
            bool madeOnce = false;
            UnityEngine.Events.UnityAction<Scene, Scene> changed = (a, b) => events.Add($"activeSceneChanged {a.name} {b.name}");
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                if (!madeOnce)
                {
                    madeOnce = true;
                    made = SceneManager.CreateScene("Made");
                    SceneManager.SetActiveScene(made);
                }
                var active = SceneManager.GetActiveScene();
                events.Add($"sceneUnloaded {s.name}: active {active.name} loaded {active.isLoaded}");
            };
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                SceneManager.activeSceneChanged += changed;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                Assert.Equal(new[]
                {
                    "activeSceneChanged Main Made",
                    "sceneUnloaded Main: active Made loaded True",
                    "sceneUnloaded Made: active Made loaded False",
                }, events);
                // The same load unloaded the handler's scene; nothing is active until the incoming
                // scene is (SceneManagerInternal.Perform).
                Assert.Null(SceneRegistry.Active);
                Assert.Empty(SceneRegistry.Loaded);
                Assert.False(made.IsValid());
                Assert.False(new Scene(mainScene.Handle).IsValid());
                Assert.Null(SceneRegistry.CreationTarget);
            }
            finally
            {
                SceneManager.activeSceneChanged -= changed;
                SceneManager.sceneUnloaded -= unloaded;
                if (made.Data != null) SceneRegistry.Forget(made.Data);
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>A sceneUnloaded handler that throws still leaves no outgoing scene active; the
        /// exception and the unforgotten scene are as before the active scene was kept.</summary>
        [Fact]
        public void SingleLoadClearsTheKeptActiveSceneWhenAHandlerThrows()
        {
            var scenes = new Scenes();
            UnityEngine.Events.UnityAction<Scene> unloaded = s => throw new InvalidOperationException("handler");
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                Assert.Throws<InvalidOperationException>(() => SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true));
                Assert.Null(SceneRegistry.Active);
                Assert.Empty(SceneRegistry.Loaded);
                Assert.Same(mainScene, SceneRegistry.Get(mainScene.Handle));
                Assert.False(mainScene.IsLoaded);
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>Single loads that are not SceneManager loads (the Editor's OpenScene, Play
        /// mode entry, AssetBundle scenes) keep Unbound's per-scene unload: needsRetetrahedralization
        /// and the set kept. Unity's behaviour there is not measured; this pins the boundary of
        /// the measured change, it does not claim Unity's.</summary>
        [Fact]
        public void OtherSingleLoadsKeepThePerSceneUnload()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            Action need = () => events.Add("needs");
            UnityEngine.Events.UnityAction<Scene> unloaded = s => events.Add($"sceneUnloaded {s.name} active {SceneRegistry.Active?.Name ?? "none"}");
            try
            {
                LightmapSettings.Reset();
                var mainScene = scenes.Loaded("Main");
                var main = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                var incoming = scenes.Loading("Extra");
                LightProbes.needsRetetrahedralization += need;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: false);
                Assert.Equal(new[] { "needs", "sceneUnloaded Main active none" }, events);
                Assert.Equal(8, main.count);
                Assert.Equal(main.cellCountSelf, main.cellCount);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
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
