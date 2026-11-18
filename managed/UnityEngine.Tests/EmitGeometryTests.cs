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
            Assert.Equal(without + 2, Copies(m_Camera));
            // The culling took them: the next one draws the canvases as without emission.
            Assert.Equal(without, Copies(m_Camera));
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
