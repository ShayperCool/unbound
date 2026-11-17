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
        /// <summary>The most bytes a channel takes in a vertex (four 32-bit components).</summary>
        const int MaxChannelBytes = 16;

        static bool Readable(Mesh mesh) => mesh != null && mesh.isReadable;

        internal static uint VertexCount(Mesh mesh) => mesh != null ? (uint)mesh.vertexCount : 0u;

        internal static uint IndexCount(Mesh mesh) => mesh != null ? (uint)mesh.IndexBufferLength : 0u;

        internal static uint IndexFormat(Mesh mesh) => Readable(mesh) ? (uint)mesh.indexFormat : None;

        /// <summary>The byte offset of the layout's attribute <paramref name="at"/> in its
        /// stream's vertices, and that stream's stride in bytes.</summary>
        static (int Offset, int Stride) Place(ReadOnlySpan<VertexAttributeDescriptor> layout, int at)
        {
            int offset = 0, stride = 0, stream = layout[at].stream;
            for (int i = 0; i < layout.Length; i++)
            {
                if (layout[i].stream != stream) continue;
                int size = Mesh.FormatSize(layout[i].format) * layout[i].dimension;
                if (i < at) offset += size;
                stride += size;
            }
            return (offset, stride);
        }

        /// <summary>Finds the channel's descriptor, its byte offset in its stream's vertices and
        /// that stream's stride in bytes.</summary>
        static bool Channel(Mesh mesh, uint channel, out VertexAttributeDescriptor found, out int offset, out int stride)
        {
            found = default;
            offset = stride = 0;
            if (!Readable(mesh) || channel >= Mesh.MaxVertexAttributes) return false;
            Span<VertexAttributeDescriptor> layout = stackalloc VertexAttributeDescriptor[Mesh.MaxVertexAttributes];
            layout = layout.Slice(0, mesh.FillLayout(layout));
            for (int i = 0; i < layout.Length; i++)
            {
                if ((uint)layout[i].attribute != channel) continue;
                found = layout[i];
                (offset, stride) = Place(layout, i);
                return true;
            }
            return false;
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
            layout = layout.Slice(0, mesh.FillLayout(layout));
            Span<byte> packed = stackalloc byte[MaxChannelBytes];
            for (int i = 0; i < layout.Length; i++)
            {
                var d = layout[i];
                if (d.stream != stream) continue;
                // The channel of the stream the offset falls on, within a vertex the mesh has.
                var (start, stride) = Place(layout, i);
                ulong bytes = (ulong)offset * 4;
                if (bytes / (ulong)stride >= (ulong)mesh.vertexCount) return;
                if ((int)(bytes % (ulong)stride) != start || d.format != format || d.dimension != dimension) continue;
                int size = Mesh.FormatSize(d.format) * d.dimension;
                mesh.PackAttribute(d, (int)(bytes / (ulong)stride), packed.Slice(0, size));
                int component = Mesh.FormatSize(format);
                int read = Math.Min(dimension, result.Length);
                for (int c = 0; c < read; c++) result[c] = Mesh.Read(packed.Slice(c * component, component), format).Item1;
                return;
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

        /// <summary>What the programs read where a mesh's buffer cannot be bound: zeros (a
        /// shared buffer of four zero words, never released).</summary>
        internal static GraphicsBuffer NoBuffer => s_NoBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Structured, NoBufferWords, sizeof(uint));

        const int NoBufferWords = 4;
        static GraphicsBuffer s_NoBuffer;

        /// <summary>Whether the buffer is <see cref="NoBuffer"/> (without making it).</summary>
        internal static bool IsNoBuffer(GraphicsBuffer buffer) => buffer != null && ReferenceEquals(buffer, s_NoBuffer);

        /// <summary>The view of the stream's vertex buffer the programs read. A buffer without the
        /// device's target for effects cannot be bound: the programs read zeros of a mesh without
        /// read/write access (which does not gain it; tests/projects/vfx <c>draw-mesh locked</c>)
        /// and, not measured, of no mesh and of a stream the mesh has no buffer of.</summary>
        internal static GraphicsBuffer VertexBuffer(Mesh mesh, uint infos, GraphicsBuffer kept)
        {
            if (mesh == null) return NoBuffer;
            Held(mesh);
            if ((mesh.vertexBufferTarget & GraphicsDeviceProfile.Current.VfxMeshBufferTarget) == 0) return NoBuffer;
            return mesh.VertexBufferView((int)(infos >> 16 & 0xff), IsNoBuffer(kept) ? null : kept) ?? NoBuffer;
        }

        /// <summary>The view of the index buffer the programs read: a readable mesh's index
        /// buffer gains the device's target for effects' programs (one without it is read as
        /// zeros, as <see cref="VertexBuffer"/>).</summary>
        internal static GraphicsBuffer IndexBuffer(Mesh mesh, GraphicsBuffer kept)
        {
            if (mesh == null) return NoBuffer;
            if (mesh.isReadable) mesh.indexBufferTarget |= GraphicsDeviceProfile.Current.VfxMeshBufferTarget;
            if ((mesh.indexBufferTarget & GraphicsDeviceProfile.Current.VfxMeshBufferTarget) == 0) return NoBuffer;
            return mesh.IndexBufferView(IsNoBuffer(kept) ? null : kept) ?? NoBuffer;
        }
    }
}
