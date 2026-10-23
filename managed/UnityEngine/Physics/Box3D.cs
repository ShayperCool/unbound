using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unbound.Box3D
{
    /// <summary>
    /// The flat C API of native/unbound_box3d.c over Box3D (the unbound_box3d library next to this
    /// assembly). Worlds are uint ids, bodies and shapes ulong ids (0 is none); vectors and
    /// quaternions cross as Unity's Vector3 and Quaternion, whose layouts are Box3D's b3Vec3 and b3Quat.
    /// </summary>
    internal static unsafe class B3
    {
        const string Library = "unbound_box3d";

        /// <summary>The shim version this binding was written for.</summary>
        internal const int Version = 1;

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
        }

        /// <summary>Columns of a 3x3 matrix.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Matrix3
        {
            internal Vector3 X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Bounds3
        {
            internal Vector3 Lower, Upper;
        }

        [DllImport(Library)] internal static extern int ua3_version();

        // World.
        [DllImport(Library)] internal static extern uint ua3_world_create(Vector3 gravity, float restitutionThreshold, float hitEventThreshold, int enableSleep);
        [DllImport(Library)] internal static extern void ua3_world_destroy(uint world);
        [DllImport(Library)] internal static extern void ua3_world_step(uint world, float timeStep, int subStepCount);
        [DllImport(Library)] internal static extern void ua3_world_set_gravity(uint world, Vector3 gravity);
        [DllImport(Library)] internal static extern void ua3_world_set_restitution_threshold(uint world, float value);
        [DllImport(Library)] internal static extern void ua3_world_enable_sleeping(uint world, int flag);
        [DllImport(Library)] internal static extern int ua3_world_events(uint world, int kind, Pair* output, int capacity);
        [DllImport(Library)] internal static extern void ua3_ignore_pair(uint world, ulong a, ulong b, int ignore);

        // Bodies.
        [DllImport(Library)] internal static extern ulong ua3_body_create(uint world, in BodyDef def);
        [DllImport(Library)] internal static extern void ua3_body_destroy(ulong body);
        [DllImport(Library)] internal static extern int ua3_body_is_valid(ulong body);
        [DllImport(Library)] internal static extern void ua3_body_set_type(ulong body, int type);
        [DllImport(Library)] internal static extern void ua3_body_get_transform(ulong body, out Vector3 position, out Quaternion rotation);
        [DllImport(Library)] internal static extern void ua3_body_set_transform(ulong body, Vector3 position, Quaternion rotation);
        [DllImport(Library)] internal static extern void ua3_body_set_target_transform(ulong body, Vector3 position, Quaternion rotation, float timeStep);
        [DllImport(Library)] internal static extern Vector3 ua3_body_get_linear_velocity(ulong body);
        [DllImport(Library)] internal static extern void ua3_body_set_linear_velocity(ulong body, Vector3 v);
        [DllImport(Library)] internal static extern Vector3 ua3_body_get_angular_velocity(ulong body);
        [DllImport(Library)] internal static extern void ua3_body_set_angular_velocity(ulong body, Vector3 v);
        [DllImport(Library)] internal static extern Vector3 ua3_body_get_point_velocity(ulong body, Vector3 worldPoint);
        [DllImport(Library)] internal static extern Vector3 ua3_body_get_world_center(ulong body);
        [DllImport(Library)] internal static extern void ua3_body_apply(ulong body, int kind, Vector3 vector, Vector3 point);
        [DllImport(Library)] internal static extern void ua3_body_shape_mass(ulong body, out float mass, out Vector3 center, out Matrix3 inertia);
        [DllImport(Library)] internal static extern void ua3_body_set_mass(ulong body, float mass, Vector3 center, in Matrix3 inertia);
        [DllImport(Library)] internal static extern void ua3_body_set_damping(ulong body, float linear, float angular);
        [DllImport(Library)] internal static extern void ua3_body_set_gravity_scale(ulong body, float scale);
        [DllImport(Library)] internal static extern int ua3_body_is_awake(ulong body);
        [DllImport(Library)] internal static extern void ua3_body_set_awake(ulong body, int awake);
        [DllImport(Library)] internal static extern void ua3_body_enable_sleep(ulong body, int flag);
        [DllImport(Library)] internal static extern void ua3_body_set_locks(ulong body, int locks);
        [DllImport(Library)] internal static extern void ua3_body_set_bullet(ulong body, int flag);
        [DllImport(Library)] internal static extern void ua3_body_set_enabled(ulong body, int enabled);

        // Shapes.
        [DllImport(Library)] internal static extern ulong ua3_shape_sphere(ulong body, in ShapeDef def, Vector3 center, float radius);
        [DllImport(Library)] internal static extern ulong ua3_shape_capsule(ulong body, in ShapeDef def, Vector3 center1, Vector3 center2, float radius);
        [DllImport(Library)] internal static extern ulong ua3_shape_box(ulong body, in ShapeDef def, Vector3 center, Quaternion rotation, Vector3 halfExtents);
        [DllImport(Library)] internal static extern ulong ua3_shape_hull(ulong body, in ShapeDef def, Vector3* points, int count);
        [DllImport(Library)] internal static extern IntPtr ua3_mesh_create(Vector3* vertices, int vertexCount, int* indices, int triangleCount);
        [DllImport(Library)] internal static extern void ua3_mesh_destroy(IntPtr mesh);
        [DllImport(Library)] internal static extern ulong ua3_shape_mesh(ulong body, in ShapeDef def, IntPtr mesh, Vector3 scale);
        [DllImport(Library)] internal static extern void ua3_shape_destroy(ulong shape, int updateBodyMass);
        [DllImport(Library)] internal static extern int ua3_shape_is_valid(ulong shape);
        [DllImport(Library)] internal static extern int ua3_shape_is_sensor(ulong shape);
        [DllImport(Library)] internal static extern ulong ua3_shape_body(ulong shape);
        [DllImport(Library)] internal static extern void ua3_shape_set_material(ulong shape, float friction, float restitution, ulong material);
        [DllImport(Library)] internal static extern void ua3_shape_set_filter(ulong shape, ulong categoryBits, ulong maskBits);
        [DllImport(Library)] internal static extern void ua3_shape_set_density(ulong shape, float density);
        [DllImport(Library)] internal static extern void ua3_shape_aabb(ulong shape, out Bounds3 bounds);
        [DllImport(Library)] internal static extern Vector3 ua3_shape_closest_point(ulong shape, Vector3 target);
        [DllImport(Library)] internal static extern int ua3_shape_raycast(ulong shape, Vector3 origin, Vector3 translation, out Hit hit);
        [DllImport(Library)] internal static extern int ua3_shape_contacts(ulong shape, Contact* output, int capacity);

        // Queries.
        [DllImport(Library)] internal static extern int ua3_raycast(uint world, Vector3 origin, Vector3 translation, ulong maskBits, int hitTriggers, int closest, Hit* hits, int capacity);
        [DllImport(Library)] internal static extern int ua3_shapecast(uint world, int kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, Vector3 translation, ulong maskBits, int hitTriggers, int closest, Hit* hits, int capacity);
        [DllImport(Library)] internal static extern int ua3_overlap(uint world, int kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, ulong maskBits, int hitTriggers, ulong* shapes, int capacity);
        [DllImport(Library)] internal static extern int ua3_overlap_aabb(uint world, Vector3 lower, Vector3 upper, ulong maskBits, int hitTriggers, ulong* shapes, int capacity);
    }
}
