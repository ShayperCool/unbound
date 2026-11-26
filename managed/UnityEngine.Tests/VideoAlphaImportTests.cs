using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Video;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Whether a clip's alpha survives its import (the Manual's Video Clip Importer: Keep
    /// Alpha preserves it in transcoding; tests/projects/videoalpha measures an untranscoded clip),
    /// and what the clip and the player do with it.</summary>
    [Collection("Serial")]
    public sealed class VideoAlphaImportTests : IDisposable
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        readonly Capture m_Log = new Capture();
        readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
        string m_Probe;
        YNode m_Importer;
        bool m_OpenAlpha;

        public VideoAlphaImportTests()
        {
            Debug.unityLogger.logHandler = m_Log;
            VideoClip.s_CallForTests = (method, request) => method switch
            {
                "video-probe" => m_Probe,
                "video-open" => m_OpenAlpha ? "{\"id\":7,\"alpha\":true}" : "{\"id\":7,\"alpha\":false}",
                _ => "{}",
            };
            VideoClip.s_ImporterForTests = _ => m_Importer;
            foreach (var member in new[]
            {
                "UnityEngine.Video.VideoClip transcoded with Keep Alpha",
                "UnityEngine.Video.VideoClip platform transcoding override",
                "UnityEngine.Video.VideoClip alpha of a non-VP8 source",
            }) Stubs.Forget(member);
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = m_Previous;
            VideoClip.s_CallForTests = null;
            VideoClip.s_ImporterForTests = null;
        }

        static YNode Yaml(string json) => YNode.FromJson(JsonDocument.Parse(json).RootElement);

        static string ProbeJson(string codec, bool alpha, bool pixelAlpha) =>
            "{\"width\":64,\"height\":48,\"frameRate\":10,\"frameCount\":10,\"length\":1,\"audioTracks\":0,\"aspectNumerator\":1,\"aspectDenominator\":1,"
            + $"\"audioTrackInfo\":[],\"alpha\":{(alpha ? "true" : "false")},\"codec\":\"{codec}\",\"pixelAlpha\":{(pixelAlpha ? "true" : "false")}}}";

        const string Default = @"{""first"": ""-2"", ""second"": {""enableTranscoding"": ""1""}}";
        const string Ios = @"{""first"": ""9"", ""second"": {""enableTranscoding"": ""1""}}";

        [Fact]
        public void AnUntranscodedClipKeepsItsAlpha()
        {
            Assert.True(VideoClip.KeepsAlpha(null, "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": {}}"), "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": [{""first"": ""-2"", ""second"": {""enableTranscoding"": ""0""}}]}"), "clip.webm"));
        }

        [Fact]
        public void TheDefaultEntryTranscodingDropsTheAlphaUnlessKeepAlpha()
        {
            Assert.False(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": [" + Default + "]}"), "clip.webm"));
            Assert.False(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": {""-2"": {""enableTranscoding"": ""1""}}}"), "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""1"", ""targetSettings"": [" + Default + "]}"), "clip.webm"));
            Assert.Contains(m_Log.Lines, line => line.Contains("VideoClip transcoded with Keep Alpha"));
        }

        [Fact]
        public void AnotherPlatformsOverrideKeepsTheAlphaAndIsSaid()
        {
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": [" + Ios + "]}"), "clip.webm"));
            Assert.Contains(m_Log.Lines, line => line.Contains("VideoClip platform transcoding override"));
        }

        [Fact]
        public void AProbedClipTakesTheImportRuleAndThePlayerTheDecodersAnswer()
        {
            m_Probe = ProbeJson("vp8", true, false);
            m_Importer = Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": [" + Default + "]}");
            Assert.False(VideoClip.Probe("/tmp/clip.webm", "Assets/clip.webm", "0123456789abcdef0123456789abcdef").m_Alpha);
            m_Importer = Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": {}}");
            var clip = VideoClip.Probe("/tmp/clip.webm", "Assets/clip.webm", "0123456789abcdef0123456789abcdef");
            Assert.True(clip.m_Alpha);
        }

        [Fact]
        public void ANonVp8SourceWithAlphaIsDrawnOpaqueAndSaid()
        {
            m_Probe = ProbeJson("prores", false, true);
            m_Importer = null;
            var clip = VideoClip.Probe("/tmp/clip.mov", "Assets/clip.mov", "0123456789abcdef0123456789abcdef");
            Assert.False(clip.m_Alpha);
            Assert.Contains(m_Log.Lines, line => line.Contains("VideoClip alpha of a non-VP8 source"));
        }
    }
}
