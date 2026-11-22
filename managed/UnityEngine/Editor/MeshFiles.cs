using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    internal static partial class UnityYamlWriter
    {
        internal sealed partial class Writer
        {
            /// <summary>The mesh's serialized version in the release's files.</summary>
            const int MeshSerializedVersion = 11;

            /// <summary>The vertex channels a mesh file lists: Unity's vertex attributes, in their order.</summary>
            const int VertexChannelCount = (int)VertexAttribute.BlendIndices + 1;

            /// <summary>Each vertex stream of a mesh file starts at a multiple of this many bytes.</summary>
            const int StreamAlignment = 16;

            /// <summary>
            /// A readable mesh's whole body: its submeshes with their index ranges, vertex ranges and
            /// bounds; blend shapes (only the vertices a frame moves, its channels named by their CRC32);
            /// bind poses and each bone's bounds in its bind pose's space (of the vertices it weighs);
            /// its indices; its vertex data as its layout has it, channel by channel in attribute
            /// order, the streams one after the other each at a multiple of 16 bytes; its bounds
            /// (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave engine-assets Rich,
            /// Shaped, Skinned, Custom, Lines, Keep*, create-assets Tri).
            /// </summary>
            internal void MeshBody(Mesh mesh)
            {
                ObjectHeader(mesh);
                String("m_Name", mesh.name);
                Int("serializedVersion", MeshSerializedVersion);
                SubMeshes(mesh);
                BlendShapes(mesh);
                var poses = mesh.bindposes;
                if (poses.Length == 0) Line(1, "m_BindPose", "[]");
                else
                {
                    Key("m_BindPose", 1);
                    foreach (var pose in poses)
                        for (int r = 0; r < 4; r++)
                            for (int c = 0; c < 4; c++)
                            {
                                var key = "e" + r.ToString(CultureInfo.InvariantCulture) + c.ToString(CultureInfo.InvariantCulture);
                                if (r == 0 && c == 0) Item(key, F(pose[r, c]), 1);
                                else Float(key, pose[r, c], 2);
                            }
                }
                // A model's mesh names its bones (their paths' CRC32); a mesh made by script none.
                var hashes = mesh.m_BoneNameHashes ?? Array.Empty<uint>();
                var hashBytes = new byte[hashes.Length * 4];
                for (int i = 0; i < hashes.Length; i++) BitConverter.TryWriteBytes(hashBytes.AsSpan(i * 4, 4), hashes[i]);
                Line(1, "m_BoneNameHashes", Convert.ToHexString(hashBytes).ToLowerInvariant());
                Int("m_RootBoneNameHash", mesh.m_RootBoneNameHash);
                BonesBounds(mesh, poses);
                Key("m_VariableBoneCountWeights", 1);
                Line(2, "m_Data", "");
                Int("m_MeshCompression", 0);
                Bool("m_IsReadable", mesh.isReadable);
                bool wide = mesh.indexFormat == IndexFormat.UInt32;
                // Kept with 16-bit indices, not with 32-bit ones, however the format was set
                // (engine-assets KeepWide, KeepWideParams, KeepNarrow, Rich).
                Bool("m_KeepVertices", !wide);
                Bool("m_KeepIndices", !wide);
                Int("m_IndexFormat", wide ? 1 : 0);
                Line(1, "m_IndexBuffer", IndexBufferHex(mesh, wide));
                VertexData(mesh);
                m_Out.Append(EmptyCompressedMesh);
                Key("m_LocalAABB", 1);
                Vector3("m_Center", mesh.bounds.center, 2);
                Vector3("m_Extent", mesh.bounds.extents, 2);
                Int("m_MeshUsageFlags", 0);
                Int("m_CookingOptions", (int)DefaultCookingOptions);
                Line(1, "m_BakedConvexCollisionMesh", "");
                Line(1, "m_BakedTriangleCollisionMesh", "");
                // The UV distribution metrics of the first two channels: 1, not computed.
                Line(1, "'m_MeshMetrics[0]'", "1");
                Line(1, "'m_MeshMetrics[1]'", "1");
                Int("m_MeshOptimizationFlags", 1);
                StreamData();
            }

            /// <summary>A new mesh's collider cooking options (CookForFasterSimulation,
            /// EnableMeshCleaning, WeldColocatedVertices, UseFastMidphase).</summary>
            const MeshColliderCookingOptions DefaultCookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

            void SubMeshes(Mesh mesh)
            {
                int size = mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2;
                Key("m_SubMeshes", 1);
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var sub = mesh.GetSubMesh(i);
                    Item("serializedVersion", "2", 1);
                    Int("firstByte", (long)sub.indexStart * size, 2);
                    Int("indexCount", sub.indexCount, 2);
                    Int("topology", (int)sub.topology, 2);
                    Int("baseVertex", sub.baseVertex, 2);
                    Int("firstVertex", sub.firstVertex, 2);
                    Int("vertexCount", sub.vertexCount, 2);
                    Key("localAABB", 2);
                    Vector3("m_Center", sub.bounds.center, 3);
                    Vector3("m_Extent", sub.bounds.extents, 3);
                }
            }

            void BlendShapes(Mesh mesh)
            {
                Key("m_Shapes", 1);
                var deltas = new List<(Vector3 Vertex, Vector3 Normal, Vector3 Tangent, int Index)>();
                var frames = new List<(int First, int Count, bool Normals, bool Tangents, float Weight)>();
                var channels = new List<(string Name, int First, int Count)>();
                foreach (var channel in mesh.m_BlendShapes)
                {
                    channels.Add((channel.Name, frames.Count, channel.Frames.Count));
                    foreach (var frame in channel.Frames)
                    {
                        int first = deltas.Count;
                        for (int v = 0; v < frame.Vertices.Length; v++)
                        {
                            var vertex = frame.Vertices[v];
                            var normal = frame.Normals != null ? frame.Normals[v] : default;
                            var tangent = frame.Tangents != null ? frame.Tangents[v] : default;
                            if (vertex != default || normal != default || tangent != default) deltas.Add((vertex, normal, tangent, v));
                        }
                        frames.Add((first, deltas.Count - first, frame.Normals != null, frame.Tangents != null, frame.Weight));
                    }
                }
                if (deltas.Count == 0) Line(2, "vertices", "[]");
                else
                {
                    Key("vertices", 2);
                    foreach (var d in deltas)
                    {
                        Item("vertex", ReferenceFreeFlow(d.Vertex), 2);
                        Vector3("normal", d.Normal, 3);
                        Vector3("tangent", d.Tangent, 3);
                        Int("index", d.Index, 3);
                    }
                }
                if (frames.Count == 0) Line(2, "shapes", "[]");
                else
                {
                    Key("shapes", 2);
                    foreach (var f in frames)
                    {
                        Item("firstVertex", f.First.ToString(CultureInfo.InvariantCulture), 2);
                        Int("vertexCount", f.Count, 3);
                        Bool("hasNormals", f.Normals, 3);
                        Bool("hasTangents", f.Tangents, 3);
                    }
                }
                if (channels.Count == 0) Line(2, "channels", "[]");
                else
                {
                    Key("channels", 2);
                    foreach (var c in channels)
                    {
                        Item("name", Scalar(c.Name), 2);
                        Int("nameHash", HashedBindings.Crc32(c.Name), 3);
                        Int("frameIndex", c.First, 3);
                        Int("frameCount", c.Count, 3);
                    }
                }
                if (frames.Count == 0) Line(2, "fullWeights", "[]");
                else
                {
                    Key("fullWeights", 2);
                    foreach (var f in frames) SequenceValue(2, F(f.Weight));
                }
            }

            string ReferenceFreeFlow(Vector3 v) =>
                "{x: " + F(v.x) + ", y: " + F(v.y) + ", z: " + F(v.z) + "}";

            /// <summary>Each bone's bounds: the vertices with a weight of it, in its bind pose's space.</summary>
            void BonesBounds(Mesh mesh, Matrix4x4[] poses)
            {
                if (poses.Length == 0)
                {
                    Line(1, "m_BonesAABB", "[]");
                    return;
                }
                var weights = mesh.boneWeights;
                var vertices = mesh.vertices;
                var lo = new Vector3[poses.Length];
                var hi = new Vector3[poses.Length];
                var any = new bool[poses.Length];
                void Add(int bone, float weight, int v)
                {
                    if (weight <= 0f || bone < 0 || bone >= poses.Length) return;
                    var p = poses[bone].MultiplyPoint3x4(vertices[v]);
                    lo[bone] = any[bone] ? UnityEngine.Vector3.Min(lo[bone], p) : p;
                    hi[bone] = any[bone] ? UnityEngine.Vector3.Max(hi[bone], p) : p;
                    any[bone] = true;
                }
                for (int v = 0; v < weights.Length && v < vertices.Length; v++)
                {
                    var w = weights[v];
                    Add(w.boneIndex0, w.weight0, v);
                    Add(w.boneIndex1, w.weight1, v);
                    Add(w.boneIndex2, w.weight2, v);
                    Add(w.boneIndex3, w.weight3, v);
                }
                Key("m_BonesAABB", 1);
                for (int b = 0; b < poses.Length; b++)
                {
                    Item("m_Min", ReferenceFreeFlow(lo[b]), 1);
                    Vector3("m_Max", hi[b], 2);
                }
            }

            static string IndexBufferHex(Mesh mesh, bool wide)
            {
                int count = mesh.IndexBufferLength;
                var bytes = new byte[count * (wide ? 4 : 2)];
                for (int i = 0; i < count; i++)
                {
                    uint index = (uint)mesh.IndexBufferAt(i);
                    if (wide) BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), index);
                    else BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), (ushort)index);
                }
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }

            void VertexData(Mesh mesh)
            {
                int count = mesh.vertexCount;
                var layout = mesh.Layout();
                var strides = new int[VertexChannelCount];
                foreach (var d in layout) strides[d.stream] = Math.Max(strides[d.stream], mesh.GetVertexAttributeOffset(d.attribute) + Mesh.FormatSize(d.format) * d.dimension);
                var starts = new int[VertexChannelCount];
                int size = 0;
                for (int s = 0; s < strides.Length; s++)
                {
                    if (strides[s] == 0) continue;
                    size = (size + StreamAlignment - 1) / StreamAlignment * StreamAlignment;
                    starts[s] = size;
                    size += strides[s] * count;
                }
                var data = new byte[size];
                foreach (var d in layout)
                {
                    int offset = mesh.GetVertexAttributeOffset(d.attribute), width = Mesh.FormatSize(d.format) * d.dimension;
                    for (int v = 0; v < count; v++)
                        mesh.PackAttribute(d, v, data.AsSpan(starts[d.stream] + v * strides[d.stream] + offset, width));
                }
                Key("m_VertexData", 1);
                Int("serializedVersion", 3, 2);
                Int("m_VertexCount", count, 2);
                Key("m_Channels", 2);
                for (int a = 0; a < VertexChannelCount; a++)
                {
                    VertexAttributeDescriptor? found = null;
                    foreach (var d in layout) if ((int)d.attribute == a) found = d;
                    var c = found ?? default;
                    Item("stream", (found.HasValue ? c.stream : 0).ToString(CultureInfo.InvariantCulture), 2);
                    Int("offset", found.HasValue ? mesh.GetVertexAttributeOffset(c.attribute) : 0, 3);
                    Int("format", found.HasValue ? (int)c.format : 0, 3);
                    Int("dimension", found.HasValue ? c.dimension : 0, 3);
                }
                Int("m_DataSize", size, 2);
                Line(2, "_typelessdata", Convert.ToHexString(data).ToLowerInvariant());
            }

            /// <summary>A mesh's compressed data when it is stored uncompressed: every part empty.</summary>
            const string EmptyCompressedMesh =
                "  m_CompressedMesh:\n" +
                "    m_Vertices:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_UV:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_Normals:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_Tangents:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_Weights:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_NormalSigns:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_TangentSigns:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_FloatColors:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_BoneIndices:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_Triangles:\n      m_NumItems: 0\n      m_Data: \n      m_BitSize: 0\n" +
                "    m_UVInfo: 0\n";
        }
    }
}
