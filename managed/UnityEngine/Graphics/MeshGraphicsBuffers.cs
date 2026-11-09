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
        byte[] m_GraphicsIndexBytes;
        bool m_GraphicsBuffersDirty = true;
        bool m_GraphicsVerticesDirty = true;
        int m_GraphicsVertexCount = -1;
        bool m_GraphicsDirtyIndices = true;
        bool m_GraphicsBindingDirty;
        int m_GraphicsBindingVersion;
        struct GraphicsSource
        {
            internal Mesh Mesh;
            internal uint VertexMask;
            internal bool Indices, Active;
            internal int Version;
        }
        GraphicsSource[] m_GraphicsSources;

        public GraphicsBuffer.Target vertexBufferTarget
        {
            get => m_VertexBufferTarget;
            set
            {
                var target = value | GraphicsBuffer.Target.Vertex;
                if (m_VertexBufferTarget == target) return;
                m_VertexBufferTarget = target;
                m_GraphicsVerticesDirty = true;
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
                m_GraphicsDirtyIndices = true;
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
            bool sameLayout = m_GraphicsVertexCount == data.VertexCount && m_GraphicsLayout != null && m_GraphicsLayout.AsSpan().SequenceEqual(data.Layout);
            for (int stream = 0; stream < 4; stream++)
            {
                if (!sameLayout)
                {
                    m_GraphicsVertexBuffers[stream]?.InvalidateMeshBuffer();
                    m_GraphicsVertexBuffers[stream] = null;
                }
                if (sameLayout && !m_GraphicsVerticesDirty) continue;
                Upload(ref m_GraphicsVertexBuffers[stream], data.VertexCount, data.Stride(stream),
                    m_VertexBufferTarget, new ReadOnlySpan<byte>(data.Streams[stream], data.StreamBytes[stream]));
            }
            int stride = indexFormat == IndexFormat.UInt32 ? 4 : 2;
            int count = data.IndexCount;
            foreach (var submesh in data.SubMeshes)
                if (submesh.topology == MeshTopology.Quads) count += submesh.indexCount / 4 * 6;
            if (count != data.IndexCount && !Compat.RenderingMeasured)
                throw new NotSupportedException("Unbound: quad mesh GPU buffers have only been measured for Unity 6000.0.71f1.");
            // Raw/Structured 16-bit buffers include a final alignment word when needed.
            if (Compat.RenderingMeasured && (m_IndexBufferTarget & (GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Structured)) != 0)
                count = (count * stride + 3) / 4 * 4 / stride;
            if (m_GraphicsDirtyIndices || m_GraphicsIndexBuffer == null || m_GraphicsIndexBuffer.Count != count || m_GraphicsIndexBuffer.Stride != stride)
            {
                int bytes = count * stride;
                if (m_GraphicsIndexBytes == null || m_GraphicsIndexBytes.Length != bytes) m_GraphicsIndexBytes = new byte[bytes];
                var original = new ReadOnlySpan<byte>(data.Indices, data.IndexCount * stride);
                original.CopyTo(m_GraphicsIndexBytes);
                m_GraphicsIndexBytes.AsSpan(original.Length).Clear();
                int destination = data.IndexCount;
                foreach (var submesh in data.SubMeshes)
                {
                    if (submesh.topology != MeshTopology.Quads) continue;
                    for (int q=0; q+3<submesh.indexCount; q+=4)
                        for (int triangleIndex=0; triangleIndex<6; triangleIndex++)
                        {
                            int corner = triangleIndex switch {0 or 3 => 0, 1 => 1, 2 or 4 => 2, _ => 3};
                            original.Slice((submesh.indexStart+q+corner)*stride,stride).CopyTo(m_GraphicsIndexBytes.AsSpan(destination++*stride,stride));
                        }
                }
                Upload(ref m_GraphicsIndexBuffer,count,stride,m_IndexBufferTarget,m_GraphicsIndexBytes);
            }
            m_GraphicsLayout = data.Layout;
            m_GraphicsVertexCount = data.VertexCount;
            m_GraphicsBuffersDirty = false;
            m_GraphicsVerticesDirty = false; m_GraphicsDirtyIndices = false; m_GraphicsBindingDirty = true;
            m_GraphicsBindingVersion++;
        }

        internal void SetSkinOutputLayout()
        {
            // Skinning produces positions, normals and tangents in one output stream.
            var attributes = Layout();
            for (int i = 0; i < attributes.Length; i++)
            {
                var d = attributes[i];
                d.stream = d.attribute <= VertexAttribute.Tangent ? 0 : 1;
                if (d.attribute <= VertexAttribute.Tangent) d.format = VertexAttributeFormat.Float32;
                attributes[i] = d;
            }
            m_Layout = attributes;
        }

        void SetGraphicsSource(int slot, Mesh source, uint vertexMask, bool indices)
        {
            m_GraphicsSources ??= new GraphicsSource[3];
            ref var link = ref m_GraphicsSources[slot-1];
            if (ReferenceEquals(link.Mesh,source) && link.VertexMask == vertexMask && link.Indices == indices) return;
            link.Mesh = source; link.VertexMask = vertexMask; link.Indices = indices; link.Version = -1;
        }

        internal void ShareDrawIndices(Mesh source)
        {
            m_IndexFormat = source.m_IndexFormat;
            m_IndexBuffer = source.m_IndexBuffer;
            m_SubMeshes.Clear(); m_SubMeshes.AddRange(source.m_SubMeshes);
            SetGraphicsSource(1,source,0,true);
        }

        /// <summary><paramref name="ownAttributes"/>: attributes neither source gives (this
        /// mesh's own CPU channels).</summary>
        internal void ShareVertexSources(Mesh source, Mesh overrides, uint overriddenAttributes, uint ownAttributes = 0)
        {
            SetGraphicsSource(2,source,~(overriddenAttributes|ownAttributes),false);
            SetGraphicsSource(3,overrides,overriddenAttributes,false);
        }

        // Drop a previous source before a CPU upload decides whether its GPU indices can stay.
        void ClearGraphicsSources()
        {
            if (m_GraphicsSources == null) return;
            for (int i=0;i<m_GraphicsSources.Length;i++)
            {
                ref var link = ref m_GraphicsSources[i];
                if (!link.Active || (link.Mesh != null && link.Mesh.m_GraphicsVertexBuffers != null)) continue;
                GfxStream.MeshBuffers(m_Handle,null,null,Array.Empty<VertexAttributeDescriptor>(),null,(byte)(i+1),0);
                link.Active = false;
            }
        }

        void SyncGraphicsBinding()
        {
            if (m_GraphicsBindingDirty)
            {
                GfxStream.MeshBuffers(m_Handle, m_GraphicsVertexBuffers, m_GraphicsIndexBuffer, m_GraphicsLayout, m_GraphicsData);
                m_GraphicsBindingDirty = false;
            }
            if (m_GraphicsSources == null) return;
            for (int i=0;i<m_GraphicsSources.Length;i++)
            {
                ref var link = ref m_GraphicsSources[i];
                var source = link.Mesh;
                if (source == null || source.m_GraphicsVertexBuffers == null) continue;
                source.SyncGraphicsBuffers();
                if (link.Active && link.Version == source.m_GraphicsBindingVersion) continue;
                GfxStream.MeshBuffers(m_Handle,source.m_GraphicsVertexBuffers,link.Indices ? source.m_GraphicsIndexBuffer : null,
                    source.m_GraphicsLayout,source.m_GraphicsData,(byte)(i+1),link.VertexMask);
                link.Version = source.m_GraphicsBindingVersion; link.Active = true;
            }
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
