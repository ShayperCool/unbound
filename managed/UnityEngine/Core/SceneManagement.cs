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

        /// <summary>What the public queries answer: <see cref="Data"/>, or a scene a Single load in
        /// progress has closed (SceneRegistry.PublicScene). Engine code keeps using Data.</summary>
        SceneData PublicData => SceneRegistry.PublicScene(new SceneHandle(m_Handle));

        public int handle => m_Handle;
        public bool IsValid() => PublicData != null;
        public string path
        {
            get
            {
                var data = PublicData;
                if (data == null) return null;
                // Unity's persistent scene has a virtual path; an ordinary runtime-created
                // scene keeps its empty path, even if its name happens to be the same.
                return SceneRegistry.IsDontDestroyOnLoad(data) ? "DontDestroyOnLoad" : data.Path;
            }
        }
        public string name
        {
            // An invalid scene has no name (measured: tests/projects/messages, null).
            get
            {
                MainThreadCheck.Check("GetNameInternal");
                return PublicData?.Name;
            }
            set
            {
                MainThreadCheck.Check("SetNameInternal");
                if (Data != null) Data.Name = value;
            }
        }
        /// <summary>Not yet while the scene's objects wake up and the scene is processed
        /// (measured: tests/projects/editorcallbacks), and no longer while a SceneManager Single
        /// load destroys its objects (SceneRegistry.IsTeardownClosing).</summary>
        public bool isLoaded => IsLoadedData(Data);

        internal static bool IsLoadedData(SceneData data) =>
            data != null && data.IsLoaded && data.LoadingState != LoadingState.Loading && !SceneRegistry.IsTeardownClosing(data);
        public int buildIndex => PublicData?.BuildIndex ?? -1;
        public bool isDirty => Data?.IsDirty ?? false;
        public int dirtyID => 0;
        public int rootCount => PublicData?.Roots.Count ?? 0;
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
            var data = PublicData;
            if (data == null) throw new ArgumentException("The scene is invalid.");
            // A loading scene lists its roots to the scene processors (measured: tests/projects/editorcallbacks).
            // So does every listed scene that is not loaded (SceneManagerInternal.Listed): a
            // requested scene (none yet), the incoming scene of a Single load while the outgoing
            // scenes close, with the objects made there meanwhile (SceneRegistry.CreationTarget),
            // and the scenes that load closes, in and after their sceneUnloaded (none left)
            // (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference creation-ownership.jsonl).
            // A valid scene that is neither loaded nor listed still throws (not measured).
            if (!data.IsLoaded && !SceneManagerInternal.IsListed(data)) throw new ArgumentException("The scene is not loaded.");
            rootGameObjects.Clear();
            foreach (var go in data.Roots)
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

        /// <summary>The listed scenes (SceneManagerInternal.Listed), requested and closing ones too.</summary>
        public static int sceneCount { get { MainThreadCheck.Check("get_sceneCount"); return SceneManagerInternal.Listed().Count; } }

        /// <summary>The listed scenes whose isLoaded is true: not a scene still loading or being
        /// closed (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// lifecycle.jsonl and creation-ownership.jsonl).</summary>
        public static int loadedSceneCount
        {
            get
            {
                int n = 0;
                foreach (var s in SceneManagerInternal.Listed()) if (Scene.IsLoadedData(s)) n++;
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

        public static Scene GetActiveScene()
        {
            MainThreadCheck.Check("GetActiveScene");
            return new Scene(SceneRegistry.EnsureActive().Handle);
        }

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
            var listed = SceneManagerInternal.Listed();
            if (index < 0 || index >= listed.Count)
                throw new ArgumentException("The scene index is out of range.");
            return new Scene(listed[index].Handle);
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
            SceneManagerInternal.CreateLocalPhysics(data, parameters.localPhysicsMode);
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
                if (AssetBundle.FindScene(sceneName, out var path, out var key))
                    return SceneManagerInternal.LoadBundled(path, key, parameters, async: false).Scene;
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
                if (AssetBundle.FindScene(sceneName, out var path, out var key))
                    return SceneManagerInternal.LoadBundled(path, key, parameters, async: true).Operation;
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

        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex) => SceneManagerInternal.Unload(GetSceneByBuildIndex(sceneBuildIndex).Data, asynchronously: true);
        public static AsyncOperation UnloadSceneAsync(string sceneName) => SceneManagerInternal.Unload(GetSceneByName(sceneName).Data, asynchronously: true);
        public static AsyncOperation UnloadSceneAsync(Scene scene) => SceneManagerInternal.Unload(scene.Data, asynchronously: true);
        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex, UnloadSceneOptions options) => UnloadSceneAsync(sceneBuildIndex);
        public static AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options) => UnloadSceneAsync(sceneName);
        public static AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options) => UnloadSceneAsync(scene);

        public static void MergeScenes(Scene sourceScene, Scene destinationScene)
        {
            var source = sourceScene.Data;
            var destination = destinationScene.Data;
            if (source == null || destination == null) throw new ArgumentException("Invalid scene.");
            foreach (var go in source.Roots.ToArray()) SceneRegistry.MoveToScene(go, destination);
            Unbound.Runtime.PhysicsWorld.SceneMembershipChanged();
            SceneManagerInternal.Unload(source);
        }

        public static void MoveGameObjectToScene(GameObject go, Scene scene)
        {
            if (go == null) throw new ArgumentException("The GameObject is null.");
            if (go.transform.parent != null) throw new ArgumentException("Gameobject is not a root in a scene");
            var data = scene.Data;
            if (data == null || !data.IsLoaded) throw new ArgumentException("The scene is invalid or not loaded.");
            SceneRegistry.MoveToScene(go, data);
            // Its bodies and colliders move to the new scene's physics scene.
            Unbound.Runtime.PhysicsWorld.SceneMembershipChanged();
        }

        public static void MoveGameObjectsToScene(Unity.Collections.NativeArray<int> instanceIDs, Scene scene)
        {
            foreach (var id in instanceIDs)
                if (ObjectRegistry.FindById(id) is GameObject go) MoveGameObjectToScene(go, scene);
        }

        internal static void MoveToDontDestroyOnLoad(GameObject go)
        {
            SceneRegistry.MoveToScene(go, SceneRegistry.DontDestroyOnLoad);
            Unbound.Runtime.PhysicsWorld.SceneMembershipChanged();
        }
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
    /// first. Async scene work is prepared before integration, honouring allowSceneActivation.</summary>
    internal static class SceneManagerInternal
    {
        internal static SceneData ActiveScene => SceneRegistry.Active;

        internal struct LoadResult
        {
            internal Scene Scene;
            internal AsyncOperation Operation;
        }

        static readonly SceneOperationQueue s_Operations = new SceneOperationQueue();
        /// <summary>Scenes requested by LoadScene/LoadSceneAsync (from an AssetBundle too) and not
        /// integrated yet: pending, held at 0.9 for allowSceneActivation, or being loaded; in
        /// request order.</summary>
        static readonly List<SceneData> s_Requested = new List<SceneData>();

        /// <summary>
        /// The scenes SceneManager lists (sceneCount, GetSceneAt, loadedSceneCount), in order. This
        /// is a view: SceneRegistry.Loaded, which loading, lighting and Find use, is not changed.
        /// Outside a Single load's teardown: the loaded scenes, then the requested ones not loaded
        /// yet, valid and not loaded (measured, Unity 6000.0.71f1: tests/projects/lightprobes
        /// Reference lifecycle.jsonl and creation-ownership.jsonl, LoadScene Single and Additive
        /// returned; a held allowSceneActivation load is not measured and is listed the same way).
        /// While a SceneManager Single load closes the scenes it replaces: the scenes loaded when
        /// it began (closed ones stay listed, valid and not loaded, until it ends), then the
        /// requested ones with its incoming scene, then the scenes made during it, closed or not
        /// (measured: creation-ownership.jsonl, in the outgoing objects' OnDestroy, in sceneUnloaded,
        /// and for a scene a sceneUnloaded handler created, also in its own sceneUnloaded). Other
        /// orders within the requested and made scenes are not measured. A scene still loading is in
        /// the loaded list and listed there once (measured: tests/projects/editorcallbacks).
        /// </summary>
        internal static IReadOnlyList<SceneData> Listed()
        {
            var teardown = SceneRegistry.Teardown;
            if (teardown == null && s_Requested.Count == 0) return SceneRegistry.Loaded;
            var list = new List<SceneData>();
            void Add(SceneData scene)
            {
                if (scene != null && !list.Contains(scene) && SceneRegistry.PublicScene(scene.Handle) != null) list.Add(scene);
            }
            if (teardown == null)
            {
                foreach (var scene in SceneRegistry.Loaded) Add(scene);
                foreach (var scene in s_Requested) Add(scene);
                return list;
            }
            foreach (var scene in teardown.Outgoing) Add(scene);
            foreach (var scene in s_Requested)
                if (!SceneRegistry.Loaded.Contains(scene)) Add(scene);
            Add(teardown.Incoming);
            foreach (var scene in teardown.Closed) Add(scene);
            Add(teardown.Closing);
            foreach (var scene in SceneRegistry.Loaded) Add(scene);
            return list;
        }

        internal static bool IsListed(SceneData scene)
        {
            var listed = Listed();
            for (int i = 0; i < listed.Count; i++)
                if (listed[i] == scene) return true;
            return false;
        }

        /// <summary>Application.isLoadingLevel: a requested scene is still in
        /// <see cref="Scene.LoadingState.Loading"/>, held loads included. A scene is Loaded before
        /// its sceneLoaded, where the value is already false. Pending async unloads also report
        /// true until their teardown removes the scene (measured: tests/projects/appfacts).</summary>
        internal static bool IsLoading
        {
            get
            {
                foreach (var data in s_Requested)
                    if (data.LoadingState == Scene.LoadingState.Loading) return true;
                return s_Operations.IsUnloading;
            }
        }

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
            CreateLocalPhysics(data, parameters.localPhysicsMode);
            data.LoadingState = Scene.LoadingState.Loading;
            s_Requested.Add(data);
            return QueueLoad(data, parameters, async, sceneManagerLoad: true);
        }

        /// <summary>A scene of a loaded AssetBundle: its path and the key its documents load by.</summary>
        internal static LoadResult LoadBundled(string path, string key, LoadSceneParameters parameters, bool async)
        {
            Unbound.Runtime.Trace.Scenes($"load scene {path} from an AssetBundle ({parameters.loadSceneMode})");
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, -1);
            CreateLocalPhysics(data, parameters.localPhysicsMode);
            data.LoadKey = key;
            data.LoadingState = Scene.LoadingState.Loading;
            s_Requested.Add(data);
            return QueueLoad(data, parameters, async, sceneManagerLoad: false);
        }

        static LoadResult QueueLoad(SceneData data, LoadSceneParameters parameters, bool async, bool sceneManagerLoad)
        {
            var op = new AsyncOperation();
            List<YDocument> documents = null;
            s_Operations.Enqueue(op, data, () => {
                try { Perform(data, parameters, sceneManagerLoad: sceneManagerLoad, documents: documents, prepared: true); }
                finally { s_Requested.Remove(data); }
                Unbound.Runtime.Trace.Scenes($"loaded scene '{data.Name}'");
            }, load: true, async: async, frame: Time.frameCount, prepare: () => documents = SceneLoader.ReadDocuments(data));
            return new LoadResult { Scene = new Scene(data.Handle), Operation = op };
        }

        /// <summary>
        /// A scene created or loaded with LocalPhysicsMode.Physics3D owns a local 3D physics scene
        /// from its creation, so its objects join it as they are made (6000.0 Scripting API:
        /// LocalPhysicsMode, CreateSceneParameters, LoadSceneParameters). A local 2D physics scene
        /// is not supported: Unbound says so, and the scene's 2D physics stays in the default world.
        /// </summary>
        internal static void CreateLocalPhysics(SceneData data, LocalPhysicsMode mode)
        {
            if ((mode & LocalPhysicsMode.Physics3D) != 0) Unbound.Runtime.PhysicsWorld.CreateLocal(data.Handle.Value);
            if ((mode & LocalPhysicsMode.Physics2D) != 0)
                Debug.LogWarning($"Unbound: LocalPhysicsMode.Physics2D is not supported yet; the 2D physics of scene '{data.Name}' is simulated in the default 2D physics scene.");
        }

        // Session teardown discards queued work without invoking a previous session's handlers.
        internal static void ResetPending()
        {
            s_Operations.Clear();
            s_Requested.Clear();
        }

        internal static void PreparePending() => s_Operations.Prepare(Time.frameCount);
        internal static void ProcessPending() => s_Operations.Process(Time.frameCount);

        /// <summary>The mode sceneLoaded reports for the scenes Play mode starts with (measured:
        /// tests/projects/editorcallbacks, 4: a value LoadSceneMode does not name).</summary>
        internal const LoadSceneMode PlayModeEntry = (LoadSceneMode)4;

        static void Perform(SceneData data, LoadSceneParameters parameters, LoadSceneMode? reported = null, bool sceneManagerLoad = false, List<YDocument> documents = null, bool prepared = false)
        {
            // Initial integration keeps the Editor's unsaved singleton roots outside the scene,
            // as a later Single load does for its outgoing scene's unsaved roots.
            if (reported == PlayModeEntry)
                foreach (var root in data.Roots.ToArray())
                    if (root != null && KeptBySingleLoad(root)) SceneRegistry.MoveToScene(root, SceneRegistry.Detached);
            if (parameters.loadSceneMode == LoadSceneMode.Single)
            {
                UnloadForSingleLoad(data, sceneManagerLoad);
                Resources.UnloadUnusedAssetsInternal();
            }
            if (!SceneRegistry.Loaded.Contains(data)) SceneRegistry.Loaded.Add(data);
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loading;
            var previousActive = SceneRegistry.Active;
            if (parameters.loadSceneMode == LoadSceneMode.Single || previousActive == null)
                SceneRegistry.Active = data;
            Action beforeAwake = reported == PlayModeEntry
                ? () => Unbound.Runtime.RuntimeInitializers.Run(RuntimeInitializeLoadType.BeforeSceneLoad) : null;
            if (prepared) SceneLoader.LoadPrepared(data, documents, beforeAwake);
            else SceneLoader.LoadInto(data, beforeAwake);
            // Play mode processes the loaded scene after its objects woke up (measured:
            // tests/projects/editorcallbacks); Edit mode opens scenes unprocessed.
            if (Unbound.Runtime.EditorSession.IsPlaying) UnityEditor.EditorInitialization.ProcessScene(new Scene(data.Handle));
            data.LoadingState = Scene.LoadingState.Loaded;
            if (parameters.loadSceneMode == LoadSceneMode.Single) Time.LevelLoaded();
            // The active scene changes before sceneLoaded (measured: tests/projects/editorcallbacks).
            if (SceneRegistry.Active == data && (previousActive != data || reported == PlayModeEntry))
                SceneManager.Internal_ActiveSceneChanged(reported != PlayModeEntry && previousActive != null
                    ? new Scene(previousActive.Handle) : default, new Scene(data.Handle));
            SceneManager.Internal_SceneLoaded(new Scene(data.Handle), reported ?? parameters.loadSceneMode);
        }

        /// <summary>
        /// The scenes a Single load of <paramref name="incoming"/> replaces are unloaded. A
        /// SceneManager load (<paramref name="sceneManagerLoad"/>) resets the light probe scene set
        /// once (LightProbes.SingleLoadReplacing), without needsRetetrahedralization, after the
        /// first outgoing scene's objects are destroyed and before its sceneUnloaded: during the
        /// recorded OnDestroy snapshot and for the object made there, the current probes keep
        /// their combined set; at sceneUnloaded it is cleared (measured for one
        /// outgoing scene, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl t1.a.iv and lifecycle.jsonl row 22). Native destroys the
        /// outgoing roots in a varying order, so where the reset falls among the roots destroyed
        /// after the measured OnDestroy is not observed; Unbound resets after all of them. With no
        /// outgoing scene, or when the teardown ends before reaching that point (an exception), the
        /// reset is made when the teardown ends. Several outgoing scenes are not measured: the
        /// reset is made once, at the first one closed, and the later ones' objects are destroyed
        /// after it. The Editor's OpenScene, Play mode entry and AssetBundle scenes are not
        /// measured and keep the per-scene unload.
        /// A SceneManager load also keeps the outgoing active scene active through its
        /// sceneUnloaded: GetActiveScene there answers that scene, valid and no longer loaded
        /// (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference lifecycle.jsonl,
        /// branch t3/light-probes-lifecycle-2, commit f8516528, row 22), and no untitled scene is
        /// created. The other Single loads are not measured and clear it before, as before.
        /// While a SceneManager load closes its outgoing scenes, objects made without a scene of
        /// their own go to the incoming scene (SceneRegistry.CreationTarget). A scene a
        /// sceneUnloaded handler creates is unloaded by the same load, after the scene whose
        /// handler made it (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl). The other Single loads keep closing only the scenes loaded
        /// when they start (not measured).
        /// During a SceneManager load's teardown the public scene queries list the outgoing,
        /// incoming and handler-made scenes (Listed); the scene being closed is no longer loaded
        /// for them, and a closed scene stays valid until the teardown ends (also when it throws).
        /// </summary>
        internal static void UnloadForSingleLoad(SceneData incoming, bool sceneManagerLoad)
        {
            if (!sceneManagerLoad)
            {
                foreach (var other in SceneRegistry.Loaded.ToArray())
                    if (other != incoming) UnloadNow(other, keepDontSave: true);
                return;
            }
            // One light probe reset per Single load, also for a load nested in this one's
            // callbacks (each has its own).
            bool probesReplaced = false;
            void ReplaceProbesOnce()
            {
                if (probesReplaced) return;
                probesReplaced = true;
                LightProbes.SingleLoadReplacing();
            }
            var outer = SceneRegistry.BeginSingleLoadTeardown(incoming);
            var teardown = SceneRegistry.Teardown;
            try
            {
                int limit = SceneRegistry.Loaded.Count + TeardownCreatedSceneLimit;
                int closed = 0;
                SceneData other;
                while ((other = SceneRegistry.Loaded.Find(s => s != incoming)) != null)
                {
                    if (closed == limit) throw TeardownLimitReached(incoming, closed);
                    closed++;
                    var before = new HashSet<GameObject>(incoming.Roots);
                    teardown.Closing = other;
                    try { UnloadNow(other, keepDontSave: true, activeThroughUnloaded: true, objectsDestroyed: ReplaceProbesOnce); }
                    finally { teardown.Closing = null; }
                    ReportNotCleanedUp(incoming, before);
                }
            }
            finally
            {
                try { ReplaceProbesOnce(); }
                finally { SceneRegistry.EndSingleLoadTeardown(outer); }
            }
        }

        /// <summary>How many scenes the callbacks of one Single load's teardown may create beyond
        /// the scenes loaded when it starts. This is Unbound's resource bound against handlers
        /// that keep creating scenes; it is not Unity behaviour (Unity's is not measured).</summary>
        internal const int TeardownCreatedSceneLimit = 1024;

        static InvalidOperationException TeardownLimitReached(SceneData incoming, int closed)
        {
            var left = new System.Text.StringBuilder();
            foreach (var scene in SceneRegistry.Loaded)
                if (scene != incoming) left.Append(left.Length == 0 ? "" : ", ").Append('\'').Append(scene.Name).Append('\'');
            return new InvalidOperationException(
                $"The Single load of '{incoming.Name}' stopped after closing {closed} scenes: its sceneUnloaded handlers keep "
                + $"creating scenes. Unbound closes at most {TeardownCreatedSceneLimit} scenes created during one load's teardown "
                + $"(an implementation limit, not Unity's behaviour). Still loaded, not closed: {left}. The {incoming.Roots.Count} "
                + $"objects made during the teardown stay in '{incoming.Name}', which is not loaded.");
        }

        /// <summary>
        /// The Error Unity logs after a closed scene's sceneUnloaded handlers when roots were made
        /// while it closed (measured text, Unity 6000.0.71f1: tests/projects/lightprobes Reference
        /// creation-ownership.jsonl). They are not destroyed: they live on in the incoming scene.
        /// Unity's name order varies between runs (measured); this lists them in the incoming
        /// scene's root order. One Error per closed scene that made roots is an inference: the
        /// measurement closes one outgoing scene. A handler of this log message runs with the
        /// teardown still in effect; roots it makes after this snapshot are not included in this
        /// diagnostic. Unity's behaviour for objects made by the diagnostic's handlers is unmeasured.
        /// </summary>
        static void ReportNotCleanedUp(SceneData incoming, HashSet<GameObject> before)
        {
            var names = new System.Text.StringBuilder();
            foreach (var root in incoming.Roots)
                if (!before.Contains(root) && !root.m_Destroyed) names.Append(root.m_Name).Append('\n');
            if (names.Length == 0) return;
            Debug.LogError(NotCleanedUpHeader + names);
        }

        internal const string NotCleanedUpHeader = "Some objects were not cleaned up when closing the scene. "
            + "(Did you spawn new GameObjects from OnDestroy?)\nThe following scene GameObjects were found:\n";

        /// <summary>Editor exit destroys the playing objects before closing its scenes
        /// (Unity 6000.0.71f1: tests/projects/editorcallbacks quit-play control).</summary>
        internal static void CloseEditorScenes()
        {
            EditorSession.DestroyPlayModeObjects(destroyDetached: true);
            ResetPending();
            foreach (var scene in SceneRegistry.Loaded.ToArray())
            {
                try { UnloadNow(scene); }
                catch (Exception e) { Debug.LogException(e); }
                finally
                {
                    // A throwing unload callback must not retain the closed scene.
                    if (SceneRegistry.Get(scene.Handle) != null) SceneRegistry.Forget(scene);
                }
            }
            SceneRegistry.Active = null;
        }

        /// <summary>Loads a scene immediately: the first scene of a session.</summary>
        internal static Scene LoadFirst(int buildIndex)
        {
            var (path, _) = ProjectInfo.BuildScenes[buildIndex];
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            data.OpenedInEditor = true;
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single));
            return new Scene(data.Handle);
        }

        /// <summary>The initial scene owns objects created while the Editor domain loads for
        /// Play mode, before runtime initializers and the scene file's activation.</summary>
        internal static SceneData PreparePlayModeScene(string path)
        {
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path,
                SceneUtility.GetBuildIndexByScenePath(path));
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loaded;
            SceneRegistry.Loaded.Add(data);
            SceneRegistry.Active = data;
            return data;
        }

        /// <summary>Loads a scene by path, also one outside the build settings (as the Editor can);
        /// <paramref name="playModeEntry"/>: the scene Play mode starts with.</summary>
        internal static Scene LoadPath(string path, bool playModeEntry = false, SceneData prepared = null)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            var data = prepared != null && SceneRegistry.Get(prepared.Handle) == prepared
                ? prepared : SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            data.OpenedInEditor = true;
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single), playModeEntry ? PlayModeEntry : null);
            return new Scene(data.Handle);
        }

        /// <summary>EditorSceneManager.OpenScene: the scene file loads now, alone or added.
        /// <paramref name="opened"/>: the Editor opens it (Edit mode), as opposed to a load in
        /// Play mode (EditorSceneManager.LoadSceneInPlayMode), which no bake follows.</summary>
        internal static Scene OpenPath(string path, bool additive, bool opened = true)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            data.OpenedInEditor = opened;
            Perform(data, new LoadSceneParameters(additive ? LoadSceneMode.Additive : LoadSceneMode.Single));
            return new Scene(data.Handle);
        }

        /// <summary>EditorSceneManager.NewScene: an untitled scene, alone or added.</summary>
        internal static Scene NewEmpty(bool additive)
        {
            if (!additive)
                foreach (var other in SceneRegistry.Loaded.ToArray()) UnloadNow(other);
            var data = SceneRegistry.Create("", "", -1);
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loaded;
            SceneRegistry.Loaded.Add(data);
            var previous = SceneRegistry.Active;
            if (!additive || previous == null || !previous.IsLoaded) SceneRegistry.Active = data;
            return new Scene(data.Handle);
        }

        /// <summary>EditorSceneManager.CloseScene: its objects go, and the scene with them when
        /// removed.</summary>
        internal static bool Close(SceneData data, bool remove)
        {
            if (data == null || SceneRegistry.Loaded.Count <= 1 && data.IsLoaded) return false;
            if (data.IsLoaded) UnloadNow(data);
            if (remove) SceneRegistry.Forget(data);
            if (SceneRegistry.Active == data || SceneRegistry.Active == null)
                SceneRegistry.Active = SceneRegistry.Loaded.Count > 0 ? SceneRegistry.Loaded[0] : null;
            return true;
        }

        internal static AsyncOperation Unload(SceneData data, bool asynchronously = false)
        {
            Unbound.Runtime.Trace.Scenes($"unload scene '{data?.Name}'");
            if (data == null || !data.IsLoaded)
                throw new ArgumentException("Scene to unload is invalid");
            if (SceneRegistry.Loaded.Count <= 1)
            {
                Debug.LogWarning("Unloading the last loaded scene is not supported.");
                return null;
            }
            if (asynchronously && Compat.DestroyPhasesMeasured)
            {
                var operation = new AsyncOperation();
                s_Operations.Enqueue(operation, data, () => {
                    if (!data.IsLoaded) return;
                    if (SceneRegistry.Loaded.Count > 1) FinishUnload(data);
                    else Debug.LogWarning("Unloading the last loaded scene is not supported.");
                }, load: false, async: true, frame: Time.frameCount);
                return operation;
            }
            FinishUnload(data);
            return AsyncOperation.Completed();
        }

        static void FinishUnload(SceneData data)
        {
            UnloadNow(data);
            if (SceneRegistry.Active == data || SceneRegistry.Active == null)
                SceneRegistry.Active = SceneRegistry.Loaded.Count > 0 ? SceneRegistry.Loaded[0] : null;
        }

        /// <summary>The roots a Single load keeps: those that must not be saved
        /// (HideFlags.DontSave) leave their scene for none (measured: tests/projects/messages).</summary>
        static bool KeptBySingleLoad(GameObject root) => (root.hideFlags & HideFlags.DontSaveInEditor) != 0;

        /// <param name="activeThroughUnloaded">A SceneManager Single load: the scene, when active,
        /// stays the active one until its sceneUnloaded handlers have run, and it stays valid and
        /// listed until the load's teardown ends (UnloadForSingleLoad).</param>
        /// <param name="objectsDestroyed">Called once the scene's objects are destroyed, before its
        /// probes leave the loaded set and its sceneUnloaded: a SceneManager Single load's light
        /// probe reset (UnloadForSingleLoad).</param>
        static void UnloadNow(SceneData data, bool keepDontSave = false, bool activeThroughUnloaded = false, Action objectsDestroyed = null)
        {
            data.LoadingState = Scene.LoadingState.Unloading;
            foreach (var root in data.Roots.ToArray())
            {
                if (root.m_Destroyed) continue;
                if (keepDontSave && KeptBySingleLoad(root)) SceneRegistry.MoveToScene(root, SceneRegistry.Detached);
                else Destruction.DestroyImmediate(root);
            }
            // Its local physics scene goes with it, once its objects have gone (when within the
            // unload is not documented or measured).
            Unbound.Runtime.PhysicsWorld.ReleaseLocal(data.Handle.Value);
            // So does its baked NavMesh (measured after the unload completes: tests/projects/navbaked).
            Unbound.Runtime.SceneNavMesh.SceneUnloaded(data.Handle.Value);
            data.IsLoaded = false;
            data.LoadingState = Scene.LoadingState.NotLoaded;
            SceneRegistry.Loaded.Remove(data);
            bool keepActive = activeThroughUnloaded && SceneRegistry.Active == data;
            if (SceneRegistry.Active == data && !keepActive) SceneRegistry.Active = null;
            // Its probes leave the loaded set, raising needsRetetrahedralization before sceneUnloaded
            // (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference lifecycle.jsonl,
            // branch t3/light-probes-lifecycle-2, commit f8516528). A SceneManager Single load
            // takes its outgoing scenes' probes out of the set here (objectsDestroyed), once, so
            // nothing is raised then.
            // For UnloadSceneAsync the measured profile reaches this teardown from the next
            // preloading pass. Its callbacks precede completion of that operation.
            try
            {
                objectsDestroyed?.Invoke();
                LightProbes.SceneUnloaded(data.Handle.Value);
                RealtimeGI.SceneUnloaded(data.Handle.Value);
                // sceneUnloaded sees the scene with its name (measured: tests/projects/editorcallbacks);
                // it is forgotten after.
                SceneManager.Internal_SceneUnloaded(new Scene(data.Handle));
            }
            finally
            {
                // The kept active scene is cleared once its handlers have run (also when one
                // threw), unless a handler made another scene active.
                if (keepActive && SceneRegistry.Active == data) SceneRegistry.Active = null;
            }
            // A SceneManager Single load's public queries keep the scene valid and listed until its
            // teardown ends; engine code sees it forgotten now, as before.
            if (activeThroughUnloaded) SceneRegistry.CloseForSingleLoad(data);
            else SceneRegistry.Forget(data);
        }
    }
}
