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
        const long ProfilerInitialPropertyStateBytes = 416;

        enum ProfilerStorage : byte { Initialized, Pending, DrawPrepared }

        void RealizeProfilerPropertyStorage()
        {
            if (m_State.m_ProfilerStorage == ProfilerStorage.Pending)
                m_State.m_ProfilerStorage = ProfilerStorage.Initialized;
        }

        internal void PrepareProfilerDraw()
        {
            if (m_State.m_ProfilerStorage == ProfilerStorage.Pending)
                m_State.m_ProfilerStorage = ProfilerStorage.DrawPrepared;
        }

        internal void TrackProfilerBlockSource(Material source)
        {
            m_State.m_ProfilerBlockSource = source.m_State.m_ProfilerStorage == ProfilerStorage.Pending ? source : null;
        }

        internal void MarkProfilerDrawEmitted()
        {
            PrepareProfilerDraw();
            var source = m_State.m_ProfilerBlockSource;
            if (source is null) return;
            m_State.m_ProfilerBlockSource = null;
            source.PrepareProfilerDraw();
        }

        internal void ObserveProfilerPass(int pass, bool allPasses = false)
        {
            if (m_State.m_ProfilerStorage != ProfilerStorage.Pending) return;
            bool? usable = m_Shader != null ? m_Shader.ProfilerPassAvailable(pass, allPasses) : null;
            if (usable == true) RealizeProfilerPropertyStorage();
            else if (!usable.HasValue) PrepareProfilerDraw();
        }

        void StartMeasuringPropertyStorage()
        {
            if (Compat.MaterialScalarMemoryMeasured && !IsPersistentAsset
                && m_Shader.ProfilerPropertyLayout is not null && m_Keywords.Count == 0)
                m_State.m_ProfilerLayoutVersion = m_Shader.ProfilerLayoutVersion;
        }

        void StopMeasuringPropertyStorage()
        {
            if (!m_State.m_ProfilerLayoutVersion.HasValue) return;
            m_State.m_ProfilerLayoutVersion = null;
            m_State.m_ProfilerAddedFloats = null;
            m_State.m_ProfilerStorage = ProfilerStorage.Initialized;
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
                && m_Shader.ProfilerLayoutVersion == version && m_Shader.ProfilerPropertyLayout is { } current)
            {
                layout = current;
                return true;
            }
            layout = default;
            return false;
        }

        internal long? ProfilerAllocationBytes(int initializedBaseBytes)
        {
            if (!TryGetProfilerPropertyLayout(out var layout)) return null;
            switch (m_State.m_ProfilerStorage)
            {
                case ProfilerStorage.Pending: return initializedBaseBytes - ProfilerInitialPropertyStateBytes;
                // A camera draw can leave a different allocation that survives a property read.
                case ProfilerStorage.DrawPrepared: return null;
            }
            long extra = m_State.m_ProfilerAddedFloats?.Count ?? 0;
            long keys = layout.Keys + extra;
            long words = layout.Words + extra;
            long keySlots = AppendedPropertyCapacity(layout.Keys, keys);
            long wordSlots = layout.MixedWords ? AppendedPropertyCapacity(layout.Words, words) : words;
            return initializedBaseBytes + (keys + keySlots) * ProfilerKeyBytes + (words + wordSlots) * ProfilerWordBytes;
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
