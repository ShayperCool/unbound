using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // A light's command buffers: the built-in renderer runs them around its shadows (the shadow
    // map, each of its passes, the screen-space mask), with the map or mask the current target.
    public sealed partial class Light
    {
        readonly List<(LightEvent Event, CommandBuffer Buffer, ShadowMapPass Passes)> m_CommandBuffers =
            new List<(LightEvent, CommandBuffer, ShadowMapPass)>();

        public int commandBufferCount => m_CommandBuffers.Count;

        // Models the native event reservations independently of the reused execution list.
        Dictionary<LightEvent, (int Count, long Slots)> m_ProfilerCommandBuffers;

        internal long ProfilerCommandBufferSlots
        {
            get
            {
                if (m_ProfilerCommandBuffers == null) return 0;
                long slots = 0;
                foreach (var entry in m_ProfilerCommandBuffers) slots += entry.Value.Slots;
                return slots;
            }
        }

        public void AddCommandBuffer(LightEvent evt, CommandBuffer buffer) => AddCommandBuffer(evt, buffer, ShadowMapPass.All);

        public void AddCommandBuffer(LightEvent evt, CommandBuffer buffer, ShadowMapPass shadowPassMask)
        {
            if (buffer == null) throw new NullReferenceException("buffer is null");
            m_CommandBuffers.Add((evt, buffer, shadowPassMask));
            if (Compat.LightCommandBufferMemoryMeasured)
            {
                m_ProfilerCommandBuffers ??= new Dictionary<LightEvent, (int, long)>();
                m_ProfilerCommandBuffers.TryGetValue(evt, out var allocation);
                allocation.Count++;
                if (allocation.Count > allocation.Slots)
                    allocation.Slots = Math.Max(1, allocation.Slots * 2);
                m_ProfilerCommandBuffers[evt] = allocation;
            }
        }

        // Asynchronous compute queues run in order with the rest here.
        public void AddCommandBufferAsync(LightEvent evt, CommandBuffer buffer, ComputeQueueType queueType) => AddCommandBuffer(evt, buffer);

        public void AddCommandBufferAsync(LightEvent evt, CommandBuffer buffer, ShadowMapPass shadowPassMask, ComputeQueueType queueType) =>
            AddCommandBuffer(evt, buffer, shadowPassMask);

        public void RemoveCommandBuffer(LightEvent evt, CommandBuffer buffer)
        {
            int removed = m_CommandBuffers.RemoveAll(c => c.Event == evt && c.Buffer == buffer);
            if (removed > 0 && m_ProfilerCommandBuffers != null
                && m_ProfilerCommandBuffers.TryGetValue(evt, out var allocation))
            {
                allocation.Count -= removed;
                m_ProfilerCommandBuffers[evt] = allocation;
            }
        }

        public void RemoveCommandBuffers(LightEvent evt)
        {
            m_CommandBuffers.RemoveAll(c => c.Event == evt);
            m_ProfilerCommandBuffers?.Remove(evt);
        }

        public void RemoveAllCommandBuffers()
        {
            m_CommandBuffers.Clear();
            m_ProfilerCommandBuffers?.Clear();
        }

        public CommandBuffer[] GetCommandBuffers(LightEvent evt)
        {
            var list = new List<CommandBuffer>();
            foreach (var c in m_CommandBuffers)
                if (c.Event == evt) list.Add(c.Buffer);
            return list.ToArray();
        }

        /// <summary>The buffers that run at an event for a shadow map pass (All for the events
        /// that are not a pass's).</summary>
        internal void CommandBuffersAt(LightEvent evt, ShadowMapPass pass, List<CommandBuffer> into)
        {
            into.Clear();
            foreach (var c in m_CommandBuffers)
                if (c.Event == evt && (c.Passes & pass) != 0) into.Add(c.Buffer);
        }
    }
}
