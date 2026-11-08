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

        void Called() => Log.Add("invoked");
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
        readonly TimedWaitRunner m_Runner = new GameObject("Timed waits").AddComponent<TimedWaitRunner>();

        public void Dispose()
        {
            Object.DestroyImmediate(m_Runner.gameObject);
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
            m_Runner.Invoke("Called", 0f);
            Run(typeof(UpdatePoint));
            Assert.Equal(new[] { "invoked" }, m_Runner.Log);
        }

        // An Invoke comes due at the fixed step whose fixed clock reaches it, before that frame's Update.
        [Fact]
        public void InvokeComesDueInsideTheFixedStepThatReachesItsTime()
        {
            Frame(5);
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
    }
}
