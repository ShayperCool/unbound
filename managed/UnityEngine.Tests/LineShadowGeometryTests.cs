using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Rendering;
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

        sealed class ShadowLogs : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal readonly List<(Exception Error, Object Context)> Errors = new();
            internal ShadowLogs(ILogHandler inner) { m_Inner = inner; }
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                m_Inner.LogFormat(type, context, format, args);
            public void LogException(Exception exception, Object context)
            {
                Errors.Add((exception, context));
                m_Inner.LogException(exception, context);
            }
        }

        sealed class ShadowScopeObserver : FrameBuilder.IDrawSource
        {
            internal FrameBuilder.CameraContext Context;
            internal bool ShadowPass, Srp;
            internal int Draws;
            public void CollectCameraDraws(FrameBuilder.CameraContext context, List<FrameBuilder.DrawItem> into)
            {
                Context = context;
                ShadowPass = context.StripShadowPass;
                Srp = context.StripShadowSrp;
                Draws = into.Count;
            }
            public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into) { }
        }

        [Theory]
        [InlineData(LightType.Rectangle, false, false)]
        [InlineData(LightType.Rectangle, false, true)]
        [InlineData(LightType.Point, false, false)]
        [InlineData(LightType.Spot, false, false)]
        [InlineData(LightType.Directional, true, false)]
        [InlineData(LightType.Point, true, false)]
        [InlineData(LightType.Spot, true, false)]
        [InlineData(LightType.Point, false, true)]
        [InlineData(LightType.Spot, false, true)]
        [InlineData(LightType.Directional, true, true)]
        [InlineData(LightType.Point, true, true)]
        [InlineData(LightType.Spot, true, true)]
        public void CollectorsRespectMeasuredLightBoundaryAndRestoreTheirScope(LightType type, bool srp, bool trail)
        {
            string version = ProjectInfo.UnityVersion;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var logs = new ShadowLogs(previousHandler);
            var previousShaderCall = ShaderPassInfo.s_CallForTests;
            var sources = FrameBuilder.Sources.ToArray();
            var observer = new ShadowScopeObserver();
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -4);
            camera.cullingMask = 1 << 30;
            var light = new GameObject("light").AddComponent<Light>();
            light.type = type;
            light.cullingMask = camera.cullingMask;
            var go = new GameObject("strip") { layer = 30 };
            Renderer strip;
            var points = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0.2f, 0.3f) };
            if (trail)
            {
                var renderer = go.AddComponent<TrailRenderer>();
                renderer.emitting = true;
                renderer.time = 100;
                renderer.AddPositions(points);
                renderer.emitting = false;
                strip = renderer;
            }
            else
            {
                var renderer = go.AddComponent<LineRenderer>();
                renderer.SetPositions(points);
                strip = renderer;
            }
            Shader shader = null;
            Material material = null;
            var data = new CullingData { Camera = camera };
            try
            {
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = name => JsonSerializer.Serialize(new
                {
                    shader = name, found = true, tags = new Dictionary<string, string>(),
                    passes = new[] { new { name = "ShadowCaster", tags = new Dictionary<string, string> { ["LightMode"] = "ShadowCaster" } } },
                });
                shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/StripShadowBoundary\" { SubShader { Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } } }");
                material = new Material(shader) { renderQueue = 2000 };
                strip.sharedMaterial = material;
                var context = FrameBuilder.ContextOf(camera);
                var ordinary = new List<FrameBuilder.DrawItem>();
                strip.CollectDraws(context, ordinary);
                Assert.Single(ordinary);

                logger.logHandler = logs;
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                FrameBuilder.Sources.Clear();
                FrameBuilder.Sources.Add(observer);
                int before = FrameBuilder.s_DrawCount;
                if (srp)
                {
                    data.Renderers.Add(strip);
                    data.Lights = new NativeArray<VisibleLight>(1, Allocator.Temp);
                    data.Lights[0] = new VisibleLight(light, default, false, false, false);
                    var tags = new List<ShaderTagId> { new ShaderTagId("ShadowCaster") };
                    var drawing = new DrawingSettings(tags[0], new SortingSettings(camera));
                    FrameBuilder.DrawRendererList(data, drawing, new FilteringSettings(RenderQueueRange.all),
                        tags, shadows: ShadowObjectsFilter.AllObjects, shadowLight: 0);
                    Assert.True(observer.ShadowPass);
                    Assert.True(observer.Srp);
                    Assert.Equal(0, observer.Draws);
                    context = observer.Context;
                }
                else
                {
                    typeof(FrameBuilder).GetMethod("CollectCasters", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { light, context });
                    var casters = (List<FrameBuilder.DrawItem>)typeof(FrameBuilder)
                        .GetField("s_Casters", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    if (type == LightType.Rectangle) Assert.DoesNotContain(casters, item => ReferenceEquals(item.Renderer, strip));
                    else Assert.Contains(casters, item => ReferenceEquals(item.Renderer, strip));
                }
                var errors = logs.Errors.FindAll(entry => ReferenceEquals(entry.Context, strip));
                if (srp || type == LightType.Rectangle)
                {
                    var error = Assert.Single(errors).Error;
                    Assert.IsType<NotSupportedException>(error);
                    Assert.Contains("supported only for built-in directional, point and spot lights", error.Message);
                }
                else Assert.Empty(errors);
                Assert.Equal(before, FrameBuilder.s_DrawCount);
                Assert.False(context.StripShadowPass);
                Assert.False(context.StripShadowSrp);
                Assert.Null(context.StripShadowLight);
                Assert.Equal(camera.worldToCameraMatrix, context.View);
            }
            finally
            {
                data.Dispose();
                FrameBuilder.Sources.Clear();
                FrameBuilder.Sources.AddRange(sources);
                logger.logHandler = previousHandler;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = previousShaderCall;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(camera.gameObject);
                if (material != null) Object.DestroyImmediate(material);
                if (shader != null) Object.DestroyImmediate(shader);
                ProjectInfo.UnityVersion = version;
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
