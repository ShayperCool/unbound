using System;
using System.Globalization;
using System.Text;
using UnityEditor.Build;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    public enum iOSSdkVersion
    {
        DeviceSDK = 988,
        SimulatorSDK = 989,
    }

    public enum tvOSSdkVersion
    {
        Device = 0,
        Simulator = 1,
    }

    public enum ProvisioningProfileType
    {
        Automatic = 0,
        Development = 1,
        Distribution = 2,
    }

    /// <summary>
    /// PlayerSettings values kept in ProjectSettings.asset: read from the file as editor code
    /// changed it, set in effect at once and saved as the Editor saves settings (SettingsAssets.Edit:
    /// in Edit mode by the next SaveAssets, not at all in Play mode). Matches the Unity 6000.0.71f1
    /// Editor, tests/projects/playersettingsvalues. A file without a key answers a new project's
    /// value (not measured).
    /// </summary>
    public sealed partial class PlayerSettings
    {
        internal static int StoredInt(string key, int fallback) =>
            int.TryParse(SettingsAssets.Value("ProjectSettings", key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        internal static string StoredText(string key) => SettingsAssets.Value("ProjectSettings", key);

        static string StoredTargetText(string key, string target) =>
            SettingsAssets.AtPath("ProjectSettings/ProjectSettings.asset")?.Body?.Get(key)?.Get(target)?.Text;

        static void SetTargetText(YText body, string key, string target, string value)
        {
            var map = body.Get(key);
            if (map?.Kind != YKind.Map) map = YText.Map();
            map.Flow = false;
            map.Raw = null;
            SettingsAssets.SetInOrdinalOrder(map, target, YText.Scalar(UnityYamlWriter.Scalar(value ?? "")));
            body.Set(key, map);
        }

        internal static void Store(string key, int value) =>
            SettingsAssets.Edit("ProjectSettings", body => body.Set(key, YText.Scalar(value.ToString(CultureInfo.InvariantCulture))));

        static NamedBuildTarget ApplicationIdentifierTarget
        {
            get
            {
                var active = EditorUserBuildSettingsInfo.ActiveGroup;
                return active == BuildTargetGroup.Standalone && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
                    ? NamedBuildTarget.Server : NamedBuildTarget.FromBuildTargetGroup(active);
            }
        }

        internal static void StandaloneBuildSubtargetChanged()
        {
            // Switching between Player and Server changes the active identifier right away.
            if (Compat.ProjectBuildIdentityMeasured && EditorUserBuildSettingsInfo.ActiveGroup == BuildTargetGroup.Standalone)
                ProjectInfo.Identifier = GetApplicationIdentifier(ApplicationIdentifierTarget);
        }

        public static string applicationIdentifier
        {
            get => GetApplicationIdentifier(ApplicationIdentifierTarget);
            set
            {
                if (!Compat.ProjectBuildIdentityMeasured)
                {
                    ProjectInfo.Identifier = value;
                    return;
                }
                Debug.LogWarning("PlayerSettings.applicationIdentifier only changes the identifier for the currently active platform. Please use SetApplicationIdentifier to set it for any platform");
                SetApplicationIdentifier(ApplicationIdentifierTarget, value);
            }
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Use PlayerSettings.applicationIdentifier instead (UnityUpgradable) -> UnityEditor.PlayerSettings.applicationIdentifier", true)]
        public static string bundleIdentifier
        {
            get
            {
                if (Compat.ProjectBuildIdentityMeasured) return applicationIdentifier;
                Stubs.NotImplemented("UnityEditor.PlayerSettings.bundleIdentifier");
                return null;
            }
            set
            {
                if (Compat.ProjectBuildIdentityMeasured) applicationIdentifier = value;
                else Stubs.NotImplemented("UnityEditor.PlayerSettings.bundleIdentifier");
            }
        }

        [Obsolete("Use GetApplicationIdentifier(NamedBuildTarget buildTarget) instead")]
        public static string GetApplicationIdentifier(BuildTargetGroup targetGroup) =>
            Compat.ProjectBuildIdentityMeasured
                ? GetApplicationIdentifier(NamedBuildTarget.FromBuildTargetGroup(targetGroup))
                : ProjectInfo.Identifier;

        public static string GetApplicationIdentifier(NamedBuildTarget buildTarget)
        {
            if (!Compat.ProjectBuildIdentityMeasured || string.IsNullOrEmpty(ProjectInfo.ProjectPath))
                return ProjectInfo.Identifier;
            return StoredTargetText("applicationIdentifier", buildTarget.TargetName)
                ?? NormalizeIdentifier($"com.{ProjectInfo.CompanyName}.{ProjectInfo.ProductName}", buildTarget);
        }

        [Obsolete("Use SetApplicationIdentifier(NamedBuildTarget buildTarget, string identifier) instead")]
        public static void SetApplicationIdentifier(BuildTargetGroup targetGroup, string identifier) =>
            SetApplicationIdentifier(Compat.ProjectBuildIdentityMeasured
                ? NamedBuildTarget.FromBuildTargetGroup(targetGroup) : NamedBuildTarget.Unknown, identifier);

        public static void SetApplicationIdentifier(NamedBuildTarget buildTarget, string identifier)
        {
            if (!Compat.ProjectBuildIdentityMeasured)
            {
                Stubs.NotImplemented("UnityEditor.PlayerSettings.SetApplicationIdentifier");
                return;
            }
            if (string.IsNullOrEmpty(buildTarget.TargetName))
                throw new ArgumentException($"The provided target platform group name ({buildTarget.TargetName}) is not valid.");

            string stored = NormalizeIdentifier(identifier, buildTarget);
            SettingsAssets.Edit("ProjectSettings", body =>
            {
                SetTargetText(body, "applicationIdentifier", buildTarget.TargetName, stored);
                body.Set("overrideDefaultApplicationIdentifier", YText.Scalar("1"));
            });
            if (buildTarget == ApplicationIdentifierTarget)
                ProjectInfo.Identifier = stored;
        }

        static string NormalizeIdentifier(string identifier, NamedBuildTarget target)
        {
            identifier ??= "";
            bool android = target == NamedBuildTarget.Android;
            var result = new StringBuilder(identifier.Length);
            foreach (byte b in Encoding.UTF8.GetBytes(identifier))
            {
                char c = (char)b;
                if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == (android ? '_' : '-'))
                    result.Append(c);
                else if (!android)
                    result.Append('-');
            }
            return result.ToString();
        }

        /// <summary>Whether the Play Mode test runner may run tests in players.</summary>
        internal static bool playModeTestRunnerEnabled
        {
            get => StoredInt("playModeTestRunnerEnabled", 0) != 0;
            set => Store("playModeTestRunnerEnabled", value ? 1 : 0);
        }

        public static bool resizableWindow
        {
            get => StoredInt("resizableWindow", 0) != 0;
            set => Store("resizableWindow", value ? 1 : 0);
        }

        public static FullScreenMode fullScreenMode
        {
            get => (FullScreenMode)StoredInt("fullscreenMode", (int)FullScreenMode.FullScreenWindow);
            set => Store("fullscreenMode", (int)value);
        }

        public partial class SplashScreen
        {
            public static bool show
            {
                get => StoredInt("m_ShowUnitySplashScreen", 1) != 0;
                set => Store("m_ShowUnitySplashScreen", value ? 1 : 0);
            }

            public static bool showUnityLogo
            {
                get => StoredInt("m_ShowUnitySplashLogo", 1) != 0;
                set => Store("m_ShowUnitySplashLogo", value ? 1 : 0);
            }
        }

        public partial class iOS
        {
            static string s_BuildNumber = "1";

            public static string buildNumber
            {
                get => Compat.ProjectBuildIdentityMeasured
                    ? StoredTargetText("buildNumber", NamedBuildTarget.iOS.TargetName) ?? s_BuildNumber
                    : s_BuildNumber;
                set
                {
                    s_BuildNumber = value;
                    if (Compat.ProjectBuildIdentityMeasured)
                        SettingsAssets.Edit("ProjectSettings", body => SetTargetText(body, "buildNumber", NamedBuildTarget.iOS.TargetName, value));
                }
            }

            public static iOSSdkVersion sdkVersion
            {
                get => (iOSSdkVersion)StoredInt("iPhoneSdkVersion", (int)iOSSdkVersion.DeviceSDK);
                set => Store("iPhoneSdkVersion", (int)value);
            }

            /// <summary>Stored as 1 (on), 2 (off) or 0 (not set: the Editor preference
            /// DefaultiOSAutomaticallySignBuild).</summary>
            public static bool appleEnableAutomaticSigning
            {
                get
                {
                    int stored = StoredInt("appleEnableAutomaticSigning", 0);
                    return stored == 0 ? EditorPrefs.GetBool("DefaultiOSAutomaticallySignBuild") : stored == 1;
                }
                set => Store("appleEnableAutomaticSigning", value ? 1 : 2);
            }

            /// <summary>The project's team, else the Editor preference DefaultiOSAutomaticSignTeamId.</summary>
            public static string appleDeveloperTeamID
            {
                get
                {
                    string stored = StoredText("appleDeveloperTeamID");
                    return stored.Length < 1 ? EditorPrefs.GetString("DefaultiOSAutomaticSignTeamId") : stored;
                }
                set => Save("appleDeveloperTeamID", value);
            }

            /// <summary>The project's profile, else the Editor preference DefaultiOSProvisioningProfileUUID.</summary>
            public static string iOSManualProvisioningProfileID
            {
                get
                {
                    string stored = StoredText("iOSManualSigningProvisioningProfileID");
                    return string.IsNullOrEmpty(stored) ? EditorPrefs.GetString("DefaultiOSProvisioningProfileUUID") : stored;
                }
                set => Save("iOSManualSigningProvisioningProfileID", value);
            }

            public static ProvisioningProfileType iOSManualProvisioningProfileType
            {
                get => (ProvisioningProfileType)StoredInt("iOSManualSigningProvisioningProfileType", 0);
                set => Store("iOSManualSigningProvisioningProfileType", (int)value);
            }

            /// <summary>The project's profile, else the Editor preference DefaulttvOSProvisioningProfileUUID.</summary>
            public static string tvOSManualProvisioningProfileID
            {
                get
                {
                    string stored = StoredText("tvOSManualSigningProvisioningProfileID");
                    return string.IsNullOrEmpty(stored) ? EditorPrefs.GetString("DefaulttvOSProvisioningProfileUUID") : stored;
                }
                set => Save("tvOSManualSigningProvisioningProfileID", value);
            }

            public static ProvisioningProfileType tvOSManualProvisioningProfileType
            {
                get => (ProvisioningProfileType)StoredInt("tvOSManualSigningProvisioningProfileType", 0);
                set => Store("tvOSManualSigningProvisioningProfileType", (int)value);
            }
        }

        public partial class tvOS
        {
            public static tvOSSdkVersion sdkVersion
            {
                get => (tvOSSdkVersion)StoredInt("tvOSSdkVersion", (int)tvOSSdkVersion.Device);
                set => Store("tvOSSdkVersion", (int)value);
            }
        }

        public sealed partial class WebGL
        {
            public static bool closeOnQuit
            {
                get => StoredInt("webGLCloseOnQuit", 0) != 0;
                set => Store("webGLCloseOnQuit", value ? 1 : 0);
            }
        }
    }

    public partial class EditorUserBuildSettings
    {
        /// <summary>The active build target's group: the --target the game runs for.</summary>
        internal static BuildTargetGroup activeBuildTargetGroup => EditorUserBuildSettingsInfo.ActiveGroup;
    }
}
