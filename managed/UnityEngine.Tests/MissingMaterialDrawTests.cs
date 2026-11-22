using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer's slot without a material draws with the error material
    /// (tests/projects/missingmaterial records the Editor's image).</summary>
    public class MissingMaterialDrawTests
    {
        static Mesh TwoParts()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 1, 3, 2 }, 1);
            return mesh;
        }

        static List<FrameBuilder.DrawItem> Draws(Renderer renderer)
        {
            var draws = new List<FrameBuilder.DrawItem>();
            renderer.CollectDraws(new FrameBuilder.CameraContext { Forward = Vector3.forward }, draws);
            return draws;
        }

        [Fact]
        public void ASlotWithoutAMaterialDrawsWithTheErrorMaterialAndNoSlotDrawsNothing()
        {
            var error = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Hidden/InternalErrorShader\" { SubShader { Pass { } } }");
            var plain = new Material(UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/MissingMaterialPlain\" { SubShader { Pass { } } }"));
            var go = new GameObject("Missing", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                Assert.NotNull(Material.Error);
                Assert.Same(Material.Error, Material.Error);
                go.GetComponent<MeshFilter>().sharedMesh = TwoParts();
                var renderer = go.GetComponent<MeshRenderer>();

                renderer.sharedMaterials = new Material[] { plain, null };
                var draws = Draws(renderer);
                Assert.Equal(2, draws.Count);
                Assert.Same(plain, draws[0].Material);
                Assert.Same(Material.Error, draws[1].Material);
                Assert.Equal(1, draws[1].Submesh);
                Assert.Equal("Hidden/InternalErrorShader", draws[1].Material.shader.name);

                renderer.sharedMaterials = new Material[0];
                Assert.Empty(Draws(renderer));
                // The renderer's own slots are not changed by drawing.
                renderer.sharedMaterials = new Material[] { null };
                Assert.Single(Draws(renderer));
                Assert.Null(renderer.sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(error);
            }
        }
    }
}
