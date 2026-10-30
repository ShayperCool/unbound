using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unbound.Box2D.Dynamics;
using Unbound.Box2D.Dynamics.Contacts;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Collision;
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
            var tangent = new V2(normal.Y, -normal.X);
            float desired = surface.m_Speed + surface.m_SpeedVariation * surface.m_Random.NextValue();
            float speed = V2.Dot(body.LinearVelocity - source.LinearVelocity, tangent);
            var impulse = body.Mass * (desired - speed) * surface.m_ForceScale * tangent;
            if (surface.m_ContactForce) body.ApplyLinearImpulse(impulse, point, true);
            else body.ApplyLinearImpulseToCenter(impulse, true);
        }

        // Volume forces are queued after integration and survive until the next step.
        // Compound bodies receive Area/Point force once; their application points differ.
        internal static void AfterStep(float dt)
        {
            if (dt <= 0f) return;
            foreach (var effector in s_Effectors.Values)
            {
                if (effector is BuoyancyEffector2D buoyancy) { Float(buoyancy); continue; }
                if (effector is not AreaEffector2D && effector is not PointEffector2D) continue;
                var targets = new Dictionary<Body, (Collider2D source, Collider2D target)>();
                foreach (var component in effector.m_GameObject.m_Components)
                {
                    if (component is not Collider2D source || !source.m_UsedByEffector || !source.isActiveAndEnabled) continue;
                    foreach (var pair in Physics2DWorld.PairsOf(source))
                    {
                        var target = ReferenceEquals(pair.A, source) ? pair.B : pair.A;
                        var body = target.attachedRigidbody?.m_Body;
                        if (body == null || !body.IsEnabled || body.BodyType != BodyType.DynamicBody) continue;
                        if (!targets.TryGetValue(body, out var previous) || body.Fixtures.FindIndex(f => ReferenceEquals(f.UserData, target))
                            < body.Fixtures.FindIndex(f => ReferenceEquals(f.UserData, previous.target))) targets[body] = (source, target);
                    }
                }
                foreach (var entry in targets)
                {
                    var body = entry.Key; var source = entry.Value.source; var target = entry.Value.target;
                    V2 force, point; float drag, angularDrag;
                    if (effector is AreaEffector2D area)
                    {
                        float angle = area.m_Angle + (area.m_GlobalAngle ? 0f : effector.transform.eulerAngles.z);
                        float radians = angle * Mathf.Deg2Rad;
                        float magnitude = area.m_Force + area.m_Variation * area.m_Random.NextValue();
                        force = magnitude * new V2(Mathf.Cos(radians), Mathf.Sin(radians));
                        point = area.m_Target == EffectorSelection2D.Collider ? ShapeCenter(body) : body.GetWorldCenter();
                        drag = area.m_Drag; angularDrag = area.m_AngularDrag;
                    }
                    else
                    {
                        var p = (PointEffector2D)effector;
                        point = p.m_Target == EffectorSelection2D.Collider ? Center(target) : body.GetWorldCenter();
                        var origin = p.m_Source == EffectorSelection2D.Collider ? Center(source) : Physics2DWorld.ToB2((Vector2)effector.transform.position);
                        var delta = point - origin; float distance = delta.Length();
                        float magnitude = p.m_Force + p.m_Variation * p.m_Random.NextValue();
                        float scaled = Mathf.Max(.0001f, distance * p.m_DistanceScale);
                        if (p.m_Mode == EffectorForceMode2D.InverseLinear) magnitude /= scaled;
                        else if (p.m_Mode == EffectorForceMode2D.InverseSquared) magnitude /= scaled * scaled;
                        force = distance > 0f ? magnitude * (delta / distance) : V2.Zero;
                        drag = p.m_Drag; angularDrag = p.m_AngularDrag;
                    }
                    body.ApplyForce(force, point, true);
                    body.LinearVelocity *= 1f / (1f + dt * drag);
                    body.AngularVelocity *= 1f / (1f + dt * angularDrag);
                }
            }
        }
        static V2 Center(Collider2D collider)
        {
            return BoundsCenter(collider.m_Fixtures, Physics2DWorld.ToB2((Vector2)collider.transform.position));
        }
        static V2 ShapeCenter(Body body) => BoundsCenter(body.Fixtures, body.GetPosition());
        static V2 BoundsCenter(IEnumerable<Fixture> fixtures, V2 fallback)
        {
            bool any = false; V2 min = default, max = default;
            foreach (var fixture in fixtures)
            {
                var shape = fixture.Shape;
                float pad = shape is CapsuleShape ? 0f : shape is PolygonShape || shape is EdgeShape || shape is ChainShape ? shape.Radius : 0f;
                for (int child = 0; child < shape.GetChildCount(); child++)
                {
                    shape.ComputeAABB(out var bounds, fixture.Body.GetTransform(), child);
                    V2 lo = bounds.LowerBound + new V2(pad), hi = bounds.UpperBound - new V2(pad);
                    if (!any) { min = lo; max = hi; any = true; }
                    else { min = V2.Min(min, lo); max = V2.Max(max, hi); }
                }
            }
            return any ? .5f * (min + max) : fallback;
        }
        static void Float(BuoyancyEffector2D effector)
        {
            var colliders = new HashSet<Collider2D>();
            foreach (var component in effector.m_GameObject.m_Components)
            {
                if (component is not Collider2D source || !source.m_UsedByEffector || !source.isActiveAndEnabled) continue;
                foreach (var pair in Physics2DWorld.PairsOf(source))
                {
                    var target = ReferenceEquals(pair.A, source) ? pair.B : pair.A;
                    if (!target.m_IsTrigger && Overlap(source, target)) colliders.Add(target);
                }
            }
            float level = effector.transform.position.y + effector.m_Surface * effector.transform.lossyScale.y;
            var submerged = new Dictionary<Body, (float area, V2 weighted)>();
            foreach (var collider in colliders)
            {
                var body = collider.attachedRigidbody?.m_Body;
                if (body == null || !body.IsEnabled || body.BodyType != BodyType.DynamicBody) continue;
                foreach (var fixture in collider.m_Fixtures)
                {
                    float area = Submerged(fixture, level, out var center);
                    if (area <= 0f) continue;
                    submerged.TryGetValue(body, out var previous);
                    submerged[body] = (previous.area + area, previous.weighted + area * center);
                }
            }
            foreach (var entry in submerged)
            {
                var body = entry.Key; float area = entry.Value.area; var center = entry.Value.weighted / area;
                float flow = effector.m_Flow + effector.m_Variation * effector.m_Random.NextValue();
                float angle = effector.m_FlowAngle * Mathf.Deg2Rad;
                var force = -effector.m_Density * area * body.GravityScale * Physics2DWorld.ToB2(Physics2D.s_Gravity);
                force += area * flow * new V2(Mathf.Cos(angle), Mathf.Sin(angle));
                force -= effector.m_LinearDrag * area * body.GetLinearVelocityFromWorldPoint(center);
                if (force.Length() > Physics2D.s_LinearSleepTolerance) body.ApplyForce(force, center, true);
                float torque = -area * body.AngularVelocity * effector.m_AngularDrag;
                if (Mathf.Abs(torque) > Unbound.Box2D.Common.Settings.AngularSleepTolerance) body.ApplyTorque(torque, true);
            }
        }
        static float Submerged(Fixture fixture, float level, out V2 center)
        {
            center = V2.Zero;
            if (fixture.Shape is CircleShape circle)
            {
                center = fixture.Body.GetWorldPoint(circle.Position);
                float radius = circle.Radius, height = level - center.Y;
                if (height <= -radius) return 0f;
                if (height >= radius) return Mathf.PI * radius * radius;
                float width = Mathf.Sqrt(radius * radius - height * height);
                float area = radius * radius * (Mathf.Asin(height / radius) + Mathf.PI * .5f) + height * width;
                center.Y -= (2f / 3f) * width * width * width / area;
                return area;
            }
            if (fixture.Shape is not PolygonShape polygon) return 0f; // Edges have no submerged area.
            V2[] vertices;
            if (polygon is CapsuleShape capsule)
            {
                // Unity's fluid calculation uses the capsule's oriented bounding rectangle.
                var delta = capsule.P2 - capsule.P1;
                var axis = (delta.LengthSquared() > 0f ? V2.Normalize(delta) : V2.UnitY) * capsule.Radius;
                var side = new V2(axis.Y, -axis.X);
                vertices = new[] { capsule.P1 - axis + side, capsule.P2 + axis + side, capsule.P2 + axis - side, capsule.P1 - axis - side };
                if (vertices.All(v => fixture.Body.GetWorldPoint(v).Y < level))
                {
                    // Fully immersed capsules use their true mass area, unlike the partial clip.
                    capsule.ComputeMass(out var mass, 1f); center = fixture.Body.GetWorldPoint(mass.Center); return mass.Mass;
                }
            }
            else vertices = polygon.Vertices.Take(polygon.Count).ToArray();
            var points = new List<V2>(vertices.Length + 2);
            V2 previous = fixture.Body.GetWorldPoint(vertices[vertices.Length - 1]);
            for (int i = 0; i < vertices.Length; i++)
            {
                V2 current = fixture.Body.GetWorldPoint(vertices[i]);
                if ((previous.Y < level) != (current.Y < level))
                    points.Add(previous + (current - previous) * ((level - previous.Y) / (current.Y - previous.Y)));
                if (current.Y < level) points.Add(current);
                previous = current;
            }
            if (points.Count < 3) return 0f;
            V2 origin = points[0], weighted = V2.Zero; float twiceArea = 0f;
            for (int i = 1; i + 1 < points.Count; i++)
            {
                V2 a = points[i] - origin, b = points[i + 1] - origin;
                float cross = a.X * b.Y - a.Y * b.X;
                twiceArea += cross; weighted += cross * (a + b);
            }
            if (twiceArea <= 0f) return 0f;
            center = origin + weighted / (3f * twiceArea);
            return .5f * twiceArea;
        }
        static bool Overlap(Collider2D a, Collider2D b)
        {
            foreach (var fa in a.m_Fixtures)
                foreach (var fb in b.m_Fixtures)
                    for (int i = 0; i < fa.Shape.GetChildCount(); i++)
                        for (int j = 0; j < fb.Shape.GetChildCount(); j++)
                            if (CollisionUtils.TestOverlap(fa.Shape, i, fb.Shape, j, fa.Body.GetTransform(), fb.Body.GetTransform(), fa.Body.World.GJkProfile)) return true;
            return false;
        }
    }
}
