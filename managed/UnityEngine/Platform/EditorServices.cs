using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Unbound.Runtime;

namespace Unbound.Runtime
{
    // Service configuration is read once from the project's settings, without contacting a service.
    // The first read may come from any thread (event subscriptions have no thread check): it reads
    // under a lock and publishes the loaded flag after the values.
    internal static class EditorServiceSettings
    {
        static volatile bool s_Loaded;
        static readonly object s_Lock = new object();
        internal static bool Configured;
        internal static bool CoreStatsRequested;
        internal static bool CaptureExceptions;
        internal static uint LogBufferSize = 10;

        internal static void Ensure()
        {
            if (s_Loaded) return;
            lock (s_Lock)
            {
                if (s_Loaded) return;
                try { Load(); }
                finally { s_Loaded = true; }
            }
        }

        static void Load()
        {
            string path = Path.Combine(UnityEngine.ProjectInfo.ProjectPath, "ProjectSettings", "UnityConnectSettings.asset");
            if (!Native.IsReady || !File.Exists(path)) return;
            foreach (var doc in Native.LoadDocuments(path))
            {
                if (doc.ClassId != 310) continue;
                Configured = doc.Body.Get("UnityAnalyticsSettings")?.Bool("m_Enabled") == true;
                var crash = doc.Body.Get("CrashReportingSettings");
                CoreStatsRequested = Configured
                    || doc.Body.Get("UnityPurchasingSettings")?.Bool("m_Enabled") == true
                    || doc.Body.Get("UnityAdsSettings")?.Bool("m_Enabled") == true
                    || doc.Body.Get("PerformanceReportingSettings")?.Bool("m_Enabled") == true
                    || crash?.Bool("m_EnableCloudDiagnosticsReporting") == true;
                if (crash == null) continue;
                CaptureExceptions = crash.Bool("m_EnableCloudDiagnosticsReporting");
                LogBufferSize = (uint)Math.Clamp(crash.Int("m_LogBufferSize", 10), 0, 50);
            }
        }
    }
}

namespace UnityEngine.UnityConsent
{
    public enum ConsentStatus { Unspecified = 0, Granted = 1, Denied = 2 }

    public partial struct ConsentState
    {
        public ConsentStatus AdsIntent;
        public ConsentStatus AnalyticsIntent;
        public ConsentState() { AdsIntent = ConsentStatus.Unspecified; AnalyticsIntent = ConsentStatus.Unspecified; }
        public override string ToString() => $"AdsIntent: {AdsIntent}, AnalyticsIntent: {AnalyticsIntent}";
    }

    public static partial class EndUserConsent
    {
        internal const string PreferenceKey = "unity.end_user_consent";
        static bool s_Loaded;
        static ConsentState s_State;
        public static event Action<ConsentState> consentStateChanged;
        [Serializable] sealed class SavedState { public int AdsIntent; public int AnalyticsIntent; }

        public static ConsentState GetConsentState()
        {
            MainThreadCheck.Check("GetConsentState");
            if (!s_Loaded)
            {
                string saved = PlayerPrefs.GetString(PreferenceKey);
                if (!string.IsNullOrEmpty(saved))
                {
                    var state = JsonUtility.FromJson<SavedState>(saved);
                    if (state != null) s_State = new ConsentState { AdsIntent = (ConsentStatus)state.AdsIntent, AnalyticsIntent = (ConsentStatus)state.AnalyticsIntent };
                }
                s_Loaded = true;
            }
            return s_State;
        }

        public static void SetConsentState(ConsentState consentState)
        {
            MainThreadCheck.Check("SetConsentState");
            var old = GetConsentState();
            if (old.AdsIntent == consentState.AdsIntent && old.AnalyticsIntent == consentState.AnalyticsIntent) return;
            s_State = consentState;
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(new SavedState { AdsIntent = (int)consentState.AdsIntent, AnalyticsIntent = (int)consentState.AnalyticsIntent }));
            consentStateChanged?.Invoke(s_State);
        }
    }
}

namespace UnityEngine.Identifiers
{
    public static partial class Identifiers
    {
        internal const string PreferenceKey = "unity_connect.installation_id";
        static string s_Id;
        public static string installationId
        {
            get
            {
                MainThreadCheck.Check("GetInstallationId");
                if (s_Id != null) return s_Id;
                s_Id = PlayerPrefs.GetString(PreferenceKey);
                if (string.IsNullOrEmpty(s_Id))
                {
                    s_Id = Guid.NewGuid().ToString("D");
                    PlayerPrefs.SetString(PreferenceKey, s_Id);
                }
                return s_Id;
            }
        }
    }
}

namespace UnityEngine.CrashReportHandler
{
    public partial class CrashReportHandler
    {
        const int MaxMetadataKeyBytes = 255;
        const uint MaxLogBufferSize = 50;
        static readonly Dictionary<string, string> s_Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        static bool? s_Capture;
        static uint? s_LogBuffer;
        private CrashReportHandler() { }

        public static bool enableCaptureExceptions
        {
            get { MainThreadCheck.Check("get_enableCaptureExceptions"); EditorServiceSettings.Ensure(); return s_Capture ?? EditorServiceSettings.CaptureExceptions; }
            set { MainThreadCheck.Check("set_enableCaptureExceptions"); s_Capture = value; }
        }
        public static uint logBufferSize
        {
            get { MainThreadCheck.Check("get_logBufferSize"); EditorServiceSettings.Ensure(); return s_LogBuffer ?? EditorServiceSettings.LogBufferSize; }
            set
            {
                MainThreadCheck.Check("set_logBufferSize");
                if (value > MaxLogBufferSize) throw new ArgumentException("logBufferSize must be less than or equal to 50");
                s_LogBuffer = value;
            }
        }
        static string Key(string key)
        {
            key ??= "";
            if (Encoding.UTF8.GetByteCount(key) > MaxMetadataKeyBytes)
                throw new ArgumentException("Length of 'key' must be less than or equal to 255");
            return key;
        }
        public static string GetUserMetadata(string key)
        {
            MainThreadCheck.Check("GetUserMetadata");
            return s_Metadata.TryGetValue(Key(key), out var value) ? value : null;
        }
        public static void SetUserMetadata(string key, string value)
        {
            MainThreadCheck.Check("SetUserMetadata");
            key = Key(key);
            if (value == null) s_Metadata.Remove(key);
            else s_Metadata[key] = value;
        }
    }
}

namespace UnityEngine.Analytics
{
    [StructLayout(LayoutKind.Sequential)]
    public static partial class Analytics
    {
        // The service-disabled Editor profile measured by tests/projects/editorservices.
        static AnalyticsResult Result(string binding)
        {
            MainThreadCheck.Check(binding);
            EditorServiceSettings.Ensure();
            if (EditorServiceSettings.Configured)
                Stubs.NotImplemented("Analytics with Unity services enabled in project settings");
            return AnalyticsResult.NotInitialized;
        }
        public static bool enabled
        {
            get { Result("get_enabledInternal"); return false; }
            set { Result("set_enabledInternal"); }
        }
        internal static bool IsInitialized() => true;
        static void EventName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cannot set custom event name to an empty or null string");
        }
        public static AnalyticsResult CustomEvent(string customEventName) { EventName(customEventName); return Result("SendCustomEventName"); }
        public static AnalyticsResult CustomEvent(string customEventName, Vector3 position)
        {
            EventName(customEventName);
            MainThreadCheck.Check("Internal_Create");
            Convert.ToDecimal(position.x); Convert.ToDecimal(position.y); Convert.ToDecimal(position.z);
            return Result("SendCustomEvent");
        }
        public static AnalyticsResult CustomEvent(string customEventName, IDictionary<string, object> eventData)
        {
            EventName(customEventName);
            if (eventData == null) return Result("SendCustomEventName");
            MainThreadCheck.Check("Internal_Create");
            if (eventData != null)
                foreach (var pair in eventData)
                {
                    object value = pair.Value;
                    if (value is float number) Convert.ToDecimal(number);
                    else if (value != null && !(value is string))
                    {
                        var type = value.GetType();
                        if (!type.IsValueType) throw new ArgumentException($"Invalid type: {type} passed");
                        if (!type.IsPrimitive && type != typeof(decimal)) value.ToString();
                    }
                }
            return Result("SendCustomEvent");
        }
        public static AnalyticsResult Transaction(string productId, decimal amount, string currency) => Transaction(productId, amount, currency, null, null, false);
        public static AnalyticsResult Transaction(string productId, decimal amount, string currency, string receiptPurchaseData, string signature) => Transaction(productId, amount, currency, receiptPurchaseData, signature, false);
        public static AnalyticsResult Transaction(string productId, decimal amount, string currency, string receiptPurchaseData, string signature, bool usingIAPService)
        {
            if (string.IsNullOrEmpty(productId)) throw new ArgumentException("Cannot set productId to an empty or null string");
            if (string.IsNullOrEmpty(currency)) throw new ArgumentException("Cannot set currency to an empty or null string");
            return Result("Transaction");
        }
    }
}
