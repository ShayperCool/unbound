using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>Vertices, attributes and submesh indices. The data lives here; the renderer gets a copy
    /// when the mesh changed.</summary>
    [ClassId(43)]
    public sealed partial class Mesh : Object, Instantiation.ICloneable
    {
        internal Vector3[] m_Vertices = Array.Empty<Vector3>();
        internal Vector3[] m_Normals;
        internal Vector4[] m_Tangents;
        internal Color32[] m_Colors;
        /// <summary>Eight uv channels, stored with four components; dimension tracked per channel.</summary>
        internal readonly Vector4[][] m_UVs = new Vector4[8][];
        internal readonly int[] m_UVDimensions = new int[8];
        internal readonly List<SubMesh> m_SubMeshes = new List<SubMesh> { new SubMesh() };
        internal BoneWeight[] m_BoneWeights;
        internal Matrix4x4[] m_Bindposes;
        Bounds m_Bounds;
        internal ulong m_Handle;
        internal bool m_Dirty = true;
        bool m_IsReadable = true;
        IndexFormat m_IndexFormat = IndexFormat.UInt16;

        internal sealed class SubMesh
        {
            internal int[] Indices = Array.Empty<int>();
            internal MeshTopology Topology = MeshTopology.Triangles;
            internal int BaseVertex;
        }

        public Mesh()
        {
            m_Name = "";
        }

        Object Instantiation.ICloneable.CloneObject()
        {
            var m = new Mesh();
            m.CopyGeometryFrom(this);
            return m;
        }

        /// <summary>Takes a copy of another mesh's data (what CanvasRenderer.SetMesh keeps).</summary>
        internal void CopyGeometryFrom(Mesh source)
        {
            m_Vertices = (Vector3[])source.m_Vertices.Clone();
            m_Normals = (Vector3[])source.m_Normals?.Clone();
            m_Tangents = (Vector4[])source.m_Tangents?.Clone();
            m_Colors = (Color32[])source.m_Colors?.Clone();
            for (int i = 0; i < 8; i++)
            {
                m_UVs[i] = (Vector4[])source.m_UVs[i]?.Clone();
                m_UVDimensions[i] = source.m_UVDimensions[i];
            }
            m_SubMeshes.Clear();
            foreach (var s in source.m_SubMeshes)
                m_SubMeshes.Add(new SubMesh { Indices = (int[])s.Indices.Clone(), Topology = s.Topology, BaseVertex = s.BaseVertex });
            m_BoneWeights = (BoneWeight[])source.m_BoneWeights?.Clone();
            m_Bindposes = (Matrix4x4[])source.m_Bindposes?.Clone();
            m_Bounds = source.m_Bounds;
            m_IndexFormat = source.m_IndexFormat;
            m_Dirty = true;
        }

        void Changed() => m_Dirty = true;

        public bool isReadable => m_IsReadable;
        public int vertexCount => m_Vertices.Length;
        public int vertexBufferCount => 1;
        public int vertexAttributeCount => 1 + (m_Normals != null ? 1 : 0) + (m_Tangents != null ? 1 : 0) + (m_Colors != null ? 1 : 0);
        public IndexFormat indexFormat { get => m_IndexFormat; set => m_IndexFormat = value; }
        public int blendShapeCount => 0;
        public MeshTopology GetTopology(int submesh) => m_SubMeshes[submesh].Topology;

        public int subMeshCount
        {
            get => m_SubMeshes.Count;
            set
            {
                if (value < 0) throw new ArgumentException("subMeshCount can't be set to negative value");
                while (m_SubMeshes.Count < value) m_SubMeshes.Add(new SubMesh());
                while (m_SubMeshes.Count > value) m_SubMeshes.RemoveAt(m_SubMeshes.Count - 1);
                Changed();
            }
        }

        public Bounds bounds
        {
            get => m_Bounds;
            set => m_Bounds = value;
        }

        // ------------------------------------------------------------------ vertices

        public Vector3[] vertices
        {
            get => (Vector3[])m_Vertices.Clone();
            set
            {
                m_Vertices = value != null ? (Vector3[])value.Clone() : Array.Empty<Vector3>();
                RecalculateBounds();
                Changed();
            }
        }

        public Vector3[] normals
        {
            get => m_Normals != null ? (Vector3[])m_Normals.Clone() : Array.Empty<Vector3>();
            set { m_Normals = Checked(value, "normals"); Changed(); }
        }

        public Vector4[] tangents
        {
            get => m_Tangents != null ? (Vector4[])m_Tangents.Clone() : Array.Empty<Vector4>();
            set { m_Tangents = Checked(value, "tangents"); Changed(); }
        }

        public Color[] colors
        {
            get
            {
                if (m_Colors == null) return Array.Empty<Color>();
                var result = new Color[m_Colors.Length];
                for (int i = 0; i < result.Length; i++) result[i] = m_Colors[i];
                return result;
            }
            set
            {
                if (value == null || value.Length == 0) { m_Colors = null; Changed(); return; }
                var c = new Color32[value.Length];
                for (int i = 0; i < c.Length; i++) c[i] = value[i];
                m_Colors = Checked(c, "colors");
                Changed();
            }
        }

        public Color32[] colors32
        {
            get => m_Colors != null ? (Color32[])m_Colors.Clone() : Array.Empty<Color32>();
            set { m_Colors = Checked(value, "colors32"); Changed(); }
        }

        T[] Checked<T>(T[] value, string what)
        {
            if (value == null || value.Length == 0) return null;
            if (value.Length != m_Vertices.Length)
            {
                Debug.LogError($"Mesh.{what} is out of bounds. The supplied array needs to be the same size as the Mesh.vertices array.");
                return null;
            }
            return (T[])value.Clone();
        }

        public Vector2[] uv { get => GetUV2(0); set => SetUV2(0, value); }
        public Vector2[] uv2 { get => GetUV2(1); set => SetUV2(1, value); }
        public Vector2[] uv3 { get => GetUV2(2); set => SetUV2(2, value); }
        public Vector2[] uv4 { get => GetUV2(3); set => SetUV2(3, value); }
        public Vector2[] uv5 { get => GetUV2(4); set => SetUV2(4, value); }
        public Vector2[] uv6 { get => GetUV2(5); set => SetUV2(5, value); }
        public Vector2[] uv7 { get => GetUV2(6); set => SetUV2(6, value); }
        public Vector2[] uv8 { get => GetUV2(7); set => SetUV2(7, value); }

        Vector2[] GetUV2(int channel)
        {
            var src = m_UVs[channel];
            if (src == null) return Array.Empty<Vector2>();
            var r = new Vector2[src.Length];
            for (int i = 0; i < r.Length; i++) r[i] = src[i];
            return r;
        }

        void SetUV2(int channel, Vector2[] value)
        {
            if (value == null || value.Length == 0) { m_UVs[channel] = null; m_UVDimensions[channel] = 0; Changed(); return; }
            if (value.Length != m_Vertices.Length)
            {
                Debug.LogError("Mesh.uv is out of bounds. The supplied array needs to be the same size as the Mesh.vertices array.");
                return;
            }
            var r = new Vector4[value.Length];
            for (int i = 0; i < r.Length; i++) r[i] = value[i];
            m_UVs[channel] = r;
            m_UVDimensions[channel] = 2;
            Changed();
        }

        public void SetVertices(List<Vector3> inVertices) => vertices = inVertices.ToArray();
        public void SetVertices(Vector3[] inVertices) => vertices = inVertices;
        public void SetVertices(List<Vector3> inVertices, int start, int length, [Internal.DefaultValue("MeshUpdateFlags.Default")] MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            vertices = inVertices.GetRange(start, length).ToArray();
        public void SetVertices(Vector3[] inVertices, int start, int length, [Internal.DefaultValue("MeshUpdateFlags.Default")] MeshUpdateFlags flags = MeshUpdateFlags.Default) =>
            vertices = new ArraySegment<Vector3>(inVertices, start, length).ToArray();

        public void GetVertices(List<Vector3> vertices)
        {
            vertices.Clear();
            vertices.AddRange(m_Vertices);
        }

        public void SetNormals(List<Vector3> inNormals) => normals = inNormals.ToArray();
        public void SetNormals(Vector3[] inNormals) => normals = inNormals;
        public void SetNormals(List<Vector3> inNormals, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => normals = inNormals.GetRange(start, length).ToArray();
        public void SetNormals(Vector3[] inNormals, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => normals = new ArraySegment<Vector3>(inNormals, start, length).ToArray();

        public void GetNormals(List<Vector3> normals)
        {
            normals.Clear();
            if (m_Normals != null) normals.AddRange(m_Normals);
        }

        public void SetTangents(List<Vector4> inTangents) => tangents = inTangents.ToArray();
        public void SetTangents(Vector4[] inTangents) => tangents = inTangents;
        public void SetTangents(List<Vector4> inTangents, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => tangents = inTangents.GetRange(start, length).ToArray();
        public void SetTangents(Vector4[] inTangents, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => tangents = new ArraySegment<Vector4>(inTangents, start, length).ToArray();

        public void GetTangents(List<Vector4> tangents)
        {
            tangents.Clear();
            if (m_Tangents != null) tangents.AddRange(m_Tangents);
        }

        public void SetColors(List<Color> inColors) => colors = inColors.ToArray();
        public void SetColors(Color[] inColors) => colors = inColors;
        public void SetColors(List<Color32> inColors) => colors32 = inColors.ToArray();
        public void SetColors(Color32[] inColors) => colors32 = inColors;
        public void SetColors(List<Color> inColors, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => colors = inColors.GetRange(start, length).ToArray();
        public void SetColors(Color[] inColors, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => colors = new ArraySegment<Color>(inColors, start, length).ToArray();
        public void SetColors(List<Color32> inColors, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => colors32 = inColors.GetRange(start, length).ToArray();
        public void SetColors(Color32[] inColors, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => colors32 = new ArraySegment<Color32>(inColors, start, length).ToArray();

        public void GetColors(List<Color> colors)
        {
            colors.Clear();
            colors.AddRange(this.colors);
        }

        public void GetColors(List<Color32> colors)
        {
            colors.Clear();
            if (m_Colors != null) colors.AddRange(m_Colors);
        }

        void SetUVsInternal(int channel, Vector4[] values, int dimension)
        {
            if (channel < 0 || channel > 7)
            {
                Debug.LogError("The uv index is invalid. Must be in the range 0 to 7.");
                return;
            }
            if (values == null || values.Length == 0)
            {
                m_UVs[channel] = null;
                m_UVDimensions[channel] = 0;
                Changed();
                return;
            }
            if (values.Length != m_Vertices.Length)
            {
                Debug.LogError("Mesh.uv is out of bounds. The supplied array needs to be the same size as the Mesh.vertices array.");
                return;
            }
            m_UVs[channel] = values;
            m_UVDimensions[channel] = dimension;
            Changed();
        }

        static Vector4[] Widen(IList<Vector2> list, int start, int length)
        {
            var r = new Vector4[length];
            for (int i = 0; i < length; i++) r[i] = list[start + i];
            return r;
        }

        static Vector4[] Widen(IList<Vector3> list, int start, int length)
        {
            var r = new Vector4[length];
            for (int i = 0; i < length; i++) r[i] = list[start + i];
            return r;
        }

        static Vector4[] Copy(IList<Vector4> list, int start, int length)
        {
            var r = new Vector4[length];
            for (int i = 0; i < length; i++) r[i] = list[start + i];
            return r;
        }

        public void SetUVs(int channel, List<Vector2> uvs) => SetUVsInternal(channel, Widen(uvs, 0, uvs.Count), 2);
        public void SetUVs(int channel, List<Vector3> uvs) => SetUVsInternal(channel, Widen(uvs, 0, uvs.Count), 3);
        public void SetUVs(int channel, List<Vector4> uvs) => SetUVsInternal(channel, Copy(uvs, 0, uvs.Count), 4);
        public void SetUVs(int channel, Vector2[] uvs) => SetUVsInternal(channel, Widen(uvs, 0, uvs.Length), 2);
        public void SetUVs(int channel, Vector3[] uvs) => SetUVsInternal(channel, Widen(uvs, 0, uvs.Length), 3);
        public void SetUVs(int channel, Vector4[] uvs) => SetUVsInternal(channel, Copy(uvs, 0, uvs.Length), 4);
        public void SetUVs(int channel, List<Vector2> uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Widen(uvs, start, length), 2);
        public void SetUVs(int channel, List<Vector3> uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Widen(uvs, start, length), 3);
        public void SetUVs(int channel, List<Vector4> uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Copy(uvs, start, length), 4);
        public void SetUVs(int channel, Vector2[] uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Widen(uvs, start, length), 2);
        public void SetUVs(int channel, Vector3[] uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Widen(uvs, start, length), 3);
        public void SetUVs(int channel, Vector4[] uvs, int start, int length, MeshUpdateFlags flags = MeshUpdateFlags.Default) => SetUVsInternal(channel, Copy(uvs, start, length), 4);

        public void GetUVs(int channel, List<Vector2> uvs)
        {
            uvs.Clear();
            if (m_UVs[channel] != null) foreach (var v in m_UVs[channel]) uvs.Add(v);
        }

        public void GetUVs(int channel, List<Vector3> uvs)
        {
            uvs.Clear();
            if (m_UVs[channel] != null) foreach (var v in m_UVs[channel]) uvs.Add(v);
        }

        public void GetUVs(int channel, List<Vector4> uvs)
        {
            uvs.Clear();
            if (m_UVs[channel] != null) uvs.AddRange(m_UVs[channel]);
        }

        public bool HasVertexAttribute(VertexAttribute attr)
        {
            switch (attr)
            {
                case VertexAttribute.Position: return m_Vertices.Length > 0;
                case VertexAttribute.Normal: return m_Normals != null;
                case VertexAttribute.Tangent: return m_Tangents != null;
                case VertexAttribute.Color: return m_Colors != null;
                case VertexAttribute.BlendWeight:
                case VertexAttribute.BlendIndices: return m_BoneWeights != null;
                default:
                    int channel = (int)attr - (int)VertexAttribute.TexCoord0;
                    return channel >= 0 && channel < 8 && m_UVs[channel] != null;
            }
        }

        public int GetVertexAttributeDimension(VertexAttribute attr)
        {
            switch (attr)
            {
                case VertexAttribute.Position: case VertexAttribute.Normal: return HasVertexAttribute(attr) ? 3 : 0;
                case VertexAttribute.Tangent: case VertexAttribute.Color: return HasVertexAttribute(attr) ? 4 : 0;
                default:
                    int channel = (int)attr - (int)VertexAttribute.TexCoord0;
                    return channel >= 0 && channel < 8 ? m_UVDimensions[channel] : 0;
            }
        }

        // ------------------------------------------------------------------ indices

        public int[] triangles
        {
            get
            {
                var all = new List<int>();
                foreach (var s in m_SubMeshes)
                    if (s.Topology == MeshTopology.Triangles)
                        foreach (var i in s.Indices) all.Add(i + s.BaseVertex);
                return all.ToArray();
            }
            set
            {
                subMeshCount = 1;
                SetTriangles(value, 0);
            }
        }

        public int[] GetTriangles(int submesh) => GetTriangles(submesh, true);

        public int[] GetTriangles(int submesh, [Internal.DefaultValue("true")] bool applyBaseVertex)
        {
            CheckSubmesh(submesh);
            var s = m_SubMeshes[submesh];
            if (s.Topology != MeshTopology.Triangles) return Array.Empty<int>();
            var r = (int[])s.Indices.Clone();
            if (applyBaseVertex && s.BaseVertex != 0) for (int i = 0; i < r.Length; i++) r[i] += s.BaseVertex;
            return r;
        }

        public void GetTriangles(List<int> triangles, int submesh) => GetTriangles(triangles, submesh, true);

        public void GetTriangles(List<int> triangles, int submesh, [Internal.DefaultValue("true")] bool applyBaseVertex)
        {
            triangles.Clear();
            triangles.AddRange(GetTriangles(submesh, applyBaseVertex));
        }

        public int[] GetIndices(int submesh) => GetIndices(submesh, true);

        public int[] GetIndices(int submesh, [Internal.DefaultValue("true")] bool applyBaseVertex)
        {
            CheckSubmesh(submesh);
            var s = m_SubMeshes[submesh];
            var r = (int[])s.Indices.Clone();
            if (applyBaseVertex && s.BaseVertex != 0) for (int i = 0; i < r.Length; i++) r[i] += s.BaseVertex;
            return r;
        }

        public void GetIndices(List<int> indices, int submesh) => GetIndices(indices, submesh, true);

        public void GetIndices(List<int> indices, int submesh, [Internal.DefaultValue("true")] bool applyBaseVertex)
        {
            indices.Clear();
            indices.AddRange(GetIndices(submesh, applyBaseVertex));
        }

        public uint GetIndexStart(int submesh)
        {
            uint start = 0;
            for (int i = 0; i < submesh; i++) start += (uint)m_SubMeshes[i].Indices.Length;
            return start;
        }

        public uint GetIndexCount(int submesh) => (uint)m_SubMeshes[submesh].Indices.Length;

        public uint GetBaseVertex(int submesh) => (uint)m_SubMeshes[submesh].BaseVertex;

        void CheckSubmesh(int submesh)
        {
            if (submesh < 0 || submesh >= m_SubMeshes.Count)
                throw new IndexOutOfRangeException($"Specified sub mesh ({submesh}) is out of range. Must be greater or equal to 0 and less than subMeshCount ({m_SubMeshes.Count}).");
        }

        public void SetTriangles(int[] triangles, int submesh) => SetTriangles(triangles, submesh, true, 0);
        public void SetTriangles(int[] triangles, int submesh, [Internal.DefaultValue("true")] bool calculateBounds) => SetTriangles(triangles, submesh, calculateBounds, 0);

        public void SetTriangles(int[] triangles, int submesh, [Internal.DefaultValue("true")] bool calculateBounds, [Internal.DefaultValue("0")] int baseVertex) =>
            SetIndices(triangles, MeshTopology.Triangles, submesh, calculateBounds, baseVertex);

        public void SetTriangles(int[] triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(new ArraySegment<int>(triangles, trianglesStart, trianglesLength).ToArray(), MeshTopology.Triangles, submesh, calculateBounds, baseVertex);

        public void SetTriangles(List<int> triangles, int submesh) => SetTriangles(triangles.ToArray(), submesh, true, 0);
        public void SetTriangles(List<int> triangles, int submesh, [Internal.DefaultValue("true")] bool calculateBounds) => SetTriangles(triangles.ToArray(), submesh, calculateBounds, 0);
        public void SetTriangles(List<int> triangles, int submesh, [Internal.DefaultValue("true")] bool calculateBounds, [Internal.DefaultValue("0")] int baseVertex) => SetTriangles(triangles.ToArray(), submesh, calculateBounds, baseVertex);
        public void SetTriangles(List<int> triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetTriangles(triangles.GetRange(trianglesStart, trianglesLength).ToArray(), submesh, calculateBounds, baseVertex);

        public void SetTriangles(ushort[] triangles, int submesh, bool calculateBounds = true, int baseVertex = 0)
        {
            var r = new int[triangles.Length];
            for (int i = 0; i < r.Length; i++) r[i] = triangles[i];
            SetTriangles(r, submesh, calculateBounds, baseVertex);
        }

        public void SetTriangles(List<ushort> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetTriangles(triangles.ToArray(), submesh, calculateBounds, baseVertex);

        public void SetIndices(int[] indices, MeshTopology topology, int submesh) => SetIndices(indices, topology, submesh, true, 0);
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, [Internal.DefaultValue("true")] bool calculateBounds) => SetIndices(indices, topology, submesh, calculateBounds, 0);

        public void SetIndices(int[] indices, MeshTopology topology, int submesh, [Internal.DefaultValue("true")] bool calculateBounds, [Internal.DefaultValue("0")] int baseVertex)
        {
            CheckSubmesh(submesh);
            if (indices == null) throw new ArgumentNullException(nameof(indices), "indices can't be null");
            foreach (var i in indices)
            {
                if (i + baseVertex < 0 || i + baseVertex >= m_Vertices.Length)
                {
                    Debug.LogError("Failed setting triangles. Some indices are referencing out of bounds vertices.");
                    return;
                }
            }
            var s = m_SubMeshes[submesh];
            s.Indices = (int[])indices.Clone();
            s.Topology = topology;
            s.BaseVertex = baseVertex;
            if (calculateBounds) RecalculateBounds();
            Changed();
        }

        public void SetIndices(int[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(new ArraySegment<int>(indices, indicesStart, indicesLength).ToArray(), topology, submesh, calculateBounds, baseVertex);

        public void SetIndices(ushort[] indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0)
        {
            var r = new int[indices.Length];
            for (int i = 0; i < r.Length; i++) r[i] = indices[i];
            SetIndices(r, topology, submesh, calculateBounds, baseVertex);
        }

        public void SetIndices(List<int> indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) =>
            SetIndices(indices.ToArray(), topology, submesh, calculateBounds, baseVertex);

        public void SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default)
        {
            CheckSubmesh(index);
            Changed();
        }

        public SubMeshDescriptor GetSubMesh(int index)
        {
            CheckSubmesh(index);
            var s = m_SubMeshes[index];
            return new SubMeshDescriptor((int)GetIndexStart(index), s.Indices.Length, s.Topology) { baseVertex = s.BaseVertex, vertexCount = m_Vertices.Length };
        }

        // ------------------------------------------------------------------ skinning

        public BoneWeight[] boneWeights
        {
            get => m_BoneWeights != null ? (BoneWeight[])m_BoneWeights.Clone() : Array.Empty<BoneWeight>();
            set { m_BoneWeights = Checked(value, "boneWeights"); Changed(); }
        }

        public Matrix4x4[] bindposes
        {
            get => m_Bindposes != null ? (Matrix4x4[])m_Bindposes.Clone() : Array.Empty<Matrix4x4>();
            set => m_Bindposes = value != null ? (Matrix4x4[])value.Clone() : null;
        }

        public void GetBindposes(List<Matrix4x4> bindposes)
        {
            bindposes.Clear();
            if (m_Bindposes != null) bindposes.AddRange(m_Bindposes);
        }

        public void GetBoneWeights(List<BoneWeight> boneWeights)
        {
            boneWeights.Clear();
            if (m_BoneWeights != null) boneWeights.AddRange(m_BoneWeights);
        }

        // ------------------------------------------------------------------ operations

        public void Clear() => Clear(true);

        public void Clear([Internal.DefaultValue("true")] bool keepVertexLayout)
        {
            m_Vertices = Array.Empty<Vector3>();
            m_Normals = null;
            m_Tangents = null;
            m_Colors = null;
            for (int i = 0; i < 8; i++)
            {
                m_UVs[i] = null;
                if (!keepVertexLayout) m_UVDimensions[i] = 0;
            }
            m_BoneWeights = null;
            foreach (var s in m_SubMeshes) s.Indices = Array.Empty<int>();
            m_Bounds = default;
            Changed();
        }

        public void RecalculateBounds() => RecalculateBounds(MeshUpdateFlags.Default);

        public void RecalculateBounds([Internal.DefaultValue("MeshUpdateFlags.Default")] MeshUpdateFlags flags)
        {
            if (m_Vertices.Length == 0)
            {
                m_Bounds = default;
                return;
            }
            var min = m_Vertices[0];
            var max = m_Vertices[0];
            foreach (var v in m_Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            m_Bounds = b;
        }

        public void RecalculateNormals() => RecalculateNormals(MeshUpdateFlags.Default);

        public void RecalculateNormals([Internal.DefaultValue("MeshUpdateFlags.Default")] MeshUpdateFlags flags)
        {
            var normals = new Vector3[m_Vertices.Length];
            foreach (var s in m_SubMeshes)
            {
                if (s.Topology != MeshTopology.Triangles) continue;
                for (int i = 0; i + 2 < s.Indices.Length; i += 3)
                {
                    int a = s.Indices[i] + s.BaseVertex, b = s.Indices[i + 1] + s.BaseVertex, c = s.Indices[i + 2] + s.BaseVertex;
                    var n = Vector3.Cross(m_Vertices[b] - m_Vertices[a], m_Vertices[c] - m_Vertices[a]);
                    normals[a] += n;
                    normals[b] += n;
                    normals[c] += n;
                }
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            m_Normals = normals;
            Changed();
        }

        public void RecalculateTangents() => RecalculateTangents(MeshUpdateFlags.Default);

        public void RecalculateTangents([Internal.DefaultValue("MeshUpdateFlags.Default")] MeshUpdateFlags flags)
        {
            var uv = m_UVs[0];
            if (uv == null || m_Normals == null) return;
            var tan1 = new Vector3[m_Vertices.Length];
            var tan2 = new Vector3[m_Vertices.Length];
            foreach (var s in m_SubMeshes)
            {
                if (s.Topology != MeshTopology.Triangles) continue;
                for (int i = 0; i + 2 < s.Indices.Length; i += 3)
                {
                    int a = s.Indices[i] + s.BaseVertex, b = s.Indices[i + 1] + s.BaseVertex, c = s.Indices[i + 2] + s.BaseVertex;
                    Vector3 e1 = m_Vertices[b] - m_Vertices[a], e2 = m_Vertices[c] - m_Vertices[a];
                    float s1 = uv[b].x - uv[a].x, s2 = uv[c].x - uv[a].x, t1 = uv[b].y - uv[a].y, t2 = uv[c].y - uv[a].y;
                    float r = s1 * t2 - s2 * t1;
                    r = Mathf.Approximately(r, 0f) ? 0f : 1f / r;
                    var sdir = (e1 * t2 - e2 * t1) * r;
                    var tdir = (e2 * s1 - e1 * s2) * r;
                    tan1[a] += sdir; tan1[b] += sdir; tan1[c] += sdir;
                    tan2[a] += tdir; tan2[b] += tdir; tan2[c] += tdir;
                }
            }
            var tangents = new Vector4[m_Vertices.Length];
            for (int i = 0; i < tangents.Length; i++)
            {
                var n = m_Normals[i];
                var t = (tan1[i] - n * Vector3.Dot(n, tan1[i])).normalized;
                float w = Vector3.Dot(Vector3.Cross(n, tan1[i]), tan2[i]) < 0f ? -1f : 1f;
                tangents[i] = new Vector4(t.x, t.y, t.z, w);
            }
            m_Tangents = tangents;
            Changed();
        }

        public void RecalculateUVDistributionMetrics(float uvAreaThreshold = 1e-9f) { }

        public float GetUVDistributionMetric(int uvSetIndex) => 1f;

        public void MarkDynamic() { }

        public void MarkModified() => Changed();

        public void Optimize() { }

        public void OptimizeIndexBuffers() { }

        public void OptimizeReorderVertexBuffer() { }

        public void UploadMeshData(bool markNoLongerReadable)
        {
            Sync();
            if (markNoLongerReadable) m_IsReadable = false;
        }

        public void CombineMeshes(CombineInstance[] combine) => CombineMeshes(combine, true, true, false);

        public void CombineMeshes(CombineInstance[] combine, [Internal.DefaultValue("true")] bool mergeSubMeshes) => CombineMeshes(combine, mergeSubMeshes, true, false);

        public void CombineMeshes(CombineInstance[] combine, [Internal.DefaultValue("true")] bool mergeSubMeshes, [Internal.DefaultValue("true")] bool useMatrices) =>
            CombineMeshes(combine, mergeSubMeshes, useMatrices, false);

        public void CombineMeshes(CombineInstance[] combine, [Internal.DefaultValue("true")] bool mergeSubMeshes, [Internal.DefaultValue("true")] bool useMatrices, [Internal.DefaultValue("false")] bool hasLightmapData)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector4>();
            var colors = new List<Color32>();
            var submeshes = new List<List<int>>();
            foreach (var c in combine)
            {
                if (c.mesh == null) continue;
                int offset = positions.Count;
                var m = useMatrices ? c.transform : Matrix4x4.identity;
                var src = c.mesh;
                foreach (var v in src.m_Vertices) positions.Add(m.MultiplyPoint3x4(v));
                for (int i = 0; i < src.m_Vertices.Length; i++)
                {
                    normals.Add(src.m_Normals != null ? m.MultiplyVector(src.m_Normals[i]).normalized : Vector3.up);
                    uvs.Add(src.m_UVs[0] != null ? src.m_UVs[0][i] : Vector4.zero);
                    colors.Add(src.m_Colors != null ? src.m_Colors[i] : new Color32(255, 255, 255, 255));
                }
                int sub = Math.Max(0, c.subMeshIndex);
                var tris = src.GetTriangles(Math.Min(sub, src.subMeshCount - 1));
                if (mergeSubMeshes || submeshes.Count == 0)
                {
                    if (submeshes.Count == 0) submeshes.Add(new List<int>());
                }
                var target = mergeSubMeshes ? submeshes[0] : NewList(submeshes);
                foreach (var t in tris) target.Add(t + offset);
            }
            Clear(false);
            m_Vertices = positions.ToArray();
            m_Normals = normals.ToArray();
            m_UVs[0] = uvs.ToArray();
            m_UVDimensions[0] = 2;
            m_Colors = colors.ToArray();
            subMeshCount = Math.Max(1, submeshes.Count);
            for (int i = 0; i < submeshes.Count; i++) SetTriangles(submeshes[i], i);
            RecalculateBounds();
        }

        static List<int> NewList(List<List<int>> into)
        {
            var list = new List<int>();
            into.Add(list);
            return list;
        }

        // ------------------------------------------------------------------ renderer side

        /// <summary>Sends the mesh to the renderer when it changed; returns its handle.</summary>
        internal unsafe ulong Sync()
        {
            if (m_Handle == 0) m_Handle = RenderHandles.Next();
            if (!m_Dirty) return m_Handle;
            m_Dirty = false;
            int count = m_Vertices.Length;
            var indices = new List<uint>();
            var subs = new SubMeshPacket[m_SubMeshes.Count];
            for (int i = 0; i < m_SubMeshes.Count; i++)
            {
                var s = m_SubMeshes[i];
                subs[i] = new SubMeshPacket { IndexStart = indices.Count, IndexCount = s.Indices.Length, Topology = (int)s.Topology, BaseVertex = s.BaseVertex };
                foreach (var index in s.Indices) indices.Add((uint)index);
            }
            var indexArray = indices.ToArray();
            fixed (Vector3* positions = m_Vertices)
            fixed (Vector3* normals = m_Normals)
            fixed (Vector4* tangents = m_Tangents)
            fixed (Color32* colors = m_Colors)
            fixed (Vector4* uv0 = m_UVs[0])
            fixed (Vector4* uv1 = m_UVs[1])
            fixed (Vector4* uv2 = m_UVs[2])
            fixed (Vector4* uv3 = m_UVs[3])
            fixed (uint* idx = indexArray)
            fixed (SubMeshPacket* sub = subs)
            {
                var packet = new MeshPacket
                {
                    VertexCount = count,
                    Positions = positions,
                    Normals = normals,
                    Tangents = tangents,
                    Colors = colors,
                    Uv0 = uv0,
                    Uv1 = uv1,
                    Uv2 = uv2,
                    Uv3 = uv3,
                    IndexCount = indexArray.Length,
                    Indices = idx,
                    SubmeshCount = subs.Length,
                    Submeshes = sub,
                };
                Native.UploadMesh(m_Handle, &packet);
            }
            return m_Handle;
        }

        internal override void OnEngineDestroy()
        {
            Native.ReleaseMesh(m_Handle);
            m_Handle = 0;
        }
    }

    public partial struct CombineInstance
    {
        public Mesh mesh { get; set; }
        public int subMeshIndex { get; set; }
        public Matrix4x4 transform { get; set; }
        public Vector4 lightmapScaleOffset { get; set; }
        public Vector4 realtimeLightmapScaleOffset { get; set; }
    }

    [Serializable]
    public partial struct BoneWeight : IEquatable<BoneWeight>
    {
        [SerializeField] float m_Weight0;
        [SerializeField] float m_Weight1;
        [SerializeField] float m_Weight2;
        [SerializeField] float m_Weight3;
        [SerializeField] int m_BoneIndex0;
        [SerializeField] int m_BoneIndex1;
        [SerializeField] int m_BoneIndex2;
        [SerializeField] int m_BoneIndex3;

        public float weight0 { get => m_Weight0; set => m_Weight0 = value; }
        public float weight1 { get => m_Weight1; set => m_Weight1 = value; }
        public float weight2 { get => m_Weight2; set => m_Weight2 = value; }
        public float weight3 { get => m_Weight3; set => m_Weight3 = value; }
        public int boneIndex0 { get => m_BoneIndex0; set => m_BoneIndex0 = value; }
        public int boneIndex1 { get => m_BoneIndex1; set => m_BoneIndex1 = value; }
        public int boneIndex2 { get => m_BoneIndex2; set => m_BoneIndex2 = value; }
        public int boneIndex3 { get => m_BoneIndex3; set => m_BoneIndex3 = value; }

        public override int GetHashCode() => boneIndex0.GetHashCode() ^ (boneIndex1.GetHashCode() << 2) ^ (boneIndex2.GetHashCode() >> 2) ^ (boneIndex3.GetHashCode() >> 1) ^ (weight0.GetHashCode() << 5) ^ (weight1.GetHashCode() << 4) ^ (weight2.GetHashCode() >> 4) ^ (weight3.GetHashCode() >> 3);
        public override bool Equals(object other) => other is BoneWeight b && Equals(b);
        public bool Equals(BoneWeight other) => boneIndex0 == other.boneIndex0 && boneIndex1 == other.boneIndex1 && boneIndex2 == other.boneIndex2 && boneIndex3 == other.boneIndex3 && new Vector4(weight0, weight1, weight2, weight3).Equals(new Vector4(other.weight0, other.weight1, other.weight2, other.weight3));
        public static bool operator ==(BoneWeight lhs, BoneWeight rhs) => lhs.Equals(rhs);
        public static bool operator !=(BoneWeight lhs, BoneWeight rhs) => !lhs.Equals(rhs);
    }

    namespace Rendering
    {
        public partial struct SubMeshDescriptor
        {
            public SubMeshDescriptor(int indexStart, int indexCount, MeshTopology topology = MeshTopology.Triangles)
            {
                this.indexStart = indexStart;
                this.indexCount = indexCount;
                this.topology = topology;
                bounds = default;
                baseVertex = 0;
                firstVertex = 0;
                vertexCount = 0;
            }

            public Bounds bounds { get; set; }
            public MeshTopology topology { get; set; }
            public int indexStart { get; set; }
            public int indexCount { get; set; }
            public int baseVertex { get; set; }
            public int firstVertex { get; set; }
            public int vertexCount { get; set; }
        }
    }
}
