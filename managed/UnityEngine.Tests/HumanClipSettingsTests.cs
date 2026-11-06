using System;
using System.Linq;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A humanoid clip's root settings (bake into pose, based upon, offsets, loop pose,
    /// mirror) on a synthetic clip. The values against the Editor are checked by
    /// tests/projects/humanoid.</summary>
    public class HumanClipSettingsTests
    {
        const int RootT = HumanClipImport.RootFirst, RootQ = HumanClipImport.RootFirst + 3, Goals = HumanClipImport.GoalFirst;

        static F4 Yaw(float degrees)
        {
            float half = degrees * MathF.PI / 360f;
            return new F4(0f, MathF.Sin(half), 0f, MathF.Cos(half));
        }

        static float YawOf(F4 q) => 2f * MathF.Atan2(q.y, q.w) * 180f / MathF.PI;

        static readonly bool[] Present = Enumerable.Range(0, HumanClipImport.CurveCount).Select(i => i >= RootT && i < Goals + 14).ToArray();

        /// <summary>The body moves forward, down and turns from 10 to 40 degrees over one second; the
        /// left foot (lower) rises relative to the body, the right foot stays.</summary>
        static void Sample(float t, float[] v)
        {
            Array.Clear(v);
            v[RootT] = 0.1f + 0.2f * t;
            v[RootT + 1] = 1f - 0.3f * t;
            v[RootT + 2] = 0.05f + 0.4f * t;
            var q = Yaw(10f + 30f * t);
            v[RootQ] = q.x; v[RootQ + 1] = q.y; v[RootQ + 2] = q.z; v[RootQ + 3] = q.w;
            v[Goals] = -0.1f; v[Goals + 1] = -0.9f + 0.1f * t; v[Goals + 6] = 1f;
            v[Goals + 7] = 0.1f; v[Goals + 8] = -0.8f; v[Goals + 13] = 1f;
        }

        static HumanClipMotion Build(Func<HumanClipSettings, HumanClipSettings> set)
        {
            var settings = set(HumanClipSettings.Default);
            settings.LoopTime = true;
            return HumanClipMotion.Build(Sample, Present, 0f, 1f, settings);
        }

        static Trs MotionAt(HumanClipMotion clip, float t)
        {
            var v = new float[HumanClipImport.CurveCount];
            Sample(t, v);
            var body = HumanRootMotion.ReadRoot(v, Present);
            return HumanRootMotion.MotionX(clip, body.t, body.q, HumanRootMotion.FeetY(v, Present, body));
        }

        /// <summary>A muscle curve as imported clips bind it (the Animator, path "").</summary>
        static void Add(AnimationClip clip, string attribute, AnimationCurve curve)
        {
            clip.m_Curves.Add(new ClipCurve { Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 }, Curve = curve });
            clip.m_Version++;
        }

        static void Near(float expected, float actual, float eps = 1e-5f) => Assert.InRange(actual, expected - eps, expected + eps);

        [Fact]
        public void TheDefaultsFollowTheBodyFromTheOriginalHeight()
        {
            var clip = Build(s => s);
            var m = MotionAt(clip, 0.5f);
            Near(0.2f, m.t.x);
            Near(-0.15f, m.t.y);
            Near(0.25f, m.t.z);
            Near(25f, YawOf(m.q), 1e-3f);
        }

        [Fact]
        public void BakedPartsKeepTheStartValue()
        {
            var clip = Build(s => { s.LoopBlendOrientation = s.LoopBlendPositionY = s.LoopBlendPositionXZ = true; return s; });
            var start = MotionAt(clip, 0f);
            var m = MotionAt(clip, 0.7f);
            Near(start.t.x, m.t.x);
            Near(start.t.y, m.t.y);
            Near(start.t.z, m.t.z);
            Near(10f, YawOf(m.q), 1e-3f);
            // The baked parts give no average speed.
            Near(0f, clip.AverageSpeed.x);
            Near(0f, clip.AverageSpeed.y);
            Near(0f, clip.AverageSpeed.z);
            Near(0f, clip.AverageAngularSpeed);
        }

        [Fact]
        public void BasedUponTheOriginalStartsAtTheOriginFacingForward()
        {
            var clip = Build(s => { s.KeepOriginalOrientation = s.KeepOriginalPositionXZ = true; return s; });
            var start = MotionAt(clip, 0f);
            Near(0f, start.t.x);
            Near(0f, start.t.y);
            Near(0f, start.t.z);
            Near(0f, YawOf(start.q), 1e-3f);
            var m = MotionAt(clip, 1f);
            Near(0.2f, m.t.x);
            Near(0.4f, m.t.z);
            Near(30f, YawOf(m.q), 1e-3f);
        }

        [Fact]
        public void CenterOfMassAndFeetSetTheHeight()
        {
            var com = Build(s => { s.KeepOriginalPositionY = false; s.Level = 0.05f; return s; });
            Near(0.85f + 0.05f, MotionAt(com, 0.5f).t.y);
            var feet = Build(s => { s.KeepOriginalPositionY = false; s.HeightFromFeet = true; return s; });
            // The lowest foot: the left one at the start (0.1 against 0.2), both at the end.
            Near(0.1f, MotionAt(feet, 0f).t.y);
            Near(-0.1f, MotionAt(feet, 1f).t.y);
            var baked = Build(s => { s.KeepOriginalPositionY = false; s.HeightFromFeet = true; s.LoopBlendPositionY = true; return s; });
            Near(0.1f, MotionAt(baked, 1f).t.y);
        }

        [Fact]
        public void TheRotationOffsetTurnsTheMotion()
        {
            var clip = Build(s => { s.OrientationOffsetY = 30f; s.Level = 0.1f; return s; });
            var m = MotionAt(clip, 0f);
            Near(40f, YawOf(m.q), 1e-3f);
            Near(0.1f, m.t.y);
        }

        [Fact]
        public void LoopPoseBringsTheStopsBodyToTheStarts()
        {
            var clip = Build(s => { s.LoopBlend = true; s.LoopBlendOrientation = s.LoopBlendPositionY = true; return s; });
            var previous = new float[HumanClipImport.CurveCount];
            var current = new float[HumanClipImport.CurveCount];
            Sample(0f, previous);
            Sample(0f, current);
            HumanRootMotion.Evaluate(clip, new HumanClipMotionInput { Time = 0f, PreviousTime = 0f, Speed = 1f }, previous, current, Present, out var atStart);
            float end = HumanRootMotion.ClipTime(clip, 0.99999f, 1f, 0f, out _);
            Sample(end, current);
            HumanRootMotion.Evaluate(clip, new HumanClipMotionInput { Time = 0.99999f, PreviousTime = 0.9f, Speed = 1f }, previous, current, Present, out var atStop);
            Near(atStart.Body.t.x, atStop.Body.t.x, 1e-4f);
            Near(atStart.Body.t.y, atStop.Body.t.y, 1e-4f);
            Near(atStart.Body.t.z, atStop.Body.t.z, 1e-4f);
            Near(YawOf(atStart.Body.q), YawOf(atStop.Body.q), 1e-2f);
            // Without loop pose the baked yaw leaves the body turned by the clip's 30 degrees.
            var plain = Build(s => { s.LoopBlendOrientation = s.LoopBlendPositionY = true; return s; });
            HumanRootMotion.Evaluate(plain, new HumanClipMotionInput { Time = 0.99999f, PreviousTime = 0.9f, Speed = 1f }, previous, current, Present, out var unblended);
            Near(YawOf(atStart.Body.q) + 30f, YawOf(unblended.Body.q), 1e-2f);
        }

        [Fact]
        public void MirrorSwapsSidesAndFlipsLeftRight()
        {
            int I(string n) => HumanClipImport.CurveIndex(n);
            var m = HumanClipImport.MirrorCurve;
            // Mirroring twice gives the clip back.
            for (int i = 0; i < m.Length; i++)
            {
                int j = m[i] < 0 ? ~m[i] : m[i];
                Assert.Equal(m[i] < 0 ? ~i : i, m[j]);
            }
            Assert.Equal(~I("RootT.x"), m[I("RootT.x")]);
            Assert.Equal(I("RootT.y"), m[I("RootT.y")]);
            Assert.Equal(~I("RootQ.y"), m[I("RootQ.y")]);
            Assert.Equal(I("RootQ.w"), m[I("RootQ.w")]);
            Assert.Equal(~I("RightFootT.x"), m[I("LeftFootT.x")]);
            Assert.Equal(~I("LeftHandQ.z"), m[I("RightHandQ.z")]);
            Assert.Equal(I("RightHandQ.x"), m[I("LeftHandQ.x")]);
            Assert.Equal(~I("Spine Left-Right"), m[I("Spine Left-Right")]);
            Assert.Equal(~I("Jaw Left-Right"), m[I("Jaw Left-Right")]);
            Assert.Equal(I("Spine Front-Back"), m[I("Spine Front-Back")]);
            Assert.Equal(I("Right Arm Twist In-Out"), m[I("Left Arm Twist In-Out")]);
            Assert.Equal(I("Left Eye In-Out"), m[I("Right Eye In-Out")]);
            Assert.Equal(I("RightHand.Index.1 Stretched"), m[I("LeftHand.Index.1 Stretched")]);
        }

        [Fact]
        public void AMirroredClipPlaysItsCurvesOnTheOtherSide()
        {
            var clip = new AnimationClip();
            Add(clip, "Left Arm Down-Up", AnimationCurve.Linear(0f, 0.2f, 1f, -0.4f));
            Add(clip, "Head Turn Left-Right", AnimationCurve.Linear(0f, 0.3f, 1f, -0.2f));
            Add(clip, "RootT.y", AnimationCurve.Linear(0f, 1f, 1f, 1.1f));
            clip.m_Mirror = true;
            var curves = clip.HumanCurves;
            Assert.Null(curves[HumanClipImport.CurveIndex("Left Arm Down-Up")]);
            Near(-0.1f, curves[HumanClipImport.CurveIndex("Right Arm Down-Up")].Evaluate(0.5f));
            Near(-0.05f, curves[HumanClipImport.CurveIndex("Head Turn Left-Right")].Evaluate(0.5f));
            Near(1.05f, curves[HumanClipImport.CurveIndex("RootT.y")].Evaluate(0.5f));
        }

        [Fact]
        public void LoopPoseLeavesTheBodyCurvesToTheRootMotion()
        {
            var clip = new AnimationClip();
            Add(clip, "RootT.y", AnimationCurve.Linear(0f, 1f, 1f, 2f));
            Add(clip, "Spine Front-Back", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            clip.m_LoopTime = clip.m_LoopBlend = true;
            float phase = clip.LoopPosePhase(0.5f);
            int body = clip.m_Curves.FindIndex(c => c.Binding.Attribute == "RootT.y");
            int muscle = clip.m_Curves.FindIndex(c => c.Binding.Attribute == "Spine Front-Back");
            Near(1.5f, clip.LoopPose(body, 1.5f, phase));
            Near(0f, clip.LoopPose(muscle, 0.5f, phase));
        }
    }
}
