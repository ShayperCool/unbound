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
    /// The Editor's analytics, as an Editor whose user turned analytics off answers: nothing is
    /// registered or sent (Unbound never sends data anywhere), every call says so.
    /// </summary>
    public static partial class EditorAnalytics
    {
        public static bool enabled { get => false; set { } }

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
            return AnalyticsResult.AnalyticsDisabled;
        }

        public static AnalyticsResult RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey) => AnalyticsResult.AnalyticsDisabled;

        public static AnalyticsResult RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey, int ver) => AnalyticsResult.AnalyticsDisabled;

        public static AnalyticsResult SendEventWithLimit(string eventName, object parameters) => AnalyticsResult.AnalyticsDisabled;

        public static AnalyticsResult SendEventWithLimit(string eventName, object parameters, int ver) => AnalyticsResult.AnalyticsDisabled;

        public static AnalyticsResult SetEventWithLimitEndPoint(string eventName, string endPoint, int ver) => AnalyticsResult.AnalyticsDisabled;
    }

    /// <summary>
    /// The project's cloud identity, from ProjectSettings.asset, and the signed-in user: nobody is
    /// signed in (Unbound does not log in to Unity's services), as in an Editor run offline.
    /// </summary>
    public partial class CloudProjectSettings
    {
        public static string userId => "";
        public static string userName => "anonymous";
        public static string accessToken => "";
        public static string projectId => ProjectSettingsFile.Scalar("cloudProjectId");
        public static string projectName => ProjectSettingsFile.Scalar("projectName");
        public static string organizationId => ProjectSettingsFile.Scalar("organizationId");
        public static string organizationName => "";
        public static string organizationKey => "";
        public static CoppaCompliance coppaCompliance => CoppaCompliance.CoppaUndefined;
        public static bool projectBound => ProjectSettingsFile.Scalar("cloudProjectId").Length > 0;

        public static void RefreshAccessToken(Action<bool> refresh) => refresh?.Invoke(false);

        public static void ShowLogin() { }
    }

    /// <summary>Top-level scalars of ProjectSettings/ProjectSettings.asset (the PlayerSettings
    /// document), read once.</summary>
    internal static class ProjectSettingsFile
    {
        static Dictionary<string, string> s_Values;

        internal static string Scalar(string key)
        {
            if (s_Values == null)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    var file = Path.Combine(UnityEngine.ProjectInfo.ProjectPath, "ProjectSettings", "ProjectSettings.asset");
                    if (File.Exists(file))
                        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^  (\w+): ?(.*?)\s*$", RegexOptions.Multiline))
                            values.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
                }
                catch (IOException) { }
                s_Values = values;
            }
            return s_Values.TryGetValue(key, out var value) ? value : "";
        }
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
