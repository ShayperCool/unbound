using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Root motion carried to a generic Animator's playable graph output by an
    /// AnimationLayerMixerPlayable and an AnimatorControllerPlayable. Scripting API 6000.0:
    /// SetLayerAdditive "Additive layers blend with previous layers"; "By default, layers are not
    /// additive and override the animation from previous layers"; SetLayerMaskFromAvatarMask "Sets
    /// the mask for the current layer"; SetInputWeight "The weight. Should be between 0 and 1";
    /// OnAnimatorMove is "invoked at each frame after the state machines and the animations have
    /// been evaluated"; "applyRootMotion has no effect when the script implements a
    /// MonoBehaviour.OnAnimatorMove() function". None of these pages, nor the Manual's Playables and
    /// Root Motion pages, says how a layer mixer or a controller playable produces root motion.
    /// Every layer composition rule below is Unbound's choice (unmeasured): layers in layer order
    /// from no motion; an override layer of weight w blends the motion so far toward its own by w;
    /// an additive layer adds its delta times w; a layer of weight 0, whose mask excludes the root,
    /// or whose playables animate neither the root's position nor its rotation (nor a root node's
    /// motion) contributes nothing, as the layer's pose overrides only the channels it animates; a
    /// mixer animates the root when one of its inputs does. The controller cases compare with the
    /// same controller run by the Animator itself (Unbound's own root motion), not with Unity. No
    /// Native capture backs them.
    /// </summary>
    [Collection("Animation")]
    public sealed class PlayableLayerMixerRootMotionTests
    {
        const string NotComputed = "Animator root motion of this playable graph output";

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>A generic clip moving the Animator's own transform linearly from the origin to
        /// <paramref name="to"/> over one second.</summary>
        static AnimationClip RootClip(Vector3 to)
        {
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, to.x));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, to.y));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 1f, to.z));
            clip.legacy = false;
            return clip;
        }

        /// <summary>A generic clip moving only the "Child" transform (not the root) along z over one
        /// second.</summary>
        static AnimationClip ChildClip(float to)
        {
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("Child", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 1f, to));
            clip.legacy = false;
            return clip;
        }

        static GameObject Rig(out Animator animator, Vector3 at)
        {
            var root = new GameObject("Actor");
            root.transform.localPosition = at;
            new GameObject("Child").transform.SetParent(root.transform, false);
            animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            return root;
        }

        /// <summary>AnimationFixtures.RootMotion: Move moves the root along x (0 to 2 over a second)
        /// and turns it about y (0 to 90).</summary>
        static RuntimeAnimatorController Controller() =>
            (RuntimeAnimatorController)ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion),
                "layermotion.asset", null, ObjectGraphBuilder.Kind.Asset, null).Objects[9100000];

        /// <summary>The same controller run by an Animator itself, Move playing from 0, after
        /// <paramref name="steps"/> updates of <paramref name="step"/>.</summary>
        static void Twin(RuntimeAnimatorController controller, float step, int steps, out Vector3 velocity, out Vector3 angular,
            out Vector3 delta, out Vector3 position, out Quaternion rotation)
        {
            var root = Rig(out var animator, Vector3.zero);
            try
            {
                animator.runtimeAnimatorController = controller;
                animator.Play("Move", 0, 0);
                animator.Update(0);
                for (int i = 0; i < steps; i++) animator.Update(step);
                velocity = animator.velocity;
                angular = animator.angularVelocity;
                delta = animator.deltaPosition;
                position = root.transform.position;
                rotation = root.transform.rotation;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A layer mixer on the output with the given layers (playable, weight).</summary>
        static AnimationLayerMixerPlayable Layers(PlayableGraph g, Animator animator, params (Playable Input, float Weight)[] layers)
        {
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var mixer = AnimationLayerMixerPlayable.Create(g, layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                g.Connect(layers[i].Input, 0, mixer, i);
                mixer.SetInputWeight(i, layers[i].Weight);
            }
            output.SetSourcePlayable(mixer);
            return mixer;
        }

        // Red at 81b7f95a: the layer mixer carried no root motion (warned, velocity zero).
        [Fact]
        public void TwoOverrideLayersBlendTheirMotionByWeight()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-override");
            try
            {
                Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 0f, 4f))), .25f));
                g.Evaluate(0f);
                g.Evaluate(.5f);
                // Unbound's choice (unmeasured): (1, 0, 0) blended toward (0, 0, 2) by .25.
                Near.Equal(new Vector3(1.5f, 0f, 1f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(.75f, 0f, .5f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(5.75f, 0f, .5f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: velocity zero (the layer mixer's motion was unknown).
        [Fact]
        public void AnAdditiveLayerAddsItsWeightedMotion()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-additive");
            try
            {
                var mixer = Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 0f, 4f))), .5f));
                mixer.SetLayerAdditive(1, true);
                g.Evaluate(0f);
                g.Evaluate(.5f);
                // Unbound's choice (unmeasured): (1, 0, 0) + .5 · (0, 0, 2).
                Near.Equal(new Vector3(2f, 0f, 2f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(1f, 0f, 1f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(6f, 0f, 1f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: velocity zero and the not-computed warning hit.
        [Fact]
        public void AWeightZeroLayerContributesNothing()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-zero");
            try
            {
                // Layer 2 carries no root motion Unbound computes (an offset playable): at weight 0 it
                // does not make the motion unknown.
                var offset = AnimationOffsetPlayable.Create(g, Vector3.zero, Quaternion.identity, 1);
                g.Connect(AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 3f, 0f))), 0, offset, 0);
                Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 0f, 4f))), 0f),
                    (offset, 0f));
                g.Evaluate(0f);
                int before = Hits(NotComputed);
                g.Evaluate(.5f);
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(6f, 0f, 0f), root.transform.position, 1e-4f);
                Assert.Equal(before, Hits(NotComputed));
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: velocity zero (the layer mixer's motion was unknown).
        [Fact]
        public void AMaskWithoutTheRootRemovesTheLayersMotion()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-mask");
            try
            {
                var mixer = Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(0f, 0f, 4f))), 1f));
                // The mask animates the child only: its root entry (the empty path) is absent.
                var mask = new AvatarMask();
                mask.AddTransformPath(root.transform.Find("Child"), false);
                mixer.SetLayerMaskFromAvatarMask(1, mask);
                g.Evaluate(0f);
                g.Evaluate(.5f);
                // Unbound's choice (unmeasured): only layer 0 moves the root (not layer 1's (0, 0, 4)).
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(6f, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 7a5e964d: the child-only layer reported a known zero motion, which overrode layer 0's
        // (velocity zero, no warning).
        [Fact]
        public void AnOverrideLayerThatDoesNotAnimateTheRootKeepsTheMotionBelow()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-child-only");
            try
            {
                Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (AnimationClipPlayable.Create(g, ChildClip(4f)), 1f));
                g.Evaluate(0f);
                int before = Hits(NotComputed);
                g.Evaluate(.5f);
                // Unbound's choice (unmeasured): layer 1 animates only the child, so, as for its pose,
                // it takes nothing from the root's motion; the result is layer 0's alone.
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(1f, 0f, 0f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(6f, 0f, 0f), root.transform.position, 1e-4f);
                Assert.Equal(before, Hits(NotComputed));
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 7a5e964d: the mixer of child-only clips reported a known zero motion, which overrode
        // layer 0's (velocity zero).
        [Fact]
        public void AnOverrideLayerMixingClipsThatDoNotAnimateTheRootKeepsTheMotionBelow()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-child-only-mixer");
            try
            {
                var mixer = AnimationMixerPlayable.Create(g, 2);
                g.Connect(AnimationClipPlayable.Create(g, ChildClip(4f)), 0, mixer, 0);
                g.Connect(AnimationClipPlayable.Create(g, ChildClip(-4f)), 0, mixer, 1);
                mixer.SetInputWeight(0, .5f);
                mixer.SetInputWeight(1, .5f);
                Layers(g, animator,
                    (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f),
                    (mixer, 1f));
                g.Evaluate(0f);
                g.Evaluate(.5f);
                // Unbound's choice (unmeasured): a mixer animates the root only when an input does.
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-4f);
                Near.Equal(new Vector3(6f, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: the controller playable carried no root motion to the output (velocity zero).
        [Fact]
        public void AControllerPlayableMovesTheRootAsTheAnimatorsOwnControllerWould()
        {
            var controller = Controller();
            Twin(controller, .25f, 2, out var velocity, out var angular, out var delta, out var position, out var rotation);
            Assert.True(velocity.sqrMagnitude > 1f, "the twin Animator moves");
            var root = Rig(out var animator, Vector3.zero);
            var g = PlayableGraph.Create("controller-motion");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var source = AnimatorControllerPlayable.Create(g, controller);
                output.SetSourcePlayable(source);
                source.Play("Move", 0, 0f);
                g.Evaluate(0f);
                g.Evaluate(.25f);
                g.Evaluate(.25f);
                Near.Equal(velocity, animator.velocity, 1e-3f);
                Near.Equal(angular, animator.angularVelocity, 1e-3f);
                Near.Equal(delta, animator.deltaPosition, 1e-4f);
                Near.Equal(position, root.transform.position, 1e-4f);
                Near.SameRotation(rotation, root.transform.rotation, 1e-2f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: neither the layer mixer nor the controller playable carried root motion.
        [Fact]
        public void AControllerPlayableLayerCarriesTheControllersMotion()
        {
            var controller = Controller();
            Twin(controller, .25f, 1, out var velocity, out var angular, out var delta, out _, out _);
            var root = Rig(out var animator, Vector3.zero);
            var g = PlayableGraph.Create("controller-layer-motion");
            try
            {
                var source = AnimatorControllerPlayable.Create(g, controller);
                Layers(g, animator, (source, 1f));
                source.Play("Move", 0, 0f);
                g.Evaluate(0f);
                g.Evaluate(.25f);
                // A single override layer of weight 1 carries its input's motion.
                Near.Equal(velocity, animator.velocity, 1e-3f);
                Near.Equal(angular, animator.angularVelocity, 1e-3f);
                Near.Equal(delta, animator.deltaPosition, 1e-4f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Red at 81b7f95a: OnAnimatorMove never ran (the layer mixer's motion was unknown).
        [Fact]
        public void OnAnimatorMoveReceivesTheLayerMixersMotion()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var receiver = root.AddComponent<MatchMovementReceiver>();
            var g = PlayableGraph.Create("layer-motion-move");
            try
            {
                Layers(g, animator, (AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 1f));
                g.Evaluate(0f);
                receiver.Count = 0;
                g.Evaluate(.25f);
                Assert.Equal(1, receiver.Count);
                Near.Equal(new Vector3(.5f, 0f, 0f), receiver.Delta, 1e-4f);
                Near.Equal(new Vector3(.5f, 0f, 0f), animator.deltaPosition, 1e-4f);
                // applyRootMotion has no effect when a script implements OnAnimatorMove (Scripting API):
                // the script did not move the root, and the graph's root curves do not either.
                Near.Equal(new Vector3(5f, 0f, 0f), root.transform.position);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        // Guard (green at 81b7f95a too): an offset playable layer still carries no root motion, so
        // the output warns, keeps the graph's root curves and reports no motion.
        [Fact]
        public void AnOffsetPlayableLayerStillWarns()
        {
            var root = Rig(out var animator, new Vector3(5f, 0f, 0f));
            var g = PlayableGraph.Create("layer-motion-offset");
            try
            {
                var offset = AnimationOffsetPlayable.Create(g, Vector3.zero, Quaternion.identity, 1);
                g.Connect(AnimationClipPlayable.Create(g, RootClip(new Vector3(2f, 0f, 0f))), 0, offset, 0);
                Layers(g, animator, (offset, 1f));
                int before = Hits(NotComputed);
                g.Evaluate(0f);
                g.Evaluate(.25f);
                Assert.True(Hits(NotComputed) > before);
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
    }
}
