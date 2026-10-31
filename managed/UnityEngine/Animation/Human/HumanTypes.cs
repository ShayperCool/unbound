using System;

namespace UnityEngine
{
    /// <summary>A transform of the avatar's T-pose (`HumanDescription.skeleton`).</summary>
    public partial struct SkeletonBone
    {
        public string name;
        internal string parentName;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    /// <summary>A human bone's muscle range (degrees).</summary>
    public partial struct HumanLimit
    {
        Vector3 m_Min;
        Vector3 m_Max;
        Vector3 m_Center;
        float m_AxisLength;
        int m_UseDefaultValues;

        public bool useDefaultValues
        {
            get => m_UseDefaultValues != 0;
            set => m_UseDefaultValues = value ? 1 : 0;
        }

        public Vector3 min { get => m_Min; set => m_Min = value; }
        public Vector3 max { get => m_Max; set => m_Max = value; }
        public Vector3 center { get => m_Center; set => m_Center = value; }
        public float axisLength { get => m_AxisLength; set => m_AxisLength = value; }
    }

    /// <summary>A transform mapped to a human bone.</summary>
    public partial struct HumanBone
    {
        string m_BoneName;
        string m_HumanName;
        public HumanLimit limit;

        public string boneName { get => m_BoneName; set => m_BoneName = value; }
        public string humanName { get => m_HumanName; set => m_HumanName = value; }
    }

    /// <summary>A humanoid avatar's configuration: bone mapping, T-pose and parameters.</summary>
    public partial struct HumanDescription
    {
        public HumanBone[] human;
        public SkeletonBone[] skeleton;
        internal float m_ArmTwist;
        internal float m_ForeArmTwist;
        internal float m_UpperLegTwist;
        internal float m_LegTwist;
        internal float m_ArmStretch;
        internal float m_LegStretch;
        internal float m_FeetSpacing;
        internal float m_GlobalScale;
        internal string m_RootMotionBoneName;
        internal bool m_HasTranslationDoF;
        internal bool m_HasExtraRoot;
        internal bool m_SkeletonHasParents;

        public float upperArmTwist { get => m_ArmTwist; set => m_ArmTwist = value; }
        public float lowerArmTwist { get => m_ForeArmTwist; set => m_ForeArmTwist = value; }
        public float upperLegTwist { get => m_UpperLegTwist; set => m_UpperLegTwist = value; }
        public float lowerLegTwist { get => m_LegTwist; set => m_LegTwist = value; }
        public float armStretch { get => m_ArmStretch; set => m_ArmStretch = value; }
        public float legStretch { get => m_LegStretch; set => m_LegStretch = value; }
        public float feetSpacing { get => m_FeetSpacing; set => m_FeetSpacing = value; }
        public bool hasTranslationDoF { get => m_HasTranslationDoF; set => m_HasTranslationDoF = value; }

        /// <summary>The description as the model importer's meta has it (`humanDescription`).</summary>
        internal static HumanDescription Read(Unbound.Runtime.YNode node)
        {
            var d = new HumanDescription();
            var humans = new System.Collections.Generic.List<HumanBone>();
            foreach (var h in node.Get("human")?.Seq() ?? Array.Empty<Unbound.Runtime.YNode>())
            {
                var limit = h.Get("limit");
                var bone = new HumanBone { boneName = h.Str("boneName"), humanName = h.Str("humanName") };
                if (limit != null)
                    bone.limit = new HumanLimit
                    {
                        min = limit.Vector3("min"),
                        max = limit.Vector3("max"),
                        center = limit.Vector3("value"),
                        axisLength = limit.Float("length"),
                        useDefaultValues = limit.Int("modified") == 0,
                    };
                else bone.limit.useDefaultValues = true;
                humans.Add(bone);
            }
            d.human = humans.ToArray();
            var skeleton = new System.Collections.Generic.List<SkeletonBone>();
            foreach (var s in node.Get("skeleton")?.Seq() ?? Array.Empty<Unbound.Runtime.YNode>())
                skeleton.Add(new SkeletonBone
                {
                    name = s.Str("name"),
                    parentName = s.Str("parentName"),
                    position = s.Vector3("position"),
                    rotation = s.Quaternion("rotation"),
                    scale = s.Vector3("scale", Vector3.one),
                });
            d.skeleton = skeleton.ToArray();
            d.m_ArmTwist = node.Float("armTwist", 0.5f);
            d.m_ForeArmTwist = node.Float("foreArmTwist", 0.5f);
            d.m_UpperLegTwist = node.Float("upperLegTwist", 0.5f);
            d.m_LegTwist = node.Float("legTwist", 0.5f);
            d.m_ArmStretch = node.Float("armStretch", 0.05f);
            d.m_LegStretch = node.Float("legStretch", 0.05f);
            d.m_FeetSpacing = node.Float("feetSpacing", 0f);
            d.m_GlobalScale = node.Float("globalScale", 1f);
            d.m_RootMotionBoneName = node.Str("rootMotionBoneName");
            d.m_HasTranslationDoF = node.Int("hasTranslationDoF") != 0;
            d.m_HasExtraRoot = node.Int("hasExtraRoot") != 0;
            d.m_SkeletonHasParents = node.Int("skeletonHasParents") != 0;
            return d;
        }

        internal HumanDescription Copy()
        {
            var d = this;
            d.human = human != null ? (HumanBone[])human.Clone() : null;
            d.skeleton = skeleton != null ? (SkeletonBone[])skeleton.Clone() : null;
            return d;
        }
    }

    /// <summary>A pose in muscle space: the body (center of mass) transform and the muscles.</summary>
    public partial struct HumanPose
    {
        public Vector3 bodyPosition;
        public Quaternion bodyRotation;
        public float[] muscles;

        internal void Init()
        {
            if (muscles != null && muscles.Length != HumanTrait.MuscleCount)
                throw new InvalidOperationException("Bad array size for HumanPose.muscles. Size must equal HumanTrait.MuscleCount");
            if (muscles == null)
            {
                muscles = new float[HumanTrait.MuscleCount];
                if (bodyRotation.x == 0f && bodyRotation.y == 0f && bodyRotation.z == 0f && bodyRotation.w == 0f)
                    bodyRotation.w = 1f;
            }
        }
    }
}
