using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Build;
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

        [Fact]
        public void BuildIdentityNormalizesPerTargetRefreshesRuntimeAndSavesInEditMode()
        {
            var project = Path.Combine(Path.GetTempPath(), "unbound-build-identity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            var settings = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset");
            var text = File.ReadAllText(Path.Combine(Repo(), "tests", "projects", "scenesave", "ProjectSettings", "ProjectSettings.asset"))
                .Replace("  productName: testrunner\n", "  productName: My project\n")
                .Replace("  applicationIdentifier: {}\n", "  applicationIdentifier:\n    Server: \n");
            File.WriteAllText(settings, text);

            var beforeProject = ProjectInfo.ProjectPath;
            var beforeCompany = ProjectInfo.CompanyName;
            var beforeProduct = ProjectInfo.ProductName;
            var beforeIdentifier = ProjectInfo.Identifier;
            var beforeVersion = ProjectInfo.BundleVersion;
            var beforeGroup = EditorUserBuildSettingsInfo.ActiveGroup;
            var beforeSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            bool playing = EditorSession.IsPlaying;

            ProjectInfo.ProjectPath = project;
            ProjectInfo.CompanyName = "DefaultCompany";
            ProjectInfo.ProductName = "My project";
            ProjectInfo.Identifier = "com.DefaultCompany.Myproject";
            EditorUserBuildSettingsInfo.ActiveGroup = BuildTargetGroup.Standalone;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            EditorSession.IsPlaying = false;
            SettingsAssets.Reset();
            try
            {
                using (TestRelease.Pin(TestRelease.Measured))
                {
                    PlayerSettings.RefreshApplicationIdentifier();
                    Assert.Equal("com.DefaultCompany.My-project", Application.identifier);
                    Assert.Equal("com.DefaultCompany.Myproject", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
                    Assert.Equal("", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Server));

                    // Switching Standalone subtarget to Server and back refreshes Application.identifier.
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
                    Assert.Equal("", Application.identifier);
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                    Assert.Equal("com.DefaultCompany.My-project", Application.identifier);

                    // Renaming companyName or productName updates derived default identifiers and Application.identifier.
                    PlayerSettings.companyName = "Probe Laß _ Inc";
                    Assert.Equal("com.Probe-La-----Inc.My-project", PlayerSettings.applicationIdentifier);
                    Assert.Equal("com.Probe-La-----Inc.My-project", Application.identifier);
                    Assert.Equal("com.ProbeLa_Inc.Myproject", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));

                    PlayerSettings.productName = "New App";
                    Assert.Equal("com.Probe-La-----Inc.New-App", PlayerSettings.applicationIdentifier);
                    Assert.Equal("com.Probe-La-----Inc.New-App", Application.identifier);

                    // Explicit target normalization: Android strips non-[A-Za-z0-9._], others map each UTF-8 byte to '-'.
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "Com.Probe _ Space-é.工具");
                    Assert.Equal("Com.Probe_Space.", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "Com.Probe _ Space-é.工具");
                    Assert.Equal("Com.Probe---Space---.------", PlayerSettings.applicationIdentifier);
                    Assert.Equal("Com.Probe---Space---.------", Application.identifier);

                    // Once explicitly set for Standalone, renaming companyName keeps the explicit identifier.
                    PlayerSettings.companyName = "Later Co";
                    Assert.Equal("Com.Probe---Space---.------", PlayerSettings.applicationIdentifier);
                    Assert.Equal("Com.Probe---Space---.------", Application.identifier);

                    // Unknown target throws ArgumentException.
                    Assert.Throws<ArgumentException>(() => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Unknown, "net.probe.x"));

                    // bundleVersion normalizes null to empty string consistently between memory and YAML.
                    PlayerSettings.bundleVersion = null;
                    Assert.Equal("", PlayerSettings.bundleVersion);
                    Assert.Equal("", Application.version);

                    // SaveAssets persists normalized identifiers, override flag, and version changes to disk.
                    PlayerSettings.bundleVersion = "2.4.1";
                    PlayerSettings.Android.bundleVersionCode = 42;
                    PlayerSettings.iOS.buildNumber = "9.1";
                    AssetDatabase.SaveAssets();
                    var saved = File.ReadAllText(settings);
                    Assert.Contains("overrideDefaultApplicationIdentifier: 1", saved);
                    Assert.Contains("Android: Com.Probe_Space.", saved);
                    Assert.Contains("Standalone: Com.Probe---Space---.------", saved);
                    Assert.Contains("bundleVersion: 2.4.1", saved);
                    Assert.Contains("AndroidBundleVersionCode: 42", saved);
                    Assert.Contains("iPhone: 9.1", saved);

                    // SerializedObject.ApplyModifiedProperties on ProjectSettings.asset syncs BundleVersion and Application.identifier.
                    var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
                    so.FindProperty("bundleVersion").stringValue = "3.0.0";
                    var ids = so.FindProperty("applicationIdentifier");
                    bool foundStandalone = false;
                    for (int i = 0; i < ids.arraySize; i++)
                    {
                        var pair = ids.GetArrayElementAtIndex(i);
                        if (pair.FindPropertyRelative("first").stringValue == "Standalone")
                        {
                            pair.FindPropertyRelative("second").stringValue = "net.serialized.app";
                            foundStandalone = true;
                        }
                    }
                    Assert.True(foundStandalone);
                    Assert.True(so.ApplyModifiedProperties());
                    Assert.Equal("3.0.0", PlayerSettings.bundleVersion);
                    Assert.Equal("3.0.0", Application.version);
                    Assert.Equal("net.serialized.app", Application.identifier);
                }
            }
            finally
            {
                ProjectInfo.ProjectPath = beforeProject;
                ProjectInfo.CompanyName = beforeCompany;
                ProjectInfo.ProductName = beforeProduct;
                ProjectInfo.Identifier = beforeIdentifier;
                ProjectInfo.BundleVersion = beforeVersion;
                EditorUserBuildSettingsInfo.ActiveGroup = beforeGroup;
                EditorUserBuildSettings.standaloneBuildSubtarget = beforeSubtarget;
                EditorSession.IsPlaying = playing;
                SettingsAssets.Reset();
                Directory.Delete(project, true);
            }
        }

        [Fact]
        public void BuildIdentityReportsMissingSettingsAndRespectsUnmeasuredRelease()
        {
            var emptyDir = Path.Combine(Path.GetTempPath(), "unbound-no-settings-" + Guid.NewGuid().ToString("N"));
            var populatedDir = Path.Combine(Path.GetTempPath(), "unbound-unmeasured-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(emptyDir);
            Directory.CreateDirectory(Path.Combine(populatedDir, "ProjectSettings"));
            var populatedSettings = Path.Combine(populatedDir, "ProjectSettings", "ProjectSettings.asset");
            var originalYaml = File.ReadAllText(Path.Combine(Repo(), "tests", "projects", "scenesave", "ProjectSettings", "ProjectSettings.asset"))
                .Replace("  productName: testrunner\n", "  productName: My project\n")
                .Replace("  applicationIdentifier: {}\n", "  applicationIdentifier:\n    Android: com.saved.android\n    Standalone: com.saved.standalone\n");
            File.WriteAllText(populatedSettings, originalYaml);

            var beforeProject = ProjectInfo.ProjectPath;
            var beforeCompany = ProjectInfo.CompanyName;
            var beforeProduct = ProjectInfo.ProductName;
            var beforeIdentifier = ProjectInfo.Identifier;
            var beforeVersion = ProjectInfo.BundleVersion;
            var beforeGroup = EditorUserBuildSettingsInfo.ActiveGroup;
            var beforeSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            bool playing = EditorSession.IsPlaying;
            try
            {
                // Missing ProjectSettings.asset under measured release reports via Stubs.Ignored on
                // SetApplicationIdentifier and keeps iOS.buildNumber writes in memory.
                ProjectInfo.ProjectPath = emptyDir;
                ProjectInfo.Identifier = "com.keep.me";
                SettingsAssets.Reset();
                int hitsBefore = StubHits("UnityEditor.PlayerSettings.SetApplicationIdentifier");
                using (TestRelease.Pin(TestRelease.Measured))
                {
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.new.id");
                    Assert.Equal("com.keep.me", Application.identifier);
                    Assert.Equal(hitsBefore + 1, StubHits("UnityEditor.PlayerSettings.SetApplicationIdentifier"));
                    Assert.Equal("0", PlayerSettings.iOS.buildNumber);
                    PlayerSettings.iOS.buildNumber = "27.3";
                    Assert.Equal("27.3", PlayerSettings.iOS.buildNumber);
                    PlayerSettings.iOS.buildNumber = null;
                    Assert.Equal("", PlayerSettings.iOS.buildNumber);
                }

                // Populated ProjectSettings.asset under an unmeasured release keeps prior fallbacks,
                // does not modify the YAML on disk, and hits Stubs.NotImplemented.
                ProjectInfo.ProjectPath = populatedDir;
                ProjectInfo.CompanyName = "DefaultCompany";
                ProjectInfo.ProductName = "My project";
                ProjectInfo.Identifier = "com.unmeasured.fallback";
                ProjectInfo.BundleVersion = "0.9";
                EditorUserBuildSettingsInfo.ActiveGroup = BuildTargetGroup.Standalone;
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                EditorSession.IsPlaying = false;
                SettingsAssets.Reset();

                int setHits = StubHits("UnityEditor.PlayerSettings.SetApplicationIdentifier");
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    Assert.Equal("com.unmeasured.fallback", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
                    Assert.Equal("com.unmeasured.fallback", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone));
                    Assert.Equal(1, PlayerSettings.Android.bundleVersionCode);
                    Assert.Equal("1", PlayerSettings.iOS.buildNumber);

                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.other.id");
                    Assert.Equal("com.unmeasured.fallback", Application.identifier);
                    Assert.Equal(setHits + 1, StubHits("UnityEditor.PlayerSettings.SetApplicationIdentifier"));

                    PlayerSettings.bundleVersion = "9.9";
                    PlayerSettings.Android.bundleVersionCode = 77;
                    PlayerSettings.iOS.buildNumber = null;
                    Assert.Null(PlayerSettings.iOS.buildNumber);
                    PlayerSettings.iOS.buildNumber = "7.7";
                    Assert.Equal("7.7", PlayerSettings.iOS.buildNumber);
                    AssetDatabase.SaveAssets();
                    Assert.Equal(originalYaml, File.ReadAllText(populatedSettings));
                }

                // SettingsAssets.Reset clears static fallback fields so writes do not leak across projects.
                SettingsAssets.Reset();
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    Assert.Equal(1, PlayerSettings.Android.bundleVersionCode);
                    Assert.Equal("1", PlayerSettings.iOS.buildNumber);
                }
            }
            finally
            {
                ProjectInfo.ProjectPath = beforeProject;
                ProjectInfo.CompanyName = beforeCompany;
                ProjectInfo.ProductName = beforeProduct;
                ProjectInfo.Identifier = beforeIdentifier;
                ProjectInfo.BundleVersion = beforeVersion;
                EditorUserBuildSettingsInfo.ActiveGroup = beforeGroup;
                EditorUserBuildSettings.standaloneBuildSubtarget = beforeSubtarget;
                EditorSession.IsPlaying = playing;
                SettingsAssets.Reset();
                Directory.Delete(emptyDir, true);
                Directory.Delete(populatedDir, true);
            }
        }

        static int StubHits(string member)
        {
            foreach (var hit in Stubs.HitReport())
                if (hit.Key == member) return hit.Value;
            return 0;
        }
    }
}
