using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Renderer.bounds (the world-space box, kept from frame to frame while nothing
    /// changes) follows every change: the object's or a parent's move, an edit of its mesh,
    /// another mesh, a local-bounds override and its reset.</summary>
    public sealed class RendererWorldBoundsTests
    {
        static void Near(Vector3 expected, Vector3 actual) => Assert.True((expected - actual).sqrMagnitude < 1e-10f, $"{expected} != {actual}");

        [Fact]
        public void TheWorldBoxFollowsEveryChange()
        {
            var parent = new GameObject("parent");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(parent.transform, false);
            var renderer = cube.GetComponent<MeshRenderer>();
            var mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            cube.GetComponent<MeshFilter>().sharedMesh = mesh;
            var other = new Mesh { vertices = new[] { Vector3.zero, new Vector3(4f, 0f, 0f), new Vector3(0f, 2f, 0f) }, triangles = new[] { 0, 1, 2 } };
            try
            {
                Near(Vector3.zero, renderer.bounds.center);
                Near(new Vector3(0.5f, 0.5f, 0.5f), renderer.bounds.extents);
                Assert.Equal(renderer.bounds, renderer.bounds);

                cube.transform.localPosition = new Vector3(1f, 0f, 0f);
                Near(new Vector3(1f, 0f, 0f), renderer.bounds.center);
                parent.transform.position = new Vector3(0f, 2f, 0f);
                Near(new Vector3(1f, 2f, 0f), renderer.bounds.center);
                parent.transform.localScale = new Vector3(2f, 2f, 2f);
                Near(new Vector3(2f, 2f, 0f), renderer.bounds.center);
                Near(Vector3.one, renderer.bounds.extents);

                // An edit of the mesh moves its bounds.
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] += new Vector3(0f, 0f, 1f);
                mesh.vertices = vertices;
                Near(new Vector3(2f, 2f, 2f), renderer.bounds.center);

                cube.GetComponent<MeshFilter>().sharedMesh = other;
                Near(new Vector3(6f, 4f, 0f), renderer.bounds.center);

                renderer.localBounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 1f));
                Near(new Vector3(2f, 2f, 0f), renderer.bounds.center);
                renderer.ResetLocalBounds();
                Near(new Vector3(6f, 4f, 0f), renderer.bounds.center);
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(other);
            }
        }
    }
}
