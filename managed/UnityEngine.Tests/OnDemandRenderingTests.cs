using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    public class EndOfFrameRecorder : MonoBehaviour
    {
        internal readonly List<int> Resumed = new List<int>();

        internal IEnumerator EndOfFrames()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                Resumed.Add(Time.frameCount);
            }
        }
    }

    /// <summary>OnDemandRendering as tests/projects/coreapis measured it (Reference w4). The interval,
    /// its frame-start latch, the frame clock, the drawing-frame flag and the target frame rate are
    /// global: every test restores them. The tests add no player-loop registrations; they only
    /// trigger the engine's own, idempotent ones.</summary>
    [Collection("Frames")]
    public class OnDemandRenderingTests
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        // GfxStream's frame-start commands.
        const byte BeginFrameOp = 1, ColorSpaceOp = 19;

        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);

        static void Run(Type node) => typeof(PlayerLoopDriver).GetMethod("RunSystem", StaticPrivate)
            .Invoke(null, new object[] { new PlayerLoopSystem { type = node } });

        /// <summary>The frame starts: the clock advances and the interval is latched.</summary>
        static void BeginFrame(int frame)
        {
            Time.s_FrameCount = frame;
            OnDemandRendering.LatchFrameInterval();
        }

        sealed class Scope : IDisposable
        {
            readonly int m_Interval = OnDemandRendering.renderFrameInterval;
            readonly int m_Latched = OnDemandRendering.s_FrameInterval;
            readonly int m_Frame = Time.s_FrameCount;
            readonly int m_Rendered = Time.s_RenderedFrameCount;
            readonly int m_Target = Application.targetFrameRate;
            readonly bool m_InDrawingFrame = FrameBuilder.InDrawingFrame;

            internal Scope() => FrameBuilder.EnsureRegistered();

            public void Dispose()
            {
                OnDemandRendering.renderFrameInterval = m_Interval;
                OnDemandRendering.s_FrameInterval = m_Latched;
                Time.s_FrameCount = m_Frame;
                Time.s_RenderedFrameCount = m_Rendered;
                Application.targetFrameRate = m_Target;
                FrameBuilder.InDrawingFrame = m_InDrawingFrame;
            }
        }

        [Fact]
        public void OnDemandRenderingIntervalAndRate()
        {
            using var scope = new Scope();
            int target = Application.targetFrameRate;
            try
            {
                OnDemandRendering.renderFrameInterval = 0;
                Assert.Equal(1, OnDemandRendering.renderFrameInterval);
                Application.targetFrameRate = 50;
                OnDemandRendering.renderFrameInterval = 3;
                Assert.Equal(17, OnDemandRendering.effectiveRenderFrameRate);
                Assert.Equal(Time.frameCount % 3 == 0, OnDemandRendering.willCurrentFrameRender);
                Application.targetFrameRate = -1;
                Assert.Equal(-1, OnDemandRendering.effectiveRenderFrameRate);
            }
            finally
            {
                Application.targetFrameRate = target;
                OnDemandRendering.renderFrameInterval = 1;
            }
        }

        [Fact]
        public void TheLatchIsRegisteredOnceAfterTheTimeUpdate()
        {
            using var scope = new Scope();
            OnDemandRendering.EnsureRegistered();
            var actions = Engine[typeof(PL.TimeUpdate.WaitForLastPresentationAndUpdateTime)];
            int latch = actions.FindIndex(a => a.Method.DeclaringType == typeof(OnDemandRendering) && a.Method.Name == "LatchFrameInterval");
            int time = actions.FindIndex(a => a.Method.DeclaringType == typeof(PlayerLoopDriver) && a.Method.Name == "UpdateTime");
            Assert.True(time >= 0 && latch > time);
            Assert.Single(actions, a => a.Method.DeclaringType == typeof(OnDemandRendering));
        }

        /// <summary>The w4 window: interval 3 set in Update of w0, 2 in Update of w7, 1 in Update of
        /// w12 and 2 at the end of w13, from a frame with frameCount % 6 == 1. The screen follows the
        /// interval latched when each frame began; willCurrentFrameRender reads the current one and
        /// differs at w0, w7 and w12.</summary>
        [Fact]
        public void TheIntervalAtTheFrameStartDecidesTheScreenNotWillCurrentFrameRender()
        {
            using var scope = new Scope();
            OnDemandRendering.renderFrameInterval = 1;
            OnDemandRendering.LatchFrameInterval();
            bool[] screen = { true, false, true, false, false, true, false, false, false, true, false, true, false, true, false, true, false };
            bool[] will = { false, false, true, false, false, true, false, true, false, true, false, true, true, true, false, true, false };
            const int first = 37;
            for (int w = 0; w < screen.Length; w++)
            {
                BeginFrame(first + w);
                if (w == 0) OnDemandRendering.renderFrameInterval = 3;
                if (w == 7) OnDemandRendering.renderFrameInterval = 2;
                if (w == 12) OnDemandRendering.renderFrameInterval = 1;
                Assert.Equal(will[w], OnDemandRendering.willCurrentFrameRender);
                Assert.Equal(screen[w], OnDemandRendering.RendersThisFrame);
                if (w == 13) OnDemandRendering.renderFrameInterval = 2;
            }
            foreach (int w in new[] { 0, 7, 12 }) Assert.NotEqual(screen[w], will[w]);
        }

        [Fact]
        public void WaitForEndOfFrameResumesOnlyOnFramesThatRender()
        {
            using var scope = new Scope();
            var go = new GameObject("end of frame recorder");
            try
            {
                var recorder = go.AddComponent<EndOfFrameRecorder>();
                recorder.StartCoroutine(recorder.EndOfFrames());
                OnDemandRendering.renderFrameInterval = 3;
                foreach (int frame in new[] { 40, 41, 42 })
                {
                    BeginFrame(frame);
                    Run(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks));
                }
                Assert.Equal(new[] { 42 }, recorder.Resumed);
                // Interval 1 from this frame's Update: willCurrentFrameRender turns true at once,
                // but the frame began with 3 and does not render; the next one does.
                BeginFrame(43);
                OnDemandRendering.renderFrameInterval = 1;
                Assert.True(OnDemandRendering.willCurrentFrameRender);
                Run(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks));
                Assert.Equal(new[] { 42 }, recorder.Resumed);
                BeginFrame(44);
                Run(typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks));
                Assert.Equal(new[] { 42, 44 }, recorder.Resumed);
                recorder.StopAllCoroutines();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        sealed class OverlaySource : FrameBuilder.IDrawSource
        {
            internal int Overlays;
            internal readonly List<Camera> Cameras = new List<Camera>();
            public void CollectCameraDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) => Cameras.Add(camera.Camera);
            public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into) => Overlays++;
        }

        /// <summary>A frame that does not render still renders the camera that renders into a
        /// texture, through the same camera path (draw collection, callbacks), after the frame's
        /// preparation (sprite mask bookkeeping, color space); it starts no screen frame and draws
        /// no screen camera or overlay. Time.renderedFrameCount and InDrawingFrame keep their
        /// current behaviour.</summary>
        [Fact]
        public void ASkippedFrameRendersOnlyTheCamerasThatRenderIntoTextures()
        {
            using var scope = new Scope();
            var rtGo = new GameObject("on-demand texture camera");
            var screenGo = new GameObject("on-demand screen camera");
            var target = new RenderTexture(16, 16, 24);
            var source = new OverlaySource();
            var events = new List<string>();
            var stencil = (IDictionary)typeof(SpriteMasking).GetField("s_Stencil", StaticPrivate).GetValue(null);
            var stale = (ulong.MaxValue - 1, ulong.MaxValue - 2);
            Camera rt = null, screen = null;
            byte firstCommand = 0;
            Camera.CameraCallback preCull = c =>
            {
                if (c != rt && c != screen) return;
                events.Add(c.name + " OnPreCull");
                if (c == rt && GfxStream.Recorded.Length > 0) firstCommand = GfxStream.Recorded[0];
            };
            Camera.CameraCallback postRender = c => { if (c == rt || c == screen) events.Add(c.name + " OnPostRender"); };
            Camera.onPreCull += preCull;
            Camera.onPostRender += postRender;
            FrameBuilder.Sources.Add(source);
            try
            {
                rt = rtGo.AddComponent<Camera>();
                rt.targetTexture = target;
                screen = screenGo.AddComponent<Camera>();
                OnDemandRendering.renderFrameInterval = 3;

                // frameCount 41 with interval 3 latched: not rendered.
                BeginFrame(41);
                stencil[stale] = new List<SpriteMask>();
                GfxStream.Flush();
                int rendered = Time.s_RenderedFrameCount;
                Run(typeof(PL.PostLateUpdate.FinishFrameRendering));
                Assert.False(OnDemandRendering.RendersThisFrame);
                Assert.Equal(new[] { rt.name + " OnPreCull", rt.name + " OnPostRender" }, events);
                Assert.Contains(rt, source.Cameras);
                Assert.DoesNotContain(screen, source.Cameras);
                Assert.Equal(0, source.Overlays);
                Assert.Equal(ColorSpaceOp, firstCommand);
                Assert.False(stencil.Contains(stale));
                Assert.Equal(rendered + 1, Time.s_RenderedFrameCount);
                Assert.True(FrameBuilder.InDrawingFrame);
                Run(typeof(PL.PostLateUpdate.GUIClearEvents));
                Assert.False(FrameBuilder.InDrawingFrame);

                // frameCount 42: rendered, the texture camera first, then the screen camera and the overlay.
                events.Clear();
                source.Cameras.Clear();
                BeginFrame(42);
                stencil[stale] = new List<SpriteMask>();
                GfxStream.Flush();
                rendered = Time.s_RenderedFrameCount;
                Run(typeof(PL.PostLateUpdate.FinishFrameRendering));
                Assert.True(OnDemandRendering.RendersThisFrame);
                Assert.Equal(new[] { rt.name + " OnPreCull", rt.name + " OnPostRender", screen.name + " OnPreCull", screen.name + " OnPostRender" }, events);
                Assert.Contains(screen, source.Cameras);
                Assert.True(source.Cameras.IndexOf(rt) >= 0 && source.Cameras.IndexOf(rt) < source.Cameras.IndexOf(screen));
                Assert.Equal(1, source.Overlays);
                Assert.Equal(BeginFrameOp, firstCommand);
                Assert.False(stencil.Contains(stale));
                Assert.Equal(rendered + 1, Time.s_RenderedFrameCount);
                Assert.True(FrameBuilder.InDrawingFrame);
            }
            finally
            {
                stencil.Remove(stale);
                FrameBuilder.Sources.Remove(source);
                Camera.onPreCull -= preCull;
                Camera.onPostRender -= postRender;
                Object.DestroyImmediate(rtGo);
                Object.DestroyImmediate(screenGo);
                Object.DestroyImmediate(target);
            }
        }
    }
}
