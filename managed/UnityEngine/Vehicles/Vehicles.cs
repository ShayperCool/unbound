using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using W = Unbound.Runtime.PhysicsWorld;

namespace Unbound.Runtime
{
    /// <summary>
    /// The vehicles WheelColliders make, as Unity has PhysX simulate them: every Rigidbody with
    /// wheels is a PxVehicleNoDrive (native/unbound_physx.cpp) whose suspension rays and update run
    /// before each physics step, with Unity's tire model; this side sets the wheels up from the
    /// components before the step and reads their state after it (measured:
    /// tests/projects/vehicles). Unity's set-up: the wheel's center is its position at full
    /// compression and targetPosition places the rest position along the travel; the suspension
    /// and the tire axes follow the body; the wheel object's scale scales radius and travel; forces
    /// apply at the resting wheel's base raised by forceAppPointDistance; 5 substeps below 5 m/s
    /// and 1 above by default; adding a wheel rebuilds the vehicle.
    /// </summary>
    internal static partial class Vehicles
    {
        static readonly Vector3 Up = Vector3.up, Right = Vector3.right;

        /// <summary>A body's vehicle.</summary>
        sealed class Vehicle
        {
            internal ulong Native;
            internal ulong Body;
            internal WheelCollider[] Wheels = Array.Empty<WheelCollider>();
            internal float Threshold = 5f;
            internal int Below = 5, Above = 1;
            // The mass and center of mass the sprung masses were shared for.
            internal float Mass = float.NaN;
            internal Vector3 Center;
        }

        static readonly ConditionalWeakTable<Rigidbody, Vehicle> s_Vehicles = new ConditionalWeakTable<Rigidbody, Vehicle>();
        static readonly List<Rigidbody> s_Bodies = new List<Rigidbody>();

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct WheelDef
        {
            internal Vector3 Centre, Travel, ForcePoint;
            internal float Radius, Mass, DampingRate;
            internal float Spring, Damper, MaxCompression, MaxDroop, SprungMass;
            internal fixed float Forward[5];
            internal fixed float Sideways[5];
            internal uint LayerMask;
            internal int Enabled;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WheelState
        {
            internal int Grounded;
            internal float Omega, Jounce, Steer, RotationAngle;
            internal ulong Shape;
            internal Vector3 Point, Normal;
            internal float Force;
            internal Vector3 ForwardDir, SidewaysDir;
            internal float ForwardSlip, SidewaysSlip;
            internal float RawOmega;
        }

        const string Library = "unbound_physx";
        [LibraryImport(Library)] private static partial ulong ua3_vehicle_create(ulong body, int wheels);
        [LibraryImport(Library)] private static partial void ua3_vehicle_destroy(ulong vehicle);
        [LibraryImport(Library)] private static unsafe partial void ua3_vehicle_configure(ulong vehicle, WheelDef* wheels, int count, float threshold, int below, int above, int expansionLimited);
        [LibraryImport(Library)] private static partial void ua3_vehicle_inputs(ulong vehicle, int index, float motorTorque, float brakeTorque, float steer);
        [LibraryImport(Library)] private static partial void ua3_vehicle_set_wheel_speed(ulong vehicle, int index, float omega);
        [LibraryImport(Library)] private static partial int ua3_vehicle_state(ulong vehicle, int index, out WheelState state);
        [LibraryImport(Library)] private static unsafe partial void ua3_sprung_masses(int count, Vector3* positions, Vector3 centre, float mass, float* masses);

        // ------------------------------------------------------------------ set-up

        static float Scale(WheelCollider w) => Mathf.Abs(w.transform.lossyScale.y);

        /// <summary>The wheel's position in the body's frame at full compression (its center).</summary>
        static Vector3 BodyLocalTop(WheelCollider w, Rigidbody rb)
        {
            var world = w.transform.TransformPoint(w.m_Center);
            return Quaternion.Inverse(rb.transform.rotation) * (world - rb.transform.position);
        }

        static void Travel(WheelCollider c, out float radius, out float maxCompression, out float maxDroop)
        {
            float scale = Scale(c);
            radius = Mathf.Max(1e-5f, c.m_Radius * scale);
            float travel = Mathf.Max(0f, c.m_SuspensionDistance * scale);
            float target = Mathf.Clamp01(c.m_SuspensionSpring.targetPosition);
            // The rest position sits targetPosition of the way from full extension to the center.
            maxCompression = travel * (1f - target);
            maxDroop = travel * target;
        }

        static Rigidbody BodyOf(WheelCollider w) => w.m_Registered ? W.FindRigidbody(w) : null;

        /// <summary>The body's wheels in component order, disabled ones too.</summary>
        static WheelCollider[] WheelsOf(Rigidbody rb) => rb.GetComponentsInChildren<WheelCollider>(true);

        static Vehicle VehicleOf(Rigidbody rb) => s_Vehicles.GetValue(rb, _ => new Vehicle());

        // ------------------------------------------------------------------ sprung masses

        internal static float SprungMass(WheelCollider w)
        {
            var rb = BodyOf(w);
            if (rb == null) return w.m_SprungMass >= 0f ? w.m_SprungMass : 0f;
            EnsureSprungMasses(rb, w);
            return w.m_SprungMass;
        }

        /// <summary>Shares the body's mass anew when a wheel needs it or the mass or the center of
        /// mass changed (measured: a disabled wheel's share is not redistributed).</summary>
        static void EnsureSprungMasses(Rigidbody rb, WheelCollider any)
        {
            var vehicle = VehicleOf(rb);
            float mass = rb.mass;
            var center = rb.centerOfMass;
            if (any.m_SprungMass >= 0f && mass == vehicle.Mass && center == vehicle.Center) return;
            vehicle.Mass = mass;
            vehicle.Center = center;
            ResetSprungMasses(any);
        }

        /// <summary>The sprung masses of the body's enabled wheels (PhysX's computeSprungMasses).</summary>
        internal static unsafe void ResetSprungMasses(WheelCollider any)
        {
            var rb = BodyOf(any);
            if (rb == null) return;
            var wheels = new List<WheelCollider>();
            foreach (var w in WheelsOf(rb)) if (w.m_Registered) wheels.Add(w);
            if (wheels.Count == 0) return;
            var positions = new Vector3[wheels.Count];
            for (int i = 0; i < wheels.Count; i++) positions[i] = BodyLocalTop(wheels[i], rb);
            var masses = new float[wheels.Count];
            fixed (Vector3* p = positions)
            fixed (float* m = masses)
                ua3_sprung_masses(wheels.Count, p, rb.centerOfMass, rb.mass, m);
            for (int i = 0; i < wheels.Count; i++) wheels[i].m_SprungMass = masses[i];
        }

        // ------------------------------------------------------------------ queries

        internal static void ConfigureSubsteps(WheelCollider w, float threshold, int below, int above)
        {
            var rb = BodyOf(w);
            if (rb == null) return;
            var v = VehicleOf(rb);
            v.Threshold = threshold;
            v.Below = Math.Max(1, below);
            v.Above = Math.Max(1, above);
        }

        internal static void SetRotationSpeed(WheelCollider w, float omega)
        {
            var rb = BodyOf(w);
            if (rb == null || !s_Vehicles.TryGetValue(rb, out var v) || v.Native == 0) return;
            int index = Array.IndexOf(v.Wheels, w);
            if (index >= 0) ua3_vehicle_set_wheel_speed(v.Native, index, omega);
        }

        internal static void WorldPose(WheelCollider c, out Vector3 position, out Quaternion rotation)
        {
            var rb = BodyOf(c);
            if (rb == null)
            {
                position = c.transform.TransformPoint(c.m_Center);
                rotation = c.transform.rotation;
                return;
            }
            var t = rb.transform;
            Travel(c, out _, out float maxCompression, out _);
            float jounce = c.m_Simulated ? c.m_Jounce : 0f;
            var local = BodyLocalTop(c, rb) - Up * maxCompression + Up * jounce;
            position = t.position + t.rotation * local;
            float steer = c.m_Simulated ? c.m_PoseSteer : 0f;
            var q3 = Quaternion.AngleAxis(steer * Mathf.Rad2Deg, Up);
            var q4 = Quaternion.AngleAxis(c.m_RotationAngle * Mathf.Rad2Deg, q3 * Right);
            rotation = t.rotation * (q4 * q3);
        }

        // ------------------------------------------------------------------ the step

        /// <summary>Before the world steps: every body with wheels gets its vehicle set up for the step.</summary>
        internal static void Update(float dt)
        {
            s_Bodies.Clear();
            var colliders = W.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                if (!(colliders[i] is WheelCollider w)) continue;
                var rb = W.FindRigidbody(w);
                if (rb == null || rb.m_Body == 0 || s_Bodies.Contains(rb)) continue;
                s_Bodies.Add(rb);
            }
            foreach (var rb in s_Bodies)
            {
                try { Prepare(rb); }
                catch (Exception e) { Debug.LogException(e, rb); }
            }
        }

        static unsafe void Prepare(Rigidbody rb)
        {
            var v = VehicleOf(rb);
            var wheels = WheelsOf(rb);
            bool same = v.Native != 0 && v.Body == rb.m_Body && wheels.Length == v.Wheels.Length;
            for (int i = 0; same && i < wheels.Length; i++) same = ReferenceEquals(wheels[i], v.Wheels[i]);
            if (!same)
            {
                // A new vehicle (Unity rebuilds it when its wheels change; a body that went took its
                // vehicle with it natively).
                if (v.Native != 0 && v.Body == rb.m_Body) ua3_vehicle_destroy(v.Native);
                v.Native = wheels.Length > 0 ? ua3_vehicle_create(rb.m_Body, wheels.Length) : 0;
                v.Body = rb.m_Body;
                v.Wheels = wheels;
                foreach (var w in wheels) w.m_Simulated = false;
                if (v.Native == 0) return;
            }

            int n = wheels.Length;
            WheelCollider registered = null;
            bool needMasses = false;
            foreach (var w in wheels)
            {
                if (!w.m_Registered) continue;
                registered ??= w;
                if (w.m_SprungMass < 0f) needMasses = true;
            }
            if (registered != null && (needMasses || rb.mass != v.Mass || rb.centerOfMass != v.Center)) EnsureSprungMasses(rb, registered);

            var cm = rb.centerOfMass;
            uint mask = unchecked((uint)Physics.s_LayerCollisionMatrix[rb.gameObject.layer & 31]);
            var defs = stackalloc WheelDef[n];
            bool expansionLimited = false;
            for (int i = 0; i < n; i++)
            {
                var c = wheels[i];
                Travel(c, out float radius, out float maxCompression, out float maxDroop);
                var centre = BodyLocalTop(c, rb) - Up * maxCompression - cm;
                WheelDef* d = defs + i;
                d->Centre = centre;
                d->Travel = -Up;
                d->ForcePoint = centre + Up * (c.m_ForceAppPointDistance - radius);
                d->Radius = radius;
                d->Mass = Mathf.Max(1e-5f, c.m_Mass);
                d->DampingRate = c.m_WheelDampingRate;
                d->Spring = c.m_SuspensionSpring.spring;
                d->Damper = c.m_SuspensionSpring.damper;
                d->MaxCompression = maxCompression;
                d->MaxDroop = maxDroop;
                d->SprungMass = Mathf.Max(1e-5f, c.m_SprungMass);
                Curve(d->Forward, c.m_ForwardFriction);
                Curve(d->Sideways, c.m_SidewaysFriction);
                d->LayerMask = mask;
                d->Enabled = c.m_Registered ? 1 : 0;
                expansionLimited |= c.m_SuspensionExpansionLimited;
            }
            ua3_vehicle_configure(v.Native, defs, n, v.Threshold, v.Below, v.Above, expansionLimited ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                var c = wheels[i];
                if (c.m_Registered) ua3_vehicle_inputs(v.Native, i, c.m_MotorTorque, c.m_BrakeTorque, c.m_SteerAngle * Mathf.Deg2Rad);
            }
        }

        static unsafe void Curve(float* into, WheelFrictionCurve f)
        {
            into[0] = f.extremumSlip;
            into[1] = f.extremumValue;
            into[2] = f.asymptoteSlip;
            into[3] = f.asymptoteValue;
            into[4] = f.stiffness;
        }

        /// <summary>After the world's step: the wheels' state.</summary>
        internal static void AfterStep()
        {
            foreach (var rb in s_Bodies)
            {
                if (rb == null || !s_Vehicles.TryGetValue(rb, out var v) || v.Native == 0) continue;
                for (int i = 0; i < v.Wheels.Length; i++)
                {
                    var c = v.Wheels[i];
                    if (c == null) continue;
                    bool simulated = ua3_vehicle_state(v.Native, i, out var s) != 0;
                    c.m_CorrectedOmega = s.Omega;
                    c.m_Omega = s.RawOmega;
                    c.m_RotationAngle = s.RotationAngle;
                    if (!simulated) continue;
                    c.m_Simulated = true;
                    c.m_Jounce = s.Jounce;
                    c.m_PoseSteer = s.Steer;
                    c.m_InAir = s.Grounded == 0;
                    c.m_Hit = new WheelHit
                    {
                        collider = s.Shape != 0 ? W.ColliderOf(s.Shape) : null,
                        point = s.Point,
                        normal = s.Normal,
                        force = s.Force,
                        forwardDir = s.ForwardDir,
                        sidewaysDir = s.SidewaysDir,
                        forwardSlip = s.ForwardSlip,
                        sidewaysSlip = s.SidewaysSlip,
                    };
                }
            }
        }
    }
}
