using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // tests/projects/animatephysics: two identical Unity 6000.0.71f1 captures.
    public class AnimatePhysicsTests
    {
        [Fact]
        public void AnimatePhysicsIsStoredBesideTheUpdateMode()
        {
            var go = new GameObject("animator");
            try
            {
                var animator = go.AddComponent<Animator>();
                Assert.False(animator.animatePhysics);
                animator.animatePhysics = true;
                Assert.Equal(AnimatorUpdateMode.Normal, animator.updateMode);
                animator.updateMode = AnimatorUpdateMode.Fixed;
                Assert.True(animator.animatePhysics);
                animator.animatePhysics = false;
                Assert.Equal(AnimatorUpdateMode.Fixed, animator.updateMode);
                animator.animatePhysics = true;
                var copy = Object.Instantiate(go).GetComponent<Animator>();
                Assert.True(copy.animatePhysics);
                Assert.Equal(AnimatorUpdateMode.Fixed, copy.updateMode);
                Object.DestroyImmediate(copy.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void OnlyWritesInsideAnAnimatePhysicsAnimatorsScopeMarkTheTransformAndItsDescendants()
        {
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform, false);
            var on = root.AddComponent<Animator>();
            on.animatePhysics = true;
            var off = new GameObject("off").AddComponent<Animator>();
            try
            {
                long since = AnimatedPhysicsWrites.s_LastStamp;
                child.transform.localPosition = new Vector3(1, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                using (AnimatedPhysicsWrites.Begin(off)) root.transform.localPosition = new Vector3(2, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                using (AnimatedPhysicsWrites.Begin(on))
                {
                    // Another Animator's writes inside are its own (without animatePhysics: none).
                    using (AnimatedPhysicsWrites.Begin(off)) child.transform.localPosition = new Vector3(3, 0, 0);
                    Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                    root.transform.localPosition = new Vector3(4, 0, 0);
                }
                // The parent's write moves the child; writes after the scope do not.
                Assert.True(AnimatedPhysicsWrites.MovedSince(child.transform, since));
                long later = AnimatedPhysicsWrites.s_LastStamp;
                root.transform.localPosition = new Vector3(5, 0, 0);
                Assert.False(AnimatedPhysicsWrites.MovedSince(child.transform, later));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(off.gameObject);
            }
        }

        [Fact]
        public void AnimatedWritesMoveKinematicBodiesAndOlderOnesDoNotTurnLaterMovesIntoMoves()
        {
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            const float dt = 0.02f;
            var bone = new GameObject("bone");
            var animator = bone.AddComponent<Animator>();
            animator.animatePhysics = true;
            var platform = new GameObject("platform");
            platform.transform.SetParent(bone.transform, false);
            platform.AddComponent<BoxCollider>();
            var body = platform.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var other = new GameObject("other");
            other.transform.position = new Vector3(0, 0, 10);
            other.AddComponent<BoxCollider>();
            var otherBody = other.AddComponent<Rigidbody>();
            otherBody.isKinematic = true;
            try
            {
                Physics.Simulate(dt);
                // The Animator's write of the parent moves the child body with velocity.
                using (AnimatedPhysicsWrites.Begin(animator)) bone.transform.position = new Vector3(0.02f, 0, 0);
                Physics.Simulate(dt);
                Assert.True(body.linearVelocity.x > 0.5f);
                // A script move of a body teleports, also onto a bone an older animated write stamped.
                Physics.Simulate(dt);
                other.transform.SetParent(bone.transform, false);
                Physics.Simulate(dt);
                Assert.Equal(0f, otherBody.linearVelocity.x);
                Assert.Equal(0.02f, otherBody.position.x, 4);
            }
            finally
            {
                Object.DestroyImmediate(bone);
                Object.DestroyImmediate(other);
                Physics.simulationMode = mode;
            }
        }
    }
}
