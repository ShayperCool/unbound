using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class AudioClipResetTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly List<Object> m_Objects = new List<Object>();
        public AudioClipResetTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            AudioSystem.ResetForTests(); AudioBackend.Use(m_Backend);
        }
        public void Dispose()
        {
            foreach (var obj in m_Objects) Object.DestroyImmediate(obj, true);
            AudioSystem.ResetForTests(); ProjectInfo.UnityVersion = m_Release;
        }
        AudioClip Clip(int frames, int channels, int frequency, bool stream = false)
        {
            var clip = AudioClip.Create("reset", frames, channels, frequency, stream,
                stream ? data => Array.Fill(data, 0.125f) : null);
            m_Objects.Add(clip); return clip;
        }
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(1, true)]
        public void ResetInvalidatesGeneratedDataBeforeNotificationIncludingUnattachedClips(int channels, bool stream)
        {
            var clip = Clip(1024, channels, 22050, stream);
            bool notified = false;
            void Changed(bool device)
            {
                notified = true; Assert.False(device); Assert.True(clip);
                Assert.Equal(0, clip.samples); Assert.Equal(0, clip.channels);
                Assert.Equal(22050, clip.frequency); Assert.Equal(0, clip.length);
                Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            }
            AudioSettings.OnAudioConfigurationChanged += Changed;
            try { Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration())); }
            finally { AudioSettings.OnAudioConfigurationChanged -= Changed; }
            Assert.True(notified);
            float[] data = { 0.375f, -0.375f };
            Assert.False(clip.GetData(data, 0)); Assert.Equal(new[] { 0.375f, -0.375f }, data);
            Assert.False(clip.SetData(data, 0));
            var go = new GameObject("reset source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>(); source.clip = clip; source.Play();
            Assert.False(source.isPlaying);
            var replacement = Clip(1024, channels, 22050);
            Assert.True(replacement.SetData(data, 0)); source.clip = replacement; source.Play();
            Assert.True(source.isPlaying);
        }
        [Fact]
        public void ImportedDataAndItsCloneReloadOriginalSamplesAndKeepTheirLoadSettings()
        {
            const string key = "reset-import";
            m_Backend.Assets[key] = (new AudioClipInfo { Frames = 4, Channels = 1, Frequency = 44100,
                LoadType = (int)AudioClipLoadType.DecompressOnLoad, Preload = 1 }, new[] { 0.125f, 0.25f, 0.375f, 0.5f });
            var asset = AudioClip.FromImport(key, "asset"); m_Objects.Add(asset);
            var clone = Object.Instantiate(asset); m_Objects.Add(clone);
            Assert.True(asset.SetData(new[] { -0.25f }, 0)); Assert.True(clone.SetData(new[] { -0.5f }, 0));
            Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration()));
            foreach (var clip in new[] { asset, clone })
            {
                Assert.Equal(4, clip.samples); Assert.Equal(1, clip.channels); Assert.Equal(44100, clip.frequency);
                Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
                var data = new float[4]; Assert.True(clip.GetData(data, 0));
                Assert.Equal(new[] { 0.125f, 0.25f, 0.375f, 0.5f }, data);
                Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            }
            Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration()));
            Assert.True(clone.SetData(new[] { 0.75f }, 0));
            Assert.Equal(AudioDataLoadState.Loaded, clone.loadState);
            Assert.Equal(AudioDataLoadState.Unloaded, asset.loadState);
            var shared = new float[1]; Assert.True(asset.GetData(shared, 0)); Assert.Equal(0.75f, shared[0]);
            ulong handle = clone.m_Handle;
            Object.DestroyImmediate(asset, true); Assert.False(asset);
            Assert.Equal(AudioDataLoadState.Unloaded, clone.loadState);
            Assert.True(m_Backend.ClipData(handle, 0, shared, write: false)); Assert.Equal(0.75f, shared[0]);
            Assert.True(clone.GetData(shared, 0)); Assert.Equal(0.125f, shared[0]);
            Object.DestroyImmediate(clone); Assert.False(clone);
            Assert.False(m_Backend.ClipData(handle, 0, shared, write: false));
        }
        [Fact]
        public void ResetReloadsImportedDataEvenWithoutAnEarlierSetData()
        {
            const string key = "unchanged-import";
            var info = new AudioClipInfo { Frames = 2, Channels = 1, Frequency = 22050, Preload = 1 };
            m_Backend.Assets[key] = (info, new[] { 0.125f, 0.25f });
            var asset = AudioClip.FromImport(key, "asset"); m_Objects.Add(asset);
            m_Backend.Assets[key] = (info, new[] { -0.125f, -0.25f });
            Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration()));
            var data = new float[2]; Assert.True(asset.GetData(data, 0));
            Assert.Equal(new[] { -0.125f, -0.25f }, data);
        }
        [Theory]
        [InlineData(44100, 44, 0f)]
        [InlineData(44100, 45, 0.001f)]
        [InlineData(48000, 47, 0f)]
        [InlineData(48000, 48, 0.001f)]
        [InlineData(48000, 4096, 0.085f)]
        public void ScriptDurationUsesCompleteMilliseconds(int frequency, int frames, float duration)
        {
            var clip = Clip(frames, 1, frequency); Assert.Equal(duration, clip.length);
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.Equal((float)((double)frames / frequency), clip.length);
        }
    }
}
