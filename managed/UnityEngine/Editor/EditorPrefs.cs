using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's machine-wide preferences. Values Unity saved on this machine
    /// (`~/.local/share/unity3d/prefs` on Linux, the `com.unity3d.UnityEditor5.x` preferences domain
    /// on macOS) are read; what the game changes goes to Unbound's own file
    /// (`$XDG_CACHE_HOME/unbound/EditorPrefs`), so the installed Editor's preferences are never written.
    /// </summary>
    public sealed partial class EditorPrefs
    {
        static Dictionary<string, object> s_Values;
        static HashSet<string> s_Deleted;

        static string UnityFile => Path.Combine(Home(), ".local", "share", "unity3d", "prefs");

        static Dictionary<string, object> ReadUnity() =>
            OperatingSystem.IsMacOS() ? MacPreferences.Read("com.unity3d.UnityEditor5.x") : UnityPrefsXml.Read(UnityFile);

        static string OwnFile
        {
            get
            {
                var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cache)) cache = Path.Combine(Home(), ".cache");
                return Path.Combine(cache, "unbound", "EditorPrefs");
            }
        }

        static string Home() => Environment.GetEnvironmentVariable("HOME") ?? "";

        static Dictionary<string, object> Values
        {
            get
            {
                if (s_Values != null) return s_Values;
                s_Values = new Dictionary<string, object>();
                s_Deleted = new HashSet<string>();
                try
                {
                    foreach (var pair in ReadUnity()) s_Values[pair.Key] = pair.Value;
                    var own = UnityPrefsXml.Read(OwnFile);
                    foreach (var pair in own) s_Values[pair.Key] = pair.Value;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: could not read EditorPrefs: {e.Message}");
                }
                return s_Values;
            }
        }

        static void Set(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Values[key] = value;
            s_Deleted.Remove(key);
            Save();
        }

        /// <summary>Writes the values that differ from Unity's file to Unbound's.</summary>
        static void Save()
        {
            try
            {
                var unity = ReadUnity();
                var own = new Dictionary<string, object>();
                foreach (var pair in s_Values)
                    if (!unity.TryGetValue(pair.Key, out var v) || !Equals(v, pair.Value)) own[pair.Key] = pair.Value;
                UnityPrefsXml.Write(OwnFile, own);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: could not save EditorPrefs: {e.Message}");
            }
        }

        public static void SetInt(string key, int value) => Set(key, value);
        public static int GetInt(string key, [UnityEngine.Internal.DefaultValue("0")] int defaultValue) => Values.TryGetValue(key, out var v) && v is int i ? i : defaultValue;
        public static int GetInt(string key) => GetInt(key, 0);
        public static void SetFloat(string key, float value) => Set(key, value);
        public static float GetFloat(string key, [UnityEngine.Internal.DefaultValue("0.0F")] float defaultValue) => Values.TryGetValue(key, out var v) && v is float f ? f : defaultValue;
        public static float GetFloat(string key) => GetFloat(key, 0f);
        public static void SetString(string key, string value) => Set(key, value ?? "");
        public static string GetString(string key, [UnityEngine.Internal.DefaultValue("\"\"")] string defaultValue) => Values.TryGetValue(key, out var v) && v is string s ? s : defaultValue;
        public static string GetString(string key) => GetString(key, "");
        /// <summary>Booleans are ints in Unity's file.</summary>
        public static void SetBool(string key, bool value) => Set(key, value ? 1 : 0);
        public static bool GetBool(string key, [UnityEngine.Internal.DefaultValue("false")] bool defaultValue) => Values.TryGetValue(key, out var v) && v is int i ? i != 0 : defaultValue;
        public static bool GetBool(string key) => GetBool(key, false);
        public static bool HasKey(string key) => Values.ContainsKey(key);

        public static void DeleteKey(string key)
        {
            if (Values.Remove(key)) Save();
        }

        public static void DeleteAll()
        {
            Values.Clear();
            Save();
        }
    }

    /// <summary>The build settings of the Play mode session: the active target is the --target the
    /// game runs for.</summary>
    public partial class EditorUserBuildSettings : UnityEngine.Object
    {
        public static BuildTarget activeBuildTarget => EditorUserBuildSettingsInfo.ActiveBuildTarget;
        static BuildTargetGroup? s_SelectedGroup;
        static readonly Dictionary<BuildTarget, string> s_BuildLocations = new Dictionary<BuildTarget, string>();

        /// <summary>The group the Build Settings window shows: the active one until set.</summary>
        public static BuildTargetGroup selectedBuildTargetGroup
        {
            get => s_SelectedGroup ?? EditorUserBuildSettingsInfo.ActiveGroup;
            set => s_SelectedGroup = value;
        }
        public static bool development { get; set; }
        public static bool allowDebugging { get; set; }
        public static bool connectProfiler { get; set; }
        public static bool buildScriptsOnly { get; set; }
        public static bool buildAppBundle { get; set; }
        public static bool exportAsGoogleAndroidProject { get; set; }
        public static string[] activeScriptCompilationDefines => ProjectInfo.Defines;

        public static bool SwitchActiveBuildTarget(BuildTargetGroup targetGroup, BuildTarget target)
        {
            if (target == activeBuildTarget) return true;
            Debug.LogError("[Unbound] Switching the build target while playing is not supported; restart with --target.");
            return false;
        }

        public static string GetBuildLocation(BuildTarget target) => s_BuildLocations.TryGetValue(target, out var location) ? location : "";
        public static void SetBuildLocation(BuildTarget target, string location) => s_BuildLocations[target] = location ?? "";
    }

    public partial class BuildPipeline
    {
        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget platform)
        {
            switch (platform)
            {
                case BuildTarget.Android: return BuildTargetGroup.Android;
                case BuildTarget.iOS: return BuildTargetGroup.iOS;
                case BuildTarget.WebGL: return BuildTargetGroup.WebGL;
                case BuildTarget.tvOS: return BuildTargetGroup.tvOS;
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64: return BuildTargetGroup.Standalone;
                default: return BuildTargetGroup.Unknown;
            }
        }

        [Unbound.EditorFaithful("no player is built while playing")]
        public static bool isBuildingPlayer => false;
    }
}
