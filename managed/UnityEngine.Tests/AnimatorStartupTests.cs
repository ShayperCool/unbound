using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using UnityEngine.Playables;
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
                AnimationTestLoop.Update(fixedStep: false);
                Near.Equal(before, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                Assert.True(animator.m_Player.Ticked);
                AnimationSystem.EndPlayModeEntry();
                AnimationTestLoop.Update(fixedStep: false);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before);
                var later = Create(out var laterRoot);
                try
                {
                    AnimationTestLoop.Update(fixedStep: false);
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

        sealed class Idle : PlayableBehaviour
        {
        }

        /// <summary>Play entry's zero interval belongs to the frame, not to animation: a graph of
        /// script playables in game or unscaled time prepares and processes at it too (playables
        /// frames.script); the next frame advances.</summary>
        [Theory]
        [InlineData(DirectorUpdateMode.GameTime)]
        [InlineData(DirectorUpdateMode.UnscaledGameTime)]
        public void AScriptGraphTakesTheZeroEntryInterval(DirectorUpdateMode mode)
        {
            string version = ProjectInfo.UnityVersion;
            float delta = Time.s_DeltaTime, unscaled = Time.s_UnscaledDeltaTime;
            var graph = PlayableGraph.Create("entry");
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = Time.s_UnscaledDeltaTime = .02f;
                graph.SetTimeUpdateMode(mode);
                var playable = ScriptPlayable<Idle>.Create(graph);
                ScriptPlayableOutput.Create(graph, "out").SetSourcePlayable(playable);
                graph.Play();
                AnimationSystem.BeginPlayMode();
                Unbound.Runtime.Playables.PlayableGraphData.UpdateAll();
                Assert.Equal(0.0, playable.GetTime());
                AnimationSystem.EndPlayModeEntry();
                Unbound.Runtime.Playables.PlayableGraphData.UpdateAll();
                Assert.Equal(.02, playable.GetTime(), 6);
            }
            finally
            {
                AnimationSystem.EndPlayModeEntry();
                graph.Destroy();
                Time.s_DeltaTime = delta;
                Time.s_UnscaledDeltaTime = unscaled;
                ProjectInfo.UnityVersion = version;
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
                AnimationTestLoop.Update(fixedStep: false);
                Near.Equal(.25f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                AnimationTestLoop.Update(fixedStep: false);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > .25f);
            }
            finally
            {
                Time.s_DeltaTime = oldDelta;
                ProjectInfo.UnityVersion = version;
                Object.DestroyImmediate(root);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HierarchyDeactivationAlsoResetsADisabledController(bool keep)
        {
            string version = ProjectInfo.UnityVersion;
            var animator = Create(out var root);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                animator.keepAnimatorStateOnDisable = keep;
                animator.Update(.2f);
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.enabled = false;
                root.SetActive(false);
                root.SetActive(true);
                animator.enabled = true;
                animator.Update(0);
                Near.Equal(keep ? before : 0f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            }
            finally
            {
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
                AnimationTestLoop.Update(fixedStep: true);
                Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > 0);
                animator.updateMode = AnimatorUpdateMode.Normal;
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                ProjectInfo.UnityVersion = "6000.0.72f1";
                AnimationTestLoop.Update(fixedStep: false);
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
