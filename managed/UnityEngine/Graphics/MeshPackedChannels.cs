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

        void SetColorFormat(VertexAttributeFormat format)
        {
            if (m_Layout == null) return;
            for (int i=0;i<m_Layout.Length;i++)
                if (m_Layout[i].attribute == VertexAttribute.Color)
                    m_Layout[i] = new VertexAttributeDescriptor(VertexAttribute.Color,format,4,m_Layout[i].stream);
        }

        void KeepFloatColors(Color[] colors)
        {
            if (m_Colors == null) return;
            m_PackedChannels ??= new PackedChannel[MaxVertexAttributes];
            m_PackedChannels[(int)VertexAttribute.Color] = new PackedChannel
            {
                Descriptor = new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4),
                Count = colors.Length,
                Bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(colors.AsSpan()).ToArray(),
            };
            SetColorFormat(VertexAttributeFormat.Float32);
        }

        Vector4[] m_RenderColors;

        // Keep floating-point and HDR vertex colors intact when uploading the draw layout.
        internal Vector4[] RenderColors()
        {
            if (m_Colors == null) return null;
            if (m_RenderColors == null || m_RenderColors.Length != m_Colors.Length) m_RenderColors = new Vector4[m_Colors.Length];
            var raw = m_PackedChannels?[(int)VertexAttribute.Color];
            for (int vertex = 0; vertex < m_RenderColors.Length; vertex++)
            {
                Vector4 color = (Color)m_Colors[vertex];
                if (raw != null && vertex < raw.Count)
                {
                    int size = FormatSize(raw.Descriptor.format), width = size * raw.Descriptor.dimension;
                    color = new Vector4(0,0,0,1);
                    for (int c = 0; c < raw.Descriptor.dimension; c++)
                        color[c] = Read(raw.Bytes.AsSpan(vertex * width + c * size, size), raw.Descriptor.format).Item1;
                }
                m_RenderColors[vertex] = color;
            }
            return m_RenderColors;
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
