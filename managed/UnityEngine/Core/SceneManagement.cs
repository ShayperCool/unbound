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
            // An invalid scene has no name (measured: tests/projects/messages, null).
            get => Data?.Name;
            set { if (Data != null) Data.Name = value; }
        }
        /// <summary>Not yet while the scene's objects wake up and the scene is processed
        /// (measured: tests/projects/editorcallbacks).</summary>
        public bool isLoaded => Data != null && Data.IsLoaded && Data.LoadingState != LoadingState.Loading;
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
            // A loading scene lists its roots to the scene processors (measured: tests/projects/editorcallbacks).
            // So does the incoming scene of a Single load while the outgoing scenes close, with
            // the objects made there meanwhile (measured: SceneRegistry.CreationTarget).
            if (!Data.IsLoaded && !SceneRegistry.IsTeardownIncoming(Data)) throw new ArgumentException("The scene is not loaded.");
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
                if (AssetBundle.FindScene(sceneName, out var path, out var key))
                    return SceneManagerInternal.LoadBundled(path, key, parameters).Scene;
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
                    return SceneManagerInternal.LoadBundled(path, key, parameters).Operation;
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
        /// <summary>Scenes requested by LoadScene/LoadSceneAsync (from an AssetBundle too) and not
        /// integrated yet: pending, held at 0.9 for allowSceneActivation, or being loaded.</summary>
        static readonly HashSet<SceneData> s_Requested = new HashSet<SceneData>();

        /// <summary>Application.isLoadingLevel: a requested scene is still in
        /// <see cref="Scene.LoadingState.Loading"/>, held loads included. A scene is Loaded before
        /// its sceneLoaded, where the value is already false (measured: tests/projects/appfacts).</summary>
        internal static bool IsLoading
        {
            get
            {
                foreach (var data in s_Requested)
                    if (data.LoadingState == Scene.LoadingState.Loading) return true;
                return false;
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
            data.LoadingState = Scene.LoadingState.Loading;
            s_Requested.Add(data);
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

        /// <summary>A scene of a loaded AssetBundle: its path and the key its documents load by.</summary>
        internal static LoadResult LoadBundled(string path, string key, LoadSceneParameters parameters)
        {
            Unbound.Runtime.Trace.Scenes($"load scene {path} from an AssetBundle ({parameters.loadSceneMode})");
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, -1);
            data.LoadKey = key;
            data.LoadingState = Scene.LoadingState.Loading;
            s_Requested.Add(data);
            var op = new AsyncOperation();
            s_Pending.Add((op, -1, parameters, data));
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
                // Entries with a build index are SceneManager.LoadScene(Async) loads; AssetBundle
                // scenes (index -1) keep the general path.
                try { Perform(data, parameters, sceneManagerLoad: index >= 0); }
                catch (Exception e) { Debug.LogException(e); }
                finally { s_Requested.Remove(data); }
                Unbound.Runtime.Trace.Scenes($"loaded scene '{data.Name}'");
                op.Complete();
            }
        }

        /// <summary>The mode sceneLoaded reports for the scenes Play mode starts with (measured:
        /// tests/projects/editorcallbacks, 4: a value LoadSceneMode does not name).</summary>
        internal const LoadSceneMode PlayModeEntry = (LoadSceneMode)4;

        static void Perform(SceneData data, LoadSceneParameters parameters, LoadSceneMode? reported = null, bool sceneManagerLoad = false)
        {
            if (parameters.loadSceneMode == LoadSceneMode.Single)
            {
                UnloadForSingleLoad(data, sceneManagerLoad);
                Resources.UnloadUnusedAssetsInternal();
            }
            SceneRegistry.Loaded.Add(data);
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loading;
            var previousActive = SceneRegistry.Active;
            if (parameters.loadSceneMode == LoadSceneMode.Single || previousActive == null)
                SceneRegistry.Active = data;
            SceneLoader.LoadInto(data);
            // Play mode processes the loaded scene after its objects woke up (measured:
            // tests/projects/editorcallbacks); Edit mode opens scenes unprocessed.
            if (Unbound.Runtime.EditorSession.IsPlaying) UnityEditor.EditorInitialization.ProcessScene(new Scene(data.Handle));
            data.LoadingState = Scene.LoadingState.Loaded;
            if (parameters.loadSceneMode == LoadSceneMode.Single) Time.LevelLoaded();
            // The active scene changes before sceneLoaded (measured: tests/projects/editorcallbacks).
            if (SceneRegistry.Active == data && previousActive != data)
                SceneManager.Internal_ActiveSceneChanged(previousActive != null ? new Scene(previousActive.Handle) : default, new Scene(data.Handle));
            SceneManager.Internal_SceneLoaded(new Scene(data.Handle), reported ?? parameters.loadSceneMode);
        }

        /// <summary>
        /// The scenes a Single load of <paramref name="incoming"/> replaces are unloaded. A
        /// SceneManager load (<paramref name="sceneManagerLoad"/>) first resets the light probe
        /// scene set once, before the outgoing scenes' sceneUnloaded and without
        /// needsRetetrahedralization (measured for one outgoing scene: tests/projects/lightprobes
        /// Reference lifecycle.jsonl). The Editor's OpenScene, Play mode entry and AssetBundle
        /// scenes are not measured and keep the per-scene unload.
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
        /// </summary>
        internal static void UnloadForSingleLoad(SceneData incoming, bool sceneManagerLoad)
        {
            if (!sceneManagerLoad)
            {
                foreach (var other in SceneRegistry.Loaded.ToArray())
                    if (other != incoming) UnloadNow(other, keepDontSave: true);
                return;
            }
            LightProbes.SingleLoadReplacing();
            var outer = SceneRegistry.BeginSingleLoadTeardown(incoming);
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
                    UnloadNow(other, keepDontSave: true, activeThroughUnloaded: true);
                    ReportNotCleanedUp(incoming, before);
                }
            }
            finally
            {
                SceneRegistry.EndSingleLoadTeardown(outer);
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
        /// teardown still in effect; the roots it makes are reported with the next closed scene.
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

        /// <summary>Loads a scene immediately: the first scene of a session.</summary>
        internal static Scene LoadFirst(int buildIndex)
        {
            var (path, _) = ProjectInfo.BuildScenes[buildIndex];
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single));
            return new Scene(data.Handle);
        }

        /// <summary>Loads a scene by path, also one outside the build settings (as the Editor can);
        /// <paramref name="playModeEntry"/>: the scene Play mode starts with.</summary>
        internal static Scene LoadPath(string path, bool playModeEntry = false)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
            Perform(data, new LoadSceneParameters(LoadSceneMode.Single), playModeEntry ? PlayModeEntry : null);
            return new Scene(data.Handle);
        }

        /// <summary>EditorSceneManager.OpenScene: the scene file loads now, alone or added.</summary>
        internal static Scene OpenPath(string path, bool additive)
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, buildIndex);
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

        /// <summary>The roots a Single load keeps: those that must not be saved
        /// (HideFlags.DontSave) leave their scene for none (measured: tests/projects/messages).</summary>
        static bool KeptBySingleLoad(GameObject root) => (root.hideFlags & HideFlags.DontSaveInEditor) != 0;

        /// <param name="activeThroughUnloaded">A SceneManager Single load: the scene, when active,
        /// stays the active one until its sceneUnloaded handlers have run (UnloadForSingleLoad).</param>
        static void UnloadNow(SceneData data, bool keepDontSave = false, bool activeThroughUnloaded = false)
        {
            data.LoadingState = Scene.LoadingState.Unloading;
            foreach (var root in data.Roots.ToArray())
            {
                if (root.m_Destroyed) continue;
                if (keepDontSave && KeptBySingleLoad(root)) SceneRegistry.MoveToScene(root, SceneRegistry.Detached);
                else Destruction.DestroyImmediate(root);
            }
            data.IsLoaded = false;
            data.LoadingState = Scene.LoadingState.NotLoaded;
            SceneRegistry.Loaded.Remove(data);
            bool keepActive = activeThroughUnloaded && SceneRegistry.Active == data;
            if (SceneRegistry.Active == data && !keepActive) SceneRegistry.Active = null;
            // Its probes leave the loaded set, raising needsRetetrahedralization before sceneUnloaded
            // (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference lifecycle.jsonl,
            // branch t3/light-probes-lifecycle-2, commit f8516528). A SceneManager Single load has
            // already taken its outgoing scenes' probes out of the set, so nothing is raised then.
            // Unity raises the group (needs, sceneUnloaded, the operation's completion) after
            // UnloadSceneAsync returns; here it happens within the call (not fixed).
            try
            {
                LightProbes.SceneUnloaded(data.Handle.Value);
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
            SceneRegistry.Forget(data);
        }
    }
}
