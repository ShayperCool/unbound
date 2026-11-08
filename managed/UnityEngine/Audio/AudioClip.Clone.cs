using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class AudioClip : Instantiation.ICloneable
    {
        // Imported clips share the decoded resource, while each Object keeps its own identity
        // and loading state. Runtime PCM data and callbacks are not serialized into a clone.
        sealed class SharedClipData
        {
            internal int References = 1;
            internal string Name;
            internal string Import;
            internal bool NeedsReload;
            internal readonly AudioBackend Backend;
            internal readonly ulong Handle;
            internal ulong ResetEpoch;
            internal SharedClipData(AudioBackend backend, ulong handle, string import = null)
            { Backend = backend; Handle = handle; Import = import; }
        }

        SharedClipData m_SharedData;

        Object Instantiation.ICloneable.CloneObject()
        {
            if (!Compat.AssetCloneResourcesMeasured)
                throw new NotSupportedException("AudioClip instantiation is measured only for Unity 6000.0.71f1.");
            var copy = new AudioClip
            {
                m_Channels = m_Channels,
                m_Frequency = m_Frequency,
                m_PreloadAudioData = m_PreloadAudioData,
                m_Ambisonic = m_Ambisonic,
                m_LoadInBackground = m_LoadInBackground,
                m_IsAsset = true,
            };
            if (m_IsAsset && m_Handle != 0)
            {
                m_SharedData.Name ??= m_Name;
                System.Threading.Interlocked.Increment(ref m_SharedData.References);
                copy.m_SharedData = m_SharedData;
                copy.m_Handle = m_Handle;
                copy.m_Samples = m_Samples;
                copy.m_LoadType = m_LoadType;
                copy.m_CompressionFormat = m_CompressionFormat;
                copy.InitializePreloadedData();
            }
            return copy;
        }

        string DataName => m_SharedData?.Name ?? m_Name;

        void EnsureSharedData()
        {
            if (m_SharedData == null || !m_SharedData.NeedsReload) return;
            if (m_SharedData.Import == null || !AudioBackend.Current.ImportClip(m_Handle, m_SharedData.Import, out _))
                throw new NotSupportedException("The audio resource cannot be reloaded after its asset was destroyed.");
            m_SharedData.NeedsReload = false;
        }
    }
}
