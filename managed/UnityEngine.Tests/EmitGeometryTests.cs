using System;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>How many times a scriptable culling draws the camera's canvases
    /// (ScriptableRenderContext.EmitGeometryForCamera / EmitWorldGeometryForSceneView), and the
    /// target SetupCameraProperties binds (tests/projects/emitgeometry).</summary>
    public sealed class EmitGeometryTests : IDisposable
    {
        readonly GameObject m_CameraObject = new GameObject("emit geometry camera");
        readonly GameObject m_OtherObject = new GameObject("emit geometry other camera");
        readonly Camera m_Camera;
        readonly Camera m_Other;

        public EmitGeometryTests()
        {
            m_Camera = m_CameraObject.AddComponent<Camera>();
            m_Other = m_OtherObject.AddComponent<Camera>();
        }

        public void Dispose()
        {
            Culling.EndRender();
            Culling.EndFrame();
            Object.DestroyImmediate(m_CameraObject);
            Object.DestroyImmediate(m_OtherObject);
        }

        static int Copies(Camera camera)
        {
            Assert.True(camera.TryGetCullingParameters(out var parameters));
            return Culling.Get(Culling.Cull(ref parameters)).CanvasCopies;
        }

        [Theory]
        [InlineData(CameraType.Game, 1)]
        [InlineData(CameraType.Reflection, 1)]
        [InlineData(CameraType.SceneView, 1)]
        [InlineData(CameraType.Preview, 0)]
        // Not measured: VR is taken as the Game camera it renders for.
        [InlineData(CameraType.VR, 1)]
        public void EachEmissionDrawsTheCanvasesOnceMore(CameraType type, int without)
        {
            m_Camera.cameraType = type;
            Assert.Equal(without, Copies(m_Camera));
            ScriptableRenderContext.EmitGeometryForCamera(m_Camera);
            Assert.Equal(without + 1, Copies(m_Camera));
            ScriptableRenderContext.EmitGeometryForCamera(m_Camera);
            ScriptableRenderContext.EmitWorldGeometryForSceneView(m_Camera);
            Assert.Equal(without + 3, Copies(m_Camera));
            // Every later culling of the camera in the render has them (Game.cullTwice); the
            // next render starts without.
            Assert.Equal(without + 3, Copies(m_Camera));
            Culling.EndRender();
            Assert.Equal(without, Copies(m_Camera));
        }

        /// <summary>An emission after a culling is not in that culling's results
        /// (tests/projects/emitgeometry Game.emitAfterCull).</summary>
        [Fact]
        public void AnEmissionAfterACullingIsNotInIt()
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            var before = Culling.Get(Culling.Cull(ref parameters));
            ScriptableRenderContext.EmitGeometryForCamera(m_Camera);
            Assert.Equal(1, before.CanvasCopies);
            Assert.Equal(2, Copies(m_Camera));
        }

        [Fact]
        public void AnEmissionIsTheCamerasOwnAndEndsWithTheRender()
        {
            ScriptableRenderContext.EmitGeometryForCamera(null);
            ScriptableRenderContext.EmitGeometryForCamera(m_Other);
            Assert.Equal(1, Copies(m_Camera));
            // Not culled in its render: dropped when the render ends.
            Culling.EndRender();
            Assert.Equal(1, Copies(m_Other));
        }

        /// <summary>The context's SetupCameraProperties sets the camera's pixel rectangle as the
        /// viewport; binding the camera target again covers the whole target
        /// (tests/projects/emitgeometry viewport.setup, viewport.cameraTarget).</summary>
        [Fact]
        public void SetupCameraPropertiesSetsTheCamerasViewport()
        {
            var target = new RenderTexture(64, 32, 24);
            try
            {
                m_Camera.targetTexture = target;
                m_Camera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                new ScriptableRenderContext().SetupCameraProperties(m_Camera);
                Assert.Equal(new Rect(32f, 0f, 32f, 32f), GfxStream.Current.Viewport);
                using (var buffer = new CommandBuffer())
                {
                    buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    var context = new ScriptableRenderContext();
                    context.SetupCameraProperties(m_Camera);
                    context.ExecuteCommandBuffer(buffer);
                }
                Assert.NotEqual(new Rect(32f, 0f, 32f, 32f), GfxStream.Current.Viewport);
            }
            finally
            {
                m_Camera.rect = new Rect(0f, 0f, 1f, 1f);
                m_Camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void EmissionsNoRenderTookEndWithTheFrame()
        {
            ScriptableRenderContext.EmitGeometryForCamera(m_Camera);
            Culling.EndFrame();
            Assert.Equal(1, Copies(m_Camera));
        }

        [Fact]
        public void SetupCameraPropertiesBindsTheCamerasTarget()
        {
            var target = new RenderTexture(8, 8, 24);
            var other = new RenderTexture(8, 8, 24);
            try
            {
                m_Camera.targetTexture = target;
                RenderTexture.active = other;
                new ScriptableRenderContext().SetupCameraProperties(m_Camera);
                Assert.Same(target, RenderTexture.active);
                // A camera drawing to the screen binds the screen.
                m_Camera.targetTexture = null;
                RenderTexture.active = other;
                new ScriptableRenderContext().SetupCameraProperties(m_Camera);
                Assert.Null(RenderTexture.active);
                // A command buffer's setup (the render graph runs it inside a pass) keeps the target.
                m_Camera.targetTexture = target;
                RenderTexture.active = other;
                using (var buffer = new CommandBuffer())
                {
                    buffer.SetupCameraProperties(m_Camera);
                    Graphics.ExecuteCommandBuffer(buffer);
                }
                Assert.Same(other, RenderTexture.active);
            }
            finally
            {
                RenderTexture.active = null;
                m_Camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(other);
            }
        }
    }
}
