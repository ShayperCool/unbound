using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEngine.Events;

namespace UnityEngine.SceneManagement
{
    [Serializable]
    public partial struct Scene : IEquatable<Scene>
    {
        internal int m_Handle;

        internal Scene(SceneHandle handle) { m_Handle = handle.Value; }

        internal SceneData Data => SceneRegistry.Get(new SceneHandle(m_Handle));

        public int handle => m_Handle;
        public bool IsValid() => Data != null;
        public string path => Data?.Path ?? "";
        public string name
        {
            get => Data?.Name ?? "";
            set { if (Data != null) Data.Name = value; }
        }
        public bool isLoaded => Data?.IsLoaded ?? false;
        public int buildIndex => Data?.BuildIndex ?? -1;
        public bool isDirty => Data?.IsDirty ?? false;
        public int dirtyID => 0;
        public int rootCount => Data?.Roots.Count ?? 0;
        public bool isSubScene { get; set; }

        public LoadingState loadingState => Data?.LoadingState ?? LoadingState.NotLoaded;

        public GameObject[] GetRootGameObjects()
        {
            var list = new List<GameObject>();
            GetRootGameObjects(list);
            return list.ToArray();
        }

        public void GetRootGameObjects(List<GameObject> rootGameObjects)
        {
            if (rootGameObjects == null) throw new ArgumentNullException(nameof(rootGameObjects));
            if (!IsValid()) throw new ArgumentException("The scene is invalid.");
            if (!isLoaded) throw new ArgumentException("The scene is not loaded.");
            rootGameObjects.Clear();
            foreach (var go in Data.Roots)
                if (!go.m_Destroyed) rootGameObjects.Add(go);
        }

        public static bool operator ==(Scene lhs, Scene rhs) => lhs.m_Handle == rhs.m_Handle;
        public static bool operator !=(Scene lhs, Scene rhs) => lhs.m_Handle != rhs.m_Handle;
        public override int GetHashCode() => m_Handle;
        public override bool Equals(object other) => other is Scene s && s.m_Handle == m_Handle;
        bool IEquatable<Scene>.Equals(Scene other) => other.m_Handle == m_Handle;

        public enum LoadingState
        {
            NotLoaded = 0,
            Loading = 1,
            Loaded = 2,
            Unloading = 3,
        }
    }




    [Serializable]
    public partial struct LoadSceneParameters
    {
        [SerializeField] LoadSceneMode m_LoadSceneMode;
        [SerializeField] LocalPhysicsMode m_LocalPhysicsMode;

        public LoadSceneMode loadSceneMode { get => m_LoadSceneMode; set => m_LoadSceneMode = value; }
        public LocalPhysicsMode localPhysicsMode { get => m_LocalPhysicsMode; set => m_LocalPhysicsMode = value; }

        public LoadSceneParameters(LoadSceneMode mode)
        {
            m_LoadSceneMode = mode;
            m_LocalPhysicsMode = LocalPhysicsMode.None;
        }

        public LoadSceneParameters(LoadSceneMode mode, LocalPhysicsMode physicsMode)
        {
            m_LoadSceneMode = mode;
            m_LocalPhysicsMode = physicsMode;
        }
    }

    [Serializable]
    public partial struct CreateSceneParameters
    {
        [SerializeField] LocalPhysicsMode m_LocalPhysicsMode;
        public LocalPhysicsMode localPhysicsMode { get => m_LocalPhysicsMode; set => m_LocalPhysicsMode = value; }
        public CreateSceneParameters(LocalPhysicsMode physicsMode) { m_LocalPhysicsMode = physicsMode; }
    }

    public partial class SceneManager
    {
        public static event UnityAction<Scene, LoadSceneMode> sceneLoaded;
        public static event UnityAction<Scene> sceneUnloaded;
        public static event UnityAction<Scene, Scene> activeSceneChanged;

        internal static void Internal_SceneLoaded(Scene scene, LoadSceneMode mode) => sceneLoaded?.Invoke(scene, mode);
        internal static void Internal_SceneUnloaded(Scene scene) => sceneUnloaded?.Invoke(scene);
        internal static void Internal_ActiveSceneChanged(Scene previous, Scene next) => activeSceneChanged?.Invoke(previous, next);

        public static int sceneCount => SceneRegistry.Loaded.Count;
        public static int loadedSceneCount
        {
            get
            {
                int n = 0;
                foreach (var s in SceneRegistry.Loaded) if (s.IsLoaded) n++;
                return n;
            }
        }

        public static int sceneCountInBuildSettings => ProjectInfo.BuildScenes.Count;

        [Obsolete("Use SceneManager.sceneCount and SceneManager.GetSceneAt(int index) to loop the all scenes instead.")]
        public static Scene[] GetAllScenes()
        {
            var list = new Scene[sceneCount];
            for (int i = 0; i < list.Length; i++) list[i] = GetSceneAt(i);
            return list;
        }

        internal static SceneHandle GetActiveSceneHandle() => SceneRegistry.EnsureActive().Handle;

        public static Scene GetActiveScene() => new Scene(SceneRegistry.EnsureActive().Handle);

        public static bool SetActiveScene(Scene scene)
        {
            var data = scene.Data;
            if (data == null || !data.IsLoaded)
                throw new ArgumentException("SceneManager.SetActiveScene failed; scene is not loaded and therefore cannot be set active");
            var previous = GetActiveScene();
            SceneRegistry.Active = data;
            if (previous != scene) Internal_ActiveSceneChanged(previous, scene);
            return true;
        }

        public static Scene GetSceneAt(int index)
        {
            if (index < 0 || index >= SceneRegistry.Loaded.Count)
                throw new ArgumentException("The scene index is out of range.");
            return new Scene(SceneRegistry.Loaded[index].Handle);
        }

        public static Scene GetSceneByName(string name)
        {
            foreach (var s in SceneRegistry.Loaded)
                if (s.Name == name || s.Path == name) return new Scene(s.Handle);
            return default;
        }

        public static Scene GetSceneByPath(string scenePath)
        {
            foreach (var s in SceneRegistry.Loaded)
                if (string.Equals(s.Path, scenePath, StringComparison.OrdinalIgnoreCase)) return new Scene(s.Handle);
            return default;
        }

        public static Scene GetSceneByBuildIndex(int buildIndex)
        {
            foreach (var s in SceneRegistry.Loaded)
                if (s.BuildIndex == buildIndex) return new Scene(s.Handle);
            return default;
        }

        public static Scene CreateScene(string sceneName) => CreateScene(sceneName, default);

        public static Scene CreateScene(string sceneName, CreateSceneParameters parameters)
        {
            foreach (var s in SceneRegistry.Loaded)
                if (s.Name == sceneName)
                    throw new ArgumentException($"A scene named '{sceneName}' already exists.");
            var data = SceneRegistry.Create(sceneName, "", -1);
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loaded;
            SceneRegistry.Loaded.Add(data);
            return new Scene(data.Handle);
        }

        public static void LoadScene(string sceneName, [Internal.DefaultValue("LoadSceneMode.Single")] LoadSceneMode mode) =>
            LoadScene(sceneName, new LoadSceneParameters(mode));

        public static void LoadScene(string sceneName) => LoadScene(sceneName, new LoadSceneParameters(LoadSceneMode.Single));

        public static Scene LoadScene(string sceneName, LoadSceneParameters parameters)
        {
            int index = SceneManagerInternal.FindBuildScene(sceneName);
            if (index < 0)
            {
                Debug.LogError($"Scene '{sceneName}' couldn't be loaded because it has not been added to the build settings or the AssetBundle has not been loaded.\nTo add a scene to the build settings use the menu File->Build Settings...");
                return default;
            }
            return SceneManagerInternal.Load(index, parameters, async: false).Scene;
        }

        public static void LoadScene(int sceneBuildIndex, [Internal.DefaultValue("LoadSceneMode.Single")] LoadSceneMode mode) =>
            LoadScene(sceneBuildIndex, new LoadSceneParameters(mode));

        public static void LoadScene(int sceneBuildIndex) => LoadScene(sceneBuildIndex, new LoadSceneParameters(LoadSceneMode.Single));

        public static Scene LoadScene(int sceneBuildIndex, LoadSceneParameters parameters)
        {
            if (sceneBuildIndex < 0 || sceneBuildIndex >= ProjectInfo.BuildScenes.Count)
            {
                Debug.LogError($"Scene with build index: {sceneBuildIndex} couldn't be loaded because it has not been added to the build settings.");
                return default;
            }
            return SceneManagerInternal.Load(sceneBuildIndex, parameters, async: false).Scene;
        }

        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, [Internal.DefaultValue("LoadSceneMode.Single")] LoadSceneMode mode) =>
            LoadSceneAsync(sceneBuildIndex, new LoadSceneParameters(mode));

        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex) => LoadSceneAsync(sceneBuildIndex, new LoadSceneParameters(LoadSceneMode.Single));

        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, LoadSceneParameters parameters)
        {
            if (sceneBuildIndex < 0 || sceneBuildIndex >= ProjectInfo.BuildScenes.Count)
            {
                Debug.LogError($"Scene with build index: {sceneBuildIndex} couldn't be loaded because it has not been added to the build settings.");
                return null;
            }
            return SceneManagerInternal.Load(sceneBuildIndex, parameters, async: true).Operation;
        }

        public static AsyncOperation LoadSceneAsync(string sceneName, [Internal.DefaultValue("LoadSceneMode.Single")] LoadSceneMode mode) =>
            LoadSceneAsync(sceneName, new LoadSceneParameters(mode));

        public static AsyncOperation LoadSceneAsync(string sceneName) => LoadSceneAsync(sceneName, new LoadSceneParameters(LoadSceneMode.Single));

        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
        {
            int index = SceneManagerInternal.FindBuildScene(sceneName);
            if (index < 0)
            {
                Debug.LogError($"Scene '{sceneName}' couldn't be loaded because it has not been added to the build settings or the AssetBundle has not been loaded.");
                return null;
            }
            return SceneManagerInternal.Load(index, parameters, async: true).Operation;
        }

        [Obsolete("Use SceneManager.UnloadSceneAsync. This function is not safe to use during triggers and under other circumstances. See Scripting reference for more details.")]
        public static bool UnloadScene(Scene scene) => SceneManagerInternal.Unload(scene.Data) != null;

        [Obsolete("Use SceneManager.UnloadSceneAsync. This function is not safe to use during triggers and under other circumstances. See Scripting reference for more details.")]
        public static bool UnloadScene(int sceneBuildIndex) => UnloadScene(GetSceneByBuildIndex(sceneBuildIndex));

        [Obsolete("Use SceneManager.UnloadSceneAsync. This function is not safe to use during triggers and under other circumstances. See Scripting reference for more details.")]
        public static bool UnloadScene(string sceneName) => UnloadScene(GetSceneByName(sceneName));

        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex) => SceneManagerInternal.Unload(GetSceneByBuildIndex(sceneBuildIndex).Data);
        public static AsyncOperation UnloadSceneAsync(string sceneName) => SceneManagerInternal.Unload(GetSceneByName(sceneName).Data);
        public static AsyncOperation UnloadSceneAsync(Scene scene) => SceneManagerInternal.Unload(scene.Data);
        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex, UnloadSceneOptions options) => UnloadSceneAsync(sceneBuildIndex);
        public static AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options) => UnloadSceneAsync(sceneName);
        public static AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options) => UnloadSceneAsync(scene);

        public static void MergeScenes(Scene sourceScene, Scene destinationScene)
        {
            var source = sourceScene.Data;
            var destination = destinationScene.Data;
            if (source == null || destination == null) throw new ArgumentException("Invalid scene.");
            foreach (var go in source.Roots.ToArray()) SceneRegistry.MoveToScene(go, destination);
            SceneManagerInternal.Unload(source);
        }

        public static void MoveGameObjectToScene(GameObject go, Scene scene)
        {
            if (go == null) throw new ArgumentException("The GameObject is null.");
            if (go.transform.parent != null) throw new ArgumentException("Gameobject is not a root in a scene");
            var data = scene.Data;
            if (data == null || !data.IsLoaded) throw new ArgumentException("The scene is invalid or not loaded.");
            SceneRegistry.MoveToScene(go, data);
        }

        public static void MoveGameObjectsToScene(Unity.Collections.NativeArray<int> instanceIDs, Scene scene)
        {
            foreach (var id in instanceIDs)
                if (ObjectRegistry.FindById(id) is GameObject go) MoveGameObjectToScene(go, scene);
        }

        internal static void MoveToDontDestroyOnLoad(GameObject go) => SceneRegistry.MoveToScene(go, SceneRegistry.DontDestroyOnLoad);
    }

    public static partial class SceneUtility
    {
        public static string GetScenePathByBuildIndex(int buildIndex) =>
            buildIndex >= 0 && buildIndex < ProjectInfo.BuildScenes.Count ? ProjectInfo.BuildScenes[buildIndex].Path : "";

        public static int GetBuildIndexByScenePath(string scenePath)
        {
            for (int i = 0; i < ProjectInfo.BuildScenes.Count; i++)
                if (string.Equals(ProjectInfo.BuildScenes[i].Path, scenePath, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>Scene loading. A load creates the scene, reads its objects through the native side
    /// (which resolves prefab instances), then wakes them; Single mode unloads the other scenes
    /// first. An async load completes on the next frame, honouring allowSceneActivation.</summary>
    internal static class SceneManagerInternal
    {
        internal static SceneData ActiveScene => SceneRegistry.Active;

        internal struct LoadResult
        {
            internal Scene Scene;
            internal AsyncOperation Operation;
        }

        static readonly List<(AsyncOperation Op, int Index, LoadSceneParameters Parameters, SceneData Scene)> s_Pending =
            new List<(AsyncOperation, int, LoadSceneParameters, SceneData)>();

        /// <summary>Build index of a scene given by name or path, as LoadScene accepts it.</summary>
        internal static int FindBuildScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return -1;
            for (int i = 0; i < ProjectInfo.BuildScenes.Count; i++)
            {
                var path = ProjectInfo.BuildScenes[i].Path;
                if (string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase)) return i;
                var withoutExtension = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - 6) : path;
                if (string.Equals(withoutExtension, sceneName, StringComparison.OrdinalIgnoreCase)) return i;
                if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase)) return i;
                if (withoutExtension.EndsWith("/" + sceneName, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        internal static LoadResult Load(int buildIndex, LoadSceneParameters parameters, bool async)
        {
            Unbound.Runtime.Trace.Scenes($"load scene #{buildIndex} ({parameters.loadSceneMode}{(async ? ", async" : "")})");
            var (path, _) = ProjectInfo.BuildScenes[buildIndex];
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            data.LoadingState = Scene.LoadingState.Loading;
            var op = new AsyncOperation();
            if (async)
            {
                s_Pending.Add((op, buildIndex, parameters, data));
                return new LoadResult { Scene = new Scene(data.Handle), Operation = op };
            }
            // LoadScene completes during the next frame in Unity too; the work is the same, done now
            // and woken at once, which is what games observe after the frame.
            s_Pending.Add((op, buildIndex, parameters, data));
            return new LoadResult { Scene = new Scene(data.Handle), Operation = op };
        }

        /// <summary>Runs the loads requested since the last frame (called early in the frame).</summary>
        internal static void ProcessPending()
        {
            if (s_Pending.Count == 0) return;
            var batch = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var (op, index, parameters, data) in batch)
            {
                if (!op.allowSceneActivation)
                {
                    op.m_Progress = 0.9f;
                    var captured = (op, index, parameters, data);
                    op.m_OnAllowActivation = () => s_Pending.Add(captured);
                    continue;
                }
                // As in Unity, an exception from a loaded object or a sceneLoaded handler is logged
                // and the load still completes.
                try { Perform(data, parameters); }
                catch (Exception e) { Debug.LogException(e); }
                Unbound.Runtime.Trace.Scenes($"loaded scene '{data.Name}'");
                op.Complete();
            }
        }

        static void Perform(SceneData data, LoadSceneParameters parameters)
        {
            if (parameters.loadSceneMode == LoadSceneMode.Single)
            {
                foreach (var other in SceneRegistry.Loaded.ToArray())
                    if (other != data) UnloadNow(other);
                Resources.UnloadUnusedAssetsInternal();
            }
            SceneRegistry.Loaded.Add(data);
            data.IsLoaded = true;
            var previousActive = SceneRegistry.Active;
            if (parameters.loadSceneMode == LoadSceneMode.Single || previousActive == null)
                SceneRegistry.Active = data;
            SceneLoader.LoadInto(data);
            data.LoadingState = Scene.LoadingState.Loaded;
            if (parameters.loadSceneMode == LoadSceneMode.Single) Time.LevelLoaded();
            SceneManager.Internal_SceneLoaded(new Scene(data.Handle), parameters.loadSceneMode);
            if (SceneRegistry.Active == data && previousActive != data)
                SceneManager.Internal_ActiveSceneChanged(previousActive != null ? new Scene(previousActive.Handle) : default, new Scene(data.Handle));
        }

        /// <summary>Loads a scene immediately: the first scene of a session.</summary>
        internal static Scene LoadFirst(int buildIndex)
        {
            var (path, _) = ProjectInfo.BuildScenes[buildIndex];
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single));
            return new Scene(data.Handle);
        }

        /// <summary>Loads a scene by path, also one outside the build settings (as the Editor can).</summary>
        internal static Scene LoadPath(string path)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single));
            return new Scene(data.Handle);
        }

        internal static AsyncOperation Unload(SceneData data)
        {
            Unbound.Runtime.Trace.Scenes($"unload scene '{data?.Name}'");
            if (data == null || !data.IsLoaded)
                throw new ArgumentException("Scene to unload is invalid");
            if (SceneRegistry.Loaded.Count <= 1)
            {
                Debug.LogWarning("Unloading the last loaded scene is not supported.");
                return null;
            }
            UnloadNow(data);
            if (SceneRegistry.Active == data || SceneRegistry.Active == null)
                SceneRegistry.Active = SceneRegistry.Loaded.Count > 0 ? SceneRegistry.Loaded[0] : null;
            return AsyncOperation.Completed();
        }

        static void UnloadNow(SceneData data)
        {
            data.LoadingState = Scene.LoadingState.Unloading;
            foreach (var root in data.Roots.ToArray())
                if (!root.m_Destroyed) Destruction.DestroyImmediate(root);
            data.IsLoaded = false;
            data.LoadingState = Scene.LoadingState.NotLoaded;
            SceneRegistry.Forget(data);
            if (SceneRegistry.Active == data) SceneRegistry.Active = null;
            SceneManager.Internal_SceneUnloaded(new Scene(data.Handle));
        }
    }
}

namespace Unity.Collections
{
    /// <summary>A minimal managed stand-in for Unity's native array, for the APIs that take one.</summary>
    public partial struct NativeArray<T> : IEnumerable<T>, IDisposable where T : struct
    {
        internal T[] m_Array;
        public NativeArray(T[] array, Allocator allocator) { m_Array = (T[])array.Clone(); }
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) { m_Array = new T[length]; }
        public int Length => m_Array?.Length ?? 0;
        public bool IsCreated => m_Array != null;
        public T this[int index] { get => m_Array[index]; set => m_Array[index] = value; }
        public void Dispose() { m_Array = null; }
        public T[] ToArray() => (T[])m_Array.Clone();
        public void CopyFrom(T[] array) => Array.Copy(array, m_Array, Math.Min(array.Length, m_Array.Length));
        public void CopyTo(T[] array) => Array.Copy(m_Array, array, Math.Min(array.Length, m_Array.Length));
        public Enumerator GetEnumerator() => new Enumerator(ref this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public partial struct Enumerator : IEnumerator<T>
        {
            NativeArray<T> m_Array;
            int m_Index;

            public Enumerator(ref NativeArray<T> array)
            {
                m_Array = array;
                m_Index = -1;
            }

            public void Dispose() { }
            public bool MoveNext() => ++m_Index < m_Array.Length;
            public void Reset() => m_Index = -1;
            public T Current => m_Array[m_Index];
            object System.Collections.IEnumerator.Current => Current;
        }
    }

    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
        AudioKernel = 5,
        Domain = 6,
        FirstUserIndex = 64,
    }

    public enum NativeArrayOptions
    {
        UninitializedMemory = 0,
        ClearMemory = 1,
    }
}
