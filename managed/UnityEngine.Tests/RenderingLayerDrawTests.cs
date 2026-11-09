using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A draw carries its rendering layer mask (a renderer's renderingLayerMask, a
    /// Graphics.RenderMesh draw's RenderParams.renderingLayerMask) as unity_RenderingLayer, the
    /// mask's bits in x, which URP's light layers compare with each light's layers: none is added
    /// for the default layer or an engine-made draw, the draw's own value wins.</summary>
    public sealed class RenderingLayerDrawTests
    {
        static readonly int s_Id = Shader.PropertyToID("unity_RenderingLayer");
        /// <summary>A property of the draw's own (property ids follow the order names are first
        /// used in, so a literal id could be unity_RenderingLayer's).</summary>
        static readonly int s_Own = Shader.PropertyToID("_RenderingLayerDrawTestsOwn");

        static int Bits(Dictionary<int, object> properties) => BitConverter.SingleToInt32Bits(((Vector4)properties[s_Id]).x);

        [Fact]
        public void ADrawOnOtherLayersCarriesItsMask()
        {
            var own = new Dictionary<int, object> { [s_Own] = 2f };
            // Default layer only, or no mask: the draw's properties stay as they are.
            Assert.Same(own, FrameBuilder.WithRenderingLayer(1u, own));
            Assert.Null(FrameBuilder.WithRenderingLayer(1u, null));
            Assert.Same(own, FrameBuilder.WithRenderingLayer(null, own));
            using (DrawPropertyPool.Begin())
            {
                var drawn = FrameBuilder.WithRenderingLayer(1u | 4u, own);
                Assert.NotSame(own, drawn);
                Assert.Equal(2f, drawn[s_Own]);
                Assert.Equal(5, Bits(drawn));
                Assert.False(own.ContainsKey(s_Id));
                // Every bit passes through unchanged: the high bit, Everything (a NaN's bits) and
                // no layer at all (not the default layer either).
                Assert.Equal(unchecked((int)0x80000002u), Bits(FrameBuilder.WithRenderingLayer(0x80000002u, null)));
                Assert.Equal(-1, Bits(FrameBuilder.WithRenderingLayer(uint.MaxValue, null)));
                Assert.Equal(0, Bits(FrameBuilder.WithRenderingLayer(0u, null)));
            }
            // A block's own value wins.
            var block = new Dictionary<int, object> { [s_Id] = new Vector4(9f, 0f, 0f, 0f) };
            Assert.Same(block, FrameBuilder.WithRenderingLayer(4u, block));
        }

        [Fact]
        public void RenderMeshDrawsKeepTheirRenderParamsMask()
        {
            Immediate.Queued.Clear();
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderingLayerDraw\" { SubShader { Pass {} } }");
            var material = new Material(shader);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            var cameraObject = new GameObject("rendering layer camera");
            var camera = cameraObject.AddComponent<Camera>();
            try
            {
                Graphics.RenderMesh(new RenderParams(material) { renderingLayerMask = 6u }, mesh, 0, Matrix4x4.identity);
                Graphics.RenderMesh(new RenderParams(material), mesh, 0, Matrix4x4.identity);
                Assert.Equal(2, Immediate.Queued.Count);
                var context = FrameBuilder.ContextOf(camera);
                Assert.Equal(6u, FrameBuilder.QueuedItem(context, Immediate.Queued[0]).RenderingLayerMask);
                Assert.Equal((uint)UnityEngine.Rendering.GraphicsSettings.defaultRenderingLayerMask, FrameBuilder.QueuedItem(context, Immediate.Queued[1]).RenderingLayerMask);
            }
            finally
            {
                Immediate.Queued.Clear();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
