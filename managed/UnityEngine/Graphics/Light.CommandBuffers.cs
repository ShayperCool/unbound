using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // A light's command buffers: the built-in renderer runs them around its shadows (the shadow
    // map, each of its passes, the screen-space mask), with the map or mask the current target.
    public sealed partial class Light
    {
        Dictionary<LightEvent, List<(CommandBuffer Buffer, ShadowMapPass Passes)>> m_CommandBuffers;
        int m_CommandBufferCount;

        public int commandBufferCount => m_CommandBufferCount;

        internal long ProfilerCommandBufferSlots
        {
            get
            {
                if (m_CommandBuffers == null) return 0;
                long slots = 0;
                foreach (var entry in m_CommandBuffers) slots += entry.Value.Capacity;
                return slots;
            }
        }

        public void AddCommandBuffer(LightEvent evt, CommandBuffer buffer) => AddCommandBuffer(evt, buffer, ShadowMapPass.All);

        public void AddCommandBuffer(LightEvent evt, CommandBuffer buffer, ShadowMapPass shadowPassMask)
        {
            if (buffer == null) throw new NullReferenceException("buffer is null");
            m_CommandBuffers ??= new Dictionary<LightEvent, List<(CommandBuffer, ShadowMapPass)>>();
            if (!m_CommandBuffers.TryGetValue(evt, out var buffers))
            {
                // An event starts with one slot and doubles on growth (componentmemory, Unity 6000.0.71f1).
                buffers = new List<(CommandBuffer, ShadowMapPass)>(1);
                m_CommandBuffers.Add(evt, buffers);
            }
            buffers.Add((buffer, shadowPassMask));
            m_CommandBufferCount++;
        }

        // Asynchronous compute queues run in order with the rest here.
        public void AddCommandBufferAsync(LightEvent evt, CommandBuffer buffer, ComputeQueueType queueType) => AddCommandBuffer(evt, buffer);

        public void AddCommandBufferAsync(LightEvent evt, CommandBuffer buffer, ShadowMapPass shadowPassMask, ComputeQueueType queueType) =>
            AddCommandBuffer(evt, buffer, shadowPassMask);

        public void RemoveCommandBuffer(LightEvent evt, CommandBuffer buffer)
        {
            if (m_CommandBuffers != null && m_CommandBuffers.TryGetValue(evt, out var buffers))
                m_CommandBufferCount -= buffers.RemoveAll(command => command.Buffer == buffer);
        }

        public void RemoveCommandBuffers(LightEvent evt)
        {
            if (m_CommandBuffers != null && m_CommandBuffers.Remove(evt, out var buffers))
                m_CommandBufferCount -= buffers.Count;
        }

        public void RemoveAllCommandBuffers()
        {
            m_CommandBuffers?.Clear();
            m_CommandBufferCount = 0;
        }

        public CommandBuffer[] GetCommandBuffers(LightEvent evt)
        {
            if (m_CommandBuffers == null || !m_CommandBuffers.TryGetValue(evt, out var buffers) || buffers.Count == 0)
                return Array.Empty<CommandBuffer>();
            var result = new CommandBuffer[buffers.Count];
            for (int i = 0; i < buffers.Count; i++)
                result[i] = buffers[i].Buffer;
            return result;
        }

        /// <summary>The buffers that run at an event for a shadow map pass (All for the events
        /// that are not a pass's).</summary>
        internal void CommandBuffersAt(LightEvent evt, ShadowMapPass pass, List<CommandBuffer> into)
        {
            into.Clear();
            if (m_CommandBuffers == null || !m_CommandBuffers.TryGetValue(evt, out var buffers)) return;
            foreach (var command in buffers)
                if (pass == ShadowMapPass.All || (command.Passes & pass) != 0) into.Add(command.Buffer);
        }
    }
}
