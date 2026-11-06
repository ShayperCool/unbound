using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The screen's camera stack (FrameBuilder): which full-screen cameras share an
    /// intermediate (measured: tests/projects/camerastack), and when a kept intermediate may no
    /// longer be joined.</summary>
    [Collection("Frames")]
    public class ScreenCameraStackTests
    {
        static RenderTexture Intermediate() => RenderTexture.GetTemporary(16, 9, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);

        [Fact]
        public void OnlyTheFramesRenderingJoinsAResolvedStack()
        {
            var texture = Intermediate();
            try
            {
                FrameBuilder.KeepScreenStack(texture);
                Assert.Null(FrameBuilder.JoinableScreenStack(true));
                FrameBuilder.MarkScreenStackResolved();
                Assert.Same(texture, FrameBuilder.JoinableScreenStack(true));
                // A script's Camera.Render() keeps its intermediate to itself.
                Assert.Null(FrameBuilder.JoinableScreenStack(false));
            }
            finally
            {
                FrameBuilder.EndScreenStack();
            }
            Assert.True(texture.m_PooledFree);
            Assert.Null(FrameBuilder.JoinableScreenStack(true));
        }

        [Fact]
        public void DrawingOnTheScreenAfterTheResolveEndsTheStack()
        {
            var texture = Intermediate();
            try
            {
                FrameBuilder.KeepScreenStack(texture);
                FrameBuilder.MarkScreenStackResolved();
                // A late command buffer, a blit or GL drawing on the screen binds it.
                GfxStream.SetTarget(0);
                Assert.Null(FrameBuilder.JoinableScreenStack(true));
            }
            finally
            {
                FrameBuilder.EndScreenStack();
            }
        }

        [Fact]
        public void EndingTheStackReleasesOnlyItsOwnAcquisition()
        {
            var texture = Intermediate();
            FrameBuilder.KeepScreenStack(texture);
            RenderTexture.ReleaseTemporary(texture);
            var again = Intermediate();
            try
            {
                Assert.Same(texture, again);
                FrameBuilder.EndScreenStack();
                Assert.False(again.m_PooledFree);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(again);
            }
        }

        [Fact]
        public void AStackTakesWhatAnyOfItsCamerasAsksFor()
        {
            var objects = new List<GameObject>();
            int antiAliasing = QualitySettings.antiAliasing;
            Camera Make(CameraClearFlags clear, bool hdr, bool msaa, Rect rect)
            {
                var go = new GameObject("StackCamera");
                objects.Add(go);
                var camera = go.AddComponent<Camera>();
                camera.clearFlags = clear;
                camera.allowHDR = hdr;
                camera.allowMSAA = msaa;
                camera.rect = rect;
                return camera;
            }
            try
            {
                QualitySettings.antiAliasing = 4;
                var full = new Rect(0f, 0f, 1f, 1f);
                var a = Make(CameraClearFlags.SolidColor, false, false, full);
                var b = Make(CameraClearFlags.Depth, true, true, full);
                var c = Make(CameraClearFlags.Nothing, false, false, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
                var d = Make(CameraClearFlags.Nothing, false, false, full);
                var e = Make(CameraClearFlags.Skybox, false, false, full);
                var f = Make(CameraClearFlags.Nothing, false, true, full);
                FrameBuilder.PlanScreenStacks(new List<Camera> { a, b, c, d, e, f });
                // A and B stack; the partial C ends it; D starts one of its own; E clears the color.
                Assert.Equal((true, 4), FrameBuilder.s_StackNeeds[a]);
                Assert.False(FrameBuilder.s_StackNeeds.ContainsKey(b));
                Assert.False(FrameBuilder.s_StackNeeds.ContainsKey(c));
                Assert.Equal((false, 1), FrameBuilder.s_StackNeeds[d]);
                Assert.Equal((false, 4), FrameBuilder.s_StackNeeds[e]);
                Assert.Equal(3, FrameBuilder.s_StackNeeds.Count);
            }
            finally
            {
                FrameBuilder.s_StackNeeds.Clear();
                QualitySettings.antiAliasing = antiAliasing;
                foreach (var o in objects) Object.DestroyImmediate(o);
            }
        }
    }
}
