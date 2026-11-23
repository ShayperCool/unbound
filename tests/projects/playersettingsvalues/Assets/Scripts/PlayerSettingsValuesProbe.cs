using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PlayerSettingsValuesProbe
{
    /// <summary>
    /// PlayerSettings and EditorUserBuildSettings values that editor scripts read and set: the
    /// splash screen, the standalone window, the play mode test runner, the Apple SDK and signing
    /// settings and WebGL's close on quit, as the project's ProjectSettings.asset has them (every one
    /// changed from its default), after setting each to another value, and the active build target
    /// group.
    /// </summary>
    public class PlayerSettingsValuesBehaviour : MonoBehaviour
    {
        const string Variable = "PLAYER_SETTINGS_VALUES_OUT";
        readonly StringBuilder m_Out = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
            new GameObject("probe").AddComponent<PlayerSettingsValuesBehaviour>();
        }

        const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        static object Read(Type type, string name) => type.GetProperty(name, Internal).GetValue(null);

        static void Write(Type type, string name, object value) => type.GetProperty(name, Internal).SetValue(null, value);

        string Values() => string.Join("|", new[]
        {
            "splash.show=" + PlayerSettings.SplashScreen.show,
            "splash.showUnityLogo=" + PlayerSettings.SplashScreen.showUnityLogo,
            "playModeTestRunnerEnabled=" + Read(typeof(PlayerSettings), "playModeTestRunnerEnabled"),
            "resizableWindow=" + PlayerSettings.resizableWindow,
            "fullScreenMode=" + PlayerSettings.fullScreenMode,
            "iOS.sdkVersion=" + PlayerSettings.iOS.sdkVersion,
            "tvOS.sdkVersion=" + PlayerSettings.tvOS.sdkVersion,
            "iOS.appleEnableAutomaticSigning=" + PlayerSettings.iOS.appleEnableAutomaticSigning,
            "iOS.appleDeveloperTeamID=" + PlayerSettings.iOS.appleDeveloperTeamID,
            "iOS.iOSManualProvisioningProfileID=" + PlayerSettings.iOS.iOSManualProvisioningProfileID,
            "iOS.iOSManualProvisioningProfileType=" + PlayerSettings.iOS.iOSManualProvisioningProfileType,
            "iOS.tvOSManualProvisioningProfileID=" + PlayerSettings.iOS.tvOSManualProvisioningProfileID,
            "iOS.tvOSManualProvisioningProfileType=" + PlayerSettings.iOS.tvOSManualProvisioningProfileType,
            "WebGL.closeOnQuit=" + PlayerSettings.WebGL.closeOnQuit,
        });

        void Start()
        {
            var dir = Environment.GetEnvironmentVariable(Variable);
            Put("project", Values());
            // The group only: the standalone target itself is the OS's.
            Put("activeBuildTargetGroup", Read(typeof(EditorUserBuildSettings), "activeBuildTargetGroup").ToString());

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            Write(typeof(PlayerSettings), "playModeTestRunnerEnabled", false);
            PlayerSettings.resizableWindow = false;
            PlayerSettings.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Device;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.appleDeveloperTeamID = "TEAM2";
            PlayerSettings.iOS.iOSManualProvisioningProfileID = "ios-2";
            PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Development;
            PlayerSettings.iOS.tvOSManualProvisioningProfileID = null;
            PlayerSettings.iOS.tvOSManualProvisioningProfileType = ProvisioningProfileType.Automatic;
            PlayerSettings.WebGL.closeOnQuit = false;
            Put("set", Values());

            // The values as the file has them once saved.
            AssetDatabase.SaveAssets();
            var keys = new[] { "m_ShowUnitySplashScreen", "m_ShowUnitySplashLogo", "playModeTestRunnerEnabled", "resizableWindow", "fullscreenMode", "iPhoneSdkVersion",
                "tvOSSdkVersion", "appleEnableAutomaticSigning", "appleDeveloperTeamID", "iOSManualSigningProvisioningProfileID", "iOSManualSigningProvisioningProfileType",
                "tvOSManualSigningProvisioningProfileID", "tvOSManualSigningProvisioningProfileType", "webGLCloseOnQuit" };
            var lines = File.ReadAllLines("ProjectSettings/ProjectSettings.asset");
            Put("set.file", string.Join("|", keys.Select(k => lines.FirstOrDefault(l => l.StartsWith("  " + k + ":", StringComparison.Ordinal))?.Trim() ?? k + ":<missing>")));

            // Back to true (an automatic signing value set twice).
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            Put("set.automaticSigningAgain", "iOS.appleEnableAutomaticSigning=" + PlayerSettings.iOS.appleEnableAutomaticSigning);

            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text ?? "<null>")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
