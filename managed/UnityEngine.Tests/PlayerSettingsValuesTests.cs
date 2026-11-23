using System.IO;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>PlayerSettings values kept in ProjectSettings.asset, as the Unity 6000.0.71f1 Editor
    /// reads and sets them in Play mode (tests/projects/playersettingsvalues).</summary>
    public sealed class PlayerSettingsValuesTests
    {
        static string Repo()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(dir, "tests", "projects"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        [Fact]
        public void ValuesAreTheProjectsAndSetOnesHoldWithoutSavingInPlayMode()
        {
            var project = Path.Combine(Path.GetTempPath(), "unbound-player-settings-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            var settings = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset");
            File.Copy(Path.Combine(Repo(), "tests", "projects", "playersettingsvalues", "ProjectSettings", "ProjectSettings.asset"), settings);
            var original = File.ReadAllText(settings);
            var before = ProjectInfo.ProjectPath;
            bool playing = EditorSession.IsPlaying;
            ProjectInfo.ProjectPath = project;
            EditorSession.IsPlaying = true;
            SettingsAssets.Reset();
            try
            {
                // The probe's "project" row.
                Assert.False(PlayerSettings.SplashScreen.show);
                Assert.False(PlayerSettings.SplashScreen.showUnityLogo);
                Assert.True(PlayerSettings.playModeTestRunnerEnabled);
                Assert.True(PlayerSettings.resizableWindow);
                Assert.Equal(FullScreenMode.Windowed, PlayerSettings.fullScreenMode);
                Assert.Equal(iOSSdkVersion.SimulatorSDK, PlayerSettings.iOS.sdkVersion);
                Assert.Equal(tvOSSdkVersion.Simulator, PlayerSettings.tvOS.sdkVersion);
                Assert.True(PlayerSettings.iOS.appleEnableAutomaticSigning);
                Assert.Equal("ABCDE12345", PlayerSettings.iOS.appleDeveloperTeamID);
                Assert.Equal("ios-profile", PlayerSettings.iOS.iOSManualProvisioningProfileID);
                Assert.Equal(ProvisioningProfileType.Distribution, PlayerSettings.iOS.iOSManualProvisioningProfileType);
                Assert.Equal("tv-profile", PlayerSettings.iOS.tvOSManualProvisioningProfileID);
                Assert.Equal(ProvisioningProfileType.Development, PlayerSettings.iOS.tvOSManualProvisioningProfileType);
                Assert.True(PlayerSettings.WebGL.closeOnQuit);

                // The "set" row: in effect at once; the file is not written in Play mode.
                PlayerSettings.SplashScreen.show = true;
                PlayerSettings.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
                PlayerSettings.iOS.appleEnableAutomaticSigning = false;
                PlayerSettings.iOS.iOSManualProvisioningProfileType = ProvisioningProfileType.Development;
                PlayerSettings.WebGL.closeOnQuit = false;
                Assert.True(PlayerSettings.SplashScreen.show);
                Assert.Equal(FullScreenMode.ExclusiveFullScreen, PlayerSettings.fullScreenMode);
                Assert.Equal(iOSSdkVersion.DeviceSDK, PlayerSettings.iOS.sdkVersion);
                Assert.False(PlayerSettings.iOS.appleEnableAutomaticSigning);
                Assert.Equal(ProvisioningProfileType.Development, PlayerSettings.iOS.iOSManualProvisioningProfileType);
                Assert.False(PlayerSettings.WebGL.closeOnQuit);
                PlayerSettings.iOS.appleEnableAutomaticSigning = true;
                Assert.True(PlayerSettings.iOS.appleEnableAutomaticSigning);
                AssetDatabase.SaveAssets();
                Assert.Equal(original, File.ReadAllText(settings));
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                EditorSession.IsPlaying = playing;
                SettingsAssets.Reset();
                Directory.Delete(project, true);
            }
        }
    }
}
