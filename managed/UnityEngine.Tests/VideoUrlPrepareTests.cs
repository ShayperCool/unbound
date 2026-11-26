using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Video;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>A network URL source prepares asynchronously (tests/projects/videourl): the host's probe
    /// answers at a later update; a failure leaves the player neither prepared nor playing, and a later
    /// Prepare does not start playback.</summary>
    [Collection("Serial")]
    public sealed class VideoUrlPrepareTests : IDisposable
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
        }

        readonly Capture m_Log = new Capture();
        readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
        string m_Poll = "{\"done\":false}";

        const string Probe = "{\"width\":64,\"height\":48,\"frameRate\":10,\"frameCount\":30,\"length\":3,\"audioTracks\":0,\"aspectNumerator\":1,\"aspectDenominator\":1,\"audioTrackInfo\":[],\"alpha\":false,\"codec\":\"vp8\",\"pixelAlpha\":false}";

        public VideoUrlPrepareTests()
        {
            Debug.unityLogger.logHandler = m_Log;
            VideoClip.s_CallForTests = (method, request) => method switch
            {
                "video-probe-start" => "{\"id\":3}",
                "video-probe-poll" => m_Poll,
                "video-open" => "{\"id\":7,\"alpha\":false}",
                _ => "{}",
            };
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Previous;
            VideoClip.s_CallForTests = null;
        }

        static void Update(VideoPlayer player) =>
            typeof(VideoPlayer).GetMethod("UpdatePlayback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);

        [Fact]
        public void AFailingUrlAfterPlayLeavesThePlayerStoppedAndALaterPrepareDoesNotPlay()
        {
            var go = new GameObject("video");
            try
            {
                var player = go.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                var errors = new List<string>();
                player.errorReceived += (_, message) => errors.Add(message);
                player.url = "http://127.0.0.1:9/missing.webm";
                player.Play();
                // The probe has not answered yet: not prepared.
                Update(player);
                Assert.False(player.isPrepared);
                m_Poll = "{\"done\":true,\"error\":\"Server returned 404 Not Found\"}";
                Update(player);
                Update(player);
                Assert.False(player.isPrepared);
                Assert.False(player.isPlaying);
                Assert.Equal(new[] { "VideoPlayer cannot play url : http://127.0.0.1:9/missing.webm\n\nCannot read file." }, errors);
                Assert.Contains(m_Log.Lines, line => line.Contains("Server returned 404 Not Found"));

                // Another URL, prepared: it does not start playing on its own.
                m_Poll = "{\"done\":false}";
                player.url = "http://127.0.0.1:9/clip.webm";
                player.Prepare();
                Update(player);
                m_Poll = "{\"done\":true,\"result\":" + Probe + "}";
                Update(player);
                Update(player);
                Assert.True(player.isPrepared);
                Assert.False(player.isPlaying);
                Assert.Equal(30UL, player.frameCount);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
