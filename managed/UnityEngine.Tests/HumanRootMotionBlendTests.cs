using System;
using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Blending humanoid root motion samples (transitions, blend trees, override layers).
    /// The values against the Editor are checked by tests/projects/humanoid.</summary>
    public class HumanRootMotionBlendTests
    {
        static F4 Yaw(float degrees)
        {
            float half = degrees * MathF.PI / 360f;
            return new F4(0f, MathF.Sin(half), 0f, MathF.Cos(half));
        }

        static Trs X(float x, float z, float yaw) => new Trs(new F4(x, 0f, z, 0f), Yaw(yaw), F4.One3);

        static HumanRootMotionSample Sample(float step, float yaw, float slip)
        {
            var foot = X(0.1f, 0f, 0f);
            return new HumanRootMotionSample
            {
                HasMotion = true,
                Delta = X(0f, step, yaw),
                Motion = X(1f, step, yaw),
                Body = X(0f, 0f, yaw),
                Root = X(1f, step, 2f * yaw),
                LeftFoot = foot,
                RightFoot = foot,
                LeftFootSlip = X(0f, slip, 0f),
                RightFootSlip = X(0f, 2f * slip, 0f),
                ClipDeltaTime = 0.1f,
                GravityWeight = 1f,
            };
        }

        static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-5f, expected + 1e-5f);

        [Fact]
        public void ClipsBlendByWeight()
        {
            var samples = new[] { Sample(1f, 0f, 0.5f), Sample(3f, 20f, 1.5f) };
            var r = HumanRootMotion.Blend(samples, new[] { 0.75f, 0.25f });
            Assert.True(r.HasMotion);
            Near(1.5f, r.Delta.t.z);
            Near(1.5f, r.Motion.t.z);
            // Rotations: the normalized weighted sum (about a quarter of the 20 degree turn).
            Assert.InRange(2f * MathF.Atan2(r.Delta.q.y, r.Delta.q.w) * 180f / MathF.PI, 4.9f, 5.1f);
            // The feet slips are the clips' slips blended.
            Near(0.75f, r.LeftFootSlip.t.z);
            Near(1.5f, r.RightFootSlip.t.z);
            Near(0.1f, r.ClipDeltaTime);
        }

        [Fact]
        public void AFullWeightClipIsKeptAsItIs()
        {
            var s = Sample(2f, 10f, 0.3f);
            var r = HumanRootMotion.Blend(new[] { s }, new[] { 1f });
            Assert.Equal(s.Delta.t.z, r.Delta.t.z);
            Assert.Equal(s.Delta.q.y, r.Delta.q.y);
            Assert.Equal(s.LeftFootSlip.t.z, r.LeftFootSlip.t.z);
        }

        [Fact]
        public void MissingWeightLeavesTheStepStill()
        {
            // A transition from a state without a motion: the clip moves the root by its weight.
            var r = HumanRootMotion.Blend(new[] { Sample(2f, 20f, 0f) }, new[] { 0.5f });
            Near(1f, r.Delta.t.z);
            Assert.InRange(2f * MathF.Atan2(r.Delta.q.y, r.Delta.q.w) * 180f / MathF.PI, 9f, 11f);
            // The body and the motion are the clip's own.
            Near(2f, r.Motion.t.z);
        }

        [Fact]
        public void OverrideLayersMoveTowardsTheirMotionByTheirWeight()
        {
            var below = Sample(1f, 0f, 0f);
            var layer = Sample(3f, 0f, 0f);
            Near(2f, HumanRootMotion.Override(below, layer, 0.5f).Delta.t.z);
            Assert.Equal(1f, HumanRootMotion.Override(below, layer, 0f).Delta.t.z);
            Assert.Equal(3f, HumanRootMotion.Override(below, layer, 1f).Delta.t.z);
            Assert.Equal(1f, HumanRootMotion.Override(below, default, 0.5f).Delta.t.z);
        }

        [Fact]
        public void APartialLayerOverNoMotionMovesByItsShare()
        {
            var layer = Sample(4f, 0f, 0f);
            var r = HumanRootMotion.Override(HumanRootMotion.NoMotion, layer, 0.25f);
            Assert.True(r.HasMotion);
            Near(1f, r.Delta.t.z);
            Assert.False(HumanRootMotion.Override(HumanRootMotion.NoMotion, layer, 0f).HasMotion);
            Assert.Equal(4f, HumanRootMotion.Override(HumanRootMotion.NoMotion, layer, 1f).Delta.t.z);
        }

        [Fact]
        public void APartialLayerOverNoMotionMovesTheRootByItsShareWithTheFeetPivot()
        {
            // A walk with planted feet (no slip): the root steps 0.05 a frame, the feet fall back as much.
            HumanRootMotionSample Walk(int frame)
            {
                var s = Sample(0.05f, 0f, 0f);
                s.LeftFoot = X(0.1f, -0.05f * frame, 0f);
                s.RightFoot = X(-0.1f, 0.2f - 0.05f * frame, 0f);
                s.LeftFootSlip = s.RightFootSlip = Trs.Identity;
                return s;
            }
            var memory = new HumanRootMotionMemory();
            var root = new Trs(F4.Zero, F4.QIdentity, F4.One3);
            for (int frame = 0; frame < 4; frame++)
            {
                var sample = HumanRootMotion.Override(HumanRootMotion.NoMotion, Walk(frame), 0.3f);
                HumanRootMotion.ApplyToAnimator(sample, root, 1f / 30f, 1f, 1f, memory, out var result);
                HumanRootMotion.EndUpdate(memory, 1f / 30f);
                Near(0.015f, result.DeltaPosition.z);
                Near(0f, result.DeltaPosition.x);
            }
        }

        [Fact]
        public void ThePoseBodyIsTheBlendedBodyPlusWhatTheValuesAdd()
        {
            var sample = Sample(1f, 0f, 0f);
            sample.Root = X(1f, 1f, 0f);
            sample.Body = X(0f, 0.5f, 0f);
            var pose = new Unbound.Runtime.Humanoid.HumanPose();
            pose.RootX = X(1f, 1f, 0f);
            HumanRootMotion.ApplyToPose(pose, sample);
            Assert.Equal(0.5f, pose.RootX.t.z);
            // An offset of the RootX values beyond the clips' moves the body by as much.
            pose.RootX = X(1.25f, 1f, 0f);
            HumanRootMotion.ApplyToPose(pose, sample);
            Near(0.25f, pose.RootX.t.x);
            Near(0.5f, pose.RootX.t.z);
        }

        [Fact]
        public void AnAdditiveBodyDifferenceScalesByTheLayerWeight()
        {
            var r = HumanRootMotion.AdditiveBody(new[] { X(0f, 1f, 20f) }, new[] { 1f }, 0.5f);
            Near(0.5f, r.t.z);
            // The rotation's vector part scales by the weight: 2·atan(0.5·tan 10°).
            float expected = 2f * MathF.Atan(0.5f * MathF.Tan(10f * MathF.PI / 180f)) * 180f / MathF.PI;
            Near(expected, 2f * MathF.Atan2(r.q.y, r.q.w) * 180f / MathF.PI);
            // Clips sum by their weights; a full layer of one clip is the clip's difference.
            r = HumanRootMotion.AdditiveBody(new[] { X(0f, 1f, 0f), X(0f, 3f, 0f) }, new[] { 0.75f, 0.25f }, 1f);
            Near(1.5f, r.t.z);
            r = HumanRootMotion.AdditiveBody(new[] { X(0f, 1f, 20f) }, new[] { 1f }, 1f);
            Near(20f, 2f * MathF.Atan2(r.q.y, r.q.w) * 180f / MathF.PI);
        }

        [Fact]
        public void AnAdditiveBodyDifferenceComposesInTheBodysSpace()
        {
            var sample = Sample(1f, 90f, 0f);
            var delta = sample.Delta;
            HumanRootMotion.AddBody(ref sample, X(0f, 1f, 30f));
            // Forward in the body's frame, turned 90 degrees: along x.
            Near(1f, sample.Body.t.x);
            Near(0f, sample.Body.t.z);
            Near(120f, 2f * MathF.Atan2(sample.Body.q.y, sample.Body.q.w) * 180f / MathF.PI);
            // The root motion stays.
            Assert.Equal(delta.t.z, sample.Delta.t.z);
        }
    }
}
