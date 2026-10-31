using System;
using Unity.Collections;

namespace UnityEngine
{
    public partial class Physics
    {
        public static event ContactEventDelegate ContactEvent;
        public static PhysicsScene defaultPhysicsScene => default;
        internal static bool HasContactEventSubscribers => ContactEvent != null;

        internal static void SendContactEvent(NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            try { ContactEvent?.Invoke(defaultPhysicsScene, headers); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
    // This engine currently exposes only the default scene. Its default value is the
    // default scene in the installed Editor too, as recorded by contactevents3d.
    public partial struct PhysicsScene
    {
        public static bool operator ==(PhysicsScene lhs, PhysicsScene rhs) => lhs.Equals(rhs);
        public static bool operator !=(PhysicsScene lhs, PhysicsScene rhs) => !lhs.Equals(rhs);
        public bool Equals(PhysicsScene other) => true;
        public override bool Equals(object other) => other is PhysicsScene;
    }
}
