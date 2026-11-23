using System.Globalization;
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

        internal static void Store(string key, int value) =>
            SettingsAssets.Edit("ProjectSettings", body => body.Set(key, YText.Scalar(value.ToString(CultureInfo.InvariantCulture))));

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
