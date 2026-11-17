using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// What a visual effect's expressions read of a mesh on the CPU: its counts, the layout of a
    /// vertex channel packed into one word, its indices and its vertices' channels decoded to
    /// floats, and the views of its buffers its programs read (Unity 6000.0.71f1,
    /// tests/projects/vfx <c>mesh operations/*</c>). Offsets and strides are in 32-bit words; a
    /// mesh whose data was given up (not readable) answers no layout and no vertices.
    /// </summary>
    internal static class VFXMeshFunctions
    {
        /// <summary>The answer for a channel the mesh does not have (or no mesh).</summary>
        internal const uint None = uint.MaxValue;

        const int Streams = 4;

        static bool Readable(Mesh mesh) => mesh != null && mesh.isReadable;

        internal static uint VertexCount(Mesh mesh) => mesh != null ? (uint)mesh.vertexCount : 0u;

        internal static uint IndexCount(Mesh mesh) => mesh != null ? (uint)mesh.IndexBufferLength : 0u;

        internal static uint IndexFormat(Mesh mesh) => Readable(mesh) ? (uint)mesh.indexFormat : None;

        /// <summary>Finds the channel's descriptor, its byte offset in its stream's vertices and
        /// that stream's stride in bytes.</summary>
        static bool Channel(Mesh mesh, uint channel, out VertexAttributeDescriptor found, out int offset, out int stride)
        {
            found = default;
            offset = stride = 0;
            if (!Readable(mesh) || channel >= Mesh.MaxVertexAttributes) return false;
            Span<VertexAttributeDescriptor> layout = stackalloc VertexAttributeDescriptor[Mesh.MaxVertexAttributes];
            int count = mesh.FillLayout(layout);
            int at = -1;
            for (int i = 0; i < count; i++)
                if ((uint)layout[i].attribute == channel) at = i;
            if (at < 0) return false;
            found = layout[at];
            for (int i = 0; i < count; i++)
            {
                if (layout[i].stream != found.stream) continue;
                int size = Mesh.FormatSize(layout[i].format) * layout[i].dimension;
                if (i < at) offset += size;
                stride += size;
            }
            return true;
        }

        internal static uint VertexStride(Mesh mesh, uint channel) =>
            Channel(mesh, channel, out _, out _, out int stride) ? (uint)stride >> 2 : None;

        internal static uint ChannelOffset(Mesh mesh, uint channel) =>
            Channel(mesh, channel, out _, out int offset, out _) ? (uint)offset >> 2 : None;

        /// <summary>The channel's stream, dimension and format in one word: the stream in bits
        /// 16..23, the dimension in bits 8..11 and the format in bits 0..7.</summary>
        internal static uint ChannelInfos(Mesh mesh, uint channel) =>
            Channel(mesh, channel, out var found, out _, out _)
                ? (uint)found.stream << 16 | ((uint)found.dimension & 0xf) << 8 | (uint)found.format
                : None;

        /// <summary>The index at <paramref name="index"/> of the index buffer read as indices of the
        /// format given (0: 16-bit, 1: 32-bit, as the buffer's bytes hold them); <see cref="None"/>
        /// for another format, no mesh or no indices; zero past the buffer's end.</summary>
        internal static uint Index(Mesh mesh, uint index, uint format)
        {
            int length = mesh != null ? mesh.IndexBufferLength : 0;
            if (length == 0 || format > 1) return None;
            bool wide = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32;
            if (format == 1)
            {
                if (wide) return index < (uint)length ? (uint)mesh.IndexBufferAt((int)index) : 0u;
                // Two 16-bit indices make one 32-bit word.
                ulong low = (ulong)index * 2;
                if (low + 1 >= (ulong)length) return 0u;
                return (uint)(ushort)mesh.IndexBufferAt((int)low) | (uint)(ushort)mesh.IndexBufferAt((int)low + 1) << 16;
            }
            if (!wide) return index < (uint)length ? (uint)(ushort)mesh.IndexBufferAt((int)index) : 0u;
            // A 32-bit index holds two 16-bit halves, the low one first.
            if (index / 2 >= (uint)length) return 0u;
            uint word = (uint)mesh.IndexBufferAt((int)(index / 2));
            return (index & 1) == 0 ? word & 0xffff : word >> 16;
        }

        /// <summary>Decodes the channel the vertex offset (in words, its stream's) points at into
        /// <paramref name="result"/>: floats copied up to the result's size, other formats
        /// converted component by component; components the channel does not have, and every one
        /// for no mesh, no channel or an offset outside the mesh's vertices, stay zero.</summary>
        internal static void Sample(Mesh mesh, uint offset, uint infos, Span<float> result)
        {
            result.Clear();
            if (!Readable(mesh) || infos == None) return;
            int stream = (int)(infos >> 16 & 0xff), dimension = (int)(infos >> 8 & 0xff);
            var format = (VertexAttributeFormat)(infos & 0xff);
            if (stream >= Streams || dimension == 0) return;
            Span<VertexAttributeDescriptor> layout = stackalloc VertexAttributeDescriptor[Mesh.MaxVertexAttributes];
            int count = mesh.FillLayout(layout);
            int stride = 0;
            for (int i = 0; i < count; i++)
                if (layout[i].stream == stream) stride += Mesh.FormatSize(layout[i].format) * layout[i].dimension;
            if (stride == 0) return;
            ulong bytes = (ulong)offset * 4;
            ulong vertex = bytes / (ulong)stride;
            int within = (int)(bytes % (ulong)stride);
            if (vertex >= (ulong)mesh.vertexCount) return;
            int start = 0;
            for (int i = 0; i < count; i++)
            {
                var d = layout[i];
                if (d.stream != stream) continue;
                int size = Mesh.FormatSize(d.format) * d.dimension;
                if (start == within && d.format == format && d.dimension == dimension)
                {
                    Span<byte> packed = stackalloc byte[16];
                    mesh.PackAttribute(d, (int)vertex, packed.Slice(0, size));
                    int component = Mesh.FormatSize(format);
                    int read = Math.Min(dimension, result.Length);
                    for (int c = 0; c < read; c++) result[c] = Mesh.Read(packed.Slice(c * component, component), format).Item1;
                    return;
                }
                start += size;
            }
        }

        /// <summary>A sampled colour: the channel's four components, red, green and blue taken
        /// from sRGB to linear.</summary>
        internal static void SampleColor(Mesh mesh, uint offset, uint infos, Span<float> result)
        {
            Sample(mesh, offset, infos, result);
            for (int c = 0; c < 3; c++) result[c] = Mathf.GammaToLinearSpace(result[c]);
        }

        /// <summary>A mesh becomes an effect's value: a readable one's vertex buffer gains the
        /// device's target for effects' programs (<see cref="GraphicsDeviceProfile.VfxMeshBufferTarget"/>).</summary>
        internal static void Held(Mesh mesh)
        {
            if (mesh != null && mesh.isReadable) mesh.vertexBufferTarget |= GraphicsDeviceProfile.Current.VfxMeshBufferTarget;
        }

        /// <summary>The view of the stream's vertex buffer the programs read.</summary>
        internal static GraphicsBuffer VertexBuffer(Mesh mesh, uint infos, GraphicsBuffer kept)
        {
            if (mesh == null) return null;
            Held(mesh);
            return mesh.VertexBufferView((int)(infos >> 16 & 0xff), kept);
        }

        /// <summary>The view of the index buffer the programs read: a readable mesh's index
        /// buffer gains the device's target for effects' programs.</summary>
        internal static GraphicsBuffer IndexBuffer(Mesh mesh, GraphicsBuffer kept)
        {
            if (mesh == null) return null;
            if (mesh.isReadable) mesh.indexBufferTarget |= GraphicsDeviceProfile.Current.VfxMeshBufferTarget;
            return mesh.IndexBufferView(kept);
        }
    }
}
