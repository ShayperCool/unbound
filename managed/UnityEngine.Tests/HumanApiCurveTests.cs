using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Humanoid curves set on a clip through AnimationClip.SetCurve or
    /// AnimationUtility.SetEditorCurve are the clip's humanoid curves, as an asset's (6000.0.71f1
    /// Editor, tests/projects/humanoid apiCurves.*): humanMotion is true, they play as a pose and
    /// mirror with the clip's Mirror setting.</summary>
    [Collection("Animation")]
    public sealed class HumanApiCurveTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        static AnimationClip Clip(bool editorCurve, bool mirror)
        {
            var clip = new AnimationClip();
            void Set(string name, float value)
            {
                var curve = AnimationCurve.Constant(0f, 1f, value);
                if (editorCurve) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
                else clip.SetCurve("", typeof(Animator), name, curve);
            }
            Set("Left Arm Down-Up", 0.8f);
            Set("RootT.y", 0.9f);
            Set("RootQ.w", 1f);
            if (mirror)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.mirror = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            return clip;
        }

        /// <summary>The muscles a clip gives the test humanoid in a manual graph.</summary>
        float[] Muscles(AnimationClip clip)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            var graph = PlayableGraph.Create("api curves");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
            try
            {
                graph.Evaluate(0f);
                var handler = new HumanPoseHandler(animator.avatar, root.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                handler.Dispose();
                return pose.muscles;
            }
            finally
            {
                graph.Destroy();
            }
        }

        static int Muscle(string name) => Array.IndexOf(HumanTrait.MuscleName, name);

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ApiSetHumanoidCurvesPlayAsTheClipsHumanoidCurves(bool editorCurve, bool mirror)
        {
            var clip = Clip(editorCurve, mirror);
            Assert.True(clip.humanMotion);
            var muscles = Muscles(clip);
            float left = muscles[Muscle("Left Arm Down-Up")], right = muscles[Muscle("Right Arm Down-Up")];
            Assert.Equal(mirror ? 0f : 0.8f, left, 3);
            Assert.Equal(mirror ? 0.8f : 0f, right, 3);
        }

        /// <summary>Control: a Transform curve set through SetCurve stays a Transform curve, and a
        /// float curve on the Animator that is no humanoid curve does not make the clip a
        /// humanoid one.</summary>
        [Fact]
        public void OtherApiSetCurvesStayWhatTheyWere()
        {
            var clip = new AnimationClip();
            clip.SetCurve("Hips", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 1f));
            clip.SetCurve("", typeof(Animator), "Speed", AnimationCurve.Constant(0f, 1f, 2f));
            Assert.False(clip.humanMotion);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.Contains(bindings, b => b.type == typeof(Transform) && b.propertyName == "m_LocalPosition.x");
            Assert.Contains(bindings, b => b.type == typeof(Animator) && b.propertyName == "Speed");
        }
    }
}
