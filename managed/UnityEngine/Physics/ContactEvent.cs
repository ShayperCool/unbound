using System;
using Unity.Collections;

namespace UnityEngine
{
    public partial class Physics
    {
        public unsafe delegate void ContactEventDelegate(global::UnityEngine.PhysicsScene scene, global::Unity.Collections.NativeArray<global::UnityEngine.ContactPairHeader>.ReadOnly headerArray);
        public static event ContactEventDelegate ContactEvent;
        [Unbound.EditorFaithful("The measured default scene has index 0 and version 0, including default(PhysicsScene).") ]
        public static PhysicsScene defaultPhysicsScene => default;
        internal static uint TranslateTriangleIndexFromID(int instanceID, uint faceIndex)
        {
            if (faceIndex == uint.MaxValue) return faceIndex;
            var collider = Unbound.Runtime.ObjectRegistry.FindById(instanceID) as MeshCollider;
            return collider != null && collider.m_Shape != 0
                ? Unbound.Box3D.B3.ua3_shape_contact_face_index(collider.m_Shape, faceIndex) : uint.MaxValue;
        }
        internal static bool HasContactEventSubscribers => ContactEvent != null;

        internal static void SendContactEvent(NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            try { ContactEvent?.Invoke(defaultPhysicsScene, headers); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
    // This engine currently exposes only the default scene. Its default value is the
    // default scene in the installed Editor too, as recorded by contactevents3d.
    public partial struct PhysicsScene : IEquatable<PhysicsScene>
    {
        public static bool operator ==(PhysicsScene lhs, PhysicsScene rhs) => lhs.Equals(rhs);
        public static bool operator !=(PhysicsScene lhs, PhysicsScene rhs) => !lhs.Equals(rhs);
        private int m_index;
        private int m_version;
        public bool Equals(PhysicsScene other) => m_index == other.m_index && m_version == other.m_version;
        public bool IsValid() => m_index == 0 && m_version == 0;
        public override string ToString() => $"PhysicsScene(Index: {m_index}, Version: {m_version})";
        public override int GetHashCode() => base.GetHashCode();
        public override bool Equals(object other) => other is PhysicsScene scene && Equals(scene);
    }
}
