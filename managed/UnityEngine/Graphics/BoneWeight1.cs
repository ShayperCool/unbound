using System;

namespace UnityEngine
{
    [Serializable]
    public partial struct BoneWeight1 : IEquatable<BoneWeight1>
    {
        [SerializeField] float m_Weight;
        [SerializeField] int m_BoneIndex;

        public float weight { get => m_Weight; set => m_Weight = value; }
        public int boneIndex { get => m_BoneIndex; set => m_BoneIndex = value; }

        public bool Equals(BoneWeight1 other) => m_BoneIndex == other.m_BoneIndex && m_Weight.Equals(other.m_Weight);
        public override bool Equals(object other) => other is BoneWeight1 value && Equals(value);
        public override int GetHashCode() => m_Weight.GetHashCode() ^ m_BoneIndex;
        public static bool operator ==(BoneWeight1 lhs, BoneWeight1 rhs) =>
            lhs.m_BoneIndex == rhs.m_BoneIndex && lhs.m_Weight == rhs.m_Weight;

        public static bool operator !=(BoneWeight1 lhs, BoneWeight1 rhs) => !(lhs == rhs);
    }
}
