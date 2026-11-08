using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A canvas renderer's draw properties (vertex-colour gamma, clip rect and softness)
    /// follow each change of the clipping and the gamma setting, and keep their values (the same
    /// boxes) while nothing changes.</summary>
    public sealed class CanvasRendererPropertiesTests
    {
        static readonly int s_ClipRect = Shader.PropertyToID("_ClipRect");
        static readonly int s_SoftnessX = Shader.PropertyToID("_UIMaskSoftnessX");
        static readonly int s_Gamma = Shader.PropertyToID("_UIVertexColorAlwaysGammaSpace");

        [Fact]
        public void ThePropertiesFollowTheClippingAndTheGammaSetting()
        {
            var go = new GameObject("ui", typeof(RectTransform));
            var cr = go.AddComponent<CanvasRenderer>();
            try
            {
                var p = cr.CanvasProperties(false);
                Assert.Equal(new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity), (Vector4)p[s_ClipRect]);
                Assert.Equal(0, (int)p[s_Gamma]);
                var box = p[s_ClipRect];
                Assert.Same(box, cr.CanvasProperties(false)[s_ClipRect]);

                cr.EnableRectClipping(new Rect(1f, 2f, 3f, 4f));
                cr.clippingSoftness = new Vector2(5f, 6f);
                p = cr.CanvasProperties(false);
                Assert.Equal(new Vector4(1f, 2f, 4f, 6f), (Vector4)p[s_ClipRect]);
                Assert.Equal(5f, (float)p[s_SoftnessX]);
                cr.EnableRectClipping(new Rect(1f, 2f, 3f, 5f));
                Assert.Equal(new Vector4(1f, 2f, 4f, 7f), (Vector4)cr.CanvasProperties(false)[s_ClipRect]);
                Assert.Equal(1, (int)cr.CanvasProperties(true)[s_Gamma]);
                cr.DisableRectClipping();
                p = cr.CanvasProperties(true);
                Assert.Equal(float.NegativeInfinity, ((Vector4)p[s_ClipRect]).x);
                Assert.Equal(0f, (float)p[s_SoftnessX]);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
