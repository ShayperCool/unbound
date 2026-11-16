using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// GPU events: particles of one system spawned by the kernels of another. A block of the
    /// spawning system appends, for each particle that triggers, that particle's index to a
    /// list on the GPU and counts it; the spawned system's initialize kernel then runs over as
    /// many threads as the step counted, each taking its attributes from the particle that
    /// spawned it. Nothing of this is counted on the CPU: the thread groups of that dispatch
    /// are arguments computed on the GPU from the list's count (<see cref="VFXIndirectArgs"/>).
    ///
    /// The list (a buffer of the spawned system's capacity and two words): the step's count,
    /// the count of all steps so far, then the indices. Each step of the spawning system
    /// starts its lists' step counts at zero, runs its kernels and computes the arguments;
    /// the spawned system, which comes after it, initializes from them in the same step.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/vfxevents).
    /// </summary>
    internal sealed partial class VFXParticleSystem
    {
        /// <summary>The names the graph compiler gives the list a system initializes from, the
        /// lists a task's kernel appends to (one per linked event, lettered) and the system
        /// whose kernels spawn into this one.</summary>
        const string EventListName = "eventList", EventListOutPrefix = "eventListOut_", ParentSystemName = "parentSystemIndex";
        /// <summary>The words before a list's indices: the step's count and the count so far.</summary>
        const int EventHeaderWords = 2;

        static readonly uint[] s_NoEvents = new uint[EventHeaderWords];

        /// <summary>The lists the system's kernels append events to, and the entry of each
        /// one's dispatch arguments.</summary>
        int[] m_EventOut = Array.Empty<int>(), m_EventOutEntries = Array.Empty<int>();
        /// <summary>The list the system initializes from, and the system that fills it.</summary>
        int m_EventList = -1, m_ParentIndex = -1;
        VFXParticleSystem m_Parent;

        void SetUpEvents(IReadOnlyList<VFXSystem> systems)
        {
            List<int> lists = null;
            foreach (var task in Desc.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                foreach (var buffer in task.buffers ?? Array.Empty<VFXMapping>())
                    if (buffer.index >= 0 && buffer.name.StartsWith(EventListOutPrefix, StringComparison.Ordinal) && (lists == null || !lists.Contains(buffer.index)))
                        (lists ??= new List<int>()).Add(buffer.index);
            if (lists != null)
            {
                m_EventOut = lists.ToArray();
                m_EventOutEntries = new int[m_EventOut.Length];
                for (int i = 0; i < m_EventOut.Length; i++) m_EventOutEntries[i] = VFXIndirectArgs.Shared.Reserve(VFXIndirectArgs.Kind.Dispatch);
            }
            if (!ReceivesGpuEvents) return;
            m_EventList = Value(Desc.buffers, EventListName);
            m_ParentIndex = Value(Desc.values, ParentSystemName);
            foreach (var system in systems)
                if (system.Index == m_ParentIndex) m_Parent = system as VFXParticleSystem;
        }

        /// <summary>Whether what the system has alive is counted on the GPU and read back from
        /// there: a system that kills particles, or one spawned into by GPU events (how many
        /// those spawned is known there alone).</summary>
        bool CountedOnGpu => HasKill || ReceivesGpuEvents;

        /// <summary>Whether the system whose kernels spawn into this one is awake: this one
        /// then initializes from what that one's step counted, and is awake itself.</summary>
        bool ParentAwake
        {
            get
            {
                if (!ReceivesGpuEvents) return false;
                m_Parent ??= Effect.SystemAt(m_ParentIndex) as VFXParticleSystem;
                return m_Parent != null && !m_Parent.m_Sleeping;
            }
        }

        /// <summary>The entry of the dispatch arguments of a list the system appends to, -1
        /// when it appends to no such list.</summary>
        int EventEntry(int list)
        {
            for (int i = 0; i < m_EventOut.Length; i++)
                if (m_EventOut[i] == list) return m_EventOutEntries[i];
            return -1;
        }

        /// <summary>The lists of an effect that starts: nothing counted in the step. The count
        /// of all steps so far starts at zero with the list and goes on through an effect's
        /// reinitialisation, and with it the numbers the spawned particles are given, which
        /// seed what they draw at random (tests/projects/vfxevents, `draw-reinit-f066`).</summary>
        void ResetEvents()
        {
            foreach (int list in m_EventOut)
            {
                var buffer = Effect.GpuBuffer(list, EventHeaderWords);
                buffer.SetData(s_NoEvents, 0, 0, m_EventsStarted ? 1 : EventHeaderWords);
            }
            m_EventsStarted = true;
        }

        bool m_EventsStarted;

        /// <summary>Before the system's kernels of a step: the lists' step counts at zero (the
        /// counts so far stay).</summary>
        void ResetEventCounts()
        {
            if (!m_HasRenderer) return;
            foreach (int list in m_EventOut) VFXIndirectArgs.Shared.ResetCount(Effect.GpuBuffer(list, 0));
        }

        /// <summary>After the system's kernels of a step: the dispatch arguments of each list
        /// from what the step counted.</summary>
        void FillEventArguments()
        {
            if (!m_HasRenderer) return;
            for (int i = 0; i < m_EventOut.Length; i++)
                VFXIndirectArgs.Shared.Fill(m_EventOutEntries[i], Effect.GpuBuffer(m_EventOut[i], 0), false);
        }

        /// <summary>The initialize kernel of a system spawned by GPU events, over what the
        /// spawning system's step counted: in the steps that system is awake.</summary>
        void InitializeFromEvents()
        {
            if (m_Initialize == null || !m_HasRenderer || !ParentAwake) return;
            int entry = m_Parent.EventEntry(m_EventList);
            var arguments = VFXIndirectArgs.Shared.Arguments;
            if (entry < 0 || arguments == null) return;
            // As for particles spawned from the CPU: the kernel takes particles from the dead
            // list as long as the count it had when the step began allows.
            if (HasKill && !HasStrips && m_DeadList >= 0) VFXManager.CopyDeadCount(Effect.GpuBuffer(m_DeadList, 0));
            m_Initialize.DispatchIndirect(arguments, VFXIndirectArgs.Offset(entry));
        }

        void ReleaseEvents()
        {
            foreach (int entry in m_EventOutEntries) VFXIndirectArgs.Shared.Release(entry);
            m_EventOutEntries = Array.Empty<int>();
            m_EventOut = Array.Empty<int>();
        }
    }
}
