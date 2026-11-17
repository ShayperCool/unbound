using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Analytics;

namespace AnalyticsSessionProbe
{
    /// <summary>
    /// AnalyticsSessionInfo and RemoteSettings in Play mode with Unity services disabled: the session
    /// values (those that vary between runs by their shape only), custom ids set and cleared, the
    /// session and token events over frames, RemoteSettings' getters with and without defaults and
    /// null keys, ForceUpdate's events over frames, and calls from a worker thread.
    /// </summary>
    public class AnalyticsSessionBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Events = new List<string>();

        [Serializable]
        public class Payload
        {
            public int number = 7;
            public string text = "default";
        }

        static readonly List<string> s_EarlyEvents = new List<string>();

        // Subscribed before any scene loads: what the session start sends to early subscribers.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SubscribeEarly()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANALYTICS_SESSION_OUT"))) return;
            AnalyticsSessionInfo.sessionStateChanged += (state, id, elapsed, changed) => s_EarlyEvents.Add("session " + state + " " + changed);
            AnalyticsSessionInfo.identityTokenChanged += token => s_EarlyEvents.Add("token " + Shape(token));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANALYTICS_SESSION_OUT"))) return;
            new GameObject("probe").AddComponent<AnalyticsSessionBehaviour>();
        }

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(value == null ? "null" : Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string Try(Func<object> f)
        {
            try
            {
                var v = f();
                return v == null ? "null" : Convert.ToString(v, Inv);
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        static string OnWorker(Func<object> f)
        {
            string result = null;
            var t = new Thread(() => result = Try(f));
            t.Start();
            t.Join();
            return result;
        }

        /// <summary>A text's shape: null, empty, or its length and whether it is lowercase hex.</summary>
        static string Shape(string s) =>
            s == null ? "null" : s.Length == 0 ? "empty" : s.Length + (s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) ? " hex" : " other");

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("ANALYTICS_SESSION_OUT");
            AnalyticsSessionInfo.sessionStateChanged += (state, id, elapsed, changed) => m_Events.Add("session " + state + " " + changed);
            AnalyticsSessionInfo.identityTokenChanged += token => m_Events.Add("token " + Shape(token));
            RemoteSettings.Updated += () => m_Events.Add("updated");
            RemoteSettings.BeforeFetchFromServer += () => m_Events.Add("beforeFetch");
            RemoteSettings.Completed += (fromServer, changed, response) => m_Events.Add("completed " + fromServer + " " + changed + " " + response);
            yield return null;

            Put("events.earlySubscriber", string.Join(" | ", s_EarlyEvents));
            Put("session.state", AnalyticsSessionInfo.sessionState);
            Put("session.id", AnalyticsSessionInfo.sessionId == 0 ? "0" : AnalyticsSessionInfo.sessionId > 0 ? "positive" : "negative");
            // The count and first-run flag carry over between runs of the project (its preferences).
            long count = AnalyticsSessionInfo.sessionCount;
            Put("session.countAtLeastOne", count >= 1);
            Put("session.firstRunIsCountOne", AnalyticsSessionInfo.sessionFirstRun == (count == 1));
            Put("session.countInPrefs", PlayerPrefs.GetString("unity.player_session_count") == count.ToString(Inv));
            Put("session.idInPrefs", PlayerPrefs.GetString("unity.player_sessionid") == AnalyticsSessionInfo.sessionId.ToString(Inv));
            Put("session.userIdInPrefs", PlayerPrefs.GetString("unity.cloud_userid") == AnalyticsSessionInfo.userId);
            Put("session.elapsed", AnalyticsSessionInfo.sessionElapsedTime == 0 ? "0" : "nonzero");
            Put("session.userId", Shape(AnalyticsSessionInfo.userId));
            Put("session.userIdStable", AnalyticsSessionInfo.userId == AnalyticsSessionInfo.userId);
            Put("session.identityToken", Shape(AnalyticsSessionInfo.identityToken));
            Put("session.customUserId", Try(() => AnalyticsSessionInfo.customUserId));
            Put("session.customDeviceId", Try(() => AnalyticsSessionInfo.customDeviceId));
            Put("session.setCustomUserId", Try(() => { AnalyticsSessionInfo.customUserId = "probe-user"; return AnalyticsSessionInfo.customUserId; }));
            Put("session.setCustomDeviceId", Try(() => { AnalyticsSessionInfo.customDeviceId = "probe-device"; return AnalyticsSessionInfo.customDeviceId; }));
            Put("session.clearCustomUserId", Try(() => { AnalyticsSessionInfo.customUserId = null; return AnalyticsSessionInfo.customUserId; }));
            Put("session.worker.id", OnWorker(() => AnalyticsSessionInfo.sessionId == 0 ? "0" : "nonzero"));
            Put("session.worker.userId", OnWorker(() => Shape(AnalyticsSessionInfo.userId)));

            Put("remote.fromServer", RemoteSettings.WasLastUpdatedFromServer());
            Put("remote.int", RemoteSettings.GetInt("k"));
            Put("remote.intDefault", RemoteSettings.GetInt("k", 5));
            Put("remote.long", RemoteSettings.GetLong("k", 6));
            Put("remote.float", RemoteSettings.GetFloat("k", 1.5f));
            Put("remote.floatNoDefault", RemoteSettings.GetFloat("k"));
            Put("remote.string", Try(() => RemoteSettings.GetString("k")));
            Put("remote.stringDefault", RemoteSettings.GetString("k", "d"));
            Put("remote.bool", RemoteSettings.GetBool("k", true));
            Put("remote.hasKey", RemoteSettings.HasKey("k"));
            Put("remote.count", RemoteSettings.GetCount());
            Put("remote.keys", Try(() => RemoteSettings.GetKeys()?.Length));
            Put("remote.objectT", Try(() => { var p = RemoteSettings.GetObject<Payload>("k"); return p == null ? "null" : p.number + " " + p.text; }));
            Put("remote.objectType", Try(() => { var p = (Payload)RemoteSettings.GetObject(typeof(Payload), "k"); return p == null ? "null" : p.number + " " + p.text; }));
            Put("remote.objectDefault", Try(() => { var p = (Payload)RemoteSettings.GetObject("k", new Payload { number = 3 }); return p == null ? "null" : p.number + " " + p.text; }));
            Put("remote.objectNullType", Try(() => RemoteSettings.GetObject(null, "k")));
            Put("remote.objectUnityType", Try(() => RemoteSettings.GetObject(typeof(MonoBehaviour), "k")));
            Put("remote.objectAbstractType", Try(() => RemoteSettings.GetObject(typeof(Stream), "k")));
            Put("remote.objectNullDefault", Try(() => RemoteSettings.GetObject("k", null)));
            Put("remote.objectUnityDefault", Try(() => RemoteSettings.GetObject("k", new GameObject("probeDefault"))));
            Put("remote.objectDefaultSame", Try(() => { var d = new Payload(); return ReferenceEquals(RemoteSettings.GetObject("k", d), d); }));
            Put("remote.dictionary", Try(() => { var d = RemoteSettings.GetDictionary("k"); return d == null ? "null" : "count " + d.Count; }));
            Put("remote.dictionaryRoot", Try(() => { var d = RemoteSettings.GetDictionary(); return d == null ? "null" : "count " + d.Count; }));
            Put("remote.nullKeyInt", Try(() => RemoteSettings.GetInt(null)));
            Put("remote.nullKeyString", Try(() => RemoteSettings.GetString(null, "d")));
            Put("remote.nullKeyHas", Try(() => RemoteSettings.HasKey(null)));
            Put("remote.worker.int", OnWorker(() => RemoteSettings.GetInt("k", 9)));
            Put("remote.worker.count", OnWorker(() => RemoteSettings.GetCount()));

            Put("events.beforeForceUpdate", string.Join(" | ", m_Events));
            m_Events.Clear();
            RemoteSettings.ForceUpdate();
            Put("events.duringForceUpdate", string.Join(" | ", m_Events));
            m_Events.Clear();
            for (int i = 0; i < 10; i++) yield return null;
            Put("events.tenFramesLater", string.Join(" | ", m_Events));
            Put("remote.fromServerAfter", RemoteSettings.WasLastUpdatedFromServer());
            m_Events.Clear();
            Put("remote.worker.forceUpdate", OnWorker(() => { RemoteSettings.ForceUpdate(); return "ok"; }));
            for (int i = 0; i < 3; i++) yield return null;
            Put("events.afterWorkerForceUpdate", string.Join(" | ", m_Events));

            // PlayerPrefs.DeleteAll: the session's preferences, and the session's values after it.
            string userBefore = AnalyticsSessionInfo.userId;
            long countBefore = AnalyticsSessionInfo.sessionCount, idBefore = AnalyticsSessionInfo.sessionId;
            PlayerPrefs.DeleteAll();
            Put("deleteAll.countKey", PlayerPrefs.HasKey("unity.player_session_count"));
            Put("deleteAll.sessionIdKey", PlayerPrefs.HasKey("unity.player_sessionid"));
            Put("deleteAll.userIdKey", PlayerPrefs.HasKey("unity.cloud_userid"));
            Put("deleteAll.userIdKeySame", PlayerPrefs.GetString("unity.cloud_userid") == userBefore);
            Put("deleteAll.sessionValuesSame", AnalyticsSessionInfo.userId == userBefore && AnalyticsSessionInfo.sessionCount == countBefore && AnalyticsSessionInfo.sessionId == idBefore);
            Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }
    }
}
