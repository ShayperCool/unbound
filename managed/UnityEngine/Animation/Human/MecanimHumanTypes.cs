using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>Unity's internal human bone order; `HumanBodyBones` puts
    /// UpperChest last (54) instead of 9.</summary>
    internal static class Bone
    {
        public const int Hips = 0, LeftUpperLeg = 1, RightUpperLeg = 2, LeftLowerLeg = 3, RightLowerLeg = 4, LeftFoot = 5,
            RightFoot = 6, Spine = 7, Chest = 8, UpperChest = 9, Neck = 10, Head = 11, LeftShoulder = 12, RightShoulder = 13,
            LeftUpperArm = 14, RightUpperArm = 15, LeftLowerArm = 16, RightLowerArm = 17, LeftHand = 18, RightHand = 19,
            LeftToes = 20, RightToes = 21, LeftEye = 22, RightEye = 23, Jaw = 24, Last = 25;

        public const int DoFCount = 55, HandDoFCount = 20, FingerBoneCount = 15, TDoFCount = 21;

        /// <summary>Per bone: the DoF index of its z, y and x muscle (−1 for none).</summary>
        public static readonly int[,] DoF =
        {
            { -1, -1, -1 }, { 21, 22, 23 }, { 29, 30, 31 }, { 24, -1, 25 }, { 32, -1, 33 }, { 26, 27, -1 }, { 34, 35, -1 },
            { 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 }, { 9, 10, 11 }, { 12, 13, 14 }, { 37, 38, -1 }, { 46, 47, -1 },
            { 39, 40, 41 }, { 48, 49, 50 }, { 42, -1, 43 }, { 51, -1, 52 }, { 44, 45, -1 }, { 53, 54, -1 },
            { 28, -1, -1 }, { 36, -1, -1 }, { 15, 16, -1 }, { 17, 18, -1 }, { 19, 20, -1 },
        };
    }

    /// <summary>The skeleton index of each finger bone (finger * 3 + phalanx).</summary>
    internal sealed class Hand
    {
        public readonly int[] BoneIndex = new int[Bone.FingerBoneCount];

        public Hand()
        {
            for (int i = 0; i < BoneIndex.Length; i++) BoneIndex[i] = -1;
        }
    }

    /// <summary>The human skeleton (node 0 the hips' parent, then the human
    /// bones and the nodes between them), its T-pose, bone indices, masses and parameters.</summary>
    internal sealed class Human
    {
        public Trs RootX = Trs.Identity;
        public Skeleton Skeleton;
        /// <summary>The T-pose, local.</summary>
        public SkeletonPose SkeletonPose;
        public Hand LeftHand, RightHand;
        public readonly int[] BoneIndex = new int[Bone.Last];
        public readonly float[] BoneMass = new float[Bone.Last];
        public float Scale = 1f;
        public float ArmTwist = 0.5f, ForeArmTwist = 0.5f, UpperLegTwist = 0.5f, LegTwist = 0.5f;
        public float ArmStretch = 0.05f, LegStretch = 0.05f, FeetSpacing;
        public bool HasLeftHand, HasRightHand, HasTDoF;

        public Human()
        {
            for (int i = 0; i < BoneIndex.Length; i++) BoneIndex[i] = -1;
        }
    }

    /// <summary>An IK goal (feet, hands) with its weights and hint.</summary>
    internal struct HumanGoal
    {
        public Trs X;
        public float WeightT, WeightR;
        public F4 HintT;
        public float HintWeightT;
    }

    /// <summary>A hand's pose: the finger muscles.</summary>
    internal sealed class HandPose
    {
        public Trs GrabX = Trs.Identity;
        public readonly float[] DoF = new float[Bone.HandDoFCount];
        public float Override, CloseOpen, InOut, Grab;

        public void CopyFrom(HandPose o)
        {
            GrabX = o.GrabX;
            Array.Copy(o.DoF, DoF, DoF.Length);
            Override = o.Override; CloseOpen = o.CloseOpen; InOut = o.InOut; Grab = o.Grab;
        }
    }

    /// <summary>The body (root) transform, look-at, goals, hands, the
    /// 55 body DoFs and the translation DoFs.</summary>
    internal sealed class HumanPose
    {
        public Trs RootX = Trs.Identity;
        public F4 LookAtPosition;
        public F4 LookAtWeight;
        public readonly HumanGoal[] Goals = new HumanGoal[4];
        public readonly HandPose LeftHand = new HandPose(), RightHand = new HandPose();
        public readonly float[] DoF = new float[Bone.DoFCount];
        public readonly F4[] TDoF = new F4[Bone.TDoFCount];

        public HumanPose()
        {
            for (int i = 0; i < Goals.Length; i++) Goals[i].X = Trs.Identity;
        }

        public void CopyFrom(HumanPose o)
        {
            RootX = o.RootX; LookAtPosition = o.LookAtPosition; LookAtWeight = o.LookAtWeight;
            Array.Copy(o.Goals, Goals, Goals.Length);
            LeftHand.CopyFrom(o.LeftHand); RightHand.CopyFrom(o.RightHand);
            Array.Copy(o.DoF, DoF, DoF.Length);
            Array.Copy(o.TDoF, TDoF, TDoF.Length);
        }
    }

    /// <summary>The Euler decomposition (, axes type 3): human avatars never use it.</summary>
    internal static class Euler
    {
        public static F4 QuatToEulerXYZ(F4 q) =>
            throw new NotSupportedException("Euler muscle axes are not supported (humanoid avatars do not use them)");

        public static F4 EulerXYZToQuat(F4 v) =>
            throw new NotSupportedException("Euler muscle axes are not supported (humanoid avatars do not use them)");
    }
}
