using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    internal readonly struct SceneHandle : IEquatable<SceneHandle>
    {
        internal readonly int Value;
        internal SceneHandle(int value) { Value = value; }
        public bool Equals(SceneHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SceneHandle h && h.Value == Value;
        public override int GetHashCode() => Value;
        public static bool operator ==(SceneHandle a, SceneHandle b) => a.Value == b.Value;
        public static bool operator !=(SceneHandle a, SceneHandle b) => a.Value != b.Value;
    }

    internal sealed class SceneData
    {
        internal SceneHandle Handle;
        internal string Name = "";
        internal string Path = "";
        /// <summary>What the scene's documents load by when not its path (an AssetBundle's scene).</summary>
        internal string LoadKey;
        internal int BuildIndex = -1;
        internal bool IsLoaded;
        internal bool IsDirty;
        internal Scene.LoadingState LoadingState;
        internal readonly List<GameObject> Roots = new List<GameObject>();
        /// <summary>The file ids of the objects the scene file made (GlobalObjectId names scene
        /// objects by them).</summary>
        internal readonly Dictionary<Object, long> FileIds = new Dictionary<Object, long>();
        /// <summary>The scene's Lighting Settings (its LightmapSettings' m_LightingSettings, or
        /// what UnityEditor.Lightmapping assigned since); null: none.</summary>
        internal LightingSettings LightingSettings;
    }

    /// <summary>The loaded scenes and their root GameObjects, in hierarchy order.</summary>
    internal static class SceneRegistry
    {
        static readonly Dictionary<int, SceneData> s_Scenes = new Dictionary<int, SceneData>();
        /// <summary>Loaded scenes in load order (what SceneManager.GetSceneAt indexes).</summary>
        internal static readonly List<SceneData> Loaded = new List<SceneData>();
        static int s_NextHandle = 2;
        static SceneData s_Active;

        /// <summary>The active scene: its RenderSettings are the ones in effect.</summary>
        internal static SceneData Active
        {
            get => s_Active;
            set
            {
                if (s_Active == value) return;
                s_Active = value;
                RenderSettings.ActiveSceneChanged(value);
            }
        }
        static SceneData s_DontDestroyOnLoad;

        internal static SceneData Create(string name, string path, int buildIndex)
        {
            var scene = new SceneData
            {
                Handle = new SceneHandle(s_NextHandle),
                Name = name,
                Path = path,
                BuildIndex = buildIndex,
            };
            s_NextHandle += 2;
            s_Scenes[scene.Handle.Value] = scene;
            return scene;
        }

        /// <summary>The scene file and file id an object was loaded from.</summary>
        internal static bool TryGetFileId(Object obj, out string scenePath, out long fileId)
        {
            foreach (var scene in Loaded)
            {
                if (scene.FileIds.TryGetValue(obj, out fileId))
                {
                    scenePath = scene.Path;
                    return true;
                }
            }
            scenePath = null;
            fileId = 0;
            return false;
        }

        internal static Object FindByFileId(string scenePath, long fileId)
        {
            foreach (var scene in Loaded)
            {
                if (!string.Equals(scene.Path, scenePath, StringComparison.Ordinal)) continue;
                foreach (var pair in scene.FileIds)
                    if (pair.Value == fileId && pair.Key != null) return pair.Key;
            }
            return null;
        }

        internal static SceneData Get(SceneHandle handle) =>
            s_Scenes.TryGetValue(handle.Value, out var scene) ? scene : null;

        internal static void Forget(SceneData scene)
        {
            s_Scenes.Remove(scene.Handle.Value);
            Loaded.Remove(scene);
            LightProbes.SceneForgotten(scene.Handle.Value);
            // Also for scenes closed without an unload (leaving Play mode).
            SceneNavMesh.SceneUnloaded(scene.Handle.Value);
        }

        internal static bool IsDontDestroyOnLoad(SceneData scene) => scene != null && scene == s_DontDestroyOnLoad;

        static SceneData s_Detached;

        /// <summary>Objects in no scene: roots with HideFlags.DontSave a Single load kept (their
        /// scene is invalid, measured: tests/projects/messages). The game sees no scene; the
        /// engine still finds and updates them.</summary>
        internal static SceneData Detached
        {
            get
            {
                if (s_Detached == null)
                {
                    s_Detached = new SceneData { Handle = new SceneHandle(s_NextHandle), IsLoaded = true, LoadingState = Scene.LoadingState.Loaded };
                    s_NextHandle += 2;
                }
                return s_Detached;
            }
        }

        /// <summary>A scene by handle, the detached one included.</summary>
        static SceneData Holder(SceneHandle handle) =>
            Get(handle) ?? (s_Detached != null && handle == s_Detached.Handle ? s_Detached : null);

        /// <summary>The scene of objects that survive scene loads; created on first use.</summary>
        internal static SceneData DontDestroyOnLoad
        {
            get
            {
                if (s_DontDestroyOnLoad == null)
                {
                    s_DontDestroyOnLoad = Create("DontDestroyOnLoad", "", -1);
                    s_DontDestroyOnLoad.IsLoaded = true;
                    s_DontDestroyOnLoad.LoadingState = Scene.LoadingState.Loaded;
                }
                return s_DontDestroyOnLoad;
            }
        }

        /// <summary>Back to Edit mode: no scene is loaded but a new untitled one.</summary>
        internal static void ResetToUntitled()
        {
            foreach (var scene in Loaded.ToArray()) Forget(scene);
            if (s_DontDestroyOnLoad != null) s_DontDestroyOnLoad.Roots.Clear();
            s_Detached?.Roots.Clear();
            Active = null;
            EnsureActive();
        }

        /// <summary>Before any scene is loaded, objects go to an untitled scene, like the Editor's.</summary>
        internal static SceneData EnsureActive()
        {
            if (Active == null)
            {
                Active = Create("", "", -1);
                Active.IsLoaded = true;
                Active.LoadingState = Scene.LoadingState.Loaded;
                Loaded.Add(Active);
            }
            return Active;
        }

        /// <summary>A SceneManager Single load closing the scenes it replaces: its incoming scene,
        /// created but not loaded yet, and the load it is nested in (an Editor load a callback
        /// started), if any. The public scene queries read the rest (SceneManagerInternal.Listed):
        /// the scenes loaded when it began, the scene it is closing and the scenes it has closed.
        /// The loaded list itself (what loading, lighting and Find use) is not changed by it.</summary>
        internal sealed class SingleLoadTeardown
        {
            internal SceneData Incoming;
            internal SingleLoadTeardown Outer;
            /// <summary>The scenes loaded when the teardown began, in load order.</summary>
            internal SceneData[] Outgoing;
            /// <summary>The scene whose objects are being destroyed or whose sceneUnloaded runs.</summary>
            internal SceneData Closing;
            /// <summary>The scenes it closed, in that order: released from the registry, but still
            /// valid (not loaded) for the public scene queries until it ends.</summary>
            internal readonly List<SceneData> Closed = new List<SceneData>();
        }

        static SingleLoadTeardown s_Teardown;

        /// <summary>The innermost Single load teardown in effect, or null.</summary>
        internal static SingleLoadTeardown Teardown => s_Teardown;

        /// <summary>
        /// Where objects made without a scene of their own go while a SceneManager Single load
        /// closes the scenes it replaces: the incoming scene, although GetActiveScene still answers
        /// the outgoing one. Roots made in an outgoing object's OnDestroy and in sceneUnloaded go
        /// there before they wake up, are found there during those callbacks and live with it
        /// (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl). Null at every other time.
        /// </summary>
        internal static SceneData CreationTarget => s_Teardown?.Incoming;

        /// <summary>Starts a Single load's teardown; pass the result to <see cref="EndSingleLoadTeardown"/>
        /// in a finally block.</summary>
        internal static SingleLoadTeardown BeginSingleLoadTeardown(SceneData incoming)
        {
            var outer = s_Teardown;
            s_Teardown = new SingleLoadTeardown
            {
                Incoming = incoming,
                Outer = outer,
                Outgoing = Loaded.FindAll(s => s != incoming).ToArray(),
            };
            return outer;
        }

        /// <summary>Ends the innermost teardown (also when it threw): the scenes it closed are
        /// invalid for the public queries too from now on, before the incoming scene is activated.</summary>
        internal static void EndSingleLoadTeardown(SingleLoadTeardown outer) => s_Teardown = outer;

        /// <summary>The incoming scene of a Single load that is closing its outgoing scenes: not
        /// loaded yet, but its objects can be found and its roots listed.</summary>
        internal static bool IsTeardownIncoming(SceneData scene)
        {
            for (var t = s_Teardown; t != null; t = t.Outer)
                if (t.Incoming == scene) return true;
            return false;
        }

        /// <summary>The scene a Single load is closing: no longer loaded for the public queries
        /// while its objects are destroyed (measured, Unity 6000.0.71f1: tests/projects/lightprobes
        /// Reference creation-ownership.jsonl, the outgoing scene in its objects' OnDisable and
        /// OnDestroy). Its internal state, and with it loading, Find and SetActiveScene, is unchanged.</summary>
        internal static bool IsTeardownClosing(SceneData scene)
        {
            for (var t = s_Teardown; t != null; t = t.Outer)
                if (t.Closing == scene) return true;
            return false;
        }

        /// <summary>
        /// A scene a SceneManager Single load has closed, after its sceneUnloaded: it is forgotten
        /// as before (<see cref="Forget"/>: the registry, loaded list and light probe lookup let it
        /// go, so engine code and explicit-scene creation treat it as gone), but the public scene
        /// queries still answer it as a valid, not loaded, listed scene until the load's teardown
        /// ends (<see cref="PublicScene"/>): a scene a handler made and the same load closes later
        /// still sees it (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl, sceneUnloaded of Made).
        /// </summary>
        internal static void CloseForSingleLoad(SceneData scene)
        {
            Forget(scene);
            s_Teardown?.Closed.Add(scene);
        }

        /// <summary>The scene the public Scene queries (IsValid, name, path, buildIndex, rootCount,
        /// GetRootGameObjects) answer for a handle: a registered scene, else one a Single load in
        /// progress has closed. Engine code uses <see cref="Get"/>, which does not see the latter.</summary>
        internal static SceneData PublicScene(SceneHandle handle)
        {
            var scene = Get(handle);
            if (scene != null) return scene;
            for (var t = s_Teardown; t != null; t = t.Outer)
                foreach (var closed in t.Closed)
                    if (closed.Handle == handle) return closed;
            return null;
        }

        /// <summary>The scene an object made without a parent or scene of its own goes to: the
        /// incoming scene during a Single load's teardown, else the active scene (an untitled
        /// one before any scene is loaded).</summary>
        internal static SceneData DefaultCreationScene() => CreationTarget ?? EnsureActive();

        internal static void AddRoot(GameObject go)
        {
            // An object's own scene stays authoritative; only one without a scene gets the default.
            var scene = Holder(go.m_Scene) ?? DefaultCreationScene();
            go.m_Scene = scene.Handle;
            if (!scene.Roots.Contains(go)) scene.Roots.Add(go);
        }

        internal static void RemoveRoot(GameObject go)
        {
            Holder(go.m_Scene)?.Roots.Remove(go);
        }

        internal static void MoveToScene(GameObject go, SceneData scene)
        {
            RemoveRoot(go);
            SetSceneRecursive(go.m_Transform, scene.Handle);
            scene.Roots.Add(go);
        }

        internal static void SetSceneRecursive(Transform t, SceneHandle handle)
        {
            t.m_GameObject.m_Scene = handle;
            foreach (var child in t.m_Children) SetSceneRecursive(child, handle);
        }

        /// <summary>Every GameObject of the loaded scenes, depth first in hierarchy order.</summary>
        internal static IEnumerable<GameObject> AllGameObjects(bool includeInactive)
        {
            var scenes = new List<SceneData>(Loaded);
            // The incoming scene of a Single load closing its outgoing scenes is not listed yet,
            // but the objects made there meanwhile are found (measured: CreationTarget).
            for (var t = s_Teardown; t != null; t = t.Outer)
                if (!scenes.Contains(t.Incoming)) scenes.Add(t.Incoming);
            if (s_DontDestroyOnLoad != null) scenes.Add(s_DontDestroyOnLoad);
            if (s_Detached != null) scenes.Add(s_Detached);
            var stack = new Stack<Transform>();
            foreach (var scene in scenes)
            {
                var roots = scene.Roots.ToArray();
                foreach (var root in roots)
                {
                    if (root.m_Destroyed) continue;
                    stack.Push(root.m_Transform);
                    while (stack.Count > 0)
                    {
                        var t = stack.Pop();
                        var go = t.m_GameObject;
                        if (go.m_Destroyed) continue;
                        if (!includeInactive && (!go.m_IsActive || go.m_AwaitingSceneAwake)) continue;
                        yield return go;
                        for (int i = t.m_Children.Count - 1; i >= 0; i--)
                            stack.Push(t.m_Children[i]);
                    }
                }
            }
        }

        /// <summary>GameObject.Find: a name, or a path of names; a leading '/' anchors it at a root.
        /// Only active objects are found.</summary>
        internal static GameObject FindByPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Contains("/"))
            {
                bool rooted = name.StartsWith("/");
                var parts = name.Trim('/').Split('/');
                foreach (var go in AllGameObjects(includeInactive: false))
                {
                    if (rooted && go.m_Transform.m_Parent != null) continue;
                    if (go.m_Name != parts[0]) continue;
                    var found = go.m_Transform.Find(string.Join("/", parts, 1, parts.Length - 1));
                    if (found != null && found.m_GameObject.activeInHierarchy) return found.m_GameObject;
                }
                return null;
            }
            foreach (var go in AllGameObjects(includeInactive: false))
                if (go.m_Name == name) return go;
            return null;
        }
    }
}
