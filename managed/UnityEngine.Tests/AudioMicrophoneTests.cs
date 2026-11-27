using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>UnityEngine.Microphone validation, device aliasing, ring advancement, clip
    /// destruction and AudioSettings.Reset semantics measured on Unity 6000.0.71f1
    /// (tests/projects/audiomic and tests/projects/platform).</summary>
    [Collection("Frames")]
    public class AudioMicrophoneTests : IDisposable
    {
        readonly AudioBackend m_Previous = AudioBackend.Current;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly List<(LogType Type, string Message)> m_Logs = new List<(LogType, string)>();
        readonly ILogHandler m_PrevLogHandler;

        sealed class CapturingHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<(LogType Type, string Message)> m_Logs;
            public CapturingHandler(ILogHandler inner, List<(LogType, string)> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                string msg = args == null || args.Length == 0 ? format : string.Format(format, args);
                lock (m_Logs) m_Logs.Add((logType, msg));
                m_Inner.LogFormat(logType, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        public AudioMicrophoneTests()
        {
            m_Backend.VirtualMicDevices.Add("Default Input Device");
            m_Backend.VirtualMicDevices.Add("Monitor of Headless");
            AudioBackend.Use(m_Backend);
            m_PrevLogHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CapturingHandler(m_PrevLogHandler, m_Logs);
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_PrevLogHandler;
            AudioBackend.Use(m_Previous);
        }

        List<(LogType Type, string Message)> TakeLogs()
        {
            lock (m_Logs) { var copy = m_Logs.ToList(); m_Logs.Clear(); return copy; }
        }

        [Fact]
        public void DevicesAndCapsReportAvailableInputs()
        {
            Assert.Equal(new[] { "Default Input Device", "Monitor of Headless" }, Microphone.devices);
            Microphone.GetDeviceCaps(null, out int lo, out int hi);
            Assert.Equal((0, 0), (lo, hi));
            Microphone.GetDeviceCaps("no such device", out lo, out hi);
            Assert.Equal((0, 0), (lo, hi));
        }

        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void StartValidationMatchesUnityErrorOrderAndBoundary(string release)
        {
            using var pinned = TestRelease.Pin(release);

            var exDev = Assert.Throws<ArgumentException>(() => Microphone.Start("no such device", false, 0, 0));
            Assert.Equal("Couldn't acquire device ID for device name no such device", exDev.Message);

            var exZeroLen = Assert.Throws<ArgumentException>(() => Microphone.Start(null, false, 0, 44100));
            Assert.Equal("Length of recording must be greater than zero seconds (was: 0 seconds)", exZeroLen.Message);

            var exNegLen = Assert.Throws<ArgumentException>(() => Microphone.Start("", false, -5, 44100));
            Assert.Equal("Length of recording must be greater than zero seconds (was: -5 seconds)", exNegLen.Message);

            var exOverHour = Assert.Throws<ArgumentException>(() => Microphone.Start(null, false, 3601, 44100));
            Assert.Equal("Length of recording must be less than one hour (was: 3601 seconds)", exOverHour.Message);

            var exFreq = Assert.Throws<ArgumentException>(() => Microphone.Start(null, false, 1, 0));
            Assert.Equal("Frequency of recording must be greater than zero (was: 0 Hz)", exFreq.Message);

            TakeLogs();
            if (Compat.MicrophoneMeasured)
            {
                Assert.Null(Microphone.Start(null, false, 3600, 44100));
                Assert.Contains(TakeLogs(), l => l.Type == LogType.Error && l.Message == "Length of the recording must be less than one hour");
            }
            else
            {
                Assert.Throws<ArgumentException>(() => Microphone.Start(null, false, 3600, 44100));
            }
        }

        [Fact]
        public void StartRecordsMonoClipAliasesDefaultDeviceAndStopsOnCompletionOrEnd()
        {
            var clip = Microphone.Start(null, false, 1, 8);
            Assert.NotNull(clip);
            Assert.Equal("Microphone", clip.name);
            Assert.Equal((1, 8, 8), (clip.channels, clip.frequency, clip.samples));
            Assert.Equal(AudioClipLoadType.DecompressOnLoad, clip.loadType);
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);

            Assert.True(Microphone.IsRecording(null));
            Assert.True(Microphone.IsRecording(""));
            Assert.True(Microphone.IsRecording("Default Input Device"));
            Assert.False(Microphone.IsRecording("Monitor of Headless"));
            Assert.False(Microphone.IsRecording("no such device"));
            Assert.True(Microphone.isAnyDeviceRecording);

            m_Backend.AdvanceVirtualMic(null, new[] { 0.1f, 0.2f, 0.3f });
            Assert.Equal(3, Microphone.GetPosition(null));
            Assert.Equal(3, Microphone.GetPosition(""));
            Assert.Equal(3, Microphone.GetPosition("Default Input Device"));

            var read = new float[3];
            Assert.True(clip.GetData(read, 0));
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, read);

            // Non-looping finishes at the clip's end and resets position to 0.
            m_Backend.AdvanceVirtualMic(null, new[] { 0.4f, 0.5f, 0.6f, 0.7f, 0.8f });
            Assert.False(Microphone.IsRecording(null));
            Assert.Equal(0, Microphone.GetPosition(null));
            Assert.False(Microphone.isAnyDeviceRecording);

            var all = new float[8];
            Assert.True(clip.GetData(all, 0));
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f }, all);
            Object.DestroyImmediate(clip);
        }

        [Fact]
        public void LoopingWrapsAndDoubleStartDestroyAndResetStopRecording()
        {
            var clip1 = Microphone.Start("Default Input Device", true, 1, 4);
            m_Backend.AdvanceVirtualMic(null, new[] { 1f, 2f, 3f, 4f, 5f });
            Assert.True(Microphone.IsRecording(null));
            Assert.Equal(1, Microphone.GetPosition(null));

            var clip2 = Microphone.Start("", true, 2, 4);
            Assert.NotNull(clip2);
            Assert.Equal(8, clip2.samples);
            Assert.True(Microphone.IsRecording("Default Input Device"));

            // Destroying the active clip stops recording (tests/projects/audiomic destroy_while_rec).
            Object.DestroyImmediate(clip2);
            Assert.False(Microphone.IsRecording(null));
            Assert.Equal(0, Microphone.GetPosition(null));

            // AudioSettings.Reset stops active recording (tests/projects/audiomic reset.after.*).
            var clip3 = Microphone.Start(null, true, 1, 8);
            Assert.True(Microphone.IsRecording(null));
            Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration()));
            Assert.False(Microphone.IsRecording(null));
            Assert.Equal(0, Microphone.GetPosition(null));

            Object.DestroyImmediate(clip1);
            Object.DestroyImmediate(clip3);
        }

        [Fact]
        public void NoDevicesOrDisabledAudioThrowsOnStart()
        {
            m_Backend.VirtualMicDevices.Clear();
            Assert.Empty(Microphone.devices);
            Assert.Throws<ArgumentException>(() => Microphone.Start(null, false, 1, 44100));
            Assert.False(Microphone.IsRecording(null));
            Assert.Equal(0, Microphone.GetPosition(null));
            Microphone.End(null);
        }
    }
}
