using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        GraphicsBuffer.Target m_VertexBufferTarget = GraphicsBuffer.Target.Vertex;
        GraphicsBuffer.Target m_IndexBufferTarget = GraphicsBuffer.Target.Index;
        GpuBufferStorage[] m_GraphicsVertexBuffers;
        GpuBufferStorage m_GraphicsIndexBuffer;
        VertexAttributeDescriptor[] m_GraphicsLayout;
        MeshDataStore m_GraphicsData;
        bool m_GraphicsBuffersDirty = true;

        public GraphicsBuffer.Target vertexBufferTarget
        {
            get => m_VertexBufferTarget;
            set
            {
                var target = value | GraphicsBuffer.Target.Vertex;
                if (m_VertexBufferTarget == target) return;
                m_VertexBufferTarget = target;
                m_GraphicsBuffersDirty = true;
            }
        }

        public GraphicsBuffer.Target indexBufferTarget
        {
            get => m_IndexBufferTarget;
            set
            {
                var target = value | GraphicsBuffer.Target.Index;
                if (m_IndexBufferTarget == target) return;
                m_IndexBufferTarget = target;
                m_GraphicsBuffersDirty = true;
            }
        }

        public GraphicsBuffer GetVertexBuffer(int index)
        {
            ThrowIfDestroyed();
            if ((uint)index >= 4)
                throw new ArgumentException($"Invalid buffer index {index} for Mesh '{name}'");
            SyncGraphicsBuffers(true);
            var storage = m_GraphicsVertexBuffers[index];
            if (storage == null)
                throw new ArgumentException($"Mesh '{name}' vertex buffer #{index} is null");
            return new GraphicsBuffer(storage);
        }

        public GraphicsBuffer GetIndexBuffer()
        {
            ThrowIfDestroyed();
            SyncGraphicsBuffers(true);
            if (m_GraphicsIndexBuffer == null)
                throw new ArgumentException($"Mesh '{name}' index buffer is null");
            return new GraphicsBuffer(m_GraphicsIndexBuffer);
        }

        /// <summary>Mesh-owned buffers share the mesh-data packer. Existing storage survives a
        /// same-layout, same-size upload; a changed layout, size or target invalidates its views.
        /// Only meshes whose buffers were requested pay for this upload.</summary>
        unsafe void SyncGraphicsBuffers(bool create = false)
        {
            if (!create && m_GraphicsVertexBuffers == null) return;
            if (!m_GraphicsBuffersDirty) return;
            var data = m_GraphicsData ??= new MeshDataStore(true);
            data.Update(this);
            m_GraphicsVertexBuffers ??= new GpuBufferStorage[4];
            bool sameLayout = m_GraphicsLayout != null && m_GraphicsLayout.AsSpan().SequenceEqual(data.Layout);
            for (int stream = 0; stream < 4; stream++)
            {
                if (!sameLayout)
                {
                    m_GraphicsVertexBuffers[stream]?.InvalidateMeshBuffer();
                    m_GraphicsVertexBuffers[stream] = null;
                }
                Upload(ref m_GraphicsVertexBuffers[stream], data.VertexCount, data.Stride(stream),
                    m_VertexBufferTarget, new ReadOnlySpan<byte>(data.Streams[stream], data.StreamBytes[stream]));
            }
            int stride = indexFormat == IndexFormat.UInt32 ? 4 : 2;
            int count = data.IndexCount;
            // Raw and Structured access require whole 32-bit words on the measured GLCore
            // backend. The public count includes the padding (renderapi mesh.flags*).
            if (Compat.RenderingMeasured && (m_IndexBufferTarget & (GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Structured)) != 0)
                count = (count * stride + 3) / 4 * 4 / stride;
            Upload(ref m_GraphicsIndexBuffer, count, stride, m_IndexBufferTarget,
                new ReadOnlySpan<byte>(data.Indices, data.IndexCount * stride));
            m_GraphicsLayout = data.Layout;
            m_GraphicsBuffersDirty = false;
        }

        static void Upload(ref GpuBufferStorage storage, int count, int stride, GraphicsBuffer.Target target, ReadOnlySpan<byte> bytes)
        {
            if (storage != null && (storage.Count != count || storage.Stride != stride || storage.Usage != (uint)target))
            {
                storage.InvalidateMeshBuffer();
                storage = null;
            }
            if (count == 0 || stride == 0) return;
            storage ??= new GpuBufferStorage(count, stride, (uint)target, meshOwned: true);
            storage.Write(0, bytes);
        }

        void ReleaseGraphicsBufferOwner()
        {
            m_GraphicsData?.Free();
            m_GraphicsData = null;
            if (m_GraphicsVertexBuffers != null)
                foreach (var buffer in m_GraphicsVertexBuffers) buffer?.ReleaseMeshOwner();
            m_GraphicsIndexBuffer?.ReleaseMeshOwner();
        }
    }
}
