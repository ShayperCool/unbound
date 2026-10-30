using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class PhysicsUpdateBehaviour2D : Behaviour
    {
        public PhysicsUpdateBehaviour2D() { }
    }

    [ClassId(247)]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed partial class ConstantForce2D : PhysicsUpdateBehaviour2D
    {
        Vector2 m_Force, m_RelativeForce;
        float m_Torque;
        public ConstantForce2D() { }
        public Vector2 force { get => m_Force; set => m_Force = value; }
        public Vector2 relativeForce { get => m_RelativeForce; set => m_RelativeForce = value; }
        public float torque { get => m_Torque; set => m_Torque = value; }

        internal void Apply()
        {
            var body = GetComponent<Rigidbody2D>();
            if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic) return;
            // The native 2D component wakes a dynamic body even when all values are zero.
            body.AddForce(m_Force);
            body.AddRelativeForce(m_RelativeForce);
            // ConstantForce2D stores angular force in degrees; AddTorque takes radians.
            body.AddTorque(m_Torque * Mathf.Deg2Rad);
        }
        internal override void OnEngineEnable() => ConstantForces.Add(this);
        internal override void OnEngineDisable() => ConstantForces.Remove(this);
        internal override void OnEngineDestroy() { ConstantForces.Remove(this); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Force = body.Vector2("m_Force"); m_RelativeForce = body.Vector2("m_RelativeForce"); m_Torque = body.Float("m_Torque");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (ConstantForce2D)source; m_Enabled = s.m_Enabled;
            m_Force = s.m_Force; m_RelativeForce = s.m_RelativeForce; m_Torque = s.m_Torque;
        }
    }
}
