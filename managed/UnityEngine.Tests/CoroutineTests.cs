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

        internal IEnumerator WaitsUntil(CustomYieldInstruction instruction, string name)
        {
            yield return instruction;
            Log.Add(name + " resumed");
        }

        internal IEnumerator Waits(AsyncOperation operation, string name)
        {
            yield return operation;
            Log.Add(name + " resumed done=" + operation.isDone);
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

        /// <summary>Measured (tests/projects/navbaked resume.*): the first coroutine that yielded a
        /// running operation resumes as it completes, before its completed handlers, wherever in the
        /// frame that is; another coroutine yielding it resumes at the next Update's coroutine point.</summary>
        [Fact]
        public void TheFirstWaitingCoroutineResumesAsTheOperationCompletes()
        {
            var frames = Time.s_FrameCount;
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            try
            {
                var operation = new AsyncOperation();
                operation.completed += _ => runner.Log.Add("handler before");
                runner.StartCoroutine(runner.Waits(operation, "first"));
                operation.completed += _ => runner.Log.Add("handler after");
                runner.StartCoroutine(runner.Waits(operation, "second"));
                NextFrame();
                Assert.Empty(runner.Log);
                operation.Complete();
                Assert.Equal(new[] { "first resumed done=True", "handler before", "handler after" }, runner.Log);
                NextFrame();
                Assert.Equal("second resumed done=True", runner.Log[3]);
                Assert.Equal(4, runner.Log.Count);
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Time.s_FrameCount = frames;
            }
        }

        /// <summary>A stopped coroutine is not resumed by the completion; an operation already done
        /// resumes its coroutine at the next frame's coroutine point (measured: navbaked
        /// resume.resources).</summary>
        [Fact]
        public void AStoppedOrLateWaiterResumesOnlyAtTheCoroutinePoint()
        {
            var frames = Time.s_FrameCount;
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            try
            {
                var operation = new AsyncOperation();
                var stopped = runner.StartCoroutine(runner.Waits(operation, "stopped"));
                runner.StopCoroutine(stopped);
                operation.Complete();
                Assert.Empty(runner.Log);
                runner.StartCoroutine(runner.Waits(operation, "late"));
                Assert.Empty(runner.Log);
                NextFrame();
                Assert.Equal(new[] { "late resumed done=True" }, runner.Log);
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Time.s_FrameCount = frames;
            }
        }

        /// <summary>A coroutine stopped while waiting hands the operation's waiter place to the next
        /// one that yields it (Unbound's choice; unmeasured).</summary>
        [Fact]
        public void AStoppedWaitersPlacePassesToTheNextCoroutine()
        {
            var frames = Time.s_FrameCount;
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            try
            {
                var operation = new AsyncOperation();
                runner.StopCoroutine(runner.StartCoroutine(runner.Waits(operation, "stopped")));
                runner.StartCoroutine(runner.Waits(operation, "next"));
                operation.Complete();
                Assert.Equal(new[] { "next resumed done=True" }, runner.Log);
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Time.s_FrameCount = frames;
            }
        }

        /// <summary>A wait's check that completes an operation resumes that operation's coroutine
        /// in the middle of the coroutine phase without disturbing it.</summary>
        [Fact]
        public void AnOperationCompletedByAWaitsCheckResumesItsCoroutine()
        {
            var frames = Time.s_FrameCount;
            var runner = new GameObject("Runner").AddComponent<CoroutineRunner>();
            try
            {
                var operation = new AsyncOperation();
                runner.StartCoroutine(runner.WaitsUntil(new WaitUntil(() =>
                {
                    if (Time.s_FrameCount > frames + 1) operation.Complete();
                    return operation.isDone;
                }), "check"));
                runner.StartCoroutine(runner.Waits(operation, "operation"));
                NextFrame();
                Assert.Empty(runner.Log);
                NextFrame();
                Assert.Equal(new[] { "operation resumed done=True", "check resumed" }, runner.Log);
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Time.s_FrameCount = frames;
            }
        }
    }
}
