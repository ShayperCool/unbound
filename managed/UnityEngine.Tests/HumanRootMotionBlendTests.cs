using System;
using System.Linq;
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

        /// <summary>A layer over no humanoid clip moves every part from the identity by its weight,
        /// as a mixer's inputs over default values, and the feet pivot weight from none (humanoid
        /// emptyPartial at 0.5 and emptyPartialQuarter at 0.25).</summary>
        [Theory]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        public void ALayerOverNoHumanoidClipScalesEveryPartByItsWeight(float weight)
        {
            var layer = Sample(4f, 0f, 0.5f);
            layer.Body = X(0.2f, 0.6f, 0f);
            layer.LeftFoot = X(0.3f, 0.4f, 0f);
            var below = default(HumanRootMotionSample);
            bool any = false;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { layer }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, weight, null);
            Assert.True(any);
            Near(4f * weight, below.Delta.t.z);
            Near(0.2f * weight, below.Body.t.x);
            Near(0.6f * weight, below.Body.t.z);
            Near(0.3f * weight, below.LeftFoot.t.x);
            Near(0.5f * weight, below.LeftFootSlip.t.z);
            Near(weight, below.GravityWeight);
            // Control: over no motion elsewhere (a playable output over none, a layer over humanoid
            // clips without body curves) only the step takes the share; the body stays the layer's.
            var over = HumanRootMotion.Override(HumanRootMotion.NoMotion, layer, weight);
            Near(4f * weight, over.Delta.t.z);
            Near(0.2f, over.Body.t.x);
            Near(1f, over.GravityWeight);
        }

        /// <summary>A partial layer over humanoid clips without body curves is not measured: it warns.</summary>
        [Fact]
        public void APartialLayerOverHumanoidClipsWithoutBodyCurvesWarns()
        {
            const string member = "Animator humanoid root motion of a partial layer over humanoid clips without body curves";
            int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;
            int before = Hits();
            var below = default(HumanRootMotionSample);
            bool any = true;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0.5f, null);
            Assert.Equal(before + 1, Hits());
            // Over no humanoid clip at all it is measured: no warning.
            below = default;
            any = false;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0.5f, null);
            Assert.Equal(before + 1, Hits());
        }

        /// <summary>An update without a humanoid clip rests the feet pivot at the root and ends the
        /// first evaluation; the next clip's first update then moves the root by its feet pivot's
        /// way back to the root (humanoid emptyExit, emptyCond, emptyBack).</summary>
        [Fact]
        public void AnIdleUpdateRestsThePivotAtTheRoot()
        {
            var memory = new HumanRootMotionMemory();
            var root = new Trs(new F4(1f, 2f, 3f, 0f), F4.QIdentity, F4.One3);
            Assert.Equal(root.t.x, HumanRootMotion.Idle(memory, root, 0f).x);
            Assert.True(memory.FirstEvaluation);
            memory.PivotWeight = 0.3f;
            memory.Pivot = new F4(0.5f, 0f, 0.5f, 0f);
            var pivot = HumanRootMotion.Idle(memory, root, 1f / 30f);
            Assert.Equal(root.t.z, pivot.z);
            Assert.False(memory.FirstEvaluation);
            Assert.Equal(0.5f, memory.PivotWeight);
            Assert.Equal(0f, memory.Pivot.x);

            var walk = Sample(0.05f, 0f, 0f);
            walk.LeftFoot = X(0.1f, 0.3f, 0f);
            walk.RightFoot = X(-0.1f, 0.5f, 0f);
            walk.LeftFootSlip = walk.RightFootSlip = Trs.Identity;
            HumanRootMotion.ApplyToAnimator(walk, root, 1f / 30f, 1f, 1f, memory, out var result);
            // The step, less the feet pivot between where the feet were before it (0, 0.45).
            Near(0.05f - 0.45f, result.DeltaPosition.z);
            Near(0f, result.DeltaPosition.x);
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

        static bool[] Parts(params AvatarMaskBodyPart[] excluded)
        {
            var parts = new bool[(int)AvatarMaskBodyPart.LastBodyPart];
            for (int p = 0; p < parts.Length; p++) parts[p] = Array.IndexOf(excluded, (AvatarMaskBodyPart)p) < 0;
            return parts;
        }

        [Fact]
        public void LayersCombineOverrideThenAdditive()
        {
            var below = default(HumanRootMotionSample);
            bool any = false;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(1f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 1f, null);
            Assert.True(any);
            Near(1f, below.Delta.t.z);
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0.5f, null);
            Near(2f, below.Delta.t.z);
            // An additive layer composes its body difference, scaled by its weight, and leaves the motion.
            float bodyZ = below.Body.t.z;
            HumanRootMotion.CombineLayer(ref below, ref any, ReadOnlySpan<HumanRootMotionSample>.Empty, new[] { X(0f, 1f, 0f) },
                new[] { 1f }, true, 0.5f, null);
            Near(2f, below.Delta.t.z);
            Near(bodyZ + 0.5f, below.Body.t.z);
            // A layer of weight 0 changes nothing.
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(9f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0f, null);
            Near(2f, below.Delta.t.z);
        }

        [Fact]
        public void AnAdditiveBodyOverNoHumanoidClipIsLeftOut()
        {
            var below = default(HumanRootMotionSample);
            bool any = false;
            HumanRootMotion.CombineLayer(ref below, ref any, ReadOnlySpan<HumanRootMotionSample>.Empty, new[] { X(0f, 1f, 0f) },
                new[] { 1f }, true, 1f, null);
            Assert.False(any);
            Assert.False(below.HasMotion);
        }

        [Fact]
        public void ALayerMaskWithoutRootAddsNoBodyOrMotion()
        {
            var below = Sample(1f, 0f, 0f);
            bool any = true;
            var noRoot = Parts(AvatarMaskBodyPart.Root);
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 20f, 0.5f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0.5f, noRoot);
            Assert.Equal(1f, below.Delta.t.z);
            Assert.Equal(0f, below.LeftFootSlip.t.z);
            HumanRootMotion.CombineLayer(ref below, ref any, ReadOnlySpan<HumanRootMotionSample>.Empty, new[] { X(0f, 1f, 30f) },
                new[] { 1f }, true, 1f, noRoot);
            Assert.Equal(0f, below.Body.t.z);
        }

        [Fact]
        public void ALayerMaskWithoutRootOverNoHumanoidClipWarns()
        {
            const string member = "Animator humanoid layer mask without Root over no humanoid clip";
            int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;
            int before = Hits();
            var below = default(HumanRootMotionSample);
            bool any = false;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 1f,
                Parts(AvatarMaskBodyPart.Root));
            Assert.False(any);
            Assert.False(below.HasMotion);
            Assert.Equal(before + 1, Hits());
            // Over a humanoid clip below it is measured: no warning.
            below = Sample(1f, 0f, 0f);
            any = true;
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { Sample(3f, 0f, 0f) }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 1f,
                Parts(AvatarMaskBodyPart.Root));
            Assert.Equal(before + 1, Hits());
        }

        [Fact]
        public void AnOverrideLayerMaskWithoutFootIKKeepsTheFeetBelow()
        {
            var below = Sample(1f, 0f, 0f);
            bool any = true;
            var layer = Sample(3f, 0f, 0.5f);
            layer.LeftFoot = layer.RightFoot = X(0.3f, 1f, 0f);
            HumanRootMotion.CombineLayer(ref below, ref any, new[] { layer }, ReadOnlySpan<Trs>.Empty, new[] { 1f }, false, 0.5f,
                Parts(AvatarMaskBodyPart.LeftFootIK));
            // The step blends; the left foot stays the one below, the right one blends.
            Near(2f, below.Delta.t.z);
            Near(0.1f, below.LeftFoot.t.x);
            Near(0f, below.LeftFootSlip.t.z);
            Near(0.2f, below.RightFoot.t.x);
            Near(0.5f, below.RightFootSlip.t.z);
        }

        [Fact]
        public void MaskBodyPartsOfHumanoidCurves()
        {
            int Part(string curve) => HumanClipImport.BodyPart(HumanClipImport.CurveIndex(curve));
            Assert.Equal((int)AvatarMaskBodyPart.Root, Part("MotionT.x"));
            Assert.Equal((int)AvatarMaskBodyPart.Root, Part("RootQ.w"));
            Assert.Equal((int)AvatarMaskBodyPart.LeftFootIK, Part("LeftFootT.x"));
            Assert.Equal((int)AvatarMaskBodyPart.RightHandIK, Part("RightHandQ.w"));
            Assert.Equal((int)AvatarMaskBodyPart.Body, Part("UpperChest Twist Left-Right"));
            Assert.Equal((int)AvatarMaskBodyPart.Head, Part("Jaw Close"));
            Assert.Equal((int)AvatarMaskBodyPart.Head, Part("Left Eye In-Out"));
            Assert.Equal((int)AvatarMaskBodyPart.LeftLeg, Part("Left Toes Up-Down"));
            Assert.Equal((int)AvatarMaskBodyPart.RightLeg, Part("Right Upper Leg In-Out"));
            Assert.Equal((int)AvatarMaskBodyPart.LeftArm, Part("Left Shoulder Down-Up"));
            Assert.Equal((int)AvatarMaskBodyPart.RightArm, Part("Right Hand In-Out"));
            Assert.Equal((int)AvatarMaskBodyPart.LeftFingers, Part("LeftHand.Little.3 Stretched"));
            Assert.Equal((int)AvatarMaskBodyPart.RightFingers, Part("RightHand.Thumb.1 Stretched"));
            Assert.Equal((int)AvatarMaskBodyPart.Body, Part("SpineTDOF.y"));
            Assert.Equal((int)AvatarMaskBodyPart.RightArm, Part("RightHandTDOF.z"));
            Assert.Equal((int)AvatarMaskBodyPart.LeftLeg, Part("LeftToesTDOF.x"));
            for (int c = 0; c < HumanClipImport.CurveCount; c++) Assert.InRange(HumanClipImport.BodyPart(c), 0, 12);
        }
    }
}
