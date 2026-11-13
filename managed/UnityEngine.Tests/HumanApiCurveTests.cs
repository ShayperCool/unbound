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

        const string MirroredWithoutFootGoals = "AnimationClip mirrored humanoid clip without foot goal curves";

        static int Hits(string member)
        {
            foreach (var hit in Unbound.Runtime.Stubs.HitReport())
                if (hit.Key == member) return hit.Value;
            return 0;
        }

        /// <summary>A mirrored humanoid clip without a foot's goal curves reports that played
        /// with Foot IK its legs differ from the Editor's (tests/projects/humanoid apiCurves.mirrorFootIK;
        /// the warning itself is logged once per run). Control: the unmirrored clip does not.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AMirroredClipWithoutFootGoalsReportsIt(bool mirror)
        {
            int before = Hits(MirroredWithoutFootGoals);
            Muscles(Clip(false, mirror));
            Assert.Equal(mirror ? before + 1 : before, Hits(MirroredWithoutFootGoals));
        }

        /// <summary>A mirrored clip with one foot's goal curves reports it too (unmeasured); with
        /// both feet's goals it does not.</summary>
        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void AMirroredClipReportsWhenAFootLacksItsGoals(bool left, bool right, bool reports)
        {
            var clip = Clip(false, false);
            if (left) clip.SetCurve("", typeof(Animator), "LeftFootQ.w", AnimationCurve.Constant(0f, 1f, 1f));
            if (right) clip.SetCurve("", typeof(Animator), "RightFootT.y", AnimationCurve.Constant(0f, 1f, 0.1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.mirror = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            int before = Hits(MirroredWithoutFootGoals);
            _ = clip.humanMotion;
            Assert.Equal(reports ? before + 1 : before, Hits(MirroredWithoutFootGoals));
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
