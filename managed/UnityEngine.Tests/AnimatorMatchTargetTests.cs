using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MatchMovementReceiver : MonoBehaviour
    {
        public bool Apply;
        public int Count;
        public Vector3 Delta;

        void OnAnimatorMove()
        {
            var animator = GetComponent<Animator>();
            Count++;
            Delta = animator.deltaPosition;
            if (Apply) animator.ApplyBuiltinRootMotion();
        }
    }

    [Collection("Animation")]
    public sealed class AnimatorMatchTargetTests
    {
        static Animator Create(out GameObject root)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion),
                "match.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            root = new GameObject("Root");
            new GameObject("Child").transform.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.applyRootMotion = true;
            animator.Play("Move", 0, 0);
            animator.Update(0);
            return animator;
        }

        // Native controls: tests/projects/modelroot, matchReview.life.*.interruptTrue.*.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CompleteInterruptUsesTheNextMovementCallback(bool apply)
        {
            var animator = Create(out var root);
            try
            {
                var receiver = root.AddComponent<MatchMovementReceiver>();
                receiver.Apply = apply;
                animator.MatchTarget(new Vector3(10, 0, 0), Quaternion.identity, AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1), 0, 1, true);
                animator.Update(.25f);
                var position = root.transform.position;
                var rotation = root.transform.rotation;
                int callbacks = receiver.Count;
                animator.InterruptMatchTarget(true);
                Assert.True(animator.isMatchingTarget);
                Assert.Equal(position, root.transform.position);
                Assert.Equal(rotation, root.transform.rotation);
                Assert.Equal(callbacks, receiver.Count);
                animator.Update(0);
                Assert.False(animator.isMatchingTarget);
                Assert.Equal(callbacks + 1, receiver.Count);
                Assert.True(receiver.Delta.sqrMagnitude > .1f);
                Near.Equal(apply ? position + receiver.Delta : position, root.transform.position, 1e-4f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Native controls: matchReview.life.*.False.playFull.* and .playOtherLoop.*.
        [Fact]
        public void ReplacingTheStateCancelsANonCompletingMatchAtEvaluation()
        {
            var animator = Create(out var root);
            try
            {
                animator.MatchTarget(new Vector3(10, 0, 0), Quaternion.identity, AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1), .2f, .8f, false);
                animator.Update(.25f);
                animator.Play("Empty", 0, 0);
                Assert.True(animator.isMatchingTarget);
                animator.Update(0);
                Assert.False(animator.isMatchingTarget);
                animator.MatchTarget(Vector3.one, Quaternion.identity, AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1), .2f, .8f, false);
                Assert.True(animator.isMatchingTarget);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
