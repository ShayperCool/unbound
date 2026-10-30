using System;
using System.Collections.Generic;
using Unbound.Runtime;
using B2Body = Unbound.Box2D.Dynamics.Body;
using B2Contact = Unbound.Box2D.Dynamics.Contacts.Contact;

namespace UnityEngine
{
    public partial class Effector2D : Behaviour
    {
        internal bool m_UseColliderMask = true;
        internal int m_ColliderMask = -1;
        public Effector2D() { }
        public bool useColliderMask { get => m_UseColliderMask; set { m_UseColliderMask = value; Changed(); } }
        public int colliderMask { get => m_ColliderMask; set { m_ColliderMask = value; Changed(); } }
        internal bool requiresCollider => true;
        internal bool designedForTrigger => TriggerEffector;
        internal bool designedForNonTrigger => !TriggerEffector;
        internal virtual bool TriggerEffector => false;
        internal virtual bool Implemented => false;
        internal virtual void Changed() => Physics2DEffectors.Refilter(this);
        internal override void OnEngineEnable()
        {
            if (!Implemented) { Debug.LogError($"Unbound: {GetType().Name} simulation is not implemented.", this); return; }
            Physics2DEffectors.Add(this);
        }
        internal override void OnEngineDisable() => Physics2DEffectors.Remove(this);
        internal override void OnEngineDestroy() { Physics2DEffectors.Remove(this); base.OnEngineDestroy(); }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_UseColliderMask = body.Bool("m_UseColliderMask", true);
            m_ColliderMask = Collider2D.ReadMask(body, "m_ColliderMask", -1);
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Effector2D)source; m_Enabled = s.m_Enabled;
            m_UseColliderMask = s.m_UseColliderMask; m_ColliderMask = s.m_ColliderMask;
        }
    }

    [ClassId(251)]
    public partial class PlatformEffector2D : Effector2D
    {
        internal bool m_OneWay = true, m_Grouping, m_SideFriction, m_SideBounce;
        internal float m_SurfaceArc = 180f, m_SideArc = 1f, m_RotationalOffset;
        readonly Dictionary<B2Contact, bool> m_Ignored = new Dictionary<B2Contact, bool>();
        readonly Dictionary<B2Contact, B2Body> m_Bodies = new Dictionary<B2Contact, B2Body>();
        readonly Dictionary<B2Body, int> m_IgnoredGroups = new Dictionary<B2Body, int>();
        public PlatformEffector2D() { }
        internal override bool Implemented => true;
        public bool useOneWay { get => m_OneWay; set { m_OneWay = value; Changed(); } }
        public bool useOneWayGrouping { get => m_Grouping; set { m_Grouping = value; Changed(); } }
        public bool useSideFriction { get => m_SideFriction; set { m_SideFriction = value; Changed(); } }
        public bool useSideBounce { get => m_SideBounce; set { m_SideBounce = value; Changed(); } }
        public float surfaceArc { get => m_SurfaceArc; set { m_SurfaceArc = Mathf.Clamp(value, 0f, 360f); Changed(); } }
        public float sideArc { get => m_SideArc; set { m_SideArc = Mathf.Clamp(value, 0f, 180f); Changed(); } }
        public float rotationalOffset { get => m_RotationalOffset; set { m_RotationalOffset = Mathf.Clamp(value, -359.9999f, 359.9999f); Changed(); } }
        [Obsolete("PlatformEffector2D.oneWay has been deprecated. Use PlatformEffector2D.useOneWay instead (UnityUpgradable) -> useOneWay")]
        public bool oneWay { get => useOneWay; set => useOneWay = value; }
        [Obsolete("PlatformEffector2D.sideFriction has been deprecated. Use PlatformEffector2D.useSideFriction instead (UnityUpgradable) -> useSideFriction")]
        public bool sideFriction { get => useSideFriction; set => useSideFriction = value; }
        [Obsolete("PlatformEffector2D.sideBounce has been deprecated. Use PlatformEffector2D.useSideBounce instead (UnityUpgradable) -> useSideBounce")]
        public bool sideBounce { get => useSideBounce; set => useSideBounce = value; }
        [Obsolete("PlatformEffector2D.sideAngleVariance has been deprecated. Use PlatformEffector2D.sideArc instead (UnityUpgradable) -> sideArc")]
        public float sideAngleVariance { get => sideArc; set => sideArc = value; }
        internal void ClearContacts() { m_Ignored.Clear(); m_Bodies.Clear(); m_IgnoredGroups.Clear(); }
        internal override void Changed() { ClearContacts(); base.Changed(); }
        internal void Forget(B2Contact contact)
        {
            if (m_Ignored.TryGetValue(contact, out bool ignored) && ignored && m_Bodies.TryGetValue(contact, out var body)
                && m_IgnoredGroups.TryGetValue(body, out int count))
            {
                if (count > 1) m_IgnoredGroups[body] = count - 1; else m_IgnoredGroups.Remove(body);
            }
            m_Ignored.Remove(contact); m_Bodies.Remove(contact);
        }
        internal bool Ignore(B2Contact contact, B2Body other, bool outsideArc)
        {
            bool known = m_Ignored.TryGetValue(contact, out bool ignored);
            if (ignored) return true;
            bool reject = outsideArc || (!known && m_Grouping && m_IgnoredGroups.ContainsKey(other));
            m_Ignored[contact] = reject;
            if (reject && m_Grouping)
            {
                m_Bodies[contact] = other; m_IgnoredGroups.TryGetValue(other, out int count); m_IgnoredGroups[other] = count + 1;
            }
            return reject;
        }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_OneWay = body.Bool("m_UseOneWay", true); m_Grouping = body.Bool("m_UseOneWayGrouping");
            m_SideFriction = body.Bool("m_UseSideFriction"); m_SideBounce = body.Bool("m_UseSideBounce");
            m_SurfaceArc = body.Float("m_SurfaceArc", 180f); m_SideArc = body.Float("m_SideArc", 1f); m_RotationalOffset = body.Float("m_RotationalOffset");
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (PlatformEffector2D)source;
            m_OneWay = s.m_OneWay; m_Grouping = s.m_Grouping; m_SideFriction = s.m_SideFriction; m_SideBounce = s.m_SideBounce;
            m_SurfaceArc = s.m_SurfaceArc; m_SideArc = s.m_SideArc; m_RotationalOffset = s.m_RotationalOffset;
        }
    }

    [ClassId(252)]
    public partial class SurfaceEffector2D : Effector2D
    {
        internal float m_Speed = 1f, m_SpeedVariation, m_ForceScale = .1f;
        internal bool m_ContactForce, m_Friction = true, m_Bounce = true;
        public SurfaceEffector2D() { }
        internal override bool Implemented => true;
        public float speed { get => m_Speed; set { m_Speed = Mathf.Clamp(value, -1000000f, 1000000f); Changed(); } }
        public float speedVariation { get => m_SpeedVariation; set { m_SpeedVariation = Mathf.Clamp(value, -1000000f, 1000000f); Changed(); } }
        public float forceScale { get => m_ForceScale; set { m_ForceScale = Mathf.Clamp01(value); Changed(); } }
        public bool useContactForce { get => m_ContactForce; set { m_ContactForce = value; Changed(); } }
        public bool useFriction { get => m_Friction; set { m_Friction = value; Changed(); } }
        public bool useBounce { get => m_Bounce; set { m_Bounce = value; Changed(); } }
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver); m_Speed = body.Float("m_Speed", 1f); m_SpeedVariation = body.Float("m_SpeedVariation");
            m_ForceScale = body.Float("m_ForceScale", .1f); m_ContactForce = body.Bool("m_UseContactForce");
            m_Friction = body.Bool("m_UseFriction", true); m_Bounce = body.Bool("m_UseBounce", true);
        }
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap); var s = (SurfaceEffector2D)source;
            m_Speed = s.m_Speed; m_SpeedVariation = s.m_SpeedVariation; m_ForceScale = s.m_ForceScale;
            m_ContactForce = s.m_ContactForce; m_Friction = s.m_Friction; m_Bounce = s.m_Bounce;
        }
    }
}
