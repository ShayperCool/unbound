using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Unbound.Runtime;
using UnityEngine.Analytics;
using Xunit;

// AnalyticsSessionInfo and RemoteSettings with Unity services not logged in (Unity 6000.0.71f1,
// tests/projects/analyticssession). Controls outside the probe: a second session in the same
// preferences counts on and keeps the user id, a count the preferences cannot read starts again at
// 1, RemoteSettings' getters return the defaults given, and worker threads are refused by binding.

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class AnalyticsSessionTests : IDisposable
    {
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object m_PreviousMainThread = MainThread.GetValue(null);
        readonly string m_PreviousPrefs = ProjectInfo.PlayerPrefsPath;
        readonly string m_Prefs = Path.Combine(Path.GetTempPath(), "unbound-analytics-" + Guid.NewGuid().ToString("N"), "prefs");

        public AnalyticsSessionTests()
        {
            MainThread.SetValue(null, Environment.CurrentManagedThreadId);
            ProjectInfo.PlayerPrefsPath = m_Prefs;
            PlayerPrefs.DeleteAll();
        }

        public void Dispose()
        {
            PlayerPrefs.DeleteAll();
            ProjectInfo.PlayerPrefsPath = m_PreviousPrefs;
            MainThread.SetValue(null, m_PreviousMainThread);
        }

        static Exception OnOtherThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            return failure;
        }

        [Fact]
        public void EachSessionCountsOnInThePreferencesAndKeepsTheUserId()
        {
            PlayerPrefs.SetString("unity.player_session_count", "41");
            AnalyticsSessionInfo.BeginPlaySession();
            Assert.Equal(42, AnalyticsSessionInfo.sessionCount);
            Assert.False(AnalyticsSessionInfo.sessionFirstRun);
            Assert.Equal(AnalyticsSessionState.kSessionStarted, AnalyticsSessionInfo.sessionState);
            string user = AnalyticsSessionInfo.userId;
            Assert.Matches("^[0-9a-f]{32}$", user);
            long first = AnalyticsSessionInfo.sessionId;
            Assert.True(first > 0);
            AnalyticsSessionInfo.BeginPlaySession();
            Assert.Equal(43, AnalyticsSessionInfo.sessionCount);
            Assert.Equal("43", PlayerPrefs.GetString("unity.player_session_count"));
            Assert.Equal(AnalyticsSessionInfo.sessionId.ToString(), PlayerPrefs.GetString("unity.player_sessionid"));
            Assert.Equal(user, AnalyticsSessionInfo.userId);
            Assert.Equal("", AnalyticsSessionInfo.identityToken);
        }

        [Fact]
        public void AnUnreadableCountStartsAgainAsAFirstRun()
        {
            PlayerPrefs.SetString("unity.player_session_count", "not a number");
            AnalyticsSessionInfo.BeginPlaySession();
            Assert.Equal(1, AnalyticsSessionInfo.sessionCount);
            Assert.True(AnalyticsSessionInfo.sessionFirstRun);
        }

        [Fact]
        public void RemoteSettingsReturnTheDefaultsGiven()
        {
            Assert.Equal(-3, RemoteSettings.GetInt("missing", -3));
            Assert.Equal(long.MinValue, RemoteSettings.GetLong("missing", long.MinValue));
            Assert.Equal("", RemoteSettings.GetString("missing"));
            Assert.Null(RemoteSettings.GetString("missing", null));
            Assert.False(RemoteSettings.GetBool(null));
            Assert.Empty(RemoteSettings.GetKeys());
            Assert.Null(RemoteSettings.GetObject<Exception>("missing"));
            var value = new int[] { 1 };
            Assert.Same(value, RemoteSettings.GetObject("missing", value));
            Assert.Equal("Cannot deserialize to new instances of type 'ScriptableObject.'",
                Assert.Throws<ArgumentException>(() => RemoteSettings.GetObject(typeof(ScriptableObject))).Message);
            // Obsolete as an error, so reached by reflection.
            var call = Assert.Throws<TargetInvocationException>(() => typeof(RemoteSettings).GetMethod("CallOnUpdate").Invoke(null, null));
            Assert.IsType<NotSupportedException>(call.InnerException);
        }

        [Fact]
        public void WorkerThreadsAreRefusedByBinding()
        {
            const string tail = " can only be called from the main thread.";
            Assert.StartsWith("get_sessionCount" + tail, OnOtherThread(() => _ = AnalyticsSessionInfo.sessionCount).Message);
            Assert.StartsWith("GetFloat" + tail, OnOtherThread(() => RemoteSettings.GetFloat("k")).Message);
            Assert.StartsWith("GetAsScriptingObject" + tail, OnOtherThread(() => RemoteSettings.GetObject(typeof(int[]))).Message);
            Assert.StartsWith("UseSafeLock" + tail, OnOtherThread(() => RemoteSettings.GetDictionary()).Message);
        }
    }
}
