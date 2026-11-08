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

        /// <summary>The stream's pending bytes are put back: the commands these tests emit never
        /// reach a renderer.</summary>
        sealed class StreamScope : System.IDisposable
        {
            static readonly System.Reflection.FieldInfo Length =
                typeof(GfxStream).GetField("s_Length", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            readonly int m_Length = (int)Length.GetValue(null);
            readonly RenderTexture m_Active = RenderTexture.active;

            public void Dispose()
            {
                RenderTexture.active = m_Active;
                Length.SetValue(null, m_Length);
            }
        }

        [Fact]
        public void BindingTheScreenAloneKeepsTheStack()
        {
            var texture = Intermediate();
            using var stream = new StreamScope();
            try
            {
                FrameBuilder.KeepScreenStack(texture);
                FrameBuilder.MarkScreenStackResolved();
                // A script restoring the screen as the active target after an offscreen blit.
                RenderTexture.active = texture;
                RenderTexture.active = null;
                GfxStream.SetTarget(0);
                Assert.Same(texture, FrameBuilder.JoinableScreenStack(true));
            }
            finally
            {
                FrameBuilder.EndScreenStack();
            }
        }

        [Fact]
        public void ClearingTheBoundScreenEndsTheStack()
        {
            var texture = Intermediate();
            using var stream = new StreamScope();
            try
            {
                // The resolve leaves the screen bound; what comes next draws without binding it.
                GfxStream.SetTarget(0);
                FrameBuilder.KeepScreenStack(texture);
                FrameBuilder.MarkScreenStackResolved();
                RenderTexture.active = null;
                GL.Clear(true, true, Color.red);
                Assert.Null(FrameBuilder.JoinableScreenStack(true));
            }
            finally
            {
                FrameBuilder.EndScreenStack();
            }
        }

        [Fact]
        public void DrawingOnTheBoundScreenEndsTheStack()
        {
            var texture = Intermediate();
            using var stream = new StreamScope();
            try
            {
                GfxStream.SetTarget(0);
                FrameBuilder.KeepScreenStack(texture);
                FrameBuilder.MarkScreenStackResolved();
                // What a late command buffer's DrawMesh or a GL draw emits on the screen.
                GfxStream.Draw(1, 0, 1, GfxStream.AllPasses, Matrix4x4.identity, Color.white);
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
                Assert.Empty(FrameBuilder.s_SingleSampleStacks);

                // A full-screen deferred camera that keeps the color continues a stack, which then
                // renders with one sample (tests/projects/camerastack *.full.*.def.persp); a partial
                // one, or one that clears its color, leaves the stack as it is.
                Camera Deferred(CameraClearFlags clear, Rect rect)
                {
                    var camera = Make(clear, true, true, rect);
                    camera.renderingPath = RenderingPath.DeferredShading;
                    return camera;
                }
                var g = Make(CameraClearFlags.SolidColor, false, true, full);
                var h = Deferred(CameraClearFlags.Depth, full);
                var i = Make(CameraClearFlags.SolidColor, false, true, full);
                var j = Deferred(CameraClearFlags.Nothing, new Rect(0.1f, 0.1f, 0.5f, 0.5f));
                var k = Make(CameraClearFlags.SolidColor, false, true, full);
                var l = Deferred(CameraClearFlags.SolidColor, full);
                var m = Make(CameraClearFlags.Nothing, false, true, full);
                FrameBuilder.PlanScreenStacks(new List<Camera> { g, h, i, j, k, l, m });
                Assert.Equal(RenderingPath.DeferredShading, h.actualRenderingPath);
                Assert.Equal((false, 1), FrameBuilder.s_StackNeeds[g]);
                Assert.Equal(new[] { g }, FrameBuilder.s_SingleSampleStacks);
                Assert.Equal((false, 4), FrameBuilder.s_StackNeeds[i]);
                Assert.Equal((false, 4), FrameBuilder.s_StackNeeds[k]);
                // After the deferred camera, a camera keeping the color starts a stack of its own.
                Assert.Equal((false, 4), FrameBuilder.s_StackNeeds[m]);
            }
            finally
            {
                FrameBuilder.s_StackNeeds.Clear();
                FrameBuilder.s_SingleSampleStacks.Clear();
                QualitySettings.antiAliasing = antiAliasing;
                foreach (var o in objects) Object.DestroyImmediate(o);
            }
        }
    }
}
