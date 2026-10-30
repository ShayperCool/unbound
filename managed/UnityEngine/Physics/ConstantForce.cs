using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Queues world and body-relative forces during FixedUpdate, including when
    /// simulation is manual. Calling Physics.Simulate alone does not run this component.</summary>
    [ClassId(75)]
    [RequireComponent(typeof(Rigidbody))]
    public partial class ConstantForce : Behaviour
    {
        Vector3 m_Force, m_RelativeForce, m_Torque, m_RelativeTorque;
        public ConstantForce() { }
        public Vector3 force { get => m_Force; set => m_Force = value; }
        public Vector3 relativeForce { get => m_RelativeForce; set => m_RelativeForce = value; }
        public Vector3 torque { get => m_Torque; set => m_Torque = value; }
        public Vector3 relativeTorque { get => m_RelativeTorque; set => m_RelativeTorque = value; }

        internal void Apply()
        {
            var body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) return;
            if (m_Force.sqrMagnitude != 0f) body.AddForce(m_Force);
            if (m_RelativeForce.sqrMagnitude != 0f) body.AddRelativeForce(m_RelativeForce);
            if (m_Torque.sqrMagnitude != 0f) body.AddTorque(m_Torque);
            if (m_RelativeTorque.sqrMagnitude != 0f) body.AddRelativeTorque(m_RelativeTorque);
        }
        internal override void OnEngineEnable() => ConstantForces.Add(this);
        internal override void OnEngineDisable() => ConstantForces.Remove(this);
        internal override void OnEngineDestroy() { ConstantForces.Remove(this); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Force = body.Vector3("m_Force"); m_RelativeForce = body.Vector3("m_RelativeForce");
            m_Torque = body.Vector3("m_Torque"); m_RelativeTorque = body.Vector3("m_RelativeTorque");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (ConstantForce)source; m_Enabled = s.m_Enabled;
            m_Force = s.m_Force; m_RelativeForce = s.m_RelativeForce;
            m_Torque = s.m_Torque; m_RelativeTorque = s.m_RelativeTorque;
        }
    }
}
