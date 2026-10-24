using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Mesh data for jobs (MeshData, MeshDataArray): a mesh's vertex streams and index buffer as raw
    /// bytes in native memory, laid out as the mesh's vertex layout says, read from meshes
    /// (AcquireReadOnlyMeshData) or written and applied to them (AllocateWritableMeshData,
    /// ApplyAndDisposeWritableMeshData). The structs hold a handle to their store, as Unity's
    /// hold a native pointer.
    /// </summary>
    public sealed partial class Mesh
    {
        internal sealed unsafe class MeshDataStore
        {
            internal VertexAttributeDescriptor[] Layout = Array.Empty<VertexAttributeDescriptor>();
            internal int VertexCount;
            internal readonly byte*[] Streams = new byte*[4];
            internal readonly int[] StreamBytes = new int[4];
            internal byte* Indices;
            internal int IndexCount;
            internal IndexFormat IndexFormat = IndexFormat.UInt16;
            internal readonly List<SubMeshDescriptor> SubMeshes = new List<SubMeshDescriptor>();
            internal readonly bool ReadOnly;
            internal AtomicSafetyHandle Safety = AtomicSafetyHandle.Create();

            internal MeshDataStore(bool readOnly) => ReadOnly = readOnly;

            internal int Stride(int stream)
            {
                int stride = 0;
                foreach (var d in Layout) if (d.stream == stream) stride += FormatSize(d.format) * d.dimension;
                return stride;
            }

            internal int Offset(VertexAttribute attr)
            {
                foreach (var d in Layout)
                {
                    if (d.attribute != attr) continue;
                    int offset = 0;
                    foreach (var e in Layout)
                    {
                        if (e.attribute == attr) return offset;
                        if (e.stream == d.stream) offset += FormatSize(e.format) * e.dimension;
                    }
                }
                return -1;
            }

            internal bool Find(VertexAttribute attr, out VertexAttributeDescriptor d)
            {
                foreach (var e in Layout)
                    if (e.attribute == attr) { d = e; return true; }
                d = default;
                return false;
            }

            internal void SetVertexBufferParams(int vertexCount, VertexAttributeDescriptor[] attributes)
            {
                if (vertexCount < 0) throw new ArgumentException("Vertex count can not be negative", nameof(vertexCount));
                Layout = CheckLayout(attributes);
                VertexCount = vertexCount;
                for (int s = 0; s < 4; s++)
                {
                    FreeStream(s);
                    int bytes = Stride(s) * vertexCount;
                    if (bytes == 0) continue;
                    Streams[s] = (byte*)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
                    UnsafeUtility.MemClear(Streams[s], bytes);
                    StreamBytes[s] = bytes;
                }
            }

            internal void SetIndexBufferParams(int indexCount, IndexFormat format)
            {
                if (indexCount < 0) throw new ArgumentException("Index count can not be negative", nameof(indexCount));
                if (Indices != null) UnsafeUtility.Free(Indices, Allocator.Persistent);
                int bytes = Math.Max(1, indexCount) * (format == IndexFormat.UInt32 ? 4 : 2);
                Indices = (byte*)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(Indices, bytes);
                IndexCount = indexCount;
                IndexFormat = format;
            }

            internal int Index(int i) => IndexFormat == IndexFormat.UInt32 ? ((int*)Indices)[i] : ((ushort*)Indices)[i];

            void FreeStream(int s)
            {
                if (Streams[s] != null) UnsafeUtility.Free(Streams[s], Allocator.Persistent);
                Streams[s] = null;
                StreamBytes[s] = 0;
            }

            internal void Free()
            {
                for (int s = 0; s < 4; s++) FreeStream(s);
                if (Indices != null) UnsafeUtility.Free(Indices, Allocator.Persistent);
                Indices = null;
                IndexCount = 0;
                AtomicSafetyHandle.Release(Safety);
            }

            /// <summary>A mesh's data in its current layout.</summary>
            internal static MeshDataStore Of(Mesh mesh, bool readOnly)
            {
                var store = new MeshDataStore(readOnly);
                var layout = mesh.Layout();
                store.SetVertexBufferParams(mesh.vertexCount, layout);
                for (int v = 0; v < mesh.vertexCount; v++)
                    foreach (var d in layout)
                    {
                        byte* at = store.Streams[d.stream] + v * store.Stride(d.stream) + store.Offset(d.attribute);
                        mesh.ReadChannelArrays(d.attribute, v, out float[] f, out int[] n);
                        for (int c = 0; c < d.dimension; c++)
                            Write(at + c * FormatSize(d.format), d.format, f[c], n[c]);
                    }
                var buffer = mesh.IndexBuffer();
                store.SetIndexBufferParams(buffer.Length, mesh.indexFormat);
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (store.IndexFormat == IndexFormat.UInt32) ((int*)store.Indices)[i] = buffer[i];
                    else ((ushort*)store.Indices)[i] = (ushort)buffer[i];
                }
                for (int s = 0; s < mesh.subMeshCount; s++) store.SubMeshes.Add(mesh.GetSubMesh(s));
                return store;
            }

            /// <summary>Writes the data into a mesh.</summary>
            internal void ApplyTo(Mesh mesh, MeshUpdateFlags flags)
            {
                mesh.SetVertexBufferParamsInternal(VertexCount, Layout);
                for (int s = 0; s < 4; s++)
                    if (StreamBytes[s] > 0) mesh.WriteVertices(new ReadOnlySpan<byte>(Streams[s], StreamBytes[s]), 0, s);
                mesh.SetIndexBufferParams(IndexCount, IndexFormat);
                if (IndexCount > 0)
                    mesh.WriteIndices(new ReadOnlySpan<byte>(Indices, IndexCount * (IndexFormat == IndexFormat.UInt32 ? 4 : 2)), 0, flags | MeshUpdateFlags.DontValidateIndices);
                mesh.subMeshCount = SubMeshes.Count;
                for (int i = 0; i < SubMeshes.Count; i++) mesh.SetSubMesh(i, SubMeshes[i], flags);
            }
        }

        static unsafe void Write(byte* p, VertexAttributeFormat format, float f, int n)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: *(float*)p = f; break;
                case VertexAttributeFormat.Float16: *(Half*)p = (Half)f; break;
                case VertexAttributeFormat.UNorm8: *p = (byte)Math.Round(Mathf.Clamp01(f) * 255f); break;
                case VertexAttributeFormat.SNorm8: *(sbyte*)p = (sbyte)Math.Round(Mathf.Clamp(f, -1f, 1f) * 127f); break;
                case VertexAttributeFormat.UNorm16: *(ushort*)p = (ushort)Math.Round(Mathf.Clamp01(f) * 65535f); break;
                case VertexAttributeFormat.SNorm16: *(short*)p = (short)Math.Round(Mathf.Clamp(f, -1f, 1f) * 32767f); break;
                case VertexAttributeFormat.UInt8: *p = (byte)n; break;
                case VertexAttributeFormat.SInt8: *(sbyte*)p = (sbyte)n; break;
                case VertexAttributeFormat.UInt16: *(ushort*)p = (ushort)n; break;
                case VertexAttributeFormat.SInt16: *(short*)p = (short)n; break;
                case VertexAttributeFormat.UInt32: *(uint*)p = (uint)n; break;
                default: *(int*)p = n; break;
            }
        }

        /// <summary>A vertex's attribute as floats and integers (missing components: 0, w 1).</summary>
        void ReadChannel(VertexAttribute attr, int v, out Vector4 f, out (int, int, int, int) n4)
        {
            f = new Vector4(0f, 0f, 0f, 1f);
            n4 = default;
            switch (attr)
            {
                case VertexAttribute.Position: { var p = m_Vertices[v]; f = new Vector4(p.x, p.y, p.z, 1f); break; }
                case VertexAttribute.Normal: { var p = m_Normals[v]; f = new Vector4(p.x, p.y, p.z, 0f); break; }
                case VertexAttribute.Tangent: f = m_Tangents[v]; break;
                case VertexAttribute.Color: { Color c = m_Colors[v]; f = new Vector4(c.r, c.g, c.b, c.a); break; }
                case VertexAttribute.BlendWeight: { var b = m_BoneWeights[v]; f = new Vector4(b.weight0, b.weight1, b.weight2, b.weight3); break; }
                case VertexAttribute.BlendIndices: { var b = m_BoneWeights[v]; n4 = (b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3); f = new Vector4(n4.Item1, n4.Item2, n4.Item3, n4.Item4); return; }
                default: f = m_UVs[(int)attr - (int)VertexAttribute.TexCoord0][v]; break;
            }
            n4 = ((int)f.x, (int)f.y, (int)f.z, (int)f.w);
        }

        void ReadChannelArrays(VertexAttribute attr, int v, out float[] f, out int[] n)
        {
            ReadChannel(attr, v, out Vector4 v4, out var i4);
            f = new[] { v4.x, v4.y, v4.z, v4.w };
            n = new[] { i4.Item1, i4.Item2, i4.Item3, i4.Item4 };
        }

        public unsafe partial struct MeshData
        {
            internal IntPtr m_Ptr;
            internal AtomicSafetyHandle m_Safety;

            internal MeshDataStore Store
            {
                get
                {
                    if (m_Ptr == IntPtr.Zero) throw new InvalidOperationException("The MeshData is not valid (its MeshDataArray was disposed or never made).");
                    return (MeshDataStore)GCHandle.FromIntPtr(m_Ptr).Target;
                }
            }

            MeshDataStore Writable
            {
                get
                {
                    var store = Store;
                    if (store.ReadOnly) throw new InvalidOperationException("Not allowed to modify MeshData from Mesh.AcquireReadOnlyMeshData");
                    return store;
                }
            }

            public int vertexCount => Store.VertexCount;

            public int vertexBufferCount
            {
                get
                {
                    int count = 0;
                    foreach (var d in Store.Layout) count = Math.Max(count, d.stream + 1);
                    return count;
                }
            }

            public int GetVertexBufferStride(int stream) => Store.Stride(stream);
            public bool HasVertexAttribute(VertexAttribute attr) => Store.Find(attr, out _);
            public int GetVertexAttributeDimension(VertexAttribute attr) => Store.Find(attr, out var d) ? d.dimension : 0;
            public VertexAttributeFormat GetVertexAttributeFormat(VertexAttribute attr) => Store.Find(attr, out var d) ? d.format : VertexAttributeFormat.Float32;
            public int GetVertexAttributeStream(VertexAttribute attr) => Store.Find(attr, out var d) ? d.stream : -1;
            public int GetVertexAttributeOffset(VertexAttribute attr) => Store.Offset(attr);

            /// <summary>An attribute of every vertex, each component read as a float.</summary>
            void Read<T>(VertexAttribute attr, NativeArray<T> output, int dimension, Func<float[], T> make) where T : struct
            {
                var store = Store;
                if (output.Length < store.VertexCount)
                    throw new InvalidOperationException($"Not enough space in output buffer (need {store.VertexCount}, has {output.Length})");
                if (!store.Find(attr, out var d))
                {
                    for (int v = 0; v < store.VertexCount; v++) output[v] = make(new float[] { 0f, 0f, 0f, attr == VertexAttribute.Color ? 1f : 0f });
                    return;
                }
                int size = FormatSize(d.format), stride = store.Stride(d.stream), offset = store.Offset(attr);
                var f = new float[4];
                for (int v = 0; v < store.VertexCount; v++)
                {
                    f[0] = f[1] = f[2] = 0f;
                    f[3] = attr == VertexAttribute.Color ? 1f : 0f;
                    byte* at = store.Streams[d.stream] + v * stride + offset;
                    for (int c = 0; c < d.dimension; c++)
                        f[c] = Mesh.Read(new ReadOnlySpan<byte>(at + c * size, size), d.format).Item1;
                    output[v] = make(f);
                }
            }

            public void GetVertices(NativeArray<Vector3> outVertices) => Read(VertexAttribute.Position, outVertices, 3, f => new Vector3(f[0], f[1], f[2]));
            public void GetNormals(NativeArray<Vector3> outNormals) => Read(VertexAttribute.Normal, outNormals, 3, f => new Vector3(f[0], f[1], f[2]));
            public void GetTangents(NativeArray<Vector4> outTangents) => Read(VertexAttribute.Tangent, outTangents, 4, f => new Vector4(f[0], f[1], f[2], f[3]));
            public void GetColors(NativeArray<Color> outColors) => Read(VertexAttribute.Color, outColors, 4, f => new Color(f[0], f[1], f[2], f[3]));
            public void GetColors(NativeArray<Color32> outColors) => Read(VertexAttribute.Color, outColors, 4, f => (Color32)new Color(f[0], f[1], f[2], f[3]));
            public void GetUVs(int channel, NativeArray<Vector2> outUVs) => Read(UV(channel), outUVs, 2, f => new Vector2(f[0], f[1]));
            public void GetUVs(int channel, NativeArray<Vector3> outUVs) => Read(UV(channel), outUVs, 3, f => new Vector3(f[0], f[1], f[2]));
            public void GetUVs(int channel, NativeArray<Vector4> outUVs) => Read(UV(channel), outUVs, 4, f => new Vector4(f[0], f[1], f[2], f[3]));

            static VertexAttribute UV(int channel)
            {
                if (channel < 0 || channel > 7) throw new ArgumentOutOfRangeException(nameof(channel), channel, "The uv index is invalid. Must be in the range 0 to 7.");
                return VertexAttribute.TexCoord0 + channel;
            }

            /// <summary>A stream's bytes as an array of <typeparamref name="T"/> (no copy).</summary>
            public NativeArray<T> GetVertexData<T>(int stream = 0) where T : struct
            {
                var store = Store;
                if (stream < 0 || stream > 3) throw new ArgumentOutOfRangeException(nameof(stream), $"Stream {stream} is out of bounds (0 to 3)");
                int size = UnsafeUtility.SizeOf<T>();
                int length = store.StreamBytes[stream] / size;
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(store.Streams[stream], length, Allocator.None);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, store.Safety);
                return array;
            }

            public void SetVertexBufferParams(int vertexCount, params VertexAttributeDescriptor[] attributes)
            {
                if (attributes == null) throw new ArgumentNullException(nameof(attributes));
                Writable.SetVertexBufferParams(vertexCount, attributes);
            }

            public void SetVertexBufferParams(int vertexCount, NativeArray<VertexAttributeDescriptor> attributes) =>
                Writable.SetVertexBufferParams(vertexCount, attributes.ToArray());

            public void SetIndexBufferParams(int indexCount, IndexFormat format) => Writable.SetIndexBufferParams(indexCount, format);

            public IndexFormat indexFormat => Store.IndexFormat;

            public NativeArray<T> GetIndexData<T>() where T : struct
            {
                var store = Store;
                int bytes = store.IndexCount * (store.IndexFormat == IndexFormat.UInt32 ? 4 : 2);
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(store.Indices, bytes / UnsafeUtility.SizeOf<T>(), Allocator.None);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, store.Safety);
                return array;
            }

            public void GetIndices(NativeArray<ushort> outIndices, int submesh, bool applyBaseVertex = true)
            {
                var (start, count, baseVertex) = Range(submesh, outIndices.Length, applyBaseVertex);
                for (int i = 0; i < count; i++) outIndices[i] = (ushort)(Store.Index(start + i) + baseVertex);
            }

            public void GetIndices(NativeArray<int> outIndices, int submesh, bool applyBaseVertex = true)
            {
                var (start, count, baseVertex) = Range(submesh, outIndices.Length, applyBaseVertex);
                for (int i = 0; i < count; i++) outIndices[i] = Store.Index(start + i) + baseVertex;
            }

            (int, int, int) Range(int submesh, int space, bool applyBaseVertex)
            {
                var d = GetSubMesh(submesh);
                if (space < d.indexCount) throw new InvalidOperationException($"Not enough space in output buffer (need {d.indexCount}, has {space})");
                return (d.indexStart, d.indexCount, applyBaseVertex ? d.baseVertex : 0);
            }

            public int subMeshCount
            {
                get => Store.SubMeshes.Count;
                set
                {
                    var list = Writable.SubMeshes;
                    if (value < 0) throw new ArgumentException("subMeshCount can't be set to negative value");
                    while (list.Count < value) list.Add(new SubMeshDescriptor(0, 0));
                    while (list.Count > value) list.RemoveAt(list.Count - 1);
                }
            }

            public SubMeshDescriptor GetSubMesh(int index)
            {
                var list = Store.SubMeshes;
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException($"Specified sub mesh ({index}) is out of range. Must be greater or equal to 0 and less than subMeshCount ({list.Count}).");
                return list[index];
            }

            /// <summary>A submesh: its range of the index buffer, with its bounds and vertex range
            /// computed unless <see cref="MeshUpdateFlags.DontRecalculateBounds"/> says to take them.</summary>
            public void SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default)
            {
                var store = Writable;
                if (index < 0 || index >= store.SubMeshes.Count)
                    throw new IndexOutOfRangeException($"Specified sub mesh ({index}) is out of range. Must be greater or equal to 0 and less than subMeshCount ({store.SubMeshes.Count}).");
                if (desc.indexStart < 0 || desc.indexCount < 0 || desc.indexStart + desc.indexCount > store.IndexCount)
                    throw new ArgumentException($"Bad SubMesh index start/count (start:{desc.indexStart} count:{desc.indexCount} buffer:{store.IndexCount})");
                if ((flags & MeshUpdateFlags.DontRecalculateBounds) == 0 && store.Find(VertexAttribute.Position, out var position))
                {
                    int min = int.MaxValue, max = int.MinValue;
                    Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                    int size = FormatSize(position.format), stride = store.Stride(position.stream), offset = store.Offset(VertexAttribute.Position);
                    for (int i = 0; i < desc.indexCount; i++)
                    {
                        int v = store.Index(desc.indexStart + i) + desc.baseVertex;
                        if (v < 0 || v >= store.VertexCount) continue;
                        min = Math.Min(min, v);
                        max = Math.Max(max, v);
                        byte* at = store.Streams[position.stream] + v * stride + offset;
                        var p = Vector3.zero;
                        for (int c = 0; c < Math.Min(3, position.dimension); c++) p[c] = Mesh.Read(new ReadOnlySpan<byte>(at + c * size, size), position.format).Item1;
                        lo = Vector3.Min(lo, p);
                        hi = Vector3.Max(hi, p);
                    }
                    if (min <= max)
                    {
                        var b = new Bounds();
                        b.SetMinMax(lo, hi);
                        desc.bounds = b;
                        desc.firstVertex = min;
                        desc.vertexCount = max - min + 1;
                    }
                }
                store.SubMeshes[index] = desc;
            }
        }

        public unsafe partial struct MeshDataArray : IDisposable
        {
            internal IntPtr* m_Ptrs;
            internal int m_Length;
            internal int m_MinIndex;
            internal int m_MaxIndex;

            public int Length => m_Length;

            public MeshData this[int index]
            {
                get
                {
                    if (m_Ptrs == null) throw new ObjectDisposedException("MeshDataArray", "The MeshDataArray has been disposed.");
                    if (index < 0 || index >= m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
                    return new MeshData { m_Ptr = m_Ptrs[index] };
                }
            }

            void Allocate(int count)
            {
                m_Length = count;
                m_MinIndex = 0;
                m_MaxIndex = count - 1;
                m_Ptrs = (IntPtr*)UnsafeUtility.Malloc(Math.Max(1, count) * (long)sizeof(IntPtr), 16, Allocator.Persistent);
            }

            internal MeshDataArray(int meshesCount)
            {
                if (meshesCount < 0) throw new InvalidOperationException($"Mesh count can not be negative (was {meshesCount})");
                m_Ptrs = null;
                m_Length = m_MinIndex = m_MaxIndex = 0;
                Allocate(meshesCount);
                for (int i = 0; i < meshesCount; i++) m_Ptrs[i] = GCHandle.ToIntPtr(GCHandle.Alloc(new MeshDataStore(false)));
            }

            internal MeshDataArray(Mesh mesh, bool checkReadWrite = true, bool createAsCopy = false)
                : this(new[] { mesh ?? throw new ArgumentNullException(nameof(mesh)) }, 1, checkReadWrite, createAsCopy) { }

            internal MeshDataArray(Mesh[] meshes, int meshesCount, bool checkReadWrite = true, bool createAsCopy = false)
            {
                if (meshes == null) throw new ArgumentNullException(nameof(meshes));
                if (meshes.Length < meshesCount) throw new InvalidOperationException($"Meshes array size ({meshes.Length}) is smaller than meshes count ({meshesCount})");
                m_Ptrs = null;
                m_Length = m_MinIndex = m_MaxIndex = 0;
                for (int i = 0; i < meshesCount; i++)
                {
                    if (meshes[i] == null) throw new ArgumentNullException(nameof(meshes), $"Mesh at index {i} is null");
                    if (checkReadWrite && !meshes[i].isReadable) throw new InvalidOperationException($"Not allowed to access vertex data on mesh '{meshes[i].name}' (isReadable is false; Read/Write must be enabled in import settings)");
                }
                Allocate(meshesCount);
                for (int i = 0; i < meshesCount; i++)
                    m_Ptrs[i] = GCHandle.ToIntPtr(GCHandle.Alloc(MeshDataStore.Of(meshes[i], !createAsCopy)));
            }

            public void Dispose()
            {
                if (m_Ptrs == null) return;
                for (int i = 0; i < m_Length; i++)
                {
                    var handle = GCHandle.FromIntPtr(m_Ptrs[i]);
                    ((MeshDataStore)handle.Target).Free();
                    handle.Free();
                }
                UnsafeUtility.Free(m_Ptrs, Allocator.Persistent);
                m_Ptrs = null;
                m_Length = 0;
            }

            internal void ApplyToMeshAndDispose(Mesh mesh, MeshUpdateFlags flags) => ApplyToMeshesAndDispose(new[] { mesh }, flags);

            internal void ApplyToMeshesAndDispose(Mesh[] meshes, MeshUpdateFlags flags)
            {
                if (m_Ptrs == null) throw new ObjectDisposedException("MeshDataArray", "The MeshDataArray has been disposed.");
                if (meshes == null) throw new ArgumentNullException(nameof(meshes));
                if (meshes.Length != m_Length) throw new InvalidOperationException($"{m_Length} mesh data elements were written, but {meshes.Length} meshes are being set");
                for (int i = 0; i < m_Length; i++)
                {
                    if (meshes[i] == null) throw new ArgumentNullException(nameof(meshes), $"Mesh at index {i} is null");
                    var store = (MeshDataStore)GCHandle.FromIntPtr(m_Ptrs[i]).Target;
                    if (store.ReadOnly) throw new InvalidOperationException("MeshDataArray acquired with Mesh.AcquireReadOnlyMeshData cannot be applied to meshes");
                    store.ApplyTo(meshes[i], flags);
                }
                Dispose();
            }
        }

        public static MeshDataArray AcquireReadOnlyMeshData(Mesh mesh) => new MeshDataArray(mesh);
        public static MeshDataArray AcquireReadOnlyMeshData(Mesh[] meshes) => new MeshDataArray(meshes ?? throw new ArgumentNullException(nameof(meshes)), meshes.Length);
        public static MeshDataArray AcquireReadOnlyMeshData(List<Mesh> meshes) => AcquireReadOnlyMeshData(meshes?.ToArray());

        public static MeshDataArray AllocateWritableMeshData(int meshCount) => new MeshDataArray(meshCount);
        /// <summary>Writable data initialized with the meshes' data.</summary>
        public static MeshDataArray AllocateWritableMeshData(Mesh mesh) => new MeshDataArray(mesh, false, true);
        public static MeshDataArray AllocateWritableMeshData(Mesh[] meshes) => new MeshDataArray(meshes ?? throw new ArgumentNullException(nameof(meshes)), meshes.Length, false, true);
        public static MeshDataArray AllocateWritableMeshData(List<Mesh> meshes) => AllocateWritableMeshData(meshes?.ToArray());

        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh mesh, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh), "Mesh is null");
            if (data.Length != 1) throw new InvalidOperationException($"{data.Length} mesh data elements were written, but 1 mesh is being set");
            data.ApplyToMeshAndDispose(mesh, flags);
        }

        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh[] meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            data.ApplyToMeshesAndDispose(meshes, flags);

        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, List<Mesh> meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            data.ApplyToMeshesAndDispose(meshes?.ToArray(), flags);
    }
}
