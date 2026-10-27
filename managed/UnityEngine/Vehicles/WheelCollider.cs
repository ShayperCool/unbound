using System;
using Unbound.Runtime;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    public struct JointSpring
    {
        public float spring;
        public float damper;
        public float targetPosition;
    }

    /// <summary>A wheel's friction curve: from no slip up to the extremum (its value reached with a
    /// zero tangent), then down or up to the asymptote, flat after it; values are fractions of the
    /// tire load, times the stiffness.</summary>
    public struct WheelFrictionCurve
    {
        float m_ExtremumSlip, m_ExtremumValue, m_AsymptoteSlip, m_AsymptoteValue, m_Stiffness;

        public float extremumSlip { get => m_ExtremumSlip; set => m_ExtremumSlip = value; }
        public float extremumValue { get => m_ExtremumValue; set => m_ExtremumValue = value; }
        public float asymptoteSlip { get => m_AsymptoteSlip; set => m_AsymptoteSlip = value; }
        public float asymptoteValue { get => m_AsymptoteValue; set => m_AsymptoteValue = value; }
        public float stiffness { get => m_Stiffness; set => m_Stiffness = value; }

        internal static WheelFrictionCurve Make(float exSlip, float exValue, float asSlip, float asValue, float stiffness) => new WheelFrictionCurve
        {
            m_ExtremumSlip = exSlip, m_ExtremumValue = exValue, m_AsymptoteSlip = asSlip, m_AsymptoteValue = asValue, m_Stiffness = stiffness,
        };

        /// <summary>The curve at a slip's magnitude, before the stiffness: a cubic Hermite from
        /// (0, 0) with the chord's slope to the extremum with a zero tangent, another to the
        /// asymptote with zero tangents at both ends, then the asymptote (measured:
        /// tests/projects/vehicles).</summary>
        internal float Evaluate(float slip)
        {
            float a = Math.Abs(slip);
            if (a <= m_ExtremumSlip)
            {
                if (m_ExtremumSlip <= 0f) return m_ExtremumValue;
                float t = a / m_ExtremumSlip, t2 = t * t, t3 = t2 * t;
                // h10 * m0 * h + h01 * p1, with m0 * h = the extremum value.
                return (t3 - 2f * t2 + t) * m_ExtremumValue + (3f * t2 - 2f * t3) * m_ExtremumValue;
            }
            if (a <= m_AsymptoteSlip)
            {
                float t = (a - m_ExtremumSlip) / (m_AsymptoteSlip - m_ExtremumSlip), t2 = t * t, t3 = t2 * t;
                return m_ExtremumValue + (m_AsymptoteValue - m_ExtremumValue) * (3f * t2 - 2f * t3);
            }
            return m_AsymptoteValue;
        }

        internal static WheelFrictionCurve Read(YNode node, WheelFrictionCurve fallback)
        {
            if (node == null) return fallback;
            return Make(node.Float("m_ExtremumSlip", fallback.m_ExtremumSlip), node.Float("m_ExtremumValue", fallback.m_ExtremumValue),
                node.Float("m_AsymptoteSlip", fallback.m_AsymptoteSlip), node.Float("m_AsymptoteValue", fallback.m_AsymptoteValue),
                node.Float("m_Stiffness", fallback.m_Stiffness));
        }
    }

    public struct WheelHit
    {
        Collider m_Collider;
        Vector3 m_Point, m_Normal, m_ForwardDir, m_SidewaysDir;
        float m_Force, m_ForwardSlip, m_SidewaysSlip;

        public Collider collider { get => m_Collider; set => m_Collider = value; }
        public Vector3 point { get => m_Point; set => m_Point = value; }
        public Vector3 normal { get => m_Normal; set => m_Normal = value; }
        public Vector3 forwardDir { get => m_ForwardDir; set => m_ForwardDir = value; }
        public Vector3 sidewaysDir { get => m_SidewaysDir; set => m_SidewaysDir = value; }
        public float force { get => m_Force; set => m_Force = value; }
        public float forwardSlip { get => m_ForwardSlip; set => m_ForwardSlip = value; }
        public float sidewaysSlip { get => m_SidewaysSlip; set => m_SidewaysSlip = value; }
    }

    /// <summary>A wheel of the vehicle its Rigidbody makes: a suspension along the body's down axis,
    /// a tire with slip-based friction and a spinning wheel, simulated each physics step by PhysX's
    /// vehicle SDK with Unity's tire (see <see cref="Unbound.Runtime.Vehicles"/>).</summary>
    [ClassId(146)]
    public partial class WheelCollider : Collider
    {
        internal Vector3 m_Center;
        internal float m_Radius = 0.5f;
        internal float m_SuspensionDistance = 0.3f;
        internal JointSpring m_SuspensionSpring = new JointSpring { spring = 35000f, damper = 4500f, targetPosition = 0.5f };
        internal bool m_SuspensionExpansionLimited;
        internal float m_ForceAppPointDistance;
        internal float m_Mass = 20f;
        internal float m_WheelDampingRate = 0.25f;
        internal WheelFrictionCurve m_ForwardFriction = WheelFrictionCurve.Make(0.4f, 1f, 0.8f, 0.5f, 1f);
        internal WheelFrictionCurve m_SidewaysFriction = WheelFrictionCurve.Make(0.2f, 1f, 0.5f, 0.75f, 1f);
        internal float m_MotorTorque, m_BrakeTorque, m_SteerAngle;

        // The wheel's dynamic state (PhysX's wheels dyn data) and its last results.
        internal float m_Omega, m_CorrectedOmega, m_RotationAngle;
        internal float m_PrevJounce = float.MaxValue;
        internal float m_LowForwardTimer, m_LowSideTimer;
        internal float m_Jounce;
        internal bool m_Simulated;
        internal float m_PoseSteer;
        internal bool m_InAir = true;
        internal WheelHit m_Hit;
        internal float m_SprungMass = -1f;

        public WheelCollider() { }

        public Vector3 center { get => m_Center; set => m_Center = value; }
        public float radius { get => m_Radius; set => m_Radius = value; }
        public float suspensionDistance { get => m_SuspensionDistance; set => m_SuspensionDistance = value; }
        public JointSpring suspensionSpring { get => m_SuspensionSpring; set => m_SuspensionSpring = value; }
        public bool suspensionExpansionLimited { get => m_SuspensionExpansionLimited; set => m_SuspensionExpansionLimited = value; }
        public float forceAppPointDistance { get => m_ForceAppPointDistance; set => m_ForceAppPointDistance = value; }
        public float mass { get => m_Mass; set => m_Mass = value; }
        public float wheelDampingRate { get => m_WheelDampingRate; set => m_WheelDampingRate = value; }
        public WheelFrictionCurve forwardFriction { get => m_ForwardFriction; set => m_ForwardFriction = value; }
        public WheelFrictionCurve sidewaysFriction { get => m_SidewaysFriction; set => m_SidewaysFriction = value; }
        // Setting an input wakes the vehicle (as Unity does: a driven wheel keeps a vehicle awake).
        public float motorTorque { get => m_MotorTorque; set { m_MotorTorque = value; WakeBody(); } }
        public float brakeTorque { get => m_BrakeTorque; set { m_BrakeTorque = value; WakeBody(); } }
        public float steerAngle { get => m_SteerAngle; set { m_SteerAngle = value; WakeBody(); } }

        void WakeBody()
        {
            if (!m_Registered) return;
            var rb = W.FindRigidbody(this);
            if (rb != null && rb.m_Body != 0 && !rb.m_IsKinematic) Unbound.Box3D.B3.ua3_body_set_awake(rb.m_Body, 1);
        }

        public bool isGrounded => m_Registered && !m_InAir;

        /// <summary>The wheel's rolling speed as PhysX reports it (blended towards the rolling speed
        /// at low forward speeds), in revolutions per minute.</summary>
        public float rpm => m_Registered ? m_CorrectedOmega * (60f / (2f * Mathf.PI)) : 0f;

        /// <summary>The same in degrees per second.</summary>
        public float rotationSpeed
        {
            get => m_Registered ? m_CorrectedOmega * Mathf.Rad2Deg : 0f;
            set
            {
                m_Omega = value * Mathf.Deg2Rad;
                m_CorrectedOmega = m_Omega;
                Vehicles.SetRotationSpeed(this, m_Omega);
            }
        }

        /// <summary>The share of the body's mass this wheel's suspension carries, from the wheels'
        /// positions around the center of mass (PhysX's computeSprungMasses).</summary>
        public float sprungMass
        {
            get => m_Registered ? Vehicles.SprungMass(this) : 0f;
            set => m_SprungMass = Mathf.Max(0f, value);
        }

        public void ResetSprungMasses() => Vehicles.ResetSprungMasses(this);

        public void ConfigureVehicleSubsteps(float speedThreshold, int stepsBelowThreshold, int stepsAboveThreshold) =>
            Vehicles.ConfigureSubsteps(this, speedThreshold, stepsBelowThreshold, stepsAboveThreshold);

        /// <summary>The wheel's world pose: at its suspension's place along the body's down axis, turned
        /// by the steer angle and rolled by the wheel's rotation.</summary>
        public void GetWorldPose(out Vector3 pos, out Quaternion quat)
        {
            // A disabled wheel has no pose: zeros, the quaternion too (measured).
            if (!m_Registered)
            {
                pos = Vector3.zero;
                quat = new Quaternion(0f, 0f, 0f, 0f);
                return;
            }
            Vehicles.WorldPose(this, out pos, out quat);
        }

        public bool GetGroundHit(out WheelHit hit)
        {
            bool grounded = isGrounded;
            hit = grounded ? m_Hit : default;
            return grounded;
        }

        internal override void OnAttached()
        {
            base.OnAttached();
            // Adding a wheel rebuilds the vehicle: the body's other wheels lose their inputs and
            // their spin, and the sprung masses are shared anew (measured).
            for (var t = transform; t != null; t = t.parent)
            {
                var rb = t.GetComponent<Rigidbody>();
                if (rb == null) continue;
                foreach (var other in rb.GetComponentsInChildren<WheelCollider>(true))
                {
                    other.m_SprungMass = -1f;
                    if (other == this) continue;
                    other.m_MotorTorque = other.m_BrakeTorque = other.m_SteerAngle = 0f;
                    other.ResetDynamics();
                }
                break;
            }
        }

        internal void ResetDynamics()
        {
            m_Omega = m_CorrectedOmega = 0f;
            m_RotationAngle = 0f;
            m_PrevJounce = float.MaxValue;
            m_LowForwardTimer = m_LowSideTimer = 0f;
        }

        internal override void OnEngineEnable()
        {
            base.OnEngineEnable();
            if (m_SprungMass == 0f) m_SprungMass = -1f;
        }

        internal override void OnEngineDisable()
        {
            base.OnEngineDisable();
            // A disabled wheel leaves the vehicle: no ground, no spin, no share of the mass.
            ResetDynamics();
            m_InAir = true;
            m_Simulated = false;
            m_SprungMass = 0f;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap);
            var s = (WheelCollider)source;
            m_Center = s.m_Center;
            m_Radius = s.m_Radius;
            m_SuspensionDistance = s.m_SuspensionDistance;
            m_SuspensionSpring = s.m_SuspensionSpring;
            m_SuspensionExpansionLimited = s.m_SuspensionExpansionLimited;
            m_ForceAppPointDistance = s.m_ForceAppPointDistance;
            m_Mass = s.m_Mass;
            m_WheelDampingRate = s.m_WheelDampingRate;
            m_ForwardFriction = s.m_ForwardFriction;
            m_SidewaysFriction = s.m_SidewaysFriction;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_Center = body.Vector3("m_Center");
            m_Radius = body.Float("m_Radius", 0.5f);
            var spring = body.Get("m_SuspensionSpring");
            if (spring != null)
                m_SuspensionSpring = new JointSpring { spring = spring.Float("spring", 35000f), damper = spring.Float("damper", 4500f), targetPosition = spring.Float("targetPosition", 0.5f) };
            m_SuspensionDistance = body.Float("m_SuspensionDistance", 0.3f);
            m_ForceAppPointDistance = body.Float("m_ForceAppPointDistance");
            m_Mass = body.Float("m_Mass", 20f);
            m_WheelDampingRate = body.Float("m_WheelDampingRate", 0.25f);
            m_ForwardFriction = WheelFrictionCurve.Read(body.Get("m_ForwardFriction"), m_ForwardFriction);
            m_SidewaysFriction = WheelFrictionCurve.Read(body.Get("m_SidewaysFriction"), m_SidewaysFriction);
        }
    }
}
