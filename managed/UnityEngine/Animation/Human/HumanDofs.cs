namespace UnityEngine
{
    // The humanoid muscle parts and their degrees of freedom (MuscleHandle).
    public enum HumanPartDof
    {
        Body = 0,
        Head = 1,
        LeftLeg = 2,
        RightLeg = 3,
        LeftArm = 4,
        RightArm = 5,
        LeftThumb = 6,
        LeftIndex = 7,
        LeftMiddle = 8,
        LeftRing = 9,
        LeftLittle = 10,
        RightThumb = 11,
        RightIndex = 12,
        RightMiddle = 13,
        RightRing = 14,
        RightLittle = 15,
        LastHumanPartDof = 16,
    }

    public enum BodyDof
    {
        SpineFrontBack = 0,
        SpineLeftRight = 1,
        SpineRollLeftRight = 2,
        ChestFrontBack = 3,
        ChestLeftRight = 4,
        ChestRollLeftRight = 5,
        UpperChestFrontBack = 6,
        UpperChestLeftRight = 7,
        UpperChestRollLeftRight = 8,
        LastBodyDof = 9,
    }

    public enum HeadDof
    {
        NeckFrontBack = 0,
        NeckLeftRight = 1,
        NeckRollLeftRight = 2,
        HeadFrontBack = 3,
        HeadLeftRight = 4,
        HeadRollLeftRight = 5,
        LeftEyeDownUp = 6,
        LeftEyeInOut = 7,
        RightEyeDownUp = 8,
        RightEyeInOut = 9,
        JawDownUp = 10,
        JawLeftRight = 11,
        LastHeadDof = 12,
    }

    public enum LegDof
    {
        UpperLegFrontBack = 0,
        UpperLegInOut = 1,
        UpperLegRollInOut = 2,
        LegCloseOpen = 3,
        LegRollInOut = 4,
        FootCloseOpen = 5,
        FootInOut = 6,
        ToesUpDown = 7,
        LastLegDof = 8,
    }

    public enum ArmDof
    {
        ShoulderDownUp = 0,
        ShoulderFrontBack = 1,
        ArmDownUp = 2,
        ArmFrontBack = 3,
        ArmRollInOut = 4,
        ForeArmCloseOpen = 5,
        ForeArmRollInOut = 6,
        HandDownUp = 7,
        HandInOut = 8,
        LastArmDof = 9,
    }

    public enum FingerDof
    {
        ProximalDownUp = 0,
        ProximalInOut = 1,
        IntermediateCloseOpen = 2,
        DistalCloseOpen = 3,
        LastFingerDof = 4,
    }
}
