using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // A mesh in Unity's own serialization (a `.asset` mesh, an AssetBundle's mesh): the vertex data's
    // streams (each starting at a multiple of 16 bytes) of the channels Unity lists (position,
    // normal, tangent, color, eight texture coordinates, blend weights and indices), the index
    // buffer, the submeshes, the bind poses and the blend shapes (MeshBlendShapes.cs).
    public sealed partial class Mesh
    {
        static int FormatSize(int format) => format switch
        {
            0 => 4, // Float32
            1 => 2, // Float16
            2 or 3 or 6 or 7 => 1, // UNorm8, SNorm8, UInt8, SInt8
            4 or 5 or 8 or 9 => 2, // UNorm16, SNorm16, UInt16, SInt16
            _ => 4, // UInt32, SInt32
        };

        static float ReadComponent(byte[] data, int at, int format) => format switch
        {
            0 => BitConverter.ToSingle(data, at),
            1 => (float)BitConverter.ToHalf(data, at),
            2 => data[at] / 255f,
            3 => Math.Max((sbyte)data[at] / 127f, -1f),
            4 => BitConverter.ToUInt16(data, at) / 65535f,
            5 => Math.Max(BitConverter.ToInt16(data, at) / 32767f, -1f),
            6 => data[at],
            7 => (sbyte)data[at],
            8 => BitConverter.ToUInt16(data, at),
            9 => BitConverter.ToInt16(data, at),
            10 => BitConverter.ToUInt32(data, at),
            _ => BitConverter.ToInt32(data, at),
        };

        static byte[] Hex(string hex) => string.IsNullOrEmpty(hex) ? Array.Empty<byte>() : Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex.Substring(0, hex.Length - 1));

        static Vector3 Vec3(YNode n) => n == null ? Vector3.zero : new Vector3(n.Float("x"), n.Float("y"), n.Float("z"));

        /// <summary>Reads Unity's serialized mesh; false when the body is not one.</summary>
        internal bool ReadUnitySerialized(YNode body)
        {
            var vertexData = body.Get("m_VertexData");
            if (vertexData == null) return false;
            if (body.Int("m_MeshCompression") != 0)
                Debug.LogWarning($"Unbound: mesh '{m_Name}' is stored compressed (Mesh Compression), which is not supported yet; it stays empty.");
            int count = vertexData.Int("m_VertexCount");
            var data = Hex(vertexData.Str("_typelessdata"));
            var channels = new List<(int Stream, int Offset, int Format, int Dimension)>();
            foreach (var c in vertexData.Get("m_Channels")?.Seq() ?? Array.Empty<YNode>())
                channels.Add((c.Int("stream"), c.Int("offset"), c.Int("format"), c.Int("dimension") & 0xF));
            // The streams: each channel's stream's stride, the streams one after the other, each at a
            // multiple of 16 bytes.
            var strides = new int[8];
            foreach (var c in channels)
                if (c.Dimension > 0) strides[c.Stream] = Math.Max(strides[c.Stream], c.Offset + FormatSize(c.Format) * c.Dimension);
            var starts = new int[8];
            int at = 0;
            for (int s = 0; s < 8; s++)
            {
                starts[s] = at;
                if (strides[s] > 0) at = (at + strides[s] * count + 15) & ~15;
            }
            float[] Channel(int index)
            {
                if (index >= channels.Count) return null;
                var c = channels[index];
                if (c.Dimension == 0) return null;
                var values = new float[count * c.Dimension];
                int size = FormatSize(c.Format);
                for (int v = 0; v < count; v++)
                {
                    int p = starts[c.Stream] + v * strides[c.Stream] + c.Offset;
                    for (int k = 0; k < c.Dimension; k++)
                        if (p + (k + 1) * size <= data.Length) values[v * c.Dimension + k] = ReadComponent(data, p + k * size, c.Format);
                }
                return values;
            }
            int Dim(int index) => index < channels.Count ? channels[index].Dimension : 0;

            var positions = Channel(0);
            var verts = new Vector3[count];
            if (positions != null)
                for (int v = 0; v < count; v++)
                    verts[v] = new Vector3(positions[v * Dim(0)], Dim(0) > 1 ? positions[v * Dim(0) + 1] : 0f, Dim(0) > 2 ? positions[v * Dim(0) + 2] : 0f);
            vertices = verts;
            var normalValues = Channel(1);
            if (normalValues != null)
            {
                var n = new Vector3[count];
                for (int v = 0; v < count; v++) n[v] = new Vector3(normalValues[v * 3], normalValues[v * 3 + 1], normalValues[v * 3 + 2]);
                normals = n;
            }
            var tangentValues = Channel(2);
            if (tangentValues != null)
            {
                var t = new Vector4[count];
                for (int v = 0; v < count; v++) t[v] = new Vector4(tangentValues[v * 4], tangentValues[v * 4 + 1], tangentValues[v * 4 + 2], Dim(2) > 3 ? tangentValues[v * 4 + 3] : 1f);
                tangents = t;
            }
            var colorValues = Channel(3);
            if (colorValues != null)
            {
                var c = new Color[count];
                int d = Dim(3);
                for (int v = 0; v < count; v++)
                    c[v] = new Color(colorValues[v * d], d > 1 ? colorValues[v * d + 1] : 0f, d > 2 ? colorValues[v * d + 2] : 0f, d > 3 ? colorValues[v * d + 3] : 1f);
                colors = c;
            }
            for (int uv = 0; uv < 8; uv++)
            {
                var values = Channel(4 + uv);
                if (values == null) continue;
                int d = Dim(4 + uv);
                var list = new List<Vector4>(count);
                for (int v = 0; v < count; v++)
                    list.Add(new Vector4(values[v * d], d > 1 ? values[v * d + 1] : 0f, d > 2 ? values[v * d + 2] : 0f, d > 3 ? values[v * d + 3] : 0f));
                if (d <= 2)
                {
                    var two = new List<Vector2>(count);
                    foreach (var x in list) two.Add(x);
                    SetUVs(uv, two);
                }
                else if (d == 3)
                {
                    var three = new List<Vector3>(count);
                    foreach (var x in list) three.Add(x);
                    SetUVs(uv, three);
                }
                else SetUVs(uv, list);
            }
            var weights = Channel(12);
            var bones = Channel(13);
            if (weights != null && bones != null)
            {
                int dw = Dim(12), db = Dim(13);
                var bw = new BoneWeight[count];
                for (int v = 0; v < count; v++)
                {
                    float W(int k) => k < dw ? weights[v * dw + k] : (k == 0 && dw == 0 ? 1f : 0f);
                    int B(int k) => k < db ? (int)bones[v * db + k] : 0;
                    bw[v] = new BoneWeight
                    {
                        weight0 = dw == 0 ? 1f : W(0), weight1 = W(1), weight2 = W(2), weight3 = W(3),
                        boneIndex0 = B(0), boneIndex1 = B(1), boneIndex2 = B(2), boneIndex3 = B(3),
                    };
                }
                boneWeights = bw;
            }

            // Indices and submeshes.
            var indexBytes = Hex(body.Str("m_IndexBuffer"));
            bool wide = body.Int("m_IndexFormat") == 1;
            int indexSize = wide ? 4 : 2;
            var submeshes = new List<YNode>(body.Get("m_SubMeshes")?.Seq() ?? Array.Empty<YNode>());
            subMeshCount = Math.Max(1, submeshes.Count);
            if (wide) indexFormat = IndexFormat.UInt32;
            for (int s = 0; s < submeshes.Count; s++)
            {
                var sm = submeshes[s];
                int first = sm.Int("firstByte") / indexSize, n = sm.Int("indexCount");
                var indices = new int[Math.Max(0, Math.Min(n, indexBytes.Length / indexSize - first))];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = wide ? (int)BitConverter.ToUInt32(indexBytes, (first + i) * 4) : BitConverter.ToUInt16(indexBytes, (first + i) * 2);
                var topology = sm.Int("topology") switch
                {
                    2 => MeshTopology.Quads,
                    3 => MeshTopology.Lines,
                    4 => MeshTopology.LineStrip,
                    5 => MeshTopology.Points,
                    _ => MeshTopology.Triangles,
                };
                SetIndices(indices, topology, s, false, sm.Int("baseVertex"));
            }

            var poses = new List<Matrix4x4>();
            foreach (var m in body.Get("m_BindPose")?.Seq() ?? Array.Empty<YNode>())
            {
                var matrix = new Matrix4x4();
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        matrix[r, c] = m.Float($"e{r}{c}");
                poses.Add(matrix);
            }
            if (poses.Count > 0) bindposes = poses.ToArray();

            ReadBlendShapes(body.Get("m_Shapes"), count);

            var aabb = body.Get("m_LocalAABB");
            if (aabb != null) bounds = new Bounds(Vec3(aabb.Get("m_Center")), Vec3(aabb.Get("m_Extent")) * 2f);
            m_IsReadable = body.Bool("m_IsReadable", true);
            return true;
        }
    }
}
