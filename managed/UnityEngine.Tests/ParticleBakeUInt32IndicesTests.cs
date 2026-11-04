using System.Linq;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // ParticleSystemRenderer.BakeMesh into a user mesh that already uses 32-bit indices. Documented
    // basis, Unity 6000.0:
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexFormat.html
    //   "Changing indexFormat sets Mesh.subMeshCount to one and clears the index buffer."
    // A bake that picks the index format after writing its indices clears them again, so the baked
    // particle must keep its triangles. Only their presence is asserted: the baked mesh's index
    // format and its triangle order are not documented. No test uploads a mesh.
    public class ParticleBakeUInt32IndicesTests
    {
        [Fact]
        public void BakeMeshIntoUInt32MeshKeepsTheBakedTriangles()
        {
            var go = new GameObject("baked-particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            go.SetActive(true);
            // A camera at the origin looking down +Z, one billboard in front of it.
            var cameraObject = new GameObject("bake-camera");
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(0, 0, 5) }, 1);
                Assert.Equal(IndexFormat.UInt32, mesh.indexFormat);

                ps.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);

                Assert.Equal(4, mesh.vertexCount);
                Assert.Equal(1, mesh.subMeshCount);
                Assert.Equal(MeshTopology.Triangles, mesh.GetTopology(0));
                Assert.Equal(6u, mesh.GetIndexCount(0));
                var triangles = mesh.triangles;
                Assert.Equal(6, triangles.Length);
                Assert.All(triangles, index => Assert.InRange(index, 0, 3));
                Assert.Equal(new[] { 0, 1, 2, 3 }, triangles.Distinct().OrderBy(index => index).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(go);
            }
        }
    }
}
