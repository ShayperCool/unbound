using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    // The animation job producer, measured on the Unity 6000.0.71f1 Editor in
    // tests/projects/animjobproducer; these tests take cases the capture does not: a stream that is
    // not a running job's, a job's changes across evaluations, and a job replaced with SetJobData.
    [Collection("Animation")]
    public sealed class AnimationJobProducerTests
    {
        struct Counter : IAnimationJob
        {
            public int rootMotion, animation;
            public List<string> calls;
            public void ProcessRootMotion(AnimationStream stream) { rootMotion++; calls?.Add("rootMotion " + rootMotion); }
            public void ProcessAnimation(AnimationStream stream) { animation++; calls?.Add("animation " + animation); }
        }

        [Fact]
        public void IAnimationJobNamesItsProducer()
        {
            var marker = typeof(IAnimationJob).GetCustomAttributes(typeof(JobProducerTypeAttribute), false).Cast<JobProducerTypeAttribute>().Single();
            Assert.Equal(typeof(ProcessAnimationJobStruct<>), marker.ProducerType);
            Assert.NotEqual(IntPtr.Zero, ProcessAnimationJobStruct<Counter>.GetJobReflectionData());
            Assert.Equal(ProcessAnimationJobStruct<Counter>.GetJobReflectionData(), ProcessAnimationJobStruct<Counter>.GetJobReflectionData());
        }

        // Execute takes the method index, not the order: the root motion method for 0 and the
        // animation method for 1, each on the job it is given by reference, and refuses any other.
        [Fact]
        public unsafe void ExecuteRunsTheIndexedMethodInPlace()
        {
            var job = new Counter();
            var stream = default(AnimationStream);
            var ranges = default(JobRanges);
            var pointer = (IntPtr)(&stream);
            ProcessAnimationJobStruct<Counter>.Execute(ref job, pointer, (IntPtr)1, ref ranges, 0);
            ProcessAnimationJobStruct<Counter>.Execute(ref job, pointer, (IntPtr)1, ref ranges, 0);
            ProcessAnimationJobStruct<Counter>.Execute(ref job, pointer, (IntPtr)0, ref ranges, 0);
            Assert.Equal(2, job.animation);
            Assert.Equal(1, job.rootMotion);
            var invalid = Assert.Throws<NotImplementedException>(() => ProcessAnimationJobStruct<Counter>.Execute(ref job, pointer, (IntPtr)(-1), ref ranges, 0));
            Assert.Equal("Invalid Animation jobs method index.", invalid.Message);
        }

        // A script playable runs its job through the producer: root motion, then animation, on the
        // job it holds, which keeps its counts across evaluations and after SetJobData replaces it.
        [Fact]
        public void AScriptPlayableKeepsItsJobsChanges()
        {
            var go = new GameObject("Producer");
            new GameObject("Child").transform.SetParent(go.transform, false);
            var animator = go.AddComponent<Animator>();
            var graph = PlayableGraph.Create("producer");
            try
            {
                var calls = new List<string>();
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationScriptPlayable.Create(graph, new Counter { calls = calls });
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
                graph.Evaluate(0.1f);
                graph.Evaluate(0.1f);
                Assert.Equal(new[] { "rootMotion 1", "animation 1", "rootMotion 2", "animation 2" }, calls);
                playable.SetJobData(new Counter { animation = 10, calls = calls });
                graph.Evaluate(0.1f);
                Assert.Equal(11, playable.GetJobData<Counter>().animation);
                Assert.Equal(1, playable.GetJobData<Counter>().rootMotion);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(go);
            }
        }
    }
}
