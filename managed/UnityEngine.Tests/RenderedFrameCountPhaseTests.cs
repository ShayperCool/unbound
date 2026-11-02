using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>Where Time.renderedFrameCount advances. tests/projects/coreapis (Reference w4) shows
    /// camera callbacks, OnGUI and WaitForEndOfFrame reading the count before it advances and the
    /// next Update reading it advanced, on rendered and skipped frames alike. These controls only
    /// read the default loop and the registrations, and invoke the counter's own action. They run no
    /// other player-loop action, coroutine or rendering. The frame globals and the engine
    /// registrations are restored afterwards.</summary>
    [Collection("Frames")]
    public class RenderedFrameCountPhaseTests
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        const string Advance = "AdvanceRenderedFrameCount";

        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);

        static bool IsAdvance(Action a) => a.Method.DeclaringType == typeof(PlayerLoopDriver) && a.Method.Name == Advance;

        sealed class Scope : IDisposable
        {
            readonly Dictionary<Type, List<Action>> m_Engine = new Dictionary<Type, List<Action>>();
            readonly int m_Rendered = Time.s_RenderedFrameCount;
            readonly int m_Frame = Time.s_FrameCount;
            readonly int m_Interval = OnDemandRendering.renderFrameInterval;
            readonly int m_Latched = OnDemandRendering.s_FrameInterval;

            internal Scope()
            {
                foreach (var pair in Engine) m_Engine.Add(pair.Key, pair.Value);
            }

            public void Dispose()
            {
                // Writes only what changed: an unchanged registration table is left untouched.
                var engine = Engine;
                foreach (var key in new List<Type>(engine.Keys))
                    if (!m_Engine.ContainsKey(key)) engine.Remove(key);
                foreach (var pair in m_Engine)
                    if (!engine.TryGetValue(pair.Key, out var list) || !ReferenceEquals(list, pair.Value)) engine[pair.Key] = pair.Value;
                Time.s_RenderedFrameCount = m_Rendered;
                Time.s_FrameCount = m_Frame;
                OnDemandRendering.renderFrameInterval = m_Interval;
                OnDemandRendering.s_FrameInterval = m_Latched;
            }
        }

        static Type[] Nodes(PlayerLoopSystem loop, Type phase)
        {
            foreach (var p in loop.subSystemList)
                if (p.type == phase) return Array.ConvertAll(p.subSystemList, s => s.type);
            throw new InvalidOperationException(phase.Name);
        }

        /// <summary>In the default loop, GUIClearEvents directly follows TriggerEndOfFrameCallbacks,
        /// which follows FinishFrameRendering. PostLateUpdate is the last phase, so the next frame's
        /// Update follows.</summary>
        [Fact]
        public void GUIClearEventsIsTheFirstNodeAfterTheEndOfFrameCallbacks()
        {
            var loop = PlayerLoopDriver.DefaultLoop();
            var phases = Array.ConvertAll(loop.subSystemList, p => p.type);
            Assert.Equal(typeof(PL.PostLateUpdate), phases[phases.Length - 1]);
            Assert.True(Array.IndexOf(phases, typeof(PL.TimeUpdate)) < Array.IndexOf(phases, typeof(PL.Update)));
            var post = Nodes(loop, typeof(PL.PostLateUpdate));
            int render = Array.IndexOf(post, typeof(PL.PostLateUpdate.FinishFrameRendering));
            int eof = Array.IndexOf(post, typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks));
            int clear = Array.IndexOf(post, typeof(PL.PostLateUpdate.GUIClearEvents));
            Assert.True(render >= 0 && render < eof);
            Assert.Equal(eof + 1, clear);
        }

        /// <summary>The count has one action, at GUIClearEvents. Nothing of the driver's own runs
        /// at FinishFrameRendering, where the count advanced before the cameras rendered.</summary>
        [Fact]
        public void TheCountAdvancesOnlyAtGUIClearEvents()
        {
            using var scope = new Scope();
            var at = new List<Type>();
            foreach (var pair in Engine)
                foreach (var a in pair.Value)
                    if (IsAdvance(a)) at.Add(pair.Key);
            Assert.Equal(new[] { typeof(PL.PostLateUpdate.GUIClearEvents) }, at);
            if (!Engine.TryGetValue(typeof(PL.PostLateUpdate.FinishFrameRendering), out var rendering)) return;
            foreach (var a in rendering)
            {
                var owner = a.Method.DeclaringType;
                Assert.False(owner == typeof(PlayerLoopDriver) || owner?.DeclaringType == typeof(PlayerLoopDriver), a.Method.Name);
            }
        }

        /// <summary>The w4 window's interval 3: one advance per frame, whether the frame draws the
        /// screen or not. Only the counter's action is invoked.</summary>
        [Fact]
        public void TheCountAdvancesOncePerRenderedOrSkippedFrame()
        {
            using var scope = new Scope();
            var advance = Engine[typeof(PL.PostLateUpdate.GUIClearEvents)].Find(IsAdvance);
            Assert.NotNull(advance);
            OnDemandRendering.renderFrameInterval = 3;
            OnDemandRendering.LatchFrameInterval();
            Time.s_RenderedFrameCount = 100;
            var drew = new List<bool>();
            for (int frame = 37; frame < 43; frame++)
            {
                Time.s_FrameCount = frame;
                drew.Add(OnDemandRendering.RendersThisFrame);
                int before = Time.renderedFrameCount;
                advance();
                Assert.Equal(before + 1, Time.renderedFrameCount);
            }
            Assert.Equal(new[] { false, false, true, false, false, true }, drew);
            Assert.Equal(106, Time.renderedFrameCount);
        }
    }
}
