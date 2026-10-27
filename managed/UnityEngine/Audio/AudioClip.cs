using System;
using System.Collections.Concurrent;
using Unbound.Runtime;
using UnityEngine.Audio;

namespace UnityEngine.Audio
{
    public abstract partial class AudioResource : Object
    {
        protected internal AudioResource() { }
    }
}

namespace UnityEngine
{
    /// <summary>
    /// Audio data: an imported asset (decoded by the native importer with the active build
    /// target's AudioImporter settings), a player clip of an AssetBundle (its FSB5 bank decoded
    /// natively) or a clip made by <c>AudioClip.Create</c>. The samples live in the native mixer, by
    /// handle.
    /// </summary>
    [ClassId(83)]
    public sealed partial class AudioClip : AudioResource, ISerializedAsset
    {
        public delegate void PCMReaderCallback(float[] data);

        public delegate void PCMSetPositionCallback(int position);

        internal ulong m_Handle;
        internal int m_Samples;
        internal int m_Channels;
        internal int m_Frequency;
        internal AudioClipLoadType m_LoadType;
        internal bool m_PreloadAudioData = true;
        internal bool m_Ambisonic;
        internal bool m_LoadInBackground;
        internal AudioDataLoadState m_LoadState = AudioDataLoadState.Unloaded;
        /// <summary>Made from an asset file (UnloadAudioData works on those only).</summary>
        internal bool m_IsAsset;
        internal bool m_Stream;
        /// <summary><c>AudioCompressionFormat</c> of an asset's data.</summary>
        internal int m_CompressionFormat;

        event PCMReaderCallback m_PCMReaderCallback = null;
        event PCMSetPositionCallback m_PCMSetPositionCallback = null;

        /// <summary>Clips whose PCM callbacks the mixer calls, by handle.</summary>
        static readonly ConcurrentDictionary<ulong, AudioClip> s_WithCallbacks = new ConcurrentDictionary<ulong, AudioClip>();

        AudioClip() { }

        public float length => m_Frequency > 0 ? (float)((double)m_Samples / m_Frequency) : 0f;

        public int samples => m_Samples;

        public int channels => m_Channels;

        public int frequency => m_Frequency;

        [Obsolete("Use AudioClip.loadState instead to get more detailed information about the loading process.")]
        public bool isReadyToPlay => m_LoadState == AudioDataLoadState.Loaded;

        public AudioClipLoadType loadType => m_LoadType;

        public bool preloadAudioData => m_PreloadAudioData;

        public bool ambisonic => m_Ambisonic;

        public bool loadInBackground => m_LoadInBackground;

        public AudioDataLoadState loadState => m_LoadState;

        public bool LoadAudioData()
        {
            ThrowIfDestroyed();
            if (m_Handle == 0) return false;
            if (m_LoadState == AudioDataLoadState.Loaded || m_LoadState == AudioDataLoadState.Loading) return true;
            // A streamed asset reads its data as it plays: as in Unity, it stays unloaded.
            if (m_IsAsset && m_LoadType == AudioClipLoadType.Streaming) return true;
            if (m_LoadInBackground)
            {
                // Loaded on the next audio update, as a background load completes a frame later.
                m_LoadState = AudioDataLoadState.Loading;
                AudioSystem.QueueLoad(this);
            }
            else
            {
                m_LoadState = AudioDataLoadState.Loaded;
            }
            return true;
        }

        public bool UnloadAudioData()
        {
            ThrowIfDestroyed();
            if (!m_IsAsset) return false;
            m_LoadState = AudioDataLoadState.Unloaded;
            return true;
        }

        /// <summary>Playing a clip loads its data, as Unity does.</summary>
        internal void EnsureLoaded()
        {
            if (m_IsAsset && m_LoadType == AudioClipLoadType.Streaming) return;
            if (m_LoadState == AudioDataLoadState.Unloaded || m_LoadState == AudioDataLoadState.Loading)
                m_LoadState = AudioDataLoadState.Loaded;
        }

        public bool GetData(Span<float> data, int offsetSamples)
        {
            if (channels <= 0)
            {
                Debug.Log("AudioClip.GetData failed; AudioClip " + m_Name + " contains no data");
                return false;
            }
            return ReadData(data, offsetSamples);
        }

        public bool GetData(float[] data, int offsetSamples)
        {
            if (channels <= 0)
            {
                Debug.Log("AudioClip.GetData failed; AudioClip " + m_Name + " contains no data");
                return false;
            }
            return ReadData(data.AsSpan(), offsetSamples);
        }

        public bool SetData(float[] data, int offsetSamples)
        {
            if (channels <= 0)
            {
                Debug.Log("AudioClip.SetData failed; AudioClip " + m_Name + " contains no data");
                return false;
            }
            if (offsetSamples < 0 || offsetSamples >= samples)
                throw new ArgumentException("AudioClip.SetData failed; invalid offsetSamples");
            if (data == null || data.Length == 0)
                throw new ArgumentException("AudioClip.SetData failed; invalid data");
            return WriteData(data.AsSpan(), offsetSamples);
        }

        public bool SetData(ReadOnlySpan<float> data, int offsetSamples)
        {
            if (channels <= 0)
            {
                Debug.Log("AudioClip.SetData failed; AudioClip " + m_Name + " contains no data");
                return false;
            }
            if (offsetSamples < 0 || offsetSamples >= samples)
                throw new ArgumentException("AudioClip.SetData failed; invalid offsetSamples");
            if (data.Length == 0)
                throw new ArgumentException("AudioClip.SetData failed; invalid data");
            return WriteData(data, offsetSamples);
        }

        bool ReadData(Span<float> data, int offsetSamples)
        {
            ThrowIfDestroyed();
            if (m_Stream || m_LoadType == AudioClipLoadType.Streaming)
            {
                Debug.LogError($"Can't get data from streamed samples for audio clip \"{m_Name}\". If you created the audio clip with AudioClip.Create, set the 'stream' argument to false, so you can access the data with 'AudioClip.GetData'. For a disk-based audio clip, change the load type to 'DecompressOnLoad', so you can access the data with 'AudioClip.GetData'.", this);
                return false;
            }
            // Compressed data stays compressed in memory: only PCM reads back.
            if (m_IsAsset && m_LoadType == AudioClipLoadType.CompressedInMemory && m_CompressionFormat != (int)AudioCompressionFormat.PCM)
            {
                Debug.LogError($"Cannot get data on compressed samples for audio clip \"{m_Name}\". Changing the load type to DecompressOnLoad on the audio clip will fix this.", this);
                return false;
            }
            if (offsetSamples < 0) offsetSamples = 0;
            return AudioBackend.Current.ClipData(m_Handle, offsetSamples, data, write: false);
        }

        bool WriteData(ReadOnlySpan<float> data, int offsetSamples)
        {
            ThrowIfDestroyed();
            if (m_Stream || m_LoadType == AudioClipLoadType.Streaming)
            {
                Debug.LogError($"AudioClip.SetData failed; AudioClip {m_Name} is streamed", this);
                return false;
            }
            var copy = data.ToArray();
            return AudioBackend.Current.ClipData(m_Handle, offsetSamples, copy, write: true);
        }

        [Obsolete("The _3D argument of AudioClip is deprecated. Use the spatialBlend property of AudioSource instead to morph between 2D and 3D playback.")]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream) =>
            Create(name, lengthSamples, channels, frequency, stream);

        [Obsolete("The _3D argument of AudioClip is deprecated. Use the spatialBlend property of AudioSource instead to morph between 2D and 3D playback.")]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream, PCMReaderCallback pcmreadercallback) =>
            Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, null);

        [Obsolete("The _3D argument of AudioClip is deprecated. Use the spatialBlend property of AudioSource instead to morph between 2D and 3D playback.")]
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream, PCMReaderCallback pcmreadercallback, PCMSetPositionCallback pcmsetpositioncallback) =>
            Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, pcmsetpositioncallback);

        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) =>
            Create(name, lengthSamples, channels, frequency, stream, null, null);

        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream, PCMReaderCallback pcmreadercallback) =>
            Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, null);

        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream, PCMReaderCallback pcmreadercallback, PCMSetPositionCallback pcmsetpositioncallback)
        {
            if (name == null) throw new NullReferenceException();
            if (lengthSamples <= 0) throw new ArgumentException("Length of created clip must be larger than 0");
            if (channels <= 0) throw new ArgumentException("Number of channels in created clip must be greater than 0");
            if (frequency <= 0) throw new ArgumentException("Frequency in created clip must be greater than 0");
            var clip = new AudioClip();
            if (pcmreadercallback != null) clip.m_PCMReaderCallback += pcmreadercallback;
            if (pcmsetpositioncallback != null) clip.m_PCMSetPositionCallback += pcmsetpositioncallback;
            clip.CreateUserClip(name, lengthSamples, channels, frequency, stream);
            return clip;
        }

        void CreateUserClip(string name, int lengthSamples, int channels, int frequency, bool stream)
        {
            m_Name = name;
            m_Samples = lengthSamples;
            m_Channels = channels;
            m_Frequency = frequency;
            m_Stream = stream && m_PCMReaderCallback != null;
            m_LoadType = m_Stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            m_LoadState = AudioDataLoadState.Loaded;
            m_Handle = AudioBackend.NextHandle();
            AudioBackend.Current.CreateClip(m_Handle, lengthSamples, channels, frequency, m_Stream);
            if (m_Stream)
            {
                s_WithCallbacks[m_Handle] = this;
            }
            else if (m_PCMReaderCallback != null)
            {
                // A non-streamed clip reads its whole data once, at creation.
                InvokePCMSetPosition(0);
                var data = new float[lengthSamples * channels];
                InvokePCMReader(data);
                AudioBackend.Current.ClipData(m_Handle, 0, data, write: true);
            }
        }

        internal void InvokePCMReader(float[] data) => m_PCMReaderCallback?.Invoke(data);

        internal void InvokePCMSetPosition(int position) => m_PCMSetPositionCallback?.Invoke(position);

        internal static AudioClip WithCallbacks(ulong handle) => s_WithCallbacks.TryGetValue(handle, out var clip) ? clip : null;

        /// <summary>The clip of an imported audio asset.</summary>
        internal static AudioClip FromImport(string guid, string name)
        {
            AudioClip clip;
            using (ObjectRegistry.AsAssets())
                clip = new AudioClip { m_Name = name, IsPersistentAsset = true, m_IsAsset = true };
            clip.m_Handle = AudioBackend.NextHandle();
            if (!AudioBackend.Current.ImportClip(clip.m_Handle, guid, out var info))
            {
                clip.m_Handle = 0;
                clip.m_LoadState = AudioDataLoadState.Failed;
                throw new InvalidOperationException("the audio clip could not be imported (see the error above)");
            }
            clip.m_Samples = info.Frames;
            clip.m_Channels = info.Channels;
            clip.m_Frequency = info.Frequency;
            clip.m_LoadType = (AudioClipLoadType)info.LoadType;
            clip.m_PreloadAudioData = info.Preload != 0;
            clip.m_LoadInBackground = info.LoadInBackground != 0;
            clip.m_Ambisonic = info.Ambisonic != 0;
            clip.m_CompressionFormat = info.CompressionFormat;
            if (clip.m_PreloadAudioData) clip.LoadAudioData();
            return clip;
        }

        /// <summary>A player clip (an AssetBundle's): its properties, and its FSB5 bank, in the
        /// bundle's stream file, decoded natively.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_IsAsset = true;
            m_LoadType = (AudioClipLoadType)body.Int("m_LoadType");
            m_Channels = body.Int("m_Channels");
            m_Frequency = body.Int("m_Frequency");
            m_Samples = (int)Math.Round(body.Float("m_Length") * (double)m_Frequency);
            m_PreloadAudioData = body.Bool("m_PreloadAudioData");
            m_LoadInBackground = body.Bool("m_LoadInBackground");
            m_Ambisonic = body.Bool("m_Ambisonic");
            m_CompressionFormat = body.Int("m_CompressionFormat");
            var resource = body.Get("m_Resource");
            var stream = resource?.Str("m_Source") ?? "";
            ulong bundle = resolver is LoadedFile file ? BundleFiles.NativeId(file.Guid) : 0;
            if (bundle == 0 || stream.Length == 0)
            {
                Debug.LogError($"Unbound: audio clip '{m_Name}' has no audio data Unbound can read (only AssetBundles' clips keep theirs in the clip).", this);
                return;
            }
            var key = $"bundle:{bundle}/{stream.Substring(stream.LastIndexOf('/') + 1)}:{resource.Long("m_Offset")}:{resource.Long("m_Size")}:{body.Int("m_SubsoundIndex")}";
            m_Handle = AudioBackend.NextHandle();
            if (!AudioBackend.Current.ImportClip(m_Handle, key, out var info))
            {
                m_Handle = 0;
                m_LoadState = AudioDataLoadState.Failed;
                Debug.LogError($"Unbound: the audio of clip '{m_Name}' could not be read (see the error above).", this);
                return;
            }
            m_Samples = info.Frames;
            if (m_PreloadAudioData) LoadAudioData();
        }

        internal override void OnEngineDestroy()
        {
            if (m_Handle == 0) return;
            s_WithCallbacks.TryRemove(m_Handle, out _);
            AudioBackend.Current.ReleaseClip(m_Handle);
            m_Handle = 0;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>The objects of an imported audio file: its AudioClip.</summary>
    internal static class AudioClipImporter
    {
        internal const long AudioClipFileId = 8300000;

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var clip = AudioClip.FromImport(info.Guid, System.IO.Path.GetFileNameWithoutExtension(info.Path));
            var file = new LoadedFile { MainObject = clip };
            file.Objects[AudioClipFileId] = clip;
            return file;
        }
    }
}
