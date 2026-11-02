using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;
using UObject = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Records what an object sees of itself when it wakes up, and runs an action in its
    /// OnDestroy (the creation-ownership witness's tracker).</summary>
    public class TeardownMade : MonoBehaviour
    {
        internal static readonly List<string> Awoke = new List<string>();
        internal Action onDestroy;

        void Awake() => Awoke.Add(SceneCreationOwnershipTests.Describe(gameObject));
        void OnDestroy() => onDestroy?.Invoke();
    }

    /// <summary>
    /// Ownership of the objects made while a SceneManager Single load closes the scenes it
    /// replaces (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
    /// creation-ownership.jsonl). Roots made without a scene of their own (new GameObject,
    /// Instantiate, a child moved to the root, in sceneUnloaded and in an outgoing object's
    /// OnDestroy) go to the incoming scene before they wake up and are found there during the
    /// callbacks, while GetActiveScene still answers the outgoing scene. They live on with the
    /// incoming scene, and Unity logs one Error naming them. Explicit parents and scenes and an
    /// object's own scene stay authoritative. A scene a handler creates is unloaded by the same
    /// load. Unit tests have no native side: SceneManagerInternal.UnloadForSingleLoad is the
    /// production teardown, and the incoming scene is left as LoadScene leaves it.
    /// </summary>
    public class SceneCreationOwnershipTests
    {
        /// <summary>An object's scene and whether the public queries find it there.</summary>
        internal static string Describe(GameObject go)
        {
            var scene = go.scene;
            var byType = UObject.FindObjectsByType(typeof(TeardownMade), FindObjectsInactive.Include, FindObjectsSortMode.None);
            var tracker = go.GetComponent<TeardownMade>();
            return $"{go.name}: scene {scene.name} loaded {scene.isLoaded} root {go.transform.parent == null}"
                + $" inRoots {Array.IndexOf(scene.GetRootGameObjects(), go) >= 0} find {GameObject.Find(go.name) == go}"
                + $" byType {tracker != null && Array.IndexOf(byType, tracker) >= 0} active {SceneManager.GetActiveScene().name}";
        }

        static GameObject Tracked(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<TeardownMade>();
            return go;
        }

        static List<string> Errors(List<(LogType Type, string Message)> log) =>
            log.Where(e => e.Type == LogType.Error).Select(e => e.Message).ToList();

        /// <summary>Destroys what a test made outside its scenes, then restores the registry.</summary>
        static void Cleanup(LightProbesTests.Scenes scenes, IEnumerable<GameObject> made)
        {
            try
            {
                foreach (var go in made)
                    if (go != null) UObject.DestroyImmediate(go);
            }
            finally
            {
                LightmapSettings.Reset();
                scenes.Dispose();
            }
        }

        [Fact]
        public void SingleLoadTeardownPutsDefaultCreationsInTheIncomingScene()
        {
            var scenes = new LightProbesTests.Scenes();
            var made = new List<GameObject>();
            var events = new List<string>();
            var log = new List<(LogType, string)>();
            bool once = false;
            Application.LogCallback capture = (message, stack, type) =>
            {
                log.Add((type, message));
                if (type == LogType.Error) events.Add("Error");
            };
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                if (once) return;
                once = true;
                events.Add($"sceneUnloaded {s.name}: active {SceneManager.GetActiveScene().name}");
                var created = Tracked("CreatedNew");
                made.Add(created);
                var copy = UObject.Instantiate(made[0]);
                made.Add(copy);
                var child = Tracked("CreatedChild");
                made.Add(child);
                child.transform.SetParent(copy.transform);
                events.Add("parented " + Describe(child));
                child.transform.SetParent(null);
                foreach (var go in new[] { created, copy, child }) events.Add("in sceneUnloaded " + Describe(go));
            };
            try
            {
                LightmapSettings.Reset();
                var main = scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                // The template a handler copies lives outside the scenes the load closes.
                var template = Tracked("CreationTiny");
                made.Add(template);
                SceneManagerInternal.MoveToDontDestroyOnLoad(template);
                var destroyer = Tracked("Destroyer");
                destroyer.GetComponent<TeardownMade>().onDestroy = () =>
                {
                    var go = Tracked("CreatedInOnDestroy");
                    made.Add(go);
                    events.Add("in OnDestroy " + Describe(go));
                };
                TeardownMade.Awoke.Clear();
                Application.logMessageReceived += capture;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);

                var copyName = "CreationTiny(Clone)";
                Assert.Equal(new[]
                {
                    "in OnDestroy CreatedInOnDestroy: scene Extra loaded False root True inRoots True find True byType True active Main",
                    "sceneUnloaded Main: active Main",
                    "parented CreatedChild: scene Extra loaded False root False inRoots False find True byType True active Main",
                    "in sceneUnloaded CreatedNew: scene Extra loaded False root True inRoots True find True byType True active Main",
                    $"in sceneUnloaded {copyName}: scene Extra loaded False root True inRoots True find True byType True active Main",
                    "in sceneUnloaded CreatedChild: scene Extra loaded False root True inRoots True find True byType True active Main",
                    "Error",
                }, events);
                // Each was already in the incoming scene when it woke up.
                Assert.Equal(new[] { "CreatedInOnDestroy", "CreatedNew", copyName, "CreatedChild" },
                    TeardownMade.Awoke.Select(a => a.Substring(0, a.IndexOf(':'))));
                Assert.All(TeardownMade.Awoke, a => Assert.Contains(": scene Extra loaded False", a));

                // They are not destroyed: they live on in the incoming scene, and the Error names
                // every one of them (Unity's name order varies; this is the root order).
                Assert.Equal(new[] { "CreatedInOnDestroy", "CreatedNew", copyName, "CreatedChild" },
                    incoming.Roots.Select(r => r.name));
                Assert.All(incoming.Roots, r => Assert.False(r.m_Destroyed));
                Assert.Equal(new[] { SceneManagerInternal.NotCleanedUpHeader + $"CreatedInOnDestroy\nCreatedNew\n{copyName}\nCreatedChild\n" },
                    Errors(log));
                Assert.False(new Scene(main.Handle).IsValid());

                // The teardown is over: the incoming scene is neither the default nor found.
                Assert.Null(SceneRegistry.CreationTarget);
                Assert.False(SceneRegistry.IsTeardownIncoming(incoming));
                Assert.Null(GameObject.Find("CreatedNew"));
            }
            finally
            {
                Application.logMessageReceived -= capture;
                SceneManager.sceneUnloaded -= unloaded;
                Cleanup(scenes, made);
            }
        }

        /// <summary>Explicit parents and scenes, and an object's own scene, are kept during the
        /// teardown: only objects without a scene of their own go to the incoming scene, and the
        /// Error names only the incoming scene's new roots. The objects made in the handler's scene
        /// go with it when the same load unloads it.</summary>
        [Fact]
        public void SingleLoadTeardownKeepsExplicitScenesParentsAndOwnScenes()
        {
            var scenes = new LightProbesTests.Scenes();
            var made = new List<GameObject>();
            var seen = new List<string>();
            var log = new List<(LogType, string)>();
            Scene other = default;
            bool once = false;
            GameObject template = null, keptParent = null, keptChild = null;
            Application.LogCallback capture = (message, stack, type) => log.Add((type, message));
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                if (once) return;
                once = true;
                other = SceneManager.CreateScene("Other");
                var inOther = UObject.Instantiate(template, new InstantiateParameters { scene = other });
                var holder = new GameObject("Holder");
                SceneManager.MoveGameObjectToScene(holder, other);
                var underHolder = UObject.Instantiate(template, holder.transform);
                var moved = new GameObject("Moved");
                SceneManager.MoveGameObjectToScene(moved, other);
                keptChild.transform.SetParent(null);
                var plain = new GameObject("Plain");
                made.AddRange(new[] { inOther, holder, underHolder, moved, plain });
                foreach (var go in new[] { inOther, holder, underHolder, moved, keptChild, plain })
                    seen.Add($"{go.name} {go.scene.name}");
            };
            try
            {
                LightmapSettings.Reset();
                scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                template = new GameObject("Template");
                keptParent = new GameObject("KeptParent");
                keptChild = new GameObject("KeptChild");
                keptChild.transform.SetParent(keptParent.transform);
                made.AddRange(new[] { template, keptParent, keptChild });
                SceneManagerInternal.MoveToDontDestroyOnLoad(template);
                SceneManagerInternal.MoveToDontDestroyOnLoad(keptParent);
                Application.logMessageReceived += capture;
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);

                Assert.Equal(new[]
                {
                    "Template(Clone) Other", "Holder Other", "Template(Clone) Other", "Moved Other",
                    "KeptChild DontDestroyOnLoad", "Plain Extra",
                }, seen);
                Assert.Equal(new[] { "Plain" }, incoming.Roots.Select(r => r.name));
                Assert.Equal(new[] { SceneManagerInternal.NotCleanedUpHeader + "Plain\n" },
                    Errors(log));
                // The same load unloaded the handler's scene with the objects placed in it.
                Assert.False(other.IsValid());
                Assert.All(made.Take(4), go => Assert.True(go == null));
                Assert.Null(SceneRegistry.CreationTarget);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                SceneManager.sceneUnloaded -= unloaded;
                if (other.Data != null) SceneRegistry.Forget(other.Data);
                Cleanup(scenes, made);
            }
        }

        /// <summary>A handler that creates a scene on every sceneUnloaded never lets the teardown
        /// finish: Unbound stops at its resource bound with a clear exception that lists what is
        /// still loaded (nothing is dropped), and the default creation scene is restored. The bound
        /// is an implementation limit, not Unity behaviour (Unity is not measured for this).</summary>
        [Fact]
        public void SingleLoadTeardownStopsLoudlyAtItsSceneLimit()
        {
            var scenes = new LightProbesTests.Scenes();
            int unloads = 0, created = 0;
            Scene last = default;
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                unloads++;
                last = SceneManager.CreateScene("Again" + created++);
            };
            try
            {
                LightmapSettings.Reset();
                scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                var error = Assert.Throws<InvalidOperationException>(
                    () => SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true));

                int limit = 1 + SceneManagerInternal.TeardownCreatedSceneLimit;
                Assert.Equal(limit, unloads);
                Assert.Contains("keep creating scenes", error.Message);
                Assert.Contains("not Unity's behaviour", error.Message);
                Assert.Contains($"'{last.name}'", error.Message);
                // The scene the last handler made is still loaded and listed, not dropped.
                Assert.Equal(new[] { last.Data }, SceneRegistry.Loaded);
                Assert.True(last.isLoaded);
                Assert.Null(SceneRegistry.CreationTarget);
                Assert.False(SceneRegistry.IsTeardownIncoming(incoming));
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                if (last.Data != null) SceneRegistry.Forget(last.Data);
                scenes.Dispose();
                LightmapSettings.Reset();
            }
        }

        /// <summary>A throwing callback propagates (as before), and the teardown's default creation
        /// scene does not outlive it: the object made before the throw stays in the incoming
        /// scene, and creation afterwards uses the active scene again.</summary>
        [Fact]
        public void SingleLoadTeardownRestoresTheCreationSceneAfterAThrow()
        {
            var scenes = new LightProbesTests.Scenes();
            var made = new List<GameObject>();
            SceneData incoming = null;
            string during = null;
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                made.Add(new GameObject("BeforeThrow"));
                during = SceneRegistry.DefaultCreationScene().Name;
                throw new InvalidOperationException("handler");
            };
            try
            {
                LightmapSettings.Reset();
                scenes.Loaded("Main");
                incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                Assert.Throws<InvalidOperationException>(() => SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true));
                SceneManager.sceneUnloaded -= unloaded;

                Assert.Equal("Extra", during);
                Assert.Equal("Extra", made[0].scene.name);
                Assert.Null(SceneRegistry.CreationTarget);
                Assert.False(SceneRegistry.IsTeardownIncoming(incoming));
                var after = scenes.Loaded("After");
                var go = new GameObject("AfterThrow");
                made.Add(go);
                Assert.Same(after, go.scene.Data);
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                Cleanup(scenes, made);
            }
        }

        /// <summary>A scene a handler makes active stays active through that handler and its own
        /// sceneUnloaded (SingleLoadKeepsAnActiveSceneAHandlerChose), while GetActiveScene answers
        /// it there. Objects made without a scene after that choice still go to the incoming scene:
        /// this is Unbound's rule (the incoming scene is the teardown's creation target); Unity is
        /// not measured for a creation after a handler's SetActiveScene.</summary>
        [Fact]
        public void SingleLoadTeardownRespectsAHandlerChosenActiveScene()
        {
            var scenes = new LightProbesTests.Scenes();
            var made = new List<GameObject>();
            var seen = new List<string>();
            Scene chosen = default;
            bool once = false;
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                if (!once)
                {
                    once = true;
                    chosen = SceneManager.CreateScene("Chosen");
                    SceneManager.SetActiveScene(chosen);
                    var go = new GameObject("AfterChoice");
                    made.Add(go);
                    seen.Add($"{s.name}: active {SceneManager.GetActiveScene().name} made in {go.scene.name}");
                    return;
                }
                seen.Add($"{s.name}: active {SceneManager.GetActiveScene().name}");
            };
            try
            {
                LightmapSettings.Reset();
                scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: true);
                Assert.Equal(new[] { "Main: active Chosen made in Extra", "Chosen: active Chosen" }, seen);
                Assert.False(chosen.IsValid());
                Assert.Null(SceneRegistry.Active);
                Assert.Equal(new[] { "AfterChoice" }, incoming.Roots.Select(r => r.name));
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                if (chosen.Data != null) SceneRegistry.Forget(chosen.Data);
                Cleanup(scenes, made);
            }
        }

        /// <summary>Single loads that are not SceneManager loads keep the active scene as the
        /// default creation scene and close only the scenes loaded when they start (not measured;
        /// this pins the boundary of the measured change).</summary>
        [Fact]
        public void OtherSingleLoadsKeepTheActiveSceneForCreation()
        {
            var scenes = new LightProbesTests.Scenes();
            var made = new List<GameObject>();
            string seen = null;
            UnityEngine.Events.UnityAction<Scene> unloaded = s =>
            {
                seen = $"target {SceneRegistry.CreationTarget?.Name ?? "none"}";
            };
            try
            {
                LightmapSettings.Reset();
                scenes.Loaded("Main");
                var incoming = scenes.Loading("Extra");
                SceneManager.sceneUnloaded += unloaded;
                SceneManagerInternal.UnloadForSingleLoad(incoming, sceneManagerLoad: false);
                Assert.Equal("target none", seen);
                Assert.Empty(incoming.Roots);
            }
            finally
            {
                SceneManager.sceneUnloaded -= unloaded;
                Cleanup(scenes, made);
            }
        }
    }
}
