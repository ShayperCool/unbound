using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;

namespace UnityEngine
{
    /// <summary>An Animator's avatar: a model's avatar sub-asset or one built with AvatarBuilder. A
    /// humanoid avatar is built (as the importer does) from its model's hierarchy and the meta's human
    /// description the first time it is used.</summary>
    [ClassId(90)]
    public partial class Avatar : Object, ISerializedAsset
    {
        Avatar() { }

        internal static Avatar Create() => new Avatar();

        internal HumanDescription m_Description;
        /// <summary>A humanoid avatar (its description maps human bones).</summary>
        internal bool m_Human;
        /// <summary>The model hierarchy a model's humanoid avatar is built from.</summary>
        GameObject m_Root;
        AvatarConstant m_Constant;
        bool m_Built;
        /// <summary>A model's generic avatar: valid, its hierarchy is not needed.</summary>
        bool m_ModelGeneric;

        internal void SetConstant(AvatarConstant constant)
        {
            m_Constant = constant;
            m_Built = true;
        }

        /// <summary>The avatar's skeleton and human, built on first use; null when invalid.</summary>
        internal AvatarConstant Constant
        {
            get
            {
                if (m_Built) return m_Constant;
                m_Built = true;
                if (m_Human && m_Root != null)
                {
                    m_Constant = AvatarBuild.Build(m_Root, m_Description, true, false, out string error);
                    if (m_Constant == null) Debug.LogError($"Avatar '{name}' is invalid: {error}");
                }
                return m_Constant;
            }
        }

        public bool isValid => m_ModelGeneric || Constant != null;

        public bool isHuman => Constant?.IsHuman ?? false;

        public HumanDescription humanDescription => m_Description.Copy();

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var human = body.Get("m_UnboundHumanDescription");
            if (human == null)
            {
                m_ModelGeneric = true;
                m_Description = new HumanDescription { human = Array.Empty<HumanBone>(), skeleton = Array.Empty<SkeletonBone>() };
                return;
            }
            m_Description = HumanDescription.Read(human);
            m_Human = true;
            var root = body.Ref("m_UnboundRoot");
            m_Root = root.IsNull ? null : resolver.Resolve(root, typeof(GameObject)) as GameObject;
        }
    }

    public enum AvatarMaskBodyPart
    {
        Root,
        Body,
        Head,
        LeftLeg,
        RightLeg,
        LeftArm,
        RightArm,
        LeftFingers,
        RightFingers,
        LeftFootIK,
        RightFootIK,
        LeftHandIK,
        RightHandIK,
        LastBodyPart,
    }

    public sealed partial class AvatarMask
    {
        /// <summary>The humanoid body parts the mask lets through (all of them by default).</summary>
        internal readonly bool[] m_HumanoidParts = { true, true, true, true, true, true, true, true, true, true, true, true, true };

        public bool GetHumanoidBodyPartActive(AvatarMaskBodyPart index)
        {
            if (index < 0 || index >= AvatarMaskBodyPart.LastBodyPart) throw new IndexOutOfRangeException("Index out of bounds.");
            return m_HumanoidParts[(int)index];
        }

        public void SetHumanoidBodyPartActive(AvatarMaskBodyPart index, bool value)
        {
            if (index < 0 || index >= AvatarMaskBodyPart.LastBodyPart) throw new IndexOutOfRangeException("Index out of bounds.");
            m_HumanoidParts[(int)index] = value;
        }

        /// <summary>`m_Mask`: the body parts as hex-encoded little-endian 32-bit flags.</summary>
        internal void ReadHumanoidMask(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            for (int i = 0; i < m_HumanoidParts.Length && (i + 1) * 8 <= hex.Length; i++)
                m_HumanoidParts[i] = Convert.ToUInt32(hex.Substring(i * 8, 2), 16) != 0;
        }
    }

    public partial class Animator
    {
        internal Avatar m_Avatar;

        public Avatar avatar
        {
            get => m_Avatar;
            set => m_Avatar = value;
        }

        /// <summary>Whether animation outputs of playable graphs target this Animator.</summary>
        public bool hasBoundPlayables
        {
            get
            {
                var outputs = AnimationOutputs.Of(this);
                if (outputs == null) return false;
                foreach (var o in outputs)
                    if (!o.Destroyed) return true;
                return false;
            }
        }

        /// <summary>The avatar's constant when the Animator has a valid avatar.</summary>
        internal AvatarConstant AvatarConstant => m_Avatar != null ? m_Avatar.Constant : null;

        /// <summary>Whether a transform is a bone of the avatar: generic Animators have none.</summary>
        internal bool IsBoneTransform(Transform transform) => false;

        public bool isHuman => m_Avatar != null && m_Avatar.isHuman;

        public bool isOptimizable => m_Avatar != null && m_Avatar.isValid;

        /// <summary>The avatar's human scale (the T-pose's center of mass height), 1 without a human.</summary>
        public float humanScale => AvatarConstant is { IsHuman: true } c ? c.Human.Scale : 1f;

        public float leftFeetBottomHeight => FootHeight(true);

        public float rightFeetBottomHeight => FootHeight(false);

        /// <summary>The length of the foot's muscle axes (its T-pose height).</summary>
        float FootHeight(bool left)
        {
            if (!(AvatarConstant is { IsHuman: true } c)) return 0f;
            int node = c.Human.BoneIndex[left ? Bone.LeftFoot : Bone.RightFoot];
            return c.Human.Skeleton.AxesArray[c.Human.Skeleton.AxesId[node]].Length;
        }

        public Transform GetBoneTransform(HumanBodyBones humanBoneId)
        {
            if (humanBoneId < HumanBodyBones.Hips || humanBoneId >= HumanBodyBones.LastBone)
                throw new IndexOutOfRangeException("humanBoneId must be between 0 and " + HumanBodyBones.LastBone);
            return GetBoneTransformInternal((int)humanBoneId);
        }

        internal Transform GetBoneTransformInternal(int humanBoneId)
        {
            if (!(AvatarConstant is { IsHuman: true } c) || m_GameObject == null) return null;
            int node = c.AvatarNodeOf(humanBoneId);
            return node < 0 ? null : AvatarBinding.Find(m_GameObject.m_Transform, c.AvatarSkeleton.Id[node]);
        }
    }

    /// <summary>The file extensions of an asset type (Timeline's .playable).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed partial class AssetFileNameExtensionAttribute : Attribute
    {
        public AssetFileNameExtensionAttribute(string preferredExtension, params string[] otherExtensions)
        {
            this.preferredExtension = preferredExtension;
            this.otherExtensions = otherExtensions ?? Array.Empty<string>();
        }

        public string preferredExtension { get; }

        public IEnumerable<string> otherExtensions { get; }
    }
}

namespace UnityEngine.Animations
{
    /// <summary>Discrete (integer) properties travel in float channels as their bits.</summary>
    internal static partial class DiscreteEvaluationAttributeUtilities
    {
        public static int ConvertFloatToDiscreteInt(float f) => BitConverter.SingleToInt32Bits(f);

        public static float ConvertDiscreteIntToFloat(int f) => BitConverter.Int32BitsToSingle(f);
    }
}
