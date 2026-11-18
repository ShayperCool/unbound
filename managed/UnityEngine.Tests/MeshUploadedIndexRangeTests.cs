using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // Unbound's own layout, not a Unity claim: the renderer holds a mesh's indices as one list,
    // each submesh's after the one before it, a quad's four as two triangles' six. An indirect
    // draw of a submesh (a Visual Effect Graph mesh output) reads its indices by that layout, which
    // differs from the ranges SetSubMesh gives where submeshes are quads or out of order.
    public class MeshUploadedIndexRangeTests
    {
        static Mesh Grid()
        {
            var mesh = new Mesh();
            var vertices = new Vector3[8];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(i % 4, i / 4, 0f);
            mesh.vertices = vertices;
            return mesh;
        }

        [Fact]
        public void SubmeshesFollowOneAnotherAndQuadsAreTwoTriangles()
        {
            var mesh = Grid();
            mesh.subMeshCount = 3;
            mesh.SetIndices(new[] { 0, 1, 4 }, MeshTopology.Triangles, 0);
            mesh.SetIndices(new[] { 0, 1, 5, 4, 1, 2, 6, 5 }, MeshTopology.Quads, 1);
            mesh.SetIndices(new[] { 0, 1, 4, 1, 5, 4 }, MeshTopology.Triangles, 2, true, 2);

            Assert.Equal((0u, 3u, 0), mesh.UploadedIndexRange(0));
            // Two quads: twelve indices of triangles.
            Assert.Equal((3u, 12u, 0), mesh.UploadedIndexRange(1));
            Assert.Equal((15u, 6u, 2), mesh.UploadedIndexRange(2));
            // Where SetIndices placed them: eight indices of quads.
            Assert.Equal(11, (int)mesh.GetSubMesh(2).indexStart);
        }

        [Fact]
        public void SubmeshesPlacedOutOfOrderAreUploadedInSubmeshOrder()
        {
            var mesh = Grid();
            mesh.SetIndexBufferParams(6, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 4, 5, 6, 0, 1, 2 }, 0, 0, 6);
            mesh.subMeshCount = 2;
            mesh.SetSubMesh(0, new SubMeshDescriptor(3, 3), MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetSubMesh(1, new SubMeshDescriptor(0, 3), MeshUpdateFlags.DontRecalculateBounds);

            Assert.Equal(3, mesh.GetSubMesh(0).indexStart);
            Assert.Equal((0u, 3u, 0), mesh.UploadedIndexRange(0));
            Assert.Equal((3u, 3u, 0), mesh.UploadedIndexRange(1));
        }

        [Fact]
        public void UploadedRangesAreTheLastUploadsAndTheNextOnesOnceTheMeshChanges()
        {
            var mesh = Grid();
            mesh.subMeshCount = 2;
            mesh.SetIndices(new[] { 0, 1, 5, 4 }, MeshTopology.Quads, 0);
            mesh.SetIndices(new[] { 1, 2, 6 }, MeshTopology.Triangles, 1);
            mesh.Sync();
            Assert.Equal((0u, 6u, 0), mesh.UploadedIndexRange(0));
            Assert.Equal((6u, 3u, 0), mesh.UploadedIndexRange(1));
            // Changed, not yet uploaded: the ranges the next upload gives.
            mesh.SetIndices(new[] { 0, 1, 5, 4, 1, 2, 6, 5 }, MeshTopology.Quads, 0);
            Assert.Equal((12u, 3u, 0), mesh.UploadedIndexRange(1));
            mesh.Sync();
            Assert.Equal((0u, 12u, 0), mesh.UploadedIndexRange(0));
            Assert.Equal((12u, 3u, 0), mesh.UploadedIndexRange(1));
        }

        [Fact]
        public void AMeshOutputDrawsTheIndicesAsTheRendererHoldsThem()
        {
            // Quads, then triangles placed before them: SetSubMesh's ranges are not the ones drawn.
            var mesh = Grid();
            mesh.SetIndexBufferParams(7, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 1, 2, 6, 0, 1, 5, 4 }, 0, 0, 7);
            mesh.subMeshCount = 2;
            mesh.SetSubMesh(0, new SubMeshDescriptor(3, 4, MeshTopology.Quads), MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetSubMesh(1, new SubMeshDescriptor(0, 3), MeshUpdateFlags.DontRecalculateBounds);
            Assert.Equal((0u, 6u, 0u), Unbound.Runtime.VFXOutput.Uploaded(mesh, 0));
            Assert.Equal((6u, 3u, 0u), Unbound.Runtime.VFXOutput.Uploaded(mesh, 1));
            Assert.Equal(0, mesh.GetSubMesh(1).indexStart);
        }
    }
}
