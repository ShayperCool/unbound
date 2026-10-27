using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unbound.Box3D;
using UnityEngine;
using W = Unbound.Runtime.PhysicsWorld;

namespace Unbound.Runtime
{
    /// <summary>
    /// The vehicles WheelColliders make, simulated as Unity has PhysX do it: every Rigidbody with
    /// wheels is a PxVehicleNoDrive updated before each physics step (PhysX 4.1's
    /// PxVehicleUpdate.cpp: updateNoDrive, processSuspTireWheels), with Unity's tire model and set-up
    /// (measured: tests/projects/vehicles). Each wheel casts a ray down the body's axis from the pose
    /// at the start of the step; the step is cut into substeps (5 below the vehicle's threshold of
    /// forward speed, 5 m/s, 1 above), each computing the suspension force (the sprung mass held up
    /// at zero jounce, plus spring and damper), the tire forces from the slips, and integrating the
    /// wheels' spin and the chassis' velocity; the chassis gets the resulting velocity, the solver
    /// then applies gravity and the vehicle's constraints (the suspension's compression limit,
    /// sticky tires at low speed).
    /// </summary>
    internal static class Vehicles
    {
        // PhysX's tolerances (length scale 1) and constants.
        const float ThresholdForwardSpeedForWheelAngleIntegration = 5f;
        const float MinLatSpeedForTireModel = 1f;
        const float StickyTireFrictionThresholdSpeed = 0.2f;
        const float MinLongSlipDenominator = 4f;
        const float StickyForwardDamping = 0.01f, StickySideDamping = 0.1f;
        const float LowForwardSpeedThresholdTime = 1f, LowSideSpeedThresholdTime = 1f;
        const float StickyConstraintDamping = 1000f;
        const float MinimumSlipThreshold = 1e-5f;
        // The tire load filter (PhysX's default).
        const float MinNormalisedLoad = 0f, MinFilteredNormalisedLoad = 0.2308f, MaxNormalisedLoad = 3f, MaxFilteredNormalisedLoad = 3f;
        // Unity's default solver iterations.
        const int PositionIterations = 6, VelocityIterations = 1;

        static readonly Vector3 Up = Vector3.up, Right = Vector3.right, Forward = Vector3.forward;

        /// <summary>A body's vehicle settings that outlive a step.</summary>
        sealed class Settings
        {
            internal float Threshold = 5f;
            internal int Below = 5, Above = 1;
            // The mass and center of mass the sprung masses were shared for.
            internal float Mass = float.NaN;
            internal Vector3 Center;
        }

        static readonly ConditionalWeakTable<Rigidbody, Settings> s_Settings = new ConditionalWeakTable<Rigidbody, Settings>();

        // ------------------------------------------------------------------ per-wheel set-up

        /// <summary>A wheel's PhysX data for this step: positions relative to the center of mass
        /// in body axes.</summary>
        struct Wheel
        {
            internal WheelCollider C;
            internal float Radius, MaxCompression, MaxDroop, Spring, Damper, SprungMass, RestLoad, Mass, RecipMoi, Damping;
            internal Vector3 CentreOffset;      // the wheel's centre at zero jounce
            internal Vector3 ForceOffset;       // where suspension and tire forces apply
            internal float Steer;
            // The ray cast at the start of the step.
            internal bool Hit;
            internal Vector4 Plane;
            internal float Distance;
            internal Collider HitCollider;
            internal Rigidbody HitBody;
            internal Vector3 HitPoint, HitNormal;
        }

        static float Scale(WheelCollider w)
        {
            var s = w.transform.lossyScale;
            return Mathf.Abs(s.y);
        }

        /// <summary>The wheel's position in the body's frame at full compression (its center).</summary>
        static Vector3 BodyLocalTop(WheelCollider w, Rigidbody rb)
        {
            var world = w.transform.TransformPoint(w.m_Center);
            return Quaternion.Inverse(rb.transform.rotation) * (world - rb.transform.position);
        }

        static void Setup(WheelCollider c, Rigidbody rb, Vector3 cm, out Wheel w)
        {
            float scale = Scale(c);
            float radius = Mathf.Max(1e-5f, c.m_Radius * scale);
            float travel = Mathf.Max(0f, c.m_SuspensionDistance * scale);
            float target = Mathf.Clamp01(c.m_SuspensionSpring.targetPosition);
            w = default;
            w.C = c;
            w.Radius = radius;
            // The rest position sits targetPosition of the way from full extension to the center
            // (full compression): it can compress the rest of the way and droop the target's share.
            w.MaxCompression = travel * (1f - target);
            w.MaxDroop = travel * target;
            w.Spring = c.m_SuspensionSpring.spring;
            w.Damper = c.m_SuspensionSpring.damper;
            w.Mass = Mathf.Max(1e-5f, c.m_Mass);
            w.RecipMoi = 1f / (0.5f * w.Mass * radius * radius);
            w.Damping = c.m_WheelDampingRate;
            w.CentreOffset = BodyLocalTop(c, rb) - Up * w.MaxCompression - cm;
            // Forces apply at the resting wheel's base, raised by forceAppPointDistance.
            w.ForceOffset = w.CentreOffset + Up * (c.m_ForceAppPointDistance - radius);
            w.Steer = c.m_SteerAngle * Mathf.Deg2Rad;
        }

        // ------------------------------------------------------------------ sprung masses

        static List<WheelCollider> WheelsOf(Rigidbody rb)
        {
            var list = new List<WheelCollider>();
            var colliders = W.Colliders;
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] is WheelCollider w && ReferenceEquals(W.FindRigidbody(w), rb)) list.Add(w);
            return list;
        }

        static Rigidbody BodyOf(WheelCollider w) => w.m_Registered ? W.FindRigidbody(w) : null;

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
            var settings = s_Settings.GetValue(rb, _ => new Settings());
            float mass = rb.mass;
            var center = rb.centerOfMass;
            if (any.m_SprungMass >= 0f && mass == settings.Mass && center == settings.Center) return;
            settings.Mass = mass;
            settings.Center = center;
            ResetSprungMasses(any);
        }

        /// <summary>The sprung masses of all the body's wheels from its mass and center of mass.</summary>
        internal static void ResetSprungMasses(WheelCollider any)
        {
            var rb = BodyOf(any);
            if (rb == null) return;
            var wheels = WheelsOf(rb);
            var coordinates = new Vector3[wheels.Count];
            for (int i = 0; i < wheels.Count; i++) coordinates[i] = BodyLocalTop(wheels[i], rb);
            var masses = ComputeSprungMasses(coordinates, rb.centerOfMass, rb.mass);
            for (int i = 0; i < wheels.Count; i++) wheels[i].m_SprungMass = masses[i];
        }

        /// <summary>PhysX's computeSprungMasses with gravity along y: one wheel carries it all, two
        /// split it along their line, three solve exactly, four and more take the masses closest
        /// to equal that balance the center of mass.</summary>
        static float[] ComputeSprungMasses(Vector3[] p, Vector3 cm, float total)
        {
            int n = p.Length;
            var m = new float[n];
            if (n == 0) return m;
            if (n == 1)
            {
                m[0] = total;
                return m;
            }
            if (n == 2)
            {
                var v = new Vector3(p[0].x, 0f, p[0].z);
                var w = new Vector3(p[1].x - p[0].x, 0f, p[1].z - p[0].z).normalized;
                var c = new Vector3(cm.x, 0f, cm.z);
                var q = v + w * Vector3.Dot(w, c - v);
                float r0 = Vector3.Dot(new Vector3(p[0].x, 0f, p[0].z) - q, w);
                float r1 = Vector3.Dot(new Vector3(p[1].x, 0f, p[1].z) - q, w);
                if (Mathf.Abs(r0 - r1) <= 1e-6f) { m[0] = m[1] = total * 0.5f; return m; }
                m[0] = total * r1 / (r1 - r0);
                m[1] = total - m[0];
                return m;
            }
            // d0 = z, d1 = x (the axes after gravity's).
            int size = n == 3 ? 3 : n + 3;
            var a = new double[size, size];
            var b = new double[size];
            for (int i = 0; i < n; i++)
            {
                a[0, i] = p[i].z;
                a[1, i] = p[i].x;
                a[2, i] = 1.0;
            }
            b[0] = total * cm.z;
            b[1] = total * cm.x;
            b[2] = total;
            if (n > 3)
            {
                double mbar = total / (double)n;
                for (int i = 0; i < n; i++)
                {
                    a[i + 3, i] = 2.0;
                    a[i + 3, n + 0] = p[i].z;
                    a[i + 3, n + 1] = p[i].x;
                    a[i + 3, n + 2] = 1.0;
                    b[i + 3] = 2 * mbar;
                }
            }
            var x = Solve(a, b);
            for (int i = 0; i < n; i++) m[i] = x == null ? total / n : (float)x[i];
            return m;
        }

        static double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = (double[,])a.Clone();
            var x = (double[])b.Clone();
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
                if (Math.Abs(m[pivot, col]) < 1e-12) return null;
                if (pivot != col)
                {
                    for (int k = 0; k < n; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                    (x[col], x[pivot]) = (x[pivot], x[col]);
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = m[r, col] / m[col, col];
                    if (f == 0) continue;
                    for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                    x[r] -= f * x[col];
                }
            }
            for (int i = 0; i < n; i++) x[i] /= m[i, i];
            return x;
        }

        // ------------------------------------------------------------------ queries

        internal static void ConfigureSubsteps(WheelCollider w, float threshold, int below, int above)
        {
            var rb = BodyOf(w);
            if (rb == null) return;
            var s = s_Settings.GetValue(rb, _ => new Settings());
            s.Threshold = threshold;
            s.Below = Math.Max(1, below);
            s.Above = Math.Max(1, above);
        }

        internal static void WorldPose(WheelCollider c, out Vector3 position, out Quaternion rotation)
        {
            var rb = BodyOf(c);
            var t = rb != null ? rb.transform : c.transform;
            Vector3 bodyPosition = t.position;
            Quaternion bodyRotation = t.rotation;
            if (rb != null)
            {
                float scale = Scale(c);
                float travel = Mathf.Max(0f, c.m_SuspensionDistance * scale);
                float maxCompression = travel * (1f - Mathf.Clamp01(c.m_SuspensionSpring.targetPosition));
                float jounce = c.m_Simulated ? c.m_Jounce : 0f;
                var local = BodyLocalTop(c, rb) - Up * maxCompression + Up * jounce;
                position = bodyPosition + bodyRotation * local;
                float steer = c.m_Simulated ? c.m_PoseSteer : 0f;
                var q3 = Quaternion.AngleAxis(steer * Mathf.Rad2Deg, Up);
                var q4 = Quaternion.AngleAxis(c.m_RotationAngle * Mathf.Rad2Deg, q3 * Right);
                rotation = bodyRotation * (q4 * q3);
                return;
            }
            position = c.transform.TransformPoint(c.m_Center);
            rotation = c.transform.rotation;
        }

        // ------------------------------------------------------------------ the update

        /// <summary>Updates every vehicle for a step of <paramref name="dt"/>, before the world steps.</summary>
        internal static void Update(float dt)
        {
            var colliders = W.Colliders;
            List<Rigidbody> bodies = null;
            for (int i = 0; i < colliders.Count; i++)
            {
                if (!(colliders[i] is WheelCollider w)) continue;
                var rb = W.FindRigidbody(w);
                if (rb == null || rb.m_Body == 0) continue;
                bodies ??= new List<Rigidbody>();
                if (!bodies.Contains(rb)) bodies.Add(rb);
            }
            if (bodies == null) return;
            foreach (var rb in bodies)
            {
                try { UpdateVehicle(rb, WheelsOf(rb), dt); }
                catch (Exception e) { Debug.LogException(e, rb); }
            }
        }

        struct Row
        {
            internal int Wheel, Kind;           // the wheel; 0 limit, 1 sticky forward, 2 sticky side
            internal Vector3 Linear, Angular;   // world
            internal float Target;              // velocity target (sticky) or bias (limit)
            internal bool Limit;                // a suspension limit (impulse ≤ 0) or a sticky tire
            internal float Impulse, Response;
        }

        static void UpdateVehicle(Rigidbody rb, List<WheelCollider> colliders, float dt)
        {
            ulong body = rb.m_Body;
            var gravity = Physics.s_Gravity;
            float gravityMagnitude = gravity.magnitude;
            float recipGravityMagnitude = gravityMagnitude > 0f ? 1f / gravityMagnitude : 0f;
            bool kinematic = rb.m_IsKinematic;

            int n = colliders.Count;
            float motor = 0f, maxBrake = 0f;
            bool finiteInput = false;
            foreach (var c in colliders)
            {
                motor = Mathf.Max(motor, Mathf.Abs(c.m_MotorTorque));
                maxBrake = Mathf.Max(maxBrake, Mathf.Abs(c.m_BrakeTorque));
                if (c.m_MotorTorque != 0f || c.m_SteerAngle != 0f) finiteInput = true;
            }

            // Sleeping vehicles stay asleep unless driven; their wheels stop.
            if (!kinematic && B3.ua3_body_is_awake(body) == 0)
            {
                if (finiteInput) B3.ua3_body_set_awake(body, 1);
                else
                {
                    foreach (var c in colliders)
                    {
                        c.m_Omega = c.m_CorrectedOmega = 0f;
                        c.m_PrevJounce = float.MaxValue;
                    }
                    return;
                }
            }

            B3.ua3_body_get_transform(body, out var bodyPosition, out var bodyRotation);
            var cm = rb.centerOfMass;
            float chassisMass = Mathf.Max(1e-7f, rb.mass);
            float inverseMass = 1f / chassisMass;
            var principal = rb.inertiaTensor;
            var inverseInertia = new Vector3(principal.x > 0f ? 1f / principal.x : 0f, principal.y > 0f ? 1f / principal.y : 0f, principal.z > 0f ? 1f / principal.z : 0f);
            var linVel = kinematic ? Vector3.zero : B3.ua3_body_get_linear_velocity(body);
            var angVel = kinematic ? Vector3.zero : B3.ua3_body_get_angular_velocity(body);
            var chassisPosition = bodyPosition + bodyRotation * cm;
            var chassisRotation = bodyRotation;

            var wheels = new Wheel[n];
            int needy = 0;
            for (int i = 0; i < n; i++) if (colliders[i].m_SprungMass < 0f) needy = i;
            EnsureSprungMasses(rb, colliders[needy]);
            for (int i = 0; i < n; i++)
            {
                Setup(colliders[i], rb, cm, out wheels[i]);
                wheels[i].SprungMass = colliders[i].m_SprungMass;
                wheels[i].RestLoad = wheels[i].SprungMass + wheels[i].Mass;
            }

            // The suspension rays, from the pose at the start of the step.
            int mask = Physics.s_LayerCollisionMatrix[rb.gameObject.layer & 31];
            for (int i = 0; i < n; i++) Cast(ref wheels[i], rb, chassisPosition, chassisRotation, mask);

            bool intentionToAccelerate = motor > 0f && maxBrake == 0f;
            var settings = s_Settings.GetValue(rb, _ => new Settings());
            float forwardSpeed = Mathf.Abs(Vector3.Dot(linVel, chassisRotation * Forward));
            int substeps = forwardSpeed < settings.Threshold ? settings.Below : settings.Above;
            float timeFraction = 1f / substeps;
            float subDt = dt * timeFraction;
            float recipSubDt = 1f / subDt;

            var origLinVel = linVel;
            var origAngVel = angVel;
            var rows = new List<Row>();
            var hitForces = new Vector3[n];
            var inAir = new bool[n];
            var jounces = new float[n];
            var forwardSpeeds = new float[n];
            var results = new WheelHit[n];

            for (int k = 0; k < substeps; k++)
            {
                var chassisForce = Vector3.zero;
                var chassisTorque = Vector3.zero;
                var latDir = chassisRotation * Right;
                for (int i = 0; i < n; i++)
                {
                    ref var w = ref wheels[i];
                    var c = w.C;
                    float jounce = -w.MaxDroop;
                    bool air = true;
                    float tireTorque = 0f;
                    float forward = 0f;
                    var suspDir = chassisRotation * -Up;
                    bool brakeApplied = c.m_BrakeTorque != 0f;
                    bool accelApplied = intentionToAccelerate && c.m_MotorTorque != 0f;
                    var hit = default(WheelHit);
                    var hitNormal = new Vector3(w.Plane.x, w.Plane.y, w.Plane.z);
                    if (w.Hit && w.Distance != 0f && Vector3.Dot(hitNormal, suspDir) < 0f
                        && IntersectRayPlane(chassisPosition, chassisRotation, w, out float dx, out var wheelBottom)
                        && dx > -w.MaxDroop)
                    {
                        jounce = Mathf.Min(dx, w.MaxCompression);
                        if (k == 0 && dx > w.MaxCompression)
                        {
                            // The suspension's compression limit, solved with the step.
                            float error = Vector3.Dot(suspDir, hitNormal) * (-dx + w.MaxCompression);
                            var dir = chassisRotation * -Up;
                            var offset = chassisRotation * w.CentreOffset;
                            rows.Add(new Row { Wheel = i, Kind = 0, Linear = dir, Angular = Vector3.Cross(offset, dir), Target = -error / dt, Limit = true });
                        }
                        var r = wheelBottom - chassisPosition;
                        var bottomVel = linVel + Vector3.Cross(angVel, r);
                        var hitVel = Vector3.zero;
                        if (w.HitBody != null && !w.HitBody.m_IsKinematic && w.HitBody.m_Body != 0)
                        {
                            hitVel = B3.ua3_body_get_point_velocity(w.HitBody.m_Body, wheelBottom);
                            bottomVel -= hitVel;
                        }
                        float jounceSpeed, previousJounce;
                        if (c.m_PrevJounce != float.MaxValue)
                        {
                            jounceSpeed = (jounce - c.m_PrevJounce) * recipSubDt;
                            previousJounce = c.m_PrevJounce;
                        }
                        else
                        {
                            jounceSpeed = 0f;
                            previousJounce = jounce;
                        }
                        float gravitySuspDir = Vector3.Dot(gravity, suspDir);
                        bool computeForce = true;
                        if (c.m_SuspensionExpansionLimited && jounceSpeed < 0f && jounce <= 0f)
                        {
                            float distToMaxDroop = previousJounce + w.MaxDroop;
                            float springAlong = distToMaxDroop * w.Spring;
                            float wheelVel = (springAlong / w.Mass + gravitySuspDir) * subDt;
                            if (jounceSpeed < -wheelVel)
                            {
                                computeForce = false;
                                jounce = Mathf.Min(previousJounce - wheelVel * subDt, w.MaxCompression);
                            }
                        }
                        if (computeForce)
                        {
                            air = false;
                            float alpha = Mathf.Max(0f, gravitySuspDir);
                            var tTimesBeta = alpha != 0f ? gravity - suspDir * alpha : Vector3.zero;
                            float forceW = Mathf.Max(0f, w.SprungMass * alpha + w.Spring * jounce) + jounceSpeed * w.Damper;
                            float forceMag = Vector3.Dot(hitNormal, -suspDir * forceW - tTimesBeta * w.SprungMass);
                            if (w.HitBody != null && !w.HitBody.m_IsKinematic)
                                hitForces[i] += hitNormal * (-forceMag * timeFraction);
                            var suspForce = hitNormal * forceMag;
                            var forceOffset = chassisRotation * w.ForceOffset;
                            chassisForce += suspForce;
                            chassisTorque += Vector3.Cross(forceOffset, suspForce);

                            float tireLoad = forceMag;
                            float normalised = tireLoad * recipGravityMagnitude / w.RestLoad;
                            float filtered = FilteredLoad(normalised);
                            float filteredLoad = filtered * gravityMagnitude * w.RestLoad;

                            TireDirs(latDir, hitNormal, w.Steer, out var longDir, out var lateralDir);
                            float longSpeed = Vector3.Dot(bottomVel, longDir);
                            float latSpeed = Vector3.Dot(bottomVel, lateralDir);
                            forward = longSpeed;
                            TireSlips(longSpeed, latSpeed, c.m_Omega, w.Radius, accelApplied, brakeApplied, out float longSlip, out float latSlip);

                            if (filteredLoad > 0f)
                            {
                                // Low speed timers and sticky tires.
                                if (Mathf.Abs(longSpeed) < StickyTireFrictionThresholdSpeed && Mathf.Abs(c.m_Omega) < StickyTireFrictionThresholdSpeed / w.Radius && !intentionToAccelerate)
                                    c.m_LowForwardTimer += subDt;
                                else c.m_LowForwardTimer = 0f;
                                bool stickyForward = (Mathf.Abs(longSpeed) < StickyTireFrictionThresholdSpeed && c.m_Omega == 0f && !intentionToAccelerate)
                                    || c.m_LowForwardTimer > LowForwardSpeedThresholdTime;
                                if (Mathf.Abs(latSpeed) < StickyTireFrictionThresholdSpeed && !intentionToAccelerate) c.m_LowSideTimer += subDt;
                                else c.m_LowSideTimer = 0f;
                                bool stickySide = c.m_LowForwardTimer > 0f && c.m_LowSideTimer > LowSideSpeedThresholdTime;
                                var tireOffset = chassisRotation * w.ForceOffset;
                                if (stickyForward)
                                {
                                    longSlip = 0f;
                                    if (k == 0) rows.Add(new Row { Wheel = i, Kind = 1, Linear = longDir, Angular = Vector3.Cross(tireOffset, longDir), Target = longSpeed * StickyForwardDamping + Vector3.Dot(hitVel, longDir) });
                                }
                                if (stickySide)
                                {
                                    latSlip = 0f;
                                    if (k == 0) rows.Add(new Row { Wheel = i, Kind = 2, Linear = lateralDir, Angular = Vector3.Cross(tireOffset, lateralDir), Target = latSpeed * StickySideDamping + Vector3.Dot(hitVel, lateralDir) });
                                }

                                TireForces(c, longSlip, latSlip, filteredLoad, out float longForce, out float latForce);
                                tireTorque = -longForce * w.Radius;
                                var tireForce = longDir * longForce + lateralDir * latForce;
                                chassisForce += tireForce;
                                chassisTorque += Vector3.Cross(tireOffset, tireForce);
                            }

                            hit.collider = w.HitCollider;
                            hit.point = w.HitPoint;
                            hit.normal = w.HitNormal;
                            hit.force = forceMag;
                            hit.forwardDir = longDir;
                            hit.sidewaysDir = lateralDir;
                            hit.forwardSlip = longSlip;
                            hit.sidewaysSlip = latSlip;
                        }
                    }
                    jounces[i] = jounce;
                    inAir[i] = air;
                    forwardSpeeds[i] = forward;
                    results[i] = hit;
                    c.m_PrevJounce = jounce;

                    // The wheel's spin: drive, brake and tire torques, damping implicit.
                    float subMoi = subDt * w.RecipMoi;
                    float brakeTorque = brakeApplied ? -Sign(c.m_Omega) * c.m_BrakeTorque : 0f;
                    float omega = (c.m_Omega + subMoi * (tireTorque + c.m_MotorTorque + brakeTorque)) / (1f + w.Damping * subMoi);
                    if (brakeApplied && c.m_Omega * omega <= 0f) omega = 0f;
                    c.m_Omega = omega;

                    // Its rotation angle, blended with the rolling speed when it rolls freely and slowly.
                    float corrected = omega;
                    if (jounce > -w.MaxDroop && !brakeApplied && c.m_MotorTorque == 0f && Mathf.Abs(forward) < ThresholdForwardSpeedForWheelAngleIntegration)
                    {
                        float blend = Mathf.Abs(forward) / ThresholdForwardSpeedForWheelAngleIntegration;
                        corrected = forward / w.Radius * (1f - blend) + omega * blend;
                    }
                    float angle = c.m_RotationAngle + corrected * subDt;
                    if (angle > 10f * Mathf.PI) angle -= 10f * Mathf.PI;
                    else if (angle < -10f * Mathf.PI) angle += 10f * Mathf.PI;
                    c.m_RotationAngle = angle;
                    c.m_CorrectedOmega = corrected;
                }

                // The chassis, integrated by the substep (frozen axes stay still).
                linVel += chassisForce * (inverseMass * subDt);
                angVel += InverseInertiaWorld(chassisRotation, inverseInertia, chassisTorque * subDt);
                Freeze(rb.m_Constraints, ref linVel, ref angVel);
                chassisPosition += linVel * subDt;
                var spin = new Quaternion(angVel.x, angVel.y, angVel.z, 0f) * chassisRotation;
                float h = subDt * 0.5f;
                chassisRotation = Quaternion.Normalize(new Quaternion(chassisRotation.x + spin.x * h, chassisRotation.y + spin.y * h, chassisRotation.z + spin.z * h, chassisRotation.w + spin.w * h));
            }

            for (int i = 0; i < n; i++)
            {
                var c = colliders[i];
                c.m_Simulated = true;
                c.m_Jounce = jounces[i];
                c.m_InAir = inAir[i];
                c.m_Hit = results[i];
                c.m_PoseSteer = wheels[i].Steer;
            }

            if (kinematic) return;

            // The new velocity, then the solver's work on it: gravity and the constraints. The body
            // moves with the velocity of the position iterations and keeps the one after the
            // velocity iterations.
            // PhysX's rows: per block of four wheels, the limits, then the sticky forward rows,
            // then the sticky side rows (Gauss-Seidel follows this order).
            rows.Sort((x, y) => x.Wheel / 4 != y.Wheel / 4 ? (x.Wheel / 4).CompareTo(y.Wheel / 4)
                : x.Kind != y.Kind ? x.Kind.CompareTo(y.Kind) : x.Wheel.CompareTo(y.Wheel));
            var g = rb.m_UseGravity ? gravity * dt : Vector3.zero;
            var v = linVel + g;
            var omegaWorld = angVel;
            Solve(rows, ref v, ref omegaWorld, inverseMass, bodyRotation, inverseInertia, dt, out var movedV, out var movedW);
            Freeze(rb.m_Constraints, ref v, ref omegaWorld);
            Freeze(rb.m_Constraints, ref movedV, ref movedW);
            if (movedV != v || movedW != omegaWorld) s_AfterStep.Add((rb, v, omegaWorld));
            v = movedV;
            omegaWorld = movedW;
            B3.ua3_body_set_linear_velocity(body, v - g);
            B3.ua3_body_set_angular_velocity(body, omegaWorld);
            if (origLinVel != v - g || origAngVel != omegaWorld) B3.ua3_body_set_awake(body, 1);

            for (int i = 0; i < n; i++)
            {
                var other = wheels[i].HitBody;
                if (other != null && hitForces[i] != Vector3.zero) other.AddForceAtPosition(hitForces[i], wheels[i].HitPoint, ForceMode.Force);
            }
        }

        static void Freeze(RigidbodyConstraints constraints, ref Vector3 v, ref Vector3 w)
        {
            if (constraints == RigidbodyConstraints.None) return;
            if ((constraints & RigidbodyConstraints.FreezePositionX) != 0) v.x = 0f;
            if ((constraints & RigidbodyConstraints.FreezePositionY) != 0) v.y = 0f;
            if ((constraints & RigidbodyConstraints.FreezePositionZ) != 0) v.z = 0f;
            if ((constraints & RigidbodyConstraints.FreezeRotationX) != 0) w.x = 0f;
            if ((constraints & RigidbodyConstraints.FreezeRotationY) != 0) w.y = 0f;
            if ((constraints & RigidbodyConstraints.FreezeRotationZ) != 0) w.z = 0f;
        }

        static float Sign(float f) => f > 0f ? 1f : (f < 0f ? -1f : 0f);

        static Vector3 InverseInertiaWorld(Quaternion rotation, Vector3 inverseInertia, Vector3 v)
        {
            var local = Quaternion.Inverse(rotation) * v;
            return rotation * Vector3.Scale(inverseInertia, local);
        }

        /// <summary>The constraints' rows on the chassis' velocity, Gauss-Seidel: suspension limits
        /// push at most until the compression's error is gone; sticky tires reach a/(1+a) of the way
        /// to their target speed (PhysX's acceleration spring, damping 1000).</summary>
        static readonly List<(Rigidbody Body, Vector3 Linear, Vector3 Angular)> s_AfterStep = new List<(Rigidbody, Vector3, Vector3)>();

        /// <summary>After the world's step: the bodies whose suspension limits corrected their
        /// position get the velocity without that correction.</summary>
        internal static void AfterStep()
        {
            foreach (var (rb, v, w) in s_AfterStep)
            {
                if (rb == null || rb.m_Body == 0) continue;
                B3.ua3_body_set_linear_velocity(rb.m_Body, v);
                B3.ua3_body_set_angular_velocity(rb.m_Body, w);
            }
            s_AfterStep.Clear();
        }

        /// <summary>PhysX's PGS on the vehicle's rows (DyConstraintSetup setSolverConstants,
        /// solve1D, conclude1D): position iterations with the limits' error as a bias, the pose
        /// integrated with that velocity, then velocity iterations without the bias, each row's
        /// accumulated impulse carried over. Sticky tires are acceleration springs (damping 1000).
        /// Returns the velocity the pose moves with; <paramref name="v"/> and <paramref name="w"/>
        /// end as the velocity the body keeps.</summary>
        static void Solve(List<Row> rows, ref Vector3 v, ref Vector3 w, float inverseMass, Quaternion rotation, Vector3 inverseInertia, float dt,
            out Vector3 movedLinear, out Vector3 movedAngular)
        {
            movedLinear = v;
            movedAngular = w;
            if (rows.Count == 0) return;
            int n = rows.Count;
            var constant = new float[n];
            var unbiased = new float[n];
            var velMul = new float[n];
            var impMul = new float[n];
            var applied = new float[n];
            var angularResponse = new Vector3[n];
            float a = dt * StickyConstraintDamping;
            float x = 1f / (1f + a);
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                angularResponse[i] = InverseInertiaWorld(rotation, inverseInertia, r.Angular);
                float response = inverseMass * Vector3.Dot(r.Linear, r.Linear) + Vector3.Dot(r.Angular, angularResponse[i]);
                float recip = response <= 0f ? 0f : 1f / response;
                if (r.Limit)
                {
                    velMul[i] = -recip;
                    impMul[i] = 1f;
                    constant[i] = recip * r.Target;     // velocity target 0 - geometric error / dt
                    unbiased[i] = 0f;
                }
                else
                {
                    // b = dt * damping * target = a * target.
                    constant[i] = unbiased[i] = x * recip * a * r.Target;
                    velMul[i] = -x * recip * a;
                    impMul[i] = 1f - x;
                }
            }
            for (int pass = 0; pass < PositionIterations + VelocityIterations; pass++)
            {
                if (pass == PositionIterations)
                {
                    movedLinear = v;
                    movedAngular = w;
                }
                var constants = pass < PositionIterations ? constant : unbiased;
                for (int i = 0; i < n; i++)
                {
                    var r = rows[i];
                    float normalVel = Vector3.Dot(r.Linear, v) + Vector3.Dot(r.Angular, w);
                    float unclamped = impMul[i] * applied[i] + velMul[i] * normalVel + constants[i];
                    float clamped = r.Limit ? Mathf.Min(0f, unclamped) : unclamped;
                    float delta = clamped - applied[i];
                    applied[i] = clamped;
                    v += r.Linear * (delta * inverseMass);
                    w += angularResponse[i] * delta;
                }
            }
        }

        static float FilteredLoad(float normalised)
        {
            if (normalised <= MinNormalisedLoad) return MinFilteredNormalisedLoad;
            if (normalised >= MaxNormalisedLoad) return MaxFilteredNormalisedLoad;
            return MinFilteredNormalisedLoad + (normalised - MinNormalisedLoad) * (MaxFilteredNormalisedLoad - MinFilteredNormalisedLoad) / (MaxNormalisedLoad - MinNormalisedLoad);
        }

        static void TireDirs(Vector3 chassisLatDir, Vector3 hitNormal, float steer, out Vector3 longDir, out Vector3 latDir)
        {
            var tz = Vector3.Cross(chassisLatDir, hitNormal);
            var tx = Vector3.Cross(hitNormal, tz);
            tz = tz.normalized;
            tx = tx.normalized;
            float cos = Mathf.Cos(steer), sin = Mathf.Sin(steer);
            longDir = tz * cos + tx * sin;
            latDir = tx * cos - tz * sin;
        }

        static void TireSlips(float longSpeed, float latSpeed, float omega, float radius, bool accel, bool brake, out float longSlip, out float latSlip)
        {
            float longSpeedAbs = Mathf.Abs(longSpeed);
            float wheelSpeed = omega * radius;
            float wheelSpeedAbs = Mathf.Abs(wheelSpeed);
            latSlip = Mathf.Atan(latSpeed / (longSpeedAbs + MinLatSpeedForTireModel));
            if (longSpeed == 0f && omega == 0f)
            {
                longSlip = 0f;
                return;
            }
            if (accel || brake) longSlip = (wheelSpeed - longSpeed) / (Mathf.Max(longSpeedAbs, wheelSpeedAbs) + 0.1f);
            else longSlip = (wheelSpeed - longSpeed) / Mathf.Max(MinLongSlipDenominator, Mathf.Max(longSpeedAbs, wheelSpeedAbs));
        }

        /// <summary>Unity's tire: each slip through its friction curve times the stiffness and the
        /// filtered load, against the slip; the pair kept inside the ellipse of the two curves'
        /// extremum forces (measured).</summary>
        static void TireForces(WheelCollider c, float longSlip, float latSlip, float load, out float longForce, out float latForce)
        {
            if (Mathf.Abs(longSlip) < MinimumSlipThreshold) longSlip = 0f;
            if (Mathf.Abs(latSlip) < MinimumSlipThreshold) latSlip = 0f;
            var f = c.m_ForwardFriction;
            var s = c.m_SidewaysFriction;
            longForce = longSlip == 0f ? 0f : Sign(longSlip) * f.Evaluate(longSlip) * f.stiffness * load;
            latForce = latSlip == 0f ? 0f : -Sign(latSlip) * s.Evaluate(latSlip) * s.stiffness * load;
            float lf = f.extremumValue * f.stiffness * load, ls = s.extremumValue * s.stiffness * load;
            if (lf <= 0f || ls <= 0f) return;
            float e = (longForce / lf) * (longForce / lf) + (latForce / ls) * (latForce / ls);
            if (e > 1f)
            {
                float k = 1f / Mathf.Sqrt(e);
                longForce *= k;
                latForce *= k;
            }
        }

        /// <summary>PhysX's intersectRayPlane: the jounce that puts the wheel's bottom on the plane,
        /// and that bottom.</summary>
        static bool IntersectRayPlane(Vector3 chassisPosition, Quaternion chassisRotation, in Wheel w, out float jounce, out Vector3 bottom)
        {
            var dir = chassisRotation * -Up;
            var start = chassisPosition + chassisRotation * w.CentreOffset - dir * (w.Radius + w.MaxCompression);
            var n = new Vector3(w.Plane.x, w.Plane.y, w.Plane.z);
            jounce = 0f;
            bottom = default;
            if (Vector3.Dot(n, start) + w.Plane.w < 0f) return false;
            float t = -(Vector3.Dot(n, start) + w.Plane.w) / Vector3.Dot(n, dir);
            float restT = 2f * w.Radius + w.MaxCompression;
            jounce = restT - t;
            bottom = start + dir * (restT - jounce);
            return true;
        }

        /// <summary>The suspension ray of a wheel: from its top at full compression down past its
        /// bottom at full droop, against everything but the body's own colliders and triggers.</summary>
        static void Cast(ref Wheel w, Rigidbody rb, Vector3 chassisPosition, Quaternion chassisRotation, int mask)
        {
            var dir = chassisRotation * -Up;
            var start = chassisPosition + chassisRotation * w.CentreOffset - dir * (w.Radius + w.MaxCompression);
            float length = w.Radius + w.MaxCompression + w.MaxDroop + w.Radius + w.Radius;
            w.Hit = false;
            var hits = PhysicsQueries.Raycast(start, dir, length, mask, QueryTriggerInteraction.Ignore, false);
            float best = float.MaxValue;
            RaycastHit found = default;
            foreach (var h in hits)
            {
                var other = h.collider;
                if (other == null || other.isTrigger || other is WheelCollider) continue;
                if (ReferenceEquals(W.FindRigidbody(other), rb)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    found = h;
                }
            }
            if (best == float.MaxValue || best == 0f) return;
            w.Hit = true;
            w.Distance = found.distance;
            w.HitPoint = found.point;
            w.HitNormal = found.normal;
            w.Plane = new Vector4(found.normal.x, found.normal.y, found.normal.z, -Vector3.Dot(found.normal, found.point));
            w.HitCollider = found.collider;
            w.HitBody = W.FindRigidbody(found.collider);
        }
    }
}
