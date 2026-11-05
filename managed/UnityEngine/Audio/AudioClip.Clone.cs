using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class AudioClip : Instantiation.ICloneable
    {
        /// <summary>
        /// A clone owns a copy of the source's samples under its own mixer handle, with the
        /// source's frequency, channels, length, load type, compression format, preload,
        /// background-load and ambisonic settings. The mixer keeps every clip's data decoded
        /// (streamed and compressed assets too), so nothing is lost; data access rules follow the
        /// copied load type, as they do for the source. PCM callbacks are not copied: a
        /// non-streamed clip's callback data is already in its samples, while a streamed clip has
        /// no samples to copy and fails loudly.
        /// </summary>
        Object Instantiation.ICloneable.CloneObject()
        {
            if (m_Stream)
                throw new ArgumentException($"Instantiating audio clip '{name}' is not supported because its samples come from its PCM reader callback, which a clone does not copy.");
            var data = m_Handle != 0 && m_Channels > 0 && m_Samples > 0 ? new float[checked(m_Samples * m_Channels)] : null;
            if (data == null || !AudioBackend.Current.ClipData(m_Handle, 0, data, write: false))
                throw new ArgumentException($"Instantiating audio clip '{name}' is not supported because its audio data is unavailable.");

            var copy = new AudioClip
            {
                m_Samples = m_Samples,
                m_Channels = m_Channels,
                m_Frequency = m_Frequency,
                m_LoadType = m_LoadType,
                m_PreloadAudioData = m_PreloadAudioData,
                m_Ambisonic = m_Ambisonic,
                m_LoadInBackground = m_LoadInBackground,
                m_IsAsset = m_IsAsset,
                m_CompressionFormat = m_CompressionFormat,
            };
            copy.m_Handle = AudioBackend.NextHandle();
            AudioBackend.Current.CreateClip(copy.m_Handle, m_Samples, m_Channels, m_Frequency, stream: false);
            if (!AudioBackend.Current.ClipData(copy.m_Handle, 0, data, write: true))
            {
                Object.DestroyImmediate(copy);
                throw new ArgumentException($"Instantiating audio clip '{name}' failed: the copy of its audio data could not be stored.");
            }
            // A clip made from asset data loads as an imported one does (its preload setting);
            // a script-made clip's data is in memory.
            if (m_IsAsset)
            {
                copy.m_LoadState = AudioDataLoadState.Unloaded;
                if (m_PreloadAudioData) copy.LoadAudioData();
            }
            else
            {
                copy.m_LoadState = AudioDataLoadState.Loaded;
            }
            return copy;
        }
    }
}
