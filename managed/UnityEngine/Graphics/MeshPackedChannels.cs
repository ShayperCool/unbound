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

        void ClearPackedChannels(bool keepLayout)
        {
            if (!keepLayout) { m_PackedChannels = null; return; }
            if (m_PackedChannels == null) return;
            // Retain format metadata without retaining data or freezing the default layout.
            foreach (var channel in m_PackedChannels)
                if (channel != null)
                {
                    channel.Count = 0;
                    channel.Bytes = Array.Empty<byte>();
                }
        }

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

        internal uint OverrideVertexChannels(Mesh source)
        {
            uint mask = 0;
            int count = vertexCount;
            void Keep(VertexAttribute attribute) { mask |= 1u << (int)attribute; CopyPackedChannelFrom(source,attribute); }
            if (source.HasVertexAttribute(VertexAttribute.Position) && source.vertexCount == count) { m_Vertices = source.m_Vertices; Keep(VertexAttribute.Position); }
            if (source.m_Normals != null && source.m_Normals.Length == count) { m_Normals = source.m_Normals; Keep(VertexAttribute.Normal); }
            if (source.m_Tangents != null && source.m_Tangents.Length == count) { m_Tangents = source.m_Tangents; Keep(VertexAttribute.Tangent); }
            if (source.m_Colors != null && source.m_Colors.Length == count) { m_Colors = source.m_Colors; Keep(VertexAttribute.Color); }
            for (int channel=0;channel<8;channel++)
                if (source.m_UVs[channel] != null && source.m_UVs[channel].Length == count)
                {
                    m_UVs[channel] = source.m_UVs[channel]; m_UVDimensions[channel] = source.m_UVDimensions[channel];
                    Keep(VertexAttribute.TexCoord0 + channel);
                }
            // Missing primary UV inputs inherit the nearest lower channel of the merged layout.
            for (int channel=0;channel<4;channel++)
                for (int lower=channel;lower>=0;lower--)
                    if (m_UVs[lower] != null)
                    {
                        if ((mask & (1u << ((int)VertexAttribute.TexCoord0 + lower))) != 0) mask |= 1u << ((int)VertexAttribute.TexCoord0 + channel);
                        break;
                    }
            return mask;
        }

        /// <summary>One UV channel of <paramref name="source"/> over this mesh's (same vertex
        /// count); its attribute bit, or 0 when the source has no such channel.</summary>
        internal uint OverrideUVChannel(Mesh source, int channel)
        {
            if (source.m_UVs[channel] == null || source.m_UVs[channel].Length != vertexCount) return 0;
            m_UVs[channel] = source.m_UVs[channel]; m_UVDimensions[channel] = source.m_UVDimensions[channel];
            CopyPackedChannelFrom(source, VertexAttribute.TexCoord0 + channel);
            return 1u << (int)(VertexAttribute.TexCoord0 + channel);
        }

        internal void CopyPackedChannelFrom(Mesh source, VertexAttribute attribute)
        {
            var channel = source.m_PackedChannels?[(int)attribute];
            if (channel == null) { InvalidatePackedChannel(attribute); return; }
            m_PackedChannels ??= new PackedChannel[MaxVertexAttributes];
            m_PackedChannels[(int)attribute] = new PackedChannel
            {
                Descriptor = channel.Descriptor, Count = channel.Count, Bytes = (byte[])channel.Bytes.Clone(),
            };
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
        // What m_RenderColors were converted from: a copy of the colors and of the raw channel.
        // Colors are also written in place (mesh buffers, meshes that reuse their arrays), so the
        // conversion is redone when these bytes differ, not when an array is replaced.
        Color32[] m_RenderColorsFrom;
        byte[] m_RenderColorsRawFrom;
        VertexAttributeDescriptor m_RenderColorsRawDescriptor;
        int m_RenderColorsRawCount = -1;

        // Keep floating-point and HDR vertex colors intact when uploading the draw layout.
        internal Vector4[] RenderColors()
        {
            if (m_Colors == null) return null;
            var raw = m_PackedChannels?[(int)VertexAttribute.Color];
            if (m_RenderColors != null && RenderColorsCurrent(raw)) return m_RenderColors;
            if (m_RenderColors == null || m_RenderColors.Length != m_Colors.Length) m_RenderColors = new Vector4[m_Colors.Length];
            KeepRenderColorsSource(raw);
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

        bool RenderColorsCurrent(PackedChannel raw)
        {
            if (m_RenderColorsFrom == null || !System.Runtime.InteropServices.MemoryMarshal.AsBytes(m_Colors.AsSpan())
                    .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(m_RenderColorsFrom.AsSpan())))
                return false;
            if (raw == null) return m_RenderColorsRawCount < 0;
            return m_RenderColorsRawCount == raw.Count && m_RenderColorsRawDescriptor.Equals(raw.Descriptor)
                && raw.Bytes.AsSpan().SequenceEqual(m_RenderColorsRawFrom);
        }

        void KeepRenderColorsSource(PackedChannel raw)
        {
            Copy(ref m_RenderColorsFrom, m_Colors);
            if (raw == null) { m_RenderColorsRawCount = -1; return; }
            Copy(ref m_RenderColorsRawFrom, raw.Bytes);
            m_RenderColorsRawDescriptor = raw.Descriptor;
            m_RenderColorsRawCount = raw.Count;

            static void Copy<T>(ref T[] to, T[] from)
            {
                if (to == null || to.Length != from.Length) to = new T[from.Length];
                from.AsSpan().CopyTo(to);
            }
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
