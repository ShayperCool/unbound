using System;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Animators' per-frame update (<see cref="AnimationSystem.TickAnimators"/> and its End leaf)
    /// allocates nothing once warm: controllers with their behaviour, move and event bookkeeping,
    /// playable clip and mixer outputs, humanoid mixer root motion, and the culled
    /// visibility-return record. The playable graphs' own passes around it
    /// (<see cref="PlayableGraphData.UpdateAll"/>, output processing, notifications and explicit
    /// <see cref="PlayableGraph.Evaluate(float)"/>) allocate nothing either, for plain, script,
    /// mixer and multi-output graphs, visible or culled. Measured over 4000 frames after a
    /// warm-up, so tiered JIT and lazily grown buffers do not count (a periodic allocation still
    /// shows in the window).</summary>
    [Collection("Animation")]
    public sealed class AnimationAllocationTests
    {
        // Warm until this many consecutive frames allocate nothing (tiered JIT promotion and
        // first growth of pooled lists happen early), then measure Frames frames.
        const int QuietFrames = 300, MaxWarmup = 5000, Frames = 4000;
        // A unit sum: the supported humanoid mixer context.
        static readonly float[] InputWeights = { 0.2f, 0.3f, 0.5f };

        /// <summary>One frame; the bytes the Animators' own update allocated.</summary>
        static long Frame()
        {
            PlayableGraphData.UpdateAll();
            PlayableGraphData.ProcessOtherOutputs();
            long before = GC.GetAllocatedBytesForCurrentThread();
            AnimationTestLoop.Tick();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            PlayableGraphData.DeliverAllNotifications();
            return allocated;
        }

        /// <summary>One whole frame of the graph and Animator passes, with the explicit
        /// evaluations of <paramref name="manual"/>; the bytes it allocated.</summary>
        static long GraphFrame(PlayableGraph manual)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            PlayableGraphData.UpdateAll();
            if (manual.IsValid()) manual.Evaluate(0.02f);
            PlayableGraphData.ProcessOtherOutputs();
            AnimationTestLoop.Tick();
            PlayableGraphData.DeliverAllNotifications();
            PlayableGraphData.LateUpdate();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        sealed class CountingBehaviour : PlayableBehaviour
        {
            internal int Prepares, Processes;

            public override void PrepareFrame(Playable playable, FrameData info) => Prepares++;

            public override void ProcessFrame(Playable playable, FrameData info, object playerData) => Processes++;
        }

        static AnimationClip GenericClip(float speed)
        {
            var clip = new AnimationClip();
            clip.SetCurve("Child", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, speed));
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Linear(0f, 0f, 1f, speed));
            clip.SetCurve("Child", typeof(Transform), "m_LocalScale.y", AnimationCurve.Linear(0f, 1f, 1f, 2f));
            clip.m_LoopTime = true;
            return clip;
        }

        static AnimationClip HumanClip(float turn)
        {
            var clip = new AnimationClip();
            void Add(string attribute, float from, float to) => clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                Curve = AnimationCurve.Linear(0f, from, 1f, to),
            });
            Add("RootT.x", 0f, 2f);
            Add("RootT.y", 1f, 1f);
            Add("RootT.z", 0f, turn);
            Add("RootQ.x", 0f, 0f);
            Add("RootQ.y", 0f, 0.5f * turn);
            Add("RootQ.z", 0f, 0f);
            Add("RootQ.w", 1f, 0.8660254f);
            clip.m_LoopTime = true;
            clip.m_Version++;
            return clip;
        }

        // The humanoid rig is root-motion only (as in DirectHumanPlayableRootMotionTests): with no
        // renderer it is culled, so CullUpdateTransforms computes motion without a skeleton write.
        static GameObject Rig(bool human, AnimatorCullingMode mode, out Animator animator, out Avatar avatar)
        {
            var root = new GameObject("AllocationRig");
            new GameObject("Child").transform.SetParent(root.transform, false);
            avatar = null;
            if (human)
            {
                avatar = Avatar.Create();
                avatar.SetConstant(new AvatarConstant
                {
                    AvatarSkeleton = new Skeleton(1, 0),
                    HumanSkeletonIndexArray = new[] { 0 },
                    Human = new Human { Skeleton = new Skeleton(1, 0) },
                });
            }
            animator = root.AddComponent<Animator>();
            if (avatar != null) animator.avatar = avatar;
            animator.applyRootMotion = true;
            animator.cullingMode = mode;
            return root;
        }

        [Theory]
        [InlineData("controller", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("controller", AnimatorCullingMode.CullUpdateTransforms)]
        [InlineData("clip", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("mixer", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("mixer", AnimatorCullingMode.CullUpdateTransforms)]
        [InlineData("controllerAndMixer", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("humanMixer", AnimatorCullingMode.CullUpdateTransforms)]
        public void AutomaticAnimationFramesDoNotAllocate(string shape, AnimatorCullingMode mode)
        {
            string version = ProjectInfo.UnityVersion;
            float delta = Time.s_DeltaTime;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.s_DeltaTime = 0.02f;
            bool human = shape == "humanMixer";
            var root = Rig(human, mode, out var animator, out var avatar);
            var graph = default(PlayableGraph);
            var clips = new AnimationClip[3];
            LoadedFile file = null;
            try
            {
                if (shape.StartsWith("controller", StringComparison.Ordinal))
                {
                    file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion),
                        "allocation.asset", null, ObjectGraphBuilder.Kind.Asset, null);
                    animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
                }
                if (shape != "controller")
                {
                    graph = PlayableGraph.Create("allocation");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                    Playable source;
                    if (shape == "clip")
                    {
                        clips[0] = GenericClip(1f);
                        source = AnimationClipPlayable.Create(graph, clips[0]);
                    }
                    else
                    {
                        var mixer = AnimationMixerPlayable.Create(graph, 3);
                        for (int i = 0; i < 3; i++)
                        {
                            clips[i] = human ? HumanClip(1f + i) : GenericClip(1f + i);
                            var input = AnimationClipPlayable.Create(graph, clips[i]);
                            input.SetApplyFootIK(false);
                            mixer.ConnectInput(i, input, 0, InputWeights[i]);
                        }
                        source = mixer;
                    }
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(source);
                    graph.Play();
                }
                for (int i = 0, quiet = 0; i < MaxWarmup && quiet < QuietFrames; i++)
                    quiet = Frame() == 0 ? quiet + 1 : 0;
                long allocated = 0;
                for (int i = 0; i < Frames; i++) allocated += Frame();
                Assert.True(allocated == 0, $"{shape}/{mode}: {allocated} bytes over {Frames} frames ({allocated / (double)Frames:F1} per frame)");
                if (human) Assert.True(animator.deltaPosition.magnitude > 0f);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(root);
                if (avatar != null) Object.DestroyImmediate(avatar);
                foreach (var clip in clips) if (clip != null) Object.DestroyImmediate(clip);
                ProjectInfo.UnityVersion = version;
                Time.s_DeltaTime = delta;
            }
        }

        /// <summary>The graph passes themselves: a plain Playable and a ScriptPlayable above clips,
        /// a mixer with a plain input, several outputs (two Animators sharing a clip through two
        /// ports, and a script output), and a Manual graph evaluated explicitly. With no renderer,
        /// CullCompletely holds the animation subtrees while the playables above keep running.</summary>
        [Theory]
        [InlineData("plain", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("plain", AnimatorCullingMode.CullCompletely)]
        [InlineData("script", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("script", AnimatorCullingMode.CullCompletely)]
        [InlineData("mixer", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("mixer", AnimatorCullingMode.CullUpdateTransforms)]
        [InlineData("outputs", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("outputs", AnimatorCullingMode.CullCompletely)]
        [InlineData("manual", AnimatorCullingMode.AlwaysAnimate)]
        [InlineData("manual", AnimatorCullingMode.CullCompletely)]
        public void GraphFramesDoNotAllocate(string shape, AnimatorCullingMode mode)
        {
            string version = ProjectInfo.UnityVersion;
            float delta = Time.s_DeltaTime;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.s_DeltaTime = 0.02f;
            var root = Rig(false, mode, out var animator, out _);
            var second = Rig(false, AnimatorCullingMode.AlwaysAnimate, out var secondAnimator, out _);
            var clips = new[] { GenericClip(1f), GenericClip(2f) };
            var graph = PlayableGraph.Create("graph-allocation");
            CountingBehaviour behaviour = null;
            try
            {
                graph.SetTimeUpdateMode(shape == "manual" ? DirectorUpdateMode.Manual : DirectorUpdateMode.GameTime);
                var clip = AnimationClipPlayable.Create(graph, clips[0]);
                var plain = Playable.Create(graph, 1);
                plain.ConnectInput(0, clip, 0, 1f);
                Playable source = plain;
                if (shape == "script" || shape == "manual")
                {
                    var script = ScriptPlayable<CountingBehaviour>.Create(graph, 1);
                    behaviour = script.GetBehaviour();
                    script.ConnectInput(0, plain, 0, 1f);
                    source = script;
                    ScriptPlayableOutput.Create(graph, "script").SetSourcePlayable(source, 0);
                    source.SetOutputCount(2);
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(source, 1);
                }
                else if (shape == "mixer")
                {
                    var mixer = AnimationMixerPlayable.Create(graph, 2);
                    mixer.ConnectInput(0, plain, 0, 0.25f);
                    mixer.ConnectInput(1, AnimationClipPlayable.Create(graph, clips[1]), 0, 0.75f);
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(mixer);
                }
                else if (shape == "outputs")
                {
                    // One clip through two output ports: a plain Playable for each Animator.
                    clip.SetOutputCount(2);
                    var other = Playable.Create(graph, 1);
                    other.ConnectInput(0, clip, 1, 1f);
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
                    AnimationPlayableOutput.Create(graph, "other", secondAnimator).SetSourcePlayable(other);
                    var script = ScriptPlayable<CountingBehaviour>.Create(graph, 0);
                    behaviour = script.GetBehaviour();
                    ScriptPlayableOutput.Create(graph, "script").SetSourcePlayable(script);
                }
                else AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(plain);
                graph.Play();
                var manual = shape == "manual" ? graph : default;
                for (int i = 0, quiet = 0; i < MaxWarmup && quiet < QuietFrames; i++)
                    quiet = GraphFrame(manual) == 0 ? quiet + 1 : 0;
                long allocated = 0;
                int prepares = behaviour?.Prepares ?? 0;
                double time = plain.GetTime();
                for (int i = 0; i < Frames; i++) allocated += GraphFrame(manual);
                Assert.True(allocated == 0, $"{shape}/{mode}: {allocated} bytes over {Frames} frames ({allocated / (double)Frames:F1} per frame)");
                // The passes ran: the plain Playable above the clips advanced every frame, culled or not.
                Assert.InRange(plain.GetTime() - (time + Frames * (double)0.02f), -1e-9, 1e-9);
                if (behaviour != null) Assert.Equal(prepares + Frames, behaviour.Prepares);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                PlayableGraphData.FlushDestroyed();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(second);
                foreach (var clip in clips) Object.DestroyImmediate(clip);
                ProjectInfo.UnityVersion = version;
                Time.s_DeltaTime = delta;
            }
        }
    }
}
