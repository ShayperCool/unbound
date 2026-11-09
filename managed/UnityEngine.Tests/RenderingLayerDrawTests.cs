using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer's draws carry its renderingLayerMask as unity_RenderingLayer (the
    /// mask's bits in x), which URP's light layers compare with each light's layers: none is added
    /// for the default layer, the draw's own value wins.</summary>
    public sealed class RenderingLayerDrawTests
    {
        static readonly int s_Id = Shader.PropertyToID("unity_RenderingLayer");

        [Fact]
        public void ARendererOnOtherLayersDrawsWithItsMask()
        {
            var go = new GameObject("layers");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                var own = new Dictionary<int, object> { [7] = 2f };
                // Default layer only: the draw's properties stay as they are.
                Assert.Same(own, FrameBuilder.WithRenderingLayer(renderer, own));
                Assert.Null(FrameBuilder.WithRenderingLayer(renderer, null));
                renderer.renderingLayerMask = 1u | 4u;
                using (DrawPropertyPool.Begin())
                {
                    var drawn = FrameBuilder.WithRenderingLayer(renderer, own);
                    Assert.NotSame(own, drawn);
                    Assert.Equal(2f, drawn[7]);
                    Assert.Equal(5, BitConverter.SingleToInt32Bits(((Vector4)drawn[s_Id]).x));
                    Assert.False(own.ContainsKey(s_Id));
                    // Every layer bit (the high bit too) passes through unchanged.
                    renderer.renderingLayerMask = 0x80000002u;
                    Assert.Equal(unchecked((int)0x80000002u), BitConverter.SingleToInt32Bits(((Vector4)FrameBuilder.WithRenderingLayer(renderer, null)[s_Id]).x));
                    // No layer at all is not the default layer either.
                    renderer.renderingLayerMask = 0u;
                    Assert.Equal(0, BitConverter.SingleToInt32Bits(((Vector4)FrameBuilder.WithRenderingLayer(renderer, null)[s_Id]).x));
                }
                // A block's own value wins.
                var block = new Dictionary<int, object> { [s_Id] = new Vector4(9f, 0f, 0f, 0f) };
                Assert.Same(block, FrameBuilder.WithRenderingLayer(renderer, block));
                Assert.Same(own, FrameBuilder.WithRenderingLayer(null, own));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
