using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>Human and hand pose operations: avatar setup (masses, muscle axes, body frame), muscle
    /// space conversions and retargeting between a skeleton pose and a human pose, matching the Unity
    /// 6000.0.71f1 Editor's results (tests/projects/humanoid). Each pose parameter says whether it is
    /// local or global and whether it is read, written or a workspace.</summary>
    internal static class HumanOps
    {
        // Degrees per radian.
        static readonly float Rad2Deg = 57.2957802f;

        // The end bone of each goal (left foot, right foot, left hand, right hand).
        static readonly int[] GoalBone = { Bone.LeftFoot, Bone.RightFoot, Bone.LeftHand, Bone.RightHand };

        // The bone each translation DoF moves.
        static readonly int[] TDoFBone = { 7, 8, 9, 10, 11, 1, 3, 5, 20, 2, 4, 6, 21, 12, 14, 16, 18, 13, 15, 17, 19 };

        // Each human bone's parent bone.
        static readonly int[] BoneParent = { -1, 0, 0, 1, 2, 3, 4, 0, 7, 8, 9, 10, 9, 9, 12, 13, 14, 15, 16, 17, 5, 6, 11, 11, 11 };

        // RetargetFrom: the bones that take the source's global rotation.
        static readonly int[] SourceRotationBones = { Bone.LeftHand, Bone.RightHand, Bone.LeftFoot, Bone.RightFoot, Bone.Head };

        static Skel.SetupAxesInfo Info(F4 pre, F4 main, F4 min, F4 max, F4 sgn, int type, int force) =>
            new Skel.SetupAxesInfo(pre, main, min, max, sgn, type, force);

        static F4 V(float x, float y, float z, float w) => new F4(x, y, z, w);

        /// <summary>The body bones' default muscle frames.</summary>
        internal static readonly Skel.SetupAxesInfo[] BodyInfo =
        {
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-40, -40, -40, 0), V(40, 40, 40, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(-0.268f, 0, 0, 1), V(1, 0, 0, 0), V(-60, -60, -90, 0), V(60, 60, 50, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(-0.268f, 0, 0, 1), V(1, 0, 0, 0), V(-60, -60, -90, 0), V(60, 60, 50, 0), V(-1, -1, 1, 1), 1, 0),
            Info(V(0.839f, 0, 0, 1), V(1, 0, 0, 0), V(-90, 0, -80, 0), V(90, 0, 80, 0), V(1, 1, -1, 1), 1, 0),
            Info(V(0.839f, 0, 0, 1), V(1, 0, 0, 0), V(-90, 0, -80, 0), V(90, 0, 80, 0), V(-1, 1, -1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, -30, -50, 0), V(0, 30, 50, 0), V(1, 1, 1, 1), 1, -2),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, -30, -50, 0), V(0, 30, 50, 0), V(1, -1, 1, 1), 1, -2),
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-40, -40, -40, 0), V(40, 40, 40, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-40, -40, -40, 0), V(40, 40, 40, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-20, -20, -20, 0), V(20, 20, 20, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-40, -40, -40, 0), V(40, 40, 40, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(-1, 0, 0, 0), V(-40, -40, -40, 0), V(40, 40, 40, 0), V(1, 1, 1, 1), 1, 2),
            Info(V(0, 0, 0, 1), V(0, 0, 1, 0), V(0, -15, -15, 0), V(0, 15, 30, 0), V(1, 1, -1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(0, 0, 1, 0), V(0, -15, -15, 0), V(0, 15, 30, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0.268f, 0.364f, 1), V(0, 0, 1, 0), V(-90, -100, -60, 0), V(90, 100, 100, 0), V(1, 1, -1, 1), 1, 0),
            Info(V(0, -0.268f, -0.364f, 1), V(0, 0, 1, 0), V(-90, -100, -60, 0), V(90, 100, 100, 0), V(-1, 1, 1, 1), 1, 0),
            Info(V(0, 0.839f, 0, 1), V(0, 1, 0, 0), V(-90, 0, -80, 0), V(90, 0, 80, 0), V(1, 1, -1, 1), 1, 0),
            Info(V(0, -0.839f, 0, 1), V(0, 1, 0, 0), V(-90, 0, -80, 0), V(90, 0, 80, 0), V(-1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(0, 0, 1, 0), V(0, -40, -80, 0), V(0, 40, 80, 0), V(1, 1, -1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(0, 0, 1, 0), V(0, -40, -80, 0), V(0, 40, 80, 0), V(1, 1, 1, 1), 1, 0),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, 0, -50, 0), V(0, 0, 50, 0), V(1, 1, 1, 1), 1, 3),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, 0, -50, 0), V(0, 0, 50, 0), V(1, 1, 1, 1), 1, 3),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, -20, -10, 0), V(0, 20, 15, 0), V(1, 1, -1, 1), 1, 3),
            Info(V(0, 0, 0, 1), V(1, 0, 0, 0), V(0, -20, -10, 0), V(0, 20, 15, 0), V(1, -1, -1, 1), 1, 3),
            Info(V(0.09f, 0, 0, 1), V(1, 0, 0, 0), V(0, -10, -10, 0), V(0, 10, 10, 0), V(1, 1, -1, 1), 1, 3),
        };

        /// <summary>The finger bones' default muscle frames, finger * 3 + phalanx.</summary>
        internal static readonly Skel.SetupAxesInfo[] FingerInfo =
        {
            Info(V(0, 0.125f, 0.125f, 1), V(0, 1, 0, 0), V(0, -25, -20, 0), V(0, 25, 20, 0), V(-1, -1, 1, -1), 1, 0),
            Info(V(0, -0.2f, 0, 1), V(0, 1, 0, 0), V(0, 0, -40, 0), V(0, 0, 35, 0), V(-1, 1, 1, -1), 1, 0),
            Info(V(0, -0.2f, 0, 1), V(0, 1, 0, 0), V(0, 0, -40, 0), V(0, 0, 35, 0), V(-1, 1, 1, -1), 1, 0),
            Info(V(0, 0.08f, 0.3f, 1), V(0, 0, 1, 0), V(0, -20, -50, 0), V(0, 20, 50, 0), V(-1, -1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0.04f, 0.3f, 1), V(0, 0, 1, 0), V(0, -7.5f, -50, 0), V(0, 7.5f, 50, 0), V(-1, -1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, -0.04f, 0.3f, 1), V(0, 0, 1, 0), V(0, -7.5f, -50, 0), V(0, 7.5f, 50, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, -0.08f, 0.3f, 1), V(0, 0, 1, 0), V(0, -20, -50, 0), V(0, 20, 50, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
            Info(V(0, 0, 0.33f, 1), V(0, 0, 1, 0), V(0, 0, -45, 0), V(0, 0, 45, 0), V(-1, 1, -1, -1), 1, 0),
        };

        // ---------------------------------------------------------------- setup

        /// <summary>The default bone masses: the relative masses divided by their sum (82.49999), the
        /// hips' 12 / 82.5 as the Editor stores it.</summary>
        internal static void DefaultMass(float[] dest)
        {
            float sum = 82.4999924f;
            float[] table = { 12f, 10f, 10f, 4f, 4f, 0.8f, 0.8f, 2.5f, 12f, 12f, 1f, 4f, 0.5f, 0.5f, 2f, 2f, 1.5f, 1.5f, 0.5f, 0.5f, 0.2f, 0.2f, 0f, 0f, 0f };
            dest[0] = 0.145454556f;
            for (int i = 1; i < Bone.Last; i++) dest[i] = table[i] / sum;
        }

        /// <summary>Moves the masses of missing bones to their parents.</summary>
        internal static void AdjustMass(Human h)
        {
            var b = h.BoneIndex;
            var m = h.BoneMass;
            if (b[Bone.Neck] < 0) { m[Bone.UpperChest] = m[Bone.Neck] + m[Bone.UpperChest]; m[Bone.Neck] = 0f; }
            if (b[Bone.LeftShoulder] < 0) { m[Bone.UpperChest] = m[Bone.LeftShoulder] + m[Bone.UpperChest]; m[Bone.LeftShoulder] = 0f; }
            if (b[Bone.RightShoulder] < 0) { m[Bone.UpperChest] = m[Bone.RightShoulder] + m[Bone.UpperChest]; m[Bone.RightShoulder] = 0f; }
            if (b[Bone.UpperChest] < 0) { m[Bone.Chest] = m[Bone.UpperChest] + m[Bone.Chest]; m[Bone.UpperChest] = 0f; }
            if (b[Bone.Chest] < 0) { m[Bone.Spine] = m[Bone.Chest] + m[Bone.Spine]; m[Bone.Chest] = 0f; }
            if (b[Bone.LeftToes] < 0) { m[Bone.LeftFoot] = m[Bone.LeftToes] + m[Bone.LeftFoot]; m[Bone.LeftToes] = 0f; }
            if (b[Bone.RightToes] < 0) { m[Bone.RightFoot] = m[Bone.RightToes] + m[Bone.RightFoot]; m[Bone.RightToes] = 0f; }
        }

        static F4 Half(F4 a, F4 b) => new F4((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, (a.z + b.z) * 0.5f, 0f);

        static F4 Third(F4 a, F4 b, F4 c) =>
            new F4((a.x + b.x + c.x) * 0.333333343f, (a.y + b.y + c.y) * 0.333333343f, (a.z + b.z + c.z) * 0.333333343f, 0f);

        static F4 Quarter(F4 a, F4 b, F4 c, F4 d) =>
            new F4((a.x + b.x + c.x + d.x) * 0.25f, (a.y + b.y + c.y + d.y) * 0.25f, (a.z + b.z + c.z + d.z) * 0.25f, 0f);

        /// <summary>Where a bone's mass sits in a global pose
        /// (mostly the middle of the bone and its child). Lower left arm averages the upper arm and the
        /// hand, as the Editor computes it.</summary>
        internal static F4 ComputeBoneMassCenter(Human h, SkeletonPose global, int bone)
        {
            var x = global.X;
            var b = h.BoneIndex;
            F4 T(int bn) => x[b[bn]].t;
            // Chest (without upper chest) and upper chest: with the neck and shoulders, or the arms.
            F4 Torso(int self) => b[Bone.Neck] > -1 && b[Bone.LeftShoulder] > -1 && b[Bone.RightShoulder] > -1
                ? Quarter(T(self), T(Bone.Neck), T(Bone.LeftShoulder), T(Bone.RightShoulder))
                : Third(T(self), T(Bone.LeftUpperArm), T(Bone.RightUpperArm));
            switch (bone)
            {
                case Bone.Hips: return Third(T(Bone.LeftUpperLeg), T(Bone.RightUpperLeg), T(Bone.Spine));
                case Bone.LeftUpperLeg: return Half(T(Bone.LeftUpperLeg), T(Bone.LeftLowerLeg));
                case Bone.RightUpperLeg: return Half(T(Bone.RightUpperLeg), T(Bone.RightLowerLeg));
                case Bone.LeftLowerLeg: return Half(T(Bone.LeftLowerLeg), T(Bone.LeftFoot));
                case Bone.RightLowerLeg: return Half(T(Bone.RightLowerLeg), T(Bone.RightFoot));
                case Bone.Spine:
                    if (b[Bone.Chest] < 0)
                    {
                        F4 s = T(Bone.Spine), l = T(Bone.LeftUpperArm), r = T(Bone.RightUpperArm);
                        return new F4(s.x * 0.1f + (l.x + r.x) * 0.45f, s.y * 0.1f + (l.y + r.y) * 0.45f, s.z * 0.1f + (l.z + r.z) * 0.45f, 0f);
                    }
                    return Half(T(Bone.Spine), T(Bone.Chest));
                case Bone.Chest:
                    return b[Bone.UpperChest] > -1 ? Half(T(Bone.Chest), T(Bone.UpperChest)) : Torso(Bone.Chest);
                case Bone.UpperChest: return Torso(Bone.UpperChest);
                case Bone.Neck: return Half(T(Bone.Neck), T(Bone.Head));
                case Bone.LeftShoulder: return Half(T(Bone.LeftShoulder), T(Bone.LeftUpperArm));
                case Bone.RightShoulder: return Half(T(Bone.RightShoulder), T(Bone.RightUpperArm));
                case Bone.LeftUpperArm: return Half(T(Bone.LeftUpperArm), T(Bone.LeftLowerArm));
                case Bone.RightUpperArm: return Half(T(Bone.RightUpperArm), T(Bone.RightLowerArm));
                case Bone.LeftLowerArm: return Half(T(Bone.LeftUpperArm), T(Bone.LeftHand));
                case Bone.RightLowerArm: return Half(T(Bone.RightLowerArm), T(Bone.RightHand));
                default: return x[b[bone]].t;
            }
        }

        /// <summary>The mass-weighted center of the existing bones in a global pose (the loop shared by
        /// SetupAxes, RetargetFrom and RetargetTo). The w component is 0.</summary>
        static F4 MassCenter(Human h, SkeletonPose global)
        {
            float total = 0f, ax = 0f, ay = 0f, az = 0f;
            for (int b = 0; b < Bone.Last; b++)
            {
                if (h.BoneIndex[b] <= -1) continue;
                float m = h.BoneMass[b];
                var c = ComputeBoneMassCenter(h, global, b);
                total = total + m;
                ax = ax + m * c.x;
                ay = ay + m * c.y;
                az = az + m * c.z;
            }
            return new F4(ax / total, ay / total, az / total, 0f);
        }

        /// <summary>The body frame of a global pose (up from the
        /// legs to the arms, right across both), relative to the human's root rotation.</summary>
        internal static F4 ComputeOrientation(Human h, SkeletonPose global)
        {
            var x = global.X;
            var b = h.BoneIndex;
            F4 lul = x[b[Bone.LeftUpperLeg]].t, rul = x[b[Bone.RightUpperLeg]].t;
            F4 lua = x[b[Bone.LeftUpperArm]].t, rua = x[b[Bone.RightUpperArm]].t;
            var up = M.QNormalize(new F4(
                (lua.x + rua.x) * 0.5f - (lul.x + rul.x) * 0.5f,
                (lua.y + rua.y) * 0.5f - (lul.y + rul.y) * 0.5f,
                (lua.z + rua.z) * 0.5f - (lul.z + rul.z) * 0.5f, 0f));
            var right = M.QNormalize(new F4(
                (rua.x - lua.x) + (rul.x - lul.x),
                (rua.y - lua.y) + (rul.y - lul.y),
                (rua.z - lua.z) + (rul.z - lul.z), 0f));
            var fwd = M.Cross(right, up);
            var side = M.Cross(up, fwd);
            var q = M.QFromColumns(side, up, fwd);
            return M.QNormalize(M.QMul(q, M.QConj(h.RootX.q)));
        }

        /// <summary>The human's root (mass center, body frame), scale (the
        /// center's height), local T-pose and every body bone's muscle axes from the global T-pose.</summary>
        /// <param name="globalTPose">Input: the T-pose, global.</param>
        internal static void SetupAxes(Human h, SkeletonPose globalTPose)
        {
            h.RootX = Trs.Identity;
            var center = MassCenter(h, globalTPose);
            var q = ComputeOrientation(h, globalTPose);
            h.RootX = new Trs(center, q, F4.One3);
            h.Scale = center.y;
            var s = h.Skeleton;
            Skel.ComputeLocal(s, globalTPose, h.SkeletonPose);
            var b = h.BoneIndex;
            for (int bone = 0; bone < Bone.Last; bone++)
            {
                int node = b[bone];
                int axis = -1;
                float len = 1f;
                switch (bone)
                {
                    case Bone.Hips: axis = b[Bone.Spine]; break;
                    case Bone.LeftUpperLeg: axis = b[Bone.LeftLowerLeg]; break;
                    case Bone.RightUpperLeg: axis = b[Bone.RightLowerLeg]; break;
                    case Bone.LeftLowerLeg: axis = b[Bone.LeftFoot]; break;
                    case Bone.RightLowerLeg: axis = b[Bone.RightFoot]; break;
                    case Bone.LeftFoot:
                    case Bone.RightFoot:
                        if (node > -1) len = -globalTPose.X[node].t.y;
                        break;
                    case Bone.Spine: axis = FirstNode(b, Bone.Chest, Bone.UpperChest, Bone.Neck, Bone.Head); break;
                    case Bone.Chest: axis = FirstNode(b, Bone.UpperChest, Bone.Neck, Bone.Head); break;
                    case Bone.UpperChest: axis = FirstNode(b, Bone.Neck, Bone.Head); break;
                    case Bone.Neck: axis = b[Bone.Head]; break;
                    case Bone.Head:
                        if (b[Bone.Neck] > -1) { axis = b[Bone.Neck]; len = -1f; }
                        else if (b[Bone.Chest] > -1) { axis = b[Bone.Chest]; len = -0.5f; }
                        else { axis = b[Bone.Spine]; len = -0.25f; }
                        break;
                    case Bone.LeftShoulder: axis = b[Bone.LeftUpperArm]; break;
                    case Bone.RightShoulder: axis = b[Bone.RightUpperArm]; break;
                    case Bone.LeftUpperArm: axis = b[Bone.LeftLowerArm]; break;
                    case Bone.RightUpperArm: axis = b[Bone.RightLowerArm]; break;
                    case Bone.LeftLowerArm: axis = b[Bone.LeftHand]; break;
                    case Bone.RightLowerArm: axis = b[Bone.RightHand]; break;
                    case Bone.LeftHand: axis = b[Bone.LeftLowerArm]; len = -0.5f; break;
                    case Bone.RightHand: axis = b[Bone.RightLowerArm]; len = -0.5f; break;
                    case Bone.LeftToes: axis = b[Bone.LeftFoot]; len = 0.5f; break;
                    case Bone.RightToes: axis = b[Bone.RightFoot]; len = 0.5f; break;
                    default: len = 0.1f; break; // eyes, jaw
                }
                if (node > -1) Skel.SetupAxes(s, globalTPose, BodyInfo[bone], node, axis, true, len);
            }
        }

        static int FirstNode(int[] boneIndex, params int[] bones)
        {
            foreach (var bone in bones)
                if (boneIndex[bone] >= 0) return boneIndex[bone];
            return boneIndex[bones[bones.Length - 1]];
        }

        /// <summary>Every finger bone's muscle axes, each phalanx
        /// pointing at the next (the last one away from the previous, length 0.75).</summary>
        /// <param name="global">Input: the T-pose, global.</param>
        internal static void HandSetupAxes(Hand hand, SkeletonPose global, Skeleton s, bool left)
        {
            var bi = hand.BoneIndex;
            for (int f = 0; f < 5; f++)
            {
                int p = bi[3 * f], m = bi[3 * f + 1], d = bi[3 * f + 2];
                if (p > -1) Skel.SetupAxes(s, global, FingerInfo[3 * f], p, m < -1 ? -1 : m, left, 1f);
                int axis = d;
                float len = 1f;
                if (d < 0)
                {
                    axis = p < -1 ? -1 : p;
                    len = p > -1 ? -0.75f : 1f;
                }
                if (m > -1) Skel.SetupAxes(s, global, FingerInfo[3 * f + 1], m, axis, left, len);
                if (d > -1) Skel.SetupAxes(s, global, FingerInfo[3 * f + 2], d, m < -1 ? -1 : m, left, m > -1 ? -0.75f : 1f);
            }
        }

        // ---------------------------------------------------------------- muscle space

        // Whether a body bone has an x (twist) and a y muscle.
        static bool HasX(int bone) => bone > 0 && bone < Bone.Last && Bone.DoF[bone, 2] != -1;

        static bool HasY(int bone) => bone > 0 && bone < Bone.Last && Bone.DoF[bone, 1] != -1;

        /// <summary>Identity root and goal transforms, zero look-at, body,
        /// finger and translation DoFs (goal weights and hand grab/override values stay).</summary>
        internal static void HumanPoseClear(HumanPose pose)
        {
            pose.RootX = Trs.Identity;
            pose.LookAtPosition = F4.Zero;
            pose.LookAtWeight = F4.Zero;
            for (int i = 0; i < pose.Goals.Length; i++) pose.Goals[i].X = Trs.Identity;
            Array.Clear(pose.DoF);
            Array.Clear(pose.LeftHand.DoF);
            Array.Clear(pose.RightHand.DoF);
            Array.Clear(pose.TDoF);
        }

        /// <summary>The body and finger muscle values of a skeleton
        /// pose.</summary>
        /// <param name="local">Input: the human skeleton pose, local.</param>
        /// <param name="pose">Output: its DoF and hand DoF arrays (only those of existing bones).</param>
        internal static void Skeleton2HumanPose(Human h, SkeletonPose local, HumanPose pose)
        {
            var s = h.Skeleton;
            for (int bone = 1; bone < Bone.Last; bone++)
            {
                int node = h.BoneIndex[bone];
                if (node == -1) continue;
                var v = Skel.GetDoF(s, local, node);
                if (HasX(bone)) pose.DoF[Bone.DoF[bone, 2]] = v.x;
                if (HasY(bone)) pose.DoF[Bone.DoF[bone, 1]] = v.y;
                pose.DoF[Bone.DoF[bone, 0]] = v.z;
            }
            if (h.HasLeftHand) Skeleton2HandPose(h.LeftHand, s, local, pose.LeftHand.DoF);
            if (h.HasRightHand) Skeleton2HandPose(h.RightHand, s, local, pose.RightHand.DoF);
        }

        /// <summary>The finger part of Skeleton2HumanPose: per finger proximal stretch (z) and spread (y),
        /// intermediate and distal stretch (all + 0, which turns -0 into +0).</summary>
        static void Skeleton2HandPose(Hand hand, Skeleton s, SkeletonPose local, float[] dof)
        {
            var bi = hand.BoneIndex;
            for (int f = 0; f < 5; f++)
            {
                if (bi[3 * f] > -1)
                {
                    var v = Skel.GetDoF(s, local, bi[3 * f]);
                    dof[4 * f + 1] = v.y + 0f;
                    dof[4 * f] = v.z + 0f;
                }
                if (bi[3 * f + 1] > -1) dof[4 * f + 2] = Skel.GetDoF(s, local, bi[3 * f + 1]).z + 0f;
                if (bi[3 * f + 2] > -1) dof[4 * f + 3] = Skel.GetDoF(s, local, bi[3 * f + 2]).z + 0f;
            }
        }

        /// <summary>Sets the rotations of the existing body and finger
        /// bones from a human pose's muscle values.</summary>
        /// <param name="local">Output: the human skeleton pose, local (only bone rotations change).</param>
        internal static void Human2SkeletonPose(Human h, HumanPose pose, SkeletonPose local)
        {
            var s = h.Skeleton;
            for (int bone = 1; bone < Bone.Last; bone++)
            {
                int node = h.BoneIndex[bone];
                if (node == -1) continue;
                var dof = new F4(
                    HasX(bone) ? pose.DoF[Bone.DoF[bone, 2]] : 0f,
                    HasY(bone) ? pose.DoF[Bone.DoF[bone, 1]] : 0f,
                    pose.DoF[Bone.DoF[bone, 0]], 0f);
                Skel.SetDoF(s, local, dof, node);
            }
            if (h.HasLeftHand) Hand2SkeletonPose(h.LeftHand, s, pose.LeftHand, local);
            if (h.HasRightHand) Hand2SkeletonPose(h.RightHand, s, pose.RightHand, local);
        }

        /// <summary>Sets the finger bones' rotations.</summary>
        /// <param name="local">Output: the skeleton pose, local.</param>
        internal static void Hand2SkeletonPose(Hand hand, Skeleton s, HandPose pose, SkeletonPose local)
        {
            var bi = hand.BoneIndex;
            var d = pose.DoF;
            for (int f = 0; f < 5; f++)
            {
                if (bi[3 * f] > -1) Skel.SetDoF(s, local, new F4(0f, d[4 * f + 1], d[4 * f], 0f), bi[3 * f]);
                if (bi[3 * f + 1] > -1) Skel.SetDoF(s, local, new F4(0f, 0f, d[4 * f + 2], 0f), bi[3 * f + 1]);
                if (bi[3 * f + 2] > -1) Skel.SetDoF(s, local, new F4(0f, 0f, d[4 * f + 3], 0f), bi[3 * f + 2]);
            }
        }

        /// <summary>Moves the muscle values of missing
        /// neck, upper chest, chest and shoulders onto the bones that exist.</summary>
        internal static void AdjustForMissingBones(Human h, HumanPose pose)
        {
            var b = h.BoneIndex;
            var d = pose.DoF;
            if (b[Bone.Neck] < 0)
            {
                d[12] = d[9] + d[12]; d[9] = 0f;
                d[13] = d[10] + d[13];
                d[14] = d[11] + d[14]; d[10] = 0f; d[11] = 0f;
            }
            if (b[Bone.UpperChest] < 0)
            {
                d[3] = d[6] * 0.5f + d[3]; d[6] = 0f;
                d[4] = d[7] * 0.5f + d[4];
                d[5] = 0.5f * d[8] + d[5]; d[7] = 0f; d[8] = 0f;
            }
            if (b[Bone.Chest] < 0)
            {
                d[0] = d[3] + d[0]; d[3] = 0f;
                d[1] = d[4] + d[1];
                d[2] = d[5] + d[2]; d[4] = 0f; d[5] = 0f;
            }
            if (b[Bone.LeftShoulder] < 0)
            {
                d[39] = d[37] * 0.15f + d[39];
                d[40] = d[38] * 0.28125f + d[40]; d[37] = 0f; d[38] = 0f;
            }
            if (b[Bone.RightShoulder] < 0)
            {
                d[48] = d[46] * 0.15f + d[48];
                d[49] = d[47] * 0.28125f + d[49]; d[46] = 0f; d[47] = 0f;
            }
        }

        // ---------------------------------------------------------------- RetargetFrom

        static float Dot3(F4 a, F4 b) => (0f + a.z * b.z) + (a.y * b.y + a.x * b.x);

        /// <summary>Q aligned with the
        /// reference along the node's axis, unchanged when the node has no axes.</summary>
        static F4 AlignQ(Skeleton s, F4 refQ, F4 q, int index)
        {
            int id = s.AxesId[index];
            return id == -1 ? q : Skel.Align(refQ, q, s.AxesArray[id].PostQ);
        }

        static Axes AxesOf(Skeleton s, int node, string what)
        {
            int id = s.AxesId[node];
            if (id == -1)
                throw new InvalidOperationException($"humanoid retargeting: the {what} node {node} has no muscle axes");
            return s.AxesArray[id];
        }

        static int NodeOf(Human h, int bone)
        {
            int node = h.BoneIndex[bone];
            if (node == -1)
                throw new InvalidOperationException($"humanoid retargeting needs human bone {bone}, which the avatar does not map");
            return node;
        }

        /// <summary>Projects a bone's rotation onto its muscle DoFs
        /// (storing them in the pose), then turns it so its axis points like the reference's.</summary>
        /// <param name="reference">Input: the source skeleton pose, global.</param>
        /// <param name="global">Workspace: the retargeted pose, global (rotations of bone..top updated).</param>
        /// <param name="local">In/out: the retargeted pose, local.</param>
        static void AlignSkeletonPose(Human h, SkeletonPose reference, HumanPose pose, SkeletonPose global, SkeletonPose local, int bone, int topBone)
        {
            var s = h.Skeleton;
            int node = h.BoneIndex[bone];
            if (node != -1)
            {
                var v = Skel.GetDoF(s, local, node);
                if (HasX(bone)) pose.DoF[Bone.DoF[bone, 2]] = v.x;
                if (HasY(bone)) pose.DoF[Bone.DoF[bone, 1]] = v.y;
                if (bone != 0) pose.DoF[Bone.DoF[bone, 0]] = v.z;
                var dof = new F4(
                    HasX(bone) ? pose.DoF[Bone.DoF[bone, 2]] : 0f,
                    HasY(bone) ? pose.DoF[Bone.DoF[bone, 1]] : 0f,
                    bone != 0 ? pose.DoF[Bone.DoF[bone, 0]] : 0f, 0f);
                Skel.SetDoF(s, local, dof, node);
            }
            node = NodeOf(h, bone);
            int top = NodeOf(h, topBone);
            Skel.ComputeGlobalQ(s, local, global, node, top);
            Skel.Align(s, reference, global, node);
            Skel.ComputeLocalQ(s, global, local, node, top);
        }

        /// <summary>Removes the middle limb bone's y (DoF-less) rotation
        /// in up to <paramref name="iterations"/> steps, turning the top bone to keep the middle bone's
        /// global rotation; stops when the removed angle is below <paramref name="minAngle"/> degrees or
        /// grows again.</summary>
        /// <param name="local">In/out: the pose, local.</param>
        /// <param name="ws">Workspace: globals relative to the top bone's parent.</param>
        static void FixMidDoF(Human h, SkeletonPose local, SkeletonPose ws, int topBone, int midBone, float minAngle, int iterations)
        {
            var s = h.Skeleton;
            int top = h.BoneIndex[topBone], mid = h.BoneIndex[midBone];
            ws.X[s.Parent[top]].q = F4.QIdentity;
            Skel.ComputeGlobalQ(s, local, ws, mid, top);
            var refQ = local.X[top].q;
            if (iterations <= 0) return;
            var axes = s.AxesArray[s.AxesId[mid]];
            var midGlobal = ws.X[mid].q;
            float tenMin = 10f * minAngle;
            float prev = 360f;
            for (int i = 0; i < iterations; i++)
            {
                var qp = M.QNormalize(M.QMul(M.QConj(axes.PreQ), M.QMul(local.X[mid].q, axes.PostQ)));
                var zy = M.Quat2ZYRoll(qp);
                // The angle is taken absolute, so the axis sign does not matter here.
                float a = MathF.Atan(zy.y);
                float angle = MathF.Abs(a + a) * Rad2Deg;
                if (minAngle > angle) return;
                if (tenMin > angle && angle > prev) return;
                var q = M.ZYRoll2Quat(new F4(zy.x, 0f, zy.z, 0f));
                local.X[mid].q = M.QNormalize(M.QMul(axes.PreQ, M.QMul(q, M.QConj(axes.PostQ))));
                Skel.ComputeGlobalQ(s, local, ws, mid, mid);
                local.X[top].q = M.QNormalize(M.QMul(M.QMul(midGlobal, M.QConj(ws.X[mid].q)), local.X[top].q));
                local.X[top].q = AlignQ(s, refQ, local.X[top].q, top);
                Skel.ComputeGlobalQ(s, local, ws, mid, top);
                ws.X[mid].q = midGlobal;
                Skel.ComputeLocalQ(s, ws, local, mid, mid);
                prev = angle;
            }
        }

        /// <summary>Removes the end bone's (hand, foot) twist, which has no
        /// muscle: the first step resets the middle and end bones to their zero muscle rotation, later
        /// steps drop the end bone's roll; the middle bone turns to keep the end bone's global rotation.
        /// Stops below <paramref name="minAngle"/> degrees or when the angle grows (from the third step).</summary>
        /// <param name="local">In/out: the pose, local.</param>
        /// <param name="ws">Workspace: globals relative to the middle bone's parent.</param>
        static void FixEndDoF(Human h, SkeletonPose local, SkeletonPose ws, int midBone, int endBone, float minAngle, int iterations)
        {
            var s = h.Skeleton;
            int mid = h.BoneIndex[midBone], end = h.BoneIndex[endBone];
            ws.X[s.Parent[mid]].q = F4.QIdentity;
            Skel.ComputeGlobalQ(s, local, ws, end, mid);
            var refQ = local.X[mid].q;
            if (iterations <= 0) return;
            var midAxes = s.AxesArray[s.AxesId[mid]];
            var endAxes = s.AxesArray[s.AxesId[end]];
            var endGlobal = ws.X[end].q;
            var midZero = M.QNormalize(M.QMul(midAxes.PreQ, M.QConj(midAxes.PostQ)));
            var endZero = M.QNormalize(M.QMul(endAxes.PreQ, M.QConj(endAxes.PostQ)));
            float prev = 360f;
            for (int i = 0; ; )
            {
                var qp = M.QNormalize(M.QMul(M.QConj(endAxes.PreQ), M.QMul(local.X[end].q, endAxes.PostQ)));
                var t = M.Quat2Qtan(qp);
                float a = MathF.Atan(t.x);
                float angle = MathF.Abs((a + a) * Rad2Deg);
                if (minAngle > angle) return;
                if (i >= 2 && angle > prev) return;
                int stop;
                if (i == 0)
                {
                    local.X[mid].q = midZero;
                    local.X[end].q = endZero;
                    stop = mid;
                }
                else
                {
                    var zy = M.Quat2ZYRoll(qp);
                    var q = M.ZYRoll2Quat(new F4(0f, zy.y, zy.z, 0f));
                    local.X[end].q = M.QNormalize(M.QMul(endAxes.PreQ, M.QMul(q, M.QConj(endAxes.PostQ))));
                    stop = end;
                }
                Skel.ComputeGlobalQ(s, local, ws, end, stop);
                local.X[mid].q = M.QNormalize(M.QMul(M.QMul(endGlobal, M.QConj(ws.X[end].q)), local.X[mid].q));
                local.X[mid].q = Skel.Align(refQ, local.X[mid].q, midAxes.PostQ);
                Skel.ComputeGlobalQ(s, local, ws, end, mid);
                ws.X[end].q = endGlobal;
                Skel.ComputeLocalQ(s, ws, local, end, end);
                i++;
                if (i >= iterations) return;
                prev = angle;
            }
        }

        /// <summary>Turns the limb's top bone so the end bone points at
        /// the reference's end position.</summary>
        /// <param name="reference">Input: the source pose, global.</param>
        /// <param name="global">In/out: the pose, global (top..end recomputed).</param>
        /// <param name="local">Output: the top bone's local.</param>
        static void Human2LimbAlign(Human h, SkeletonPose reference, SkeletonPose global, SkeletonPose local, int endBone, int topBone)
        {
            var s = h.Skeleton;
            int top = h.BoneIndex[topBone], end = h.BoneIndex[endBone];
            var tt = global.X[top].t;
            var e = global.X[end].t;
            var r = reference.X[end].t;
            var a = new F4(e.x - tt.x, e.y - tt.y, e.z - tt.z, 0f);
            var b = new F4(r.x - tt.x, r.y - tt.y, r.z - tt.z, 0f);
            var c = M.Cross(a, b);
            float w = MathF.Sqrt(Dot3(b, b) * Dot3(a, a)) + Dot3(a, b);
            var q = M.QNormalize(new F4(c.x, c.y, c.z, w));
            global.X[top].q = M.QMul(q, global.X[top].q);
            Skel.ComputeLocal(s, global, local, top, top);
            Skel.ComputeGlobal(s, local, global, end, top);
        }

        /// <summary>A goal's transform in a global pose: the end bone's
        /// position and muscle frame rotation, the feet moved along their axis by the foot length.</summary>
        /// <param name="global">Input: the pose, global.</param>
        internal static Trs GetGoalXform(Human h, SkeletonPose global, int goal)
        {
            var s = h.Skeleton;
            int node = h.BoneIndex[GoalBone[goal]];
            var x = global.X[node];
            var q = M.QNormalize(M.QMul(x.q, s.AxesArray[s.AxesId[node]].PostQ));
            var t = x.t;
            if (goal < 2)
            {
                int foot = h.BoneIndex[goal != 0 ? Bone.RightFoot : Bone.LeftFoot];
                float len = s.AxesArray[s.AxesId[foot]].Length;
                t = t + M.QRotate(q, new F4(len, 0f, 0f, 0f));
            }
            return new Trs(t, q, F4.One3);
        }

        /// <summary>The human pose (root, muscles, goals) of a skeleton pose.
        /// The translation DoF branches (taken with a <paramref name="tdof"/> callback on avatars with
        /// translation DoF) are not supported and throw.</summary>
        /// <param name="local">Input: the skeleton pose, local.</param>
        /// <param name="pose">Output: root, goal transforms, DoF and hand DoF (other fields untouched).</param>
        /// <param name="ws4">Workspace: the input pose, global (left holding it).</param>
        /// <param name="ws5">Workspace: the retargeted pose, global.</param>
        /// <param name="ws6">Workspace: the retargeted pose, local (left holding it).</param>
        /// <param name="ws7">Workspace: per-limb globals.</param>
        internal static void RetargetFrom(Human h, SkeletonPose local, HumanPose pose, SkeletonPose ws4, SkeletonPose ws5,
            SkeletonPose ws6, SkeletonPose ws7, object tdof)
        {
            if (tdof != null && h.HasTDoF)
                throw new NotSupportedException("humanoid translation DoF retargeting from a skeleton pose (RetargetFromTDoF) is not supported");
            var s = h.Skeleton;
            var tpose = h.SkeletonPose;
            int hips = h.BoneIndex[Bone.Hips];
            float scale = h.Scale;

            Skel.ComputeGlobal(s, local, ws4);
            ws5.CopyFrom(ws4);
            ws6.CopyFrom(local);
            // Nodes without muscles keep their T-pose rotation.
            for (int i = 1; i < s.Count; i++)
                if (s.AxesId[i] == -1) ws5.X[i].q = M.QMul(ws5.X[s.Parent[i]].q, tpose.X[i].q);
            Skel.ComputeLocalQ(s, ws5, ws6);

            bool hasLeftShoulder = h.BoneIndex[Bone.LeftShoulder] != -1, hasRightShoulder = h.BoneIndex[Bone.RightShoulder] != -1;
            if (hasLeftShoulder) AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.LeftShoulder, Bone.LeftShoulder);
            if (hasRightShoulder) AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.RightShoulder, Bone.RightShoulder);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.LeftUpperArm, hasLeftShoulder ? Bone.LeftShoulder : Bone.LeftUpperArm);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.RightUpperArm, hasRightShoulder ? Bone.RightShoulder : Bone.RightUpperArm);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.LeftUpperLeg, Bone.LeftUpperLeg);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.RightUpperLeg, Bone.RightUpperLeg);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.LeftLowerArm, Bone.LeftUpperArm);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.RightLowerArm, Bone.RightUpperArm);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.LeftLowerLeg, Bone.LeftUpperLeg);
            AlignSkeletonPose(h, ws4, pose, ws5, ws6, Bone.RightLowerLeg, Bone.RightUpperLeg);

            const float MinAngle = 0.1f;
            FixMidDoF(h, ws6, ws7, Bone.LeftUpperArm, Bone.LeftLowerArm, MinAngle, 10);
            FixMidDoF(h, ws6, ws7, Bone.RightUpperArm, Bone.RightLowerArm, MinAngle, 10);
            FixMidDoF(h, ws6, ws7, Bone.LeftUpperLeg, Bone.LeftLowerLeg, MinAngle, 10);
            FixMidDoF(h, ws6, ws7, Bone.RightUpperLeg, Bone.RightLowerLeg, MinAngle, 10);

            Skeleton2HumanPose(h, ws6, pose);
            Human2SkeletonPose(h, pose, ws6);
            Skel.ComputeGlobal(s, ws6, ws5);
            Human2LimbAlign(h, ws4, ws5, ws6, Bone.LeftFoot, Bone.LeftUpperLeg);
            Human2LimbAlign(h, ws4, ws5, ws6, Bone.RightFoot, Bone.RightUpperLeg);
            Human2LimbAlign(h, ws4, ws5, ws6, Bone.LeftHand, Bone.LeftUpperArm);
            Human2LimbAlign(h, ws4, ws5, ws6, Bone.RightHand, Bone.RightUpperArm);

            // Hands, feet and head take the source's global rotations.
            foreach (int bone in SourceRotationBones)
            {
                int n = h.BoneIndex[bone];
                ws5.X[n].q = ws4.X[n].q;
                Skel.ComputeLocalQ(s, ws5, ws6, n, n);
            }

            FixEndDoF(h, ws6, ws7, Bone.LeftLowerArm, Bone.LeftHand, MinAngle, 5);
            FixEndDoF(h, ws6, ws7, Bone.RightLowerArm, Bone.RightHand, MinAngle, 5);
            FixEndDoF(h, ws6, ws7, Bone.LeftLowerLeg, Bone.LeftFoot, MinAngle, 5);
            FixEndDoF(h, ws6, ws7, Bone.RightLowerLeg, Bone.RightFoot, MinAngle, 5);

            Skeleton2HumanPose(h, ws6, pose);
            ws6.CopyFrom(tpose);
            Human2SkeletonPose(h, pose, ws6);
            if (h.HasTDoF) RetargetToTDoF(h, pose, tpose, ws6, ws7);
            Skel.ComputeGlobal(s, ws6, ws5);

            // The root: mass center and body frame of the retargeted pose, moved from its hips onto the
            // source's hips.
            var root = new Trs(MassCenter(h, ws5), ComputeOrientation(h, ws5), F4.One3);
            root = M.InvMul(ws5.X[hips], root);
            var hx = ws4.X[hips];
            root = new Trs(M.QRotate(hx.q, root.t * hx.s) + hx.t, M.QMul(hx.q, root.q), F4.One3);
            pose.RootX = root;

            var rootConj = M.QConj(root.q);
            for (int g = 0; g < 4; g++)
            {
                var x = GetGoalXform(h, ws4, g);
                pose.Goals[g].X = new Trs(M.QRotate(rootConj, x.t - root.t) / scale, M.QMul(rootConj, x.q), F4.One3);
            }
            pose.RootX.t = root.t / scale;
        }

        /// <summary>Applies the translation DoFs: each bone moves from its
        /// T-pose position, in the frame of its closest existing parent bone, by TDoF * scale.</summary>
        /// <param name="tpose">Input: the T-pose, local.</param>
        /// <param name="local">In/out: the pose, local (bone translations).</param>
        /// <param name="ws">Workspace: globals relative to the parent bone's parent.</param>
        internal static void RetargetToTDoF(Human h, HumanPose pose, SkeletonPose tpose, SkeletonPose local, SkeletonPose ws)
        {
            var s = h.Skeleton;
            for (int i = 0; i < Bone.TDoFCount; i++)
            {
                int bone = TDoFBone[i];
                int cur = bone, parentNode;
                while (true)
                {
                    int pb = BoneParent[cur];
                    parentNode = pb < 0 ? -1 : h.BoneIndex[pb];
                    if (cur == 0) break;
                    cur = pb;
                    if (parentNode != -1) break;
                }
                int node = h.BoneIndex[bone];
                if (node == -1 || parentNode == -1) continue;
                ws.X[s.Parent[parentNode]] = Trs.Identity;
                local.X[node].t = tpose.X[node].t;
                Skel.ComputeGlobal(s, local, ws, node, parentNode);
                var px = ws.X[parentNode];
                var q = M.QNormalize(M.QMul(px.q, AxesOf(s, parentNode, "translation DoF parent").PostQ));
                var td = pose.TDoF[i];
                var v = new F4(h.Scale * td.x * px.s.x, h.Scale * td.y * px.s.y, h.Scale * td.z * px.s.z, td.w * 0f * px.s.w);
                ws.X[node].t = M.QRotate(q, v) + ws.X[node].t;
                Skel.ComputeLocal(s, ws, local, node, node);
            }
        }

        // ---------------------------------------------------------------- RetargetTo

        /// <summary>The fields RetargetTo copies from the input pose: root, look-at, goals, body, finger
        /// and translation DoFs (not the hands' grab transform and override values).</summary>
        static void CopyRetargetFields(HumanPose dst, HumanPose src)
        {
            dst.RootX = src.RootX;
            dst.LookAtPosition = src.LookAtPosition;
            dst.LookAtWeight = src.LookAtWeight;
            Array.Copy(src.Goals, dst.Goals, dst.Goals.Length);
            CopyDoFs(dst, src);
        }

        static void CopyDoFs(HumanPose dst, HumanPose src)
        {
            Array.Copy(src.DoF, dst.DoF, dst.DoF.Length);
            Array.Copy(src.LeftHand.DoF, dst.LeftHand.DoF, dst.LeftHand.DoF.Length);
            Array.Copy(src.RightHand.DoF, dst.RightHand.DoF, dst.RightHand.DoF.Length);
            Array.Copy(src.TDoF, dst.TDoF, dst.TDoF.Length);
        }

        /// <summary>The skeleton pose of a human pose placed by
        /// <paramref name="x"/>: the root and goals are scaled by the human scale and moved by x, the
        /// muscles set the bone rotations, the hips are placed so the body's mass center and frame are
        /// at the root. With <paramref name="prev"/>, the goals come from it and, after the hips are
        /// placed, so do the muscle values.</summary>
        /// <param name="pose">Input: the human pose (muscle space, unscaled).</param>
        /// <param name="prev">Input, optional: the pose whose goals and muscles are used.</param>
        /// <param name="outPose">Output: the pose with world root and goals.</param>
        /// <param name="ws1">Output: the skeleton pose, local (node 0 = the root).</param>
        /// <param name="ws2">Workspace: the skeleton pose, global.</param>
        /// <param name="adjustMissingBones">Applies AdjustForMissingBones to the output pose.</param>
        internal static void RetargetTo(Human h, HumanPose pose, HumanPose prev, Trs x, HumanPose outPose, SkeletonPose ws1,
            SkeletonPose ws2, bool adjustMissingBones)
        {
            var s = h.Skeleton;
            int hips = h.BoneIndex[Bone.Hips];
            float scale = h.Scale;

            CopyRetargetFields(outPose, pose);
            var root = pose.RootX;
            root.t = root.t * scale;
            outPose.RootX = M.Mul(x, root);

            var goals = (prev ?? pose).Goals;
            for (int g = 0; g < 4; g++)
            {
                var gx = goals[g].X;
                gx.t = gx.t * scale;
                gx = M.Mul(x, gx);
                if (g < 2)
                {
                    int foot = h.BoneIndex[g != 0 ? Bone.RightFoot : Bone.LeftFoot];
                    float len = s.AxesArray[s.AxesId[foot]].Length;
                    var v = new F4(gx.s.x * -len, gx.s.y * -0f, gx.s.z * -0f, gx.s.w * -0f);
                    gx.t = gx.t + M.QRotate(gx.q, v);
                }
                outPose.Goals[g].X = gx;
            }

            ws1.CopyFrom(h.SkeletonPose);
            if (adjustMissingBones) AdjustForMissingBones(h, outPose);
            Human2SkeletonPose(h, outPose, ws1);
            if (h.HasTDoF) RetargetToTDoF(h, outPose, h.SkeletonPose, ws1, ws2);
            Skel.ComputeGlobal(s, ws1, ws2);

            // The hips relative to the body frame (mass center, orientation).
            var center = MassCenter(h, ws2);
            var qc = M.QConj(ComputeOrientation(h, ws2));
            var hx = ws2.X[hips];
            ws1.X[hips] = new Trs(M.QRotate(qc, hx.t - center), M.QMul(qc, hx.q), hx.s);

            if (prev != null)
            {
                CopyDoFs(outPose, prev);
                if (adjustMissingBones) AdjustForMissingBones(h, outPose);
                Human2SkeletonPose(h, outPose, ws1);
                if (h.HasTDoF) RetargetToTDoF(h, outPose, h.SkeletonPose, ws1, ws2);
            }
            ws1.X[0] = outPose.RootX;
        }

        /// <summary>Spreads the limb twists (arm, forearm, upper leg, leg twist
        /// factors) between the limb bones.</summary>
        /// <param name="local">In/out: the skeleton pose, local.</param>
        /// <param name="global">Workspace: globals relative to each limb bone's parent.</param>
        internal static void TwistSolve(Human h, SkeletonPose local, SkeletonPose global)
        {
            var b = h.BoneIndex;
            var s = h.Skeleton;
            HumanFixTwist(b, s, local, global, Bone.LeftLowerArm, Bone.LeftHand, h.ForeArmTwist);
            HumanFixTwist(b, s, local, global, Bone.LeftUpperArm, Bone.LeftLowerArm, h.ArmTwist);
            HumanFixTwist(b, s, local, global, Bone.RightLowerArm, Bone.RightHand, h.ForeArmTwist);
            HumanFixTwist(b, s, local, global, Bone.RightUpperArm, Bone.RightLowerArm, h.ArmTwist);
            HumanFixTwist(b, s, local, global, Bone.LeftLowerLeg, Bone.LeftFoot, h.LegTwist);
            HumanFixTwist(b, s, local, global, Bone.LeftUpperLeg, Bone.LeftLowerLeg, h.UpperLegTwist);
            HumanFixTwist(b, s, local, global, Bone.RightLowerLeg, Bone.RightFoot, h.LegTwist);
            HumanFixTwist(b, s, local, global, Bone.RightUpperLeg, Bone.RightLowerLeg, h.UpperLegTwist);
        }

        /// <summary>Scales a bone's twist muscle by <paramref name="twist"/>,
        /// keeps its axis direction and its child's global rotation.</summary>
        /// <param name="local">In/out: the skeleton pose, local.</param>
        /// <param name="global">Workspace: globals relative to the bone's parent.</param>
        internal static void HumanFixTwist(int[] boneIndex, Skeleton s, SkeletonPose local, SkeletonPose global, int bone, int child, float twist)
        {
            int node = boneIndex[bone], childNode = boneIndex[child];
            global.X[s.Parent[node]].q = F4.QIdentity;
            Skel.ComputeGlobalQ(s, local, global, childNode, node);
            var refQ = local.X[node].q;
            var childGlobal = global.X[childNode].q;
            var axes = s.AxesArray[s.AxesId[node]];
            var dof = Skel.ToAxes(axes, local.X[node].q);
            dof.x = dof.x * twist;
            local.X[node].q = Skel.FromAxes(axes, dof);
            local.X[node].q = Skel.Align(refQ, local.X[node].q, axes.PostQ);
            Skel.ComputeGlobalQ(s, local, global, childNode, node);
            global.X[childNode].q = childGlobal;
            Skel.ComputeLocalQ(s, global, local, childNode, childNode);
        }
    }
}
