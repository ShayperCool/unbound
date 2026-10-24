using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The Editor application as Play mode code sees it: always playing; update and
    /// delayCall run in the frame, as the Editor runs them.</summary>
    public sealed partial class EditorApplication
    {
        public delegate void CallbackFunction();
        public delegate void ProjectWindowItemCallback(string guid, Rect selectionRect);
        public delegate void HierarchyWindowItemCallback(int instanceID, Rect selectionRect);

        public static CallbackFunction update;
        public static CallbackFunction delayCall;
        public static CallbackFunction searchChanged;
        public static CallbackFunction modifierKeysChanged;
        public static ProjectWindowItemCallback projectWindowItemOnGUI;
        public static HierarchyWindowItemCallback hierarchyWindowItemOnGUI;

        public static event Action<PlayModeStateChange> playModeStateChanged;
        public static event Action<PauseState> pauseStateChanged;
        public static event Action quitting;
        public static event Func<bool> wantsToQuit;
        public static event Action hierarchyChanged;
        public static event Action projectChanged;
        public static event Action<bool> focusChanged;

        static bool s_Paused;

        static EditorApplication()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerSendFrameComplete), RunEditorCallbacks);
        }

        internal static void EnsureRegistered() { }

        /// <summary>The Editor's own per-update work (selection notifications, undo flushes),
        /// before the callbacks editor code registers.</summary>
        internal static Action InternalUpdate;

        /// <summary>The Editor's update, run from an Edit mode frame.</summary>
        internal static void RunEditorCallbacksNow() => RunEditorCallbacks();

        static void RunEditorCallbacks()
        {
            try { InternalUpdate?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            var calls = delayCall;
            delayCall = null;
            if (calls != null)
            {
                foreach (CallbackFunction call in calls.GetInvocationList())
                {
                    try { call(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            if (update != null)
            {
                foreach (CallbackFunction call in update.GetInvocationList())
                {
                    long start = Trace.PerfOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    try { call(); }
                    catch (Exception e) { Debug.LogException(e); }
                    if (Trace.PerfOn) Trace.Perf("EditorApplication.update " + call.Method.DeclaringType?.FullName + "." + call.Method.Name, start);
                }
            }
        }

        public static bool isPlaying
        {
            get => EditorSession.IsPlaying;
            set => EditorSession.Request(value);
        }

        public static bool isPlayingOrWillChangePlaymode => EditorSession.IsPlaying != EditorSession.WillChange;
        public static bool isPaused { get => s_Paused; set => s_Paused = value; }
        public static bool isCompiling => false;
        public static bool isUpdating => false;
        public static bool isRemoteConnected => false;
        public static bool isTemporaryProject => false;
        public static bool isFocused => Application.isFocused;
        public static bool isCreateFolder => false;
        public static double timeSinceStartup => Time.realtimeSinceStartupAsDouble;
        public static string applicationContentsPath => UnityInstallInfo.DataPath;
        public static string applicationPath => System.IO.Path.Combine(UnityInstallInfo.DataPath, "..", "Unity");
        [Obsolete("Use EditorSceneManager to see which scenes are currently loaded")]
        public static string currentScene { get => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; set { } }

        public static void ExitPlaymode() => EditorSession.Request(false);
        public static void EnterPlaymode() => EditorSession.Request(true);
        public static void Step() { }
        public static void Beep() { }
        public static void QueuePlayerLoopUpdate() { }
        public static void RepaintHierarchyWindow() { }
        public static void RepaintProjectWindow() { }
        public static void RepaintAnimationWindow() { }
        public static void DirtyHierarchyWindowSorting() { }
        public static bool ExecuteMenuItem(string menuItemPath) => false;
        public static void LockReloadAssemblies() { }
        public static void UnlockReloadAssemblies() { }
        public static void OpenProject(string projectPath, params string[] args) { }
        /// <summary>The Editor exits with the code (the Test Framework's run result).</summary>
        public static void Exit(int returnValue) => PlayerLoopDriver.RequestQuit(returnValue);
        public static void SetTemporaryProjectKeepPath(string path) { }
        public static void UpdateMainWindowTitle() { }

        /// <summary>Each handler runs on its own: one that throws is logged and the others still run.</summary>
        internal static void Internal_PlayModeStateChanged(PlayModeStateChange state)
        {
            var handlers = playModeStateChanged;
            if (handlers == null) return;
            foreach (Action<PlayModeStateChange> handler in handlers.GetInvocationList())
            {
                try { handler(state); }
                catch (TypeInitializationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }



    /// <summary>Facts about the Unity installation the project belongs to.</summary>
    internal static class UnityInstallInfo
    {
        internal static string DataPath = "";
    }

    /// <summary>Player settings of the project, as Play mode code reads them.</summary>
    public sealed partial class PlayerSettings : Object
    {
        public static string companyName { get => ProjectInfo.CompanyName; set => ProjectInfo.CompanyName = value; }
        public static string productName { get => ProjectInfo.ProductName; set => ProjectInfo.ProductName = value; }
        public static string applicationIdentifier { get => ProjectInfo.Identifier; set => ProjectInfo.Identifier = value; }
        public static string bundleVersion { get => ProjectInfo.BundleVersion; set => ProjectInfo.BundleVersion = value; }
        public static bool runInBackground { get => Application.runInBackground; set => Application.runInBackground = value; }
        public static ColorSpace colorSpace { get => QualitySettings.activeColorSpace; set { } }
        public static int defaultScreenWidth { get; set; } = 1024;
        public static int defaultScreenHeight { get; set; } = 768;
        public static bool allowUnsafeCode { get; set; }
        public static bool stripEngineCode { get; set; }

        internal static List<(string Guid, long FileId)> s_PreloadedRefs = new List<(string, long)>();
        static Object[] s_Preloaded;

        /// <summary>The assets PlayerSettings preloads (loaded on first use; changes stay in memory).</summary>
        public static Object[] GetPreloadedAssets()
        {
            if (s_Preloaded == null)
            {
                var list = new List<Object>();
                foreach (var (guid, fileId) in s_PreloadedRefs)
                    list.Add(AssetDatabaseRuntime.Load(guid, fileId, typeof(Object)));
                s_Preloaded = list.ToArray();
            }
            return (Object[])s_Preloaded.Clone();
        }

        public static void SetPreloadedAssets(Object[] assets) => s_Preloaded = (Object[])(assets ?? Array.Empty<Object>()).Clone();

        public static string GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget) => string.Join(";", ProjectInfo.Defines);

        public static string GetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup) => string.Join(";", ProjectInfo.Defines);

        public static void SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget, string defines) { }

        public static void SetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup, string defines) { }

        public static string GetApplicationIdentifier(BuildTargetGroup targetGroup) => ProjectInfo.Identifier;

        public static string GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget buildTarget) => ProjectInfo.Identifier;

        public sealed partial class Android
        {
            public static int bundleVersionCode { get; set; } = 1;
        }

        public sealed partial class iOS
        {
            public static string buildNumber { get; set; } = "1";
        }
    }

    /// <summary>Prefab queries Play mode code asks: assets are prefab assets; scene objects are
    /// treated as plain objects (Unbound does not keep instance links at run time).</summary>
    public sealed partial class PrefabUtility
    {
        static GameObject GameObjectOf(Object o) => o as GameObject ?? (o as Component)?.m_GameObject;

        public static bool IsPartOfPrefabAsset(Object componentOrGameObject)
        {
            var go = GameObjectOf(componentOrGameObject);
            return go != null && go.m_IsPrefabAsset;
        }

        public static bool IsPartOfAnyPrefab(Object componentOrGameObject) => IsPartOfPrefabAsset(componentOrGameObject);

        public static bool IsPartOfPrefabInstance(Object componentOrGameObject) => false;

        public static bool IsPartOfNonAssetPrefabInstance(Object componentOrGameObject) => false;

        public static bool IsPartOfRegularPrefab(Object componentOrGameObject) => IsPartOfPrefabAsset(componentOrGameObject);

        public static bool IsPartOfModelPrefab(Object componentOrGameObject) => false;

        public static bool IsPartOfVariantPrefab(Object componentOrGameObject) => false;

        public static bool IsPartOfImmutablePrefab(Object componentOrGameObject) => false;

        public static bool IsAnyPrefabInstanceRoot(GameObject gameObject) => false;

        public static bool IsOutermostPrefabInstanceRoot(GameObject gameObject) => false;

        public static PrefabAssetType GetPrefabAssetType(Object componentOrGameObject) =>
            IsPartOfPrefabAsset(componentOrGameObject) ? PrefabAssetType.Regular : PrefabAssetType.NotAPrefab;

        public static PrefabInstanceStatus GetPrefabInstanceStatus(Object componentOrGameObject) => PrefabInstanceStatus.NotAPrefab;

        public static T GetCorrespondingObjectFromSource<T>(T componentOrGameObject) where T : Object => null;

        public static GameObject GetOutermostPrefabInstanceRoot(Object componentOrGameObject) => null;

        public static GameObject GetNearestPrefabInstanceRoot(Object componentOrGameObject) => null;

        public static string GetPrefabAssetPathOfNearestInstanceRoot(Object instanceComponentOrGameObject) => "";

        public static Object InstantiatePrefab(Object assetComponentOrGameObject) => Object.Instantiate(assetComponentOrGameObject);

        public static Object InstantiatePrefab(Object assetComponentOrGameObject, Transform parent) => Object.Instantiate(assetComponentOrGameObject, parent);

        public static Object InstantiatePrefab(Object assetComponentOrGameObject, UnityEngine.SceneManagement.Scene destinationScene) => Object.Instantiate(assetComponentOrGameObject, destinationScene);

        public static void RecordPrefabInstancePropertyModifications(Object targetObject) { }
    }



    /// <summary>EditorUtility as Play mode code uses it.</summary>
    public partial class EditorUtility
    {
        public static bool IsPersistent(Object target) => target != null && target.IsPersistentAsset;
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) => false;
        public static void ClearProgressBar() { }
        public static bool DisplayDialog(string title, string message, string ok, [UnityEngine.Internal.DefaultValue("\"\"")] string cancel)
        {
            Debug.Log($"[Unbound] Editor dialog '{title}': {message} -> {ok}");
            return true;
        }
        public static bool DisplayDialog(string title, string message, string ok) => DisplayDialog(title, message, ok, "");
        public static Object InstanceIDToObject(int instanceID) => ObjectRegistry.FindById(instanceID);
        public static void UnloadUnusedAssetsImmediate() { }
        public static void UnloadUnusedAssetsImmediate(bool includeMonoReferencesAsRoots) { }
        public static void CopySerialized(Object source, Object dest)
        {
            if (source != null && dest != null) new SerializedCopier(o => o).CopyScriptObject(source, dest);
        }
        public static void FocusProjectWindow() { }
        public static void RevealInFinder(string path) { }
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => "";
        public static string OpenFolderPanel(string title, string folder, string defaultName) => "";
    }
}

namespace UnityEditor.Build
{
    public partial struct NamedBuildTarget : IEquatable<NamedBuildTarget>
    {
        public string TargetName { get; }

        internal NamedBuildTarget(string name) { TargetName = name; }

        public static readonly NamedBuildTarget Unknown = new NamedBuildTarget("");
        public static readonly NamedBuildTarget Standalone = new NamedBuildTarget("Standalone");
        public static readonly NamedBuildTarget Server = new NamedBuildTarget("Server");
        public static readonly NamedBuildTarget iOS = new NamedBuildTarget("iPhone");
        public static readonly NamedBuildTarget Android = new NamedBuildTarget("Android");
        public static readonly NamedBuildTarget WebGL = new NamedBuildTarget("WebGL");

        public static NamedBuildTarget FromBuildTargetGroup(BuildTargetGroup buildTargetGroup) => new NamedBuildTarget(buildTargetGroup.ToString());

        public BuildTargetGroup ToBuildTargetGroup() => Enum.TryParse<BuildTargetGroup>(TargetName, out var g) ? g : BuildTargetGroup.Unknown;

        public bool Equals(NamedBuildTarget other) => TargetName == other.TargetName;
        public override bool Equals(object obj) => obj is NamedBuildTarget other && Equals(other);
        public override int GetHashCode() => TargetName?.GetHashCode() ?? 0;
        public static bool operator ==(NamedBuildTarget lhs, NamedBuildTarget rhs) => lhs.Equals(rhs);
        public static bool operator !=(NamedBuildTarget lhs, NamedBuildTarget rhs) => !lhs.Equals(rhs);
    }
}
