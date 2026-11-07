using System;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        // Keep exact channel bytes independently of stream placement. The renderer's decoded
        // channels cannot represent every vertex format (for example HDR colors or UInt32 UVs).
        sealed class PackedChannel
        {
            internal VertexAttributeDescriptor Descriptor;
            internal byte[] Bytes;
            internal int Count;
        }

        PackedChannel[] m_PackedChannels;

        void InvalidatePackedChannel(VertexAttribute attribute)
        {
            if (m_PackedChannels != null) m_PackedChannels[(int)attribute] = null;
        }

        void ResizePackedChannels(int count, VertexAttributeDescriptor[] layout = null)
        {
            if (m_PackedChannels == null) return;
            for (int i = 0; i < m_PackedChannels.Length; i++)
            {
                var channel = m_PackedChannels[i];
                if (channel == null) continue;
                if (layout != null)
                {
                    bool present = false;
                    foreach (var d in layout)
                        if ((int)d.attribute == i && d.format == channel.Descriptor.format && d.dimension == channel.Descriptor.dimension)
                            present = true;
                    if (!present) { m_PackedChannels[i] = null; continue; }
                }
                channel.Count = Math.Min(channel.Count, count);
            }
        }

        void CopyPackedChannels(Mesh source)
        {
            m_PackedChannels = null;
            if (source.m_PackedChannels == null) return;
            m_PackedChannels = new PackedChannel[MaxVertexAttributes];
            for (int i = 0; i < m_PackedChannels.Length; i++)
            {
                var channel = source.m_PackedChannels[i];
                if (channel != null) m_PackedChannels[i] = new PackedChannel
                {
                    Descriptor = channel.Descriptor, Count = channel.Count, Bytes = (byte[])channel.Bytes.Clone(),
                };
            }
        }

        internal unsafe void PackAttribute(VertexAttributeDescriptor d, int vertex, Span<byte> destination)
        {
            int size = FormatSize(d.format), width = size * d.dimension;
            var raw = m_PackedChannels?[(int)d.attribute];
            if (raw != null && raw.Descriptor.format == d.format && raw.Descriptor.dimension == d.dimension && vertex < raw.Count)
            {
                raw.Bytes.AsSpan(vertex * width, width).CopyTo(destination);
                return;
            }
            ReadChannel(d.attribute, vertex, out var f, out var n);
            fixed (byte* at = destination)
                for (int c = 0; c < d.dimension; c++)
                    Write(at + c * size, d.format, f[c], c switch {0 => n.Item1, 1 => n.Item2, 2 => n.Item3, _ => n.Item4});
        }

        PackedChannel WritablePackedChannel(VertexAttributeDescriptor d)
        {
            m_PackedChannels ??= new PackedChannel[MaxVertexAttributes];
            var raw = m_PackedChannels[(int)d.attribute];
            if (raw != null && raw.Count == vertexCount && raw.Descriptor.format == d.format && raw.Descriptor.dimension == d.dimension)
                return raw;
            int width = FormatSize(d.format) * d.dimension;
            var bytes = new byte[vertexCount * width];
            for (int vertex = 0; vertex < vertexCount; vertex++)
                PackAttribute(d, vertex, bytes.AsSpan(vertex * width, width));
            return m_PackedChannels[(int)d.attribute] = new PackedChannel {Descriptor = d, Count = vertexCount, Bytes = bytes};
        }
    }
}
