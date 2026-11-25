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
                Assert.False(UnityEditor.LocalizationDatabase.enableEditorLocalization);
            }
            finally { Debug.unityLogger.logHandler = previous; }
            foreach (var member in new[] { "ChannelService.ChannelNameToId", "ChannelService.Start", "ChannelService.IsRunning",
                         "ChannelService.GetChannelList", "ChannelService.GetOrCreateChannel", "Collab.instance",
                         "LocalizationDatabase.enableEditorLocalization" })
                Assert.Contains(capture.Lines, line => line.Contains(member + " is not implemented"));
        }
    }
}
