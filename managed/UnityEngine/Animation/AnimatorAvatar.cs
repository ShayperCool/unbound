using System;
using System.Collections.Generic;
using Unbound.Runtime.Playables;

namespace UnityEngine
{
    /// <summary>An Animator's avatar. Unbound animates generic hierarchies: there are no humanoid
    /// avatars, and a model's avatar reference resolves to none.</summary>
    public partial class Avatar : Object
    {
        Avatar() { }

        public bool isValid
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Avatar.isValid", "is always true (avatars are not read yet)");
                return true;
            }
        }

        public bool isHuman
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Avatar.isHuman", "is always false (humanoid avatars are not supported yet)");
                return false;
            }
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

        /// <summary>Stored; Unbound animates without avatars (generic hierarchies).</summary>
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

        /// <summary>Whether a transform is a bone of the avatar: generic Animators have none.</summary>
        internal bool IsBoneTransform(Transform transform) => false;
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
