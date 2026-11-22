using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which Mesh setters update the mesh's and the sub-meshes' bounds (measured, Unity
    /// 6000.0.71f1: tests/projects/scenesave mesh-bounds).</summary>
    public class MeshSetterBoundsTests
    {
        static Vector3[] Tri(float scale) => new[] { Vector3.zero, Vector3.right * scale, Vector3.up * scale };

        [Fact]
        public void VerticesOfTheSameCountKeepTheBoundsAndAnotherCountMakesThemAgain()
        {
            var mesh = new Mesh { vertices = Tri(1f), triangles = new[] { 0, 1, 2 } };
            Assert.Equal(new Vector3(0.5f, 0.5f, 0f), mesh.bounds.center);

            mesh.vertices = Tri(2f);
            Assert.Equal(new Vector3(0.5f, 0.5f, 0f), mesh.bounds.center);
            Assert.Equal(new Vector3(0.5f, 0.5f, 0f), mesh.GetSubMesh(0).bounds.center);

            mesh.vertices = new[] { Vector3.zero, Vector3.right * 3f, Vector3.up * 3f, Vector3.one * 3f };
            Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), mesh.bounds.center);
            // The sub-mesh is measured again over its own vertices.
            Assert.Equal(new Vector3(1.5f, 1.5f, 0f), mesh.GetSubMesh(0).bounds.center);

            mesh.RecalculateBounds();
            Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), mesh.bounds.center);
            Object.DestroyImmediate(mesh);
        }

        [Fact]
        public void TriangleSettersMeasureOnlyTheSubMeshAndOnlyWithCalculateBounds()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up * 2f, Vector3.one * 4f } };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0, true);
            Assert.Equal(new Vector3(1f, 1f, 0f), mesh.GetSubMesh(0).bounds.center);

            mesh.SetTriangles(new[] { 0, 1, 3 }, 0, false);
            var kept = mesh.GetSubMesh(0);
            Assert.Equal(new Vector3(1f, 1f, 0f), kept.bounds.center);
            Assert.Equal(0, kept.firstVertex);
            Assert.Equal(4, kept.vertexCount);

            mesh.SetIndices(new[] { 0, 1, 3 }, MeshTopology.Triangles, 0, true);
            Assert.Equal(new Vector3(2f, 2f, 2f), mesh.GetSubMesh(0).bounds.center);
            // The mesh's own bounds are those its vertices made.
            Assert.Equal(new Vector3(2f, 2f, 2f), mesh.bounds.center);

            mesh.bounds = new Bounds(Vector3.one * 10f, Vector3.one);
            mesh.triangles = new[] { 0, 1, 2 };
            Assert.Equal(Vector3.one * 10f, mesh.bounds.center);
            Assert.Equal(new Vector3(1f, 1f, 0f), mesh.GetSubMesh(0).bounds.center);
            Object.DestroyImmediate(mesh);
        }

        [Fact]
        public void ClearKeepsTheMeshBoundsAndEmptiesTheSubMeshes()
        {
            var mesh = new Mesh { vertices = Tri(2f), triangles = new[] { 0, 1, 2 } };
            mesh.Clear(true);
            Assert.Equal(new Vector3(1f, 1f, 0f), mesh.bounds.center);
            var s = mesh.GetSubMesh(0);
            Assert.Equal(default, s.bounds);
            Assert.Equal(0, s.vertexCount);
            Object.DestroyImmediate(mesh);
        }
    }
}
