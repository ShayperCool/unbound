using Xunit;

namespace UnityEngine.Tests
{
    public class BuiltinMeshTests
    {
        /// <summary>Every triangle faces away from the center (Unity's fronts wind clockwise, which
        /// makes Cross(b - a, c - a) the outward normal).</summary>
        static void AssertOutward(Mesh mesh, Vector3 center)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var a = v[t[i]];
                var b = v[t[i + 1]];
                var c = v[t[i + 2]];
                var face = Vector3.Cross(b - a, c - a);
                var centroid = (a + b + c) / 3f;
                Assert.True(Vector3.Dot(face, centroid - center) > 0f, $"{mesh.name} triangle {i / 3} faces inwards");
            }
        }

        [Theory]
        [InlineData("Cube", 1f, 1f)]
        [InlineData("Sphere", 1f, 1f)]
        [InlineData("Capsule", 1f, 2f)]
        [InlineData("Cylinder", 1f, 2f)]
        public void SolidsHaveUnitysSizesAndFaceOutwards(string name, float width, float height)
        {
            var mesh = BuiltinMeshes.Create(name);
            Assert.Equal(width, mesh.bounds.size.x, 3);
            Assert.Equal(height, mesh.bounds.size.y, 3);
            Assert.Equal(Vector3.zero, mesh.bounds.center);
            AssertOutward(mesh, Vector3.zero);
            Assert.Equal(mesh.vertexCount, mesh.normals.Length);
        }

        [Fact]
        public void PlaneAndQuadFaceTheirWays()
        {
            var plane = BuiltinMeshes.Create("Plane");
            Assert.Equal(121, plane.vertexCount);
            Assert.Equal(200, plane.triangles.Length / 3);
            Assert.Equal(10f, plane.bounds.size.x, 3);
            AssertOutward(plane, Vector3.down);
            var quad = BuiltinMeshes.Create("Quad");
            Assert.Equal(4, quad.vertexCount);
            AssertOutward(quad, Vector3.forward);
            Assert.NotNull(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
        }
    }
}
