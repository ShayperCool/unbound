using System;
using Unbound.Runtime;
using B2 = Unbound.Box2D.Dynamics;
using J2 = Unbound.Box2D.Dynamics.Joints;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    public partial struct JointMotor2D
    {
        public float motorSpeed { get; set; }
        public float maxMotorTorque { get; set; }
    }
    public partial struct JointAngleLimits2D
    {
        public float min { get; set; }
        public float max { get; set; }
    }

    [ClassId(233)]
    public sealed partial class HingeJoint2D : AnchoredJoint2D
    {
        bool m_UseMotor, m_UseLimits, m_UseConnectedAnchor = true;
        JointMotor2D m_Motor = new JointMotor2D { maxMotorTorque = 10000f };
        JointAngleLimits2D m_Limits = new JointAngleLimits2D { max = 359f };
        public HingeJoint2D() { m_AutoAnchor = true; }
        internal override bool UsesReferenceAngle => true;
        public float referenceAngle => isActiveAndEnabled ? m_ReferenceAngle * Mathf.Rad2Deg : 0f;
        public float jointAngle
        {
            get
            {
                if (!isActiveAndEnabled) return 0f;
                var own = attachedRigidbody?.Body;
                var other = m_ConnectedBody != null ? m_ConnectedBody.Body : null;
                return ((other?.GetAngle() ?? 0f) - (own?.GetAngle() ?? attachedRigidbody.m_SyncAngle) - m_ReferenceAngle) * Mathf.Rad2Deg;
            }
        }
        public float jointSpeed => isActiveAndEnabled ? (m_ConnectedBody != null ? m_ConnectedBody.angularVelocity : 0f) - attachedRigidbody.angularVelocity : 0f;
        public JointLimitState2D limitState => m_Joint is J2.RevoluteJoint hinge ? (JointLimitState2D)hinge.GetLimitState() : JointLimitState2D.Inactive;
        public bool useMotor { get => m_UseMotor; set { m_UseMotor = value; Changed(); } }
        public bool useLimits { get => m_UseLimits; set { m_UseLimits = value; Changed(); } }
        public bool useConnectedAnchor { get => m_UseConnectedAnchor; set { m_UseConnectedAnchor = value; Changed(); } }
        public JointMotor2D motor
        {
            get => m_Motor;
            set
            {
                m_Motor = new JointMotor2D { motorSpeed = Mathf.Clamp(value.motorSpeed, -1000000f, 1000000f), maxMotorTorque = Mathf.Clamp(value.maxMotorTorque, 0f, 1000000f) };
                m_UseMotor = true; Changed();
            }
        }
        public JointAngleLimits2D limits
        {
            get => m_Limits;
            set
            {
                m_Limits = new JointAngleLimits2D { min = Mathf.Clamp(value.min, -1000000f, 1000000f), max = Mathf.Clamp(value.max, -1000000f, 1000000f) };
                m_UseLimits = true; Changed();
            }
        }
        public float GetMotorTorque(float timeStep) => timeStep > 0f && isActiveAndEnabled
            ? m_Joint is J2.RevoluteJoint hinge ? hinge.GetMotorTorque(1f / timeStep) : m_SuspendedMotor / timeStep : 0f;
        internal override J2.JointDef Definition(B2.Body own, B2.Body other) => new J2.RevoluteJointDef
        {
            LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor)), LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor)),
            ReferenceAngle = m_ReferenceAngle, EnableMotor = m_UseMotor, EnableLimit = m_UseLimits && m_Limits.min <= m_Limits.max,
            EnableAnchor = m_UseConnectedAnchor, MotorSpeed = m_Motor.motorSpeed * Mathf.Deg2Rad,
            MaxMotorTorque = m_Motor.maxMotorTorque, LowerAngle = m_Limits.min * Mathf.Deg2Rad, UpperAngle = m_Limits.max * Mathf.Deg2Rad,
        };
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_UseMotor = body.Bool("m_UseMotor"); m_UseLimits = body.Bool("m_UseLimits");
            m_UseConnectedAnchor = body.Bool("m_UseConnectedAnchor", true);
            var motor = body.Get("m_Motor"); var limits = body.Get("m_AngleLimits");
            if (motor != null) m_Motor = new JointMotor2D { motorSpeed = motor.Float("m_MotorSpeed"), maxMotorTorque = motor.Float("m_MaximumMotorForce", 10000f) };
            if (limits != null) m_Limits = new JointAngleLimits2D { min = limits.Float("m_LowerAngle"), max = limits.Float("m_UpperAngle", 359f) };
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (HingeJoint2D)source;
            m_UseMotor = s.m_UseMotor; m_UseLimits = s.m_UseLimits; m_UseConnectedAnchor = s.m_UseConnectedAnchor;
            m_Motor = s.m_Motor; m_Limits = s.m_Limits;
        }
    }

    [ClassId(255)]
    public sealed partial class FixedJoint2D : AnchoredJoint2D
    {
        float m_Frequency, m_Damping;
        public FixedJoint2D() { m_AutoAnchor = true; }
        internal override bool UsesReferenceAngle => true;
        public float referenceAngle => isActiveAndEnabled ? m_ReferenceAngle * Mathf.Rad2Deg : 0f;
        public float frequency { get => m_Frequency; set { m_Frequency = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float dampingRatio { get => m_Damping; set { m_Damping = Mathf.Clamp01(value); Changed(); } }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            var def = new J2.WeldJointDef { LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor)), LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor)), ReferenceAngle = m_ReferenceAngle };
            J2.JointUtils.AngularStiffness(out def.Stiffness, out def.Damping, m_Frequency, m_Damping, own, other);
            return def;
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_Frequency = body.Float("m_Frequency"); m_Damping = body.Float("m_DampingRatio");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (FixedJoint2D)source; m_Frequency = s.m_Frequency; m_Damping = s.m_Damping;
        }
    }

    [ClassId(256)]
    public sealed partial class FrictionJoint2D : AnchoredJoint2D
    {
        float m_MaxForce = 1f, m_MaxTorque = 1f;
        public FrictionJoint2D() { m_AutoAnchor = true; }
        public float maxForce { get => m_MaxForce; set { m_MaxForce = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float maxTorque { get => m_MaxTorque; set { m_MaxTorque = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other) => new J2.FrictionJointDef
        {
            LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor)), LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor)), MaxForce = m_MaxForce, MaxTorque = m_MaxTorque,
        };
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_MaxForce = body.Float("m_MaxForce", 1f); m_MaxTorque = body.Float("m_MaxTorque", 1f);
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (FrictionJoint2D)source; m_MaxForce = s.m_MaxForce; m_MaxTorque = s.m_MaxTorque;
        }
    }
}
