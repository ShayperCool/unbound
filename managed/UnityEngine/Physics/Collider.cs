using System;

namespace UnityEngine
{
    /// <summary>Base of the 3D colliders.</summary>
    [Unbound.Runtime.ClassId(56)]
    public partial class Collider : Component
    {
        internal bool m_Enabled = true;
        internal bool m_IsTrigger;

        public bool enabled { get => m_Enabled; set => m_Enabled = value; }
        public bool isTrigger { get => m_IsTrigger; set => m_IsTrigger = value; }
    }

    [Unbound.Runtime.ClassId(65)]
    public partial class BoxCollider : Collider
    {
        internal Vector3 m_Center;
        internal Vector3 m_Size = Vector3.one;

        public Vector3 center { get => m_Center; set => m_Center = value; }
        public Vector3 size { get => m_Size; set => m_Size = value; }
    }
}
