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
        /// <summary>The model hierarchy a model's avatar is built from.</summary>
        GameObject m_Root;
        /// <summary>A model avatar's transforms in the model file's order: the order of its
        /// skeleton (null: the hierarchy's order).</summary>
        Transform[] m_SkeletonOrder;
        AvatarConstant m_Constant;
        bool m_Built;
        /// <summary>A model's generic avatar: valid, its hierarchy is not needed.</summary>
        bool m_ModelGeneric;
        AvatarConstant m_GenericSkeleton;

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
                    // In the model file's order, as the generic skeleton: what a deoptimized
                    // hierarchy is rebuilt in (6000.0.71f1, tests/projects/animoptimize h.deopt).
                    m_Constant = AvatarBuild.Build(m_Root, m_Description, true, false, out string error, m_SkeletonOrder);
                    if (m_Constant == null) Debug.LogError($"Avatar '{name}' is invalid: {error}");
                }
                return m_Constant;
            }
        }

        public bool isValid => m_ModelGeneric || Constant != null;

        /// <summary>The avatar's skeleton (every transform of the hierarchy it was built from, its
        /// path hash and default pose): what an Animator without its transform hierarchy animates.
        /// A model's generic avatar builds it from its model on first use, so a model must be asked
        /// before its hierarchy is optimized (<see cref="HierarchyOptimizer"/>). Null when invalid.</summary>
        internal AvatarConstant Skeleton
        {
            get
            {
                if (!m_ModelGeneric) return Constant;
                if (m_GenericSkeleton == null && m_Root != null)
                {
                    var none = new HumanDescription { human = Array.Empty<HumanBone>(), skeleton = Array.Empty<SkeletonBone>() };
                    m_GenericSkeleton = AvatarBuild.Build(m_Root, none, false, false, out _, m_SkeletonOrder);
                }
                return m_GenericSkeleton;
            }
        }

        public bool isHuman => Constant?.IsHuman ?? false;

        public HumanDescription humanDescription => m_Description.Copy();

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var root = body.Ref("m_UnboundRoot");
            m_Root = root.IsNull ? null : resolver.Resolve(root, typeof(GameObject)) as GameObject;
            if (body.Get("m_UnboundSkeletonOrder") is YNode order)
            {
                var transforms = new List<Transform>();
                foreach (var item in order.Seq())
                    transforms.Add(resolver.Resolve(item.AsRef(), typeof(Transform)) as Transform);
                m_SkeletonOrder = transforms.ToArray();
            }
            var human = body.Get("m_UnboundHumanDescription");
            if (human == null)
            {
                m_ModelGeneric = true;
                m_Description = new HumanDescription { human = Array.Empty<HumanBone>(), skeleton = Array.Empty<SkeletonBone>() };
                return;
            }
            m_Description = HumanDescription.Read(human);
            m_Human = true;
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

        /// <summary>Whether a humanoid body part is left out.</summary>
        internal bool MasksHumanoidParts => Array.IndexOf(m_HumanoidParts, false) >= 0;

        /// <summary>Whether the mask lets a layer animate a humanoid curve
        /// (<see cref="Unbound.Runtime.Humanoid.HumanClipImport.BodyPart"/>).</summary>
        internal bool AllowsHumanCurve(int curve) => m_HumanoidParts[Unbound.Runtime.Humanoid.HumanClipImport.BodyPart(curve)];

        /// <summary>Whether the mask lets a layer animate a bound channel group: humanoid curves
        /// follow the body parts; Transform properties the transform paths below
        /// <paramref name="root"/>; other components' properties are animated whatever the mask says
        /// about their object.</summary>
        internal bool AllowsGroup(Transform root, Unbound.Runtime.PropertyGroup group)
        {
            if (group != null && group.HumanCurve >= 0) return AllowsHumanCurve(group.HumanCurve);
            if (group?.SkeletonPath != null) return Allows(group.SkeletonPath);
            if (!(group?.Target is Transform)) return true;
            var t = group.Transform;
            if (t == null || root == null) return true;
            var parts = new System.Collections.Generic.List<string>();
            for (var p = t; p != null && p != root; p = p.m_Parent) parts.Insert(0, p.m_GameObject.m_Name);
            return Allows(string.Join("/", parts));
        }

        /// <summary>Whether the mask filters anything: transform entries or humanoid body parts.</summary>
        internal bool Filters => m_Elements.Count > 0 || MasksHumanoidParts;

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
            get { MainThreadCheck.Check("get_avatar"); return m_Avatar; }
            set { MainThreadCheck.Check("set_avatar"); m_Avatar = value; }
        }

        /// <summary>Whether animation outputs of playable graphs target this Animator.</summary>
        public bool hasBoundPlayables
        {
            get
            { MainThreadCheck.Check("get_hasBoundPlayables");
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

        internal bool IsHuman => m_Avatar != null && m_Avatar.isHuman;

        public bool isHuman
        { get { MainThreadCheck.Check("get_isHuman"); return IsHuman; } }

        public bool isOptimizable
        { get { MainThreadCheck.Check("get_isOptimizable"); return m_Avatar != null && m_Avatar.isValid; } }

        /// <summary>The avatar's human scale (the T-pose's center of mass height), 1 without a human.</summary>
        public float humanScale
        { get { MainThreadCheck.Check("get_humanScale"); return AvatarConstant is { IsHuman: true } c ? c.Human.Scale : 1f; } }

        public float leftFeetBottomHeight { get { MainThreadCheck.Check("get_leftFeetBottomHeight"); return FootHeight(true); } }

        public float rightFeetBottomHeight { get { MainThreadCheck.Check("get_rightFeetBottomHeight"); return FootHeight(false); } }

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
