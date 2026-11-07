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
    /// <summary>The Animators' per-frame update (<see cref="AnimationSystem.TickAnimators"/>)
    /// allocates nothing once warm: controllers with their behaviour, move and event bookkeeping,
    /// playable clip and mixer outputs, humanoid mixer root motion, and the culled
    /// visibility-return record. Measured over 4000 frames after a warm-up, so tiered JIT and
    /// lazily grown buffers do not count (a periodic allocation still shows in the window). The
    /// playable graphs' own update around it (<see cref="PlayableGraphData.UpdateAll"/>, output
    /// processing, notifications) runs every frame but is not measured here.</summary>
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
            AnimationSystem.TickAnimators(fixedStep: false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            PlayableGraphData.DeliverAllNotifications();
            return allocated;
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
    }
}
