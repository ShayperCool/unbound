using System.Collections.Generic;
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

        sealed class Counting : PlayableBehaviour
        {
            public int Starts, Plays, Prepares;
            public float Delta = -1f;

            public override void OnGraphStart(Playable playable) => Starts++;

            public override void OnBehaviourPlay(Playable playable, FrameData info) => Plays++;

            public override void PrepareFrame(Playable playable, FrameData info)
            {
                Prepares++;
                Delta = info.deltaTime;
            }
        }

        sealed class CountingAsset : PlayableAsset
        {
            public override double duration => 10.0;

            public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Counted(graph);
        }

        static Playable Counted(PlayableGraph graph)
        {
            var playable = ScriptPlayable<Counting>.Create(graph);
            ScriptPlayableOutput.Create(graph, "out").SetSourcePlayable(playable);
            return playable;
        }

        static Counting CountsOf(PlayableGraph graph) => ((ScriptPlayable<Counting>)graph.GetRootPlayable(0)).GetBehaviour();

        static PlayableDirector Director(List<Object> created, DirectorUpdateMode mode = DirectorUpdateMode.GameTime)
        {
            var go = new GameObject("director");
            created.Add(go);
            var asset = ScriptableObject.CreateInstance<CountingAsset>();
            created.Add(asset);
            var director = go.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.timeUpdateMode = mode;
            director.playableAsset = asset;
            return director;
        }

        /// <summary>A PlayableDirector's own clock at Play entry (6000.0.71f1,
        /// tests/projects/animstartup director profile; the Scripting API's Play and initialTime
        /// pages say only that playback starts, at initialTime when first played): its first
        /// automatic update prepares at the time it was played at, with a zero frame delta at
        /// entry and the frame's delta later, and the next one advances. Control: a plain graph
        /// played after entry advances in its first update.</summary>
        [Theory]
        [InlineData(true, 0.0)]
        [InlineData(true, 0.25)]
        [InlineData(false, 0.0)]
        [InlineData(false, 0.25)]
        public void ADirectorsFirstUpdatePreparesAtTheTimeItWasPlayedAt(bool entry, double initialTime)
        {
            string version = ProjectInfo.UnityVersion;
            float delta = Time.s_DeltaTime, unscaled = Time.s_UnscaledDeltaTime;
            var created = new List<Object>();
            var plain = PlayableGraph.Create("plain");
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = Time.s_UnscaledDeltaTime = .02f;
                if (entry) AnimationSystem.BeginPlayMode();
                var director = Director(created);
                director.initialTime = initialTime;
                director.Play();
                plain.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var plainRoot = Counted(plain);
                plain.Play();
                Unbound.Runtime.Playables.PlayableGraphData.UpdateAll();
                Assert.Equal(initialTime, director.time);
                var counts = CountsOf(director.playableGraph);
                Assert.Equal(1, counts.Prepares);
                Assert.Equal(entry ? 0f : .02f, counts.Delta);
                Assert.Equal(entry ? 0.0 : .02, plainRoot.GetTime(), 6);
                AnimationSystem.EndPlayModeEntry();
                Unbound.Runtime.Playables.PlayableGraphData.UpdateAll();
                Assert.Equal(initialTime + .02, director.time, 6);
            }
            finally
            {
                AnimationSystem.EndPlayModeEntry();
                plain.Destroy();
                foreach (var o in created) Object.DestroyImmediate(o);
                Time.s_DeltaTime = delta;
                Time.s_UnscaledDeltaTime = unscaled;
                ProjectInfo.UnityVersion = version;
            }
        }

        /// <summary>A PlayableDirector's Evaluate delivers the starts waiting on every graph on an
        /// automatic clock, other directors' and plain graphs': OnGraphStart and OnBehaviourPlay
        /// come there, once, and not at the next director update (6000.0.71f1,
        /// tests/projects/animstartup director profile, */play, */graph; the Scripting API's
        /// Evaluate page speaks of the director's own graph only). Controls: nothing is started
        /// before the Evaluate, and a director on a manual clock is not started by it.</summary>
        [Fact]
        public void ADirectorsEvaluateStartsTheGraphsPlayedBeforeIt()
        {
            var created = new List<Object>();
            var plain = PlayableGraph.Create("plain");
            try
            {
                var played = Director(created);
                played.Play();
                var manual = Director(created, DirectorUpdateMode.Manual);
                manual.Play();
                plain.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                Counted(plain);
                plain.Play();
                var evaluated = Director(created);
                evaluated.Play();
                Assert.Equal(0, CountsOf(played.playableGraph).Starts);
                Assert.Equal(0, CountsOf(plain).Starts);

                evaluated.Evaluate();
                foreach (var counts in new[] { CountsOf(played.playableGraph), CountsOf(plain), CountsOf(evaluated.playableGraph) })
                {
                    Assert.Equal(1, counts.Starts);
                    Assert.Equal(1, counts.Plays);
                }
                Assert.Equal(0, CountsOf(played.playableGraph).Prepares);
                Assert.Equal(1, CountsOf(evaluated.playableGraph).Prepares);
                Assert.Equal(0, CountsOf(manual.playableGraph).Starts);

                Unbound.Runtime.Playables.PlayableGraphData.UpdateAll();
                Assert.Equal(1, CountsOf(played.playableGraph).Starts);
                Assert.Equal(1, CountsOf(plain).Plays);
                Assert.Equal(1, CountsOf(played.playableGraph).Prepares);
            }
            finally
            {
                plain.Destroy();
                foreach (var o in created) Object.DestroyImmediate(o);
            }
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
