using System;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Video;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>A VideoPlayer's audio track settings: as the Editor writes them in a scene
    /// (tests/projects/scenesave/Reference/unity-6000.0.71f1/defaults.unity: a list of target
    /// references and direct volumes, the enabled and mute flags as hex bytes), copied by
    /// Instantiate, and the target a player made beside an AudioSource takes (measured:
    /// tests/projects/videoaudio *.prepared).</summary>
    [Collection("Serial")]
    public sealed class VideoAudioTrackSettingsTests
    {
        sealed class OneSource : IReferenceResolver
        {
            readonly AudioSource m_Source;
            internal OneSource(AudioSource source) => m_Source = source;
            public Object Resolve(ObjectRef reference, Type expected) => reference.FileId == 5 ? m_Source : null;
        }

        static YNode Yaml(string json) => YNode.FromJson(JsonDocument.Parse(json).RootElement);

        [Fact]
        public void SerializedTracksGiveTheirTargetsVolumesAndFlags()
        {
            var go = new GameObject("video");
            try
            {
                var source = go.AddComponent<AudioSource>();
                var player = go.AddComponent<VideoPlayer>();
                player.ReadSerialized(Yaml(@"{
                    ""m_TargetAudioSources"": [{""fileID"": ""5""}, {""fileID"": ""0""}],
                    ""m_DirectAudioVolumes"": [""0.5"", ""1""],
                    ""m_EnabledAudioTracks"": ""0001"",
                    ""m_DirectAudioMutes"": ""0100"",
                    ""m_ControlledAudioTrackCount"": ""2""
                }"), new OneSource(source));
                Assert.Same(source, player.GetTargetAudioSource(0));
                Assert.Null(player.GetTargetAudioSource(1));
                Assert.False(player.IsAudioTrackEnabled(0));
                Assert.True(player.IsAudioTrackEnabled(1));
                Assert.Equal(0.5f, player.GetDirectAudioVolume(0));
                Assert.Equal(1f, player.GetDirectAudioVolume(1));
                Assert.True(player.GetDirectAudioMute(0));
                Assert.False(player.GetDirectAudioMute(1));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ASerializedPlayerWithoutATargetDoesNotTakeItsGameObjectsSource()
        {
            var go = new GameObject("video");
            try
            {
                go.AddComponent<AudioSource>();
                var player = go.AddComponent<VideoPlayer>();
                player.ReadSerialized(Yaml(@"{""m_TargetAudioSources"": [{""fileID"": ""0""}], ""m_EnabledAudioTracks"": ""01""}"), new OneSource(null));
                Assert.Null(player.GetTargetAudioSource(0));
                Assert.True(player.IsAudioTrackEnabled(0));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void APlayerMadeBesideAnAudioSourceTargetsIt()
        {
            var go = new GameObject("video");
            try
            {
                var source = go.AddComponent<AudioSource>();
                var player = go.AddComponent<VideoPlayer>();
                Assert.Same(source, player.GetTargetAudioSource(0));
                Assert.Null(player.GetTargetAudioSource(1));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void TheSoundsSpeedIsThePicturesTimeScaledSpeedUnlessTheyFollowUnscaledTime()
        {
            var go = new GameObject("video");
            float scale = Time.timeScale;
            try
            {
                var player = go.AddComponent<VideoPlayer>();
                player.playbackSpeed = 2f;
                player.timeUpdateMode = VideoTimeUpdateMode.GameTime;
                Time.timeScale = 0.25f;
                Assert.Equal(0.5f, player.EffectiveAudioSpeed);
                Time.timeScale = 0f;
                Assert.Equal(0f, player.EffectiveAudioSpeed);
                player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                Assert.Equal(2f, player.EffectiveAudioSpeed);
            }
            finally
            {
                Time.timeScale = scale;
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ASeekOrLoopRestartsTheDecoderAndAStartSyncsIt()
        {
            // A new decoder starts at the pictures' time.
            Assert.Equal(((double?)1.5, (double?)null), VideoPlayer.AudioControlTimes(true, null, true, true, 1.5));
            // A seek of 0.1 s while playing restarts it, whatever the drift check would say.
            Assert.Equal(((double?)0.9, (double?)null), VideoPlayer.AudioControlTimes(false, 0.9, false, true, 0.9));
            // A start, and the periodic check, sync to the pictures.
            Assert.Equal(((double?)null, (double?)1.0), VideoPlayer.AudioControlTimes(false, null, true, false, 1.0));
            Assert.Equal(((double?)null, (double?)2.0), VideoPlayer.AudioControlTimes(false, null, false, true, 2.0));
            // Otherwise nothing.
            Assert.Equal(((double?)null, (double?)null), VideoPlayer.AudioControlTimes(false, null, false, false, 2.0));
        }

        [Fact]
        public void AClaimedSourceReportsPlayingUntilItsOwnStopOrPauseOrThePlayersDestruction()
        {
            var go = new GameObject("video");
            try
            {
                var source = go.AddComponent<AudioSource>();
                var player = go.AddComponent<VideoPlayer>();
                // A new source with Play On Awake reports a pending play: stop it first.
                source.playOnAwake = false;
                source.Stop();
                Assert.False(source.isPlaying);
                player.ClaimAudioSource(source);
                Assert.True(source.isPlaying);
                source.Stop();
                Assert.False(source.isPlaying);
                player.ClaimAudioSource(source);
                source.Pause();
                Assert.False(source.isPlaying);
                player.ClaimAudioSource(source);
                Assert.True(source.isPlaying);
                Object.DestroyImmediate(player);
                Assert.False(source.isPlaying);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void InstantiateCopiesTheTracksWithTheirTargetsRemapped()
        {
            var go = new GameObject("video");
            GameObject copy = null;
            try
            {
                var source = go.AddComponent<AudioSource>();
                var player = go.AddComponent<VideoPlayer>();
                player.SetTargetAudioSource(0, source);
                player.EnableAudioTrack(1, false);
                player.SetDirectAudioVolume(1, 0.25f);
                player.SetDirectAudioMute(0, true);
                copy = Object.Instantiate(go);
                var copied = copy.GetComponent<VideoPlayer>();
                Assert.Same(copy.GetComponent<AudioSource>(), copied.GetTargetAudioSource(0));
                Assert.False(copied.IsAudioTrackEnabled(1));
                Assert.Equal(0.25f, copied.GetDirectAudioVolume(1));
                Assert.True(copied.GetDirectAudioMute(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }
    }
}
