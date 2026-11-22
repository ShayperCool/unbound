using System.Collections.Generic;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using UnityEngine;
using B2 = System.Numerics.Vector2;
using B2Settings = Unbound.Box2D.Common.Settings;
using B2Transform = Unbound.Box2D.Common.Transform;
using W = Unbound.Runtime.Physics2DWorld;

namespace Unbound.Runtime
{
    /// <summary>
    /// Where a particle meets the 2D colliders in a step, for the Collision module's World type in
    /// Collision2D mode (matches the Unity 6000.0.71f1 Editor, tests/projects/particlerender
    /// collide/world2d/*). The particle is a circle of its collision radius moving in the XY plane
    /// from where its step started to where it ended; colliders count at any depth.
    /// <list type="bullet">
    /// <item>Candidates: the shapes whose bounds overlap the bounds of the swept circle, except
    /// triggers, shapes of other layers than the module's collidesWith and, unless
    /// enableDynamicColliders, shapes of Dynamic bodies.</item>
    /// <item>For each candidate in turn, Box2D's time of impact of the circle's movement against the
    /// shape at rest (the shape's skin included, its target separation computed with a fixed linear
    /// slop of 0.005), searched only up to the earliest impact found so far; an impact that is
    /// not later than that one becomes the latest candidate contact.</item>
    /// <item>From the latest candidate back, the first that Box2D's contact manifold confirms (the
    /// circle at its time of impact against the shape) is the hit: its normal points out of the
    /// shape, and a circle deeper in the shape than the contact's linear slop is pushed out along
    /// it by Physics2D.baumgarteScale times that depth, at most Physics2D.maxLinearCorrection
    /// (as Box2D's position correction). A particle that starts inside a shape meets it at once
    /// and is pushed out that way.</item>
    /// </list>
    /// The query allocates nothing once its lists have grown.
    /// </summary>
    internal static class Physics2DParticleQuery
    {
        /// <summary>The linear slop of the query's time of impact and position correction: Box2D's
        /// default, whatever Physics2D.defaultContactOffset says.</summary>
        const float LinearSlop = 0.005f;
        /// <summary>A particle's circle must be larger than this to collide.</summary>
        const float MinRadius = 1e-4f;
        /// <summary>A contact whose separation is below this is pushed out (single-precision
        /// epsilon times ten).</summary>
        const float PushSeparation = 1.1920929e-6f;

        /// <summary>The reusable state of a system's queries.</summary>
        internal sealed class Scratch : IQueryCallback
        {
            internal readonly List<(Fixture Fixture, int Child)> Candidates = new List<(Fixture, int)>();
            internal readonly List<(Fixture Fixture, int Child, float Time)> Impacts = new List<(Fixture, int, float)>();
            internal readonly CircleShape Circle = new CircleShape();
            internal readonly EdgeShape ChainEdge = new EdgeShape();
            internal int LayerMask, MaxShapes;
            internal bool DynamicColliders, Truncated;

            public bool QueryCallback(Fixture fixture, int childIndex)
            {
                if (fixture.IsSensor) return true;
                if (!DynamicColliders && fixture.Body.BodyType == BodyType.DynamicBody) return true;
                if (fixture.UserData is not Collider2D collider || collider.m_Destroyed) return true;
                var go = collider.m_GameObject;
                if (go == null || (LayerMask & (1 << go.layer)) == 0) return true;
                if (Candidates.Count >= MaxShapes)
                {
                    Truncated = true;
                    return false;
                }
                Candidates.Add((fixture, childIndex));
                return true;
            }
        }

        /// <summary>The hit of a particle's circle of <paramref name="radius"/> moving from
        /// <paramref name="start"/> to <paramref name="end"/> (world XY) in the physics scene of
        /// <paramref name="owner"/> (see the class comment): where the circle's center is at the
        /// contact, where it was at the time of impact before the push out
        /// (<paramref name="impact"/>), the normal out of the shape hit, and its collider. <paramref name="truncated"/>
        /// tells that more than <paramref name="maxShapes"/> shapes were near.</summary>
        internal static bool Contact(GameObject owner, Vector2 start, Vector2 end, float radius, int layerMask, bool dynamicColliders,
            int maxShapes, Scratch s, out Vector2 point, out Vector2 impact, out Vector2 normal, out Collider2D collider, out bool truncated)
        {
            point = impact = normal = default;
            collider = null;
            truncated = false;
            if (!(radius > MinRadius) || maxShapes <= 0) return false;
            W.PrepareQuery();
            var world = W.SpaceOf(owner).World;
            if (world == null) return false;

            var a = W.ToB2(start);
            var b = W.ToB2(end);
            var lower = B2.Min(a, b);
            var upper = B2.Max(a, b);
            s.Candidates.Clear();
            s.Impacts.Clear();
            s.LayerMask = layerMask;
            s.MaxShapes = maxShapes;
            s.DynamicColliders = dynamicColliders;
            s.Truncated = false;
            world.QueryAABB(s, new AABB(lower - new B2(radius), upper + new B2(radius)));
            truncated = s.Truncated;
            if (s.Candidates.Count == 0) return false;

            var circle = s.Circle;
            circle.Position = B2.Zero;
            circle.Radius = radius;
            var input = new ToiInput
            {
                SweepA = new Sweep { C0 = a, C = b },
                Tmax = 1f,
            };
            input.ProxyA.Set(circle, 0);
            foreach (var (fixture, child) in s.Candidates)
            {
                var bounds = fixture.Proxies[child].AABB;
                if (!((lower.X - radius) - bounds.UpperBound.X <= 0f && (lower.Y - radius) - bounds.UpperBound.Y <= 0f
                      && bounds.LowerBound.X - (upper.X + radius) <= 0f && bounds.LowerBound.Y - (upper.Y + radius) <= 0f))
                    continue;
                var body = fixture.Body;
                input.ProxyB.Set(fixture.Shape, child);
                input.SweepB = new Sweep
                {
                    LocalCenter = body.Sweep.LocalCenter,
                    C0 = body.Sweep.C, C = body.Sweep.C,
                    A0 = body.Sweep.A, A = body.Sweep.A,
                };
                TimeOfImpact.ComputeTimeOfImpact(out var toi, input, LinearSlop);
                if (toi.State != ToiOutput.ToiState.Overlapped && toi.State != ToiOutput.ToiState.Touching) continue;
                if (!(toi.Time <= input.Tmax)) continue;
                s.Impacts.Add((fixture, child, toi.Time));
                input.Tmax = toi.Time;
            }

            for (int i = s.Impacts.Count - 1; i >= 0; i--)
            {
                var (fixture, child, time) = s.Impacts[i];
                input.SweepA.GetTransform(out var xfParticle, time);
                var xfBody = fixture.Body.Transform;
                var manifold = new Manifold();
                bool particleIsA = Evaluate(ref manifold, fixture, child, s, xfParticle, xfBody);
                if (manifold.PointCount <= 0) continue;
                var worldManifold = new WorldManifold();
                float separation;
                B2 n;
                if (particleIsA)
                {
                    worldManifold.Initialize(manifold, xfParticle, radius, xfBody, fixture.Shape.Radius);
                    n = -worldManifold.Normal;
                }
                else
                {
                    worldManifold.Initialize(manifold, xfBody, fixture.Shape.Radius, xfParticle, radius);
                    n = worldManifold.Normal;
                }
                separation = worldManifold.Separations.Value0;
                var p = xfParticle.Position;
                impact = W.ToUnity(p);
                if (separation < PushSeparation)
                {
                    float correction = (separation + LinearSlop) * B2Settings.Baumgarte;
                    if (0f <= correction) correction = 0f;
                    if (correction < -B2Settings.MaxLinearCorrection) correction = -B2Settings.MaxLinearCorrection;
                    p -= n * correction;
                }
                point = W.ToUnity(p);
                normal = W.ToUnity(n);
                collider = (Collider2D)fixture.UserData;
                return true;
            }
            return false;
        }

        /// <summary>The contact manifold of the particle's circle and a shape, in the order Box2D's
        /// contact registry puts the pair (a circle pair keeps the particle first; a polygon,
        /// capsule, edge or chain comes before the circle). True when the particle is shape A.</summary>
        static bool Evaluate(ref Manifold manifold, Fixture fixture, int child, Scratch s, in B2Transform xfParticle, in B2Transform xfBody)
        {
            switch (fixture.Shape)
            {
                case CircleShape circle:
                    CollisionUtils.CollideCircles(ref manifold, s.Circle, xfParticle, circle, xfBody);
                    return true;
                case PolygonShape polygon:
                    CollisionUtils.CollidePolygonAndCircle(ref manifold, polygon, xfBody, s.Circle, xfParticle);
                    return false;
                case EdgeShape edge:
                    CollisionUtils.CollideEdgeAndCircle(ref manifold, edge, xfBody, s.Circle, xfParticle, useAdjacency: true);
                    return false;
                case ChainShape chain:
                    chain.GetChildEdge(s.ChainEdge, child);
                    CollisionUtils.CollideEdgeAndCircle(ref manifold, s.ChainEdge, xfBody, s.Circle, xfParticle, useAdjacency: true);
                    return false;
                default:
                    manifold.PointCount = 0;
                    return false;
            }
        }
    }
}
