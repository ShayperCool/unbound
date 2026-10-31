// Layout and public access measured against Unity 6000.0.71f1 Linux.
namespace UnityEngine
{
    public unsafe readonly partial struct ContactPair
    {
        internal ContactPair(int a, int b, System.IntPtr points, uint count, CollisionPairFlags flags, CollisionPairEventFlags events, Vector3 sum)
        { m_ColliderID=a; m_OtherColliderID=b; m_StartPtr=points; m_NbPoints=count; m_Flags=flags; m_Events=events; m_ImpulseSum=sum; }
        public ref readonly ContactPairPoint GetContactPoint(int index) => ref *GetContactPoint_Internal(index);
        internal ContactPairPoint* GetContactPoint_Internal(int index)
        {
            if (index >= contactCount) throw new System.IndexOutOfRangeException("Invalid ContactPairPoint index. Index should be greater than 0 and less than ContactPair.ContactCount");
            return (ContactPairPoint*)m_StartPtr + index;
        }
        public uint GetContactPointFaceIndex(int contactIndex)
        {
            ref readonly var point = ref GetContactPoint(contactIndex);
            return point.m_InternalFaceIndex0 != uint.MaxValue ? point.m_InternalFaceIndex0 : point.m_InternalFaceIndex1;
        }
        public void CopyToNativeArray(Unity.Collections.NativeArray<ContactPairPoint> buffer)
        {
            int count = System.Math.Min(buffer.Length, contactCount);
            for (int i=0; i<count; i++) buffer[i] = GetContactPoint(i);
        }
        internal int ExtractContacts(System.Collections.Generic.List<ContactPoint> managedContainer, bool flipped)
        {
            managedContainer.Clear();
            for (int i=0; i<contactCount; i++) managedContainer.Add(ToLegacy(i, flipped));
            return contactCount;
        }
        internal int ExtractContactsArray(ContactPoint[] managedContainer, bool flipped)
        {
            int count=System.Math.Min(managedContainer.Length, contactCount);
            for (int i=0; i<count; i++) managedContainer[i]=ToLegacy(i, flipped);
            return count;
        }
        ContactPoint ToLegacy(int index, bool flipped)
        {
            ref readonly var p=ref GetContactPoint(index);
            return new ContactPoint(p.position, flipped ? -p.normal : p.normal, p.impulse, p.separation,
                flipped ? m_OtherColliderID : m_ColliderID, flipped ? m_ColliderID : m_OtherColliderID);
        }

        internal readonly int m_ColliderID;
        internal readonly int m_OtherColliderID;
        internal readonly global::System.IntPtr m_StartPtr;
        internal readonly uint m_NbPoints;
        internal readonly global::UnityEngine.CollisionPairFlags m_Flags;
        internal readonly global::UnityEngine.CollisionPairEventFlags m_Events;
        internal readonly global::UnityEngine.Vector3 m_ImpulseSum;
        public int colliderInstanceID => m_ColliderID;
        public int otherColliderInstanceID => m_OtherColliderID;
        public global::UnityEngine.Collider collider => Unbound.Runtime.ObjectRegistry.FindById(m_ColliderID) as Collider;
        public global::UnityEngine.Collider otherCollider => Unbound.Runtime.ObjectRegistry.FindById(m_OtherColliderID) as Collider;
        public int contactCount => (int)m_NbPoints;
        public global::UnityEngine.Vector3 impulseSum => m_ImpulseSum;
        public bool isCollisionEnter => (m_Events & CollisionPairEventFlags.NotifyTouchFound) != 0;
        public bool isCollisionExit => (m_Events & CollisionPairEventFlags.NotifyTouchLost) != 0;
        public bool isCollisionStay => (m_Events & CollisionPairEventFlags.NotifyTouchPersists) != 0;
        internal bool hasRemovedCollider => (m_Flags & (CollisionPairFlags.RemovedShape | CollisionPairFlags.RemovedOtherShape)) != 0;
        [global::System.Obsolete("Please use ContactPair.colliderInstanceID instead. (UnityUpgradable) -> colliderInstanceID")] public int ColliderInstanceID => colliderInstanceID;
        [global::System.Obsolete("Please use ContactPair.otherColliderInstanceID instead. (UnityUpgradable) -> otherColliderInstanceID")] public int OtherColliderInstanceID => otherColliderInstanceID;
        [global::System.Obsolete("Please use ContactPair.collider instead. (UnityUpgradable) -> collider")] public global::UnityEngine.Collider Collider => collider;
        [global::System.Obsolete("Please use ContactPair.otherCollider instead. (UnityUpgradable) -> otherCollider")] public global::UnityEngine.Collider OtherCollider => otherCollider;
        [global::System.Obsolete("Please use ContactPair.contactCount instead. (UnityUpgradable) -> contactCount")] public int ContactCount => contactCount;
        [global::System.Obsolete("Please use ContactPair.impulseSum instead. (UnityUpgradable) -> impulseSum")] public global::UnityEngine.Vector3 ImpulseSum => impulseSum;
        [global::System.Obsolete("Please use ContactPair.isCollisionEnter instead. (UnityUpgradable) -> isCollisionEnter")] public bool IsCollisionEnter => isCollisionEnter;
        [global::System.Obsolete("Please use ContactPair.isCollisionExit instead. (UnityUpgradable) -> isCollisionExit")] public bool IsCollisionExit => isCollisionExit;
        [global::System.Obsolete("Please use ContactPair.isCollisionStay instead. (UnityUpgradable) -> isCollisionStay")] public bool IsCollisionStay => isCollisionStay;
    }
    public unsafe readonly partial struct ContactPairHeader
    {
        internal ContactPairHeader(int a, int b, System.IntPtr pairs, uint count, CollisionPairHeaderFlags flags)
        { m_BodyID=a; m_OtherBodyID=b; m_StartPtr=pairs; m_NbPairs=count; m_Flags=flags; m_RelativeVelocity=default; }
        public ref readonly ContactPair GetContactPair(int index) => ref *GetContactPair_Internal(index);
        internal ContactPair* GetContactPair_Internal(int index)
        {
            if (index >= pairCount) throw new System.IndexOutOfRangeException("Invalid ContactPair index. Index should be greater than 0 and less than ContactPairHeader.PairCount");
            return (ContactPair*)m_StartPtr + index;
        }

        internal readonly int m_BodyID;
        internal readonly int m_OtherBodyID;
        internal readonly global::System.IntPtr m_StartPtr;
        internal readonly uint m_NbPairs;
        internal readonly global::UnityEngine.CollisionPairHeaderFlags m_Flags;
        internal readonly global::UnityEngine.Vector3 m_RelativeVelocity;
        public int bodyInstanceID => m_BodyID;
        public int otherBodyInstanceID => m_OtherBodyID;
        public global::UnityEngine.Component body => Unbound.Runtime.ObjectRegistry.FindById(m_BodyID) as Component;
        public global::UnityEngine.Component otherBody => Unbound.Runtime.ObjectRegistry.FindById(m_OtherBodyID) as Component;
        public int pairCount => (int)m_NbPairs;
        internal bool hasRemovedBody => m_Flags != 0;
        [global::System.Obsolete("Please use ContactPairHeader.bodyInstanceID instead. (UnityUpgradable) -> bodyInstanceID")] public int BodyInstanceID => bodyInstanceID;
        [global::System.Obsolete("Please use ContactPairHeader.otherBodyInstanceID instead. (UnityUpgradable) -> otherBodyInstanceID")] public int OtherBodyInstanceID => otherBodyInstanceID;
        [global::System.Obsolete("Please use ContactPairHeader.body instead. (UnityUpgradable) -> body")] public global::UnityEngine.Component Body => body;
        [global::System.Obsolete("Please use ContactPairHeader.otherBody instead. (UnityUpgradable) -> otherBody")] public global::UnityEngine.Component OtherBody => otherBody;
        [global::System.Obsolete("Please use ContactPairHeader.pairCount instead. (UnityUpgradable) -> pairCount")] public int PairCount => pairCount;
    }
    public unsafe readonly partial struct ContactPairPoint
    {
        internal readonly global::UnityEngine.Vector3 m_Position;
        internal readonly float m_Separation;
        internal readonly global::UnityEngine.Vector3 m_Normal;
        internal readonly uint m_InternalFaceIndex0;
        internal readonly global::UnityEngine.Vector3 m_Impulse;
        internal readonly uint m_InternalFaceIndex1;
        public global::UnityEngine.Vector3 position => m_Position;
        public float separation => m_Separation;
        public global::UnityEngine.Vector3 normal => m_Normal;
        public global::UnityEngine.Vector3 impulse => m_Impulse;
        [global::System.Obsolete("Please use ContactPairPoint.position instead. (UnityUpgradable) -> position")] public global::UnityEngine.Vector3 Position => position;
        [global::System.Obsolete("Please use ContactPairPoint.separation instead. (UnityUpgradable) -> separation")] public float Separation => separation;
        [global::System.Obsolete("Please use ContactPairPoint.normal instead. (UnityUpgradable) -> normal")] public global::UnityEngine.Vector3 Normal => normal;
        [global::System.Obsolete("Please use ContactPairPoint.impulse instead. (UnityUpgradable) -> impulse")] public global::UnityEngine.Vector3 Impulse => impulse;
    }
}
