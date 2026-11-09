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

        // Native controls: matchReview.precision, including a clip duration of 1.3 seconds.
        [Fact]
        public void PlaybackTimeRoundsEachNormalizedStepToSinglePrecision()
        {
            var animator = Create(out var root);
            try
            {
                var clip = animator.runtimeAnimatorController.animationClips[0];
                clip.m_HasRange = true;
                clip.m_StartTime = 0;
                clip.m_StopTime = 1.3f;
                clip.m_LoopTime = true;
                clip.m_Version++;
                animator.applyRootMotion = false;
                animator.Play("Move", 0, .17f); animator.Update(0);
                for (int i = 0; i < 20; i++)
                {
                    animator.Update(.03f);
                    if (i == 7) Assert.Equal(0x3eb59026, System.BitConverter.SingleToInt32Bits(animator.GetCurrentAnimatorStateInfo(0).normalizedTime));
                }
                Assert.Equal(0x3f21ac84, System.BitConverter.SingleToInt32Bits(animator.GetCurrentAnimatorStateInfo(0).normalizedTime));
                for (int i = 0; i < 20; i++) animator.Update(.1f);
                Assert.Equal(0x400ae14c, System.BitConverter.SingleToInt32Bits(animator.GetCurrentAnimatorStateInfo(0).normalizedTime));
                for (int i = 0; i < 20; i++) animator.Update(.37f);
                Assert.Equal(0x40fb980e, System.BitConverter.SingleToInt32Bits(animator.GetCurrentAnimatorStateInfo(0).normalizedTime));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Native control: matchReview.timing.0.4.0.6.False.step2 reaches the end exactly.
        [Fact]
        public void MatchingRemainsTrueAtTheExactEndUntilTimeAdvancesPastIt()
        {
            var animator = Create(out var root);
            try
            {
                animator.MatchTarget(new Vector3(10, 0, 0), Quaternion.identity, AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1), .2f, .5f, false);
                animator.Update(.5f);
                Assert.Equal(.5f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                Assert.True(animator.isMatchingTarget);
                animator.Update(.1f);
                Assert.False(animator.isMatchingTarget);
            }
            finally { Object.DestroyImmediate(root); }
        }


        sealed class Warnings : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
            public void LogException(System.Exception exception, Object context) => Messages.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + string.Format(format, args));
        }

        static void Loop(Animator animator)
        {
            var clip = animator.runtimeAnimatorController.animationClips[0];
            clip.m_LoopTime = true;
            clip.m_Version++;
        }

        // Native controls: tests/projects/humanoid match.pastTarget (looping) and match.once (not looping).
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ATargetAlreadyPassedMovesToTheNextLoopOrIsIgnored(bool loops)
        {
            var animator = Create(out var root);
            try
            {
                if (loops) Loop(animator);
                animator.Update(.7f);
                animator.MatchTarget(new Vector3(10, 0, 0), Quaternion.identity, AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1), .1f, .5f);
                Assert.Equal(loops, animator.isMatchingTarget);
                if (!loops) return;
                animator.Update(.5f);
                Assert.True(animator.isMatchingTarget);
                animator.Update(.3f);
                Assert.True(animator.isMatchingTarget);
                Near.Equal(new Vector3(10, 0, 0), new Vector3(root.transform.position.x, 0, root.transform.position.z), 1e-3f);
                animator.Update(.1f);
                Assert.False(animator.isMatchingTarget);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Native controls: tests/projects/humanoid match.inTransition and match.invalid.
        [Fact]
        public void CallsInATransitionOrWithAnInvalidTargetOnlyWarn()
        {
            var animator = Create(out var root);
            var previous = Debug.unityLogger.logHandler;
            var warnings = new Warnings();
            try
            {
                Debug.unityLogger.logHandler = warnings;
                animator.MatchTarget(Vector3.one, Quaternion.identity, (AvatarTarget)7, new MatchTargetWeightMask(Vector3.one, 1), .1f, .9f);
                Assert.False(animator.isMatchingTarget);
                animator.CrossFade("Empty", .5f);
                animator.Update(.1f);
                Assert.True(animator.IsInTransition(0));
                animator.MatchTarget(Vector3.one, Quaternion.identity, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 1), .1f, .9f);
                Assert.False(animator.isMatchingTarget);
                Assert.Equal(new[] { "Warning: Invalid Target Index", "Warning: Calling Animator.MatchTarget while in transition does not have any effect." },
                    warnings.Messages);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(root);
            }
        }

        // Native controls: tests/projects/humanoid match.genericRoot, match.genericLeftHand. A generic
        // rig has no body parts: every target matches the root, a goal target with the goal's
        // orientation offset.
        [Fact]
        public void BodyPartTargetsOfAGenericRigMatchTheRoot()
        {
            (Vector3, Quaternion) Matched(AvatarTarget target)
            {
                var animator = Create(out var root);
                try
                {
                    animator.MatchTarget(new Vector3(4, 0, 3), Quaternion.identity, target, new MatchTargetWeightMask(Vector3.one, 1), .1f, .6f);
                    Assert.True(animator.isMatchingTarget);
                    animator.Update(.3f);
                    animator.Update(.3f);
                    return (root.transform.position, root.transform.rotation);
                }
                finally { Object.DestroyImmediate(root); }
            }
            var (position, rotation) = Matched(AvatarTarget.Root);
            Near.Equal(new Vector3(4, 0, 3), new Vector3(position.x, 0, position.z), 1e-3f);
            Assert.True(Quaternion.Angle(Quaternion.identity, rotation) < .01f);
            Assert.Equal((position, rotation), Matched(AvatarTarget.Body));
            var (_, hand) = Matched(AvatarTarget.LeftHand);
            // The root turns so that it, times the left hand's goal offset (180 degrees about x+z), is the identity.
            Assert.True(Quaternion.Angle(Quaternion.Inverse(new Quaternion(0.707107f, 0f, 0.707107f, 0f)), hand) < .01f);
        }

        // Native control: tests/projects/humanoid massCenter.api (an Animator without an avatar keeps it).
        [Fact]
        public void LayersAffectMassCenterIsKeptWithoutAnAvatar()
        {
            var animator = new GameObject("Plain").AddComponent<Animator>();
            try
            {
                Assert.False(animator.layersAffectMassCenter);
                animator.layersAffectMassCenter = true;
                Assert.True(animator.layersAffectMassCenter);
                animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.identity);
            }
            finally { Object.DestroyImmediate(animator.gameObject); }
        }
    }
}
