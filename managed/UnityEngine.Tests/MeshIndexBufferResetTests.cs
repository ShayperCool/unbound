using System;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // Documented basis, Unity 6000.0:
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexFormat.html
    //   "Changing indexFormat sets Mesh.subMeshCount to one and clears the index buffer."
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetIndexBufferParams.html
    //   "When you change the index buffer size or format, the Mesh.subMeshCount reverts to one, and
    //   the index buffer data is uninitialized."
    // Not claimed: index values after SetIndexBufferParams, sub-mesh 0's descriptor after that
    // reset, and Unity's handling of an unchanged value or a negative count. Tests marked "guard"
    // pin Unbound's own choice there and are not Native claims. No test uploads a mesh.
    public class MeshIndexBufferResetTests
    {
        static readonly int[][] s_Triangles = { new[] { 0, 1, 2 }, new[] { 1, 2, 3 }, new[] { 3, 4, 5 } };

        static Mesh ThreeSubmeshes(IndexFormat format = IndexFormat.UInt16)
        {
            var mesh = new Mesh { indexFormat = format };
            mesh.vertices = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(1, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0),
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one, new Vector2(2, 0), new Vector2(2, 1) };
            mesh.colors32 = new[]
            {
                new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12),
                new Color32(13, 14, 15, 16), new Color32(17, 18, 19, 20), new Color32(21, 22, 23, 24),
            };
            mesh.subMeshCount = s_Triangles.Length;
            for (int i = 0; i < s_Triangles.Length; i++) mesh.SetTriangles(s_Triangles[i], i);
            return mesh;
        }

        static (Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Color32[] Colors) Channels(Mesh mesh) =>
            (mesh.vertices, mesh.normals, mesh.uv, mesh.colors32);

        static void AssertChannels((Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Color32[] Colors) expected, Mesh mesh)
        {
            Assert.Equal(expected.Vertices.Length, mesh.vertexCount);
            Assert.Equal(expected.Vertices, mesh.vertices);
            Assert.Equal(expected.Normals, mesh.normals);
            Assert.Equal(expected.Uv, mesh.uv);
            Assert.Equal(expected.Colors, mesh.colors32);
        }

        static void AssertOriginalSubmeshes(Mesh mesh)
        {
            Assert.Equal(s_Triangles.Length, mesh.subMeshCount);
            for (int i = 0; i < s_Triangles.Length; i++) Assert.Equal(s_Triangles[i], mesh.GetIndices(i));
        }

        // ------------------------------------------------------------------ indexFormat

        [Theory]
        [InlineData(IndexFormat.UInt16, IndexFormat.UInt32)]
        [InlineData(IndexFormat.UInt32, IndexFormat.UInt16)]
        public void ChangingIndexFormatLeavesOneSubmeshAndNoIndices(IndexFormat from, IndexFormat to)
        {
            var mesh = ThreeSubmeshes(from);
            try
            {
                mesh.indexFormat = to;
                Assert.Equal(to, mesh.indexFormat);
                Assert.Equal(1, mesh.subMeshCount);
                Assert.Equal(0u, mesh.GetIndexCount(0));
                Assert.Empty(mesh.GetIndices(0));
                Assert.Empty(mesh.triangles);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ChangingIndexFormatClearsSingleSubmeshTriangles()
        {
            var mesh = new Mesh { vertices = new Vector3[3], triangles = new[] { 0, 1, 2 } };
            try
            {
                mesh.indexFormat = IndexFormat.UInt32;
                Assert.Equal(1, mesh.subMeshCount);
                Assert.Empty(mesh.triangles);
                Assert.Empty(mesh.GetTriangles(0));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ChangingIndexFormatKeepsVertexChannels()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                var before = Channels(mesh);
                mesh.indexFormat = IndexFormat.UInt32;
                AssertChannels(before, mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void IndicesSetAfterIndexFormatChangeAreKept()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(s_Triangles[2], 0);
                mesh.SetTriangles(s_Triangles[0], 1);
                Assert.Equal(2, mesh.subMeshCount);
                Assert.Equal(s_Triangles[2], mesh.GetIndices(0));
                Assert.Equal(s_Triangles[0], mesh.GetIndices(1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // Guard (Unbound's reading of "changing"; Unity's handling of an equal value is unmeasured).
        [Theory]
        [InlineData(IndexFormat.UInt16)]
        [InlineData(IndexFormat.UInt32)]
        public void GuardAssigningSameIndexFormatKeepsSubmeshes(IndexFormat format)
        {
            var mesh = ThreeSubmeshes(format);
            try
            {
                var before = Channels(mesh);
                mesh.indexFormat = format;
                Assert.Equal(format, mesh.indexFormat);
                AssertOriginalSubmeshes(mesh);
                AssertChannels(before, mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ------------------------------------------------------------------ SetIndexBufferParams

        [Theory]
        [InlineData(12, IndexFormat.UInt16)]
        [InlineData(3, IndexFormat.UInt16)]
        [InlineData(0, IndexFormat.UInt16)]
        [InlineData(9, IndexFormat.UInt32)]
        [InlineData(12, IndexFormat.UInt32)]
        public void SetIndexBufferParamsWithNewSizeOrFormatRevertsToOneSubmesh(int count, IndexFormat format)
        {
            var mesh = ThreeSubmeshes();
            try
            {
                mesh.SetIndexBufferParams(count, format);
                Assert.Equal(format, mesh.indexFormat);
                Assert.Equal(1, mesh.subMeshCount);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void SetIndexBufferParamsKeepsVertexChannels()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                var before = Channels(mesh);
                mesh.SetIndexBufferParams(12, IndexFormat.UInt32);
                AssertChannels(before, mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // The documented usage pattern after a reset; every index read back was written first.
        [Fact]
        public void SetIndexBufferParamsThenDataAndSubMeshesBuildsTheNewSubmeshes()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                var indices = new uint[] { 3, 4, 5, 0, 1, 2 };
                mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
                mesh.subMeshCount = 2;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3));
                mesh.SetSubMesh(1, new SubMeshDescriptor(3, 3));
                Assert.Equal(2, mesh.subMeshCount);
                Assert.Equal(new[] { 3, 4, 5 }, mesh.GetIndices(0));
                Assert.Equal(new[] { 0, 1, 2 }, mesh.GetIndices(1));
                Assert.Equal(3u, mesh.GetIndexStart(1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // Guard (the reset's trigger, a new size or format, is not met; Unity's handling of it is
        // unmeasured). 9 = the three submeshes' indices one after another.
        [Fact]
        public void GuardSetIndexBufferParamsWithSameSizeAndFormatKeepsSubmeshes()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                var before = Channels(mesh);
                mesh.SetIndexBufferParams(9, IndexFormat.UInt16);
                Assert.Equal(IndexFormat.UInt16, mesh.indexFormat);
                AssertOriginalSubmeshes(mesh);
                AssertChannels(before, mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // Guard (Unbound fails loudly before changing anything; Unity documents no behaviour for a
        // negative count).
        [Fact]
        public void GuardNegativeIndexCountThrowsAndKeepsTheMesh()
        {
            var mesh = ThreeSubmeshes();
            try
            {
                var before = Channels(mesh);
                Assert.Throws<ArgumentException>(() => mesh.SetIndexBufferParams(-1, IndexFormat.UInt32));
                Assert.Equal(IndexFormat.UInt16, mesh.indexFormat);
                AssertOriginalSubmeshes(mesh);
                AssertChannels(before, mesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
