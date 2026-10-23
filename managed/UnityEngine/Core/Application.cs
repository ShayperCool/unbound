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

        public static bool isPlaying => true;
        public static bool isEditor => true;
        public static bool isBatchMode => false;
        public static bool isMobilePlatform => false;
        public static bool isConsolePlatform => false;
        public static bool isFocused { get; internal set; } = true;
        public static bool runInBackground { get; set; } = true;
        public static bool isLoadingLevel => false;
        public static bool genuine => true;
        public static bool genuineCheckAvailable => false;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static string unityVersion => ProjectInfo.UnityVersion;
        public static string version => ProjectInfo.BundleVersion;
        public static string productName => ProjectInfo.ProductName;
        public static string companyName => ProjectInfo.CompanyName;
        public static string identifier => ProjectInfo.Identifier;
        public static string installerName => "";
        public static ApplicationInstallMode installMode => ApplicationInstallMode.Editor;
        public static ApplicationSandboxType sandboxType => ApplicationSandboxType.NotSandboxed;
        public static string cloudProjectId => ProjectInfo.CloudProjectId;
        public static string buildGUID => "";
        public static string absoluteURL { get; private set; } = "";
        public static SystemLanguage systemLanguage => Unbound.Runtime.SystemLocale.Language;
        public static NetworkReachability internetReachability => NetworkReachability.ReachableViaLocalAreaNetwork;
        public static ThreadPriority backgroundLoadingPriority { get; set; } = ThreadPriority.Normal;
        public static int targetFrameRate { get; set; } = -1;
        public static CancellationToken exitCancellationToken => s_ExitCancellation.Token;

        public static string dataPath => ProjectInfo.AssetsPath;
        public static string streamingAssetsPath => Path.Combine(ProjectInfo.AssetsPath, "StreamingAssets");
        public static string temporaryCachePath => ProjectInfo.TemporaryCachePath;
        public static string consoleLogPath => "";

        /// <summary>As the Editor computes it on Linux: <c>~/.config/unity3d/&lt;company&gt;/&lt;product&gt;</c>.</summary>
        public static string persistentDataPath => ProjectInfo.PersistentDataPath;

        [Obsolete("This property is deprecated, please use LoadLevelAsync to detect if a specific scene is currently loading.")]
        public static int loadedLevel => SceneManagement.SceneManager.GetActiveScene().buildIndex;

        [Obsolete("Use SceneManager.GetActiveScene instead.")]
        public static string loadedLevelName => SceneManagement.SceneManager.GetActiveScene().name;

        public static void Quit() => Quit(0);

        public static void Quit(int exitCode) => PlayerLoopDriver.RequestQuit(exitCode);

        public static void Unload() => Quit(0);

        public static void OpenURL(string url) => Native.Log((int)LogType.Log, $"[Unbound] Application.OpenURL({url}) ignored");

        public static bool CanStreamedLevelBeLoaded(int levelIndex) => levelIndex >= 0 && levelIndex < ProjectInfo.BuildScenes.Count;

        public static bool CanStreamedLevelBeLoaded(string levelName) => SceneManagement.SceneManagerInternal.FindBuildScene(levelName) >= 0;

        public static float GetStreamProgressForLevel(int levelIndex) => 1f;

        public static float GetStreamProgressForLevel(string levelName) => 1f;

        public static StackTraceLogType GetStackTraceLogType(LogType logType) =>
            s_StackTraceLogTypes.TryGetValue(logType, out var value) ? value : StackTraceLogType.ScriptOnly;

        public static void SetStackTraceLogType(LogType logType, StackTraceLogType stackTraceType) =>
            s_StackTraceLogTypes[logType] = stackTraceType;

        public static bool RequestAdvertisingIdentifierAsync(AdvertisingIdentifierCallback delegateMethod) => false;

        public static bool HasUserAuthorization(UserAuthorization mode) => true;

        public static AsyncOperation RequestUserAuthorization(UserAuthorization mode) => AsyncOperation.Completed();

        public static bool HasProLicense() => true;

        public static void ExternalCall(string functionName, params object[] args) { }

        public static void ExternalEval(string script) { }

        public static string[] GetBuildTags() => Array.Empty<string>();

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
        internal static string TemporaryCachePath = Path.Combine(Path.GetTempPath(), "unbound");
        internal static string PersistentDataPath = "";
        internal static string[] Defines = Array.Empty<string>();
        internal static readonly List<(string Path, string Guid)> BuildScenes = new List<(string, string)>();
    }









    public readonly partial struct ApplicationMemoryUsageChange
    {
        public ApplicationMemoryUsage memoryUsage { get; }
        public ApplicationMemoryUsageChange(ApplicationMemoryUsage usage) { memoryUsage = usage; }
    }
}
