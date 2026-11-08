using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using FixedPoint = UnityEngine.PlayerLoop.FixedUpdate.ScriptRunDelayedFixedFrameRate;
using UpdatePoint = UnityEngine.PlayerLoop.Update.ScriptRunDelayedDynamicFrameRate;

namespace UnityEngine.Tests
{
    public class TimedWaitRunner : MonoBehaviour
    {
        internal readonly List<string> Log = new List<string>();

        internal IEnumerator Waits(float seconds, string name)
        {
            yield return new WaitForSeconds(seconds);
            Log.Add(name);
        }

        internal async Awaitable Awaits(float seconds, string name)
        {
            await Awaitable.WaitForSecondsAsync(seconds);
            Log.Add(name);
        }

        internal bool InvokingAtCall;
        void Called() { InvokingAtCall = IsInvoking(nameof(Called)); Log.Add("invoked"); }
        void CancelledRepeat() { Log.Add("repeat"); CancelInvoke(nameof(CancelledRepeat)); }
        void Neighbor() => Log.Add("neighbor");
        void CancelsNeighbor() { Log.Add("cancel tick"); CancelInvoke(nameof(Neighbor)); }
    }

    /// <summary>
    /// When WaitForSeconds, Awaitable.WaitForSecondsAsync and Invoke come due, at the player loop's
    /// real check points (6000.0.71f1, tests/projects/waitforseconds). Frames are simulated through
    /// the clocks; each test restores them.
    /// </summary>
    [Collection("Frames")]
    public sealed class TimedWaitTests : IDisposable
    {
        readonly (double Time, double Fixed, int Frames, bool InFixed) m_Clocks =
            (Time.s_Time, Time.s_FixedTime, Time.s_FrameCount, Time.s_InFixedTimeStep);
        readonly string m_Release = ProjectInfo.UnityVersion;
        public TimedWaitTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        readonly TimedWaitRunner m_Runner = new GameObject("Timed waits").AddComponent<TimedWaitRunner>();

        public void Dispose()
        {
            Object.DestroyImmediate(m_Runner.gameObject);
            ProjectInfo.UnityVersion = m_Release;
            (Time.s_Time, Time.s_FixedTime, Time.s_FrameCount, Time.s_InFixedTimeStep) = m_Clocks;
        }

        static void Run(Type point)
        {
            foreach (var action in PlayerLoopDriver.Registered(point)) action();
        }

        static void Frame(double time)
        {
            Time.s_FrameCount++;
            Time.s_Time = time;
        }

        /// <summary>One fixed step at <paramref name="fixedTime"/>; <paramref name="body"/> stands for
        /// the fixed-step scripts.</summary>
        static void FixedStep(double fixedTime, Action body = null)
        {
            Time.s_InFixedTimeStep = true;
            Time.s_FixedTime = fixedTime;
            try
            {
                body?.Invoke();
                Run(typeof(FixedPoint));
            }
            finally { Time.s_InFixedTimeStep = false; }
        }

        // A zero wait started during Update is not over in that frame, even though its time has come:
        // the next frame's fixed steps run first (what a script that shows a control in a fixed step
        // and hides it after a zero wait relies on).
        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void AWaitThatIsAlreadyDueResumesInTheNextFrameAfterItsFixedSteps(float seconds)
        {
            Frame(5);
            m_Runner.StartCoroutine(m_Runner.Waits(seconds, "coroutine"));
            _ = m_Runner.Awaits(seconds, "awaitable");
            Run(typeof(UpdatePoint));
            Assert.Empty(m_Runner.Log);

            Frame(5.125);
            FixedStep(5.1, () => m_Runner.Log.Add("fixed step"));
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "fixed step", "coroutine", "awaitable" }, m_Runner.Log);
        }

        // Started in a fixed step, a wait counts from the frame's time, not from the fixed clock
        // Time.time reads there (which would make it due one frame earlier here).
        [Fact]
        public void AWaitStartedInAFixedStepCountsFromTheFrameTime()
        {
            Frame(5);
            FixedStep(4.9, () =>
            {
                m_Runner.StartCoroutine(m_Runner.Waits(0.3f, "coroutine"));
                _ = m_Runner.Awaits(0.3f, "awaitable");
            });
            Run(typeof(UpdatePoint));
            Frame(5.25);
            Run(typeof(UpdatePoint));
            Assert.Empty(m_Runner.Log);
            Frame(5.375);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "coroutine", "awaitable" }, m_Runner.Log);
        }

        // A wait shorter than a frame ends in the next frame, not one later.
        [Fact]
        public void AWaitShorterThanAFrameEndsInTheNextFrame()
        {
            Frame(5);
            m_Runner.StartCoroutine(m_Runner.Waits(0.1f, "coroutine"));
            Frame(5.125);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "coroutine" }, m_Runner.Log);
        }

        [Fact]
        public void InvokeWithoutDelayFromUpdateRunsInThatFrame()
        {
            Frame(5);
            FixedStep(5);
            m_Runner.Invoke("Called", 0f);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "invoked" }, m_Runner.Log);
        }

        // The fixed-step check reads that step's clock (tests/projects/invokesteps).
        [Fact]
        public void InvokeComesDueInsideTheFixedStepThatReachesItsTime()
        {
            Frame(5);
            FixedStep(5);
            m_Runner.Invoke("Called", 0.3f);
            Run(typeof(UpdatePoint));
            Frame(5.25);
            FixedStep(5.15);
            FixedStep(5.25);
            Run(typeof(UpdatePoint));
            Assert.Empty(m_Runner.Log);

            Frame(5.375);
            FixedStep(5.28);
            Assert.Empty(m_Runner.Log);
            FixedStep(5.32);
            Assert.Equal(new[] { "invoked" }, m_Runner.Log);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "invoked" }, m_Runner.Log);
        }

        [Fact]
        public void InvokeSchedulesFromTheEarlierClockAndRemovesOneShotBeforeCalling()
        {
            Frame(5);
            FixedStep(4.9);
            m_Runner.Invoke("Called", 0.3f);
            Run(typeof(UpdatePoint));
            Assert.Empty(m_Runner.Log);
            Frame(5.25);
            FixedStep(5.18);
            Assert.Empty(m_Runner.Log);
            FixedStep(5.22);
            Assert.Equal(new[] { "invoked" }, m_Runner.Log);
            Assert.False(m_Runner.InvokingAtCall);
        }

        [Fact]
        public void LateRepeatsFireOnceAtEachDelayedPointAndRetainTheirCadence()
        {
            Frame(5);
            FixedStep(5);
            m_Runner.InvokeRepeating("Called", 0f, 0.07f);
            Run(typeof(UpdatePoint));
            Run(typeof(UnityEngine.PlayerLoop.PostLateUpdate.ScriptRunDelayedDynamicFrameRate));
            Assert.Single(m_Runner.Log);
            Frame(5.25);
            FixedStep(5.1);
            FixedStep(5.2);
            Run(typeof(UpdatePoint));
            Run(typeof(UnityEngine.PlayerLoop.PostLateUpdate.ScriptRunDelayedDynamicFrameRate));
            Assert.Equal(4, m_Runner.Log.Count);
            Assert.True(m_Runner.InvokingAtCall);
            m_Runner.CancelInvoke();
        }

        [Fact]
        public void ARepeatCanCancelItselfWithoutBeingReinserted()
        {
            Frame(5);
            FixedStep(5);
            m_Runner.InvokeRepeating("CancelledRepeat", 0f, 0.001f);
            Run(typeof(UpdatePoint));
            Frame(5.25);
            FixedStep(5.1);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "repeat" }, m_Runner.Log);
            Assert.False(m_Runner.IsInvoking());
        }

        [Fact]
        public void InterleavedLateRepeatsCanBeVisitedAgainInDeadlineOrder()
        {
            Frame(5);
            FixedStep(4);
            m_Runner.InvokeRepeating("Called", 0f, 0.125f);
            m_Runner.InvokeRepeating("Called", 0f, 0.125f);
            Run(typeof(UpdatePoint));
            Assert.Equal(18, m_Runner.Log.Count);
            m_Runner.CancelInvoke();
        }

        [Fact]
        public void CancelledCallsDoNotChangeRepeatTraversal()
        {
            Frame(5);
            FixedStep(4);
            m_Runner.InvokeRepeating("Called", 0f, 0.1f);
            m_Runner.Invoke("Neighbor", 0.05f);
            m_Runner.CancelInvoke("Neighbor");
            Run(typeof(UpdatePoint));
            Assert.Single(m_Runner.Log);
            m_Runner.CancelInvoke();
        }

        [Fact]
        public void ARepeatReinsertedBeforeItsCallbackRemainsVisibleAfterNeighborCancellation()
        {
            Frame(5);
            FixedStep(4);
            m_Runner.InvokeRepeating("CancelsNeighbor", 0f, 0.1f);
            m_Runner.Invoke("Neighbor", 0.05f);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "cancel tick", "cancel tick" }, m_Runner.Log);
            m_Runner.CancelInvoke();
        }

        [Fact]
        public void NonFiniteDeadlinesRemainPendingWithoutHangingDelivery()
        {
            Frame(5);
            FixedStep(5);
            m_Runner.InvokeRepeating("Called", float.NaN, 0.1f);
            m_Runner.InvokeRepeating("Neighbor", float.NaN, 0.1f);
            Run(typeof(UpdatePoint));
            Assert.Empty(m_Runner.Log);
            Assert.True(m_Runner.IsInvoking("Called"));
            m_Runner.CancelInvoke();
        }
    }
}
