using System;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class ScreenCaptureStateTests
    {
        static void SetCallerState()
        {
            GfxStream.SetTargets(new ulong[] { 801, 802 }, 803, 1, 2);
            GfxStream.Viewport(new Rect(1, 2, 3, 4));
            GfxStream.Camera(Matrix4x4.Translate(new Vector3(2, 3, 4)), Matrix4x4.Scale(new Vector3(3, 4, 5)), GfxStream.CameraOverlay);
        }

        static void EqualState(GfxStream.State expected)
        {
            var actual = GfxStream.Current;
            Assert.Equal(expected.Colors, actual.Colors);
            Assert.Equal(expected.Color, actual.Color);
            Assert.Equal(expected.Depth, actual.Depth);
            Assert.Equal(expected.Mip, actual.Mip);
            Assert.Equal(expected.Slice, actual.Slice);
            Assert.Equal(expected.Viewport, actual.Viewport);
            Assert.Equal(expected.Scissor, actual.Scissor);
            Assert.Equal(expected.View, actual.View);
            Assert.Equal(expected.Projection, actual.Projection);
            Assert.Equal(expected.Flags, actual.Flags);
        }

        [Fact]
        public void IntoTexturePreservesAllCallerAttachmentsViewportAndWireframe()
        {
            var saved = GfxStream.Current;
            var active = RenderTexture.ActiveTarget;
            bool wireframe = GL.wireframe;
            var target = new RenderTexture(8, 8, 0);
            try
            {
                SetCallerState();
                GL.wireframe = true;
                var expected = GfxStream.Current;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                EqualState(expected);
                Assert.True(GL.wireframe);
                Assert.Same(active, RenderTexture.ActiveTarget);
            }
            finally
            {
                GL.wireframe = wireframe;
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(saved);
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void SupersizedCapturePreservesTheCallerStream()
        {
            var saved = GfxStream.Current;
            var active = RenderTexture.ActiveTarget;
            Texture2D capture = null;
            try
            {
                SetCallerState();
                var expected = GfxStream.Current;
                capture = ScreenCapture.CaptureScreenshotAsTexture(2);
                EqualState(expected);
                Assert.Same(active, RenderTexture.ActiveTarget);
            }
            finally
            {
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(saved);
                if (capture != null) Object.DestroyImmediate(capture);
            }
        }

        [Fact]
        public void FailedCameraCallbackRestoresTheCallerStream()
        {
            var saved = GfxStream.Current;
            var active = RenderTexture.ActiveTarget;
            var go = new GameObject("Screen capture state test");
            var camera = go.AddComponent<Camera>();
            Camera.CameraCallback fail = c => { if (c == camera) throw new InvalidOperationException("capture callback failure"); };
            Camera.onPreRender += fail;
            try
            {
                SetCallerState();
                var expected = GfxStream.Current;
                Assert.Throws<InvalidOperationException>(() => ScreenCapture.CaptureScreenshotAsTexture(2));
                EqualState(expected);
                Assert.Same(active, RenderTexture.ActiveTarget);
                Assert.Null(camera.targetTexture);
            }
            finally
            {
                Camera.onPreRender -= fail;
                Object.DestroyImmediate(go);
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(saved);
            }
        }
    }
}
