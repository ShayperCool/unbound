using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.UnityConsent;
using Crash = UnityEngine.CrashReportHandler.CrashReportHandler;

public class EditorServicesProbe : MonoBehaviour
{
    public static Action<int> finished;
    static StreamWriter writer;
    static readonly List<string> notifications = new List<string>();
    static readonly List<string> logCallbacks = new List<string>();
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] class Row { public string key; public string value; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string directory = Environment.GetEnvironmentVariable("EDITORSERVICES_PROBE_OUT");
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        writer = new StreamWriter(Path.Combine(directory, "values.jsonl")) { AutoFlush = true };
        new GameObject("EditorServicesProbe").AddComponent<EditorServicesProbe>();
    }

    static void Value(string key, object value) => writer.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : value.ToString() }));
    static void Check(string key, Func<object> action)
    {
        try { Value(key, action()); }
        catch (Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            Value(key, e.GetType().Name + ":" + e.Message.Replace("\r", ""));
        }
    }
    static object Property(Type type, string name) => type.GetProperty(name, All).GetValue(null);
    static void NotifyA(ConsentState state) => notifications.Add("A:" + state);
    static void NotifyB(ConsentState state) => notifications.Add("B:" + state);
    static void State(string key)
    {
        Value(key + ".state", EndUserConsent.GetConsentState());
        Value(key + ".events", string.Join("|", notifications));
        Value(key + ".prefs", PlayerPrefs.GetString("unity.end_user_consent", "<missing>"));
        notifications.Clear();
    }
    static void LogA(string condition, string trace, LogType type) { if (condition.StartsWith("service-log-")) logCallbacks.Add("A:" + condition); }
    static void LogB(string condition, string trace, LogType type) { if (condition.StartsWith("service-log-")) logCallbacks.Add("B:" + condition); }
    static void LogObserver(string condition, string trace, LogType type) { if (condition.StartsWith("service-log-")) logCallbacks.Add("O:" + condition); }

    void Start()
    {
        int code = 0;
        try
        {
            Value("application.playing", Application.isPlaying);
            Check("application.human", () => Property(typeof(Application), "isHumanControllingUs"));
            ThreadControls("cold");
            Consent();
            CrashReports();
            AnalyticsControls();
            ThreadControls("cached");
            LogControls();
            Value("complete", true);
        }
        catch (Exception e) { Value("fatal", e); code = 1; }
        finally { writer.Dispose(); }
        if (finished != null) finished(code); else UnityEditor.EditorApplication.Exit(code);
    }

    static void ThreadControls(string context)
    {
        var thread = new System.Threading.Thread(() =>
        {
            string p = "thread." + context + ".";
            Check(p + "application.human", () => Property(typeof(Application), "isHumanControllingUs"));
            Check(p + "consent.get", () => EndUserConsent.GetConsentState());
            Check(p + "consent.set", () => { EndUserConsent.SetConsentState(new ConsentState { AdsIntent = ConsentStatus.Granted }); return "ok"; });
            Check(p + "installation", () => UnityEngine.Identifiers.Identifiers.installationId);
            Check(p + "crash.get", () => Crash.GetUserMetadata("q481.key"));
            Check(p + "crash.set.invalid", () => { Crash.SetUserMetadata(new string('k', 256), "value"); return "ok"; });
            Check(p + "crash.setEnabled", () => { Crash.enableCaptureExceptions = false; return "ok"; });
            Check(p + "crash.buffer.get", () => Crash.logBufferSize);
            Check(p + "crash.buffer.set", () => { Crash.logBufferSize = 20; return "ok"; });
            Check(p + "crash.enabled", () => Crash.enableCaptureExceptions);
            Check(p + "crash.buffer.invalid", () => { Crash.logBufferSize = 51; return Crash.logBufferSize; });
            Check(p + "analytics.enabled", () => Analytics.enabled);
            Check(p + "analytics.setEnabled", () => { Analytics.enabled = true; return "ok"; });
            Check(p + "analytics.initialized", () => typeof(Analytics).GetMethod("IsInitialized", All).Invoke(null, null));
            Check(p + "analytics.event", () => Analytics.CustomEvent("probe"));
            Check(p + "analytics.event.invalid", () => Analytics.CustomEvent(""));
            Check(p + "analytics.position.invalid", () => Analytics.CustomEvent("probe", new Vector3(float.NaN, 0, 0)));
            Check(p + "analytics.data.invalid", () => Analytics.CustomEvent("probe", new Dictionary<string, object> { { "bad", new object() } }));
            Check(p + "analytics.transaction", () => Analytics.Transaction("item", 1m, "USD"));
            Check(p + "analytics.transaction.invalid", () => Analytics.Transaction("", 1m, "USD"));
        });
        thread.Start(); thread.Join();
        Value("thread." + context + ".consent.stateAfter", EndUserConsent.GetConsentState());
        Value("thread." + context + ".crash.enabledAfter", Crash.enableCaptureExceptions);
        Value("thread." + context + ".crash.bufferAfter", Crash.logBufferSize);
    }

    static void Consent()
    {
        EndUserConsent.SetConsentState(new ConsentState { AdsIntent = ConsentStatus.Denied, AnalyticsIntent = ConsentStatus.Denied });
        EndUserConsent.SetConsentState(new ConsentState());
        PlayerPrefs.DeleteAll();
        Value("consent.defaultStruct", default(ConsentState));
        Value("consent.newStruct", new ConsentState());
        State("consent.initial");
        EndUserConsent.consentStateChanged += NotifyA;
        EndUserConsent.consentStateChanged += NotifyB;
        EndUserConsent.SetConsentState(new ConsentState()); State("consent.sameDefault");
        var state = new ConsentState { AdsIntent = ConsentStatus.Granted, AnalyticsIntent = ConsentStatus.Denied };
        EndUserConsent.SetConsentState(state); State("consent.changed");
        EndUserConsent.SetConsentState(state); State("consent.repeated");
        EndUserConsent.consentStateChanged -= NotifyA;
        state.AnalyticsIntent = ConsentStatus.Granted;
        EndUserConsent.SetConsentState(state); State("consent.analyticsOnly");
        EndUserConsent.consentStateChanged += NotifyB;
        state.AdsIntent = ConsentStatus.Denied;
        EndUserConsent.SetConsentState(state); State("consent.duplicateSubscriber");
        EndUserConsent.consentStateChanged -= NotifyB;
        state.AdsIntent = (ConsentStatus)42; state.AnalyticsIntent = (ConsentStatus)(-7);
        EndUserConsent.SetConsentState(state); State("consent.unknownValues");
        PlayerPrefs.SetString("unity.end_user_consent", "{}"); State("consent.directPrefsWrite");
        PlayerPrefs.DeleteAll(); State("consent.deleteAll");
        EndUserConsent.consentStateChanged -= NotifyB;
        EndUserConsent.SetConsentState(new ConsentState()); State("consent.unsubscribed");
    }

    static void CrashReports()
    {
        Check("crash.enabled.initial", () => Crash.enableCaptureExceptions);
        Check("crash.enabled.false", () => { Crash.enableCaptureExceptions = false; return Crash.enableCaptureExceptions; });
        Check("crash.enabled.true", () => { Crash.enableCaptureExceptions = true; return Crash.enableCaptureExceptions; });
        Check("crash.buffer.initial", () => Crash.logBufferSize);
        foreach (uint size in new uint[] { 0, 1, 50, 51, uint.MaxValue, 10 })
            Check("crash.buffer." + size, () => { Crash.logBufferSize = size; return Crash.logBufferSize; });
        foreach (int count in new[] { 127, 128, 255 })
            Check("crash.utf8key." + count, () => { string k = new string('\u03b1', count); Crash.SetUserMetadata(k, "unicode"); return Crash.GetUserMetadata(k); });
        Check("crash.missing", () => Crash.GetUserMetadata("q481.missing"));
        foreach (string key in new string[] { "q481.key", "", null, new string('k', 256) })
        {
            string label = key == null ? "null" : key.Length == 0 ? "empty" : key.Length > 100 ? "long" : "normal";
            foreach (string value in new string[] { "first", "second", "", null, new string('v', 1024) })
            {
                string vlabel = value == null ? "null" : value.Length == 0 ? "empty" : value.Length > 100 ? "long" : value;
                Check("crash.metadata." + label + "." + vlabel, () => { Crash.SetUserMetadata(key, value); return Crash.GetUserMetadata(key); });
            }
        }
        Check("identifiers.installation.shape", () => Guid.TryParse(UnityEngine.Identifiers.Identifiers.installationId, out var id) && id != Guid.Empty);
        Check("identifiers.installation.prefs", () => UnityEngine.Identifiers.Identifiers.installationId == PlayerPrefs.GetString("unity_connect.installation_id"));
        Check("identifiers.repeated", () => UnityEngine.Identifiers.Identifiers.installationId == UnityEngine.Identifiers.Identifiers.installationId);
    }

    static void AnalyticsControls()
    {
        Value("analytics.enabled.initial", Analytics.enabled);
        foreach (bool enabled in new bool[] { false, true, false })
        {
            Check(enabled ? "analytics.enable" : "analytics.disable." + (enabledCount++), () => { Analytics.enabled = enabled; return Analytics.enabled; });
        }
        if (Analytics.enabled) throw new InvalidOperationException("Analytics must be disabled before event controls");
        Check("analytics.initialized", () => typeof(Analytics).GetMethod("IsInitialized", All).Invoke(null, null));
        foreach (string name in new string[] { "probe", "", null, "unicode-\u03b1" })
        {
            string key = name == null ? "null" : name.Length == 0 ? "empty" : name == "probe" ? "normal" : "unicode";
            Check("analytics.custom." + key, () => Analytics.CustomEvent(name));
            Check("analytics.position." + key, () => Analytics.CustomEvent(name, new Vector3(float.NaN, 2, 3)));
            Check("analytics.data." + key, () => Analytics.CustomEvent(name, new Dictionary<string, object> { { "number", 12 }, { "object", new object() } }));
            Check("analytics.nullData." + key, () => Analytics.CustomEvent(name, (IDictionary<string, object>)null));
        }
        Check("analytics.transaction", () => Analytics.Transaction("item", 1.25m, "USD"));
        Check("analytics.transaction.receipt", () => Analytics.Transaction("item", -2m, "usd", null, null));
        Check("analytics.transaction.service", () => Analytics.Transaction("item", decimal.MaxValue, "USD", "receipt", "signature", true));
        Check("analytics.transaction.nullProduct", () => Analytics.Transaction(null, 1m, "USD"));
        Check("analytics.transaction.emptyProduct", () => Analytics.Transaction("", 1m, "USD"));
        Check("analytics.transaction.nullCurrency", () => Analytics.Transaction("item", 1m, null));
        Check("analytics.transaction.emptyCurrency", () => Analytics.Transaction("item", 1m, ""));
    }
    static int enabledCount;

    static void LogControls()
    {
#pragma warning disable 618
        Application.logMessageReceived += LogObserver;
        Application.RegisterLogCallback(LogA);
        Debug.Log("service-log-first");
        Application.RegisterLogCallback(LogB);
        Debug.Log("service-log-replaced");
        Application.RegisterLogCallbackThreaded(LogA);
        Debug.Log("service-log-threaded");
        Application.RegisterLogCallback(null);
        Debug.Log("service-log-cleared");
        Application.logMessageReceived -= LogObserver;
        Value("application.logCallbacks", string.Join("|", logCallbacks));
#pragma warning restore 618
    }
}
