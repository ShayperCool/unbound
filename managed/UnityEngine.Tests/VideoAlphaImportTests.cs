using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Video;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Whether a clip's alpha survives its import (the Manual's Video Clip Importer: Keep
    /// Alpha preserves it in transcoding; tests/projects/videoalpha measures an untranscoded clip).</summary>
    [Collection("Serial")]
    public sealed class VideoAlphaImportTests
    {
        static YNode Yaml(string json) => YNode.FromJson(JsonDocument.Parse(json).RootElement);

        [Fact]
        public void AnUntranscodedClipKeepsItsAlpha()
        {
            Assert.True(VideoClip.KeepsAlpha(null, "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": {}}"), "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": [{""first"": ""-2"", ""second"": {""enableTranscoding"": ""0""}}]}"), "clip.webm"));
        }

        [Fact]
        public void ATranscodedClipKeepsItsAlphaOnlyWithKeepAlpha()
        {
            const string transcoded = @"[{""first"": ""-2"", ""second"": {""enableTranscoding"": ""1"", ""codec"": ""0""}}]";
            Assert.False(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": " + transcoded + "}"), "clip.webm"));
            Assert.True(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""1"", ""targetSettings"": " + transcoded + "}"), "clip.webm"));
            // A map form of the target settings counts too.
            Assert.False(VideoClip.KeepsAlpha(Yaml(@"{""encodeAlpha"": ""0"", ""targetSettings"": {""-2"": {""enableTranscoding"": ""1""}}}"), "clip.webm"));
        }
    }
}
