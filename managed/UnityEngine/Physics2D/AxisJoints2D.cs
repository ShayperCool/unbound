using System;
using Unbound.Runtime;
using B2 = Unbound.Box2D.Dynamics;
using J2 = Unbound.Box2D.Dynamics.Joints;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    public partial struct JointSuspension2D
    {
        public float dampingRatio { get; set; }
        public float frequency { get; set; }
        public float angle { get; set; }
    }
    public partial struct JointTranslationLimits2D
    {
        public float min { get; set; }
        public float max { get; set; }
    }

    [ClassId(235)]
    public sealed partial class WheelJoint2D : AnchoredJoint2D
    {
        JointSuspension2D m_Suspension = new JointSuspension2D { frequency = 2f, dampingRatio = .7f, angle = 90f };
        JointMotor2D m_Motor = new JointMotor2D { maxMotorTorque = 10000f };
        bool m_UseMotor;
        public WheelJoint2D() { m_AutoAnchor = true; }
        internal override bool NeedsConnectedBody => true;
        // This native component stores the auto-anchor flag without configuring its anchor.
        internal override void AnchorsChanged() => Changed();
        public JointSuspension2D suspension { get => m_Suspension; set { m_Suspension = value; Changed(); } }
        public JointMotor2D motor { get => m_Motor; set { m_Motor = value; m_UseMotor = true; Changed(); } }
        public bool useMotor { get => m_UseMotor; set { m_UseMotor = value; Changed(); } }
        public float jointTranslation => !isActiveAndEnabled ? 0f : CurrentJoint is J2.WheelJoint joint ? joint.GetJointTranslation() : 0f;
        public float jointLinearSpeed => !isActiveAndEnabled ? 0f : CurrentJoint is J2.WheelJoint joint ? joint.GetJointLinearSpeed() : 0f;
        public float jointAngle => !isActiveAndEnabled ? 0f : CurrentJoint is J2.WheelJoint joint ? joint.GetJointAngle() * Mathf.Rad2Deg : 0f;
        public float jointSpeed => !isActiveAndEnabled ? 0f : CurrentJoint is J2.WheelJoint joint ? joint.GetJointAngularSpeed() * Mathf.Rad2Deg : 0f;
        public float GetMotorTorque(float timeStep) => timeStep > 0f && isActiveAndEnabled
            ? CurrentJoint is J2.WheelJoint joint ? joint.GetMotorTorque(1f / timeStep) : m_SuspendedMotor / timeStep : 0f;
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            float angle = m_Suspension.angle * Mathf.Deg2Rad;
            return new J2.WheelJointDef
            {
                LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor)), LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor)),
                LocalAxisA = new System.Numerics.Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), EnableMotor = m_UseMotor,
                MotorSpeed = m_Motor.motorSpeed * Mathf.Deg2Rad, MaxMotorTorque = m_Motor.maxMotorTorque,
                UnityFrequency = m_Suspension.frequency, UnityDampingRatio = m_Suspension.dampingRatio,
            };
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_UseMotor = body.Bool("m_UseMotor");
            var suspension = body.Get("m_Suspension"); var motor = body.Get("m_Motor");
            if (suspension != null) m_Suspension = new JointSuspension2D { frequency = suspension.Float("m_Frequency", 2f), dampingRatio = suspension.Float("m_DampingRatio", .7f), angle = suspension.Float("m_Angle", 90f) };
            if (motor != null) m_Motor = new JointMotor2D { motorSpeed = motor.Float("m_MotorSpeed"), maxMotorTorque = motor.Float("m_MaximumMotorForce", 10000f) };
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (WheelJoint2D)source;
            m_Suspension = s.m_Suspension; m_Motor = s.m_Motor; m_UseMotor = s.m_UseMotor;
        }
    }

    [ClassId(234)]
    public sealed partial class SliderJoint2D : AnchoredJoint2D
    {
        bool m_AutoAngle = true, m_UseMotor, m_UseLimits;
        float m_Angle;
        JointMotor2D m_Motor = new JointMotor2D { maxMotorTorque = 10000f };
        JointTranslationLimits2D m_Limits;
        public SliderJoint2D() { }
        internal override bool UsesReferenceAngle => true;
        public bool autoConfigureAngle { get => m_AutoAngle; set { m_AutoAngle = value; DistanceChanged(); } }
        public float angle { get => m_Angle; set { if (m_AutoAngle) DistanceChanged(); else { m_Angle = Mathf.Clamp(value, -1000000f, 1000000f); Changed(); } } }
        public float referenceAngle => isActiveAndEnabled ? m_ReferenceAngle * Mathf.Rad2Deg : 0f;
        public bool useMotor { get => m_UseMotor; set { m_UseMotor = value; Changed(); } }
        public bool useLimits { get => m_UseLimits; set { m_UseLimits = value; Changed(); } }
        public JointMotor2D motor
        {
            get => m_Motor;
            set
            {
                m_Motor = new JointMotor2D { motorSpeed = Mathf.Clamp(value.motorSpeed, -1000000f, 1000000f), maxMotorTorque = Mathf.Clamp(value.maxMotorTorque, 0f, 1000000f) };
                m_UseMotor = true; Changed();
            }
        }
        public JointTranslationLimits2D limits
        {
            get => m_Limits;
            set
            {
                float min = Mathf.Clamp(value.min, -1000000f, 1000000f);
                m_Limits = new JointTranslationLimits2D { min = min, max = Mathf.Clamp(value.max, min, 1000000f) };
                m_UseLimits = true; Changed();
            }
        }
        public float jointTranslation => !isActiveAndEnabled ? 0f : CurrentJoint is J2.PrismaticJoint joint ? joint.GetJointTranslation() : 0f;
        // Unity applies the radians-to-degrees conversion to this linear query too.
        public float jointSpeed => !isActiveAndEnabled ? 0f : CurrentJoint is J2.PrismaticJoint joint ? joint.GetJointSpeed() * Mathf.Rad2Deg : 0f;
        public JointLimitState2D limitState => !isActiveAndEnabled ? JointLimitState2D.Inactive
            : CurrentJoint is J2.PrismaticJoint joint ? (JointLimitState2D)joint.GetLimitState() : JointLimitState2D.Inactive;
        public float GetMotorForce(float timeStep) => timeStep > 0f && isActiveAndEnabled
            ? CurrentJoint is J2.PrismaticJoint joint ? joint.GetMotorForce(1f / timeStep) : m_SuspendedMotor / timeStep : 0f;
        internal override void DistanceChanged()
        {
            if (m_AutoAngle && m_GameObject != null)
            {
                var d = OtherAnchor - WorldAnchor; m_Angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }
            Changed();
        }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            float angle = m_Angle * Mathf.Deg2Rad;
            return new J2.PrismaticJointDef
            {
                LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor)), LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor)),
                LocalAxisA = new System.Numerics.Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), ReferenceAngle = m_ReferenceAngle,
                EnableMotor = m_UseMotor, MotorSpeed = m_Motor.motorSpeed, MaxMotorForce = m_Motor.maxMotorTorque,
                UnityLimits = true, EnableLimit = m_UseLimits, LowerTranslation = m_Limits.min, UpperTranslation = m_Limits.max,
            };
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_AutoAngle = body.Bool("m_AutoConfigureAngle", true); m_Angle = body.Float("m_Angle");
            m_UseMotor = body.Bool("m_UseMotor"); m_UseLimits = body.Bool("m_UseLimits");
            var motor = body.Get("m_Motor"); var limits = body.Get("m_TranslationLimits");
            if (motor != null) m_Motor = new JointMotor2D { motorSpeed = motor.Float("m_MotorSpeed"), maxMotorTorque = motor.Float("m_MaximumMotorForce", 10000f) };
            if (limits != null) m_Limits = new JointTranslationLimits2D { min = limits.Float("m_LowerTranslation"), max = limits.Float("m_UpperTranslation") };
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (SliderJoint2D)source;
            m_AutoAngle = s.m_AutoAngle; m_Angle = s.m_Angle; m_UseMotor = s.m_UseMotor; m_UseLimits = s.m_UseLimits;
            m_Motor = s.m_Motor; m_Limits = s.m_Limits;
        }
    }
}
