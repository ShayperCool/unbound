using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// Contact modification (6000.0 Scripting API: Physics.ContactModifyEvent, ContactModifyEventCCD,
// ModifiableContactPair, ModifiableMassProperties, Collider.hasModifiableContacts). The pairs are
// PhysX's own contact modification buffers: these structs mirror PxContactModifyPair,
// PxModifiableContact and PxContactPatch, and every edit writes those buffers, so the solver
// sees it. Measured against Unity 6000.0.71f1 Linux: tests/projects/contactmodify3d.
namespace UnityEngine
{
    [StructLayout(LayoutKind.Sequential)]
    internal partial struct ModifiableContact
    {
        public Vector3 contact;
        public float separation;
        public Vector3 targetVelocity;
        public float maxImpulse;
        public Vector3 normal;
        public float restitution;
        public uint materialFlags;
        public ushort materialIndex;
        public ushort otherMaterialIndex;
        public float staticFriction;
        public float dynamicFriction;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct ModifiableContactPatch
    {
        public ModifiableMassProperties massProperties;
        public Vector3 normal;
        public float restitution;
        public float dynamicFriction;
        public float staticFriction;
        public byte startContactIndex;
        public byte contactCount;
        public byte materialFlags;
        public byte internalFlags;
        public ushort materialIndex;
        public ushort otherMaterialIndex;

        public enum Flags
        {
            HasFaceIndices = 1,
            HasModifiedMassRatios = 8,
            HasTargetVelocity = 16,
            HasMaxImpulse = 32,
            RegeneratePatches = 64,
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct ModifiableMassProperties
    {
        public float inverseMassScale;
        public float inverseInertiaScale;
        public float otherInverseMassScale;
        public float otherInverseInertiaScale;
    }

    /// <summary>A view of one pair in a contact modification buffer. Valid only during the
    /// event; copies share the buffer, so edits through any copy apply.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial struct ModifiableContactPair
    {
        private IntPtr actor;
        private IntPtr otherActor;
        private IntPtr shape;
        private IntPtr otherShape;
        public Quaternion rotation;
        public Vector3 position;
        public Quaternion otherRotation;
        public Vector3 otherPosition;
        private int numContacts;
        private IntPtr contacts;

        public int colliderInstanceID => ResolveShapeToInstanceID(shape);
        public int otherColliderInstanceID => ResolveShapeToInstanceID(otherShape);
        public int bodyInstanceID => ResolveActorToInstanceID(actor);
        public int otherBodyInstanceID => ResolveActorToInstanceID(otherActor);
        public Vector3 bodyVelocity => GetActorLinearVelocity(actor);
        public Vector3 bodyAngularVelocity => GetActorAngularVelocity(actor);
        public Vector3 otherBodyVelocity => GetActorLinearVelocity(otherActor);
        public Vector3 otherBodyAngularVelocity => GetActorAngularVelocity(otherActor);
        public int contactCount => numContacts;

        public ModifiableMassProperties massProperties
        {
            get => GetContactPatch()->massProperties;
            set
            {
                var patch = GetContactPatch();
                patch->massProperties = value;
                patch->internalFlags |= (byte)ModifiableContactPatch.Flags.HasModifiedMassRatios;
            }
        }

        public Vector3 GetPoint(int i) => GetContact(i)->contact;
        public void SetPoint(int i, Vector3 v) => GetContact(i)->contact = v;

        public Vector3 GetNormal(int i) => GetContact(i)->normal;
        public void SetNormal(int i, Vector3 normal)
        {
            GetContact(i)->normal = normal;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.RegeneratePatches;
        }

        public float GetSeparation(int i) => GetContact(i)->separation;
        public void SetSeparation(int i, float separation) => GetContact(i)->separation = separation;

        public Vector3 GetTargetVelocity(int i) => GetContact(i)->targetVelocity;
        public void SetTargetVelocity(int i, Vector3 velocity)
        {
            GetContact(i)->targetVelocity = velocity;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.HasTargetVelocity;
        }

        public float GetBounciness(int i) => GetContact(i)->restitution;
        public void SetBounciness(int i, float bounciness)
        {
            GetContact(i)->restitution = bounciness;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.RegeneratePatches;
        }

        public float GetStaticFriction(int i) => GetContact(i)->staticFriction;
        public void SetStaticFriction(int i, float staticFriction)
        {
            GetContact(i)->staticFriction = staticFriction;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.RegeneratePatches;
        }

        public float GetDynamicFriction(int i) => GetContact(i)->dynamicFriction;
        public void SetDynamicFriction(int i, float dynamicFriction)
        {
            GetContact(i)->dynamicFriction = dynamicFriction;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.RegeneratePatches;
        }

        public float GetMaxImpulse(int i) => GetContact(i)->maxImpulse;
        public void SetMaxImpulse(int i, float value)
        {
            GetContact(i)->maxImpulse = value;
            GetContactPatch()->internalFlags |= (byte)ModifiableContactPatch.Flags.HasMaxImpulse;
        }

        /// <summary>A zero maximum impulse: the solver applies no force at the point.</summary>
        public void IgnoreContact(int i) => SetMaxImpulse(i, 0f);

        /// <summary>The triangle of the other collider's mesh at the point, in Mesh.triangles order;
        /// uint.MaxValue when the stream has no face indices (non-mesh pairs, CCD pairs).</summary>
        public uint GetFaceIndex(int i)
        {
            if ((GetContactPatch()->internalFlags & (byte)ModifiableContactPatch.Flags.HasFaceIndices) == 0)
                return uint.MaxValue;
            // The face indices follow the contacts: the first shape's, then the other shape's.
            var faces = (uint*)(contacts + numContacts * sizeof(ModifiableContact));
            return TranslateTriangleIndex(otherShape, faces[numContacts + CheckIndex(i)]);
        }

        int CheckIndex(int i)
        {
            if ((uint)i >= (uint)numContacts)
                throw new IndexOutOfRangeException($"Contact index {i} is out of range [0, {numContacts}).");
            return i;
        }

        ModifiableContact* GetContact(int i) => (ModifiableContact*)contacts + CheckIndex(i);

        // PhysX keeps one patch header per contact in front of the contacts; the pair's
        // patch-wide values (mass ratios, flags) are in the first.
        ModifiableContactPatch* GetContactPatch() => (ModifiableContactPatch*)contacts - numContacts;

        internal static uint TranslateTriangleIndex(IntPtr shapePtr, uint rawIndex) =>
            rawIndex == uint.MaxValue ? rawIndex : Unbound.Box3D.B3.ua3_shape_contact_face_index((ulong)shapePtr, rawIndex);

        internal static int ResolveShapeToInstanceID(IntPtr shapePtr)
        {
            var collider = Unbound.Runtime.PhysicsWorld.ColliderOf((ulong)shapePtr);
            return collider != null ? collider.GetInstanceID() : 0;
        }

        internal static int ResolveActorToInstanceID(IntPtr actorPtr) => Unbound.Runtime.PhysicsWorld.BodyInstanceId((ulong)actorPtr, false);

        internal static Vector3 GetActorLinearVelocity(IntPtr actorPtr)
        {
            Unbound.Box3D.B3.ua3_actor_velocity((ulong)actorPtr, out var linear, out _);
            return linear;
        }

        internal static Vector3 GetActorAngularVelocity(IntPtr actorPtr)
        {
            Unbound.Box3D.B3.ua3_actor_velocity((ulong)actorPtr, out _, out var angular);
            return angular;
        }
    }

    public partial class Physics
    {
        /// <summary>Discrete contact pairs with a collider that has modifiable contacts, in the
        /// narrow phase's batches: called during the simulation step, before its collision
        /// messages. Unity documents that it may run on any thread; Unbound runs it on the thread
        /// that steps the simulation.</summary>
        public static event Action<PhysicsScene, NativeArray<ModifiableContactPair>> ContactModifyEvent;

        /// <summary>The contact pairs that sweep CCD generates, one at a time (their normals point
        /// the other way, as Unity documents).</summary>
        public static event Action<PhysicsScene, NativeArray<ModifiableContactPair>> ContactModifyEventCCD;

        internal static unsafe void SendContactModify(PhysicsScene scene, IntPtr pairs, int count, bool ccd)
        {
            var handler = ccd ? ContactModifyEventCCD : ContactModifyEvent;
            if (handler == null) return;
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<ModifiableContactPair>((void*)pairs, count, Allocator.None);
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safety);
            try { handler(scene, array); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { AtomicSafetyHandle.Release(safety); }
        }
    }
}
