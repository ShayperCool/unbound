using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The application as the game sees it while playing in the Editor (Unbound is an Editor
    /// substitute): isEditor is true and the platform is the Editor's.</summary>
    public partial class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public delegate void LowMemoryCallback();
        public delegate void MemoryUsageChangedCallback(in ApplicationMemoryUsageChange usage);
        public delegate void AdvertisingIdentifierCallback(string advertisingId, bool trackingEnabled, string errorMsg);

        static LogCallback s_DeprecatedLogCallback;
        static LogCallback s_LogCallbackHandler;
        static LogCallback s_LogCallbackHandlerThreaded;
        static readonly Dictionary<LogType, StackTraceLogType> s_StackTraceLogTypes = new Dictionary<LogType, StackTraceLogType>();
        static CancellationTokenSource s_ExitCancellation = new CancellationTokenSource();

        [Obsolete("Application.RegisterLogCallback is deprecated. Use Application.logMessageReceived instead.")]
        public static void RegisterLogCallback(LogCallback handler) => SetDeprecatedLogCallback(handler, false);

        [Obsolete("Application.RegisterLogCallbackThreaded is deprecated. Use Application.logMessageReceivedThreaded instead.")]
        public static void RegisterLogCallbackThreaded(LogCallback handler) => SetDeprecatedLogCallback(handler, true);

        static void SetDeprecatedLogCallback(LogCallback handler, bool threaded)
        {
            logMessageReceived -= s_DeprecatedLogCallback;
            logMessageReceivedThreaded -= s_DeprecatedLogCallback;
            s_DeprecatedLogCallback = handler;
            if (threaded) logMessageReceivedThreaded += handler;
            else logMessageReceived += handler;
        }

        internal static bool isHumanControllingUs
        {
            get { MainThreadCheck.Check("get_isHumanControllingUs"); return UnityEditorInternal.InternalEditorUtility.isHumanControllingUs; }
        }

        public static event LogCallback logMessageReceived
        {
            add => s_LogCallbackHandler += value;
            remove => s_LogCallbackHandler -= value;
        }

        public static event LogCallback logMessageReceivedThreaded
        {
            add => s_LogCallbackHandlerThreaded += value;
            remove => s_LogCallbackHandlerThreaded -= value;
        }

        public static event LowMemoryCallback lowMemory;
        public static event MemoryUsageChangedCallback memoryUsageChanged;
        public static event Action<bool> focusChanged;
        public static event Action<string> deepLinkActivated;
        public static event Func<bool> wantsToQuit;
        public static event Action quitting;
        public static event Action unloading;
        /// <summary>Before rendering, in the order of the callbacks' BeforeRenderOrderAttribute
        /// (<see cref="BeforeRenderHelper"/>).</summary>
        public static event UnityEngine.Events.UnityAction onBeforeRender
        {
            add => BeforeRenderHelper.RegisterCallback(value);
            remove => BeforeRenderHelper.UnregisterCallback(value);
        }

        [ThreadStatic] static bool t_InLogCallback;

        internal static void Internal_InvokeLogCallbacks(string message, string stackTrace, LogType type)
        {
            if (t_InLogCallback) return; // a handler that logs must not recurse
            t_InLogCallback = true;
            try
            {
                if (PlayerLoopDriver.IsMainThread) s_LogCallbackHandler?.Invoke(message, stackTrace, type);
                s_LogCallbackHandlerThreaded?.Invoke(message, stackTrace, type);
            }
            catch (Exception e)
            {
                Native.Log((int)LogType.Exception, $"Exception in a log callback: {e}");
            }
            finally
            {
                t_InLogCallback = false;
            }
        }

        internal static void Internal_FocusChanged(bool focus)
        {
            s_IsFocused = focus;
            focusChanged?.Invoke(focus);
        }

        internal static void Internal_BeforeRender() => BeforeRenderHelper.Invoke();

        internal static void Internal_LowMemory() => lowMemory?.Invoke();

        /// <summary>Asks wantsToQuit handlers; returns false when one of them cancels.</summary>
        internal static bool Internal_WantsToQuit()
        {
            if (wantsToQuit == null) return true;
            foreach (Func<bool> handler in wantsToQuit.GetInvocationList())
            {
                try
                {
                    if (!handler()) return false;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return true;
        }

        internal static void Internal_Quitting()
        {
            try { s_ExitCancellation.Cancel(); } catch (Exception e) { Debug.LogException(e); }
            quitting?.Invoke();
            unloading?.Invoke();
        }

        internal static void Internal_DeepLink(string url)
        {
            s_AbsoluteURL = url;
            deepLinkActivated?.Invoke(url);
        }

        // ------------------------------------------------------------------ state

        public static bool isPlaying { get { MainThreadCheck.Check("get_isPlaying"); return Unbound.Runtime.EditorSession.IsPlaying; } }
        [Unbound.EditorFaithful("Unbound plays as the Editor")]
        public static bool isEditor => true;
        [Unbound.EditorFaithful("Unbound plays as the interactive Editor (-batchmode is not emulated)")]
        public static bool isBatchMode => false;
        [Unbound.EditorFaithful("the desktop Editor; measured: tests/projects/platform")]
        public static bool isMobilePlatform => false;
        [Unbound.EditorFaithful("the desktop Editor is no console")]
        public static bool isConsolePlatform => false;
        static bool s_IsFocused = true;
        public static bool isFocused => s_IsFocused;
        internal static void Internal_SetFocusState(bool value) => s_IsFocused = value;
        public static bool runInBackground { get; set; } = true;
        /// <summary>True from a LoadScene/LoadSceneAsync call (held loads too) until the scene is
        /// integrated; already false in sceneLoaded (measured: tests/projects/appfacts).</summary>
        [global::System.@ObsoleteAttribute("This property is deprecated, please use LoadLevelAsync to detect if a specific scene is currently loading.")] public static bool isLoadingLevel => SceneManagement.SceneManagerInternal.IsLoading;
        [Unbound.EditorFaithful("the Editor is not a tampered player build")]
        public static bool genuine => true;
        [Unbound.EditorFaithful("the genuine check exists in player builds only")]
        public static bool genuineCheckAvailable => false;
        /// <summary>The Editor of the machine's OS (the Editor exists for macOS, Windows and Linux).</summary>
        public static RuntimePlatform platform =>
            OperatingSystem.IsMacOS() ? RuntimePlatform.OSXEditor :
            OperatingSystem.IsWindows() ? RuntimePlatform.WindowsEditor : RuntimePlatform.LinuxEditor;
        public static string unityVersion => ProjectInfo.UnityVersion;
        public static string version { get { MainThreadCheck.Check("get_version"); return ProjectInfo.BundleVersion; } }
        public static string productName { get { MainThreadCheck.Check("get_productName"); return ProjectInfo.ProductName; } }
        public static string companyName => ProjectInfo.CompanyName;
        public static string identifier => ProjectInfo.Identifier;
        [Unbound.EditorFaithful("the Editor was not installed from a store: no installer name")]
        public static string installerName => "";
        public static ApplicationInstallMode installMode => ApplicationInstallMode.Editor;
        public static ApplicationSandboxType sandboxType => ApplicationSandboxType.NotSandboxed;
        public static string cloudProjectId => ProjectInfo.CloudProjectId;
        [Unbound.EditorFaithful("the Editor has no build: 32 zeros (measured: tests/projects/appfacts)")]
        public static string buildGUID => "00000000000000000000000000000000";
        static string s_AbsoluteURL = "";
        public static string absoluteURL => s_AbsoluteURL;
        public static SystemLanguage systemLanguage => Unbound.Runtime.SystemLocale.Language;
        public static NetworkReachability internetReachability => NetworkReachability.ReachableViaLocalAreaNetwork;
        static ThreadPriority s_BackgroundLoadingPriority = ThreadPriority.BelowNormal;
        /// <summary>The loader's priority, which sets the per-frame integration limit of the
        /// scene-operation queue. BelowNormal by default (Scripting API 6000.0). The Linux Editor
        /// lowers its loading thread for that default and cannot raise it again: Normal and High,
        /// and any value from Normal up, read back as Low and take Low's limit (measured:
        /// tests/projects/editorcallbacks --queue-budget, one machine and user, not allowed to
        /// raise thread priorities). Other platforms keep the value set (not measured).</summary>
        public static ThreadPriority backgroundLoadingPriority
        {
            get => s_BackgroundLoadingPriority;
            set => s_BackgroundLoadingPriority = Compat.SceneAsyncPreparationMeasured && OperatingSystem.IsLinux()
                && (int)value >= (int)ThreadPriority.Normal ? ThreadPriority.Low : value;
        }
        static int s_TargetFrameRate = -1;
        public static int targetFrameRate
        {
            get { MainThreadCheck.Check("get_targetFrameRate"); return s_TargetFrameRate; }
            set { MainThreadCheck.Check("set_targetFrameRate"); s_TargetFrameRate = value; }
        }
        public static CancellationToken exitCancellationToken => s_ExitCancellation.Token;

        public static string dataPath => ProjectInfo.AssetsPath;
        public static string streamingAssetsPath => Path.Combine(ProjectInfo.AssetsPath, "StreamingAssets");
        /// <summary>As the Editor has it: the system's temporary directory, then
        /// <c>&lt;company&gt;/&lt;product&gt;</c>, created when asked for.</summary>
        public static string temporaryCachePath
        {
            get
            {
                MainThreadCheck.Check("get_temporaryCachePath");
                var path = ProjectInfo.TemporaryCachePath;
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception) { }
                return path;
            }
        }
        /// <summary>The log file the session writes, as the Editor answers its own log: the
        /// command line's <c>-logFile</c> when given (measured: tests/projects/appfacts), else
        /// <c>LibraryUnbound/Editor.log</c> in the project (Unbound's stand-in for Editor.log).</summary>
        public static string consoleLogPath => Unbound.Runtime.ConsoleLog.Path;

        /// <summary>As the Editor computes it: <c>~/.config/unity3d/&lt;company&gt;/&lt;product&gt;</c> on Linux,
        /// <c>~/Library/Application Support/&lt;company&gt;/&lt;product&gt;</c> on macOS.</summary>
        public static string persistentDataPath { get { MainThreadCheck.Check("get_persistentDataPath"); return ProjectInfo.PersistentDataPath; } }

        [global::System.@ObsoleteAttribute("Use SceneManager to determine what scenes have been loaded")]
        public static int loadedLevel => SceneManagement.SceneManager.GetActiveScene().buildIndex;

        [global::System.@ObsoleteAttribute("Use SceneManager to determine what scenes have been loaded")]
        public static string loadedLevelName => SceneManagement.SceneManager.GetActiveScene().name;

        public static void Quit() => Quit(0);

        /// <summary>The Editor ignores it in Play mode: the game goes on, no wantsToQuit or
        /// quitting (measured: tests/projects/messages). Edit mode code (`unbound test`) quits.</summary>
        public static void Quit(int exitCode)
        {
            if (Unbound.Runtime.EditorSession.IsPlaying) return;
            PlayerLoopDriver.RequestQuit(exitCode);
        }

        public static void Unload() => Quit(0);

        /// <summary>As the Editor in Play mode: the URL, whatever it is (empty for null), goes to the
        /// desktop's opener, <c>xdg-open</c> on Linux, <c>open</c> on macOS, without a message
        /// (measured: tests/projects/appfacts; <see cref="DesktopOpener"/>).</summary>
        public static void OpenURL(string url) => DesktopOpener.Open(url ?? "", "Application.OpenURL");

        public static bool CanStreamedLevelBeLoaded(int levelIndex) => levelIndex >= 0 && levelIndex < ProjectInfo.BuildScenes.Count;

        public static bool CanStreamedLevelBeLoaded(string levelName) => SceneManagement.SceneManagerInternal.FindBuildScene(levelName) >= 0;

        [global::System.@ObsoleteAttribute("Streaming was a Unity Web Player feature, and is removed. This function is deprecated and always returns 1.0 for valid level indices.")] public static float GetStreamProgressForLevel(int levelIndex) => CanStreamedLevelBeLoaded(levelIndex) ? 1f : 0f;

        [global::System.@ObsoleteAttribute("Streaming was a Unity Web Player feature, and is removed. This function is deprecated and always returns 1.0.")] [Unbound.EditorFaithful("obsolete: Unity always returns 1 (streaming was a Web Player feature)")]
        public static float GetStreamProgressForLevel(string levelName) => 1f;

        public static StackTraceLogType GetStackTraceLogType(LogType logType) =>
            s_StackTraceLogTypes.TryGetValue(logType, out var value) ? value : StackTraceLogType.ScriptOnly;

        public static void SetStackTraceLogType(LogType logType, StackTraceLogType stackTraceType) =>
            s_StackTraceLogTypes[logType] = stackTraceType;

        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool RequestAdvertisingIdentifierAsync(AdvertisingIdentifierCallback delegateMethod) => false;

        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool HasUserAuthorization(UserAuthorization mode) => true;

        public static AsyncOperation RequestUserAuthorization(UserAuthorization mode) => AsyncOperation.Completed();

        /// <summary>From the Editor licence on the machine: false for Unity Personal (measured:
        /// tests/projects/appfacts).</summary>
        public static bool HasProLicense() => Unbound.Runtime.EditorLicense.HasPro;

        [global::System.@ObsoleteAttribute("Application.ExternalCall is deprecated. See https://docs.unity3d.com/Manual/webgl-interactingwithbrowserscripting.html for alternatives.")] [Unbound.EditorFaithful("obsolete WebGL browser call: nothing to call outside a web player")]
        public static void ExternalCall(string functionName, params object[] args) { }

        [global::System.@ObsoleteAttribute("Application.ExternalEval is deprecated. See https://docs.unity3d.com/Manual/webgl-interactingwithbrowserscripting.html for alternatives.")] [Unbound.EditorFaithful("obsolete WebGL browser call: nothing to call outside a web player")]
        public static void ExternalEval(string script) { }

        [global::System.@ObsoleteAttribute("Application.GetBuildTags is no longer supported and will be removed.", false)] [Unbound.EditorFaithful("obsolete: \"no longer supported\"; a project has no build tags")]
        public static string[] GetBuildTags() => Array.Empty<string>();

        [global::System.@ObsoleteAttribute("Application.SetBuildTags is no longer supported and will be removed.", false)] [Unbound.EditorFaithful("obsolete: \"no longer supported and will be removed\"")]
        public static void SetBuildTags(string[] buildTags) { }

        public static bool IsPlaying(Object obj) => obj != null && !obj.IsPersistentAsset;
    }

    /// <summary>Facts about the project being played, set by the host at start.</summary>
    internal static class ProjectInfo
    {
        /// <summary>The project's release (its ProjectSettings/ProjectVersion.txt), which the host
        /// always sets: Unbound opens no project without one. Until then, in the tools that run
        /// without a game, the release the 6000.0 family's answers were measured on.</summary>
        internal static string UnityVersion = Unbound.Runtime.Compat.Measured6000_0;
        /// <summary>The release's build revision (as `907bc2d768b5`), "" when the host does not know
        /// it (its installation, or the project's version file of the same release).</summary>
        internal static string UnityRevision = "";
        /// <summary>The compatibility profile's behaviour switches this release has (unbound-compat's
        /// Feature names); empty when the host did not say, as in unit tests.</summary>
        internal static readonly HashSet<string> Features = new HashSet<string>(StringComparer.Ordinal);
        internal static string ProjectPath = "";

        /// <summary>The project's own folder, as the host resolved it ("" where no host runs,
        /// as in the managed tests): <see cref="ProjectPath"/> is the view the game runs in.</summary>
        internal static string ProjectRootPath = "";

        /// <summary>Unbound's directory for the project, as the host resolved it from the
        /// command line's <c>--project-data</c> or <c>UNBOUND_PROJECT_DATA</c> ("" where no host
        /// runs): the managed side asks no environment for it.</summary>
        internal static string UnboundDirectoryPath = "";

        /// <summary>The name of Unbound's own directory in a project's root, where nothing else
        /// is asked for: everything Unbound keeps per project (caches, the project view the game
        /// runs in, logs, results). <c>Library/</c> is Unity's: Unbound keeps nothing there.</summary>
        internal const string UnboundFolder = "LibraryUnbound";

        /// <summary>The name of the project view in Unbound's directory (the game's working
        /// directory: links to the project's entries and packages).</summary>
        internal const string ViewFolder = "ProjectView";

        /// <summary>Unbound's directory for a project path, the one place that names it: what
        /// the host resolved for the running project; without a host, or for another path,
        /// the default place (the path is the project's root, or its view, which is in that
        /// directory).</summary>
        internal static string UnboundDirectory(string projectPath)
        {
            if (UnboundDirectoryPath.Length > 0 && projectPath == ProjectPath) return UnboundDirectoryPath;
            string trimmed = Path.TrimEndingDirectorySeparator(projectPath ?? "");
            string parent = Path.GetDirectoryName(trimmed);
            if (parent != null && Path.GetFileName(trimmed) == ViewFolder && Path.GetFileName(parent) == UnboundFolder) return parent;
            return Path.Combine(trimmed, UnboundFolder);
        }

        /// <summary>A path in the running project's Unbound directory.</summary>
        internal static string UnboundPath(params string[] parts) => Path.Combine(UnboundDirectory(ProjectPath), Path.Combine(parts));
        internal static string AssetsPath = "";
        internal static string ProductName = "";
        internal static string CompanyName = "";
        internal static string Identifier = "";
        internal static string BundleVersion = "1.0";
        internal static string CloudProjectId = "";
        internal static string TemporaryCachePath => Path.Combine(Path.GetTempPath(), CompanyName, ProductName);
        internal static string PersistentDataPath = "";
        /// <summary>A PlayerPrefs file the run was given instead of the Editor's (null: none).</summary>
        internal static string PlayerPrefsPath;
        internal static string[] Defines = Array.Empty<string>();
        /// <summary>Every build target group's scripting defines, by the key ProjectSettings stores them
        /// under (a named target's name), with those scripts set earlier in the run.</summary>
        internal static Dictionary<string, string[]> DefinesByGroup = new Dictionary<string, string[]>(StringComparer.Ordinal);
        internal static readonly List<(string Path, string Guid)> BuildScenes = new List<(string, string)>();
    }









    public partial struct ApplicationMemoryUsageChange
    {
        public ApplicationMemoryUsage memoryUsage { get; private set; }
        public ApplicationMemoryUsageChange(ApplicationMemoryUsage usage) { memoryUsage = usage; }
    }
}
