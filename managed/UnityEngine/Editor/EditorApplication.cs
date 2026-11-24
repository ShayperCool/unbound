using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The Editor application as Play mode code sees it: always playing; tick, update and
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
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerSendFrameComplete), RunFrameEditorCallbacks);
        }

        internal static void EnsureRegistered() { }

        /// <summary>The Editor's own per-update work (selection notifications, undo flushes),
        /// before the callbacks editor code registers.</summary>
        internal static Action InternalUpdate;

        /// <summary>The Editor's update, run from an Edit mode frame.</summary>
        internal static void RunEditorCallbacksNow() => RunEditorCallbacks();

        /// <summary>The Editor's update in a frame's loop. The first Play mode frame runs it after
        /// EnteredPlayMode instead, at the frame's end (Unity 6000.0.71f1, tests/projects/toolapis
        /// playRequest.seen).</summary>
        static void RunFrameEditorCallbacks()
        {
            if (PlayerLoopDriver.IsPlayModeEntryFrame) return;
            RunEditorCallbacks();
        }

        /// <summary>The Editor's internal per-update event: each of its updates raises it before
        /// update and delayCall (matches the Unity 6000.0.71f1 Editor, tests/projects/editorhooks).
        /// A field-like event, so threads may add to it (CallDelayed from a worker thread). A
        /// handler that throws is logged and the others still run, as for update (Unbound's
        /// choice: the Editor's own call stops at the first throw).</summary>
        internal static event CallbackFunction tick;

        /// <summary>Runs <paramref name="action"/> on the main thread from the first Editor update
        /// at least <paramref name="delaySeconds"/> after the call (0: the next update's tick);
        /// the returned action cancels it. Callable from any thread.</summary>
        internal static Action CallDelayed(CallbackFunction action, double delaySeconds = 0d)
        {
            var start = DateTime.UtcNow;
            CallbackFunction delayed = null;
            delayed = () =>
            {
                if ((DateTime.UtcNow - start).TotalSeconds < delaySeconds) return;
                tick -= delayed;
                action();
            };
            tick += delayed;
            if (delaySeconds == 0d) SignalTick();
            return () => tick -= delayed;
        }

        /// <summary>Wakes an idle Editor for its next tick; Unbound's frames never idle.</summary>
        [Unbound.EditorFaithful("Unbound runs its Editor updates every frame, so nothing waits for a signal")]
        internal static void SignalTick() { }

        static void RunEditorCallbacks()
        {
            // Each Editor update starts a frame: prefab instances take the changes made by script
            // before it (scenesave modification-timing *.frame).
            if (!isPlaying) Unbound.Runtime.PrefabRecordTiming.FrameBoundary();
            Undo.FlushDirtiness();
            ObjectChangeEvents.Publish();
            try { InternalUpdate?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            var ticks = tick;
            if (ticks != null)
            {
                foreach (CallbackFunction call in ticks.GetInvocationList())
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
            // delayCall runs after update: one registered by an update handler runs in the same
            // update (matches the Unity 6000.0.71f1 Editor, tests/projects/editorhooks frame1).
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

        /// <summary>Runs a registered script or dynamic menu command after validation.</summary>
        public static bool ExecuteMenuItem(string menuItemPath)
        {
            MainThreadCheck.Check("ExecuteMenuItemInternal");
            try
            {
                if (Menu.TryExecute(menuItemPath, out bool result)) return result;
            }
            catch (System.Reflection.TargetInvocationException error)
            {
                Debug.LogException(error.InnerException ?? error);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                return true;
            }
            Stubs.Ignored("EditorApplication.ExecuteMenuItem", "runs registered script and dynamic commands; other built-in commands are not available");
            return false;
        }

        static int s_ReloadLocks;
        static bool s_ReloadPending, s_ReloadPendingOptimized;

        /// <summary>The session is an Edit-mode one (<c>unbound test</c>): the Test Framework holds the
        /// reload lock for its whole run and expects a reload to resume it, which a restarted session
        /// cannot.</summary>
        internal static bool s_EditModeSession;

        /// <summary>The reload a locked session deferred (<paramref name="optimized"/>: an edit it
        /// could not patch because the code was compiled with optimizations): beforeAssemblyReload,
        /// then the session restarts (Unbound's reload of the scripts). Unbound's choices
        /// (unmeasured in the Editor): an Edit-mode session goes on with the loaded scripts, so a
        /// test run ends with its results; a session that is quitting does not restart. Tests
        /// replace it.</summary>
        internal static Action<bool> s_DeferredReload = optimized =>
        {
            if (s_EditModeSession)
            {
                Debug.LogWarning("Unbound: the scripts asked for a reload during an Edit-mode session (unbound test), which does not reload them; the run goes on with the loaded scripts");
                return;
            }
            if (PlayerLoopDriver.QuitRequested) return;
            AssemblyReloadEvents.RaiseBefore();
            Native.Call("script-reload", optimized ? "{\"optimized\":true}" : "{}");
        };

        /// <summary>No assembly reloads until as many unlocks: a compilation still compiles and
        /// raises its events, its reload waits for the last unlock (the Test Framework holds the lock
        /// during an Edit-mode run).</summary>
        public static void LockReloadAssemblies() => s_ReloadLocks++;

        public static void UnlockReloadAssemblies()
        {
            if (s_ReloadLocks > 0) s_ReloadLocks--;
            if (s_ReloadLocks != 0 || !s_ReloadPending) return;
            bool optimized = s_ReloadPendingOptimized;
            s_ReloadPending = s_ReloadPendingOptimized = false;
            s_DeferredReload(optimized);
        }

        /// <summary>While the reload is locked, remembers a reload asked for, for the last unlock,
        /// and answers true; else false (the caller reloads now).</summary>
        internal static bool TryDeferReload(bool optimized)
        {
            if (s_ReloadLocks == 0) return false;
            s_ReloadPending = true;
            s_ReloadPendingOptimized |= optimized;
            return true;
        }
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
        /// <summary>The project's names: in effect at once, saved into ProjectSettings.asset as the
        /// Editor saves settings (SettingsAssets.Edit).</summary>
        public static string companyName
        {
            get => ProjectInfo.CompanyName;
            set
            {
                ProjectInfo.CompanyName = value;
                SettingsAssets.Edit("ProjectSettings", body => body.Set("companyName", YText.Scalar(UnityYamlWriter.Scalar(value ?? ""))));
                RefreshApplicationIdentifier();
            }
        }

        public static string productName
        {
            get => ProjectInfo.ProductName;
            set
            {
                ProjectInfo.ProductName = value;
                SettingsAssets.Edit("ProjectSettings", body => body.Set("productName", YText.Scalar(UnityYamlWriter.Scalar(value ?? ""))));
                RefreshApplicationIdentifier();
            }
        }

        /// <summary>The project's GUID (ProjectSettings productGUID, hex in Unity's order: the low
        /// nibble of each byte first) (measured: tests/projects/editorload player.product_guid).</summary>
        public static Guid productGUID => s_ProductGuid;
        internal static Guid s_ProductGuid;

        internal static Guid ParseProductGuid(string hex)
        {
            if (hex == null || hex.Length != 32) return Guid.Empty;
            Span<byte> bytes = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                if (!Uri.IsHexDigit(hex[2 * i]) || !Uri.IsHexDigit(hex[2 * i + 1])) return Guid.Empty;
                bytes[i] = (byte)(Uri.FromHex(hex[2 * i + 1]) << 4 | Uri.FromHex(hex[2 * i]));
            }
            return new Guid(bytes);
        }
        public static string bundleVersion
        {
            get => ProjectInfo.BundleVersion;
            set
            {
                ProjectInfo.BundleVersion = value;
                if (Compat.ProjectBuildIdentityMeasured) Save("bundleVersion", value);
            }
        }
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
        internal static Object[] s_Preloaded;

        /// <summary>The assets PlayerSettings preloads (loaded on first use). A new list is saved into
        /// ProjectSettings.asset as the Editor saves settings: by SaveAssets and at the Editor's exit
        /// in Edit mode, as references (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/scenesave settings-writes).</summary>
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

        public static void SetPreloadedAssets(Object[] assets)
        {
            var preloaded = (Object[])(assets ?? Array.Empty<Object>()).Clone();
            s_Preloaded = preloaded;
            SettingsAssets.Edit("ProjectSettings", body =>
            {
                var writer = new UnityYamlWriter.Writer(new System.Text.StringBuilder(), new Dictionary<Object, long>());
                var list = YText.Seq();
                foreach (var asset in preloaded) list.Items.Add(SceneFiles.FlowValue(writer.ReferenceText(asset)));
                list.Flow = list.Items.Count == 0;
                body.Set("preloadedAssets", list);
            });
        }

        // Defines set in this session, by named target, as given.
        static readonly Dictionary<string, string> s_SetDefines = new Dictionary<string, string>();

        /// <summary>A target's scripting defines: as set in this session, else the project's (with the
        /// ones scripts set in an earlier session of this run).</summary>
        public static string GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget)
        {
            string name = buildTarget.TargetName ?? "";
            if (s_SetDefines.TryGetValue(name, out var set)) return set;
            return ProjectInfo.DefinesByGroup.TryGetValue(name, out var defines) ? string.Join(";", defines) : "";
        }

        [global::System.@ObsoleteAttribute("Use GetScriptingDefineSymbols(NamedBuildTarget buildTarget) instead")] public static string GetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup) =>
            GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup));

        /// <summary>A target's scripting defines (saved into ProjectSettings.asset in Edit mode as
        /// the Editor saves settings; a restarted session keeps them). New defines for the active target compile the scripts with them and
        /// reload them between frames; another target's, or the same defines again, compile nothing
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/compileevents).</summary>
        public static void SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget buildTarget, string defines)
        {
            string name = buildTarget.TargetName ?? "";
            string before = GetScriptingDefineSymbols(buildTarget);
            s_SetDefines[name] = defines ?? "";
            // Saved into ProjectSettings.asset as the Editor saves settings (SettingsAssets.Edit).
            SettingsAssets.Edit("ProjectSettings", body =>
            {
                var map = body.Get("scriptingDefineSymbols");
                if (map == null || map.Kind != YKind.Map) return;
                map.Flow = false;
                map.Raw = null;
                SettingsAssets.SetInOrdinalOrder(map, name, YText.Scalar(UnityYamlWriter.Scalar(defines ?? "")));
            });
            Native.Call("scripting-defines", System.Text.Json.JsonSerializer.Serialize(new { group = name, defines = defines ?? "" }));
            bool active = name == UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettingsInfo.ActiveGroup).TargetName;
            if (active && !SameDefines(before, defines)) Native.Call("request-script-compilation", "{}");
        }

        /// <summary>Whether two define lists hold the same defines in the same order (separated by
        /// ';', trimmed, empty ones dropped, as compilations read them).</summary>
        static bool SameDefines(string a, string b)
        {
            static IEnumerable<string> Defines(string s) =>
                (s ?? "").Split(';').Select(d => d.Trim()).Where(d => d.Length != 0);
            return Defines(a).SequenceEqual(Defines(b), StringComparer.Ordinal);
        }

        [global::System.@ObsoleteAttribute("Use SetScriptingDefineSymbols(NamedBuildTarget buildTarget, string defines) instead")] public static void SetScriptingDefineSymbolsForGroup(BuildTargetGroup targetGroup, string defines) =>
            SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);

        public partial class Android
        {
            static int s_BundleVersionCode = 1;

            public static int bundleVersionCode
            {
                get => Compat.ProjectBuildIdentityMeasured
                    ? StoredInt("AndroidBundleVersionCode", s_BundleVersionCode)
                    : s_BundleVersionCode;
                set
                {
                    s_BundleVersionCode = value;
                    if (Compat.ProjectBuildIdentityMeasured) Store("AndroidBundleVersionCode", value);
                }
            }
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
        public static Object InstanceIDToObject(int instanceID) => AssetDatabaseRuntime.Reloaded(ObjectRegistry.FindById(instanceID));
        public static void UnloadUnusedAssetsImmediate() => Resources.UnloadUnusedAssetsInternal();
        public static void UnloadUnusedAssetsImmediate(bool includeMonoReferencesAsRoots) => Resources.UnloadUnusedAssetsInternal();
        public static void CopySerialized(Object source, Object dest)
        {
            if (source != null && dest != null) new SerializedCopier(o => o).CopyScriptObject(source, dest);
        }
        [Unbound.EditorFaithful("no Project window is open (as the batch-mode Editor)")]
        public static void FocusProjectWindow() { }
        public static void RevealInFinder(string path) => Stubs.Ignored("EditorUtility.RevealInFinder", "opens no file manager");
    }
}

namespace UnityEditor.Build
{
    public readonly partial struct NamedBuildTarget : IEquatable<NamedBuildTarget>, IComparable<NamedBuildTarget>
    {
        public string TargetName { get; }

        internal NamedBuildTarget(string name) { TargetName = name; }

        public static readonly NamedBuildTarget Unknown = new NamedBuildTarget("");
        public static readonly NamedBuildTarget Standalone = new NamedBuildTarget("Standalone");
        public static readonly NamedBuildTarget Server = new NamedBuildTarget("Server");
        public static readonly NamedBuildTarget iOS = new NamedBuildTarget("iPhone");
        public static readonly NamedBuildTarget Android = new NamedBuildTarget("Android");
        public static readonly NamedBuildTarget WebGL = new NamedBuildTarget("WebGL");
        public static readonly NamedBuildTarget WindowsStoreApps = new NamedBuildTarget("Windows Store Apps");
        public static readonly NamedBuildTarget PS4 = new NamedBuildTarget("PS4");
        public static readonly NamedBuildTarget PS5 = new NamedBuildTarget("PS5");
        public static readonly NamedBuildTarget XboxOne = new NamedBuildTarget("XboxOne");
        public static readonly NamedBuildTarget tvOS = new NamedBuildTarget("tvOS");
        public static readonly NamedBuildTarget VisionOS = new NamedBuildTarget("VisionOS");
        public static readonly NamedBuildTarget NintendoSwitch = new NamedBuildTarget("Nintendo Switch");
        public static readonly NamedBuildTarget NintendoSwitch2 = new NamedBuildTarget("Nintendo Switch 2");
        [global::System.@ObsoleteAttribute("Stadia has been removed in 2023.1")]
        public static readonly NamedBuildTarget Stadia = new NamedBuildTarget("Stadia");
        public static readonly NamedBuildTarget LinuxHeadlessSimulation = new NamedBuildTarget("LinuxHeadlessSimulation");
        [global::System.@ObsoleteAttribute("CloudRendering is deprecated, please use LinuxHeadlessSimulation (UnityUpgradable) -> LinuxHeadlessSimulation", false)]
        public static readonly NamedBuildTarget CloudRendering = LinuxHeadlessSimulation;
        public static readonly NamedBuildTarget EmbeddedLinux = new NamedBuildTarget("EmbeddedLinux");
        public static readonly NamedBuildTarget QNX = new NamedBuildTarget("QNX");

        /// <summary>The release's named target of each platform group; other groups have none.</summary>
        public static NamedBuildTarget FromBuildTargetGroup(BuildTargetGroup buildTargetGroup) => buildTargetGroup switch
        {
            BuildTargetGroup.Unknown => Unknown,
            BuildTargetGroup.Standalone => Standalone,
            BuildTargetGroup.iOS => iOS,
            BuildTargetGroup.Android => Android,
            BuildTargetGroup.WebGL => WebGL,
            BuildTargetGroup.WSA => WindowsStoreApps,
            BuildTargetGroup.PS4 => PS4,
            BuildTargetGroup.XboxOne => XboxOne,
            BuildTargetGroup.tvOS => tvOS,
            BuildTargetGroup.VisionOS => VisionOS,
            BuildTargetGroup.Switch => NintendoSwitch,
            BuildTargetGroup.Switch2 => NintendoSwitch2,
            BuildTargetGroup.LinuxHeadlessSimulation => LinuxHeadlessSimulation,
            BuildTargetGroup.EmbeddedLinux => EmbeddedLinux,
            BuildTargetGroup.QNX => QNX,
            BuildTargetGroup.GameCoreXboxSeries => new NamedBuildTarget("GameCoreScarlett"),
            BuildTargetGroup.GameCoreXboxOne => new NamedBuildTarget("GameCoreXboxOne"),
            BuildTargetGroup.PS5 => PS5,
            BuildTargetGroup.Kepler => new NamedBuildTarget("Kepler"),
            _ => throw new ArgumentException($"There is no a valid NamedBuildTarget for BuildTargetGroup '{buildTargetGroup}'"),
        };

        /// <summary>The platform group a named target builds for: Server's is Standalone, every
        /// other target's the group <see cref="FromBuildTargetGroup"/> names it by.</summary>
        public BuildTargetGroup ToBuildTargetGroup()
        {
            if (TargetName == Server.TargetName) return BuildTargetGroup.Standalone;
            switch (TargetName)
            {
                case "": return BuildTargetGroup.Unknown;
                case "iPhone": return BuildTargetGroup.iOS;
                case "Windows Store Apps": return BuildTargetGroup.WSA;
                case "Nintendo Switch": return BuildTargetGroup.Switch;
                case "Nintendo Switch 2": return BuildTargetGroup.Switch2;
                case "GameCoreScarlett": return BuildTargetGroup.GameCoreXboxSeries;
            }
            return Enum.TryParse<BuildTargetGroup>(TargetName, out var g) ? g : BuildTargetGroup.Unknown;
        }

        public bool Equals(NamedBuildTarget other) => TargetName == other.TargetName;
        public override bool Equals(object obj) => obj is NamedBuildTarget other && Equals(other);
        public override int GetHashCode() => TargetName.GetHashCode();
        public int CompareTo(NamedBuildTarget other) => TargetName.CompareTo(other.TargetName);
        public static bool operator ==(NamedBuildTarget lhs, NamedBuildTarget rhs) => lhs.Equals(rhs);
        public static bool operator !=(NamedBuildTarget lhs, NamedBuildTarget rhs) => !lhs.Equals(rhs);
    }
}
