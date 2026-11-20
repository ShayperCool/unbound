using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Analytics;
using UnityEngine;

static class AnalyticsSettingsProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("ANALYTICS_SETTINGS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, bool value) => output.Append("{\"k\":\"").Append(key)
            .Append("\",\"v\":").Append(value ? "true" : "false").Append("}\n");
        bool Read(string name) => (bool)typeof(AnalyticsSettings)
            .GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        Put("enabled", AnalyticsSettings.enabled);
        Put("enabledForPlatform", Read("enabledForPlatform"));
        Put("hasCoreStatsInBuild", Read("hasCoreStatsInBuild"));
        Put("deviceStatsEnabledInBuild", AnalyticsSettings.deviceStatsEnabledInBuild);

        bool called = false;
        AnalyticsSettings.RequireInBuildDelegate requirement = () => { called = true; return true; };
        AnalyticsSettings.OnRequireInBuildHandler += requirement;
        try
        {
            Put("deviceStatsAfterLateSubscription", AnalyticsSettings.deviceStatsEnabledInBuild);
            Put("lateSubscriberCalled", called);
        }
        finally { AnalyticsSettings.OnRequireInBuildHandler -= requirement; }

        Put("complete", true);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
