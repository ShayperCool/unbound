using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AudioClip.GetData / SetData failure contracts on the managed twin of the mixer
    /// (<see cref="VirtualAudioBackend"/>). The zeroed failure buffer and the compressed SetData
    /// restriction are the Unity 6000.0 Scripting API's documented guarantees; these are internal
    /// regressions of them, not Native observations (the bundleaudio reference reads into fresh,
    /// already zero buffers and never calls SetData). Messages Unity's console was measured to show
    /// are kept verbatim; "Unbound:" messages, the SetData return value on compressed clips and the
    /// "preservation" cases are Unbound's own, unmeasured in Unity.
    /// </summary>
    public class AudioClipDataContractTests : IDisposable
    {
        const string StreamedMessage = "Can't get data from streamed samples for audio clip \"{0}\". If you created the audio clip with AudioClip.Create, set the 'stream' argument to false, so you can access the data with 'AudioClip.GetData'. For a disk-based audio clip, change the load type to 'DecompressOnLoad', so you can access the data with 'AudioClip.GetData'.";
        const string CompressedMessage = "Cannot get data on compressed samples for audio clip \"{0}\". Changing the load type to DecompressOnLoad on the audio clip will fix this.";

        static readonly FieldInfo CurrentBackend = typeof(AudioBackend).GetField("s_Current", BindingFlags.Static | BindingFlags.NonPublic);

        readonly AudioBackend m_PreviousBackend;
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly List<Object> m_Created = new List<Object>();
        readonly List<(LogType Type, string Message)> m_Logs = new List<(LogType, string)>();

        public AudioClipDataContractTests()
        {
            m_PreviousBackend = (AudioBackend)CurrentBackend.GetValue(null);
            AudioBackend.Use(m_Backend);
            Application.logMessageReceived += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= Capture;
            // Releases the clips' handles (and callback registrations) on this test's backend.
            foreach (var o in m_Created) Object.DestroyImmediate(o, true);
            CurrentBackend.SetValue(null, m_PreviousBackend);
        }

        void Capture(string message, string stack, LogType type) => m_Logs.Add((type, message));

        List<string> Errors() => m_Logs.FindAll(l => l.Type == LogType.Error).ConvertAll(l => l.Message);

        T Track<T>(T o) where T : Object
        {
            m_Created.Add(o);
            return o;
        }

        static float[] Filled(int length, float value)
        {
            var data = new float[length];
            Array.Fill(data, value);
            return data;
        }

        static float[] Ramp(int length)
        {
            var data = new float[length];
            for (int i = 0; i < data.Length; i++) data[i] = (i + 1) / 100f;
            return data;
        }

        /// <summary>An imported clip as the native importer reports it (load type, format).</summary>
        AudioClip Imported(string name, AudioClipLoadType loadType, AudioCompressionFormat format, float[] samples, int frames = 8, int channels = 2)
        {
            string guid = "audio-clip-data-" + name;
            m_Backend.Assets[guid] = (new AudioClipInfo
            {
                Frames = frames, Channels = channels, Frequency = 1000, LoadType = (int)loadType, Preload = 1, CompressionFormat = (int)format,
            }, samples);
            return Track(AudioClip.FromImport(guid, name));
        }

        /// <summary>A bundle clip whose audio stream Unbound has no data for: ReadSerialized logs and
        /// leaves it without a native handle, its properties read.</summary>
        AudioClip WithoutNativeData(string name, int channels = 2)
        {
            var clip = Track((AudioClip)Activator.CreateInstance(typeof(AudioClip), nonPublic: true));
            clip.m_Name = name;
            ((ISerializedAsset)clip).ReadSerialized(TestYaml.DocumentBody($@"
AudioClip:
  m_Name: {name}
  m_LoadType: 0
  m_Channels: {channels}
  m_Frequency: 1000
  m_Length: 0.008
  m_PreloadAudioData: 0
  m_LoadInBackground: 0
  m_Ambisonic: 0
  m_CompressionFormat: 0
"), null);
            Assert.Equal(0ul, clip.m_Handle);
            m_Logs.Clear();
            return clip;
        }

        float[] Stored(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            Assert.True(m_Backend.ClipData(clip.m_Handle, 0, data, write: false));
            return data;
        }

        // ------------------------------------------------------------------ GetData failures (documented zeroes)

        [Fact]
        public void GetDataOnAStreamedRuntimeClipZeroesAReusedBuffer()
        {
            var clip = Track(AudioClip.Create("streamed", 64, 1, 1000, true, data => { }));
            var buffer = Filled(16, 1f);
            Assert.False(clip.GetData(buffer, 0));
            Assert.All(buffer, s => Assert.Equal(0f, s));
            Assert.Equal(new[] { string.Format(StreamedMessage, "streamed") }, Errors());
        }

        [Theory]
        [InlineData(AudioClipLoadType.Streaming, AudioCompressionFormat.Vorbis, false)]
        [InlineData(AudioClipLoadType.Streaming, AudioCompressionFormat.PCM, true)]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, true)]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.ADPCM, false)]
        public void GetDataOnAnUnreadableAssetZeroesAReusedBuffer(AudioClipLoadType loadType, AudioCompressionFormat format, bool span)
        {
            var clip = Imported($"unreadable-{loadType}-{format}", loadType, format, Ramp(16));
            var buffer = Filled(16, -0.5f);
            Assert.False(span ? clip.GetData(buffer.AsSpan(), 3) : clip.GetData(buffer, 3));
            Assert.All(buffer, s => Assert.Equal(0f, s));
            string expected = loadType == AudioClipLoadType.Streaming ? StreamedMessage : CompressedMessage;
            Assert.Equal(new[] { string.Format(expected, clip.name) }, Errors());
        }

        [Fact]
        public void GetDataClearsWhatASuccessfulReadLeftWhenTheNextClipCannotBeRead()
        {
            var readable = Imported("decompressed", AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.Vorbis, Ramp(16));
            var compressed = Imported("compressed", AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, Ramp(16));
            var buffer = new float[16];
            Assert.True(readable.GetData(buffer, 0));
            Assert.Equal(Ramp(16), buffer);
            Assert.False(compressed.GetData(buffer, 0));
            Assert.Equal(new float[16], buffer);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetDataOnAClipWithoutNativeDataZeroesTheBufferAndReportsIt(bool span)
        {
            var clip = WithoutNativeData("lost");
            var buffer = Filled(16, 1f);
            Assert.False(span ? clip.GetData(buffer.AsSpan(), 0) : clip.GetData(buffer, 0));
            Assert.Equal(new float[16], buffer);
            var error = Assert.Single(Errors());
            Assert.StartsWith("Unbound: AudioClip.GetData failed", error);
            Assert.Contains("\"lost\"", error);
        }

        [Fact]
        public void GetDataZeroesTheBufferWhenTheBackendReadsNothing()
        {
            // The twin holds no samples for this clip (an empty store), so its read returns false.
            var clip = Imported("empty", AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.PCM, Array.Empty<float>());
            var buffer = Filled(16, 0.25f);
            Assert.False(clip.GetData(buffer, 0));
            Assert.Equal(new float[16], buffer);
            Assert.StartsWith("Unbound: AudioClip.GetData failed", Assert.Single(Errors()));
        }

        // ------------------------------------------------------------------ GetData preservation controls

        [Fact]
        public void GetDataStillReadsWithWrapAroundAndANegativeOffsetFromTheStart()
        {
            var clip = Track(AudioClip.Create("ring", 4, 2, 1000, false));
            Assert.True(clip.SetData(Ramp(8), 0));
            var wrapped = Filled(6, 9f);
            Assert.True(clip.GetData(wrapped.AsSpan(), 3));
            Assert.Equal(new[] { 0.07f, 0.08f, 0.01f, 0.02f, 0.03f, 0.04f }, wrapped);
            var clamped = Filled(4, 9f);
            Assert.True(clip.GetData(clamped, -2));
            Assert.Equal(new[] { 0.01f, 0.02f, 0.03f, 0.04f }, clamped);
            Assert.Empty(Errors());
        }

        [Theory]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.PCM)]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.Vorbis)]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.ADPCM)]
        public void GetDataStillReadsPcmAndDecompressedAssets(AudioClipLoadType loadType, AudioCompressionFormat format)
        {
            var clip = Imported($"readable-{loadType}-{format}", loadType, format, Ramp(16));
            var buffer = Filled(16, 9f);
            Assert.True(clip.GetData(buffer, 0));
            Assert.Equal(Ramp(16), buffer);
            Assert.Empty(Errors());
        }

        [Fact]
        public void GetDataWithoutChannelsKeepsItsEarlyReturn()
        {
            // Internal preservation control of the unchanged early return; unmeasured in Unity.
            var clip = WithoutNativeData("silent", channels: 0);
            var buffer = Filled(4, 1f);
            Assert.False(clip.GetData(buffer, 0));
            Assert.Equal(Filled(4, 1f), buffer);
            Assert.Empty(Errors());
            Assert.Contains(m_Logs, l => l.Type == LogType.Log && l.Message == "AudioClip.GetData failed; AudioClip silent contains no data");
        }

        [Fact]
        public void GetDataOnADestroyedClipThrowsBeforeTouchingTheBuffer()
        {
            var clip = Track(AudioClip.Create("gone", 4, 1, 1000, false));
            Object.DestroyImmediate(clip);
            var buffer = Filled(4, 1f);
            Assert.Throws<MissingReferenceException>(() => clip.GetData(buffer, 0));
            Assert.Equal(Filled(4, 1f), buffer);
        }

        // ------------------------------------------------------------------ SetData on compressed clips (documented)

        [Theory]
        [InlineData(AudioCompressionFormat.Vorbis, false)]
        [InlineData(AudioCompressionFormat.ADPCM, true)]
        public void SetDataOnACompressedInMemoryAssetIsRefusedAndKeepsItsSamples(AudioCompressionFormat format, bool span)
        {
            var clip = Imported($"packed-{format}", AudioClipLoadType.CompressedInMemory, format, Ramp(16));
            var data = Filled(16, 0.5f);
            Assert.False(span ? clip.SetData((ReadOnlySpan<float>)data, 0) : clip.SetData(data, 0));
            Assert.Equal(Ramp(16), Stored(clip));
            var error = Assert.Single(Errors());
            Assert.StartsWith("Unbound: AudioClip.SetData failed", error);
            Assert.Contains($"\"{clip.name}\"", error);
        }

        [Fact]
        public void SetDataArgumentGuardsStillThrowFirstOnACompressedAsset()
        {
            var clip = Imported("guarded", AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, Ramp(16));
            Assert.Throws<ArgumentException>(() => clip.SetData(Array.Empty<float>(), 0));
            Assert.Throws<ArgumentException>(() => clip.SetData(Filled(2, 0.5f), clip.samples));
            Assert.Throws<ArgumentException>(() => clip.SetData((ReadOnlySpan<float>)Filled(2, 0.5f), -1));
            Assert.Equal(Ramp(16), Stored(clip));
            Assert.Empty(Errors());
        }

        // ------------------------------------------------------------------ SetData preservation controls

        [Theory]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.PCM)]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.Vorbis)]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.ADPCM)]
        public void SetDataStillWritesPcmAndDecompressedAssetsWithWrapAround(AudioClipLoadType loadType, AudioCompressionFormat format)
        {
            var clip = Imported($"writable-{loadType}-{format}", loadType, format, new float[16]);
            Assert.True(clip.SetData((ReadOnlySpan<float>)new[] { 0.1f, 0.2f, 0.3f, 0.4f }, 7));
            var expected = new float[16];
            expected[14] = 0.1f; expected[15] = 0.2f; expected[0] = 0.3f; expected[1] = 0.4f;
            Assert.Equal(expected, Stored(clip));
            Assert.Empty(Errors());
        }

        [Fact]
        public void SetDataStillWritesARuntimeClip()
        {
            var clip = Track(AudioClip.Create("procedural", 8, 2, 1000, false));
            Assert.True(clip.SetData(Ramp(16), 0));
            var buffer = new float[16];
            Assert.True(clip.GetData(buffer, 0));
            Assert.Equal(Ramp(16), buffer);
            Assert.Empty(Errors());
        }

        [Fact]
        public void SetDataOnAStreamedClipKeepsItsMessage()
        {
            var clip = Track(AudioClip.Create("live", 64, 1, 1000, true, data => { }));
            Assert.False(clip.SetData(Filled(4, 0.5f), 0));
            Assert.Equal(new[] { "AudioClip.SetData failed; AudioClip live is streamed" }, Errors());
        }

        // ------------------------------------------------------------------ SetData without native data (Unbound diagnostics)

        [Fact]
        public void SetDataWithoutNativeDataOrABackendStoreReportsIt()
        {
            var lost = WithoutNativeData("lost");
            Assert.False(lost.SetData(Filled(4, 0.5f), 0));
            var empty = Imported("empty", AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.PCM, Array.Empty<float>());
            Assert.False(empty.SetData((ReadOnlySpan<float>)Filled(4, 0.5f), 0));
            var errors = Errors();
            Assert.Equal(2, errors.Count);
            Assert.All(errors, e => Assert.StartsWith("Unbound: AudioClip.SetData failed", e));
            Assert.Contains("\"lost\"", errors[0]);
            Assert.Contains("\"empty\"", errors[1]);
        }
    }
}
