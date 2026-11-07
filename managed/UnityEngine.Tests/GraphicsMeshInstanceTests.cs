using System;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class GraphicsMeshInstanceTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SelectedRangeCopiesPropertiesFromZeroAndRetainsReplayIds(bool render)
        {
            Immediate.Queued.Clear();
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/MeshInstanceRange\" { SubShader { Pass {} } }");
            var material = new Material(shader) { enableInstancing = true };
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            var matrices = Enumerable.Range(0, 6).Select(i => Matrix4x4.Translate(Vector3.right * i)).ToArray();
            var block = new MaterialPropertyBlock();
            block.SetFloatArray("_Weights", new[] { 2f, 3f, 5f, 7f, 11f, 13f });
            try
            {
                if (render) Graphics.RenderMeshInstanced(new RenderParams(material) { matProps = block }, mesh, 0, matrices, 3, 2);
                else Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 3, block);
                matrices[2] = Matrix4x4.zero;
                block.SetFloatArray("_Weights", new float[6]);
                Assert.Equal(3, Immediate.Queued.Count);
                for (int i = 0; i < 3; i++)
                {
                    var draw = Immediate.Queued[i];
                    // Instance i takes element i of the call's arrays (the renderer selects it).
                    Assert.Equal((uint)i, draw.Setup.Value.MeshInstance);
                    Assert.Equal(Matrix4x4.Translate(Vector3.right * (i + (render ? 2 : 0))), draw.Matrix);
                    Assert.Same(Immediate.Queued[0].Properties, draw.Properties);
                    var weights = Assert.IsType<GfxStream.SharedArray>(draw.Properties[Shader.PropertyToID("_Weights")]);
                    Assert.Equal(1u, weights.Components);
                    Assert.Equal(new[] { 2f, 3f, 5f, 7f, 11f, 13f }, weights.Data);
                }
            }
            finally
            {
                Immediate.Queued.Clear();
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
