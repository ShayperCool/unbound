using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Animator.hasRootMotion answers whether the avatar has a root motion transform
    /// (tests/projects/awakerootmotion measures the Editor, imported and humanoid avatars included).</summary>
    [Collection("Animation")]
    public sealed class AnimatorHasRootMotionTests
    {
        static Animator Rig()
        {
            var root = new GameObject("R");
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            new GameObject("Leg").transform.SetParent(hips, false);
            return root.AddComponent<Animator>();
        }

        [Fact]
        public void NoAvatarHasNoRootMotion()
        {
            var animator = Rig();
            Assert.False(animator.hasRootMotion);
            animator.applyRootMotion = true;
            Assert.False(animator.hasRootMotion);
            Object.DestroyImmediate(animator.gameObject);
        }

        [Theory]
        [InlineData("Hips", true)]
        [InlineData("Leg", true)]
        [InlineData("", false)]
        public void AGenericAvatarHasRootMotionWithItsRootMotionTransform(string transform, bool expected)
        {
            var animator = Rig();
            animator.avatar = AvatarBuilder.BuildGenericAvatar(animator.gameObject, transform);
            Assert.Equal(expected, animator.hasRootMotion);
            // Neither applying root motion nor the Animator's state changes the answer.
            animator.applyRootMotion = !animator.applyRootMotion;
            Assert.Equal(expected, animator.hasRootMotion);
            animator.enabled = false;
            Assert.Equal(expected, animator.hasRootMotion);
            animator.avatar = null;
            Assert.False(animator.hasRootMotion);
            Object.DestroyImmediate(animator.gameObject);
        }
    }
}
