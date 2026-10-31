using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unbound.Box3D
{
    /// <summary>
    /// The flat C API of native/unbound_physx.cpp over PhysX 4.1, the engine Unity runs (the
    /// unbound_physx library next to this assembly; the API began over Box3D, hence the names).
    /// Worlds are uint ids, bodies and shapes ulong ids (0 is none); vectors and quaternions cross
    /// as Unity's Vector3 and Quaternion, whose layouts are PhysX's PxVec3 and PxQuat.
    /// </summary>
    internal static unsafe partial class B3
    {
        const string Library = "unbound_physx";

        /// <summary>The shim version this binding was written for.</summary>
        internal const int Version = 9;

        // The per-body accessors called every step are constant time, never block and never call
        // back into managed code: they skip the GC transition ([SuppressGCTransition]).

        [StructLayout(LayoutKind.Sequential)]
        internal struct ContactHeaderReport
        {
            internal ulong Actor0, Actor1;
            internal uint Start, Count;
            internal ushort Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ContactPairReport
        {
            internal ulong Shape0, Shape1;
            internal uint Start, Count;
            internal ushort Flags, Events;
            internal Vector3 ImpulseSum;
        }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ContactHeaderReport* ua3_world_contact_headers(uint world, int* count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ContactPairReport* ua3_world_contact_pairs(uint world);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ContactPairPoint* ua3_world_contact_points(uint world);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint ua3_shape_contact_face_index(ulong shape, uint faceIndex);

        internal enum BodyType
        {
            Static = 0,
            Kinematic = 1,
            Dynamic = 2,
        }

        /// <summary>What <see cref="ua3_body_apply"/> applies.</summary>
        internal enum Apply
        {
            ForceAtPoint = 0,
            Force = 1,
            Torque = 2,
            ImpulseAtPoint = 3,
            Impulse = 4,
            AngularImpulse = 5,
        }

        /// <summary>The query shapes of casts and overlaps.</summary>
        internal enum Proxy
        {
            Sphere = 0,
            Capsule = 1,
            Box = 2,
        }

        /// <summary>The event lists of <see cref="ua3_world_events"/>.</summary>
        internal enum Events
        {
            ContactBegin = 0,
            ContactEnd = 1,
            SensorBegin = 2,
            SensorEnd = 3,
            ContactPersist = 4,
        }

        /// <summary>Creation-only DynamicsManager settings. Enum values match Unity's serialized
        /// PGS/TGS, patch/one/two-directional friction, and kinematic pair-mode bits.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SceneDef
        {
            internal int SolverType, FrictionType, ContactPairsMode;
            internal int EnableEnhancedDeterminism, EnablePcm, ImprovedPatchFriction;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyDef
        {
            internal int Type;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 LinearVelocity;
            internal Vector3 AngularVelocity;
            internal float LinearDamping;
            internal float AngularDamping;
            internal float GravityScale;
            /// <summary>1 linear x, 2 linear y, 4 linear z, 8 angular x, 16 angular y, 32 angular z.</summary>
            internal int Locks;
            internal int EnableSleep;
            internal int IsAwake;
            internal int IsBullet;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ShapeDef
        {
            internal float Density;
            internal float Friction;
            internal float Restitution;
            /// <summary>Combine modes: bits 0-1 friction, 2-3 bounce (0 Average, 1 Minimum, 2 Multiply, 3 Maximum).</summary>
            internal ulong Material;
            internal ulong CategoryBits;
            internal ulong MaskBits;
            internal int IsSensor;
            internal int EnableSensorEvents;
            internal int EnableContactEvents;
            internal int UpdateBodyMass;
            internal float StaticFriction;
            /// <summary>The collider's contact offset; 0 for the default.</summary>
            internal float ContactOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Pair
        {
            internal ulong A, B;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Hit
        {
            internal ulong Shape;
            internal Vector3 Point;
            internal Vector3 Normal;
            internal float Fraction;
            internal int TriangleIndex;
            internal Vector2 UV;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Contact
        {
            internal ulong ShapeA, ShapeB;
            internal Vector3 Point;
            /// <summary>From shape A to shape B.</summary>
            internal Vector3 Normal;
            internal float Separation;
            internal float NormalImpulse;
            /// <summary>The impulse as PhysX reports it, whichever shape asks.</summary>
            internal Vector3 Impulse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ControllerDef
        {
            /// <summary>The capsule's centre.</summary>
            internal Vector3 Position;
            internal float Radius;
            /// <summary>Between the caps' centres.</summary>
            internal float Height;
            internal float ContactOffset;
            internal float StepOffset;
            /// <summary>The cosine of the steepest walkable slope.</summary>
            internal float SlopeLimit;
            /// <summary>0 easy, 1 constrained.</summary>
            internal int ClimbingMode;
            /// <summary>0 prevent climbing, 1 prevent climbing and force sliding.</summary>
            internal int NonWalkableMode;
            internal float Density;
            internal float ScaleCoeff;
            internal float VolumeGrowth;
            internal ulong CategoryBits;
            internal ulong MaskBits;
            internal float Friction;
            internal float Restitution;
            internal ulong Material;
            internal float StaticFriction;
            internal int DetectCollisions;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ControllerHit
        {
            internal ulong Shape;
            internal Vector3 Point;
            internal Vector3 Normal;
            internal Vector3 Direction;
            internal float Length;
        }

        /// <summary>Columns of a 3x3 matrix.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Matrix3
        {
            internal Vector3 X, Y, Z;
        }

        /// <summary>A body's pose and linear velocity (<c>ua3_bodies_state</c>).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyState
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 LinearVelocity;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Bounds3
        {
            internal Vector3 Lower, Upper;
        }

        [LibraryImport(Library)] internal static partial int ua3_version();
        [LibraryImport(Library)] internal static partial void ua3_settings(float contactOffset, float sleepThreshold, float maxDepenetration, float maxAngularVelocity, int positionIterations, int velocityIterations);

        // World.
        [LibraryImport(Library)] internal static partial uint ua3_world_create(Vector3 gravity, float restitutionThreshold, float hitEventThreshold, int enableSleep, in SceneDef settings);
        [LibraryImport(Library)] internal static partial void ua3_set_improved_patch_friction(int enabled);
        [LibraryImport(Library)] internal static partial void ua3_world_destroy(uint world);
        [LibraryImport(Library)] internal static partial void ua3_world_step(uint world, float timeStep, int subStepCount);
        [LibraryImport(Library)] internal static partial void ua3_world_set_gravity(uint world, Vector3 gravity);
        [LibraryImport(Library)] internal static partial void ua3_world_set_restitution_threshold(uint world, float value);
        [LibraryImport(Library)] internal static partial void ua3_world_enable_sleeping(uint world, int flag);
        [LibraryImport(Library)] internal static partial int ua3_world_events(uint world, int kind, Pair* output, int capacity);
        [LibraryImport(Library)] internal static partial void ua3_ignore_pair(uint world, ulong a, ulong b, int ignore);

        // Bodies.
        [LibraryImport(Library)] internal static partial ulong ua3_body_create(uint world, in BodyDef def);
        [LibraryImport(Library)] internal static partial void ua3_body_destroy(ulong body);
        [LibraryImport(Library), SuppressGCTransition] internal static partial int ua3_body_is_valid(ulong body);
        [LibraryImport(Library)] internal static partial void ua3_body_set_type(ulong body, int type);
        /// <summary>The pose and linear velocity of <paramref name="count"/> bodies.</summary>
        [LibraryImport(Library)] internal static partial void ua3_bodies_state(ulong* bodies, int count, BodyState* output);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_get_transform(ulong body, out Vector3 position, out Quaternion rotation);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_set_transform(ulong body, Vector3 position, Quaternion rotation);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_set_target_transform(ulong body, Vector3 position, Quaternion rotation, float timeStep);
        [LibraryImport(Library), SuppressGCTransition] internal static partial Vector3 ua3_body_get_linear_velocity(ulong body);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_set_linear_velocity(ulong body, Vector3 v);
        [LibraryImport(Library), SuppressGCTransition] internal static partial Vector3 ua3_body_get_angular_velocity(ulong body);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_set_angular_velocity(ulong body, Vector3 v);
        [LibraryImport(Library), SuppressGCTransition] internal static partial Vector3 ua3_body_get_point_velocity(ulong body, Vector3 worldPoint);
        [LibraryImport(Library), SuppressGCTransition] internal static partial Vector3 ua3_body_get_world_center(ulong body);
        [LibraryImport(Library), SuppressGCTransition] internal static partial void ua3_body_apply(ulong body, int kind, Vector3 vector, Vector3 point);
        [LibraryImport(Library)] internal static partial void ua3_body_shape_mass(ulong body, out float mass, out Vector3 center, out Matrix3 inertia);
        [LibraryImport(Library)] internal static partial void ua3_body_set_mass(ulong body, float mass, Vector3 center, in Matrix3 inertia);
        [LibraryImport(Library)] internal static partial void ua3_body_set_damping(ulong body, float linear, float angular);
        [LibraryImport(Library)] internal static partial void ua3_body_set_gravity_scale(ulong body, float scale);
        [LibraryImport(Library), SuppressGCTransition] internal static partial int ua3_body_is_awake(ulong body);
        [LibraryImport(Library)] internal static partial void ua3_body_set_awake(ulong body, int awake);
        [LibraryImport(Library)] internal static partial void ua3_body_enable_sleep(ulong body, int flag);
        [LibraryImport(Library)] internal static partial void ua3_body_set_locks(ulong body, int locks);
        [LibraryImport(Library)] internal static partial void ua3_body_set_bullet(ulong body, int flag);
        [LibraryImport(Library)] internal static partial void ua3_body_set_enabled(ulong body, int enabled);
        [LibraryImport(Library)] internal static partial void ua3_body_set_sleep_threshold(ulong body, float threshold);
        [LibraryImport(Library)] internal static partial void ua3_body_set_max_angular_velocity(ulong body, float value);
        [LibraryImport(Library)] internal static partial void ua3_body_set_max_depenetration(ulong body, float value);
        [LibraryImport(Library)] internal static partial void ua3_body_set_solver_iterations(ulong body, int position, int velocity);
        [LibraryImport(Library)] internal static partial void ua3_body_freeze_inertia(ulong body, int locks);

        // Shapes.
        [LibraryImport(Library)] internal static partial ulong ua3_shape_sphere(ulong body, in ShapeDef def, Vector3 center, float radius);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_capsule_geometry(ulong body, in ShapeDef def, Vector3 center, Quaternion rotation, float radius, float halfHeight);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_capsule(ulong body, in ShapeDef def, Vector3 center1, Vector3 center2, float radius);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_box(ulong body, in ShapeDef def, Vector3 center, Quaternion rotation, Vector3 halfExtents);
        [LibraryImport(Library)] internal static partial void ua3_shape_update_box(ulong shape, in ShapeDef def, Vector3 center, Quaternion rotation, Vector3 halfExtents);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_hull(ulong body, in ShapeDef def, Vector3* points, int count);
        [LibraryImport(Library)] internal static partial IntPtr ua3_mesh_create(Vector3* vertices, int vertexCount, int* indices, int triangleCount);
        [LibraryImport(Library)] internal static partial void ua3_mesh_destroy(IntPtr mesh);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_mesh(ulong body, in ShapeDef def, IntPtr mesh, Vector3 scale);
        /// <summary>A height field: rows along x, columns along z, Unity's 16-bit heights, a hole
        /// flag per cell (rows - 1 by columns - 1).</summary>
        [LibraryImport(Library)] internal static partial IntPtr ua3_height_field_samples(short* heights, byte* holes, int rows, int columns, int tessellate);
        [LibraryImport(Library)] internal static partial void ua3_height_field_destroy(IntPtr heightField);
        [LibraryImport(Library)] internal static partial ulong ua3_shape_height_field_scaled(ulong body, in ShapeDef def, IntPtr heightField, float heightScale, float rowScale, float columnScale, Vector3 offset);
        [LibraryImport(Library)] internal static partial void ua3_shape_destroy(ulong shape, int updateBodyMass);
        [LibraryImport(Library), SuppressGCTransition] internal static partial int ua3_shape_is_valid(ulong shape);
        [LibraryImport(Library)] internal static partial int ua3_shape_is_sensor(ulong shape);
        [LibraryImport(Library), SuppressGCTransition] internal static partial ulong ua3_shape_body(ulong shape);
        [LibraryImport(Library)] internal static partial void ua3_shape_set_material(ulong shape, float friction, float restitution, ulong material, float staticFriction);
        [LibraryImport(Library)] internal static partial void ua3_shape_set_contact_offset(ulong shape, float contactOffset);
        [LibraryImport(Library)] internal static partial void ua3_shape_set_filter(ulong shape, ulong categoryBits, ulong maskBits);
        [LibraryImport(Library)] internal static partial void ua3_shape_set_density(ulong shape, float density);
        [LibraryImport(Library)] internal static partial void ua3_shape_aabb(ulong shape, out Bounds3 bounds);
        [LibraryImport(Library)] internal static partial Vector3 ua3_shape_closest_point(ulong shape, Vector3 target);
        [LibraryImport(Library)] internal static partial int ua3_shape_raycast(ulong shape, Vector3 origin, Vector3 translation, out Hit hit);
        [LibraryImport(Library)] internal static partial int ua3_shape_contacts(ulong shape, Contact* output, int capacity);
        [LibraryImport(Library)] internal static partial int ua3_shapes_penetration(ulong a, Vector3 positionA, Quaternion rotationA, ulong b, Vector3 positionB, Quaternion rotationB, out Vector3 direction, out float depth);

        // Joints.
        [LibraryImport(Library)] internal static partial ulong ua3_joint_create(uint world, int kind, ulong body0, Vector3 position0, Quaternion rotation0, ulong body1, Vector3 position1, Quaternion rotation1);
        [LibraryImport(Library)] internal static partial void ua3_joint_destroy(ulong joint);
        [LibraryImport(Library)] internal static partial void ua3_joint_set_frames(ulong joint, Vector3 position0, Quaternion rotation0, Vector3 position1, Quaternion rotation1);
        [LibraryImport(Library)] internal static partial void ua3_joint_set_common(ulong joint, float breakForce, float breakTorque, int collision, int preprocessing, float massScale0, float massScale1);
        [LibraryImport(Library)] internal static partial void ua3_joint_force(ulong joint, out Vector3 force, out Vector3 torque);
        [LibraryImport(Library), SuppressGCTransition] internal static partial int ua3_joint_is_broken(ulong joint);
        [LibraryImport(Library)] internal static partial int ua3_world_broken_joints(uint world, ulong* joints, float* forces, int capacity);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_motion(ulong joint, int axis, int motion);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_linear_limit(ulong joint, float extent, float stiffness, float damping, float restitution, float bounceThreshold, float contactDistance);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_twist_limit(ulong joint, float lower, float upper, float stiffness, float damping, float restitution, float bounceThreshold, float contactDistance);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_swing_limit(ulong joint, float yAngle, float zAngle, float stiffness, float damping, float restitution, float bounceThreshold, float contactDistance);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_drive(ulong joint, int index, float stiffness, float damping, float forceLimit, int acceleration);
        [LibraryImport(Library)] internal static partial void ua3_d6_set_drive_target(ulong joint, Vector3 position, Quaternion rotation, Vector3 linear, Vector3 angular);
        [LibraryImport(Library)] internal static partial void ua3_d6_twist(ulong joint, out float angle, out float velocity);
        [LibraryImport(Library)] internal static partial void ua3_distance_set(ulong joint, float minDistance, float maxDistance, float tolerance, float stiffness, float damping);

        // Character controllers.
        [LibraryImport(Library)] internal static partial ulong ua3_cct_create(uint world, in ControllerDef def);
        [LibraryImport(Library)] internal static partial void ua3_cct_destroy(ulong controller);
        [LibraryImport(Library)] internal static partial ulong ua3_cct_shape(ulong controller);
        [LibraryImport(Library)] internal static partial void ua3_cct_set_filter(ulong controller, ulong categoryBits, ulong maskBits, int detectCollisions);
        [LibraryImport(Library)] internal static partial void ua3_cct_configure(ulong controller, float radius, float height, float contactOffset, float stepOffset, float slopeLimit);
        [LibraryImport(Library)] internal static partial void ua3_cct_set_position(ulong controller, Vector3 position);
        [LibraryImport(Library), SuppressGCTransition] internal static partial Vector3 ua3_cct_position(ulong controller);
        [LibraryImport(Library)] internal static partial int ua3_cct_move(ulong controller, Vector3 displacement, float minDistance, float elapsedTime, int overlapRecovery);
        [LibraryImport(Library)] internal static partial int ua3_cct_hits(ulong controller, ControllerHit* output, int capacity);

        // Queries.
        [LibraryImport(Library)] internal static partial int ua3_raycast(uint world, Vector3 origin, Vector3 translation, ulong maskBits, int hitTriggers, int closest, Hit* hits, int capacity);
        [LibraryImport(Library)] internal static partial int ua3_shapecast(uint world, int kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, Vector3 translation, ulong maskBits, int hitTriggers, int closest, Hit* hits, int capacity);
        [LibraryImport(Library)] internal static partial int ua3_overlap(uint world, int kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, ulong maskBits, int hitTriggers, ulong* shapes, int capacity);
        [LibraryImport(Library)] internal static partial int ua3_overlap_aabb(uint world, Vector3 lower, Vector3 upper, ulong maskBits, int hitTriggers, ulong* shapes, int capacity);
    }
}
