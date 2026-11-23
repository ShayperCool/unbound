using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class Material
    {
        // The ordinary shader property storage measured in tests/projects/materialmemory.
        const long ProfilerKeyBytes = 12;
        const long ProfilerWordBytes = 4;

        void StartMeasuringPropertyStorage()
        {
            if (Compat.MaterialScalarMemoryMeasured && m_Shader.ProfilerPropertyLayout is not null
                && m_Keywords.Count == 0)
                m_State.m_ProfilerLayoutVersion = m_Shader.ProfilerLayoutVersion;
        }

        void StopMeasuringPropertyStorage()
        {
            m_State.m_ProfilerLayoutVersion = null;
            m_State.m_ProfilerAddedFloats = null;
        }

        void RecordProfilerPropertyWrite(int nameID, ShaderPropertyType type)
        {
            if (!m_State.m_ProfilerLayoutVersion.HasValue) return;
            var declared = m_Shader.PropertyById(nameID)?.Type;
            if (declared == type
                || type == ShaderPropertyType.Float && declared == ShaderPropertyType.Range
                || type == ShaderPropertyType.Vector && declared == ShaderPropertyType.Color)
                return;
            if (declared == null && type == ShaderPropertyType.Float && Shader.IdToName(nameID) != null)
                (m_State.m_ProfilerAddedFloats ??= new HashSet<int>()).Add(nameID);
            else
                StopMeasuringPropertyStorage();
        }

        bool TryGetProfilerPropertyLayout(out (long Keys, long Words, bool MixedWords) layout)
        {
            if (!IsPersistentAsset && m_Parent == null && m_State.m_ProfilerLayoutVersion is { } version && m_Shader != null
                && m_Shader.ProfilerLayoutVersion == version && m_Shader.ProfilerPropertyLayout is { } current
                && m_RawBindings.Count == 0)
            {
                layout = current;
                return true;
            }
            layout = default;
            return false;
        }

        internal long? ProfilerPropertyStorageBytes
        {
            get
            {
                if (!TryGetProfilerPropertyLayout(out var layout)) return null;
                long extra = m_State.m_ProfilerAddedFloats?.Count ?? 0;
                long keys = layout.Keys + extra;
                long words = layout.Words + extra;
                long keySlots = AppendedPropertyCapacity(layout.Keys, keys);
                long wordSlots = layout.MixedWords ? AppendedPropertyCapacity(layout.Words, words) : words;
                return (keys + keySlots) * ProfilerKeyBytes + (words + wordSlots) * ProfilerWordBytes;
            }
        }

        static long AppendedPropertyCapacity(long initial, long count)
        {
            if (initial >= count) return initial;
            long capacity = Math.Max(initial, 1);
            while (capacity < count) capacity *= 2;
            return capacity;
        }
    }
}
