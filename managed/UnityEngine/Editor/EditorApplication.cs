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
        /// <summary>Play mode's pause: while paused, frames run only the Editor's updates (and a
        /// Step one game frame). In a window Ctrl+Shift+P toggles it, as in the Editor.</summary>
        public static bool isPaused
        {
            get => s_Paused;
            set
            {
                if (s_Paused == value) return;
                s_Paused = value;
                if (!EditorSession.IsPlaying) return;
                if (value) Native.Log((int)LogType.Log, "[Unbound] Play mode paused: frames stop until EditorApplication.isPaused is false again (Ctrl+Shift+P in the window)");
                pauseStateChanged?.Invoke(value ? PauseState.Paused : PauseState.Unpaused);
            }
        }

        static bool s_StepRequested;

        /// <summary>Takes a Step request: the paused game runs one frame.</summary>
        internal static bool TakeStep()
        {
            bool step = s_StepRequested;
            s_StepRequested = false;
            return step;
        }
        [Unbound.EditorFaithful("nothing compiles while playing: Unbound compiles the scripts before Play")]
        public static bool isCompiling => false;
        [Unbound.EditorFaithful("no asset refresh runs while playing (the asset list follows Unbound's watcher)")]
        public static bool isUpdating => false;
        [Unbound.EditorFaithful("no Unity Remote device is connected")]
        public static bool isRemoteConnected => false;
        [Unbound.EditorFaithful("the project is a regular one, not a temporary project")]
        public static bool isTemporaryProject => false;
        public static bool isFocused => Application.isFocused;
        public static bool isCreateFolder => false;
        public static double timeSinceStartup => Time.realtimeSinceStartupAsDouble;
        public static string applicationContentsPath => UnityInstallInfo.DataPath;
        /// <summary>The Editor's executable: <c>Editor/Unity</c>, on macOS the <c>Unity.app</c> bundle.</summary>
        public static string applicationPath => OperatingSystem.IsMacOS()
            ? System.IO.Path.GetDirectoryName(UnityInstallInfo.DataPath.TrimEnd('/'))
            : System.IO.Path.Combine(UnityInstallInfo.DataPath, "..", "Unity");
        [Obsolete("Use EditorSceneManager to see which scenes are currently loaded")]
        [Unbound.EditorFaithful("Unity's obsolete setter does nothing too")]
        public static string currentScene { get => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; set { } }

        public static void ExitPlaymode() => EditorSession.Request(false);
        public static void EnterPlaymode() => EditorSession.Request(true);
        /// <summary>Runs one game frame of a paused Play mode (and pauses it if it was not).</summary>
        public static void Step()
        {
            if (!EditorSession.IsPlaying) return;
            isPaused = true;
            s_StepRequested = true;
        }
        public static void Beep() => Stubs.Ignored("EditorApplication.Beep", "plays no system sound");
        /// <summary>Requests an Edit mode script update even without a scene modification.</summary>
        public static void QueuePlayerLoopUpdate() => EditorSession.QueuePlayerLoopUpdate();
        [Unbound.EditorFaithful("no Hierarchy window is open (as the batch-mode Editor)")]
        public static void RepaintHierarchyWindow() { }
        [Unbound.EditorFaithful("no Project window is open (as the batch-mode Editor)")]
        public static void RepaintProjectWindow() { }
        [Unbound.EditorFaithful("no Animation window is open (as the batch-mode Editor)")]
        public static void RepaintAnimationWindow() { }
        [Unbound.EditorFaithful("no Hierarchy window is open (as the batch-mode Editor)")]
        public static void DirtyHierarchyWindowSorting() { }

        /// <summary>Runs a project's [MenuItem] method (after its validate function, if any); the
        /// Editor's own menu items are not available.</summary>
        public static bool ExecuteMenuItem(string menuItemPath)
        {
            System.Reflection.MethodInfo run = null, check = null;
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                if (!method.IsStatic || method.GetParameters().Length != 0) continue;
                foreach (MenuItem item in method.GetCustomAttributes(typeof(MenuItem), false))
                {
                    if (item.menuItem != menuItemPath) continue;
                    if (item.validate) check ??= method;
                    else run ??= method;
                }
            }
            if (run == null)
            {
                Stubs.Ignored("EditorApplication.ExecuteMenuItem", "runs only the project's own [MenuItem] methods without parameters");
                return false;
            }
            try
            {
                if (check != null && check.ReturnType == typeof(bool) && !(bool)check.Invoke(null, null)) return false;
                run.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
            return true;
        }

        [Unbound.EditorFaithful("Unbound never reloads assemblies while playing")]
        public static void LockReloadAssemblies() { }
        [Unbound.EditorFaithful("Unbound never reloads assemblies while playing")]
        public static void UnlockReloadAssemblies() { }
        public static void OpenProject(string projectPath, params string[] args) =>
            Stubs.Ignored("EditorApplication.OpenProject", "does not open another project");
        /// <summary>The Editor exits with the code (the Test Framework's run result).</summary>
        public static void Exit(int returnValue) => PlayerLoopDriver.RequestQuit(returnValue, editorExit: true);
        [Unbound.EditorFaithful("the project is not a temporary one, so there is nothing to keep")]
        public static void SetTemporaryProjectKeepPath(string path) { }
        [Unbound.EditorFaithful("Unbound's Editor has no main window title (as the batch-mode Editor)")]
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
        public static ColorSpace colorSpace
        {
            get => QualitySettings.activeColorSpace;
            set
            {
                if (value != QualitySettings.activeColorSpace)
                    Stubs.Ignored("PlayerSettings.colorSpace", "cannot change while playing (the project's color space stays)");
            }
        }
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

        // Defines set in this session: answered back, but the scripts are not recompiled with them.
        static readonly Dictionary<string, string> s_SetDefines = new Dictionary<string, string>();

        public static string GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget) =>
            s_SetDefines.TryGetValue(buildTarget.TargetName ?? "", out var set) ? set : string.Join(";", ProjectInfo.Defines);

        [global::System.@ObsoleteAttribute("Use GetScriptingDefineSymbols(NamedBuildTarget buildTarget) instead")] public static string GetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup) =>
            GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup));

        public static void SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget, string defines)
        {
            Stubs.Ignored("PlayerSettings.SetScriptingDefineSymbols", "is kept for the session; scripts are not recompiled with the new defines");
            s_SetDefines[buildTarget.TargetName ?? ""] = defines ?? "";
        }

        [global::System.@ObsoleteAttribute("Use SetScriptingDefineSymbols(NamedBuildTarget buildTarget, string defines) instead")] public static void SetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup, string defines) =>
            SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);

        [global::System.@ObsoleteAttribute("Use GetApplicationIdentifier(NamedBuildTarget buildTarget) instead")] public static string GetApplicationIdentifier(BuildTargetGroup targetGroup) => ProjectInfo.Identifier;

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

    /// <summary>EditorUtility as Play mode code uses it.</summary>
    public partial class EditorUtility
    {
        public static bool IsPersistent(Object target) => target != null && target.IsPersistentAsset;
        [Unbound.EditorFaithful("no window shows a progress bar (as the batch-mode Editor)")]
        public static void DisplayProgressBar(string title, string info, float progress) { }
        [Unbound.EditorFaithful("no window shows a progress bar (as the batch-mode Editor): nobody cancels it")]
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) => false;
        [Unbound.EditorFaithful("no window shows a progress bar (as the batch-mode Editor)")]
        public static void ClearProgressBar() { }
        /// <summary>A dialog nobody can answer (no editor windows): cancelled, as the batch-mode
        /// Editor cancels them (measured).</summary>
        public static bool DisplayDialog(string title, string message, string ok, [UnityEngine.Internal.DefaultValue("\"\"")] string cancel)
        {
            Debug.LogAssertion($"Cancelling DisplayDialog: {title} {message}");
            return false;
        }
        public static bool DisplayDialog(string title, string message, string ok) => DisplayDialog(title, message, ok, "");
        public static Object InstanceIDToObject(int instanceID) => ObjectRegistry.FindById(instanceID);
        public static void UnloadUnusedAssetsImmediate() => Resources.UnloadUnusedAssetsInternal();
        public static void UnloadUnusedAssetsImmediate(bool includeMonoReferencesAsRoots) => Resources.UnloadUnusedAssetsInternal();
        public static void CopySerialized(Object source, Object dest)
        {
            if (source != null && dest != null) new SerializedCopier(o => o).CopyScriptObject(source, dest);
        }
        [Unbound.EditorFaithful("no Project window is open (as the batch-mode Editor)")]
        public static void FocusProjectWindow() { }
        public static void RevealInFinder(string path) => Stubs.Ignored("EditorUtility.RevealInFinder", "opens no file manager");
        // File dialogs nobody can answer: cancelled, as the batch-mode Editor cancels its dialogs.
        [Unbound.EditorFaithful("a dialog nobody answers is cancelled, as in the batch-mode Editor (measured for DisplayDialog: tests/projects/editorui)")]
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        [Unbound.EditorFaithful("a dialog nobody answers is cancelled, as in the batch-mode Editor (measured for DisplayDialog: tests/projects/editorui)")]
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => "";
        [Unbound.EditorFaithful("a dialog nobody answers is cancelled, as in the batch-mode Editor (measured for DisplayDialog: tests/projects/editorui)")]
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
