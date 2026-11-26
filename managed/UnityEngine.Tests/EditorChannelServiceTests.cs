using System.Collections.Generic;
using System.Linq;
using UnityEditor.MPE;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Editor's channel service, interface language and collaboration answers: the
    /// measured family's (tests/projects/editorchannels) and the not-implemented notice of others.</summary>
    public class EditorChannelServiceTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(System.Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        [Fact]
        public void MeasuredFamilyAnswersAsTheEditor()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // tests/projects/editorchannels mpe.id.before and mpe.id.name.
            Assert.Equal(-332022498, ChannelService.ChannelNameToId("probe.channel"));
            Assert.Equal(714770103, ChannelService.ChannelNameToId(""));
            ChannelService.Start();
            try
            {
                Assert.True(ChannelService.IsRunning());
                Assert.Equal(new[] { "echo", "echoAll", "event", "status" },
                    ChannelService.GetChannelList().Select(c => c.name).OrderBy(n => n, System.StringComparer.Ordinal));
                System.Action<int, byte[]> handler = (client, data) => { };
                using (new ChannelScope("tests.scope", handler))
                    Assert.Contains(ChannelService.GetChannelList(), c => c.name == "tests.scope");
                Assert.DoesNotContain(ChannelService.GetChannelList(), c => c.name == "tests.scope");
            }
            finally { ChannelService.Stop(); }
            Assert.False(ChannelService.IsRunning());
            // tests/projects/uvmetrics collab.IsCollabEnabledForCurrentProject.
            Assert.False(UnityEditor.Collaboration.Collab.instance.IsCollabEnabledForCurrentProject());
        }

        [Fact]
        public void OtherFamiliesReportTheAnswersNotImplemented()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                Assert.Equal(0, ChannelService.ChannelNameToId("probe.channel"));
                ChannelService.Start();
                Assert.False(ChannelService.IsRunning());
                Assert.Empty(ChannelService.GetChannelList());
                Assert.Null(ChannelService.GetOrCreateChannel("tests.unmeasured", (client, data) => { }));
                Assert.Null(UnityEditor.Collaboration.Collab.instance);
                var collab = (UnityEditor.Collaboration.Collab)System.Activator.CreateInstance(typeof(UnityEditor.Collaboration.Collab), nonPublic: true);
                Assert.False(collab.IsCollabEnabledForCurrentProject());
                Assert.False(UnityEditor.LocalizationDatabase.enableEditorLocalization);
                Assert.Null(UnityEditor.LocalizationDatabase.GetCulture(SystemLanguage.German));
                Assert.Null(UnityEditor.LocalizationDatabase.GetLocalizedString("Play"));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            foreach (var member in new[] { "ChannelService.ChannelNameToId", "ChannelService.Start", "ChannelService.IsRunning",
                         "ChannelService.GetChannelList", "ChannelService.GetOrCreateChannel", "Collab.instance", "Collab.IsCollabEnabledForCurrentProject",
                         "LocalizationDatabase.enableEditorLocalization", "LocalizationDatabase.GetCulture", "LocalizationDatabase.GetLocalizedString" })
                Assert.Contains(capture.Lines, line => line.Contains(member + " is not implemented"));
        }

        /// <summary>The Editor answers EditorPrefs Editor.kEnableEditorLocalization (measured in
        /// tests/projects/editorlocalization); Unbound keeps its answer without the key and says so
        /// when the preferences hold another value (injected here, nothing written).</summary>
        [Fact]
        public void LocalizationNamesAPreferenceItDoesNotFollow()
        {
            const System.Reflection.BindingFlags Static = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var prefs = typeof(UnityEditor.EditorPrefs);
            var values = prefs.GetField("s_Values", Static);
            var deleted = prefs.GetField("s_Deleted", Static);
            var db = typeof(UnityEditor.LocalizationDatabase);
            var oldValues = values.GetValue(null);
            var oldDeleted = deleted.GetValue(null);
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Measured);
                bool answer = System.OperatingSystem.IsMacOS();
                values.SetValue(null, new System.Collections.Generic.Dictionary<string, object> { ["Editor.kEnableEditorLocalization"] = answer ? 0 : 1 });
                deleted.SetValue(null, new System.Collections.Generic.HashSet<string>());
                db.GetField("s_PreferenceChecked", Static).SetValue(null, false);
                db.GetField("s_EnableSet", Static).SetValue(null, false);
                Unbound.Runtime.Stubs.Forget("UnityEditor.LocalizationDatabase.enableEditorLocalization");
                Debug.unityLogger.logHandler = capture;
                Assert.Equal(answer, UnityEditor.LocalizationDatabase.enableEditorLocalization);
                Assert.Contains(capture.Lines, line => line.Contains("enableEditorLocalization answers") && line.Contains("Editor.kEnableEditorLocalization"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                values.SetValue(null, oldValues);
                deleted.SetValue(null, oldDeleted);
            }
        }

        /// <summary>tests/projects/editorlocalization (windowed Linux Editor, clean preferences): the
        /// culture codes, identity translations and the localization folder; an installation with
        /// files in its Localization folder (language packs) gets a notice.</summary>
        [Fact]
        public void LocalizationAnswersAsTheEditorAndNamesLanguagePacks()
        {
            var dataPath = UnityEditor.UnityInstallInfo.DataPath;
            var dir = System.IO.Directory.CreateTempSubdirectory("unbound-l10n-");
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Measured);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir.FullName, "Localization"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir.FullName, "Localization", ".DS_Store"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir.FullName, "Localization", "ja.po"), "");
                UnityEditor.UnityInstallInfo.DataPath = dir.FullName;
                typeof(UnityEditor.LocalizationDatabase).GetField("s_PacksChecked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, false);
                Unbound.Runtime.Stubs.Forget("UnityEditor.LocalizationDatabase language packs");
                Debug.unityLogger.logHandler = capture;
                Assert.Equal("zh-hans", UnityEditor.LocalizationDatabase.GetCulture(SystemLanguage.ChineseSimplified));
                Assert.Equal("hu", UnityEditor.LocalizationDatabase.GetCulture(SystemLanguage.Hungarian));
                Assert.Equal("", UnityEditor.LocalizationDatabase.GetCulture(SystemLanguage.Unknown));
                Assert.Equal("Play", UnityEditor.LocalizationDatabase.GetLocalizedStringInLang(SystemLanguage.Japanese, "Play"));
                Assert.Equal(new[] { SystemLanguage.English }, UnityEditor.LocalizationDatabase.GetAvailableEditorLanguages());
                Assert.Equal(dir.FullName + "/Localization", UnityEditor.LocalizationDatabase.GetLocalizationResourceFolder());
                Assert.Equal(".", UnityEditor.LocalizationDatabase.noLocalizationGroupName);
                Assert.Contains(capture.Lines, line => line.Contains("language packs are not read"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                UnityEditor.UnityInstallInfo.DataPath = dataPath;
                try { dir.Delete(true); } catch (System.IO.IOException) { }
            }
        }
    }
}
