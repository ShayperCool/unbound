using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.Networking;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Tracker module AudioClip (.mod/.xm/.s3m/.it) lifecycle, GetData/SetData, cloning
    /// and UnityWebRequestMultimedia download rules measured on Unity 6000.0.71f1
    /// (tests/projects/audiotracker).</summary>
    [Collection("Frames")]
    public class AudioTrackerTests : IDisposable
    {
        readonly WebRequestTestLoop m_Loop = new WebRequestTestLoop();
        readonly AudioBackend m_Previous = AudioBackend.Current;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly string m_Directory = Path.Combine(Path.GetTempPath(), $"unbound-tracker-{Guid.NewGuid():N}");
        readonly List<(LogType Type, string Message)> m_Logs = new List<(LogType, string)>();
        readonly ILogHandler m_PrevLogHandler;
        int m_DecodeCalls;

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

        public AudioTrackerTests()
        {
            Directory.CreateDirectory(m_Directory);
            m_Backend.Downloads = Decode;
            AudioBackend.Use(m_Backend);
            m_PrevLogHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CapturingHandler(m_PrevLogHandler, m_Logs);
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_PrevLogHandler;
            AudioBackend.Use(m_Previous);
            m_Loop.Dispose();
            Directory.Delete(m_Directory, true);
        }

        (AudioClipInfo, float[])? Decode(byte[] data, int type, bool headerOnly)
        {
            m_DecodeCalls++;
            if (headerOnly) return null;
            bool isXm = data.Length >= 17 && data[0] == (byte)'E' && data[1] == (byte)'x';
            if (type is not ((int)AudioType.UNKNOWN or (int)AudioType.XM)) return null;
            if (!isXm) return null;
            var info = new AudioClipInfo { Frames = 64, Channels = 2, Frequency = 48000, IsTracker = 1 };
            var samples = Enumerable.Repeat(0.25f, 128).ToArray();
            return (info, samples);
        }

        List<(LogType Type, string Message)> TakeLogs()
        {
            lock (m_Logs) { var copy = m_Logs.ToList(); m_Logs.Clear(); return copy; }
        }

        static UnityWebRequest Send(UnityWebRequest req)
        {
            req.SendWebRequest();
            WebRequestTestLoop.Until(() => req.isDone);
            return req;
        }

        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void BackgroundLoadOfTrackerBlocksWithWarningOnMeasuredFamily(string release)
        {
            using var pinned = TestRelease.Pin(release);
            m_Backend.Assets["bg-tracker"] = (
                new AudioClipInfo
                {
                    Frames = 64, Channels = 2, Frequency = 48000,
                    LoadType = (int)AudioClipLoadType.DecompressOnLoad,
                    Preload = 0, LoadInBackground = 1, IsTracker = 1,
                },
                new float[128]);
            var clip = AudioClip.FromImport("bg-tracker", "bg_xm");
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
            TakeLogs();

            Assert.True(clip.LoadAudioData());
            var logs = TakeLogs();
            if (Compat.TrackerAudioMeasured)
            {
                Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message.Contains("background loading of tracked music files is not supported"));
            }
            else
            {
                Assert.Equal(AudioDataLoadState.Loading, clip.loadState);
                Assert.DoesNotContain(logs, l => l.Message.Contains("background loading of tracked music files"));
            }
            Object.DestroyImmediate(clip);
        }

        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void CompressedInMemoryTrackerRejectsGetDataAndSetDataEvenWithPcmCompressionSetting(string release)
        {
            using var pinned = TestRelease.Pin(release);
            m_Backend.Assets["cmp-tracker"] = (
                new AudioClipInfo
                {
                    Frames = 64, Channels = 2, Frequency = 48000,
                    LoadType = (int)AudioClipLoadType.CompressedInMemory,
                    Preload = 1, CompressionFormat = (int)AudioCompressionFormat.PCM, IsTracker = 1,
                },
                new float[128]);
            var clip = AudioClip.FromImport("cmp-tracker", "cmp_xm");
            TakeLogs();

            bool getOk = clip.GetData(new float[16], 0);
            var getLogs = TakeLogs();
            bool setOk = clip.SetData(new float[16], 0);
            var setLogs = TakeLogs();
            if (Compat.TrackerAudioMeasured)
            {
                Assert.False(getOk);
                Assert.DoesNotContain(getLogs, l => l.Message.Contains("compressed samples"));
                Assert.False(setOk);
                Assert.Contains(setLogs, l => l.Type == LogType.Error && l.Message == "Cannot set data on shared sample for audio clip \"cmp_xm\"");
            }
            else
            {
                Assert.True(getOk);
                Assert.True(setOk);
            }

            if (Compat.AssetCloneResourcesMeasured && Compat.TrackerAudioMeasured)
            {
                var clone = Object.Instantiate(clip);
                TakeLogs();
                Assert.False(clone.SetData(new float[16], 0));
                Assert.Contains(TakeLogs(), l => l.Type == LogType.Error && l.Message.Contains("shared sample"));
                Object.DestroyImmediate(clone);
            }
            Object.DestroyImmediate(clip);
        }

        [Fact]
        public void DecompressOnLoadTrackerSupportsGetDataAndSetDataMutation()
        {
            m_Backend.Assets["decomp-tracker"] = (
                new AudioClipInfo
                {
                    Frames = 32, Channels = 2, Frequency = 48000,
                    LoadType = (int)AudioClipLoadType.DecompressOnLoad,
                    Preload = 1, CompressionFormat = (int)AudioCompressionFormat.Vorbis, IsTracker = 1,
                },
                Enumerable.Repeat(0.125f, 64).ToArray());
            var clip = AudioClip.FromImport("decomp-tracker", "decomp_xm");
            var read = new float[8];
            Assert.True(clip.GetData(read, 0));
            Assert.All(read, v => Assert.Equal(0.125f, v));

            var patch = Enumerable.Repeat(0.5f, 8).ToArray();
            Assert.True(clip.SetData(patch, 0));
            Assert.True(clip.GetData(read, 0));
            Assert.All(read, v => Assert.Equal(0.5f, v));
            Object.DestroyImmediate(clip);
        }

        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void StreamedTrackerDownloadReturnsNullWithErrorOnMeasuredFamily(string release)
        {
            using var pinned = TestRelease.Pin(release);
            var xmPath = Path.Combine(m_Directory, "song.xm");
            File.WriteAllBytes(xmPath, "Extended Module: dummy"u8.ToArray());
            string url = "file://" + xmPath;

            foreach (var type in new[] { AudioType.XM, AudioType.UNKNOWN })
            {
                TakeLogs();
                m_DecodeCalls = 0;
                using var req = UnityWebRequestMultimedia.GetAudioClip(url, type);
                var dh = (DownloadHandlerAudioClip)req.downloadHandler;
                dh.streamAudio = true;
                Send(req);
                var clip = dh.audioClip;
                var logs = TakeLogs();
                if (Compat.TrackerAudioMeasured)
                {
                    Assert.Null(clip);
                    Assert.Contains(logs, l => l.Type == LogType.Error && l.Message.Contains("Tracker files (XM/IT/MOD/S3M) cannot be streamed in realtime"));
                    // Second access returns null and logs again (matches Unity 6000.0.71f1) without
                    // re-decoding the module.
                    int callsAfterFirst = m_DecodeCalls;
                    Assert.Equal(type == AudioType.XM ? 0 : 1, callsAfterFirst);
                    var second = dh.audioClip;
                    var secondLogs = TakeLogs();
                    Assert.Null(second);
                    Assert.Contains(secondLogs, l => l.Type == LogType.Error && l.Message.Contains("Tracker files (XM/IT/MOD/S3M) cannot be streamed in realtime"));
                    Assert.Equal(callsAfterFirst, m_DecodeCalls);
                }
                else
                {
                    Assert.NotNull(clip);
                    Object.DestroyImmediate(clip);
                }
            }
        }

        [Fact]
        public void CompressedTrackerDownloadDisallowsGetDataAndInvalidTrackerLogsFmodError()
        {
            var xmPath = Path.Combine(m_Directory, "ok.xm");
            File.WriteAllBytes(xmPath, "Extended Module: dummy"u8.ToArray());
            using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + xmPath, AudioType.XM))
            {
                var dh = (DownloadHandlerAudioClip)req.downloadHandler;
                dh.compressed = true;
                Send(req);
                var clip = dh.audioClip;
                Assert.NotNull(clip);
                Assert.Equal((2, 48000, 64), (clip.channels, clip.frequency, clip.samples));
                Assert.False(clip.GetData(new float[8], 0));
                Object.DestroyImmediate(clip);
            }

            var badPath = Path.Combine(m_Directory, "bad.mod");
            File.WriteAllBytes(badPath, "not a mod"u8.ToArray());
            TakeLogs();
            using (var req = Send(UnityWebRequestMultimedia.GetAudioClip("file://" + badPath, AudioType.MOD)))
            {
                var clip = ((DownloadHandlerAudioClip)req.downloadHandler).audioClip;
                Assert.NotNull(clip);
                Assert.Equal(0, clip.channels);
                Assert.Contains(TakeLogs(), l => l.Type == LogType.Error && l.Message.StartsWith("Error: Cannot create FMOD::Sound", StringComparison.Ordinal));
                Object.DestroyImmediate(clip);
            }
        }
    }
}
