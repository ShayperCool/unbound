using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Animator.hasBoundPlayables counts the controller's own graph (tests/projects/boundplayables
    /// measures the Editor).</summary>
    [Collection("Animation")]
    public sealed class AnimatorBoundPlayablesTests
    {
        static AnimatorController Controller()
        {
            var controller = new AnimatorController { name = "Bound" };
            controller.AddLayer("Base Layer");
            controller.AddMotion(new AnimationClip { name = "Clip" });
            return controller;
        }

        [Fact]
        public void TheControllersGraphCountsFromItsAssignmentToTheEndOfTheFrameItIsTakenAway()
        {
            var animator = new GameObject("Bound").AddComponent<Animator>();
            Assert.False(animator.hasBoundPlayables);
            animator.runtimeAnimatorController = Controller();
            Assert.True(animator.hasBoundPlayables);
            animator.enabled = false;
            Assert.True(animator.hasBoundPlayables);
            animator.runtimeAnimatorController = null;
            Assert.True(animator.hasBoundPlayables);
            Time.s_FrameCount++;
            Assert.False(animator.hasBoundPlayables);
        }

        [Fact]
        public void AnInactiveObjectsControllerCountsOnceTheObjectIsActive()
        {
            var go = new GameObject("Inactive");
            go.SetActive(false);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = Controller();
            Assert.False(animator.hasBoundPlayables);
            go.SetActive(true);
            Assert.True(animator.hasBoundPlayables);
            go.SetActive(false);
            Time.s_FrameCount++;
            Assert.True(animator.hasBoundPlayables);
        }
    }
}
