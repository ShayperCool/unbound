using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AnimationStream root motion seen by animation jobs (Scripting API 6000.0: velocity and
    /// angularVelocity "for the evaluated frame", rootMotionPosition/rootMotionRotation "the root
    /// motion position/rotation for the evaluated frame", CopyAnimationStreamMotion "deep copies
    /// motion from a source animation stream", isHumanStream "true if the stream is from a humanoid
    /// avatar"). The values are derived from the Animator's existing root motion: these tests compare
    /// a controller playable's stream with the same controller run by the Animator itself, and keep
    /// Unbound's own unmeasured choices (world-space velocities like Animator.velocity, the root
    /// motion pose as the current root pose moved by them) from drifting. No Native capture backs
    /// them: they are not Unity measurements.
    /// </summary>
    [Collection("Animation")]
    public sealed class AnimationStreamRootMotionTests
    {
        /// <summary>Records the stream's motion in ProcessRootMotion; optionally sets a velocity
        /// first or copies the motion of an input stream.</summary>
        struct MotionJob : IAnimationJob
        {
            internal bool SetVelocity;
            internal Vector3 Velocity, AngularVelocity;
            internal int CopyFromInput;

            internal static Vector3 SeenVelocity, SeenAngularVelocity, SeenRootPosition;
            internal static Quaternion SeenRootRotation;
            internal static bool SeenHuman;
            internal static int Calls;

            public void ProcessRootMotion(AnimationStream stream)
            {
                if (CopyFromInput > 0) stream.CopyAnimationStreamMotion(stream.GetInputStream(CopyFromInput));
                if (SetVelocity)
                {
                    stream.velocity = Velocity;
                    stream.angularVelocity = AngularVelocity;
                    return;
                }
                SeenVelocity = stream.velocity;
                SeenAngularVelocity = stream.angularVelocity;
                SeenRootPosition = stream.rootMotionPosition;
                SeenRootRotation = stream.rootMotionRotation;
                SeenHuman = stream.isHumanStream;
                Calls++;
            }

            public void ProcessAnimation(AnimationStream stream) { }

            internal static void Reset()
            {
                SeenVelocity = SeenAngularVelocity = SeenRootPosition = new Vector3(float.NaN, float.NaN, float.NaN);
                SeenRootRotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
                SeenHuman = true;
                Calls = 0;
            }
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>AnimationFixtures.RootMotion: Move moves the root along x (0 to 2 over a second)
        /// and turns it about y (0 to 90).</summary>
        static RuntimeAnimatorController Controller() =>
            (RuntimeAnimatorController)ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion),
                "streammotion.asset", null, ObjectGraphBuilder.Kind.Asset, null).Objects[9100000];

        static GameObject Rig(string name, out Animator animator, bool applyRootMotion = true)
        {
            var root = new GameObject(name);
            new GameObject("Child").transform.SetParent(root.transform, false);
            animator = root.AddComponent<Animator>();
            animator.applyRootMotion = applyRootMotion;
            return root;
        }

        /// <summary>controller playable → job(s) → output on the Animator, Move playing from 0.</summary>
        static PlayableGraph Graph(Animator animator, RuntimeAnimatorController controller, params MotionJob[] jobs)
        {
            var g = PlayableGraph.Create("stream-motion");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var source = AnimatorControllerPlayable.Create(g, controller);
            Playable top = source;
            foreach (var data in jobs)
            {
                var job = AnimationScriptPlayable.Create(g, data, 1);
                job.ConnectInput(0, top, 0, 1f);
                top = job;
            }
            output.SetSourcePlayable(top);
            source.Play("Move", 0, 0f);
            g.Evaluate(0f);
            return g;
        }

        /// <summary>The same controller run by an Animator itself: its velocities after a step.</summary>
        static void Twin(RuntimeAnimatorController controller, float step, out Vector3 velocity, out Vector3 angular,
            out Vector3 position, out Quaternion rotation)
        {
            var root = Rig("Twin", out var animator);
            try
            {
                animator.runtimeAnimatorController = controller;
                animator.Play("Move", 0, 0);
                animator.Update(0);
                animator.Update(step);
                velocity = animator.velocity;
                angular = animator.angularVelocity;
                position = root.transform.position;
                rotation = root.transform.rotation;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Red at the base: velocity and angularVelocity were always zero, the pose zero/identity.
        [Fact]
        public void AControllerInputStreamReportsTheAnimatorsRootMotion()
        {
            var controller = Controller();
            Twin(controller, .25f, out var velocity, out var angular, out var position, out var rotation);
            Assert.True(velocity.sqrMagnitude > 1f, "the twin Animator moves");
            var root = Rig("Graph", out var animator);
            var g = Graph(animator, controller, new MotionJob());
            try
            {
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Near.Equal(velocity, MotionJob.SeenVelocity, 1e-4f);
                Near.Equal(angular, MotionJob.SeenAngularVelocity, 1e-4f);
                // Unbound's choice (unmeasured): where the root motion takes the root this frame,
                // i.e. where the Animator's own builtin root motion put its twin.
                Near.Equal(position, MotionJob.SeenRootPosition, 1e-4f);
                Near.Equal(rotation, MotionJob.SeenRootRotation, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at the base: every velocity read logged the "always zero" warning.
        [Fact]
        public void WithoutRequestedRootMotionTheStreamReportsNoneAndDoesNotWarn()
        {
            var root = Rig("Graph", out var animator, applyRootMotion: false);
            var g = Graph(animator, Controller(), new MotionJob());
            try
            {
                // The root's pose after the first evaluation wrote the controller's absolute root curves.
                var start = root.transform.position;
                var startRotation = root.transform.rotation;
                int before = Hits("AnimationStream.velocity");
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Assert.Equal(before, Hits("AnimationStream.velocity"));
                Near.Equal(Vector3.zero, MotionJob.SeenVelocity);
                Near.Equal(Vector3.zero, MotionJob.SeenAngularVelocity);
                Near.Equal(start, MotionJob.SeenRootPosition, 1e-4f);
                Near.Equal(startRotation, MotionJob.SeenRootRotation, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at the base: the setter was ignored, the job above read zero.
        [Fact]
        public void AVelocitySetByAJobIsTheMotionOfTheJobAbove()
        {
            var root = Rig("Graph", out var animator);
            var set = new MotionJob { SetVelocity = true, Velocity = new Vector3(0f, 0f, 7f), AngularVelocity = new Vector3(0f, 2f, 0f) };
            var g = Graph(animator, Controller(), set, new MotionJob());
            try
            {
                int warned = Hits("AnimationStream.velocity (set)");
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Near.Equal(new Vector3(0f, 0f, 7f), MotionJob.SeenVelocity);
                Near.Equal(new Vector3(0f, 2f, 0f), MotionJob.SeenAngularVelocity);
                // The Animator does not apply stream root motion yet: the setter stays loud.
                Assert.True(Hits("AnimationStream.velocity (set)") > warned);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at the base: CopyAnimationStreamMotion copied nothing, velocity read zero.
        [Fact]
        public void CopyAnimationStreamMotionTakesAnInputStreamsMotion()
        {
            var controller = Controller();
            Twin(controller, .25f, out var velocity, out var angular, out _, out _);
            var root = Rig("Graph", out var animator);
            var g = PlayableGraph.Create("stream-motion-copy");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var source = AnimatorControllerPlayable.Create(g, controller);
                var job = AnimationScriptPlayable.Create(g, new MotionJob { CopyFromInput = 1 }, 2);
                // Input 0 has no root motion of its own; input 1 is the controller.
                job.ConnectInput(0, AnimationMixerPlayable.Create(g, 0), 0, 1f);
                job.ConnectInput(1, source, 0, 1f);
                output.SetSourcePlayable(job);
                source.Play("Move", 0, 0f);
                g.Evaluate(0f);
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Near.Equal(velocity, MotionJob.SeenVelocity, 1e-4f);
                Near.Equal(angular, MotionJob.SeenAngularVelocity, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Guard (green at the base too): streams Unbound computes no motion for stay loud.
        [Fact]
        public void AStreamWithoutComputedMotionStillWarns()
        {
            var root = Rig("Graph", out var animator);
            var g = PlayableGraph.Create("stream-motion-none");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var job = AnimationScriptPlayable.Create(g, new MotionJob(), 1);
                job.ConnectInput(0, AnimationMixerPlayable.Create(g, 0), 0, 1f);
                output.SetSourcePlayable(job);
                int before = Hits("AnimationStream.velocity");
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Near.Equal(Vector3.zero, MotionJob.SeenVelocity);
                Assert.True(Hits("AnimationStream.velocity") > before);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at the base: isHumanStream logged "humanoid avatars are not supported" on every read.
        // (A humanoid avatar's true answer follows Animator.isHuman; no in-process humanoid fixture.)
        [Fact]
        public void IsHumanStreamFollowsTheAnimatorWithoutWarning()
        {
            var root = Rig("Graph", out var animator);
            var g = Graph(animator, Controller(), new MotionJob());
            try
            {
                int before = Hits("AnimationStream.isHumanStream");
                MotionJob.Reset();
                g.Evaluate(.25f);
                Assert.Equal(1, MotionJob.Calls);
                Assert.Equal(animator.isHuman, MotionJob.SeenHuman);
                Assert.False(MotionJob.SeenHuman);
                Assert.Equal(before, Hits("AnimationStream.isHumanStream"));
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }
    }
}
