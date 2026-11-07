using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Which playables a CullCompletely-culled Animator stops (PlayableGraphData.PreparePass):
    /// the animation playables of its outputs' trees and everything below them are held like
    /// paused playables, while plain and script playables above them keep the graph's clock
    /// (6000.0.71f1, tests/projects/animculling clock/). The rigs have no renderer, so they are
    /// never visible.
    /// </summary>
    [Collection("Animation")]
    public sealed class PlayableCullingClockTests : System.IDisposable
    {
        const double Step = 0.1;
        readonly List<Object> m_Objects = new List<Object>();
        readonly List<PlayableGraph> m_Graphs = new List<PlayableGraph>();
        readonly string m_Version = ProjectInfo.UnityVersion;

        public PlayableCullingClockTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose()
        {
            foreach (var g in m_Graphs) if (g.IsValid()) g.Destroy();
            PlayableGraphData.FlushDestroyed();
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            ProjectInfo.UnityVersion = m_Version;
        }

        sealed class Counter : PlayableBehaviour
        {
            internal int Prepares, Plays, Pauses;
            internal double LastDelta = -1;

            public override void PrepareFrame(Playable playable, FrameData info)
            {
                Prepares++;
                LastDelta = info.deltaTime;
            }

            public override void OnBehaviourPlay(Playable playable, FrameData info) => Plays++;

            public override void OnBehaviourPause(Playable playable, FrameData info) => Pauses++;
        }

        Animator Rig(AnimatorCullingMode mode)
        {
            var go = new GameObject("ClockRig");
            m_Objects.Add(go);
            var animator = go.AddComponent<Animator>();
            animator.cullingMode = mode;
            return animator;
        }

        AnimationClip Clip()
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            clip.m_LoopTime = true;
            m_Objects.Add(clip);
            return clip;
        }

        PlayableGraph Graph(DirectorUpdateMode mode = DirectorUpdateMode.GameTime)
        {
            var graph = PlayableGraph.Create("clock");
            graph.SetTimeUpdateMode(mode);
            m_Graphs.Add(graph);
            return graph;
        }

        static Playable Above(PlayableGraph graph, Playable input)
        {
            var plain = Playable.Create(graph, 1);
            plain.ConnectInput(0, input, 0, 1f);
            return plain;
        }

        static ScriptPlayable<Counter> Script(PlayableGraph graph, Playable input)
        {
            var script = ScriptPlayable<Counter>.Create(graph, 1);
            script.ConnectInput(0, input, 0, 1f);
            return script;
        }

        static void Frames(PlayableGraph graph, int count)
        {
            var data = PlayableGraphData.Get(graph);
            for (int i = 0; i < count; i++)
            {
                data.PreparePass(Step, evaluate: false);
                data.FrameId++;
            }
        }

        [Fact]
        public void CullCompletelyHoldsOnlyTheAnimationPlayables()
        {
            var animator = Rig(AnimatorCullingMode.CullCompletely);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            var below = Script(graph, clip);
            var mixer = AnimationMixerPlayable.Create(graph, 2);
            mixer.ConnectInput(0, below, 0, 0.5f);
            mixer.ConnectInput(1, AnimationClipPlayable.Create(graph, Clip()), 0, 0.5f);
            var plain = Above(graph, mixer);
            var above = Script(graph, plain);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(above);
            graph.Play();

            Frames(graph, 3);
            Assert.Equal(3 * Step, above.GetTime(), 9);
            Assert.Equal(3 * Step, plain.GetTime(), 9);
            Assert.Equal((3, (float)Step), (above.GetBehaviour().Prepares, (float)above.GetBehaviour().LastDelta));
            Assert.Equal(0, mixer.GetTime());
            Assert.Equal(0, clip.GetTime());
            // Held from its first frame: one pause, never prepared.
            var held = below.GetBehaviour();
            Assert.Equal((0, 0, 1), (held.Prepares, held.Plays, held.Pauses));

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Frames(graph, 1);
            Assert.Equal((1, 1, 1), (held.Prepares, held.Plays, held.Pauses));
            Assert.Equal(Step, clip.GetTime(), 9);
            Assert.Equal(0, clip.GetPreviousTime());
            Assert.Equal(4 * Step, plain.GetTime(), 9);

            // Culled again: paused once more, the clip keeps its time.
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            Frames(graph, 2);
            Assert.Equal((1, 1, 2), (held.Prepares, held.Plays, held.Pauses));
            Assert.Equal(Step, clip.GetTime(), 9);
            Assert.Equal(6 * Step, plain.GetTime(), 9);
        }

        [Fact]
        public void ExplicitEvaluationAdvancesThePlayablesAboveAHeldClip()
        {
            var animator = Rig(AnimatorCullingMode.CullCompletely);
            var graph = Graph(DirectorUpdateMode.Manual);
            var clip = AnimationClipPlayable.Create(graph, Clip());
            var plain = Above(graph, clip);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
            graph.Play();
            graph.Evaluate(0.07f);
            graph.Evaluate(0f);
            graph.Evaluate(0.04f);
            Assert.Equal((double)0.07f + 0.04f, plain.GetTime(), 6);
            Assert.Equal(0, clip.GetTime());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AHoldWinsOverAVisibleAnimatorSharingTheClip(bool culledFirst)
        {
            var culled = Rig(AnimatorCullingMode.CullCompletely);
            var visible = Rig(AnimatorCullingMode.AlwaysAnimate);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            clip.SetOutputCount(2);
            var mine = Playable.Create(graph, 1);
            mine.ConnectInput(0, clip, 0, 1f);
            var theirs = Playable.Create(graph, 1);
            theirs.ConnectInput(0, clip, 1, 1f);
            if (culledFirst) AnimationPlayableOutput.Create(graph, "culled", culled).SetSourcePlayable(mine);
            AnimationPlayableOutput.Create(graph, "visible", visible).SetSourcePlayable(theirs);
            if (!culledFirst) AnimationPlayableOutput.Create(graph, "culled", culled).SetSourcePlayable(mine);
            graph.Play();

            Frames(graph, 2);
            Assert.Equal(0, clip.GetTime());
            Assert.Equal(2 * Step, mine.GetTime(), 9);
            Assert.Equal(2 * Step, theirs.GetTime(), 9);

            // Control: CullUpdateTransforms keeps every clock running.
            culled.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            Frames(graph, 1);
            Assert.Equal(Step, clip.GetTime(), 9);
        }

        [Fact]
        public void AClipOutsideAnyAnimationOutputKeepsTheGraphClock()
        {
            Rig(AnimatorCullingMode.CullCompletely);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            ScriptPlayableOutput.Create(graph, "script").SetSourcePlayable(Above(graph, clip));
            graph.Play();
            Frames(graph, 2);
            Assert.Equal(2 * Step, clip.GetTime(), 9);
        }

        [Fact]
        public void AClipReachesItsDurationAndIsDone()
        {
            var animator = Rig(AnimatorCullingMode.AlwaysAnimate);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            clip.SetDuration(0.25);
            var plain = Above(graph, clip);
            plain.SetDuration(0.25);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
            graph.Play();
            Frames(graph, 3);
            Assert.Equal(0.25, clip.GetTime(), 9);
            // A frame's automatic passes: done at the end of the frame.
            Assert.False(clip.IsDone());
            Assert.False(plain.IsDone());
            PlayableGraphData.LateUpdate();
            Assert.True(clip.IsDone());
            Assert.True(plain.IsDone());
        }

        [Fact]
        public void AnExplicitEvaluationFinishesPlainPlayablesAtOnceAndClipsAtTheFrameEnd()
        {
            var animator = Rig(AnimatorCullingMode.AlwaysAnimate);
            var graph = Graph(DirectorUpdateMode.Manual);
            var clip = AnimationClipPlayable.Create(graph, Clip());
            clip.SetDuration(0.25);
            var plain = Above(graph, clip);
            plain.SetDuration(0.25);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
            graph.Play();
            graph.Evaluate(0.3f);
            Assert.True(plain.IsDone());
            Assert.False(clip.IsDone());
            PlayableGraphData.LateUpdate();
            Assert.True(clip.IsDone());
            // SetDone(false) wins over a pending finish.
            clip.SetDone(false);
            plain.SetTime(0);
            clip.SetTime(0);
            graph.Evaluate(0.3f);
            clip.SetDone(false);
            PlayableGraphData.LateUpdate();
            Assert.False(clip.IsDone());
        }

        /// <summary>25 evaluations of 0.02f put a 0.5 s loop at 0.4999999888 s, which the clip
        /// samples as its start. Root motion crosses that wrap with the pose: a steady step, no
        /// jump back and forth.</summary>
        [Fact]
        public void RootMotionCrossesASinglePrecisionLoopWrapSteadily()
        {
            var animator = Rig(AnimatorCullingMode.AlwaysAnimate);
            animator.applyRootMotion = true;
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 0.5f, 1f));
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 0.5f, 0f));
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0f, 0.5f, 0f));
            clip.m_LoopTime = true;
            m_Objects.Add(clip);
            var graph = Graph(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
            for (int frame = 1; frame <= 30; frame++)
            {
                graph.Evaluate(0.02f);
                Assert.True(System.Math.Abs(animator.deltaPosition.x - 0.04f) < 1e-4f,
                    $"frame {frame} (t={playable.GetTime():R}): delta {animator.deltaPosition.x:R}");
            }
            Assert.True(System.Math.Abs(animator.transform.position.x - 1.2f) < 1e-3f, $"root at {animator.transform.position.x:R}");
        }

        /// <summary>The automatic Play-entry interval is zero for an animation output's whole
        /// tree, the playables above its clip included (unmeasured there; the existing rule).</summary>
        [Fact]
        public void ThePlayEntryIntervalHoldsTheWholeAnimationOutputTree()
        {
            var animator = Rig(AnimatorCullingMode.AlwaysAnimate);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            var plain = Above(graph, clip);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
            graph.Play();
            AnimationSystem.BeginPlayMode();
            try
            {
                Frames(graph, 1);
                Assert.Equal(0, plain.GetTime());
                Assert.Equal(0, clip.GetTime());
            }
            finally { AnimationSystem.EndPlayModeEntry(); }
            Frames(graph, 1);
            Assert.Equal(Step, plain.GetTime(), 9);
            Assert.Equal(Step, clip.GetTime(), 9);
        }

        [Fact]
        public void OtherReleasesHoldTheWholeSourceTree()
        {
            ProjectInfo.UnityVersion = "2022.3.20f1";
            var animator = Rig(AnimatorCullingMode.CullCompletely);
            var graph = Graph();
            var clip = AnimationClipPlayable.Create(graph, Clip());
            var plain = Above(graph, clip);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
            graph.Play();
            Frames(graph, 2);
            Assert.Equal(0, plain.GetTime());
            Assert.Equal(0, clip.GetTime());
        }
    }
}
