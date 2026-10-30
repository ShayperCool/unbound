using System;
using Unbound.Runtime;
using B2 = Unbound.Box2D.Dynamics;
using J2 = Unbound.Box2D.Dynamics.Joints;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    [ClassId(254)]
    public sealed partial class RelativeJoint2D : Joint2D
    {
        float m_MaxForce = 10000f, m_MaxTorque = 10000f, m_Correction = .3f;
        bool m_AutoOffset = true;
        Vector2 m_LinearOffset;
        float m_AngularOffset, m_AngularRadians;
        public RelativeJoint2D() { }
        public float maxForce { get => m_MaxForce; set { m_MaxForce = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float maxTorque { get => m_MaxTorque; set { m_MaxTorque = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float correctionScale { get => m_Correction; set { m_Correction = Mathf.Clamp01(value); Changed(); } }
        public bool autoConfigureOffset { get => m_AutoOffset; set => m_AutoOffset = value; }
        public Vector2 linearOffset { get => m_LinearOffset; set { m_LinearOffset = value; Changed(); } }
        public float angularOffset
        {
            get => isActiveAndEnabled ? m_AngularRadians * Mathf.Rad2Deg : m_AngularOffset;
            set { m_AngularOffset = value; m_AngularRadians = value * Mathf.Deg2Rad; Changed(); }
        }
        public Vector2 target
        {
            get
            {
                if (!isActiveAndEnabled) return Vector2.zero;
                var own = attachedRigidbody.Body;
                var other = m_ConnectedBody != null ? m_ConnectedBody.Body : null;
                Vector2 p = other != null ? W.ToUnity(other.GetPosition()) : m_ConnectedBody != null ? (Vector2)m_ConnectedBody.transform.position : Vector2.zero;
                float angle = other != null ? other.GetAngle() : 0f;
                angle -= m_AngularRadians;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                return p - new Vector2(c * m_LinearOffset.x - s * m_LinearOffset.y, s * m_LinearOffset.x + c * m_LinearOffset.y);
            }
        }
        internal override void ConnectionChanged()
        {
            if (!m_AutoOffset || m_GameObject == null) return;
            var own = attachedRigidbody.Body;
            var other = m_ConnectedBody != null ? m_ConnectedBody.Body : null;
            var p = other != null ? W.ToUnity(other.GetPosition()) : m_ConnectedBody != null ? (Vector2)m_ConnectedBody.transform.position : Vector2.zero;
            m_LinearOffset = own != null ? W.ToUnity(own.GetLocalPoint(W.ToB2(p))) : Vector2.zero;
            float otherAngle = 0f;
            if (other != null) otherAngle = other.GetAngle();
            else if (m_ConnectedBody != null) W.PoseOf(m_ConnectedBody.transform, out _, out otherAngle);
            m_AngularRadians = otherAngle - (own?.GetAngle() ?? 0f);
            m_AngularOffset = m_AngularRadians * Mathf.Rad2Deg;
        }
        internal override void OnEngineEnable() { ConnectionChanged(); base.OnEngineEnable(); }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other) => new J2.MotorJointDef
        {
            LinearOffset = W.ToB2(m_LinearOffset), AngularOffset = m_AngularRadians,
            MaxForce = m_MaxForce, MaxTorque = m_MaxTorque, CorrectionFactor = m_Correction,
        };
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_MaxForce = body.Float("m_MaxForce", 10000f); m_MaxTorque = body.Float("m_MaxTorque", 10000f);
            m_Correction = body.Float("m_CorrectionScale", .3f); m_AutoOffset = body.Bool("m_AutoConfigureOffset", true);
            m_LinearOffset = body.Vector2("m_LinearOffset"); m_AngularOffset = body.Float("m_AngularOffset"); m_AngularRadians = m_AngularOffset * Mathf.Deg2Rad;
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (RelativeJoint2D)source;
            m_MaxForce = s.m_MaxForce; m_MaxTorque = s.m_MaxTorque; m_Correction = s.m_Correction; m_AutoOffset = s.m_AutoOffset;
            m_LinearOffset = s.m_LinearOffset; m_AngularOffset = s.m_AngularOffset; m_AngularRadians = s.m_AngularRadians;
        }
    }

    [ClassId(257)]
    public sealed partial class TargetJoint2D : Joint2D
    {
        Vector2 m_Anchor, m_Target;
        bool m_AutoTarget = true;
        float m_MaxForce = 10000f, m_Frequency = 5f, m_Damping = 1f;
        public TargetJoint2D() { m_EnableCollision = true; }
        internal override bool RejectConnectionSettings => true;
        internal override bool ReverseBodies => true;
        public Vector2 anchor { get => m_Anchor; set { m_Anchor = value; AnchorsChanged(); } }
        public Vector2 target { get => m_Target; set { m_Target = value; Changed(); } }
        public bool autoConfigureTarget { get => m_AutoTarget; set => m_AutoTarget = value; }
        public float maxForce { get => m_MaxForce; set { m_MaxForce = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float frequency { get => m_Frequency; set { m_Frequency = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float dampingRatio { get => m_Damping; set { m_Damping = Mathf.Clamp01(value); Changed(); } }
        internal override void AnchorsChanged()
        {
            if (m_AutoTarget && m_GameObject != null) m_Target = attachedRigidbody.position;
            Changed();
        }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(.0001f, m_Frequency);
            var scale = transform.lossyScale;
            return new J2.MouseJointDef
            {
                Target = W.ToB2(m_Target), HasLocalAnchor = true,
                LocalAnchorB = W.ToB2(new Vector2(m_Anchor.x * scale.x, m_Anchor.y * scale.y)),
                MaxForce = m_MaxForce, Stiffness = own.Mass * omega * omega, Damping = 2f * own.Mass * m_Damping * omega,
            };
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_ConnectedBody = null; m_EnableCollision = true;
            m_Anchor = body.Vector2("m_Anchor"); m_Target = body.Vector2("m_Target"); m_AutoTarget = body.Bool("m_AutoConfigureTarget", true);
            m_MaxForce = body.Float("m_MaxForce", 10000f); m_Frequency = body.Float("m_Frequency", 5f); m_Damping = body.Float("m_DampingRatio", 1f);
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (TargetJoint2D)source; m_ConnectedBody = null; m_EnableCollision = true;
            m_Anchor = s.m_Anchor; m_Target = s.m_Target; m_AutoTarget = s.m_AutoTarget;
            m_MaxForce = s.m_MaxForce; m_Frequency = s.m_Frequency; m_Damping = s.m_Damping;
        }
    }
}
