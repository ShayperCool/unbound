using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// Which probes object a scene load gives: one per Lighting Data identity (the scene's
    /// m_LightingDataAsset guid and fileID) while that object is still referenced. Each Fact
    /// labels its assertions as one of:
    /// <list type="bullet">
    /// <item>measured: Unity 6000.0.71f1 only, tests/projects/lightprobes Reference
    /// lifecycle.jsonl and creation-ownership.jsonl, each published from two identical Editor
    /// captures whose witnesses keep every probes object they observe;</item>
    /// <item>inferred: an Unbound mechanism inferred from the documented sharing remark
    /// (Scripting API 6000.0, LightProbes.GetSharedLightProbesForScene: modifying the object "may
    /// affect all currently loaded versions of the scene, or change the light probe data stored in
    /// Lighting Data assets"), not a documented identity and not measured;</item>
    /// <item>internal: a preservation or cleanup control of Unbound's own bookkeeping, not a Unity
    /// rule.</item>
    /// </list>
    /// Identities are new random GUIDs and every scene path is unique to its test; the Scenes
    /// helper restores the identity map. Nothing here depends on when the CLR collects an object.
    /// </summary>
    public partial class LightProbesTests
    {
        /// <summary>A LightingData asset's main object (class 1120).</summary>
        const long LightingDataFileId = 112000000;

        static string UniqueScenePath() => $"Assets/UnboundTests/{Guid.NewGuid():N}.unity";

        /// <summary>A project Lighting Data reference no other test names.</summary>
        static ObjectRef NewLightingData(long fileId = LightingDataFileId) =>
            new ObjectRef(fileId, Guid.NewGuid().ToString("N"), 2);

        /// <summary>
        /// Applies the retained-object reload mechanism to a self-reload. That configuration is
        /// not captured. The measured histories are Main -> Extra -> Main (creation seq21 then
        /// seq69-73) and a later load of the earlier Extra (lifecycle row22 then23-29).
        /// Guard: the admitted reset at sceneUnloaded keeps the own counts, but clears the combined
        /// count and cells (lifecycle row22, creation seq75). Internal: the fresh duplicate leaves
        /// the registry and the kept object stays registered. Neither this registry choice nor the
        /// self-reload shape is a universal Unity identity guarantee.
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
                var data = NewLightingData();
                var first = scenes.Loaded("Reloaded", path);
                var kept = Tetrahedralized(MainBox, 1f);
                int cells = kept.cellCountSelf;
                LightProbes.SceneLoaded(first.Handle.Value, data, kept);
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
                LightProbes.SceneLoaded(incoming.Handle.Value, data, read);

                Assert.Same(kept, LightmapSettings.lightProbes);
                Assert.Same(kept, LightProbes.GetSharedLightProbesForScene(new Scene(incoming.Handle)));
                Assert.Equal((8, 8, cells, cells), (kept.count, kept.countSelf, kept.cellCount, kept.cellCountSelf));
                Assert.Null(ObjectRegistry.FindById(read.GetInstanceID()));
                Assert.Same(kept, ObjectRegistry.FindById(kept.GetInstanceID()));
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
        /// Measured: a scene loaded additively, unloaded (UnloadSceneAsync) and later loaded by a
        /// Single load of it from another scene gets the object its first load gave, which the
        /// script still holds (lifecycle.jsonl rows 3-5 and 23-29: "probes1 8 8 22 22" both times;
        /// also creation-ownership.jsonl seq 39 and 77, a Single load each time). Guard: the
        /// outgoing scene's own object keeps the admitted reset (lifecycle.jsonl row 22).
        /// </summary>
        [Fact]
        public void SingleLoadOfAnEarlierUnloadedSceneGivesItsReferencedObjectBack()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                string mainPath = UniqueScenePath(), extraPath = UniqueScenePath();
                var mainData = NewLightingData();
                var extraData = NewLightingData();
                var mainScene = scenes.Loaded("Main", mainPath);
                var main = Tetrahedralized(MainBox, 1f);
                int mainCells = main.cellCountSelf;
                LightProbes.SceneLoaded(mainScene.Handle.Value, mainData, main);
                var extraScene = scenes.Loaded("Extra", extraPath);
                var extra = Tetrahedralized(Box, 2f);
                int extraCells = extra.cellCountSelf;
                LightProbes.SceneLoaded(extraScene.Handle.Value, extraData, extra);
                LightProbes.Tetrahedralize();
                SceneManagerInternal.Unload(extraScene);
                LightProbes.Tetrahedralize();
                Assert.Equal((8, 8, mainCells, mainCells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));

                var incoming = scenes.Loading("Extra", extraPath);
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                Assert.Equal((0, 8, 0, mainCells), (main.count, main.countSelf, main.cellCount, main.cellCountSelf));

                var read = Tetrahedralized(Box, 2f);
                LightmapSettings.Reset();
                LightProbes.SceneLoaded(incoming.Handle.Value, extraData, read);

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
        /// Inferred, not measured: a second loaded version of a scene shares its object. Guards: an
        /// object in use keeps its data, so the current probes do not change before the next
        /// tetrahedralization, and the additive load still raises needsRetetrahedralization (Manual
        /// 6000.0 "Load Light Probes in multiple scenes"; measured: lifecycle.jsonl rows 3-6).
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
                var data = NewLightingData();
                var firstScene = scenes.Loaded("Twice", path);
                var shared = Tetrahedralized(MainBox, 1f);
                int cells = shared.cellCountSelf;
                LightProbes.SceneLoaded(firstScene.Handle.Value, data, shared);

                LightProbes.needsRetetrahedralization += need;
                var secondScene = scenes.Loaded("Twice", path);
                LightProbes.SceneLoaded(secondScene.Handle.Value, data, Tetrahedralized(MainBox, 1f));

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
        /// Inferred, not measured: two scene files naming one Lighting Data asset share its object.
        /// A Single load of the second file, while a script holds the first file's object, gives
        /// that object back with the asset's whole set.
        /// </summary>
        [Fact]
        public void TwoSceneFilesNamingOneLightingDataGetOneObject()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                var data = NewLightingData();
                var firstScene = scenes.Loaded("First", UniqueScenePath());
                var shared = Tetrahedralized(MainBox, 1f);
                int cells = shared.cellCountSelf;
                LightProbes.SceneLoaded(firstScene.Handle.Value, data, shared);

                var incoming = scenes.Loading("Second", UniqueScenePath());
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                var read = Tetrahedralized(MainBox, 1f);
                LightmapSettings.Reset();
                LightProbes.SceneLoaded(incoming.Handle.Value, data, read);

                Assert.Same(shared, LightmapSettings.lightProbes);
                Assert.Same(shared, LightProbes.GetSharedLightProbesForScene(new Scene(incoming.Handle)));
                Assert.Equal((8, 8, cells, cells), (shared.count, shared.countSelf, shared.cellCount, shared.cellCountSelf));
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Internal: a version of one scene path whose lighting data names another asset (another
        /// guid), or another object of the same asset file (another fileID), gets its own object,
        /// also while the first version's object is the current probes; the earlier object is
        /// neither shared nor changed and stays registered. No hot reload or reimport is implied.
        /// </summary>
        [Fact]
        public void OneScenePathNamingAnotherLightingDataGetsAnotherObject()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                string path = UniqueScenePath();
                var before = NewLightingData();
                var firstScene = scenes.Loaded("Edited", path);
                var old = Tetrahedralized(MainBox, 1f);
                int oldCells = old.cellCountSelf;
                LightProbes.SceneLoaded(firstScene.Handle.Value, before, old);

                foreach (var after in new[] { NewLightingData(), new ObjectRef(before.FileId + 1, before.Guid, 2) })
                {
                    var version = scenes.Loaded("Edited", path);
                    var read = Tetrahedralized(Box, 2f);
                    int readCells = read.cellCountSelf;
                    LightProbes.SceneLoaded(version.Handle.Value, after, read);

                    Assert.Same(read, LightProbes.GetSharedLightProbesForScene(new Scene(version.Handle)));
                    Assert.Equal((8, readCells), (read.countSelf, read.cellCountSelf));
                    Assert.Same(read, ObjectRegistry.FindById(read.GetInstanceID()));
                    Assert.Same(old, LightmapSettings.lightProbes);
                    Assert.Same(old, LightProbes.GetSharedLightProbesForScene(new Scene(firstScene.Handle)));
                    Assert.Equal(MainBox, old.GetPositionsSelf());
                    Assert.Equal(oldCells, old.cellCountSelf);
                    Assert.Same(old, ObjectRegistry.FindById(old.GetInstanceID()));
                }
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Internal: two fileIDs of one guid are two identities. Two loaded versions of one scene
        /// path naming them get two objects.
        /// </summary>
        [Fact]
        public void DistinctFileIdsOfOneGuidAreDistinctIdentities()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                string path = UniqueScenePath();
                var a = NewLightingData();
                var b = new ObjectRef(a.FileId + 2, a.Guid, 2);
                var aScene = scenes.Loaded("Versions", path);
                var aProbes = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(aScene.Handle.Value, a, aProbes);
                var bScene = scenes.Loaded("Versions", path);
                var bProbes = Tetrahedralized(Box, 2f);
                LightProbes.SceneLoaded(bScene.Handle.Value, b, bProbes);

                Assert.Same(aProbes, LightProbes.GetSharedLightProbesForScene(new Scene(aScene.Handle)));
                Assert.Same(bProbes, LightProbes.GetSharedLightProbesForScene(new Scene(bScene.Handle)));
                Assert.Same(bProbes, ObjectRegistry.FindById(bProbes.GetInstanceID()));
                Assert.Equal(Box, bProbes.GetPositionsSelf());
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Internal: only a duplicate this API never handed out leaves the registry. Passing the kept
        /// object itself again keeps it (the ReferenceEquals guard), and an object already handed out
        /// for another scene stays registered although the identity's kept object is used instead.
        /// </summary>
        [Fact]
        public void OnlyANeverHandedOutDuplicateLeavesTheRegistry()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                var data = NewLightingData();
                var keptScene = scenes.Loaded("Kept", UniqueScenePath());
                var kept = Tetrahedralized(MainBox, 1f);
                LightProbes.SceneLoaded(keptScene.Handle.Value, data, kept);

                LightProbes.SceneLoaded(keptScene.Handle.Value, data, kept);
                Assert.Same(kept, LightProbes.GetSharedLightProbesForScene(new Scene(keptScene.Handle)));
                Assert.Same(kept, ObjectRegistry.FindById(kept.GetInstanceID()));

                var otherScene = scenes.Loaded("Other", UniqueScenePath());
                var other = Tetrahedralized(Box, 2f);
                LightProbes.SceneLoaded(otherScene.Handle.Value, NewLightingData(), other);
                var thirdScene = scenes.Loaded("Third", UniqueScenePath());
                LightProbes.SceneLoaded(thirdScene.Handle.Value, data, other);

                Assert.Same(kept, LightProbes.GetSharedLightProbesForScene(new Scene(thirdScene.Handle)));
                Assert.Same(other, LightProbes.GetSharedLightProbesForScene(new Scene(otherScene.Handle)));
                Assert.Same(other, ObjectRegistry.FindById(other.GetInstanceID()));
                Assert.Same(kept, ObjectRegistry.FindById(kept.GetInstanceID()));
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Guards, not measured, the previous behaviour: no Lighting Data identity (none, a local
        /// reference, Unity's built-in resources), a scene without a project path (made at run
        /// time), handle 0, and an AssetBundle's scene keep the object their lighting data gives. So
        /// does an identity whose earlier object was destroyed, or is pending destruction (it would
        /// turn fake-null at the end of the frame). A loaded scene without its own bake still falls
        /// back to the current probes (the existing raw fallback).
        /// </summary>
        [Fact]
        public void UnkeyedScenesAndDestroyedObjectsGetTheReadObject()
        {
            var scenes = new Scenes();
            LightProbes pending = null;
            try
            {
                LightmapSettings.Reset();
                var noIdentities = new[]
                {
                    default(ObjectRef),
                    new ObjectRef(LightingDataFileId, null, 0),
                    // unity_builtin_extra, Unity's own GUID (not a fixture's).
                    new ObjectRef(LightingDataFileId, "0000000000000000f000000000000000", 0),
                };
                LightProbes first = null;
                foreach (var none in noIdentities)
                {
                    string path = UniqueScenePath();
                    var once = scenes.Loaded("Unkeyed", path);
                    var a = Baked(MainBox, _ => 1f);
                    LightProbes.SceneLoaded(once.Handle.Value, none, a);
                    first ??= a;
                    var again = scenes.Loaded("Unkeyed", path);
                    var b = Baked(MainBox, _ => 1f);
                    LightProbes.SceneLoaded(again.Handle.Value, none, b);
                    Assert.Same(b, LightProbes.GetSharedLightProbesForScene(new Scene(again.Handle)));
                    Assert.Same(b, ObjectRegistry.FindById(b.GetInstanceID()));
                }

                var data = NewLightingData();
                var unnamed = scenes.Loaded("Unsaved");
                LightProbes.SceneLoaded(unnamed.Handle.Value, data, Baked(MainBox, _ => 1f));
                var unnamedAgain = scenes.Loaded("Unsaved again");
                var second = Baked(MainBox, _ => 1f);
                LightProbes.SceneLoaded(unnamedAgain.Handle.Value, data, second);
                Assert.Same(second, LightProbes.GetSharedLightProbesForScene(new Scene(unnamedAgain.Handle)));

                LightProbes.SceneLoaded(0, data, Baked(MainBox, _ => 1f));
                var handleZero = Baked(MainBox, _ => 1f);
                LightProbes.SceneLoaded(0, data, handleZero);
                Assert.Same(handleZero, ObjectRegistry.FindById(handleZero.GetInstanceID()));
                LightProbes.SceneForgotten(0);

                string bundlePath = UniqueScenePath();
                var bundleData = NewLightingData();
                var bundled = scenes.Loaded("Bundled", bundlePath);
                bundled.LoadKey = "bundle:" + bundlePath;
                LightProbes.SceneLoaded(bundled.Handle.Value, bundleData, Baked(Box, _ => 2f));
                var bundledAgain = scenes.Loaded("Bundled", bundlePath);
                bundledAgain.LoadKey = bundled.LoadKey;
                var bundledRead = Baked(Box, _ => 2f);
                LightProbes.SceneLoaded(bundledAgain.Handle.Value, bundleData, bundledRead);
                Assert.Same(bundledRead, LightProbes.GetSharedLightProbesForScene(new Scene(bundledAgain.Handle)));

                var destroyedData = NewLightingData();
                var destroyedScene = scenes.Loaded("Destroyed", UniqueScenePath());
                var destroyed = Baked(Box, _ => 3f);
                LightProbes.SceneLoaded(destroyedScene.Handle.Value, destroyedData, destroyed);
                SceneManagerInternal.Unload(destroyedScene);
                UnityEngine.Object.DestroyImmediate(destroyed);
                var reloaded = scenes.Loaded("Destroyed", UniqueScenePath());
                var fresh = Baked(Box, _ => 3f);
                LightProbes.SceneLoaded(reloaded.Handle.Value, destroyedData, fresh);
                Assert.Same(fresh, LightProbes.GetSharedLightProbesForScene(new Scene(reloaded.Handle)));

                var pendingData = NewLightingData();
                var pendingScene = scenes.Loaded("Pending", UniqueScenePath());
                pending = Baked(Box, _ => 4f);
                LightProbes.SceneLoaded(pendingScene.Handle.Value, pendingData, pending);
                SceneManagerInternal.Unload(pendingScene);
                // Internal guard: only the pending flag is needed. Do not enqueue a destruction
                // that would leave the test's object in the global end-of-frame queue.
                pending.m_PendingDestroy = true;
                Assert.True(pending.m_PendingDestroy);
                var pendingAgain = scenes.Loaded("Pending", UniqueScenePath());
                var replacement = Baked(Box, _ => 4f);
                LightProbes.SceneLoaded(pendingAgain.Handle.Value, pendingData, replacement);
                Assert.Same(replacement, LightProbes.GetSharedLightProbesForScene(new Scene(pendingAgain.Handle)));
                Assert.Same(replacement, ObjectRegistry.FindById(replacement.GetInstanceID()));

                var unbaked = scenes.Loaded("Unbaked", UniqueScenePath());
                Assert.Same(LightmapSettings.lightProbes, LightProbes.GetSharedLightProbesForScene(new Scene(unbaked.Handle)));
                Assert.Same(first, LightmapSettings.lightProbes);
            }
            finally
            {
                if (pending != null && !pending.m_Destroyed)
                {
                    pending.m_PendingDestroy = false;
                    UnityEngine.Object.DestroyImmediate(pending);
                }
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        /// <summary>
        /// Guard: the production entry, BakedLighting.Read with the scene's Lighting Data reference,
        /// makes its own object and, on a reload, gives the kept one back (the measured reload of
        /// SingleReloadGivesTheStillReferencedProbesObjectBackWhole through the real caller); without
        /// a reference it gives a new object, as before.
        /// </summary>
        [Fact]
        public void BakedLightingReadPassesTheLightingDataIdentity()
        {
            var scenes = new Scenes();
            try
            {
                LightmapSettings.Reset();
                string path = UniqueScenePath();
                var data = NewLightingData();
                string Reply() => $"{{\"probes\":{ProbesJson(MainBox, _ => 1f)}}}";
                var first = scenes.Loaded("Read", path);
                using (var reply = System.Text.Json.JsonDocument.Parse(Reply()))
                    BakedLighting.Read(reply.RootElement, null, first.Handle.Value, data);
                var kept = LightProbes.GetSharedLightProbesForScene(new Scene(first.Handle));
                Assert.NotNull(kept);

                var incoming = scenes.Loading("Read", path);
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                LightmapSettings.Reset();
                using (var reply = System.Text.Json.JsonDocument.Parse(Reply()))
                    BakedLighting.Read(reply.RootElement, null, incoming.Handle.Value, data);
                Assert.Same(kept, LightmapSettings.lightProbes);
                Assert.Same(kept, LightProbes.GetSharedLightProbesForScene(new Scene(incoming.Handle)));

                var unkeyed = scenes.Loaded("Read", path);
                using (var reply = System.Text.Json.JsonDocument.Parse(Reply()))
                    BakedLighting.Read(reply.RootElement, null, unkeyed.Handle.Value);
                Assert.NotSame(kept, LightProbes.GetSharedLightProbesForScene(new Scene(unkeyed.Handle)));
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }
    }
}
