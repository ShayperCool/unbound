using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Video;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// VideoClip / VideoPlayer audio track formats and VideoClip.sRGB, with a stand-in for the host's
    /// `video-probe` (and `video-open` / `video-close`) calls and for the importer section of the
    /// clip's .meta. Unity 6000.0 Scripting API: VideoClip.GetAudioChannelCount /
    /// GetAudioSampleRate / GetAudioLanguage (a track without a language: "an empty string"),
    /// VideoPlayer.GetAudioChannelCount / GetAudioSampleRate / GetAudioLanguageCode and
    /// audioTrackCount ("For URL sources, this will only be set once the source preparation is
    /// completed"), VideoClip.sRGB and VideoClipImporter.sRGBClip ("copied into the VideoClip.sRGB
    /// value of the resulting clip"). An index past the tracks is not documented: the 0 / "" asserted
    /// here is Unbound's choice (unmeasured), as is the clip source answering before preparation.
    /// The seams are reached by reflection so the file also compiles against sources without them.
    /// </summary>
    public class VideoAudioTrackInfoTests : IDisposable
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        const string File = "/project/Assets/Video/clip.webm";
        const string AssetPath = "Assets/Video/clip.webm";
        const string Guid = "ade0b4f25530c05868ec8f2726d57b0d";

        /// <summary>The host's answer for a clip with two audio tracks of different formats (stereo
        /// English at 48 kHz, 5.1 without a language at 44.1 kHz), in crates/unbound-runtime's
        /// video::probe shape.</summary>
        const string TwoTracks =
            "{\"width\":64,\"height\":48,\"frameRate\":10.0,\"frameRateNumerator\":10,\"frameRateDenominator\":1,"
            + "\"frameCount\":30,\"length\":3.0,\"audioTracks\":2,\"aspectNumerator\":1,\"aspectDenominator\":1,"
            + "\"audioTrackInfo\":[{\"channels\":2,\"sampleRate\":48000,\"language\":\"eng\"},"
            + "{\"channels\":6,\"sampleRate\":44100,\"language\":\"\"}]}";

        /// <summary>A video-only clip.</summary>
        const string VideoOnly =
            "{\"width\":64,\"height\":48,\"frameRate\":10.0,\"frameRateNumerator\":10,\"frameRateDenominator\":1,"
            + "\"frameCount\":30,\"length\":3.0,\"audioTracks\":0,\"aspectNumerator\":1,\"aspectDenominator\":1,"
            + "\"audioTrackInfo\":[]}";

        /// <summary>The answer's fields before per-track formats (an older host's shape).</summary>
        const string WithoutTrackInfo =
            "{\"width\":64,\"height\":48,\"frameRate\":10.0,\"frameRateNumerator\":10,\"frameRateDenominator\":1,"
            + "\"frameCount\":30,\"length\":3.0,\"audioTracks\":1,\"aspectNumerator\":1,\"aspectDenominator\":1}";

        /// <summary>tests/projects/video/Assets/Video/clip.webm.meta as the Unity 6000.0.71f1 Editor
        /// saved it for a clip imported with default settings (sRGB on, per the Manual).</summary>
        const string EditorSavedMeta = @"fileFormatVersion: 2
guid: ade0b4f25530c05868ec8f2726d57b0d
VideoClipImporter:
  externalObjects: {}
  serializedVersion: 3
  frameRange: 0
  startFrame: -1
  endFrame: -1
  colorSpace: 0
  deinterlace: 0
  encodeAlpha: 0
  flipVertical: 0
  flipHorizontal: 0
  importAudio: 1
  targetSettings: {}
  userData:
  assetBundleName:
  assetBundleVariant:
";

        readonly Dictionary<string, string> m_Probes = new Dictionary<string, string>();
        readonly List<string> m_Calls = new List<string>();
        readonly List<GameObject> m_GameObjects = new List<GameObject>();
        readonly List<Object> m_Assets = new List<Object>();
        YNode m_Importer;

        public VideoAudioTrackInfoTests()
        {
            Seam("s_CallForTests").SetValue(null, new Func<string, string, string>(Host));
            Seam("s_ImporterForTests").SetValue(null, new Func<string, YNode>(_ => m_Importer));
        }

        public void Dispose()
        {
            // Players first: destroying one closes its decoder through the stand-in.
            foreach (var go in m_GameObjects) if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in m_Assets) if (asset != null) Object.DestroyImmediate(asset, true);
            typeof(VideoClip).GetField("s_CallForTests", Static)?.SetValue(null, null);
            typeof(VideoClip).GetField("s_ImporterForTests", Static)?.SetValue(null, null);
        }

        static FieldInfo Seam(string name) =>
            typeof(VideoClip).GetField(name, Static)
            ?? throw new MissingFieldException(nameof(VideoClip), name + " (the video host stand-in)");

        string Host(string method, string request)
        {
            m_Calls.Add(method);
            using var document = JsonDocument.Parse(request);
            switch (method)
            {
                case "video-probe": return m_Probes[document.RootElement.GetProperty("path").GetString()];
                case "video-open": return "{\"id\":7}";
                case "video-close": return "{}";
                default: throw new InvalidOperationException("unexpected host call " + method);
            }
        }

        /// <summary>The clip the asset import makes from the host's <paramref name="answer"/>.</summary>
        VideoClip Import(string answer)
        {
            m_Probes[File] = answer;
            var probe = typeof(VideoClip).GetMethod("Probe", Static, null, new[] { typeof(string), typeof(string), typeof(string) }, null)
                ?? throw new MissingMethodException(nameof(VideoClip), "Probe(file, assetPath, guid)");
            var clip = (VideoClip)probe.Invoke(null, new object[] { File, AssetPath, Guid });
            m_Assets.Add(clip);
            return clip;
        }

        VideoPlayer Player()
        {
            var go = new GameObject("Video");
            m_GameObjects.Add(go);
            var player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            return player;
        }

        /// <summary>The player's next video update (PostLateUpdate.UpdateVideo), where a preparation
        /// completes.</summary>
        static void Update(VideoPlayer player) =>
            typeof(VideoPlayer).GetMethod("UpdatePlayback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);

        static YNode ImporterSection(string meta) => TestYaml.Parse(meta)["VideoClipImporter"];

        [Fact]
        public void ClipReportsEachTracksChannelsSampleRateAndLanguage()
        {
            var clip = Import(TwoTracks);

            Assert.Equal(2, clip.audioTrackCount);
            Assert.Equal(2, clip.GetAudioChannelCount(0));
            Assert.Equal(6, clip.GetAudioChannelCount(1));
            Assert.Equal(48000u, clip.GetAudioSampleRate(0));
            Assert.Equal(44100u, clip.GetAudioSampleRate(1));
            Assert.Equal("eng", clip.GetAudioLanguage(0));
        }

        [Fact]
        public void TrackWithoutLanguageAnswersEmptyString()
        {
            var clip = Import(TwoTracks.Replace("\"language\":\"\"", "\"language\":null"));

            Assert.Equal("", clip.GetAudioLanguage(1));
            Assert.Equal(6, clip.GetAudioChannelCount(1));
            Assert.Equal("eng", clip.GetAudioLanguage(0));
        }

        /// <summary>Unbound's choice (unmeasured): Unity's pages do not say what an index past the
        /// tracks gives.</summary>
        [Fact]
        public void IndexPastTheTracksAnswersZeroAndEmpty()
        {
            var clip = Import(TwoTracks);
            Assert.Equal(0, clip.GetAudioChannelCount(2));
            Assert.Equal(0u, clip.GetAudioSampleRate(2));
            Assert.Equal("", clip.GetAudioLanguage(ushort.MaxValue));

            var silent = Import(VideoOnly);
            Assert.Equal(0, silent.audioTrackCount);
            Assert.Equal(0, silent.GetAudioChannelCount(0));
            Assert.Equal(0u, silent.GetAudioSampleRate(0));
            Assert.Equal("", silent.GetAudioLanguage(0));
        }

        /// <summary>Guard: the fields the probe answered before keep their values, and an answer
        /// without per-track formats still loads (its tracks' formats read 0).</summary>
        [Fact]
        public void ExistingClipPropertiesAreUnchanged()
        {
            foreach (var answer in new[] { TwoTracks, WithoutTrackInfo })
            {
                var clip = Import(answer);
                Assert.Equal("clip", clip.name);
                Assert.Equal(AssetPath, clip.originalPath);
                Assert.Equal(64u, clip.width);
                Assert.Equal(48u, clip.height);
                Assert.Equal(10.0, clip.frameRate);
                Assert.Equal(30ul, clip.frameCount);
                Assert.Equal(3.0, clip.length);
                Assert.Equal(1u, clip.pixelAspectRatioNumerator);
                Assert.Equal(1u, clip.pixelAspectRatioDenominator);
            }
            var older = Import(WithoutTrackInfo);
            Assert.Equal(1, older.audioTrackCount);
            Assert.Equal(0, older.GetAudioChannelCount(0));
            Assert.Equal("", older.GetAudioLanguage(0));
        }

        /// <summary>A clip source answers from the current clip; that it does before preparation is
        /// Unbound's choice (unmeasured: the pages only state the URL case).</summary>
        [Fact]
        public void PlayerForwardsItsClipsTracks()
        {
            var player = Player();
            Assert.Equal(0, player.GetAudioChannelCount(0));
            Assert.Equal("", player.GetAudioLanguageCode(0));

            player.clip = Import(TwoTracks);

            Assert.Equal(2, player.audioTrackCount);
            Assert.Equal(2, player.GetAudioChannelCount(0));
            Assert.Equal(6, player.GetAudioChannelCount(1));
            Assert.Equal(48000u, player.GetAudioSampleRate(0));
            Assert.Equal(44100u, player.GetAudioSampleRate(1));
            Assert.Equal("eng", player.GetAudioLanguageCode(0));
            Assert.Equal("", player.GetAudioLanguageCode(1));
            Assert.Equal(0, player.GetAudioChannelCount(2));
        }

        /// <summary>A URL's tracks come from the probe of its file when it opens, and are answered
        /// once the preparation is completed, as documented; a new URL starts over.</summary>
        [Fact]
        public void PlayerAnswersAUrlsTracksOncePrepared()
        {
            m_Probes[File] = TwoTracks;
            var player = Player();
            player.source = VideoSource.Url;
            player.url = "file://" + File;
            Assert.Equal(0, player.audioTrackCount);
            Assert.Equal(0, player.GetAudioChannelCount(0));

            player.Prepare();
            Assert.Contains("video-probe", m_Calls);
            Assert.False(player.isPrepared);
            Assert.Equal(0, player.GetAudioChannelCount(0));
            Assert.Equal(0u, player.GetAudioSampleRate(0));
            Assert.Equal("", player.GetAudioLanguageCode(0));

            Update(player);
            Assert.True(player.isPrepared);
            Assert.Equal(2, player.audioTrackCount);
            Assert.Equal(2, player.GetAudioChannelCount(0));
            Assert.Equal(6, player.GetAudioChannelCount(1));
            Assert.Equal(48000u, player.GetAudioSampleRate(0));
            Assert.Equal(44100u, player.GetAudioSampleRate(1));
            Assert.Equal("eng", player.GetAudioLanguageCode(0));
            Assert.Equal("", player.GetAudioLanguageCode(1));
            Assert.Equal(0, player.GetAudioChannelCount(2));

            player.url = "file:///project/Assets/Video/other.webm";
            Assert.False(player.isPrepared);
            Assert.Equal(0, player.audioTrackCount);
            Assert.Equal(0, player.GetAudioChannelCount(0));
        }

        /// <summary>Guard: the Editor-saved default import's <c>colorSpace: 0</c> is sRGB.</summary>
        [Fact]
        public void EditorSavedDefaultImportIsSRGB()
        {
            m_Importer = ImporterSection(EditorSavedMeta);
            Assert.True(Import(VideoOnly).sRGB);
        }

        /// <summary><c>colorSpace: 1</c> reads as sRGB off (Unbound's choice, unmeasured: the
        /// setting's other value; no Editor-saved .meta with sRGB off is in the repository).</summary>
        [Fact]
        public void ImporterWithSRGBOffGivesANonSRGBClip()
        {
            m_Importer = ImporterSection(EditorSavedMeta.Replace("colorSpace: 0", "colorSpace: 1"));
            Assert.False(Import(VideoOnly).sRGB);
        }

        /// <summary>Guard: without the key, or without an importer section, the clip has the
        /// importer's default (sRGB on, per the Manual).</summary>
        [Fact]
        public void MissingImporterSettingIsTheDefaultSRGB()
        {
            m_Importer = ImporterSection(EditorSavedMeta.Replace("colorSpace: 0", "otherSetting: 0"));
            Assert.False(m_Importer.TryGet("colorSpace", out _));
            Assert.True(Import(VideoOnly).sRGB);
            m_Importer = null;
            Assert.True(Import(VideoOnly).sRGB);
        }
    }
}
