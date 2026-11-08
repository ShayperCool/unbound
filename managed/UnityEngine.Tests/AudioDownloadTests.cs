using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.Networking;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>UnityWebRequestMultimedia.GetAudioClip and DownloadHandlerAudioClip over file://
    /// URLs, as measured in the Unity 6000.0.71f1 Editor (tests/projects/networkcaching). The audio
    /// backend is the virtual one: it decodes 16-bit mono PCM WAV files and MPEG-looking files
    /// (a frame sync) to silence, and refuses anything else.</summary>
    [Collection("Frames")]
    public class AudioDownloadTests : IDisposable
    {
        readonly WebRequestTestLoop loop = new WebRequestTestLoop();
        readonly AudioBackend previous = AudioBackend.Current;
        readonly VirtualAudioBackend backend = new VirtualAudioBackend();
        readonly string directory = Path.Combine(Path.GetTempPath(), $"unbound-audiodl-{Guid.NewGuid():N}");
        readonly List<(LogType Type, string Message)> logs = new List<(LogType, string)>();
        readonly List<bool> headerOnlyCalls = new List<bool>();

        public AudioDownloadTests()
        {
            Directory.CreateDirectory(directory);
            backend.Downloads = Decode;
            AudioBackend.Use(backend);
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            AudioBackend.Use(previous);
            loop.Dispose();
            Directory.Delete(directory, true);
        }

        void Capture(string message, string stack, LogType type)
        {
            lock (logs) logs.Add((type, message));
        }

        (AudioClipInfo, float[])? Decode(byte[] data, int type, bool headerOnly)
        {
            headerOnlyCalls.Add(headerOnly);
            bool wav = data.Length >= 44 && data[0] == 'R' && data[8] == 'W';
            bool mpeg = data.Length >= 2 && data[0] == 0xff && (data[1] & 0xe0) == 0xe0;
            // As the native decoder: the types it decodes, each its own container only.
            if (type is not ((int)AudioType.UNKNOWN or (int)AudioType.WAV or (int)AudioType.MPEG)) return null;
            if ((type == (int)AudioType.WAV && !wav) || (type == (int)AudioType.MPEG && !mpeg) || (!wav && !mpeg)) return null;
            int frames = wav ? BitConverter.ToInt32(data, 40) / 2 : 1152;
            var info = new AudioClipInfo { Frames = frames, Channels = 1, Frequency = 22050 };
            return (info, Enumerable.Range(0, frames).Select(i => i / 32768f).ToArray());
        }

        static byte[] Wav(int frames)
        {
            var data = new byte[44 + frames * 2];
            "RIFF"u8.CopyTo(data);
            "WAVEfmt "u8.CopyTo(data.AsSpan(8));
            BitConverter.GetBytes(frames * 2).CopyTo(data, 40);
            return data;
        }

        string Write(string name, byte[] contents)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, contents);
            return "file://" + path;
        }

        static UnityWebRequest Send(UnityWebRequest request)
        {
            request.SendWebRequest();
            WebRequestTestLoop.Until(() => request.isDone);
            return request;
        }

        List<(LogType Type, string Message)> TakeLogs()
        {
            lock (logs)
            {
                var taken = logs.ToList();
                logs.Clear();
                return taken;
            }
        }

        [Fact]
        public void TheClipIsMadeOnFirstAccessAsALoadedNamelessClip()
        {
            var url = Write("tone.wav", Wav(300));
            using var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
            var handler = Assert.IsType<DownloadHandlerAudioClip>(request.downloadHandler);
            Assert.Equal("Cannot access the .audioClip property of DownloadHandlerAudioClip before the download is initiated",
                Assert.Throws<InvalidOperationException>(() => handler.audioClip).Message);
            Send(request);
            Assert.Equal(UnityWebRequest.Result.Success, request.result);
            Assert.Empty(headerOnlyCalls);
            var clip = handler.audioClip;
            Assert.Equal(("", 300, 1, 22050), (clip.name, clip.samples, clip.channels, clip.frequency));
            Assert.Equal((AudioClipLoadType.DecompressOnLoad, AudioDataLoadState.Loaded, true), (clip.loadType, clip.loadState, clip.preloadAudioData));
            Assert.Same(clip, handler.audioClip);
            Assert.Same(clip, DownloadHandlerAudioClip.GetContent(request));
            Assert.Equal(44 + 600, handler.data.Length);
            Assert.Equal("String access is not supported for audio clips", Assert.Throws<NotSupportedException>(() => handler.text).Message);
            var data = new float[300];
            Assert.True(clip.GetData(data, 0));
            Assert.Equal(299 / 32768f, data[299]);
            Assert.Empty(TakeLogs());
        }

        [Fact]
        public void FlagsWarnWhenBothAreSetAndShapeWhatReadsBack()
        {
            var handler = new DownloadHandlerAudioClip("file:///x", AudioType.MPEG);
            handler.compressed = true;
            Assert.Empty(TakeLogs());
            handler.streamAudio = true;
            handler.compressed = false;
            Assert.Equal(2, TakeLogs().Count(l => l == (LogType.Warning, "Audio compression is not valid as streamAudio is set to true")));
            Assert.True(handler.streamAudio);
            handler.Dispose();

            var mpeg = Write("tone.mp3", new byte[] { 0xff, 0xfb, 0x90, 0xc4, 0, 0 });
            var wav = Write("tone.wav", Wav(10));
            foreach (var (url, type, stream, compressed, readable) in new[]
            {
                (mpeg, AudioType.MPEG, false, true, false),
                (wav, AudioType.WAV, false, true, true),
                (wav, AudioType.WAV, true, false, false),
            })
            {
                using var request = UnityWebRequestMultimedia.GetAudioClip(url, type);
                var h = (DownloadHandlerAudioClip)request.downloadHandler;
                h.streamAudio = stream;
                h.compressed = compressed;
                var clip = DownloadHandlerAudioClip.GetContent(Send(request));
                Assert.Equal(AudioClipLoadType.DecompressOnLoad, clip.loadType);
                Assert.Equal(readable, clip.GetData(new float[clip.samples], 0));
                var errors = TakeLogs().Where(l => l.Type == LogType.Error).Select(l => l.Message).ToList();
                if (readable) Assert.Empty(errors);
                else Assert.Contains(errors, e => e.StartsWith(stream ? "Can't get data from streamed samples for audio clip \"\"" : "Cannot get data on compressed samples for audio clip \"\"", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void DataOfAnotherTypeGivesAClipWithoutData()
        {
            var url = Write("notaudio.wav", "this is not audio data at all\n"u8.ToArray());
            using var request = Send(UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV));
            var clip = ((DownloadHandlerAudioClip)request.downloadHandler).audioClip;
            Assert.NotNull(clip);
            Assert.Equal((0, 0, 0, AudioDataLoadState.Loaded), (clip.samples, clip.channels, clip.frequency, clip.loadState));
            Assert.Contains((LogType.Error, "Error: Cannot create FMOD::Sound instance for clip \"\" (FMOD error: Unsupported file or audio format. )"), TakeLogs());
            Assert.True(clip.LoadAudioData());
            Assert.False(clip.GetData(new float[4], 0));
            Assert.Contains((LogType.Log, "AudioClip.GetData failed; AudioClip  contains no data"), TakeLogs());

            using var tracker = Send(UnityWebRequestMultimedia.GetAudioClip(Write("song.mod", Wav(4)), AudioType.MOD));
            Assert.Equal(0, ((DownloadHandlerAudioClip)tracker.downloadHandler).audioClip.channels);
            // Unbound's own diagnostic (which skips the log callbacks), not the Editor's decoding error.
            Assert.DoesNotContain(TakeLogs(), l => l.Message.StartsWith("Error: Cannot create", StringComparison.Ordinal));
        }

        [Fact]
        public void AFailedDownloadHasNoClipAndNoData()
        {
            using var request = Send(UnityWebRequestMultimedia.GetAudioClip("file://" + Path.Combine(directory, "missing.wav"), AudioType.WAV));
            var handler = (DownloadHandlerAudioClip)request.downloadHandler;
            Assert.Equal(UnityWebRequest.Result.ConnectionError, request.result);
            Assert.False(handler.isDone);
            Assert.Null(handler.data);
            Assert.Equal("Cannot access the .audioClip property of an aborted DownloadHandlerAudioClip",
                Assert.Throws<InvalidOperationException>(() => handler.audioClip).Message);
        }
    }
}
