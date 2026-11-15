using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class LineShadowGeometryTests
    {
        [Theory]
        [InlineData(LineAlignment.View)]
        [InlineData(LineAlignment.TransformZ)]
        public void BiasUsesTheSampledFullWidthAndLeavesTheOrdinaryStripUnchanged(LineAlignment alignment)
        {
            var p = new LineParameters { Alignment = alignment, WidthMultiplier = 0.4f,
                WidthCurve = AnimationCurve.Linear(0, 0.5f, 1, 1.5f) };
            var points = new[] { new Vector3(-1, 0, 2), new Vector3(1, 0.2f, 2) };
            var frame = Matrix4x4.TRS(new Vector3(3, -2, 1), Quaternion.Euler(10, 35, 20), Vector3.one);
            var space = LineSpace.Of(p, frame.inverseDoublePrecision, false, 0.3f, frame,
                new Vector3(3, -2, -1), Matrix4x4.identity, Matrix4x4.identity);
            var strip = new LineStrip();
            Assert.True(strip.Build(points, null, false, p, space));
            var normal = (Vector3[])strip.Positions.Clone();
            float bias = alignment == LineAlignment.View ? 0.5f : -0.5f;
            Assert.True(strip.Build(points, null, false, p, space, bias));
            var direction = space.FromBuild.MultiplyVector(Vector3.forward);
            for (int i = 0; i < strip.VertexCount; i++)
            {
                float width = i < 2 ? 0.2f : 0.6f;
                Assert.InRange(Vector3.Distance(normal[i] + direction * (width * bias), strip.Positions[i]), 0f, 2e-6f);
            }
            Assert.True(strip.Build(points, null, false, p, space));
            for (int i = 0; i < strip.VertexCount; i++) Assert.Equal(normal[i], strip.Positions[i]);
        }

        [Fact]
        public void LightFramesIgnoreScaleAndRestoreTheCameraAndNestedShadowState()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var light = new GameObject("light").AddComponent<Light>();
            var second = new GameObject("second").AddComponent<Light>();
            try
            {
                light.transform.position = new Vector3(1, 2, -3);
                light.transform.rotation = Quaternion.Euler(10, 55, 20);
                var context = new FrameBuilder.CameraContext { Camera = camera, View = camera.worldToCameraMatrix };
                var ordinary = context.View;
                Matrix4x4 unscaled;
                using (new FrameBuilder.StripShadowScope(context, true, light)) unscaled = context.StripShadowView;
                light.transform.localScale = new Vector3(2, 3, 4);
                using (new FrameBuilder.StripShadowScope(context, true, light))
                {
                    Assert.Equal(unscaled, context.StripShadowView);
                    Assert.Equal(ordinary, context.View);
                    Assert.InRange(Vector3.Distance(context.StripShadowView.MultiplyPoint3x4(light.transform.position), Vector3.zero), 0f, 1e-5f);
                    Assert.InRange(Vector3.Distance(context.StripShadowView.MultiplyVector(light.transform.forward), Vector3.forward), 0f, 1e-5f);
                    using (new FrameBuilder.StripShadowScope(context, true, second)) Assert.Same(second, context.StripShadowLight);
                    Assert.Same(light, context.StripShadowLight);
                    Assert.Equal(unscaled, context.StripShadowView);
                }
                Assert.False(context.StripShadowPass);
                Assert.Null(context.StripShadowLight);
                Assert.Equal(ordinary, context.View);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Fact]
        public void StreamedMeshesAreReusedAndReleasedWithTheirCameraOrLight()
        {
            var a = new GameObject("cameraA").AddComponent<Camera>();
            var b = new GameObject("cameraB").AddComponent<Camera>();
            var light = new GameObject("light").AddComponent<Light>();
            var cache = new LineRenderMeshes();
            try
            {
                var context = new FrameBuilder.CameraContext { Camera = a };
                var normalA = cache.Get(context, "test");
                Assert.Same(normalA, cache.Get(context, "test"));
                context.StripShadowLight = light;
                var shadowA = cache.Get(context, "test");
                Assert.NotSame(normalA, shadowA);
                Assert.Same(shadowA, cache.Get(context, "test"));
                context.Camera = b;
                var shadowB = cache.Get(context, "test");
                Assert.NotSame(shadowA, shadowB);
                Object.DestroyImmediate(a.gameObject);
                Assert.True(normalA == null);
                Assert.True(shadowA == null);
                Assert.True(shadowB != null);
                Assert.Same(shadowB, cache.Get(context, "test"));
                Object.DestroyImmediate(light.gameObject);
                Assert.True(shadowB == null);
                context.StripShadowLight = null;
                var normalB = cache.Get(context, "test");
                Assert.True(normalB != null);
                cache.Clear();
                Assert.True(normalB == null);
                cache.Clear();
            }
            finally
            {
                cache.Clear();
                if (a != null) Object.DestroyImmediate(a.gameObject);
                if (b != null) Object.DestroyImmediate(b.gameObject);
                if (light != null) Object.DestroyImmediate(light.gameObject);
            }
        }

        [Theory]
        [InlineData(LightType.Point, false)]
        [InlineData(LightType.Spot, false)]
        [InlineData(LightType.Directional, true)]
        [InlineData(LightType.Point, true)]
        [InlineData(LightType.Spot, true)]
        public void UnmeasuredShadowContextsFailWithAClearDiagnostic(LightType type, bool srp)
        {
            string version = ProjectInfo.UnityVersion;
            var camera = new GameObject("camera").AddComponent<Camera>();
            var light = new GameObject("light").AddComponent<Light>();
            var line = new GameObject("line").AddComponent<LineRenderer>();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                light.type = type;
                var context = new FrameBuilder.CameraContext { Camera = camera, View = camera.worldToCameraMatrix };
                using (new FrameBuilder.StripShadowScope(context, true, light, srp))
                {
                    var draws = new System.Collections.Generic.List<FrameBuilder.DrawItem>();
                    var error = Assert.Throws<NotSupportedException>(() => line.CollectDraws(context, draws));
                    Assert.Contains("supported only for built-in directional lights", error.Message);
                    Assert.Empty(draws);
                }
                Assert.False(context.StripShadowPass);
                Assert.False(context.StripShadowSrp);
            }
            finally
            {
                ProjectInfo.UnityVersion = version;
                Object.DestroyImmediate(line.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-0.2f)]
        [InlineData(float.NaN)]
        public void ImperfectStripsKeepTheirIndicesWithinTheirVertexRange(float width)
        {
            var p = new LineParameters { WidthMultiplier = width, NumCapVertices = 2, NumCornerVertices = 2 };
            var points = new[] { Vector3.zero, Vector3.zero, new Vector3(1, 0, 0) };
            var space = LineSpace.Of(p, Matrix4x4.Translate(new Vector3(0, 0, -2)), false, 0.3f,
                Matrix4x4.identity, Vector3.zero, Matrix4x4.identity, Matrix4x4.identity);
            var strip = new LineStrip();
            Assert.True(strip.Build(points, null, false, p, space, 0.5f));
            for (int i = 0; i < strip.IndexCount; i++) Assert.InRange(strip.Indices[i], 0, strip.VertexCount - 1);
        }
    }
}
