using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Analytics;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's analytics, answering as the Editor does with analytics on (measured: enabled,
    /// events accepted); Unbound sends nothing anywhere, events are dropped.
    /// </summary>
    public static partial class EditorAnalytics
    {
        public static bool enabled { get => true; set { } }

        public static bool recordEventsEnabled { get => false; set { } }

        public static bool SendAnalyticsEventsImmediately { get => false; set { } }

        public static AnalyticsResult SendAnalytic(IAnalytic analytic)
        {
            if (analytic == null) throw new ArgumentNullException(nameof(analytic));
            bool described = false;
            foreach (var attribute in analytic.GetType().GetCustomAttributes(true))
                if (attribute.GetType().FullName == "UnityEngine.Analytics.AnalyticInfoAttribute") described = true;
            if (!described)
            {
                Debug.LogError($"{analytic.GetType()} must contain an attribute of AnalyticInfoAttribute");
                return AnalyticsResult.InvalidData;
            }
            return AnalyticsResult.Ok;
        }

        public static AnalyticsResult RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey) => AnalyticsResult.Ok;

        public static AnalyticsResult RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey, int ver) => AnalyticsResult.Ok;

        public static AnalyticsResult SendEventWithLimit(string eventName, object parameters) => AnalyticsResult.Ok;

        public static AnalyticsResult SendEventWithLimit(string eventName, object parameters, int ver) => AnalyticsResult.Ok;

        public static AnalyticsResult SetEventWithLimitEndPoint(string eventName, string endPoint, int ver) => AnalyticsResult.Ok;
    }

    /// <summary>
    /// The cloud identity: nobody is signed in (Unbound does not log in to Unity's services).
    /// </summary>
    public partial class CloudProjectSettings
    {
        public static string userId => "";
        public static string userName => "anonymous";
        public static string accessToken => "";
        // Without a signed-in user the Editor reports no project identity, whatever
        // ProjectSettings.asset says (measured).
        public static string projectId => "";
        public static string projectName => "";
        public static string organizationId => "";
        public static string organizationName => "";
        public static string organizationKey => "";
        public static CoppaCompliance coppaCompliance => CoppaCompliance.CoppaUndefined;
        public static bool projectBound => false;

        public static void RefreshAccessToken(Action<bool> refresh) => refresh?.Invoke(false);

        public static void ShowLogin() { }
    }

    /// <summary>The Editor's localization: Unbound's Editor speaks English, the language package
    /// strings are written in.</summary>
    public static partial class L10n
    {
        public static string Tr(string str) => str;

        public static string[] Tr(string[] str_list)
        {
            var result = new string[str_list.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Tr(str_list[i]);
            return result;
        }

        public static string Tr(string str, string groupName) => str;

        public static string TrPath(string path) => path;
    }

    public sealed partial class EditorApplication
    {
        /// <summary>The project was not just created from a template.</summary>
        public static bool isCreateFromTemplate => false;
    }

    public partial class AssetDatabase
    {
        /// <summary>Imports run in the Editor's own process: there are no import workers.</summary>
        public static bool IsAssetImportWorkerProcess() => false;
    }
}

namespace UnityEditor.MPE
{
    /// <summary>The Editor runs as the main process; no secondary processes are launched.</summary>
    public partial class ProcessService
    {
        public static ProcessLevel level => ProcessLevel.Main;

        public static string roleName => "";

        public static bool IsChannelServiceStarted() => false;

        public static string ReadParameter(string paramName) => "";

        public static bool HasCapability(string capName) => false;
    }
}
