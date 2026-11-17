using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Unbound.Runtime;

namespace UnityEngine.Analytics
{
    /// <summary>
    /// The analytics session of the Editor with Unity services not logged in: each Play mode starts a
    /// session, counted and identified in the project's preferences (<c>unity.player_session_count</c>,
    /// <c>unity.player_sessionid</c>, and the user id <c>unity.cloud_userid</c>, made once as 32
    /// lowercase hex digits), with no identity token; nothing is sent and no event is raised. Each
    /// value is read on the main thread only. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/analyticssession).
    /// </summary>
    public enum AnalyticsSessionState
    {
        kSessionStopped = 0,
        kSessionStarted = 1,
        kSessionPaused = 2,
        kSessionResumed = 3,
    }

    public static partial class AnalyticsSessionInfo
    {
        public delegate void SessionStateChanged(AnalyticsSessionState sessionState, long sessionId, long sessionElapsedTime, bool sessionChanged);
        public delegate void IdentityTokenChanged(string token);

        const string CountKey = "unity.player_session_count";
        const string SessionIdKey = "unity.player_sessionid";
        const string UserIdKey = "unity.cloud_userid";

        static AnalyticsSessionState s_State = AnalyticsSessionState.kSessionStopped;
        static long s_SessionId;
        static long s_Count;
        static bool s_FirstRun;
        static string s_UserId = "";
        static string s_CustomUserId = "";
        static string s_CustomDeviceId = "";
        static readonly Stopwatch s_Elapsed = new Stopwatch();

        public static event SessionStateChanged sessionStateChanged;
        public static event IdentityTokenChanged identityTokenChanged;

        /// <summary>Play mode started: a new session, the count and ids written to the preferences.</summary>
        internal static void BeginPlaySession()
        {
            long.TryParse(PlayerPrefs.GetString(CountKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out long previous);
            s_Count = Math.Max(0, previous) + 1;
            s_FirstRun = s_Count == 1;
            PlayerPrefs.SetString(CountKey, s_Count.ToString(CultureInfo.InvariantCulture));
            s_SessionId = System.Random.Shared.NextInt64(1, long.MaxValue);
            PlayerPrefs.SetString(SessionIdKey, s_SessionId.ToString(CultureInfo.InvariantCulture));
            s_UserId = PlayerPrefs.GetString(UserIdKey);
            if (string.IsNullOrEmpty(s_UserId))
            {
                s_UserId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(UserIdKey, s_UserId);
            }
            s_State = AnalyticsSessionState.kSessionStarted;
            s_Elapsed.Restart();
        }

        internal static void CallSessionStateChanged(AnalyticsSessionState sessionState, long sessionId, long sessionElapsedTime, bool sessionChanged) =>
            sessionStateChanged?.Invoke(sessionState, sessionId, sessionElapsedTime, sessionChanged);

        internal static void CallIdentityTokenChanged(string token) => identityTokenChanged?.Invoke(token);

        public static AnalyticsSessionState sessionState { get { MainThreadCheck.Check("get_sessionState"); return s_State; } }
        public static long sessionId { get { MainThreadCheck.Check("get_sessionId"); return s_SessionId; } }
        public static long sessionCount { get { MainThreadCheck.Check("get_sessionCount"); return s_Count; } }
        public static long sessionElapsedTime { get { MainThreadCheck.Check("get_sessionElapsedTime"); return s_Elapsed.ElapsedMilliseconds; } }
        public static bool sessionFirstRun { get { MainThreadCheck.Check("get_sessionFirstRun"); return s_FirstRun; } }
        public static string userId { get { MainThreadCheck.Check("get_userId"); return s_UserId; } }
        public static string identityToken { get { MainThreadCheck.Check("get_identityToken"); return ""; } }

        public static string customUserId
        {
            get { MainThreadCheck.Check("get_customUserIdInternal"); return s_CustomUserId; }
            set { MainThreadCheck.Check("set_customUserIdInternal"); s_CustomUserId = value ?? ""; }
        }

        public static string customDeviceId
        {
            get { MainThreadCheck.Check("get_customDeviceIdInternal"); return s_CustomDeviceId; }
            set { MainThreadCheck.Check("set_customDeviceIdInternal"); s_CustomDeviceId = value ?? ""; }
        }
    }
}

namespace UnityEngine
{
    /// <summary>
    /// Remote settings with Unity services not logged in: no settings arrive, so every getter
    /// returns its default (0, "", false, null for objects and dictionaries, the given default) and
    /// ForceUpdate fetches nothing and raises no event. Each call is for the main thread only.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/analyticssession).
    /// </summary>
    public static partial class RemoteSettings
    {
        public delegate void UpdatedEventHandler();

        public static event UpdatedEventHandler Updated;
        public static event Action BeforeFetchFromServer;
        public static event Action<bool, bool, int> Completed;

        internal static void RemoteSettingsUpdated(bool wasLastUpdatedFromServer) => Updated?.Invoke();
        internal static void RemoteSettingsBeforeFetchFromServer() => BeforeFetchFromServer?.Invoke();
        internal static void RemoteSettingsUpdateCompleted(bool wasLastUpdatedFromServer, bool settingsChanged, int response) =>
            Completed?.Invoke(wasLastUpdatedFromServer, settingsChanged, response);

        [Obsolete("Calling CallOnUpdate() is not necessary any more and should be removed. Use RemoteSettingsUpdated instead", true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void CallOnUpdate() => throw new NotSupportedException("Calling CallOnUpdate() is not necessary any more and should be removed.");

        public static void ForceUpdate() => MainThreadCheck.Check("ForceUpdate");

        public static bool WasLastUpdatedFromServer() { MainThreadCheck.Check("WasLastUpdatedFromServer"); return false; }

        public static int GetInt(string key) => GetInt(key, 0);
        public static int GetInt(string key, int defaultValue) { MainThreadCheck.Check("GetInt"); return defaultValue; }
        public static long GetLong(string key) => GetLong(key, 0L);
        public static long GetLong(string key, long defaultValue) { MainThreadCheck.Check("GetLong"); return defaultValue; }
        public static float GetFloat(string key) => GetFloat(key, 0f);
        public static float GetFloat(string key, float defaultValue) { MainThreadCheck.Check("GetFloat"); return defaultValue; }
        public static string GetString(string key) => GetString(key, "");
        public static string GetString(string key, string defaultValue) { MainThreadCheck.Check("GetString"); return defaultValue; }
        public static bool GetBool(string key) => GetBool(key, false);
        public static bool GetBool(string key, bool defaultValue) { MainThreadCheck.Check("GetBool"); return defaultValue; }
        public static bool HasKey(string key) { MainThreadCheck.Check("HasKey"); return false; }
        public static int GetCount() { MainThreadCheck.Check("GetCount"); return 0; }
        public static string[] GetKeys() { MainThreadCheck.Check("GetKeys"); return Array.Empty<string>(); }

        public static T GetObject<T>(string key = "") => (T)GetObject(typeof(T), key);

        public static object GetObject(Type type, string key = "")
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            CheckDeserializable(type);
            return GetAsScriptingObject(type, null, key);
        }

        public static object GetObject(string key, object defaultValue)
        {
            if (defaultValue == null) throw new ArgumentNullException(nameof(defaultValue));
            var type = defaultValue.GetType();
            CheckDeserializable(type);
            return GetAsScriptingObject(type, defaultValue, key);
        }

        /// <summary>Settings deserialize into new plain objects only.</summary>
        static void CheckDeserializable(Type type)
        {
            if (type.IsAbstract || type.IsSubclassOf(typeof(Object)))
                throw new ArgumentException("Cannot deserialize to new instances of type '" + type.Name + ".'");
        }

        internal static object GetAsScriptingObject(Type t, object defaultValue, string key)
        {
            MainThreadCheck.Check("GetAsScriptingObject");
            return defaultValue;
        }

        public static IDictionary<string, object> GetDictionary(string key = "")
        {
            UseSafeLock();
            ReleaseSafeLock();
            return null;
        }

        internal static void UseSafeLock() => MainThreadCheck.Check("UseSafeLock");
        internal static void ReleaseSafeLock() { }
        internal static IntPtr GetSafeTopMap() => IntPtr.Zero;
    }
}
