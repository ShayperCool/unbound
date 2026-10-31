using System;

namespace UnityEngine
{
    /// <summary>Unity's human bone and muscle tables (`HumanTrait`, 6000.0): names, bone/muscle mapping,
    /// default muscle ranges in degrees, required bones, parents and hierarchy masses, as the Editor
    /// returns them (tests/projects/humanoid).</summary>
    public partial class HumanTrait
    {
        public HumanTrait() { }

        public static int MuscleCount => s_MuscleNames.Length;
        public static string[] MuscleName => (string[])s_MuscleNames.Clone();
        public static int BoneCount => s_BoneNames.Length;
        public static string[] BoneName => (string[])s_BoneNames.Clone();
        public static int RequiredBoneCount
        {
            get
            {
                int n = 0;
                foreach (bool r in s_Required) if (r) n++;
                return n;
            }
        }

        public static int MuscleFromBone(int i, int dofIndex)
        {
            if (i < 0 || i >= BoneCount || dofIndex < 0 || dofIndex > 2) return -1;
            return s_BoneMuscles[i, dofIndex];
        }

        public static int BoneFromMuscle(int i) => i < 0 || i >= MuscleCount ? -1 : s_MuscleBone[i];

        public static bool RequiredBone(int i) => i >= 0 && i < BoneCount && s_Required[i];

        public static float GetMuscleDefaultMin(int i) => i < 0 || i >= MuscleCount ? 0f : s_MuscleMin[i];

        public static float GetMuscleDefaultMax(int i) => i < 0 || i >= MuscleCount ? 0f : s_MuscleMax[i];

        public static float GetBoneDefaultHierarchyMass(int i) => i < 0 || i >= BoneCount ? 0f : s_HierarchyMass[i];

        public static int GetParentBone(int i) => i < 0 || i >= BoneCount ? -1 : s_Parent[i];

        /// <summary>A `HumanBodyBones` index to Unity's internal human bone index (UpperChest is 9
        /// internally, 54 in `HumanBodyBones`; fingers follow the 25 body bones).</summary>
        internal static int GetBoneIndexFromMono(int humanId)
        {
            if (humanId == 54) return 9;
            if (humanId < 0 || humanId > 54) return -1;
            return humanId < 9 ? humanId : humanId + 1;
        }

        internal static int GetBoneIndexToMono(int boneIndex)
        {
            if (boneIndex == 9) return 54;
            if (boneIndex < 0 || boneIndex > 54) return -1;
            return boneIndex < 9 ? boneIndex : boneIndex - 1;
        }

        static readonly string[] s_MuscleNames =
        {
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right", "Chest Front-Back",
            "Chest Left-Right", "Chest Twist Left-Right", "UpperChest Front-Back", "UpperChest Left-Right",
            "UpperChest Twist Left-Right", "Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right",
            "Head Nod Down-Up", "Head Tilt Left-Right", "Head Turn Left-Right", "Left Eye Down-Up",
            "Left Eye In-Out", "Right Eye Down-Up", "Right Eye In-Out", "Jaw Close",
            "Jaw Left-Right", "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
            "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out", "Left Foot Up-Down", "Left Foot Twist In-Out",
            "Left Toes Up-Down", "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out",
            "Right Lower Leg Stretch", "Right Lower Leg Twist In-Out", "Right Foot Up-Down", "Right Foot Twist In-Out",
            "Right Toes Up-Down", "Left Shoulder Down-Up", "Left Shoulder Front-Back", "Left Arm Down-Up",
            "Left Arm Front-Back", "Left Arm Twist In-Out", "Left Forearm Stretch", "Left Forearm Twist In-Out",
            "Left Hand Down-Up", "Left Hand In-Out", "Right Shoulder Down-Up", "Right Shoulder Front-Back",
            "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch",
            "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out", "Left Thumb 1 Stretched",
            "Left Thumb Spread", "Left Thumb 2 Stretched", "Left Thumb 3 Stretched", "Left Index 1 Stretched",
            "Left Index Spread", "Left Index 2 Stretched", "Left Index 3 Stretched", "Left Middle 1 Stretched",
            "Left Middle Spread", "Left Middle 2 Stretched", "Left Middle 3 Stretched", "Left Ring 1 Stretched",
            "Left Ring Spread", "Left Ring 2 Stretched", "Left Ring 3 Stretched", "Left Little 1 Stretched",
            "Left Little Spread", "Left Little 2 Stretched", "Left Little 3 Stretched", "Right Thumb 1 Stretched",
            "Right Thumb Spread", "Right Thumb 2 Stretched", "Right Thumb 3 Stretched", "Right Index 1 Stretched",
            "Right Index Spread", "Right Index 2 Stretched", "Right Index 3 Stretched", "Right Middle 1 Stretched",
            "Right Middle Spread", "Right Middle 2 Stretched", "Right Middle 3 Stretched", "Right Ring 1 Stretched",
            "Right Ring Spread", "Right Ring 2 Stretched", "Right Ring 3 Stretched", "Right Little 1 Stretched",
            "Right Little Spread", "Right Little 2 Stretched", "Right Little 3 Stretched",
        };
        static readonly int[] s_MuscleBone = { 7, 7, 7, 8, 8, 8, 54, 54, 54, 9, 9, 9, 10, 10, 10, 21, 21, 22, 22, 23, 23, 1, 1, 1, 3, 3, 5, 5, 19, 2, 2, 2, 4, 4, 6, 6, 20, 11, 11, 13, 13, 13, 15, 15, 17, 17, 12, 12, 14, 14, 14, 16, 16, 18, 18, 24, 24, 25, 26, 27, 27, 28, 29, 30, 30, 31, 32, 33, 33, 34, 35, 36, 36, 37, 38, 39, 39, 40, 41, 42, 42, 43, 44, 45, 45, 46, 47, 48, 48, 49, 50, 51, 51, 52, 53 };
        static readonly float[] s_MuscleMin = { -40f, -40f, -40f, -40f, -40f, -40f, -20f, -20f, -20f, -40f, -40f, -40f, -40f, -40f, -40f, -10f, -20f, -10f, -20f, -10f, -10f, -90f, -60f, -60f, -80f, -90f, -50f, -30f, -50f, -90f, -60f, -60f, -80f, -90f, -50f, -30f, -50f, -15f, -15f, -60f, -100f, -90f, -80f, -90f, -80f, -40f, -15f, -15f, -60f, -100f, -90f, -80f, -90f, -80f, -40f, -20f, -25f, -40f, -40f, -50f, -20f, -45f, -45f, -50f, -7.5f, -45f, -45f, -50f, -7.5f, -45f, -45f, -50f, -20f, -45f, -45f, -20f, -25f, -40f, -40f, -50f, -20f, -45f, -45f, -50f, -7.5f, -45f, -45f, -50f, -7.5f, -45f, -45f, -50f, -20f, -45f, -45f };
        static readonly float[] s_MuscleMax = { 40f, 40f, 40f, 40f, 40f, 40f, 20f, 20f, 20f, 40f, 40f, 40f, 40f, 40f, 40f, 15f, 20f, 15f, 20f, 10f, 10f, 50f, 60f, 60f, 80f, 90f, 50f, 30f, 50f, 50f, 60f, 60f, 80f, 90f, 50f, 30f, 50f, 30f, 15f, 100f, 100f, 90f, 80f, 90f, 80f, 40f, 30f, 15f, 100f, 100f, 90f, 80f, 90f, 80f, 40f, 20f, 25f, 35f, 35f, 50f, 20f, 45f, 45f, 50f, 7.5f, 45f, 45f, 50f, 7.5f, 45f, 45f, 50f, 20f, 45f, 45f, 20f, 25f, 35f, 35f, 50f, 20f, 45f, 45f, 50f, 7.5f, 45f, 45f, 50f, 7.5f, 45f, 45f, 50f, 20f, 45f, 45f };
        static readonly string[] s_BoneNames =
        {
            "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot",
            "RightFoot", "Spine", "Chest", "Neck", "Head", "LeftShoulder",
            "RightShoulder", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand",
            "RightHand", "LeftToes", "RightToes", "LeftEye", "RightEye", "Jaw",
            "Left Thumb Proximal", "Left Thumb Intermediate", "Left Thumb Distal", "Left Index Proximal", "Left Index Intermediate", "Left Index Distal",
            "Left Middle Proximal", "Left Middle Intermediate", "Left Middle Distal", "Left Ring Proximal", "Left Ring Intermediate", "Left Ring Distal",
            "Left Little Proximal", "Left Little Intermediate", "Left Little Distal", "Right Thumb Proximal", "Right Thumb Intermediate", "Right Thumb Distal",
            "Right Index Proximal", "Right Index Intermediate", "Right Index Distal", "Right Middle Proximal", "Right Middle Intermediate", "Right Middle Distal",
            "Right Ring Proximal", "Right Ring Intermediate", "Right Ring Distal", "Right Little Proximal", "Right Little Intermediate", "Right Little Distal",
            "UpperChest",
        };
        static readonly int[,] s_BoneMuscles =
        {
            { -1, -1, -1 }, { 23, 22, 21 }, { 31, 30, 29 }, { 25, -1, 24 }, { 33, -1, 32 }, { -1, 27, 26 },
            { -1, 35, 34 }, { 2, 1, 0 }, { 5, 4, 3 }, { 11, 10, 9 }, { 14, 13, 12 }, { -1, 38, 37 },
            { -1, 47, 46 }, { 41, 40, 39 }, { 50, 49, 48 }, { 43, -1, 42 }, { 52, -1, 51 }, { -1, 45, 44 },
            { -1, 54, 53 }, { -1, -1, 28 }, { -1, -1, 36 }, { -1, 16, 15 }, { -1, 18, 17 }, { -1, 20, 19 },
            { -1, 56, 55 }, { -1, -1, 57 }, { -1, -1, 58 }, { -1, 60, 59 }, { -1, -1, 61 }, { -1, -1, 62 },
            { -1, 64, 63 }, { -1, -1, 65 }, { -1, -1, 66 }, { -1, 68, 67 }, { -1, -1, 69 }, { -1, -1, 70 },
            { -1, 72, 71 }, { -1, -1, 73 }, { -1, -1, 74 }, { -1, 76, 75 }, { -1, -1, 77 }, { -1, -1, 78 },
            { -1, 80, 79 }, { -1, -1, 81 }, { -1, -1, 82 }, { -1, 84, 83 }, { -1, -1, 85 }, { -1, -1, 86 },
            { -1, 88, 87 }, { -1, -1, 89 }, { -1, -1, 90 }, { -1, 92, 91 }, { -1, -1, 93 }, { -1, -1, 94 },
            { 8, 7, 6 },
        };
        static readonly bool[] s_Required = { true, true, true, true, true, true, true, true, false, false, true, false, false, true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false };
        static readonly int[] s_Parent = { -1, 0, 0, 1, 2, 3, 4, 0, 7, 54, 9, 54, 54, 11, 12, 13, 14, 15, 16, 5, 6, 10, 10, 10, 17, 24, 25, 17, 27, 28, 17, 30, 31, 17, 33, 34, 17, 36, 37, 18, 39, 40, 18, 42, 43, 18, 45, 46, 18, 48, 49, 18, 51, 52, 8 };
        static readonly float[] s_HierarchyMass = { 82.5f, 15f, 15f, 5f, 5f, 1f, 1f, 40.5f, 38f, 5f, 4f, 4.5f, 4.5f, 4f, 4f, 2f, 2f, 0.5f, 0.5f, 0.2f, 0.2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 26f };
    }
}
