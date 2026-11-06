using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Animation")]
    public sealed class AnimatorStartupTests
    {
        static Animator Create(out GameObject root)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion),
                "startup.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            root = new GameObject("Root");
            new GameObject("Child").transform.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            return animator;
        }

        // Native entry/positive and entry/zero controls: prior evaluation does not consume the
        // automatic entry interval. Explicit evaluation still uses the supplied delta.
        [Theory]
        [InlineData(0f)]
        [InlineData(.03f)]
        public void EntryEvaluationSamplesWithoutAnAdditionalStep(float explicitDelta)
        {
            string version = ProjectInfo.UnityVersion;
            float oldDelta = Time.s_DeltaTime;
            var animator = Create(out var root);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = .02f;
                AnimationSystem.BeginPlayMode();
                animator.Update(explicitDelta);
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                AnimationSystem.UpdateAnimators(fixedStep: false);
                Near.Equal(before, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                Assert.True(animator.m_Player.Ticked);
                AnimationSystem.EndPlayModeEntry();
                AnimationSystem.UpdateAnimators(fixedStep: false);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before);
                var later = Create(out var laterRoot);
                try
                {
                    AnimationSystem.UpdateAnimators(fixedStep: false);
                    Assert.True(later.GetCurrentAnimatorStateInfo(0).normalizedTime > 0);
                }
                finally { Object.DestroyImmediate(laterRoot); }
            }
            finally
            {
                AnimationSystem.EndPlayModeEntry();
                Time.s_DeltaTime = oldDelta;
                ProjectInfo.UnityVersion = version;
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void InitialAutomaticPlaySamplesTheRequestedOffset()
        {
            string version = ProjectInfo.UnityVersion;
            float oldDelta = Time.s_DeltaTime;
            var animator = Create(out var root);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = .02f;
                AnimationSystem.EndPlayModeEntry();
                animator.Play("Move", 0, .25f);
                AnimationSystem.UpdateAnimators(fixedStep: false);
                Near.Equal(.25f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                AnimationSystem.UpdateAnimators(fixedStep: false);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > .25f);
            }
            finally
            {
                Time.s_DeltaTime = oldDelta;
                ProjectInfo.UnityVersion = version;
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void InitialSceneLookupExcludesObjectsAwaitingAwake()
        {
            var root = new GameObject("LoadedRoot");
            var child = new GameObject("LoadedChild");
            child.transform.SetParent(root.transform, false);
            GameObject runtime = null;
            try
            {
                SceneLoader.ActivateRoots(new[] { root }, () =>
                {
                    Assert.True(root.activeSelf);
                    Assert.False(root.activeInHierarchy);
                    Assert.False(child.activeInHierarchy);
                    Assert.DoesNotContain(root, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                    Assert.Contains(root, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
                    runtime = new GameObject("RuntimeRoot");
                    Assert.True(runtime.activeInHierarchy);
                });
                Assert.True(root.activeInHierarchy);
                Assert.True(child.activeInHierarchy);
                Assert.Contains(root, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            }
            finally
            {
                Object.DestroyImmediate(runtime);
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void FixedStepsAndUnmeasuredReleasesKeepTheirTime()
        {
            string version = ProjectInfo.UnityVersion;
            float oldDelta = Time.s_DeltaTime;
            var animator = Create(out var root);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = .02f;
                AnimationSystem.BeginPlayMode();
                animator.updateMode = AnimatorUpdateMode.Fixed;
                AnimationSystem.UpdateAnimators(fixedStep: true);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > 0);
                animator.updateMode = AnimatorUpdateMode.Normal;
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                ProjectInfo.UnityVersion = "6000.0.72f1";
                AnimationSystem.UpdateAnimators(fixedStep: false);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before);
            }
            finally
            {
                AnimationSystem.EndPlayModeEntry();
                Time.s_DeltaTime = oldDelta;
                ProjectInfo.UnityVersion = version;
                Object.DestroyImmediate(root);
            }
        }
    }
}
