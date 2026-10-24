using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering
{
    /// <summary>One attribute of a mesh's vertex layout: what it is, its format and dimension, and
    /// the vertex stream it lies in.</summary>
    public partial struct VertexAttributeDescriptor : IEquatable<VertexAttributeDescriptor>
    {
        public VertexAttributeDescriptor(VertexAttribute attribute = VertexAttribute.Position, VertexAttributeFormat format = VertexAttributeFormat.Float32, int dimension = 3, int stream = 0)
        {
            this.attribute = attribute;
            this.format = format;
            this.dimension = dimension;
            this.stream = stream;
        }

        public VertexAttribute attribute { get; set; }
        public VertexAttributeFormat format { get; set; }
        public int dimension { get; set; }
        public int stream { get; set; }

        public override string ToString() => $"(attr={attribute} fmt={format} dim={dimension} stream={stream})";
        public override int GetHashCode() => ((int)attribute * 397 ^ (int)format) * 397 ^ dimension * 31 ^ stream;
        public override bool Equals(object other) => other is VertexAttributeDescriptor d && Equals(d);
        public bool Equals(VertexAttributeDescriptor other) =>
            attribute == other.attribute && format == other.format && dimension == other.dimension && stream == other.stream;
        public static bool operator ==(VertexAttributeDescriptor lhs, VertexAttributeDescriptor rhs) => lhs.Equals(rhs);
        public static bool operator !=(VertexAttributeDescriptor lhs, VertexAttributeDescriptor rhs) => !lhs.Equals(rhs);
    }
}

namespace UnityEngine
{
    /// <summary>
    /// The mesh's vertex and index buffers as Unity describes them: a layout of attributes in
    /// streams (attributes in VertexAttribute order, packed), raw vertex data written into it
    /// (decoded into the mesh's channels), one index buffer and submeshes as ranges of it.
    /// </summary>
    public sealed partial class Mesh
    {
        /// <summary>The layout SetVertexBufferParams gave; null: the channels' default layout.</summary>
        VertexAttributeDescriptor[] m_Layout;
        /// <summary>The index buffer SetIndexBufferParams made; null: the submeshes' indices in turn.</summary>
        int[] m_IndexBuffer;

        internal static int FormatSize(VertexAttributeFormat format) => format switch
        {
            VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
            VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16
                or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
            _ => 1,
        };

        bool ChannelPresent(VertexAttribute attr)
        {
            switch (attr)
            {
                case VertexAttribute.Position: return m_Vertices.Length > 0 || LayoutHas(attr);
                case VertexAttribute.Normal: return m_Normals != null;
                case VertexAttribute.Tangent: return m_Tangents != null;
                case VertexAttribute.Color: return m_Colors != null;
                case VertexAttribute.BlendWeight:
                case VertexAttribute.BlendIndices: return m_BoneWeights != null && (m_Layout == null || LayoutHas(attr));
                default:
                    int channel = (int)attr - (int)VertexAttribute.TexCoord0;
                    return channel >= 0 && channel < 8 && m_UVs[channel] != null;
            }
        }

        bool LayoutHas(VertexAttribute attr)
        {
            if (m_Layout == null) return false;
            foreach (var d in m_Layout) if (d.attribute == attr) return true;
            return false;
        }

        /// <summary>The current layout: the attributes the mesh has, in VertexAttribute order, as
        /// SetVertexBufferParams described them or, set through the channel setters, in Unity's
        /// default formats (Float32 positions, normals, tangents and uvs, UNorm8 colors).</summary>
        internal VertexAttributeDescriptor[] Layout()
        {
            var list = new List<VertexAttributeDescriptor>(14);
            for (var attr = VertexAttribute.Position; attr <= VertexAttribute.BlendIndices; attr++)
            {
                if (!ChannelPresent(attr)) continue;
                VertexAttributeDescriptor? custom = null;
                if (m_Layout != null)
                    foreach (var d in m_Layout)
                        if (d.attribute == attr) custom = d;
                if (custom.HasValue)
                {
                    list.Add(custom.Value);
                    continue;
                }
                list.Add(attr switch
                {
                    VertexAttribute.Position or VertexAttribute.Normal => new VertexAttributeDescriptor(attr, VertexAttributeFormat.Float32, 3),
                    VertexAttribute.Tangent => new VertexAttributeDescriptor(attr, VertexAttributeFormat.Float32, 4),
                    VertexAttribute.Color => new VertexAttributeDescriptor(attr, VertexAttributeFormat.UNorm8, 4),
                    VertexAttribute.BlendWeight => new VertexAttributeDescriptor(attr, VertexAttributeFormat.Float32, 4),
                    VertexAttribute.BlendIndices => new VertexAttributeDescriptor(attr, VertexAttributeFormat.UInt32, 4),
                    _ => new VertexAttributeDescriptor(attr, VertexAttributeFormat.Float32, Math.Max(1, m_UVDimensions[(int)attr - (int)VertexAttribute.TexCoord0])),
                });
            }
            return list.ToArray();
        }

        public int vertexAttributeCount => Layout().Length;

        public int vertexBufferCount
        {
            get
            {
                int count = 0;
                foreach (var d in Layout()) count = Math.Max(count, d.stream + 1);
                return Math.Max(count, 1);
            }
        }

        public VertexAttributeDescriptor[] GetVertexAttributes() => Layout();

        public int GetVertexAttributes(VertexAttributeDescriptor[] attributes)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            var layout = Layout();
            int n = Math.Min(layout.Length, attributes.Length);
            Array.Copy(layout, attributes, n);
            return n;
        }

        public int GetVertexAttributes(List<VertexAttributeDescriptor> attributes)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            attributes.Clear();
            attributes.AddRange(Layout());
            return attributes.Count;
        }

        public VertexAttributeDescriptor GetVertexAttribute(int index)
        {
            var layout = Layout();
            if (index < 0 || index >= layout.Length)
                throw new IndexOutOfRangeException($"Invalid vertex attribute index {index}, valid range is 0 to {layout.Length - 1}");
            return layout[index];
        }

        public bool HasVertexAttribute(VertexAttribute attr) => ChannelPresent(attr);

        public int GetVertexAttributeDimension(VertexAttribute attr)
        {
            foreach (var d in Layout()) if (d.attribute == attr) return d.dimension;
            return 0;
        }

        public VertexAttributeFormat GetVertexAttributeFormat(VertexAttribute attr)
        {
            foreach (var d in Layout()) if (d.attribute == attr) return d.format;
            return VertexAttributeFormat.Float32;
        }

        public int GetVertexAttributeStream(VertexAttribute attr)
        {
            foreach (var d in Layout()) if (d.attribute == attr) return d.stream;
            return -1;
        }

        /// <summary>The attribute's byte offset in its stream's vertices; -1 when the mesh has none.</summary>
        public int GetVertexAttributeOffset(VertexAttribute attr)
        {
            var layout = Layout();
            foreach (var d in layout)
            {
                if (d.attribute != attr) continue;
                int offset = 0;
                foreach (var e in layout)
                {
                    if (e.attribute == attr) return offset;
                    if (e.stream == d.stream) offset += FormatSize(e.format) * e.dimension;
                }
            }
            return -1;
        }

        public int GetVertexBufferStride(int stream)
        {
            int stride = 0;
            foreach (var d in Layout()) if (d.stream == stream) stride += FormatSize(d.format) * d.dimension;
            return stride;
        }

        // ------------------------------------------------------------------ vertex buffer

        public void SetVertexBufferParams(int vertexCount, params VertexAttributeDescriptor[] attributes)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            SetVertexBufferParamsInternal(vertexCount, attributes);
        }

        public void SetVertexBufferParams(int vertexCount, NativeArray<VertexAttributeDescriptor> attributes) =>
            SetVertexBufferParamsInternal(vertexCount, attributes.ToArray());

        internal void SetVertexBufferParamsInternal(int vertexCount, VertexAttributeDescriptor[] attributes)
        {
            if (vertexCount < 0) throw new ArgumentException("Vertex count can not be negative", nameof(vertexCount));
            var layout = CheckLayout(attributes);
            bool Has(VertexAttribute a) => Array.Exists(layout, d => d.attribute == a);
            T[] Resize<T>(T[] old, bool present) where T : struct
            {
                if (!present) return null;
                var fresh = new T[vertexCount];
                if (old != null) Array.Copy(old, fresh, Math.Min(old.Length, vertexCount));
                return fresh;
            }
            m_Vertices = Resize(m_Vertices, true);
            m_Normals = Resize(m_Normals, Has(VertexAttribute.Normal));
            m_Tangents = Resize(m_Tangents, Has(VertexAttribute.Tangent));
            m_Colors = Resize(m_Colors, Has(VertexAttribute.Color));
            for (int c = 0; c < 8; c++)
            {
                var attr = VertexAttribute.TexCoord0 + c;
                m_UVs[c] = Resize(m_UVs[c], Has(attr));
                m_UVDimensions[c] = Has(attr) ? Array.Find(layout, d => d.attribute == attr).dimension : 0;
            }
            m_BoneWeights = Resize(m_BoneWeights, Has(VertexAttribute.BlendWeight) || Has(VertexAttribute.BlendIndices));
            m_Layout = layout;
            Changed();
        }

        /// <summary>A layout as Unity takes it: sorted by attribute, each once, 1 to 4 components of
        /// a multiple of 4 bytes, in streams 0 to 3.</summary>
        internal static VertexAttributeDescriptor[] CheckLayout(VertexAttributeDescriptor[] attributes)
        {
            var layout = (VertexAttributeDescriptor[])attributes.Clone();
            Array.Sort(layout, (a, b) => a.attribute.CompareTo(b.attribute));
            for (int i = 0; i < layout.Length; i++)
            {
                var d = layout[i];
                if (i > 0 && layout[i - 1].attribute == d.attribute)
                    throw new ArgumentException($"Vertex attribute {d.attribute} is specified more than once");
                if (d.dimension < 1 || d.dimension > 4)
                    throw new ArgumentException($"Vertex attribute {d.attribute} dimension must be 1 to 4 (was {d.dimension})");
                if (d.stream < 0 || d.stream > 3)
                    throw new ArgumentException($"Vertex attribute {d.attribute} stream must be 0 to 3 (was {d.stream})");
                if (FormatSize(d.format) * d.dimension % 4 != 0)
                    throw new ArgumentException($"Vertex attribute {d.attribute} with format {d.format} and dimension {d.dimension} is not a multiple of 4 bytes");
            }
            return layout;
        }

        public void SetVertexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            CheckRange(data.Length, dataStart, count);
            unsafe
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                int size = UnsafeUtility.SizeOf<T>();
                WriteVertices(new ReadOnlySpan<byte>(p + (long)dataStart * size, count * size), meshBufferStart * size, stream);
            }
        }

        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            CheckRange(data.Length, dataStart, count);
            int size = UnsafeUtility.SizeOf<T>();
            WriteVertices(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data, dataStart, count)), meshBufferStart * size, stream);
        }

        public void SetVertexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, int stream = 0, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            CheckRange(data.Count, dataStart, count);
            int size = UnsafeUtility.SizeOf<T>();
            WriteVertices(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data).Slice(dataStart, count)), meshBufferStart * size, stream);
        }

        static void CheckRange(int length, int start, int count)
        {
            if (start < 0 || count < 0 || start + count > length)
                throw new ArgumentOutOfRangeException(nameof(start), $"Bad start/count arguments (start:{start} count:{count} length:{length})");
        }

        /// <summary>Decodes stream data written at byte <paramref name="offset"/> of the stream into
        /// the vertices it covers (whole vertices of the stream's stride).</summary>
        internal void WriteVertices(ReadOnlySpan<byte> bytes, int offset, int stream)
        {
            var layout = Layout();
            int stride = GetVertexBufferStride(stream);
            if (stride == 0) throw new ArgumentException($"The mesh has no vertex attributes in stream {stream}", nameof(stream));
            if (offset + bytes.Length > m_Vertices.Length * stride)
                throw new ArgumentException($"Writing {bytes.Length} bytes at {offset} overflows the vertex buffer's {m_Vertices.Length * stride} bytes (stream {stream}); call SetVertexBufferParams first");
            int first = offset / stride;
            int last = (offset + bytes.Length + stride - 1) / stride;
            Span<float> f = stackalloc float[4];
            Span<int> n = stackalloc int[4];
            for (int v = first; v < last; v++)
            {
                int at = 0;
                foreach (var d in layout)
                {
                    if (d.stream != stream) continue;
                    int size = FormatSize(d.format);
                    int start = v * stride + at - offset;
                    at += size * d.dimension;
                    // Only attributes the data covers wholly.
                    if (start < 0 || start + size * d.dimension > bytes.Length) continue;
                    for (int c = 0; c < 4; c++)
                    {
                        f[c] = c == 3 ? 1f : 0f;
                        n[c] = 0;
                    }
                    for (int c = 0; c < d.dimension; c++)
                    {
                        var s = bytes.Slice(start + c * size, size);
                        (f[c], n[c]) = Read(s, d.format);
                    }
                    Store(d, v, f, n, bytes.Slice(start, size * d.dimension));
                }
            }
            Changed();
        }

        /// <summary>A component as a float (normalized formats to their range) and as an integer.</summary>
        internal static (float, int) Read(ReadOnlySpan<byte> s, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: { float x = MemoryMarshal.Read<float>(s); return (x, (int)x); }
                case VertexAttributeFormat.Float16: { float x = (float)MemoryMarshal.Read<Half>(s); return (x, (int)x); }
                case VertexAttributeFormat.UNorm8: return (s[0] / 255f, s[0]);
                case VertexAttributeFormat.SNorm8: return (Math.Max(-1f, (sbyte)s[0] / 127f), (sbyte)s[0]);
                case VertexAttributeFormat.UNorm16: { ushort x = MemoryMarshal.Read<ushort>(s); return (x / 65535f, x); }
                case VertexAttributeFormat.SNorm16: { short x = MemoryMarshal.Read<short>(s); return (Math.Max(-1f, x / 32767f), x); }
                case VertexAttributeFormat.UInt8: return (s[0], s[0]);
                case VertexAttributeFormat.SInt8: return ((sbyte)s[0], (sbyte)s[0]);
                case VertexAttributeFormat.UInt16: { ushort x = MemoryMarshal.Read<ushort>(s); return (x, x); }
                case VertexAttributeFormat.SInt16: { short x = MemoryMarshal.Read<short>(s); return (x, x); }
                case VertexAttributeFormat.UInt32: { uint x = MemoryMarshal.Read<uint>(s); return (x, (int)x); }
                default: { int x = MemoryMarshal.Read<int>(s); return (x, x); }
            }
        }

        void Store(VertexAttributeDescriptor d, int v, ReadOnlySpan<float> f, ReadOnlySpan<int> n, ReadOnlySpan<byte> raw)
        {
            switch (d.attribute)
            {
                case VertexAttribute.Position: m_Vertices[v] = new Vector3(f[0], f[1], f[2]); break;
                case VertexAttribute.Normal: m_Normals[v] = new Vector3(f[0], f[1], f[2]); break;
                case VertexAttribute.Tangent: m_Tangents[v] = new Vector4(f[0], f[1], f[2], f[3]); break;
                case VertexAttribute.Color:
                    m_Colors[v] = d.format == VertexAttributeFormat.UNorm8 && d.dimension == 4
                        ? new Color32(raw[0], raw[1], raw[2], raw[3])
                        : (Color32)new Color(f[0], f[1], f[2], d.dimension == 4 ? f[3] : 1f);
                    break;
                case VertexAttribute.BlendWeight:
                {
                    var b = m_BoneWeights[v];
                    b.weight0 = f[0];
                    b.weight1 = d.dimension > 1 ? f[1] : 0f;
                    b.weight2 = d.dimension > 2 ? f[2] : 0f;
                    b.weight3 = d.dimension > 3 ? f[3] : 0f;
                    m_BoneWeights[v] = b;
                    break;
                }
                case VertexAttribute.BlendIndices:
                {
                    var b = m_BoneWeights[v];
                    b.boneIndex0 = n[0];
                    b.boneIndex1 = n[1];
                    b.boneIndex2 = n[2];
                    b.boneIndex3 = n[3];
                    m_BoneWeights[v] = b;
                    break;
                }
                default:
                {
                    int channel = (int)d.attribute - (int)VertexAttribute.TexCoord0;
                    m_UVs[channel][v] = new Vector4(f[0], d.dimension > 1 ? f[1] : 0f, d.dimension > 2 ? f[2] : 0f, d.dimension > 3 ? f[3] : 0f);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ index buffer

        public void SetIndexBufferParams(int indexCount, IndexFormat format)
        {
            if (indexCount < 0) throw new ArgumentException("Index count can not be negative", nameof(indexCount));
            var fresh = new int[indexCount];
            var old = IndexBuffer();
            Array.Copy(old, fresh, Math.Min(old.Length, indexCount));
            m_IndexBuffer = fresh;
            m_IndexFormat = format;
            Changed();
        }

        public void SetIndexBufferData<T>(NativeArray<T> data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            CheckRange(data.Length, dataStart, count);
            unsafe
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                int size = UnsafeUtility.SizeOf<T>();
                WriteIndices(new ReadOnlySpan<byte>(p + (long)dataStart * size, count * size), meshBufferStart * size, flags);
            }
        }

        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            CheckRange(data.Length, dataStart, count);
            WriteIndices(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data, dataStart, count)), meshBufferStart * UnsafeUtility.SizeOf<T>(), flags);
        }

        public void SetIndexBufferData<T>(List<T> data, int dataStart, int meshBufferStart, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            CheckRange(data.Count, dataStart, count);
            WriteIndices(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data).Slice(dataStart, count)), meshBufferStart * UnsafeUtility.SizeOf<T>(), flags);
        }

        /// <summary>Indices (of the buffer's format) written at byte <paramref name="offset"/>.</summary>
        internal void WriteIndices(ReadOnlySpan<byte> bytes, int offset, MeshUpdateFlags flags)
        {
            var buffer = IndexBuffer();
            int size = m_IndexFormat == IndexFormat.UInt32 ? 4 : 2;
            int first = offset / size, count = bytes.Length / size;
            if (first + count > buffer.Length)
                throw new ArgumentException($"Writing {count} indices at {first} overflows the index buffer's {buffer.Length}; call SetIndexBufferParams first");
            for (int i = 0; i < count; i++)
            {
                int index = size == 4 ? (int)MemoryMarshal.Read<uint>(bytes.Slice(i * size)) : MemoryMarshal.Read<ushort>(bytes.Slice(i * size));
                if ((flags & MeshUpdateFlags.DontValidateIndices) == 0 && (index < 0 || index >= m_Vertices.Length))
                    throw new ArgumentException($"Index buffer element #{first + i} ({index}) is out of bounds; mesh vertex count is {m_Vertices.Length}");
                buffer[first + i] = index;
            }
            m_IndexBuffer = buffer;
            // The submeshes see the new indices.
            foreach (var s in m_SubMeshes)
                if (s.IndexStart >= 0 && s.IndexStart + s.Indices.Length <= buffer.Length)
                    s.Indices = new ArraySegment<int>(buffer, s.IndexStart, s.Indices.Length).ToArray();
            Changed();
        }

        /// <summary>The index buffer: the one set, else the submeshes' indices one after another.</summary>
        int[] IndexBuffer()
        {
            if (m_IndexBuffer != null) return m_IndexBuffer;
            var all = new List<int>();
            foreach (var s in m_SubMeshes)
            {
                s.IndexStart = all.Count;
                all.AddRange(s.Indices);
            }
            return m_IndexBuffer = all.ToArray();
        }

        // ------------------------------------------------------------------ submeshes

        public void SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            CheckSubmesh(index);
            var buffer = IndexBuffer();
            if (desc.indexStart < 0 || desc.indexCount < 0 || desc.indexStart + desc.indexCount > buffer.Length)
                throw new ArgumentException($"Bad SubMesh index start/count (start:{desc.indexStart} count:{desc.indexCount} buffer:{buffer.Length})");
            var s = m_SubMeshes[index];
            s.Indices = new ArraySegment<int>(buffer, desc.indexStart, desc.indexCount).ToArray();
            s.IndexStart = desc.indexStart;
            s.Topology = desc.topology;
            s.BaseVertex = desc.baseVertex;
            if ((flags & MeshUpdateFlags.DontValidateIndices) == 0)
                foreach (var i in s.Indices)
                    if (i + desc.baseVertex < 0 || i + desc.baseVertex >= m_Vertices.Length)
                        throw new ArgumentException($"SubMesh {index} indices are out of bounds; mesh vertex count is {m_Vertices.Length}");
            // The submesh's bounds and vertex range, unless the descriptor's are to be taken; the
            // mesh's bounds stay (measured: tests/projects/graphics meshLayout).
            if ((flags & MeshUpdateFlags.DontRecalculateBounds) == 0) s.Measure(m_Vertices);
            else
            {
                s.Bounds = desc.bounds;
                s.FirstVertex = desc.firstVertex;
                s.VertexCount = desc.vertexCount;
            }
            Changed();
        }

        public void SetSubMeshes(SubMeshDescriptor[] desc, int start, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            CheckRange(desc.Length, start, count);
            subMeshCount = count;
            for (int i = 0; i < count; i++) SetSubMesh(i, desc[start + i], flags);
        }

        public void SetSubMeshes(SubMeshDescriptor[] desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            SetSubMeshes(desc, 0, desc?.Length ?? 0, flags);

        public void SetSubMeshes(List<SubMeshDescriptor> desc, int start, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            SetSubMeshes(desc?.ToArray(), start, count, flags);

        public void SetSubMeshes(List<SubMeshDescriptor> desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            SetSubMeshes(desc?.ToArray(), flags);

        public void SetSubMeshes<T>(NativeArray<T> desc, int start, int count, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct
        {
            if (UnsafeUtility.SizeOf<T>() != UnsafeUtility.SizeOf<SubMeshDescriptor>())
                throw new ArgumentException($"{typeof(T)} is not the size of a SubMeshDescriptor");
            SetSubMeshes(desc.Reinterpret<SubMeshDescriptor>().ToArray(), start, count, flags);
        }

        public void SetSubMeshes<T>(NativeArray<T> desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) where T : struct =>
            SetSubMeshes(desc, 0, desc.Length, flags);

        // ------------------------------------------------------------------ indices from native data

        public void SetIndices<T>(NativeArray<T> indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) where T : struct =>
            SetIndices(indices, 0, indices.Length, topology, submesh, calculateBounds, baseVertex);

        public void SetIndices<T>(NativeArray<T> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) where T : struct
        {
            CheckRange(indices.Length, indicesStart, indicesLength);
            int size = UnsafeUtility.SizeOf<T>();
            if (size != 2 && size != 4) throw new ArgumentException("SetIndices with NativeArray should use type is 2 or 4 bytes in size");
            var values = new int[indicesLength];
            unsafe
            {
                byte* p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indices) + (long)indicesStart * size;
                for (int i = 0; i < indicesLength; i++) values[i] = size == 4 ? ((int*)p)[i] : ((ushort*)p)[i];
            }
            SetIndices(values, topology, submesh, calculateBounds, baseVertex);
        }

        public void SetIndices(ushort[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(new ArraySegment<ushort>(indices, indicesStart, indicesLength).ToArray(), topology, submesh, calculateBounds, baseVertex);

        public void SetIndices(List<int> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(indices.GetRange(indicesStart, indicesLength).ToArray(), topology, submesh, calculateBounds, baseVertex);

        public void SetIndices(List<ushort> indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(indices.ToArray(), topology, submesh, calculateBounds, baseVertex);

        public void SetIndices(List<ushort> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(indices.GetRange(indicesStart, indicesLength).ToArray(), topology, submesh, calculateBounds, baseVertex);
    }
}
