using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Root motion of a generic Animator driven by a PlayableGraph output. Scripting API and Manual
    /// 6000.0: the root's change is "computed at every frame" and "applied to the Game Object"
    /// (How Root Motion works); Animator.deltaPosition/velocity are "for the last evaluated frame";
    /// "applyRootMotion has no effect when the script implements a MonoBehaviour.OnAnimatorMove()
    /// function"; OnAnimatorMove is "invoked at each frame after the state machines and the
    /// animations have been evaluated"; AnimationStream.velocity is "the avatar velocity for the
    /// evaluated frame". None of these pages mentions playables: the expected values below are
    /// Unbound's derivation (the Animator's own root motion rules applied to a clip playable's
    /// previous and current time), predicted for Unity, not measured. No Native capture backs them.
    /// </summary>
    [Collection("Animation")]
    public sealed class PlayableOutputRootMotionTests
    {
        const string NotComputed = "Animator root motion of this playable graph output";

        /// <summary>Sets the stream's velocities in ProcessRootMotion.</summary>
        struct SetMotionJob : IAnimationJob
        {
            internal Vector3 Velocity, AngularVelocity;

            public void ProcessRootMotion(AnimationStream stream)
            {
                stream.velocity = Velocity;
                stream.angularVelocity = AngularVelocity;
            }

            public void ProcessAnimation(AnimationStream stream) { }
        }

        /// <summary>Records the stream's velocities in ProcessRootMotion.</summary>
        struct ReadMotionJob : IAnimationJob
        {
            internal static Vector3 SeenVelocity, SeenAngularVelocity;
            internal static int Calls;

            public void ProcessRootMotion(AnimationStream stream)
            {
                SeenVelocity = stream.velocity;
                SeenAngularVelocity = stream.angularVelocity;
                Calls++;
            }

            public void ProcessAnimation(AnimationStream stream) { }

            internal static void Reset()
            {
                SeenVelocity = SeenAngularVelocity = new Vector3(float.NaN, float.NaN, float.NaN);
                Calls = 0;
            }
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>A generic clip moving the Animator's own transform linearly from the origin to
        /// <paramref name="to"/> over one second.</summary>
        static AnimationClip RootClip(Vector3 to, bool loop = false)
        {
            // Runtime SetCurve takes legacy clips only, as in Unity: build legacy, then play as a playable clip.
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, to.x));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, to.y));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 1f, to.z));
            clip.legacy = false;
            clip.m_LoopTime = loop;
            return clip;
        }

        static GameObject Rig(out Animator animator, bool applyRootMotion = true)
        {
            var root = new GameObject("Actor");
            root.transform.localPosition = new Vector3(5f, 0f, 0f);
            new GameObject("Child").transform.SetParent(root.transform, false);
            animator = root.AddComponent<Animator>();
            animator.applyRootMotion = applyRootMotion;
            return root;
        }

        /// <summary>clip playable → output on the Animator, evaluated once at time zero.</summary>
        static PlayableGraph ClipGraph(Animator animator, AnimationClip clip, out AnimationClipPlayable playable)
        {
            var g = PlayableGraph.Create("output-root-motion");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            playable = AnimationClipPlayable.Create(g, clip);
            output.SetSourcePlayable(playable);
            g.Evaluate(0f);
            return g;
        }

        // Red at da57c463: playable outputs never set the Animator's motion (velocity stays zero) and
        // the job's velocity moved nothing.
        [Fact]
        public void AJobsVelocityMovesAGenericAnimator()
        {
            var root = Rig(out var animator);
            root.transform.localPosition = Vector3.zero;
            var g = PlayableGraph.Create("output-root-motion-job");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var job = AnimationScriptPlayable.Create(g,
                    new SetMotionJob { Velocity = new Vector3(0f, 0f, 4f), AngularVelocity = new Vector3(0f, 1f, 0f) }, 1);
                // The input carries no root motion of its own.
                job.ConnectInput(0, AnimationMixerPlayable.Create(g, 0), 0, 1f);
                output.SetSourcePlayable(job);
                g.Evaluate(0f);
                Near.Equal(Vector3.zero, root.transform.position);
                g.Evaluate(.5f);
                Near.Equal(new Vector3(0f, 0f, 4f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(0f, 1f, 0f), animator.angularVelocity, 1e-4f);
                Near.Equal(new Vector3(0f, 0f, 2f), animator.deltaPosition, 1e-4f);
                // Unbound's choice (unmeasured): the root goes where the stream's rootMotionPosition and
                // rootMotionRotation put it (velocity times the stream's delta time).
                Near.Equal(new Vector3(0f, 0f, 2f), root.transform.position, 1e-4f);
                Near.SameRotation(Quaternion.AngleAxis(.5f * Mathf.Rad2Deg, Vector3.up), root.transform.rotation, 1e-2f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: the root took the clip's absolute root curves (x = 0 after the first evaluation).
        [Fact]
        public void AClipPlayableMovesTheAnimatorByItsRootCurves()
        {
            var root = Rig(out var animator);
            var g = ClipGraph(animator, RootClip(new Vector3(2f, 0f, 0f)), out _);
            try
            {
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
                g.Evaluate(.25f);
                Near.Equal(new Vector3(.5f, 0f, 0f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(Vector3.zero, animator.angularVelocity, 1e-4f);
                Near.Equal(new Vector3(5.5f, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: velocity stayed zero (the mixer's absolute blend posed the root instead).
        [Fact]
        public void AMixerSumsItsClipsRootMotionByWeight()
        {
            var root = Rig(out var animator);
            var g = PlayableGraph.Create("output-root-motion-mixer");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var mixer = AnimationMixerPlayable.Create(g, 2);
                g.Connect(AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 0, mixer, 0);
                g.Connect(AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 0f, 4f))), 0, mixer, 1);
                mixer.SetInputWeight(0, .25f);
                mixer.SetInputWeight(1, .75f);
                output.SetSourcePlayable(mixer);
                g.Evaluate(0f);
                g.Evaluate(.5f);
                // .25 · (1, 0, 0) + .75 · (0, 0, 2) over half a second.
                Near.Equal(new Vector3(.5f, 0f, 3f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(.25f, 0f, 1.5f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(5.25f, 0f, 1.5f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: the root took the looping clip's absolute pose (x = 1 at local time .5).
        [Fact]
        public void ALoopingClipPlayableAccumulatesWholeLoops()
        {
            var root = Rig(out var animator);
            var g = ClipGraph(animator, RootClip(new Vector3(2f, 0f, 0f), loop: true), out _);
            try
            {
                g.Evaluate(2.5f);
                // Two whole loops (2 each) and half a loop.
                Near.Equal(new Vector3(10f, 0f, 0f), root.transform.position, 1e-3f);
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-3f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: the first evaluation put the root at the clip's x = 0 and the seek at x = 1.5.
        [Fact]
        public void ZeroTimeAndSeeksMoveTheRootByNothing()
        {
            var root = Rig(out var animator);
            var g = ClipGraph(animator, RootClip(new Vector3(2f, 0f, 0f)), out var playable);
            try
            {
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
                g.Evaluate(0f);
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
                Near.Equal(Vector3.zero, animator.deltaPosition);
                Near.Equal(Vector3.zero, animator.velocity);
                // Unbound's choice (unmeasured): a seek jumps the clip's time; it moves the root by nothing.
                playable.SetTime(.75);
                g.Evaluate(.25f);
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
                Near.Equal(Vector3.zero, animator.deltaPosition);
                Near.Equal(Vector3.zero, animator.velocity);
                // Playback continues from the seeked time: .75 to 1.
                g.Evaluate(.25f);
                Near.Equal(new Vector3(5.5f, 0f, 0f), root.transform.position, 1e-4f);
                Near.Equal(new Vector3(.5f, 0f, 0f), animator.deltaPosition, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: OnAnimatorMove never ran for an Animator driven by a playable output.
        [Fact]
        public void OnAnimatorMoveReceivesTheMotionInsteadOfTheBuiltinApplication()
        {
            var root = Rig(out var animator);
            var receiver = root.AddComponent<MatchMovementReceiver>();
            var g = ClipGraph(animator, RootClip(new Vector3(2f, 0f, 0f)), out _);
            try
            {
                receiver.Count = 0;
                g.Evaluate(.25f);
                Assert.Equal(1, receiver.Count);
                Near.Equal(new Vector3(.5f, 0f, 0f), receiver.Delta, 1e-4f);
                // applyRootMotion has no effect when a script implements OnAnimatorMove (Scripting API):
                // the script did not move the root, and the graph's root curves do not either.
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
                // The script applies the default root motion itself.
                receiver.Apply = true;
                g.Evaluate(.25f);
                Assert.Equal(2, receiver.Count);
                Near.Equal(new Vector3(5.5f, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Guard (green at da57c463 too): without requested root motion, the graph's root curves pose
        // the root, the Animator reports no motion, and nothing warns.
        [Fact]
        public void WithoutRootMotionTheRootCurvesStillPoseTheRoot()
        {
            var root = Rig(out var animator, applyRootMotion: false);
            var g = ClipGraph(animator, RootClip(new Vector3(2f, 0f, 0f)), out _);
            try
            {
                int before = Hits(NotComputed);
                g.Evaluate(.25f);
                Near.Equal(new Vector3(.5f, 0f, 0f), root.transform.position, 1e-4f);
                Near.Equal(Vector3.zero, animator.deltaPosition);
                Near.Equal(Vector3.zero, animator.velocity);
                Assert.Equal(before, Hits(NotComputed));
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: nothing warned that the offset playable's root motion is not applied.
        [Fact]
        public void RootMotionNotCarriedThroughTheGraphWarnsAndKeepsTheRootCurves()
        {
            var root = Rig(out var animator);
            var g = PlayableGraph.Create("output-root-motion-offset");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                // An offset playable carries no root motion Unbound computes (layer mixers do since
                // the layer mixer root motion change).
                var offset = AnimationOffsetPlayable.Create(g, Vector3.zero, Quaternion.identity, 1);
                g.Connect(AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 0, offset, 0);
                offset.SetInputWeight(0, 1f);
                output.SetSourcePlayable(offset);
                int before = Hits(NotComputed);
                g.Evaluate(0f);
                g.Evaluate(.25f);
                Assert.True(Hits(NotComputed) > before);
                // Not approximated: the root keeps the graph's root curves, the Animator reports no motion.
                Near.Equal(new Vector3(.5f, 0f, 0f), root.transform.position, 1e-4f);
                Near.Equal(Vector3.zero, animator.deltaPosition);
                Near.Equal(Vector3.zero, animator.velocity);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at da57c463: a job above a clip read zero velocity (and the read warned).
        [Fact]
        public void AJobAboveAClipReadsTheClipsRootMotion()
        {
            var root = Rig(out var animator);
            var g = PlayableGraph.Create("output-root-motion-read");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var job = AnimationScriptPlayable.Create(g, new ReadMotionJob(), 1);
                job.ConnectInput(0, AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 0, 1f);
                output.SetSourcePlayable(job);
                g.Evaluate(0f);
                int before = Hits("AnimationStream.velocity");
                ReadMotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, ReadMotionJob.Calls);
                Near.Equal(new Vector3(2f, 0f, 0f), ReadMotionJob.SeenVelocity, 1e-4f);
                Near.Equal(Vector3.zero, ReadMotionJob.SeenAngularVelocity, 1e-4f);
                Assert.Equal(before, Hits("AnimationStream.velocity"));
                // The job left the motion as it was: the Animator moves by the clip's motion.
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(5.5f, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }
    }
}
