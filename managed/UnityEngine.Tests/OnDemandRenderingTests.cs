using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>OnDemandRendering as tests/projects/coreapis measured it (Reference w4). The interval,
    /// its frame-start latch, the frame clock, the drawing-frame flag and the target frame rate are
    /// global: every test restores them. The tests add no player-loop registrations; they only
    /// trigger the engine's own, idempotent ones.</summary>
    [Collection("Frames")]
    public class OnDemandRenderingTests
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        // GfxStream's frame-start commands.
        const byte DepthBiasOp = 17, StateOverrideOp = 18, ColorSpaceOp = 19;

        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);

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

        /// <summary>Preparing a skipped frame resets the same mask/raster defaults without a
        /// BeginFrame command, which would advance the screen ring. No renderer, player-loop
        /// callback or unrelated pending coroutine is run by this control.</summary>
        [Fact]
        public void AnOffscreenFrameResetsMaskAndRasterStateWithoutStartingAScreenFrame()
        {
            var bufferField = typeof(GfxStream).GetField("s_Buffer", StaticPrivate);
            var lengthField = typeof(GfxStream).GetField("s_Length", StaticPrivate);
            var overrideField = typeof(GfxStream).GetField("<CurrentOverride>k__BackingField", StaticPrivate);
            var masksField = typeof(SpriteMasking).GetField("s_Masks", StaticPrivate);
            var stencil = (IDictionary)typeof(SpriteMasking).GetField("s_Stencil", StaticPrivate).GetValue(null);
            var buffer = (byte[])bufferField.GetValue(null);
            var length = (int)lengthField.GetValue(null);
            var pending = GfxStream.Recorded.ToArray();
            var stateOverride = GfxStream.CurrentOverride;
            var masks = masksField.GetValue(null);
            var entries = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in stencil) entries.Add(entry);
            try
            {
                GfxStream.DepthBias(3f, 4f);
                GfxStream.StateOverride(new RenderStateBlock(RenderStateMask.Raster));
                Assert.True(GfxStream.CurrentOverride.HasValue);
                lengthField.SetValue(null, 0);
                stencil[(ulong.MaxValue - 1, ulong.MaxValue - 2)] = new List<SpriteMask>();

                GfxStream.BeginOffscreenFrame(ColorSpace.Linear);

                Assert.Empty(stencil);
                Assert.Null(GfxStream.CurrentOverride);
                var commands = GfxStream.Recorded.ToArray();
                Assert.Equal(16, commands.Length);
                Assert.Equal(ColorSpaceOp, commands[0]);
                Assert.Equal(1, BitConverter.ToInt32(commands, 1));
                Assert.Equal(DepthBiasOp, commands[5]);
                Assert.Equal(0f, BitConverter.ToSingle(commands, 6));
                Assert.Equal(0f, BitConverter.ToSingle(commands, 10));
                Assert.Equal(StateOverrideOp, commands[14]);
                Assert.Equal((byte)0, commands[15]);
            }
            finally
            {
                pending.CopyTo(buffer, 0);
                bufferField.SetValue(null, buffer);
                lengthField.SetValue(null, length);
                overrideField.SetValue(null, stateOverride);
                masksField.SetValue(null, masks);
                stencil.Clear();
                foreach (var entry in entries) stencil.Add(entry.Key, entry.Value);
            }
        }
    }
}
