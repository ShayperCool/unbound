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

        static LogCallback s_LogCallbackHandler;
        static LogCallback s_LogCallbackHandlerThreaded;
        static readonly Dictionary<LogType, StackTraceLogType> s_StackTraceLogTypes = new Dictionary<LogType, StackTraceLogType>();
        static CancellationTokenSource s_ExitCancellation = new CancellationTokenSource();

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
        public static event UnityEngine.Events.UnityAction onBeforeRender;

        [ThreadStatic] static bool t_InLogCallback;

        internal static void Internal_InvokeLogCallbacks(string message, string stackTrace, LogType type)
        {
            if (t_InLogCallback) return; // a handler that logs must not recurse
            t_InLogCallback = true;
            try
            {
                s_LogCallbackHandlerThreaded?.Invoke(message, stackTrace, type);
                if (PlayerLoopDriver.IsMainThread) s_LogCallbackHandler?.Invoke(message, stackTrace, type);
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
            isFocused = focus;
            focusChanged?.Invoke(focus);
        }

        internal static void Internal_BeforeRender() => onBeforeRender?.Invoke();

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
            absoluteURL = url;
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
        public static bool isFocused { get; internal set; } = true;
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
        public static string absoluteURL { get; private set; } = "";
        public static SystemLanguage systemLanguage => Unbound.Runtime.SystemLocale.Language;
        public static NetworkReachability internetReachability => NetworkReachability.ReachableViaLocalAreaNetwork;
        public static ThreadPriority backgroundLoadingPriority { get; set; } = ThreadPriority.Normal;
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
        /// <c>Library/Unbound/Editor.log</c> in the project (Unbound's stand-in for Editor.log).</summary>
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
        /// (measured: tests/projects/appfacts). <c>UNBOUND_OPEN_URL</c> names another program, so
        /// tests never start a browser.</summary>
        public static void OpenURL(string url)
        {
            string opener = Environment.GetEnvironmentVariable("UNBOUND_OPEN_URL");
            if (string.IsNullOrEmpty(opener)) opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(opener) { UseShellExecute = false };
                start.ArgumentList.Add(url ?? "");
                System.Diagnostics.Process.Start(start)?.Dispose();
            }
            catch (Exception e)
            {
                Native.Log((int)LogType.Warning, $"{Unbound.Runtime.Log.DiagnosticPrefix}Application.OpenURL could not run {opener}: {e.Message}");
            }
        }

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
        internal static string UnityVersion = "6000.0.0f1";
        /// <summary>The compatibility profile's behaviour switches this release has (unbound-compat's
        /// Feature names); empty when the host did not say, as in unit tests.</summary>
        internal static readonly HashSet<string> Features = new HashSet<string>(StringComparer.Ordinal);
        internal static string ProjectPath = "";
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
        internal static readonly List<(string Path, string Guid)> BuildScenes = new List<(string, string)>();
    }









    public readonly partial struct ApplicationMemoryUsageChange
    {
        public ApplicationMemoryUsage memoryUsage { get; }
        public ApplicationMemoryUsageChange(ApplicationMemoryUsage usage) { memoryUsage = usage; }
    }
}
