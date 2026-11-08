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
        sealed class ClipLoadStatus
        {
            internal AudioDataLoadState State = AudioDataLoadState.Unloaded;
        }
        ClipLoadStatus m_LoadStatus = new ClipLoadStatus();
        bool m_OwnsLoadStatus = true;
        bool m_CloneAwaitingLoadRequest;
        internal AudioDataLoadState m_LoadState { get => m_LoadStatus.State; set => m_LoadStatus.State = value; }
        /// <summary>Made from an asset file (UnloadAudioData works on those only).</summary>
        internal bool m_IsAsset;
        internal bool m_Stream;
        /// <summary><c>AudioCompressionFormat</c> of an asset's data.</summary>
        internal int m_CompressionFormat;
        // One node per streamed clip; the latest voice owns its cached buffer.
        // Control-thread transfers and cleanup unlink in constant time.
        internal AudioSource m_StreamBufferOwner;
        internal AudioClip m_StreamBufferPrevious, m_StreamBufferNext;
        internal bool m_StreamBufferMain;

        internal bool TracksStreamBuffer => m_IsAsset && m_LoadType == AudioClipLoadType.Streaming && Compat.StreamingClipBufferLifetimeMeasured;

        internal void ClaimStreamBuffer(AudioSource source, bool main)
        {
            if (!TracksStreamBuffer) return;
            DetachStreamBuffer();
            m_StreamBufferOwner = source; m_StreamBufferMain = main;
            m_StreamBufferNext = source.m_StreamBuffers;
            if (!ReferenceEquals(m_StreamBufferNext, null)) m_StreamBufferNext.m_StreamBufferPrevious = this;
            source.m_StreamBuffers = this;
        }

        internal void DetachStreamBuffer()
        {
            if (ReferenceEquals(m_StreamBufferOwner, null)) return;
            if (!ReferenceEquals(m_StreamBufferPrevious, null)) m_StreamBufferPrevious.m_StreamBufferNext = m_StreamBufferNext;
            else m_StreamBufferOwner.m_StreamBuffers = m_StreamBufferNext;
            if (!ReferenceEquals(m_StreamBufferNext, null)) m_StreamBufferNext.m_StreamBufferPrevious = m_StreamBufferPrevious;
            m_StreamBufferOwner = null; m_StreamBufferPrevious = null; m_StreamBufferNext = null;
        }

        internal void ReleaseStreamBuffer()
        {
            DetachStreamBuffer(); m_LoadState = AudioDataLoadState.Unloaded;
        }

        event PCMReaderCallback m_PCMReaderCallback = null;
        event PCMSetPositionCallback m_PCMSetPositionCallback = null;

        /// <summary>Clips whose PCM callbacks the mixer calls, by handle.</summary>
        static readonly ConcurrentDictionary<ulong, AudioClip> s_WithCallbacks = new ConcurrentDictionary<ulong, AudioClip>();

        AudioClip() { }

        public float length => m_Frequency <= 0 ? 0f : !m_IsAsset && Compat.ScriptAudioClipDurationMeasured
            ? (long)m_Samples * 1000 / m_Frequency / 1000f
            : (float)((double)m_Samples / m_Frequency);

        public int samples => m_Samples;

        public int channels => m_Channels;

        public int frequency => m_Frequency;

        [Obsolete("Use AudioClip.loadState instead to get more detailed information about the loading process.")]
        public bool isReadyToPlay => loadState == AudioDataLoadState.Loaded;

        public AudioClipLoadType loadType => m_LoadType;

        public bool preloadAudioData => m_PreloadAudioData;

        public bool ambisonic => m_Ambisonic;

        public bool loadInBackground => m_LoadInBackground;

        public AudioDataLoadState loadState => m_SharedData?.NeedsReload == true || m_CloneAwaitingLoadRequest
            ? AudioDataLoadState.Unloaded : m_LoadState;

        void InitializePreloadedData()
        {
            if (m_PreloadAudioData && (m_LoadType != AudioClipLoadType.Streaming || !Compat.StreamingClipBufferLifetimeMeasured))
                LoadAudioData();
        }

        public bool LoadAudioData()
        {
            ThrowIfDestroyed();
            // A clip without data loads as nothing: an asset's empty clip, a download that did not decode.
            if (m_Handle == 0) return (m_IsAsset || m_Download != DownloadedForm.None) && m_Samples == 0;
            EnsureSharedData();
            m_CloneAwaitingLoadRequest = false;
            if (m_LoadState == AudioDataLoadState.Loaded || m_LoadState == AudioDataLoadState.Loading) return true;
            // Retain the prior unloaded streaming state for unmeasured releases.
            if (m_IsAsset && m_LoadType == AudioClipLoadType.Streaming && !Compat.StreamingClipBufferLifetimeMeasured) return true;
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
            DetachStreamBuffer();
            if (m_OwnsLoadStatus || m_SharedData == null || m_SharedData.References == 1 || m_LoadType == AudioClipLoadType.Streaming)
                m_LoadState = AudioDataLoadState.Unloaded;
            return true;
        }

        /// <summary>Playing a clip loads its data, as Unity does.</summary>
        internal void EnsureLoaded()
        {
            EnsureSharedData();
            m_CloneAwaitingLoadRequest = false;
            if (m_LoadState == AudioDataLoadState.Unloaded || m_LoadState == AudioDataLoadState.Loading)
                m_LoadState = AudioDataLoadState.Loaded;
        }

        public bool GetData(Span<float> data, int offsetSamples)
        {
            if (!CheckChannels("GetData")) return false;
            return ReadData(data, offsetSamples);
        }

        public bool GetData(float[] data, int offsetSamples)
        {
            if (!CheckChannels("GetData")) return false;
            return ReadData(data.AsSpan(), offsetSamples);
        }

        public bool SetData(float[] data, int offsetSamples)
        {
            if (!CheckChannels("SetData")) return false;
            if (offsetSamples < 0 || offsetSamples >= samples)
                throw new ArgumentException("AudioClip.SetData failed; invalid offsetSamples");
            if (data == null || data.Length == 0)
                throw new ArgumentException("AudioClip.SetData failed; invalid data");
            return WriteData(data.AsSpan(), offsetSamples);
        }

        public bool SetData(ReadOnlySpan<float> data, int offsetSamples)
        {
            if (!CheckChannels("SetData")) return false;
            if (offsetSamples < 0 || offsetSamples >= samples)
                throw new ArgumentException("AudioClip.SetData failed; invalid offsetSamples");
            if (data.Length == 0)
                throw new ArgumentException("AudioClip.SetData failed; invalid data");
            return WriteData(data, offsetSamples);
        }

        bool CheckChannels(string api)
        {
            ThrowIfDestroyed();
            if (m_Channels > 0) return true;
            Debug.Log("AudioClip." + api + " failed; AudioClip " + m_Name + " contains no data");
            return false;
        }

        bool ReadData(Span<float> data, int offsetSamples)
        {
            ThrowIfDestroyed();
            if (m_Handle == 0) return false;
            if (Compat.AssetCloneResourcesMeasured) EnsureLoaded();
            if (m_Stream || m_LoadType == AudioClipLoadType.Streaming || m_Download == DownloadedForm.Streamed)
            {
                Debug.LogError($"Can't get data from streamed samples for audio clip \"{m_Name}\". If you created the audio clip with AudioClip.Create, set the 'stream' argument to false, so you can access the data with 'AudioClip.GetData'. For a disk-based audio clip, change the load type to 'DecompressOnLoad', so you can access the data with 'AudioClip.GetData'.", this);
                return false;
            }
            // Compressed data stays compressed in memory: only PCM reads back.
            if ((m_IsAsset && m_LoadType == AudioClipLoadType.CompressedInMemory && m_CompressionFormat != (int)AudioCompressionFormat.PCM) || m_Download == DownloadedForm.Compressed)
            {
                Debug.LogError($"Cannot get data on compressed samples for audio clip \"{DataName}\". Changing the load type to DecompressOnLoad on the audio clip will fix this.", this);
                return false;
            }
            if (offsetSamples < 0) offsetSamples = 0;
            return AudioBackend.Current.ClipData(m_Handle, offsetSamples, data, write: false);
        }

        bool WriteData(ReadOnlySpan<float> data, int offsetSamples)
        {
            ThrowIfDestroyed();
            if (m_Handle == 0) return false;
            if (Compat.AssetCloneResourcesMeasured) EnsureLoaded();
            if (m_Stream || m_LoadType == AudioClipLoadType.Streaming || m_Download == DownloadedForm.Streamed)
            {
                Debug.LogError($"Cannot set data on streamed samples for audio clip \"{m_Name}\". If the AudioClip was created via AudioClip.Create and no PCM read callback was provided, the 'stream' argument must be false. For a disk-based AudioClip changing the load type to DecompressOnLoad on the AudioClip will allow modification of the data.", this);
                return false;
            }
            if ((m_IsAsset && m_LoadType == AudioClipLoadType.CompressedInMemory && m_CompressionFormat != (int)AudioCompressionFormat.PCM) || m_Download == DownloadedForm.Compressed)
            {
                Debug.LogError($"Cannot set data on compressed samples for audio clip \"{DataName}\". Changing the load type to DecompressOnLoad on the audio clip will fix this.", this);
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
            m_LoadType = AudioClipLoadType.DecompressOnLoad;
            m_LoadState = AudioDataLoadState.Loaded;
            m_Handle = AudioBackend.NextHandle();
            m_SharedData = new SharedClipData(AudioBackend.Current, m_Handle);
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

        /// <summary>How a downloaded clip keeps its data (<see cref="UnityEngine.Networking.DownloadHandlerAudioClip"/>):
        /// its samples read back as the clip's load type would let them, which stays DecompressOnLoad.</summary>
        internal enum DownloadedForm { None, Decompressed, Compressed, Streamed }

        internal DownloadedForm m_Download;

        /// <summary>A downloaded clip, nameless: the file decoded as <paramref name="type"/>, or,
        /// <paramref name="headerOnly"/>, a clip of the length a partial file announces with no
        /// samples yet, to be filled by <see cref="FillDownload"/>. A compressed clip stays so for
        /// the codings played compressed (MP3 and ADPCM), the others are decoded. A file that does
        /// not decode gives a clip without data (no channels, samples or frequency) and logs the
        /// Editor's error; one of a type Unbound does not decode logs that instead.</summary>
        internal static AudioClip FromDownload(ReadOnlySpan<byte> data, AudioType type, DownloadedForm form, bool headerOnly)
        {
            var clip = new AudioClip { m_Name = "", m_LoadType = AudioClipLoadType.DecompressOnLoad, m_LoadState = AudioDataLoadState.Loaded, m_Download = form };
            clip.m_Handle = AudioBackend.NextHandle();
            clip.m_SharedData = new SharedClipData(AudioBackend.Current, clip.m_Handle);
            if (!AudioBackend.Current.DecodeClip(clip.m_Handle, data, (int)type, headerOnly, out var info))
            {
                clip.m_SharedData = null;
                clip.m_Handle = 0;
                if (headerOnly) return clip;
                if (type is AudioType.UNKNOWN or AudioType.AIFF or AudioType.MPEG or AudioType.OGGVORBIS or AudioType.WAV)
                    Debug.LogError("Error: Cannot create FMOD::Sound instance for clip \"\" (FMOD error: Unsupported file or audio format. )");
                else
                    Debug.LogError($"Unbound: downloaded audio of AudioType.{type} is not supported; the clip has no data");
                return clip;
            }
            // Until its file is complete a clip made from a header holds no samples (silence).
            if (headerOnly) AudioBackend.Current.CreateClip(clip.m_Handle, info.Frames, info.Channels, info.Frequency, true);
            else if (form == DownloadedForm.Compressed && info.CompressionFormat is not ((int)AudioCompressionFormat.MP3 or (int)AudioCompressionFormat.ADPCM))
                clip.m_Download = DownloadedForm.Decompressed;
            clip.m_Samples = info.Frames;
            clip.m_Channels = info.Channels;
            clip.m_Frequency = info.Frequency;
            return clip;
        }

        /// <summary>Decodes the whole file into a clip made from its header; the clip takes the
        /// decoded format and length.</summary>
        internal void FillDownload(ReadOnlySpan<byte> data, AudioType type)
        {
            if (m_Handle == 0 || !AudioBackend.Current.DecodeClip(m_Handle, data, (int)type, false, out var info)) return;
            m_Samples = info.Frames;
            m_Channels = info.Channels;
            m_Frequency = info.Frequency;
        }

        /// <summary>The clip of an imported audio asset.</summary>
        internal static AudioClip FromImport(string guid, string name)
        {
            AudioClip clip;
            using (ObjectRegistry.AsAssets())
                clip = new AudioClip { m_Name = name, IsPersistentAsset = true, m_IsAsset = true, m_ImportedFrom = guid };
            clip.m_Handle = AudioBackend.NextHandle();
            clip.m_SharedData = new SharedClipData(AudioBackend.Current, clip.m_Handle, guid);
            if (!AudioBackend.Current.ImportClip(clip.m_Handle, guid, out var info))
            {
                clip.m_Handle = 0;
                clip.m_LoadState = AudioDataLoadState.Failed;
                throw new InvalidOperationException("the audio clip could not be imported (see the error above)");
            }
            clip.ApplyImport(info);
            return clip;
        }

        /// <summary>The audio file an imported clip comes from (null for other clips).</summary>
        string m_ImportedFrom;

        /// <summary>The properties an import gives; its data loads now when it preloads.</summary>
        void ApplyImport(in AudioClipInfo info)
        {
            m_Samples = info.Frames;
            m_Channels = info.Channels;
            m_Frequency = info.Frequency;
            m_LoadType = (AudioClipLoadType)info.LoadType;
            m_PreloadAudioData = info.Preload != 0;
            m_LoadInBackground = info.LoadInBackground != 0;
            m_Ambisonic = info.Ambisonic != 0;
            m_CompressionFormat = info.CompressionFormat;
            InitializePreloadedData();
        }

        /// <summary>
        /// Resources.UnloadAsset of an imported clip (UnloadUnusedAssets keeps clips: it cannot load
        /// one back lazily): the file is imported
        /// again into the same mixer handle, so SetData's changes are gone and sources that hold
        /// the clip play the saved samples. Its load state starts over as after loading
        /// (preloaded or unloaded); that, and keeping the one object, are unmeasured for clips
        /// (materials are measured, tests/projects/droppedparams unload.material). False, with
        /// nothing changed, for a clip no audio file made or one that cannot be imported again.
        /// </summary>
        internal bool ReloadImported()
        {
            return ReloadData(m_ImportedFrom, restoreProperties: true);
        }

        bool ReloadData(string key, bool restoreProperties)
        {
            if (key == null || m_Handle == 0) return false;
            if (!AudioBackend.Current.ImportClip(m_Handle, key, out var info)) return false;
            m_SharedData.NeedsReload = false;
            m_LoadState = AudioDataLoadState.Unloaded;
            if (restoreProperties) ApplyImport(info);
            return true;
        }

        internal void OnAudioReset(ulong epoch)
        {
            DetachStreamBuffer();
            if (m_Handle == 0 || (m_SharedData == null || m_SharedData.Backend != AudioBackend.Current)) return;
            if (m_SharedData.Import != null)
            {
                // Reset starts each object's new load request independently of its earlier
                // loaded-resource status. The decoded resource is still shared.
                m_LoadStatus = new ClipLoadStatus();
                m_OwnsLoadStatus = true;
                m_CloneAwaitingLoadRequest = false;
                if (m_SharedData.ResetEpoch != epoch && !ReloadData(m_SharedData.Import, restoreProperties: false))
                {
                    Debug.LogError($"Unbound: AudioSettings.Reset could not reload AudioClip '{m_Name}'.", this);
                    return;
                }
                m_SharedData.ResetEpoch = epoch;
                m_LoadState = AudioDataLoadState.Unloaded;
                return;
            }
            // The object and frequency remain, but the generated sound has no frames or
            // channels. GetData/SetData leave the caller's data untouched and return false.
            OnEngineDestroy();
            m_Samples = 0;
            m_Channels = 0;
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
            m_ImportedFrom = key;
            m_Handle = AudioBackend.NextHandle();
            m_SharedData = new SharedClipData(AudioBackend.Current, m_Handle, key);
            if (!AudioBackend.Current.ImportClip(m_Handle, key, out var info))
            {
                m_Handle = 0;
                m_LoadState = AudioDataLoadState.Failed;
                Debug.LogError($"Unbound: the audio of clip '{m_Name}' could not be read (see the error above).", this);
                return;
            }
            m_Samples = info.Frames;
            InitializePreloadedData();
        }

        internal override void OnEngineDestroy()
        {
            DetachStreamBuffer();
            if (m_Handle == 0) return;
            s_WithCallbacks.TryRemove(m_Handle, out _);
            if (m_SharedData != null)
            {
                if (IsPersistentAsset) m_SharedData.NeedsReload = true;
                if (System.Threading.Interlocked.Decrement(ref m_SharedData.References) == 0)
                    m_SharedData.Backend.ReleaseClip(m_SharedData.Handle);
            }
            m_Handle = 0;
            m_SharedData = null;
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
