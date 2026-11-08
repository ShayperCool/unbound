using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class CoroutineRunner : MonoBehaviour
    {
        internal readonly List<string> Log = new List<string>();

        internal IEnumerator StopsItself()
        {
            Log.Add("start");
            yield return null;
            Log.Add("resumed");
            StopAllCoroutines();
            Log.Add("after stop");
        }

        internal IEnumerator Outer()
        {
            yield return Inner();
            Log.Add("outer done");
        }

        internal static IEnumerator EndsAtOnce()
        {
            yield break;
        }

        IEnumerator Inner()
        {
            Log.Add("inner");
            yield return null;
            Log.Add("inner done");
        }
    }

    /// <summary>Advances the global frame count, so it shares a collection with the other tests that do.</summary>
    [Collection("Frames")]
    public class CoroutineTests
    {
        static void NextFrame()
        {
            Time.s_FrameCount++;
            Coroutines.Run(Coroutines.Phase.Update);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1000.0)]
        public void SecondsWaitUsesThePreciseScaledClock(double start)
        {
            var (time, inFixed, frames) = (Time.s_Time, Time.s_InFixedTimeStep, Time.s_FrameCount);
            try
            {
                Time.s_InFixedTimeStep = false;
                Time.s_FrameCount = 1;
                Time.s_Time = start;
                double began = Time.timeAsDouble;
                const float duration = 0.1f;
                var coroutine = new Coroutine();
                coroutine.Wait(new WaitForSeconds(duration));
                Time.s_FrameCount++;
                Time.s_Time = start + duration - 1e-9;
                Assert.False(coroutine.ReadyIn(Coroutines.Phase.Update));
                Time.s_Time = start + duration;
                Assert.False(coroutine.ReadyIn(Coroutines.Phase.FixedUpdate));
                Assert.True(coroutine.ReadyIn(Coroutines.Phase.Update));
                Assert.True(Time.timeAsDouble - began >= duration);
                // Subtracting rounded float timestamps is not a duration guarantee.
                if (start == 1000.0)
                    Assert.True(Time.time - (float)began < duration);
            }
            finally
            {
                (Time.s_Time, Time.s_InFixedTimeStep, Time.s_FrameCount) = (time, inFixed, frames);
            }
        }

        [Fact]
        public void ARoutineThatStopsItselfEndsQuietly()
        {
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            runner.StartCoroutine(runner.StopsItself());
            Assert.Equal(new[] { "start" }, runner.Log);
            NextFrame();
            Assert.Equal(new[] { "start", "resumed", "after stop" }, runner.Log);
            NextFrame();
            Assert.Equal(3, runner.Log.Count);
        }

        [Fact]
        public void NestedRoutinesResumeTheirParent()
        {
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            runner.StartCoroutine(runner.Outer());
            Assert.Equal(new[] { "inner" }, runner.Log);
            NextFrame();
            Assert.Equal(new[] { "inner", "inner done", "outer done" }, runner.Log);
        }

        [Fact]
        public void ACoroutineThatEndsInItsFirstStepGivesNull()
        {
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            Assert.Null(runner.StartCoroutine(CoroutineRunner.EndsAtOnce()));
            Assert.NotNull(runner.StartCoroutine(runner.Outer()));
        }
    }
}
