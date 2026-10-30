using System;
using Unbound.Runtime;
using B2 = Unbound.Box2D.Dynamics;
using J2 = Unbound.Box2D.Dynamics.Joints;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    public enum JointBreakAction2D { Ignore, CallbackOnly, Disable, Destroy }

    [RequireComponent(typeof(Transform), typeof(Rigidbody2D))]
    public partial class Joint2D : Behaviour
    {
        internal Rigidbody2D m_ConnectedBody;
        internal bool m_EnableCollision, m_JointDirty = true;
        internal float m_BreakForce = float.PositiveInfinity, m_BreakTorque = float.PositiveInfinity;
        internal JointBreakAction2D m_BreakAction = JointBreakAction2D.Destroy;
        internal J2.Joint m_Joint;
        internal Vector2 m_SuspendedImpulse;
        internal float m_SuspendedTorque, m_SuspendedMotor;
        bool m_Warned;

        public Joint2D() { }
        public Rigidbody2D attachedRigidbody => GetComponent<Rigidbody2D>();
        public Rigidbody2D connectedBody
        {
            get => m_ConnectedBody;
            set { m_ConnectedBody = value; ConnectionChanged(); AnchorsChanged(); }
        }
        public bool enableCollision { get => m_EnableCollision; set { m_EnableCollision = value; Changed(); } }
        public float breakForce { get => m_BreakForce; set => m_BreakForce = Mathf.Max(0f, value); }
        public float breakTorque { get => m_BreakTorque; set => m_BreakTorque = Mathf.Max(0f, value); }
        public JointBreakAction2D breakAction { get => m_BreakAction; set => m_BreakAction = value; }
        public Vector2 reactionForce => GetReactionForce(Time.fixedDeltaTime);
        public float reactionTorque => GetReactionTorque(Time.fixedDeltaTime);
        public Vector2 GetReactionForce(float timeStep) => timeStep > 0f && isActiveAndEnabled
            ? m_Joint != null ? W.ToUnity(m_Joint.GetReactionForce(1f / timeStep)) : m_SuspendedImpulse / timeStep : Vector2.zero;
        public float GetReactionTorque(float timeStep) => timeStep > 0f && isActiveAndEnabled
            ? m_Joint != null ? m_Joint.GetReactionTorque(1f / timeStep) : m_SuspendedTorque / timeStep : 0f;
        [Obsolete("Joint2D.collideConnected has been deprecated. Use Joint2D.enableCollision instead (UnityUpgradable) -> enableCollision")]
        public bool collideConnected { get => enableCollision; set => enableCollision = value; }

        internal void Changed() => m_JointDirty = true;
        internal virtual void ConnectionChanged() { }
        internal virtual void AnchorsChanged() => Changed();
        internal virtual J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            if (!m_Warned) { m_Warned = true; Debug.LogError($"Unbound: {GetType().Name} simulation is not implemented.", this); }
            return null;
        }
        internal override void OnEngineEnable() { AnchorsChanged(); Physics2DJoints.Add(this); }
        internal override void OnEngineDisable() => Physics2DJoints.Release(this);
        internal override void OnEngineDestroy() { Physics2DJoints.Remove(this); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_ConnectedBody = resolver.Resolve(body.Ref("m_ConnectedRigidBody"), typeof(Rigidbody2D)) as Rigidbody2D;
            m_EnableCollision = body.Bool("m_EnableCollision");
            m_BreakForce = body.Float("m_BreakForce", float.PositiveInfinity);
            m_BreakTorque = body.Float("m_BreakTorque", float.PositiveInfinity);
            m_BreakAction = (JointBreakAction2D)body.Int("m_BreakAction", 3);
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Joint2D)source; m_Enabled = s.m_Enabled;
            m_ConnectedBody = remap(s.m_ConnectedBody) as Rigidbody2D;
            m_EnableCollision = s.m_EnableCollision; m_BreakForce = s.m_BreakForce;
            m_BreakTorque = s.m_BreakTorque; m_BreakAction = s.m_BreakAction;
        }
    }

    public partial class AnchoredJoint2D : Joint2D
    {
        internal Vector2 m_Anchor, m_ConnectedAnchor;
        internal bool m_AutoAnchor;
        internal float m_ReferenceAngle;
        bool m_ReferenceConfigured;
        internal virtual bool UsesReferenceAngle => false;
        public AnchoredJoint2D() { }
        public Vector2 anchor { get => m_Anchor; set { m_Anchor = value; AnchorsChanged(); } }
        public Vector2 connectedAnchor
        {
            get => m_ConnectedAnchor;
            set { if (m_AutoAnchor) AnchorsChanged(); else { m_ConnectedAnchor = value; DistanceChanged(); } }
        }
        public bool autoConfigureConnectedAnchor { get => m_AutoAnchor; set { m_AutoAnchor = value; AnchorsChanged(); } }
        // Box2D anchors use the physics pose and signed lossy scale, excluding hierarchy shear.
        internal static Vector2 WorldPoint(Rigidbody2D rb, Vector2 point)
        {
            var scale = rb.transform.lossyScale;
            var local = W.ToB2(new Vector2(point.x * scale.x, point.y * scale.y));
            var body = rb.Body;
            if (body != null) return W.ToUnity(body.GetWorldPoint(local));
            W.PoseOf(rb.transform, out var position, out var angle);
            var c = Mathf.Cos(angle); var s = Mathf.Sin(angle);
            return position + new Vector2(c * local.X - s * local.Y, s * local.X + c * local.Y);
        }
        static Vector2 LocalPoint(Rigidbody2D rb, Vector2 point)
        {
            var body = rb.Body;
            Vector2 local;
            if (body != null) local = W.ToUnity(body.GetLocalPoint(W.ToB2(point)));
            else
            {
                W.PoseOf(rb.transform, out var position, out var angle);
                var d = point - position; var c = Mathf.Cos(angle); var s = Mathf.Sin(angle);
                local = new Vector2(c * d.x + s * d.y, -s * d.x + c * d.y);
            }
            var scale = rb.transform.lossyScale;
            return new Vector2(scale.x != 0f ? local.x / scale.x : 0f, scale.y != 0f ? local.y / scale.y : 0f);
        }
        internal Vector2 WorldAnchor => WorldPoint(attachedRigidbody, m_Anchor);
        internal Vector2 OtherAnchor => m_ConnectedBody != null ? WorldPoint(m_ConnectedBody, m_ConnectedAnchor) : m_ConnectedAnchor;
        internal override void AnchorsChanged()
        {
            if (m_AutoAnchor && m_GameObject != null)
                m_ConnectedAnchor = m_ConnectedBody != null ? LocalPoint(m_ConnectedBody, WorldAnchor) : WorldAnchor;
            DistanceChanged();
        }
        internal virtual void DistanceChanged() => Changed();
        internal override void ConnectionChanged()
        {
            if (!UsesReferenceAngle || m_GameObject == null) return;
            var own = attachedRigidbody?.Body;
            var other = m_ConnectedBody != null ? m_ConnectedBody.Body : null;
            float otherAngle = 0f;
            if (other != null) otherAngle = other.GetAngle();
            else if (m_ConnectedBody != null) W.PoseOf(m_ConnectedBody.transform, out _, out otherAngle);
            m_ReferenceAngle = otherAngle - (own?.GetAngle() ?? 0f);
            m_ReferenceConfigured = true;
        }
        internal override void OnEngineEnable()
        {
            if (!m_ReferenceConfigured) ConnectionChanged();
            base.OnEngineEnable();
        }
        internal void SetAnchors(J2.DistanceJointDef def, B2.Body own, B2.Body other)
        {
            def.LocalAnchorA = own.GetLocalPoint(W.ToB2(WorldAnchor));
            def.LocalAnchorB = other.GetLocalPoint(W.ToB2(OtherAnchor));
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_Anchor = body.Vector2("m_Anchor"); m_ConnectedAnchor = body.Vector2("m_ConnectedAnchor");
            m_AutoAnchor = body.Bool("m_AutoConfigureConnectedAnchor");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (AnchoredJoint2D)source;
            m_Anchor = s.m_Anchor; m_ConnectedAnchor = s.m_ConnectedAnchor; m_AutoAnchor = s.m_AutoAnchor;
        }
    }

    [ClassId(232)]
    public sealed partial class DistanceJoint2D : AnchoredJoint2D
    {
        bool m_AutoDistance = true, m_MaxOnly;
        float m_Distance = 1f;
        public DistanceJoint2D() { }
        public bool autoConfigureDistance { get => m_AutoDistance; set { m_AutoDistance = value; DistanceChanged(); } }
        public float distance { get => m_Distance; set { m_Distance = Mathf.Max(.005f, value); Changed(); } }
        public bool maxDistanceOnly { get => m_MaxOnly; set { m_MaxOnly = value; Changed(); } }
        internal override void DistanceChanged()
        {
            if (m_AutoDistance && m_GameObject != null) m_Distance = Mathf.Max(.005f, Vector2.Distance(WorldAnchor, OtherAnchor));
            Changed();
        }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            var d = new J2.DistanceJointDef { Length = m_Distance, MinLength = m_MaxOnly ? 0f : m_Distance, MaxLength = m_Distance, UnityLimits = true };
            SetAnchors(d, own, other); return d;
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_AutoDistance = body.Bool("m_AutoConfigureDistance", true);
            m_Distance = body.Float("m_Distance", 1f); m_MaxOnly = body.Bool("m_MaxDistanceOnly");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (DistanceJoint2D)source;
            m_Distance = s.m_Distance; m_AutoDistance = s.m_AutoDistance; m_MaxOnly = s.m_MaxOnly;
        }
    }

    [ClassId(231)]
    public sealed partial class SpringJoint2D : AnchoredJoint2D
    {
        bool m_AutoDistance = true;
        float m_Distance = 1f, m_Frequency = 1f, m_Damping;
        public SpringJoint2D() { }
        public bool autoConfigureDistance { get => m_AutoDistance; set { m_AutoDistance = value; DistanceChanged(); } }
        public float distance { get => m_Distance; set { m_Distance = Mathf.Max(.005f, value); Changed(); } }
        public float frequency { get => m_Frequency; set { m_Frequency = Mathf.Clamp(value, 0f, 1000000f); Changed(); } }
        public float dampingRatio { get => m_Damping; set { m_Damping = Mathf.Clamp01(value); Changed(); } }
        internal override void DistanceChanged()
        {
            if (m_AutoDistance && m_GameObject != null) m_Distance = Mathf.Max(.005f, Vector2.Distance(WorldAnchor, OtherAnchor));
            Changed();
        }
        internal override J2.JointDef Definition(B2.Body own, B2.Body other)
        {
            var d = new J2.DistanceJointDef { Length = m_Distance, MinLength = m_Frequency > 0f ? 0f : m_Distance,
                MaxLength = m_Frequency > 0f ? float.MaxValue : m_Distance, UnityLimits = true,
                UnityFrequency = m_Frequency, UnityDampingRatio = m_Damping };
            J2.JointUtils.LinearStiffness(out d.Stiffness, out d.Damping, m_Frequency, m_Damping, own, other);
            SetAnchors(d, own, other); return d;
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_AutoDistance = body.Bool("m_AutoConfigureDistance", true);
            m_Distance = body.Float("m_Distance", 1f); m_Frequency = body.Float("m_Frequency", 1f); m_Damping = body.Float("m_DampingRatio");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (SpringJoint2D)source;
            m_Distance = s.m_Distance; m_AutoDistance = s.m_AutoDistance; m_Frequency = s.m_Frequency; m_Damping = s.m_Damping;
        }
    }
}
