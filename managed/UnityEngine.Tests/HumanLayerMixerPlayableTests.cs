using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Humanoid root motion through AnimationLayerMixerPlayables (6000.0.71f1,
    /// tests/projects/humanoid mixer.*): a layer mixer moves the root as its humanoid clips' bodies
    /// do, override layers by weight, and a layer whose avatar mask leaves out Root moves nothing;
    /// SetLayerMaskFromAvatarMask copies the mask's humanoid body parts.
    /// </summary>
    [Collection("Animation")]
    public class HumanLayerMixerPlayableTests
    {
        // A root-motion-only avatar (no skeleton to retarget), as DirectHumanPlayableRootMotionTests.
        static GameObject Rig(out Animator animator, out Avatar avatar)
        {
            var root = new GameObject("HumanLayers");
            avatar = Avatar.Create();
            avatar.SetConstant(new AvatarConstant
            {
                AvatarSkeleton = new Skeleton(1, 0),
                HumanSkeletonIndexArray = new[] { 0 },
                Human = new Human { Skeleton = new Skeleton(1, 0) },
            });
            animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return root;
        }

        /// <summary>A humanoid clip whose body moves along x from 0 to <paramref name="distance"/> in 1 s.</summary>
        static AnimationClip Clip(float distance)
        {
            var clip = new AnimationClip();
            void Add(string attribute, float from, float to) => clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                Curve = AnimationCurve.Linear(0f, from, 1f, to),
            });
            Add("RootT.x", 0f, distance);
            Add("RootT.y", 1f, 1f);
            Add("RootQ.x", 0f, 0f);
            Add("RootQ.y", 0f, 0f);
            Add("RootQ.z", 0f, 0f);
            Add("RootQ.w", 1f, 1f);
            clip.m_Version++;
            return clip;
        }

        /// <summary>The root's x after Evaluate(0) and one step of 0.25 s of the graph's output.</summary>
        static float Step(Func<PlayableGraph, Playable> build, List<Object> created)
        {
            var root = Rig(out var animator, out var avatar);
            created.Add(root);
            created.Add(avatar);
            var graph = PlayableGraph.Create("human-layers");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(build(graph));
                graph.Evaluate(0f);
                graph.Evaluate(0.25f);
                return animator.deltaPosition.x;
            }
            finally
            {
                graph.Destroy();
            }
        }

        static AnimationClipPlayable ClipPlayable(PlayableGraph graph, AnimationClip clip)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            return playable;
        }

        static AnimationLayerMixerPlayable Layers(PlayableGraph graph, AnimationClip below, AnimationClip above, float weight, AvatarMask mask)
        {
            var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(ClipPlayable(graph, below), 0, mixer, 0);
            graph.Connect(ClipPlayable(graph, above), 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, weight);
            if (mask != null) mixer.SetLayerMaskFromAvatarMask(1, mask);
            return mixer;
        }

        [Fact]
        public void LayersMoveTheRootAsTheirHumanoidClipsDo()
        {
            // Layer mixer composition is measured on 6000.0.71f1 (Compat.HumanPlayableLayerMixerMotionMeasured).
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var created = new List<Object>();
            var forward = Clip(2f);
            var back = Clip(-2f);
            created.Add(forward);
            created.Add(back);
            try
            {
                float alone = Step(g => ClipPlayable(g, forward), created);
                Assert.True(alone > 0.1f);
                // One layer: as the clip alone.
                Near.Equal(alone, Step(g =>
                {
                    var mixer = AnimationLayerMixerPlayable.Create(g, 1);
                    g.Connect(ClipPlayable(g, forward), 0, mixer, 0);
                    mixer.SetInputWeight(0, 1f);
                    return mixer;
                }, created), 1e-4f);
                // An override layer of weight 1 replaces the motion; of weight 0.5 it lands between.
                Near.Equal(-alone, Step(g => Layers(g, forward, back, 1f, null), created), 1e-4f);
                Near.Equal(0f, Step(g => Layers(g, forward, back, 0.5f, null), created), 1e-4f);

                // A mask without Root: the layer moves nothing. The mask is copied (Unbound's choice:
                // the Scripting API only says the call sets the layer's mask), so changing the source
                // afterwards does not change the layer.
                var noRoot = new AvatarMask();
                created.Add(noRoot);
                noRoot.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
                Near.Equal(alone, Step(g =>
                {
                    var mixer = Layers(g, forward, back, 1f, noRoot);
                    noRoot.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, true);
                    return mixer;
                }, created), 1e-4f);
                // Control: a mask keeping Root lets the layer move the root.
                Near.Equal(-alone, Step(g => Layers(g, forward, back, 1f, noRoot), created), 1e-4f);
            }
            finally
            {
                ProjectInfo.UnityVersion = version;
                foreach (var o in created) Object.DestroyImmediate(o);
            }
        }

        /// <summary>Clips' goals blend in the motion's frame; an input that animates no goal (or has no
        /// weight) does not decide the frame, one that has its goals relative to its body does, and
        /// the others are then taken back relative to their bodies.</summary>
        [Fact]
        public void OnlyInputsWithGoalsDecideTheGoalFrame()
        {
            var root = Rig(out _, out var avatar);
            var forward = Clip(2f);
            var goals = Clip(1f);
            void Goal(string attribute, float value) => goals.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                Curve = AnimationCurve.Constant(0f, 1f, value),
            });
            Goal("LeftFootT.x", 0.1f);
            Goal("LeftFootT.y", -0.9f);
            Goal("LeftFootT.z", 0.05f);
            Goal("LeftFootQ.x", 0f);
            Goal("LeftFootQ.y", 0f);
            Goal("LeftFootQ.z", 0f);
            Goal("LeftFootQ.w", 1f);
            goals.m_Version++;
            try
            {
                var bindings = new AnimationBindings(root);
                bindings.Bind(forward);
                bindings.Bind(goals);
                AnimStream Stream(bool withGoals, bool inMotion)
                {
                    var s = AnimStream.Rent(bindings);
                    int c = bindings.HumanChannel(HumanClipImport.GoalFirst);
                    Assert.True(c >= 0);
                    if (withGoals) s.Animated[c] = true;
                    s.GoalsInMotion = inMotion;
                    return s;
                }
                var inputs = new List<(AnimStream, float)>();
                var into = AnimStream.Rent(bindings);
                // Two clips in the motion's frame and a weighted input without goals: the motion's frame.
                inputs.Add((Stream(true, true), 0.5f));
                inputs.Add((Stream(true, true), 0.5f));
                inputs.Add((Stream(false, false), 1f));
                AnimationEvaluation.MixInto(into, inputs);
                Assert.True(into.GoalsInMotion);
                // A body-relative input without weight does not decide either.
                inputs[2] = (Stream(true, false), 0f);
                into.Reset(bindings);
                AnimationEvaluation.MixInto(into, inputs);
                Assert.True(into.GoalsInMotion);
                // With weight it does: everything blends relative to the bodies.
                inputs[2] = (Stream(true, false), 0.5f);
                into.Reset(bindings);
                AnimationEvaluation.MixInto(into, inputs);
                Assert.False(into.GoalsInMotion);
                Assert.False(inputs[0].Item1.GoalsInMotion);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(forward);
                Object.DestroyImmediate(goals);
            }
        }

        /// <summary>Generic root curves alone on a humanoid Animator in a PlayableGraph move it by
        /// its human scale, as its controller does (6000.0.71f1, tests/projects/humanoid
        /// genericOnHuman.graph). Control: the same clip on a plain Animator moves by the curve.</summary>
        [Fact]
        public void GenericRootCurvesMoveAHumanoidByItsHumanScale()
        {
            var created = new List<Object>();
            var move = new AnimationClip();
            move.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            created.Add(move);
            float Moved(GameObject root, Animator animator)
            {
                created.Add(root);
                var graph = PlayableGraph.Create("generic-on-human");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, move));
                    graph.Evaluate(0f);
                    graph.Evaluate(0.25f);
                    return animator.deltaPosition.x;
                }
                finally { graph.Destroy(); }
            }
            try
            {
                var human = Rig(out var animator, out var avatar);
                created.Add(avatar);
                avatar.Constant.Human.Scale = 2f;
                Near.Equal(0.5f, Moved(human, animator), 1e-4f);
                var plain = new GameObject("Plain");
                var generic = plain.AddComponent<Animator>();
                generic.applyRootMotion = true;
                Near.Equal(0.25f, Moved(plain, generic), 1e-4f);
            }
            finally
            {
                foreach (var o in created) Object.DestroyImmediate(o);
            }
        }
    }
}
