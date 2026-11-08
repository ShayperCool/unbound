using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class AudioStreamBufferTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly List<Object> m_Objects = new List<Object>();
        public AudioStreamBufferTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            AudioSystem.ResetForTests(); AudioBackend.Use(m_Backend);
        }
        public void Dispose()
        {
            foreach (var obj in m_Objects) Object.DestroyImmediate(obj, true);
            AudioSystem.ResetForTests(); ProjectInfo.UnityVersion = m_Release;
        }
        AudioClip Clip(bool preload = true, bool stream = true)
        {
            string key = "buffer-" + m_Objects.Count;
            m_Backend.Assets[key] = (new AudioClipInfo { Frames = 100, Channels = 1, Frequency = 48000,
                LoadType = (int)(stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad),
                Preload = preload ? 1 : 0 }, new float[100]);
            var clip = AudioClip.FromImport(key, key); m_Objects.Add(clip); return clip;
        }
        AudioSource Source(AudioClip clip)
        {
            var go = new GameObject("buffer source"); m_Objects.Add(go);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.clip = clip; return source;
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitLoadAndIdleStopKeepBufferButPlayingStopReleasesIt(bool preload)
        {
            var clip = Clip(preload); var source = Source(clip);
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
            Assert.True(clip.LoadAudioData()); source.Stop();
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            source.Play(); Assert.True(source.isPlaying); source.Stop();
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
            Assert.False(clip.GetData(new float[1], 0));
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LatestStartedSourceOwnsBufferRegardlessOfOtherPlayingSources(bool reverse)
        {
            var clip = Clip(); var first = Source(clip); var last = Source(clip);
            first.Play(); last.Play(); (reverse ? last : first).Stop();
            Assert.Equal(reverse ? AudioDataLoadState.Unloaded : AudioDataLoadState.Loaded, clip.loadState);
            (reverse ? first : last).Stop();
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
        }
        [Fact]
        public void OneShotsReplacementDisableAndDestructionReleaseOnlyOwnedBuffers()
        {
            var a = Clip(); var b = Clip(); var pcm = Clip(stream: false); var source = Source(a);
            source.Play(); source.PlayOneShot(b); source.clip = pcm;
            Assert.Equal(AudioDataLoadState.Unloaded, a.loadState);
            Assert.Equal(AudioDataLoadState.Loaded, b.loadState);
            source.enabled = false;
            Assert.Equal(AudioDataLoadState.Unloaded, b.loadState);
            Assert.Equal(AudioDataLoadState.Loaded, pcm.loadState);
            source.enabled = true; source.clip = a; source.Play();
            Object.DestroyImmediate(source.gameObject);
            Assert.Equal(AudioDataLoadState.Unloaded, a.loadState);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MainClipReplacementKeepsTheLatestOneShotBufferOnTheSameSource(bool mainLast)
        {
            var clip = Clip(); var source = Source(clip);
            if (mainLast) { source.PlayOneShot(clip); source.Play(); }
            else { source.Play(); source.PlayOneShot(clip); }
            source.clip = null;
            Assert.Equal(mainLast ? AudioDataLoadState.Unloaded : AudioDataLoadState.Loaded, clip.loadState);
            source.Stop(); Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
        }
        [Fact]
        public void UnmeasuredReleaseRetainsPriorStreamingLoadAndStopBehavior()
        {
            ProjectInfo.UnityVersion = "6000.0.72f1";
            var clip = Clip(); Assert.True(clip.LoadAudioData());
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
            var source = Source(clip); source.Play(); source.Stop();
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
        }
        [Fact]
        public void RejectedDisabledPlayDoesNotStealBufferAndResetDetachesAllOwners()
        {
            var clip = Clip(); var first = Source(clip); var rejected = Source(clip);
            first.Play(); rejected.enabled = false; rejected.Play(); rejected.Stop();
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            Assert.Same(first, clip.m_StreamBufferOwner);
            Assert.True(AudioSettings.Reset(AudioSettings.GetConfiguration()));
            Assert.Null(first.m_StreamBuffers); Assert.Null(clip.m_StreamBufferOwner);
            Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
        }
        [Theory]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 2)]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 2)]
        public void DeferredDestructionStopsPlaybackBeforeTheObjectIsDestroyed(bool stream, int target)
        {
            var clip = Clip(stream: stream); var source = Source(clip);
            var parent = new GameObject("parent"); m_Objects.Add(parent); source.transform.parent = parent.transform;
            source.Play(); Object.Destroy(target == 0 ? (Object)source : target == 1 ? source.gameObject : parent);
            Assert.True(source); Assert.False(source.isPlaying);
            Assert.Equal(stream ? AudioDataLoadState.Unloaded : AudioDataLoadState.Loaded, clip.loadState);
            Assert.Null(clip.m_StreamBufferOwner);
            Assert.True(clip.LoadAudioData()); Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            Destruction.DeliverRequested(); Destruction.FlushEndOfFrame(); Assert.False(source);
        }
        [RequireComponent(typeof(AudioSource))]
        public class KeepsSource : MonoBehaviour { }
        [Fact]
        public void RefusedRequiredSourceRemovalDoesNotStopItsVoice()
        {
            var clip = Clip(); var source = Source(clip); source.gameObject.AddComponent<KeepsSource>(); source.Play();
            Object.Destroy(source); Assert.True(source.isPlaying);
            Destruction.FlushEndOfFrame(); Assert.True(source); Assert.True(source.isPlaying);
            Object.Destroy(source.gameObject); Assert.False(source.isPlaying); Destruction.DeliverRequested(); Destruction.FlushEndOfFrame();
        }
        public class DestroyFromFilter : MonoBehaviour
        {
            internal Object Target;
            internal AudioClip Clip;
            internal bool Called, LoadedInside;
            void OnAudioFilterRead(float[] data, int channels)
            {
                Object.Destroy(Target);
                Called = true;
                LoadedInside = Clip.loadState == AudioDataLoadState.Loaded;
            }
        }
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public unsafe void CallbackDestructionDefersMixerCommandsUntilNormalDelivery(int target)
        {
            var clip = Clip(); var source = Source(clip);
            var parent = new GameObject("callback parent"); m_Objects.Add(parent); source.transform.parent = parent.transform;
            source.Play();
            var filter = source.gameObject.AddComponent<DestroyFromFilter>();
            filter.Target = target == 0 ? (Object)source : target == 1 ? source.gameObject : parent;
            filter.Clip = clip;
            Assert.True(AudioFilters.Update(source.Handle, source.gameObject));
            float* samples = stackalloc float[2];
            delegate* unmanaged<int, ulong, float*, int, int, long, void> invoke = &AudioFilters.Invoke;
            invoke(AudioCallbackKind.SourceFilter, source.Handle, samples, 2, 1, 0);
            Assert.True(filter.Called); Assert.True(filter.LoadedInside);
            Assert.True(source.isPlaying); Assert.Same(source, clip.m_StreamBufferOwner);
            Destruction.DeliverRequested(); Destruction.FlushEndOfFrame();
            Assert.False(source); Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
        }
        [Fact]
        public void UnmeasuredReleaseKeepsDeferredPlaybackCleanup()
        {
            ProjectInfo.UnityVersion = "6000.0.72f1";
            var source = Source(Clip(stream: false)); source.Play(); Object.Destroy(source);
            Assert.True(source.isPlaying); Destruction.DeliverRequested(); Destruction.FlushEndOfFrame(); Assert.False(source);
        }
        [Fact]
        public void BackendRejectionDoesNotTransferBufferOwnership()
        {
            var clip = Clip(); var first = Source(clip); var rejected = Source(clip);
            first.Play(); m_Backend.ReleaseClip(clip.m_Handle);
            rejected.Play(); rejected.PlayOneShot(clip); rejected.Stop();
            Assert.Same(first, clip.m_StreamBufferOwner);
            Assert.Equal(AudioDataLoadState.Loaded, clip.loadState);
            first.Stop(); Assert.Equal(AudioDataLoadState.Unloaded, clip.loadState);
        }
        [Fact]
        public void TransfersAndExplicitUnloadUnlinkNodesWithoutRetainingDestroyedClips()
        {
            var a = Clip(); var b = Clip(); var first = Source(a); var last = Source(a);
            first.PlayOneShot(b);
            for (int i = 0; i < 1000; i++) { first.Play(); last.Play(); }
            Assert.Same(b, first.m_StreamBuffers); Assert.Same(a, last.m_StreamBuffers);
            Assert.True(b.UnloadAudioData()); Assert.Null(first.m_StreamBuffers);
            Object.DestroyImmediate(a, true); Assert.Null(last.m_StreamBuffers);
            first.Stop(); last.Stop();
        }
    }
}
