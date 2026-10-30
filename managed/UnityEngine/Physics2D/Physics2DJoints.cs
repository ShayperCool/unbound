using System.Collections.Generic;
using UnityEngine;
using Unbound.Box2D.Dynamics;

namespace Unbound.Runtime
{
    internal static class Physics2DJoints
    {
        static readonly List<Joint2D> s_Joints = new List<Joint2D>();
        static Body s_Ground;
        internal static void Add(Joint2D joint)
        {
            Physics2DWorld.EnsureRegistered();
            if (!s_Joints.Contains(joint)) s_Joints.Add(joint);
            joint.m_JointDirty = true;
        }
        internal static void Release(Joint2D joint, bool preserve = false)
        {
            if (!preserve) { joint.m_SuspendedImpulse = Vector2.zero; joint.m_SuspendedTorque = joint.m_SuspendedMotor = 0f; }
            if (joint.m_Joint != null)
            {
                if (preserve)
                {
                    joint.m_SuspendedImpulse = Physics2DWorld.ToUnity(joint.m_Joint.GetReactionForce(1f));
                    joint.m_SuspendedTorque = joint.m_Joint.GetReactionTorque(1f);
                    if (joint.m_Joint is Unbound.Box2D.Dynamics.Joints.RevoluteJoint hinge) joint.m_SuspendedMotor = hinge.GetMotorTorque(1f);
                }
                Physics2DWorld.World.DestroyJoint(joint.m_Joint);
                joint.m_Joint = null;
            }
            joint.m_JointDirty = true;
        }
        internal static void Remove(Joint2D joint) { Release(joint); s_Joints.Remove(joint); }
        internal static void BeforeDestroy(Body body)
        {
            foreach (var joint in s_Joints)
                if (joint.m_Joint != null && (joint.m_Joint.BodyA == body || joint.m_Joint.BodyB == body)) Release(joint, preserve: true);
        }
        internal static void Reset() { s_Joints.Clear(); s_Ground = null; }
        internal static void Sync()
        {
            var world = Physics2DWorld.World;
            foreach (var joint in s_Joints)
            {
                if (joint == null || !joint.isActiveAndEnabled) continue;
                var own = joint.attachedRigidbody?.m_Body;
                var other = joint.m_ConnectedBody != null ? joint.m_ConnectedBody.m_Body : null;
                if (own == null || (joint.m_ConnectedBody != null && other == null)) { Release(joint, preserve: true); continue; }
                if (other == null) other = s_Ground ??= world.CreateBody(new BodyDef());
                if (own == other) { Release(joint); continue; }
                if (joint.m_Joint != null && !joint.m_JointDirty && joint.m_Joint.BodyA == own && joint.m_Joint.BodyB == other) continue;
                Release(joint);
                var def = joint.Definition(own, other);
                if (def != null)
                {
                    def.BodyA = own; def.BodyB = other; def.CollideConnected = joint.m_EnableCollision; def.UserData = joint;
                    joint.m_Joint = world.CreateJoint(def);
                }
                joint.m_JointDirty = false;
            }
        }
        internal static void AfterStep(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            foreach (var joint in s_Joints.ToArray())
            {
                if (joint == null || joint.m_Joint == null || joint.m_BreakAction == JointBreakAction2D.Ignore) continue;
                var force = joint.GetReactionForce(deltaTime);
                var torque = joint.GetReactionTorque(deltaTime);
                if (force.sqrMagnitude <= joint.m_BreakForce * joint.m_BreakForce && Mathf.Abs(torque) <= joint.m_BreakTorque) continue;
                Physics2DWorld.Deliver(joint.gameObject, "OnJointBreak2D", typeof(Joint2D), joint);
                if (joint == null) continue;
                if (joint.m_BreakAction == JointBreakAction2D.Disable) joint.enabled = false;
                else if (joint.m_BreakAction == JointBreakAction2D.Destroy) Object.DestroyImmediate(joint);
            }
        }
    }
}
