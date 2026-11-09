using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A copy of a mesh's geometry made into arrays the copy already has: the same
    /// geometry as a fresh copy, never the source's arrays, and seen as new positions.</summary>
    public sealed class MeshCopyReuseTests
    {
        static Mesh Quad(float x)
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(x, 0, 0), new Vector3(x + 1, 0, 0), new Vector3(x + 1, 1, 0), new Vector3(x, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                colors32 = new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12), new Color32(13, 14, 15, 16) },
            };
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            return mesh;
        }

        [Fact]
        public void ACopyIntoItsOwnArraysEqualsAFreshCopy()
        {
            var a = Quad(0f);
            var b = Quad(5f);
            var copy = new Mesh();
            var fresh = new Mesh();
            var tri = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            try
            {
                copy.CopyGeometryFrom(a, reuseArrays: true);
                var vertices = copy.m_Vertices;
                int positions = copy.m_PositionsVersion;
                b.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                copy.CopyGeometryFrom(b, reuseArrays: true);
                fresh.CopyGeometryFrom(b);
                Assert.Same(vertices, copy.m_Vertices);
                Assert.NotEqual(positions, copy.m_PositionsVersion);
                Assert.NotSame(b.m_Vertices, copy.m_Vertices);
                Assert.Equal(fresh.vertices, copy.vertices);
                Assert.Equal(fresh.uv, copy.uv);
                Assert.Equal(fresh.colors32, copy.colors32);
                Assert.Equal(fresh.GetTriangles(0), copy.GetTriangles(0));
                Assert.Equal(fresh.bounds, copy.bounds);
                // A change of the source does not reach the copy.
                b.m_Vertices[0] = new Vector3(9, 9, 9);
                Assert.Equal(new Vector3(5, 0, 0), copy.m_Vertices[0]);
                // Another vertex count: new arrays, and channels the source lacks are gone.
                copy.CopyGeometryFrom(tri, reuseArrays: true);
                Assert.Equal(3, copy.vertexCount);
                Assert.Null(copy.m_Colors);
                Assert.Equal(new[] { 0, 1, 2 }, copy.GetTriangles(0));
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(fresh);
                Object.DestroyImmediate(tri);
            }
        }
    }
}
