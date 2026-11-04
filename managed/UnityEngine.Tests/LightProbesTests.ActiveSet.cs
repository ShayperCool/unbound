using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// Which probes object a scene load gives: one per scene file while it is still referenced.
    /// Unity documents the shared LightProbes object as its Lighting Data asset's own, shared by
    /// all loaded versions of the scene (Scripting API 6000.0,
    /// LightProbes.GetSharedLightProbesForScene), and keeps an asset a static variable reaches
    /// (Resources.UnloadUnusedAssets). The reload identity itself is measured, not documented:
    /// Unity 6000.0.71f1, tests/projects/lightprobes Reference lifecycle.jsonl rows 20-29 and
    /// creation-ownership.jsonl seq 61-80, each published from two identical Editor captures whose
    /// witnesses keep every probes object they observe. Every scene path is unique to its test.
    /// </summary>
    public partial class LightProbesTests
    {
        static string UniqueScenePath() => $"Assets/UnboundTests/{Guid.NewGuid():N}.unity";

        /// <summary>
        /// A Single reload of the scene whose probes object a script still holds (the outgoing scene
        /// itself). The admitted reset stays as it was: at the outgoing sceneUnloaded the object has
        /// count and cellCount 0 and keeps its own counts, not null, and no event is raised (guard;
        /// lifecycle.jsonl row 22, creation-ownership.jsonl seq 75). The incoming lighting data then
        /// gives that same object back with the scene's whole set (red before this change: a new
        /// object; creation-ownership.jsonl seq 69-80, "probes0 18 18 73 73" after "probes0 0 18 0 73",
        /// and lifecycle.jsonl rows 23-29 for the other scene), and the object read for it is not left
        /// registered (red).
        /// </summary>
        [Fact]
        public void SingleReloadGivesTheStillReferencedProbesObjectBackWhole()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            Action need = () => events.Add("needs");
            Action updated = () => events.Add("updated");
            try
            {
                LightmapSettings.Reset();
                string path = UniqueScenePath();
                var first = scenes.Loaded("Reloaded", path);
                var kept = Tetrahedralized(MainBox, 1f);
                int cells = kept.cellCountSelf;
                LightProbes.SceneLoaded(first.Handle.Value, kept);
                Assert.Same(kept, LightmapSettings.lightProbes);

                var incoming = scenes.Loading("Reloaded", path);
                LightProbes.needsRetetrahedralization += need;
                LightProbes.lightProbesUpdated += updated;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                // Guard: the admitted Single-load reset, empty but valid, not null.
                Assert.Same(kept, LightmapSettings.lightProbes);
                Assert.Equal((0, 8, 0, cells), (kept.count, kept.countSelf, kept.cellCount, kept.cellCountSelf));

                // The incoming scene's lighting data (a first scene's: the set is reset first).
                var read = Tetrahedralized(MainBox, 1f);
                LightmapSettings.Reset();
                LightProbes.SceneLoaded(incoming.Handle.Value, read);

                Assert.Same(kept, LightmapSettings.lightProbes);
                Assert.Same(kept, LightProbes.GetSharedLightProbesForScene(new Scene(incoming.Handle)));
                Assert.Equal((8, 8, cells, cells), (kept.count, kept.countSelf, kept.cellCount, kept.cellCountSelf));
                Assert.Null(ObjectRegistry.FindById(read.GetInstanceID()));
                Assert.Empty(events);
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightProbes.lightProbesUpdated -= updated;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// A scene loaded additively, unloaded (UnloadSceneAsync) and later loaded by a Single load
        /// of it from another scene gets the object its first load gave, which the script still
        /// holds (red; lifecycle.jsonl rows 3-5 and 23-29: "probes1 8 8 22 22" both times; also
        /// creation-ownership.jsonl seq 39 and 77, a Single load each time). The outgoing scene's own
        /// object keeps the admitted reset (guard; lifecycle.jsonl row 22).
        /// </summary>
        [Fact]
        public void SingleLoadOfAnEarlierUnloadedSceneGivesItsReferencedObjectBack()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                string mainPath = UniqueScenePath(), extraPath = UniqueScenePath();
                var mainScene = scenes.Loaded("Main", mainPath);
                var main = Tetrahedralized(MainBox, 1f);
                int mainCells = main.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, main);
                var extraScene = scenes.Loaded("Extra", extraPath);
                var extra = Tetrahedralized(Box, 2f);
                int extraCells = extra.cellCountSelf;
                LightProbes.SceneLoaded(extraScene.Handle.Value, extra);
                LightProbes.Tetrahedralize();
                SceneManagerInternal.Unload(extraScene);
                LightProbes.Tetrahedralize();
                Assert.Equal((8, 8, mainCells, mainCells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));

                var incoming = scenes.Loading("Extra", extraPath);
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                Assert.Equal((0, 8, 0, mainCells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));

                var read = Tetrahedralized(Box, 2f);
                LightmapSettings.Reset();
                LightProbes.SceneLoaded(incoming.Handle.Value, read);

                Assert.Same(extra, LightmapSettings.lightProbes);
                Assert.Same(extra, LightProbes.GetSharedLightProbesForScene(new Scene(incoming.Handle)));
                Assert.Equal((8, 8, extraCells, extraCells), (extra.count, extra.countSelf, extra.cellCount, extra.cellCountSelf));
                // Guard: the earlier current object keeps its reset; it is no scene's any more.
                Assert.Equal((0, 8, 0, mainCells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));
                Assert.NotSame(main, LightmapSettings.lightProbes);
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// A second loaded version of a scene shares its object (red; documented by
        /// GetSharedLightProbesForScene, not measured). An object in use keeps its data: the
        /// current probes do not change before the next tetrahedralization and the additive load
        /// still raises needsRetetrahedralization (guard; Manual 6000.0 "Load Light Probes in
        /// multiple scenes", reference lifecycle.jsonl rows 3-6).
        /// </summary>
        [Fact]
        public void AdditiveLoadOfALoadedSceneSharesItsObjectWithoutChangingTheCurrentSet()
        {
            var events = new List<string>();
            var scenes = new Scenes();
            Action need = () => events.Add("needs");
            try
            {
                LightmapSettings.Reset();
                string path = UniqueScenePath();
                var firstScene = scenes.Loaded("Twice", path);
                var shared = Tetrahedralized(MainBox, 1f);
                int cells = shared.cellCountSelf;
                LightProbes.SceneLoaded(firstScene.Handle.Value, shared);

                LightProbes.needsRetetrahedralization += need;
                var secondScene = scenes.Loaded("Twice", path);
                LightProbes.SceneLoaded(secondScene.Handle.Value, Tetrahedralized(MainBox, 1f));

                Assert.Equal(new[] { "needs" }, events);
                Assert.Same(shared, LightmapSettings.lightProbes);
                Assert.Same(shared, LightProbes.GetSharedLightProbesForScene(new Scene(firstScene.Handle)));
                Assert.Same(shared, LightProbes.GetSharedLightProbesForScene(new Scene(secondScene.Handle)));
                Assert.Equal((8, 8, cells, cells), (shared.count, shared.countSelf, shared.cellCount, shared.cellCountSelf));
            }
            finally
            {
                LightProbes.needsRetetrahedralization -= need;
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Guards: a scene without a project path (made at run time) and an AssetBundle's scene
        /// keep the object their lighting data gives, as before (not measured); so does a scene
        /// whose earlier object was destroyed. A loaded scene without its own bake still falls back
        /// to the current probes (the existing raw fallback).
        /// </summary>
        [Fact]
        public void ScenesWithoutAProjectFileOrWithADestroyedObjectGetTheReadObject()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                var unnamed = scenes.Loaded("Unsaved");
                var first = Baked(MainBox, _ => 1f);
                LightProbes.SceneLoaded(unnamed.Handle.Value, first);
                var unnamedAgain = scenes.Loaded("Unsaved again");
                var second = Baked(MainBox, _ => 1f);
                LightProbes.SceneLoaded(unnamedAgain.Handle.Value, second);
                Assert.Same(second, LightProbes.GetSharedLightProbesForScene(new Scene(unnamedAgain.Handle)));

                string bundlePath = UniqueScenePath();
                var bundled = scenes.Loaded("Bundled", bundlePath);
                bundled.LoadKey = "bundle:" + bundlePath;
                LightProbes.SceneLoaded(bundled.Handle.Value, Baked(Box, _ => 2f));
                var bundledAgain = scenes.Loaded("Bundled", bundlePath);
                bundledAgain.LoadKey = bundled.LoadKey;
                var bundledRead = Baked(Box, _ => 2f);
                LightProbes.SceneLoaded(bundledAgain.Handle.Value, bundledRead);
                Assert.Same(bundledRead, LightProbes.GetSharedLightProbesForScene(new Scene(bundledAgain.Handle)));

                string path = UniqueScenePath();
                var destroyedScene = scenes.Loaded("Destroyed", path);
                var destroyed = Baked(Box, _ => 3f);
                LightProbes.SceneLoaded(destroyedScene.Handle.Value, destroyed);
                SceneManagerInternal.Unload(destroyedScene);
                UnityEngine.Object.DestroyImmediate(destroyed);
                var reloaded = scenes.Loaded("Destroyed", path);
                var fresh = Baked(Box, _ => 3f);
                LightProbes.SceneLoaded(reloaded.Handle.Value, fresh);
                Assert.Same(fresh, LightProbes.GetSharedLightProbesForScene(new Scene(reloaded.Handle)));

                var unbaked = scenes.Loaded("Unbaked", UniqueScenePath());
                Assert.Same(LightmapSettings.lightProbes, LightProbes.GetSharedLightProbesForScene(new Scene(unbaked.Handle)));
                Assert.Same(first, LightmapSettings.lightProbes);
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }
    }
}
