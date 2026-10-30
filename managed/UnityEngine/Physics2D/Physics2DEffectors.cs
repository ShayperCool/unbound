using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unbound.Box2D.Dynamics;
using Unbound.Box2D.Dynamics.Contacts;
using V2 = System.Numerics.Vector2;

namespace Unbound.Runtime
{
    internal static class Physics2DEffectors
    {
        static readonly Dictionary<GameObject, Effector2D> s_Effectors = new Dictionary<GameObject, Effector2D>();
        static readonly HashSet<Contact> s_Overrides = new HashSet<Contact>();
        static readonly Dictionary<Contact, List<PlatformEffector2D>> s_PlatformContacts = new Dictionary<Contact, List<PlatformEffector2D>>();
        internal static void Reset() { s_Effectors.Clear(); s_Overrides.Clear(); s_PlatformContacts.Clear(); }
        internal static void Add(Effector2D effector) { s_Effectors[effector.m_GameObject] = effector; Refilter(effector); }
        internal static void Remove(Effector2D effector)
        {
            s_Effectors.Remove(effector.m_GameObject);
            if (effector is PlatformEffector2D platform)
            {
                platform.ClearContacts();
                foreach (var contact in s_PlatformContacts.Keys.ToArray())
                    if (s_PlatformContacts[contact].Remove(platform) && s_PlatformContacts[contact].Count == 0) s_PlatformContacts.Remove(contact);
            }
            Refilter(effector);
        }
        internal static void Refilter(Effector2D effector)
        {
            if (effector.m_GameObject == null) return;
            foreach (var component in effector.m_GameObject.m_Components)
                if (component is Collider2D collider) Physics2DWorld.Refilter(collider);
        }
        static Effector2D Of(Collider2D collider) => collider != null && collider.m_UsedByEffector
            && s_Effectors.TryGetValue(collider.m_GameObject, out var effector) ? effector : null;
        internal static bool? LayerDecision(Collider2D a, Collider2D b, int layerA, int layerB)
        {
            var ea = Of(a); var eb = Of(b);
            bool? allowA = ea != null && ea.m_UseColliderMask ? (ea.m_ColliderMask & (1 << layerB)) != 0 : null;
            bool? allowB = eb != null && eb.m_UseColliderMask ? (eb.m_ColliderMask & (1 << layerA)) != 0 : null;
            return allowA.HasValue && allowB.HasValue ? allowA.Value && allowB.Value : allowA ?? allowB;
        }
        internal static void End(Contact contact)
        {
            if (s_PlatformContacts.Remove(contact, out var platforms)) foreach (var platform in platforms) platform.Forget(contact);
            if (s_Overrides.Remove(contact)) { contact.ResetFriction(); contact.ResetRestitution(); }
        }
        internal static void PreSolve(Contact contact, Collider2D a, Collider2D b)
        {
            var ea = Of(a); var eb = Of(b);
            if (s_Overrides.Remove(contact)) { contact.ResetFriction(); contact.ResetRestitution(); }
            if (ea == null && eb == null) return;
            contact.GetWorldManifold(out var world);
            Apply(ea, contact, Physics2DWorld.ToUnity(world.Normal), contact.FixtureB.Body);
            Apply(eb, contact, -Physics2DWorld.ToUnity(world.Normal), contact.FixtureA.Body);
        }
        static void Apply(Effector2D effector, Contact contact, Vector2 outward, Body other)
        {
            if (effector is PlatformEffector2D p)
            {
                var rotation = Quaternion.Euler(0, 0, p.m_RotationalOffset);
                var up = ((Vector2)p.transform.TransformVector(rotation * Vector3.up)).normalized;
                var right = ((Vector2)p.transform.TransformVector(rotation * Vector3.right)).normalized;
                if (p.m_OneWay)
                {
                    if (!s_PlatformContacts.TryGetValue(contact, out var tracked)) s_PlatformContacts[contact] = tracked = new List<PlatformEffector2D>(2);
                    if (!tracked.Contains(p)) tracked.Add(p);
                    bool outside = p.m_SurfaceArc <= 0f || (p.m_SurfaceArc < 360f && Vector2.Dot(up, outward) <= Mathf.Cos(p.m_SurfaceArc * .5f * Mathf.Deg2Rad));
                    if (p.Ignore(contact, other, outside)) contact.SetEnabled(false);
                }
                bool side = p.m_SideArc > 0f && Mathf.Abs(Vector2.Dot(right, outward)) > Mathf.Cos(p.m_SideArc * .5f * Mathf.Deg2Rad);
                if (side)
                {
                    if (!p.m_SideFriction) contact.SetFriction(0f);
                    if (!p.m_SideBounce) contact.SetRestitution(0f);
                    s_Overrides.Add(contact);
                }
            }
            else if (effector is SurfaceEffector2D surface)
            {
                if (!surface.m_Friction) contact.SetFriction(0f);
                if (!surface.m_Bounce) contact.SetRestitution(0f);
                s_Overrides.Add(contact);
            }
        }
        internal static void PostSolve(Contact contact)
        {
            var ea = Of(contact.FixtureA.UserData as Collider2D); var eb = Of(contact.FixtureB.UserData as Collider2D);
            if (ea is not SurfaceEffector2D && eb is not SurfaceEffector2D) return;
            contact.GetWorldManifold(out var world);
            var point = world.Points[0];
            if (ea is SurfaceEffector2D sa) Convey(sa, contact.FixtureA.Body, contact.FixtureB.Body, world.Normal, point);
            if (eb is SurfaceEffector2D sb) Convey(sb, contact.FixtureB.Body, contact.FixtureA.Body, -world.Normal, point);
        }
        static void Convey(SurfaceEffector2D surface, Body source, Body body, V2 normal, V2 point)
        {
            if (body.BodyType != BodyType.DynamicBody) return;
            if (surface.m_SpeedVariation != 0f) throw new NotSupportedException("Unbound: SurfaceEffector2D.speedVariation simulation is not implemented.");
            var tangent = new V2(normal.Y, -normal.X);
            float speed = V2.Dot(body.LinearVelocity - source.LinearVelocity, tangent);
            var impulse = body.Mass * (surface.m_Speed - speed) * surface.m_ForceScale * tangent;
            if (surface.m_ContactForce) body.ApplyLinearImpulse(impulse, point, true);
            else body.ApplyLinearImpulseToCenter(impulse, true);
        }
    }
}
