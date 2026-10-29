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
        }

        internal static bool IsDontDestroyOnLoad(SceneData scene) => scene != null && scene == s_DontDestroyOnLoad;

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

        internal static void AddRoot(GameObject go)
        {
            var scene = Get(go.m_Scene) ?? EnsureActive();
            go.m_Scene = scene.Handle;
            if (!scene.Roots.Contains(go)) scene.Roots.Add(go);
        }

        internal static void RemoveRoot(GameObject go)
        {
            Get(go.m_Scene)?.Roots.Remove(go);
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
            if (s_DontDestroyOnLoad != null) scenes.Add(s_DontDestroyOnLoad);
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
                        if (!includeInactive && !go.m_IsActive) continue;
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
