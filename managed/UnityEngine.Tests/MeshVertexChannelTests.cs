using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // Documented basis: Unity 6000.0 Mesh.vertices resizes existing vertex attributes.
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-vertices.html
    // Padding values and channel layout after a zero-sized assignment are not Native claims.
    // These tests never upload a mesh, so the old-Core red control cannot read past an array.
    public class MeshVertexChannelTests
    {
        static Mesh WithChannels(int count)
        {
            var mesh = new Mesh
            {
                vertices = new Vector3[count],
                normals = new Vector3[count],
                tangents = new Vector4[count],
                colors32 = new Color32[count],
                boneWeights = new BoneWeight[count],
            };
            for (int channel = 0; channel < 8; channel++)
            {
                int dimension = 2 + channel % 3;
                if (dimension == 2) mesh.SetUVs(channel, new Vector2[count]);
                else if (dimension == 3) mesh.SetUVs(channel, new Vector3[count]);
                else mesh.SetUVs(channel, new Vector4[count]);
            }
            return mesh;
        }

        static void AssertChannelCounts(Mesh mesh, int count)
        {
            Assert.Equal(count, mesh.vertexCount);
            Assert.Equal(count, mesh.normals.Length);
            Assert.Equal(count, mesh.tangents.Length);
            Assert.Equal(count, mesh.colors.Length);
            Assert.Equal(count, mesh.colors32.Length);
            Assert.Equal(count, mesh.boneWeights.Length);
            var values = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                mesh.GetUVs(channel, values);
                Assert.Equal(count, values.Count);
                Assert.Equal(2 + channel % 3,
                    mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)));
            }
        }

        [Fact]
        public void GrowingPositionsResizesEveryExistingVertexChannel()
        {
            var mesh = WithChannels(3);
            try
            {
                mesh.vertices = new Vector3[300];
                AssertChannelCounts(mesh, 300);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ShrinkingPositionsResizesEveryExistingVertexChannel()
        {
            var mesh = WithChannels(5);
            try
            {
                mesh.vertices = new Vector3[2];
                AssertChannelCounts(mesh, 2);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void EmptyPositionsResizeChannelsAndCanBeReplacedAgain()
        {
            var mesh = WithChannels(3);
            try
            {
                mesh.vertices = null;
                AssertChannelCounts(mesh, 0);
                // Unbound layout-preservation control, not a measured zero-vertex layout rule.
                mesh.vertices = new Vector3[4];
                AssertChannelCounts(mesh, 4);
                mesh.vertices = Array.Empty<Vector3>();
                AssertChannelCounts(mesh, 0);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void SetVerticesListArrayAndRangeOverloadsResizeChannels()
        {
            var mesh = WithChannels(3);
            try
            {
                mesh.SetVertices(new List<Vector3>(new Vector3[5]));
                AssertChannelCounts(mesh, 5);
                mesh.SetVertices(new Vector3[7], 2, 3);
                AssertChannelCounts(mesh, 3);
                mesh.SetVertices(new List<Vector3>(new Vector3[9]), 1, 4);
                AssertChannelCounts(mesh, 4);
                mesh.SetVertices(new Vector3[2]);
                AssertChannelCounts(mesh, 2);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ResizingPositionsKeepsMissingChannelsAbsentAndBindposesSeparate()
        {
            var mesh = new Mesh { vertices = new Vector3[3], bindposes = new[] { Matrix4x4.identity } };
            try
            {
                foreach (int count in new[] { 8, 0, 2 })
                {
                    mesh.vertices = new Vector3[count];
                    Assert.Empty(mesh.normals);
                    Assert.Empty(mesh.tangents);
                    Assert.Empty(mesh.colors32);
                    Assert.Empty(mesh.boneWeights);
                    Assert.False(mesh.HasVertexAttribute(VertexAttribute.Normal));
                    Assert.False(mesh.HasVertexAttribute(VertexAttribute.Tangent));
                    Assert.False(mesh.HasVertexAttribute(VertexAttribute.Color));
                    var values = new List<Vector4>();
                    for (int channel = 0; channel < 8; channel++)
                    {
                        mesh.GetUVs(channel, values);
                        Assert.Empty(values);
                        Assert.False(mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)));
                    }
                    Assert.Equal(new[] { Matrix4x4.identity }, mesh.bindposes);
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void SameCountAssignmentCopiesPositionsAndKeepsExistingChannelValues()
        {
            var mesh = WithChannels(3);
            try
            {
                mesh.normals = new[] { Vector3.up, Vector3.forward, Vector3.right };
                mesh.tangents = new[] { Vector4.one, Vector4.zero, new Vector4(1, 2, 3, 4) };
                mesh.colors32 = new[] { new Color32(10, 20, 30, 40), new Color32(50, 60, 70, 80), default };
                mesh.uv8 = new[] { Vector2.one, Vector2.zero, Vector2.right };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 2, weight0 = 1 }, default, default };
                var normals = mesh.normals;
                var tangents = mesh.tangents;
                var colors = mesh.colors32;
                var uv = mesh.uv8;
                var weights = mesh.boneWeights;
                var positions = new[] { Vector3.one, Vector3.zero, Vector3.up };
                mesh.vertices = positions;
                positions[0] = Vector3.right;
                Assert.Equal(Vector3.one, mesh.vertices[0]);
                Assert.Equal(normals, mesh.normals);
                Assert.Equal(tangents, mesh.tangents);
                Assert.Equal(colors, mesh.colors32);
                Assert.Equal(uv, mesh.uv8);
                Assert.Equal(weights, mesh.boneWeights);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void PositionAssignmentResizesChannelsCreatedByRawBufferLayout()
        {
            var mesh = new Mesh();
            try
            {
                mesh.SetVertexBufferParams(3,
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.Normal),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, dimension: 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord7, dimension: 4),
                    new VertexAttributeDescriptor(VertexAttribute.BlendWeight, dimension: 4));
                var layout = mesh.GetVertexAttributes();
                mesh.vertices = new Vector3[7];
                Assert.Equal(7, mesh.normals.Length);
                Assert.Equal(7, mesh.colors32.Length);
                Assert.Equal(7, mesh.boneWeights.Length);
                Assert.Equal(7, mesh.uv.Length);
                Assert.Equal(7, mesh.uv2.Length);
                Assert.Equal(7, mesh.uv8.Length);
                Assert.Equal(layout, mesh.GetVertexAttributes());
                Assert.Empty(mesh.tangents);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }

    // Internal upload-boundary controls. NEVER run this class on an old Core: it intentionally
    // constructs invalid internal arrays, and an old native-connected upload can read past them.
    public class MeshUploadInvariantTests
    {
        static (string Name, Action<Mesh> Corrupt)[] Corruptions()
        {
            var cases = new List<(string, Action<Mesh>)>
            {
                ("normals", mesh => mesh.m_Normals = new Vector3[1]),
                ("tangents", mesh => mesh.m_Tangents = new Vector4[1]),
                ("colors", mesh => mesh.m_Colors = new Color32[1]),
                ("boneWeights", mesh => mesh.m_BoneWeights = new BoneWeight[1]),
            };
            for (int channel = 0; channel < 8; channel++)
            {
                int uv = channel;
                cases.Add(("uv" + channel, mesh => mesh.m_UVs[uv] = new Vector4[1]));
            }
            return cases.ToArray();
        }

        [Fact]
        public void ShortChannelsAreRejectedBeforeAllocatingAnUploadHandle()
        {
            foreach (var (name, corrupt) in Corruptions())
            {
                var mesh = new Mesh { vertices = new Vector3[3] };
                try
                {
                    corrupt(mesh);
                    var exception = Assert.Throws<InvalidOperationException>(() => mesh.UploadMeshData(false));
                    Assert.Contains(name, exception.Message);
                    Assert.Contains("1 values for 3 vertices", exception.Message);
                    Assert.Equal(0UL, mesh.m_Handle);
                    Assert.Equal(0, mesh.m_SyncVersion);
                    Assert.True(mesh.m_Dirty);
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        [Fact]
        public void RejectedUploadKeepsItsExistingHandleDirtyVersionAndReadability()
        {
            var mesh = new Mesh { vertices = new Vector3[2] };
            try
            {
                // A sentinel, never passed to Native and cleared before releasing the test object.
                mesh.m_Handle = 91;
                mesh.m_SyncVersion = 4;
                mesh.m_UVs[7] = new Vector4[3];
                var exception = Assert.Throws<InvalidOperationException>(() => mesh.UploadMeshData(true));
                Assert.Contains("uv7", exception.Message);
                Assert.Equal(91UL, mesh.m_Handle);
                Assert.Equal(4, mesh.m_SyncVersion);
                Assert.True(mesh.m_Dirty);
                Assert.True(mesh.isReadable);
                Assert.Equal(3, mesh.m_UVs[7].Length);
            }
            finally
            {
                mesh.m_Handle = 0;
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
