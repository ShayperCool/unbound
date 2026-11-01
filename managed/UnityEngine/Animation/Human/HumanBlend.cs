using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The parts of a human pose a layer or blend affects: the root (body), the 55 body
    /// DoFs, the four IK goals, the two hands' finger DoFs and the 21 translation DoFs.</summary>
    internal static class HumanMask
    {
        public const int Root = 0, DoFFirst = 1, GoalFirst = 56, LeftHand = 60, RightHand = 61, TDoFFirst = 62, Count = 83;

        static readonly bool[] s_Full = Full();

        static bool[] Full()
        {
            var m = new bool[Count];
            for (int i = 0; i < Count; i++) m[i] = true;
            return m;
        }

        /// <summary>Every part (a layer without an avatar mask).</summary>
        public static bool[] FullBody => s_Full;

        // AvatarMaskBodyPart: 0 Root, 1 Body, 2 Head, 3 LeftLeg, 4 RightLeg, 5 LeftArm, 6 RightArm,
        // 7 LeftFingers, 8 RightFingers, 9 LeftFootIK, 10 RightFootIK, 11 LeftHandIK, 12 RightHandIK.
        // The body DoFs are grouped by part in their order: body 0..8, head 9..20, left leg 21..28,
        // right leg 29..36, left arm 37..45, right arm 46..54.
        static readonly int[] DoFPartStart = { 0, 9, 21, 29, 37, 46, 55 };

        /// <summary>The body part of each translation DoF's bone.</summary>
        static readonly int[] TDoFPart = { 1, 1, 1, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6 };

        /// <summary>The parts an avatar mask's humanoid body parts select.</summary>
        public static bool[] FromBodyParts(Func<int, bool> active)
        {
            var m = new bool[Count];
            m[Root] = active(0);
            for (int part = 1; part <= 6; part++)
                for (int d = DoFPartStart[part - 1]; d < DoFPartStart[part]; d++) m[DoFFirst + d] = active(part);
            m[LeftHand] = active(7);
            m[RightHand] = active(8);
            for (int g = 0; g < 4; g++) m[GoalFirst + g] = active(9 + g);
            for (int t = 0; t < Bone.TDoFCount; t++) m[TDoFFirst + t] = active(TDoFPart[t]);
            return m;
        }
    }

    /// <summary>Blending and layering of human poses as the Animator's mixers do them: a blend sums
    /// the weighted poses (quaternions in the hemisphere of the running sum) and normalizes the
    /// rotations, the missing weight going to the identity; an override layer interpolates the masked
    /// parts towards the layer's pose; an additive layer adds the layer's weighted difference.</summary>
    internal static class HumanBlend
    {
        /// <summary>The cleared pose a layer mixer starts from: zero DoFs, identity transforms.</summary>
        public static void Clear(HumanPose p)
        {
            p.RootX = Trs.Identity;
            p.LookAtPosition = F4.Zero;
            p.LookAtWeight = F4.Zero;
            for (int g = 0; g < p.Goals.Length; g++) p.Goals[g] = new HumanGoal { X = Trs.Identity };
            Array.Clear(p.DoF);
            Array.Clear(p.LeftHand.DoF);
            Array.Clear(p.RightHand.DoF);
            Array.Clear(p.TDoF);
        }

        /// <summary>The start of a blend: every sum at zero, the scales one.</summary>
        public static void Begin(HumanPose p)
        {
            Clear(p);
            p.RootX.q = F4.Zero;
            for (int g = 0; g < p.Goals.Length; g++) p.Goals[g].X.q = F4.Zero;
        }

        /// <summary>A quaternion's weighted contribution, its sign following the running sum.</summary>
        static F4 AddQ(F4 sum, F4 q, float w)
        {
            float d = (sum.x * q.x + sum.y * q.y) + (sum.z * q.z + sum.w * q.w);
            var v = q * w;
            if (d < 0f || (d == 0f && float.IsNegative(d))) v = -v;
            return sum + v;
        }

        public static void Node(HumanPose sum, HumanPose p, float w)
        {
            for (int i = 0; i < Bone.DoFCount; i++) sum.DoF[i] += p.DoF[i] * w;
            for (int i = 0; i < Bone.HandDoFCount; i++)
            {
                sum.LeftHand.DoF[i] += p.LeftHand.DoF[i] * w;
                sum.RightHand.DoF[i] += p.RightHand.DoF[i] * w;
            }
            for (int g = 0; g < sum.Goals.Length; g++)
            {
                sum.Goals[g].X.t = sum.Goals[g].X.t + p.Goals[g].X.t * w;
                sum.Goals[g].X.q = AddQ(sum.Goals[g].X.q, p.Goals[g].X.q, w);
            }
            sum.RootX.t = sum.RootX.t + p.RootX.t * w;
            sum.RootX.q = AddQ(sum.RootX.q, p.RootX.q, w);
            for (int i = 0; i < Bone.TDoFCount; i++) sum.TDoF[i] = sum.TDoF[i] + p.TDoF[i] * w;
        }

        static F4 EndQ(F4 q, float rest) => M.QNormalize(new F4(q.x + 0f, q.y + 0f, q.z + 0f, q.w + rest));

        /// <summary>The end of a blend: the rotations normalized with the weight missing from one given
        /// to the identity.</summary>
        public static void End(HumanPose sum, float weight)
        {
            float rest = Math.Clamp(1f - weight, 0f, 1f);
            for (int g = 0; g < sum.Goals.Length; g++) sum.Goals[g].X.q = EndQ(sum.Goals[g].X.q, rest);
            sum.RootX.q = EndQ(sum.RootX.q, rest);
        }

        static F4 Lerp(F4 a, F4 b, float w) => (b - a) * w + a;

        static F4 NLerp(F4 a, F4 b, float w)
        {
            float d = (a.x * b.x + a.y * b.y) + (a.z * b.z + a.w * b.w);
            if (d < 0f || (d == 0f && float.IsNegative(d))) b = -b;
            return M.QNormalize((b - a) * w + a);
        }

        static Trs LerpX(in Trs a, in Trs b, float w) => new Trs(Lerp(a.t, b.t, w), NLerp(a.q, b.q, w), Lerp(a.s, b.s, w));

        /// <summary>An override layer: the masked parts of <paramref name="layer"/> replace those of
        /// <paramref name="pose"/> (weight one or more) or are interpolated towards them.</summary>
        public static void OverrideLayer(HumanPose pose, HumanPose layer, float w, bool[] mask)
        {
            if (!(0f < w)) return;
            bool full = w >= 1f;
            float k = 1f - w;
            for (int i = 0; i < Bone.DoFCount; i++)
                if (mask[HumanMask.DoFFirst + i]) pose.DoF[i] = full ? layer.DoF[i] : layer.DoF[i] * w + pose.DoF[i] * k;
            if (mask[HumanMask.LeftHand])
                for (int i = 0; i < Bone.HandDoFCount; i++)
                    pose.LeftHand.DoF[i] = full ? layer.LeftHand.DoF[i] : layer.LeftHand.DoF[i] * w + pose.LeftHand.DoF[i] * k;
            if (mask[HumanMask.RightHand])
                for (int i = 0; i < Bone.HandDoFCount; i++)
                    pose.RightHand.DoF[i] = full ? layer.RightHand.DoF[i] : layer.RightHand.DoF[i] * w + pose.RightHand.DoF[i] * k;
            for (int g = 0; g < pose.Goals.Length; g++)
                if (mask[HumanMask.GoalFirst + g])
                    pose.Goals[g].X = full ? layer.Goals[g].X : LerpX(pose.Goals[g].X, layer.Goals[g].X, w);
            if (mask[HumanMask.Root]) pose.RootX = full ? layer.RootX : LerpX(pose.RootX, layer.RootX, w);
            for (int i = 0; i < Bone.TDoFCount; i++)
                if (mask[HumanMask.TDoFFirst + i])
                    pose.TDoF[i] = full ? layer.TDoF[i] : layer.TDoF[i] * w + pose.TDoF[i] * k;
        }

        /// <summary>A transform added by an additive layer: the layer's rotation scaled in its vector
        /// part by the weight (normalized) after the pose's rotation, its translation scaled by the
        /// weight in the pose's frame.</summary>
        static Trs AddX(in Trs x, in Trs d, float w)
        {
            var q = M.QNormalize(new F4(w * d.q.x, d.q.y * w, d.q.z * w, d.q.w));
            var t = new F4(d.t.x * w * x.s.x, d.t.y * w * x.s.y, d.t.z * w * x.s.z, 0f);
            return new Trs(M.QRotate(x.q, t) + x.t, M.QMul(x.q, q), x.s);
        }

        /// <summary>An additive layer: the masked parts of the layer's difference pose, weighted, are
        /// added to <paramref name="pose"/>.</summary>
        public static void AdditiveLayer(HumanPose pose, HumanPose layer, float w, bool[] mask)
        {
            if (!(0f < w)) return;
            for (int i = 0; i < Bone.DoFCount; i++)
                if (mask[HumanMask.DoFFirst + i]) pose.DoF[i] = layer.DoF[i] * w + pose.DoF[i];
            if (mask[HumanMask.LeftHand])
                for (int i = 0; i < Bone.HandDoFCount; i++) pose.LeftHand.DoF[i] = layer.LeftHand.DoF[i] * w + pose.LeftHand.DoF[i];
            if (mask[HumanMask.RightHand])
                for (int i = 0; i < Bone.HandDoFCount; i++) pose.RightHand.DoF[i] = layer.RightHand.DoF[i] * w + pose.RightHand.DoF[i];
            for (int g = 0; g < pose.Goals.Length; g++)
                if (mask[HumanMask.GoalFirst + g]) pose.Goals[g].X = AddX(pose.Goals[g].X, layer.Goals[g].X, w);
            if (mask[HumanMask.Root]) pose.RootX = AddX(pose.RootX, layer.RootX, w);
            for (int i = 0; i < Bone.TDoFCount; i++)
                if (mask[HumanMask.TDoFFirst + i]) pose.TDoF[i] = layer.TDoF[i] * w + pose.TDoF[i];
        }

        /// <summary>The difference of two poses (an additive clip's pose less its reference pose): DoFs
        /// subtracted, the root and goals in the reference's frame.</summary>
        public static void Sub(HumanPose result, HumanPose a, HumanPose reference)
        {
            for (int g = 0; g < result.Goals.Length; g++)
            {
                var c = M.QConj(reference.Goals[g].X.q);
                result.Goals[g].X = new Trs(M.QRotate(c, a.Goals[g].X.t - reference.Goals[g].X.t), M.QMul(c, a.Goals[g].X.q), F4.One3);
            }
            var rc = M.QConj(reference.RootX.q);
            result.RootX = new Trs(M.QRotate(rc, a.RootX.t - reference.RootX.t), M.QMul(rc, a.RootX.q), F4.One3);
            for (int i = 0; i < Bone.DoFCount; i++) result.DoF[i] = a.DoF[i] - reference.DoF[i];
            for (int i = 0; i < Bone.HandDoFCount; i++)
            {
                result.LeftHand.DoF[i] = a.LeftHand.DoF[i] - reference.LeftHand.DoF[i];
                result.RightHand.DoF[i] = a.RightHand.DoF[i] - reference.RightHand.DoF[i];
            }
            for (int i = 0; i < Bone.TDoFCount; i++) result.TDoF[i] = a.TDoF[i] - reference.TDoF[i];
        }

        // ---------------------------------------------------------------- root motion

        /// <summary>A cleared motion: no motion, identity transforms.</summary>
        public static HumanRootMotionSample ClearMotion() => new HumanRootMotionSample
        {
            Delta = Trs.Identity, Motion = Trs.Identity, Body = Trs.Identity, LeftFoot = Trs.Identity, RightFoot = Trs.Identity,
            LeftFootSlip = Trs.Identity, RightFootSlip = Trs.Identity,
        };

        /// <summary>The start of a motion blend: zero sums.</summary>
        public static HumanRootMotionSample BeginMotion()
        {
            var z = new Trs(F4.Zero, F4.Zero, F4.One3);
            return new HumanRootMotionSample
            {
                Delta = z, Motion = z, Body = Trs.Identity, LeftFoot = z, RightFoot = z, LeftFootSlip = z, RightFootSlip = z,
            };
        }

        static Trs SumX(in Trs sum, in Trs x, float w) => new Trs(sum.t + x.t * w, AddQ(sum.q, x.q, w), sum.s);

        /// <summary>A clip's (or a state's) motion added to a blend: the delta and the motion with the
        /// root, the feet and their slips with the foot goals in <paramref name="mask"/>; the feet's
        /// speeds take the largest.</summary>
        public static void MotionNode(ref HumanRootMotionSample sum, in HumanRootMotionSample m, float w, bool[] mask)
        {
            sum.HasMotion |= m.HasMotion;
            if (mask[HumanMask.Root])
            {
                sum.Delta = SumX(sum.Delta, m.Delta, w);
                sum.Motion = SumX(sum.Motion, m.Motion, w);
            }
            if (mask[HumanMask.GoalFirst])
            {
                sum.LeftFoot = SumX(sum.LeftFoot, m.LeftFoot, w);
                sum.LeftFootSlip = SumX(sum.LeftFootSlip, m.LeftFootSlip, w);
                sum.LeftFootSpeedT = Math.Max(sum.LeftFootSpeedT, m.LeftFootSpeedT);
                sum.LeftFootSpeedQ = Math.Max(sum.LeftFootSpeedQ, m.LeftFootSpeedQ);
            }
            if (mask[HumanMask.GoalFirst + 1])
            {
                sum.RightFoot = SumX(sum.RightFoot, m.RightFoot, w);
                sum.RightFootSlip = SumX(sum.RightFootSlip, m.RightFootSlip, w);
                sum.RightFootSpeedT = Math.Max(sum.RightFootSpeedT, m.RightFootSpeedT);
                sum.RightFootSpeedQ = Math.Max(sum.RightFootSpeedQ, m.RightFootSpeedQ);
            }
            sum.GravityWeight += w * m.GravityWeight;
        }

        static Trs EndX(in Trs x, float rest) => new Trs(x.t, EndQ(x.q, rest), x.s);

        public static void EndMotion(ref HumanRootMotionSample sum, float weight, bool[] mask)
        {
            float rest = Math.Clamp(1f - weight, 0f, 1f);
            if (mask[HumanMask.Root])
            {
                sum.Delta = EndX(sum.Delta, rest);
                sum.Motion = EndX(sum.Motion, rest);
            }
            if (mask[HumanMask.GoalFirst])
            {
                sum.LeftFoot = EndX(sum.LeftFoot, rest);
                sum.LeftFootSlip = EndX(sum.LeftFootSlip, rest);
            }
            if (mask[HumanMask.GoalFirst + 1])
            {
                sum.RightFoot = EndX(sum.RightFoot, rest);
                sum.RightFootSlip = EndX(sum.RightFootSlip, rest);
            }
            if (0f < weight) sum.GravityWeight /= weight;
        }

        /// <summary>An override layer's motion: the masked parts (the root's delta and motion, each
        /// foot with its slip and speeds) replace or are interpolated towards the layer's.</summary>
        public static void MotionOverrideLayer(ref HumanRootMotionSample motion, in HumanRootMotionSample layer, float w, bool[] mask)
        {
            motion.HasMotion |= layer.HasMotion;
            bool full = w >= 1f;
            if (mask[HumanMask.Root])
            {
                motion.Delta = full ? layer.Delta : LerpX(motion.Delta, layer.Delta, w);
                motion.Motion = full ? layer.Motion : LerpX(motion.Motion, layer.Motion, w);
            }
            if (mask[HumanMask.GoalFirst])
            {
                motion.LeftFoot = full ? layer.LeftFoot : LerpX(motion.LeftFoot, layer.LeftFoot, w);
                motion.LeftFootSlip = full ? layer.LeftFootSlip : LerpX(motion.LeftFootSlip, layer.LeftFootSlip, w);
                motion.LeftFootSpeedT = full ? layer.LeftFootSpeedT : (layer.LeftFootSpeedT - motion.LeftFootSpeedT) * w + motion.LeftFootSpeedT;
                motion.LeftFootSpeedQ = full ? layer.LeftFootSpeedQ : (layer.LeftFootSpeedQ - motion.LeftFootSpeedQ) * w + motion.LeftFootSpeedQ;
            }
            if (mask[HumanMask.GoalFirst + 1])
            {
                motion.RightFoot = full ? layer.RightFoot : LerpX(motion.RightFoot, layer.RightFoot, w);
                motion.RightFootSlip = full ? layer.RightFootSlip : LerpX(motion.RightFootSlip, layer.RightFootSlip, w);
                motion.RightFootSpeedT = full ? layer.RightFootSpeedT : (layer.RightFootSpeedT - motion.RightFootSpeedT) * w + motion.RightFootSpeedT;
                motion.RightFootSpeedQ = full ? layer.RightFootSpeedQ : (layer.RightFootSpeedQ - motion.RightFootSpeedQ) * w + motion.RightFootSpeedQ;
            }
            motion.GravityWeight = full ? layer.GravityWeight : (layer.GravityWeight - motion.GravityWeight) * w + motion.GravityWeight;
        }
    }
}
