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
    }
}
