using System;
using System.Collections.Generic;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using UnityEngine;
using B2 = System.Numerics.Vector2;
using B2Shape = Unbound.Box2D.Collision.Shapes.Shape;
using B2Settings = Unbound.Box2D.Common.Settings;
using B2Transform = Unbound.Box2D.Common.Transform;
using W = Unbound.Runtime.Physics2DWorld;

namespace Unbound.Runtime
{
    /// <summary>
    /// The 2D physics queries: ray casts, shape casts, overlaps and distances against the fixtures of
    /// the Box2D world. Results are one per collider (its nearest shape), ray and shape casts sorted
    /// by distance, overlaps by depth (Z); casts that start inside a collider report it at distance
    /// zero when Physics2D.queriesStartInColliders is set, and skip it otherwise.
    /// </summary>
    internal static class Physics2DQueries
    {
        sealed class AabbQuery : IQueryCallback
        {
            internal readonly List<Fixture> Found = new List<Fixture>();

            public bool QueryCallback(Fixture fixture)
            {
                Found.Add(fixture);
                return true;
            }
        }

        sealed class RayQuery : IRayCastCallback
        {
            internal readonly List<(Fixture Fixture, float Fraction, B2 Normal)> Hits = new List<(Fixture, float, B2)>();

            public float RayCastCallback(Fixture fixture, in B2 point, in B2 normal, float fraction)
            {
                Hits.Add((fixture, fraction, normal));
                return 1f; // every hit, unclipped
            }
        }

        static List<Fixture> FixturesIn(AABB aabb)
        {
            var query = new AabbQuery();
            W.World.QueryAABB(query, aabb);
            return query.Found;
        }

        /// <summary>A length for an infinite query that reaches past every proxy.</summary>
        static float Reach(Vector2 origin, float distance, float extent)
        {
            if (!float.IsInfinity(distance) && distance < 1e6f) return Mathf.Max(0f, distance);
            if (!W.World.ContactManager.BroadPhase.TryGetBounds(out var bounds)) return 1f;
            float far = 0f;
            foreach (var corner in new[] { bounds.LowerBound, bounds.UpperBound, new B2(bounds.LowerBound.X, bounds.UpperBound.Y), new B2(bounds.UpperBound.X, bounds.LowerBound.Y) })
                far = Mathf.Max(far, (W.ToUnity(corner) - origin).magnitude);
            return Mathf.Min(far + extent + 1f, distance);
        }

        static bool Passes(Collider2D c, ContactFilter2D filter) => c != null && !c.m_Destroyed && filter.Accepts(c);

        static void Keep(Dictionary<Collider2D, RaycastHit2D> best, RaycastHit2D hit)
        {
            if (!best.TryGetValue(hit.collider, out var current) || hit.distance < current.distance) best[hit.collider] = hit;
        }

        static List<RaycastHit2D> Sorted(Dictionary<Collider2D, RaycastHit2D> best)
        {
            var list = new List<RaycastHit2D>(best.Values);
            // Stable by distance.
            var keyed = new List<(float Distance, int Index, RaycastHit2D Hit)>(list.Count);
            for (int i = 0; i < list.Count; i++) keyed.Add((list[i].distance, i, list[i]));
            keyed.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));
            for (int i = 0; i < keyed.Count; i++) list[i] = keyed[i].Hit;
            return list;
        }

        // ------------------------------------------------------------------ rays

        internal static List<RaycastHit2D> Raycast(Vector2 origin, Vector2 direction, float distance, ContactFilter2D filter)
        {
            W.PrepareQuery();
            var best = new Dictionary<Collider2D, RaycastHit2D>(ReferenceComparer.Instance);
            if (float.IsNaN(distance) || distance < 0f) distance = 0f;
            var dir = direction.normalized;
            float length = Reach(origin, distance, 0f);
            float fractionScale = float.IsInfinity(distance) ? length : distance;

            // Colliders the ray starts in.
            var inside = new HashSet<Collider2D>(ReferenceComparer.Instance);
            var pointBox = new AABB(W.ToB2(origin) - new B2(1e-4f), W.ToB2(origin) + new B2(1e-4f));
            foreach (var f in FixturesIn(pointBox))
            {
                var c = f.UserData as Collider2D;
                if (c == null || !f.TestPoint(W.ToB2(origin))) continue;
                inside.Add(c);
                if (!Physics2D.s_QueriesStartInColliders || !Passes(c, filter)) continue;
                var normal = -dir;
                if (filter.IsFilteringNormalAngle(normal)) continue;
                Keep(best, new RaycastHit2D(c, origin, normal, 0f, 0f, origin));
            }

            if (dir != Vector2.zero && length > 0f)
            {
                var query = new RayQuery();
                W.World.RayCast(query, W.ToB2(origin), W.ToB2(origin + dir * length));
                foreach (var (fixture, fraction, b2Normal) in query.Hits)
                {
                    var c = fixture.UserData as Collider2D;
                    if (c == null || inside.Contains(c) || !Passes(c, filter)) continue;
                    var normal = W.ToUnity(b2Normal);
                    if (filter.IsFilteringNormalAngle(normal)) continue;
                    float hitDistance = fraction * length;
                    var point = origin + dir * hitDistance;
                    Keep(best, new RaycastHit2D(c, point, normal, hitDistance, fractionScale > 0f ? hitDistance / fractionScale : 0f, point));
                }
            }
            return Sorted(best);
        }

        /// <summary>A 3D ray against the colliders, each in the XY plane at its own depth: the hits
        /// in order along the ray.</summary>
        internal static List<RaycastHit2D> GetRayIntersection(Ray ray, float distance, int layerMask)
        {
            W.PrepareQuery();
            var hits = new List<RaycastHit2D>();
            var dir = ray.direction.normalized;
            var origin = ray.origin;
            if (float.IsNaN(distance) || distance < 0f) return hits;
            float length = float.IsInfinity(distance) ? 1e5f : distance;
            var end = origin + dir * length;
            var lower = new Vector2(Mathf.Min(origin.x, end.x), Mathf.Min(origin.y, end.y));
            var upper = new Vector2(Mathf.Max(origin.x, end.x), Mathf.Max(origin.y, end.y));
            var seen = new HashSet<Collider2D>(ReferenceComparer.Instance);
            foreach (var f in FixturesIn(new AABB(W.ToB2(lower), W.ToB2(upper))))
            {
                var c = f.UserData as Collider2D;
                if (c == null || c.m_Destroyed || seen.Contains(c)) continue;
                if ((layerMask & (1 << c.m_GameObject.m_Layer)) == 0) continue;
                if (c.m_IsTrigger && !Physics2D.s_QueriesHitTriggers) continue;
                float z = c.m_GameObject.m_Transform.position.z;
                float t;
                if (Mathf.Abs(dir.z) < 1e-6f)
                {
                    if (Mathf.Abs(origin.z - z) > 1e-4f) continue;
                    t = 0f;
                }
                else
                {
                    t = (z - origin.z) / dir.z;
                }
                if (t < 0f || t > length) continue;
                var p = origin + dir * t;
                bool hit = false;
                foreach (var g in c.m_Fixtures)
                    if (g.TestPoint(new B2(p.x, p.y))) { hit = true; break; }
                if (!hit) continue;
                seen.Add(c);
                hits.Add(new RaycastHit2D(c, new Vector2(p.x, p.y), -new Vector2(dir.x, dir.y).normalized, t, length > 0f ? t / length : 0f, new Vector2(p.x, p.y)));
            }
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            return hits;
        }

        // ------------------------------------------------------------------ shape casts

        /// <summary>Casts a shape placed at <paramref name="pose"/> along a direction.</summary>
        internal static List<RaycastHit2D> ShapeCast(B2Shape shape, B2Transform pose, Vector2 direction, float distance, ContactFilter2D filter)
        {
            W.PrepareQuery();
            var best = new Dictionary<Collider2D, RaycastHit2D>(ReferenceComparer.Instance);
            CastShape(shape, pose, direction, distance, filter, best, null, null, new HashSet<Collider2D>(ReferenceComparer.Instance));
            return Sorted(best);
        }

        /// <summary>Casts the shapes of colliders (at their bodies, or at <paramref name="pose"/>):
        /// the colliders themselves and those of <paramref name="ignoreBody"/> are not hit.</summary>
        internal static List<RaycastHit2D> CastColliders(IEnumerable<Collider2D> colliders, B2Transform? pose, Vector2 direction, float distance, ContactFilter2D filter, Rigidbody2D ignoreBody)
        {
            W.PrepareQuery();
            var best = new Dictionary<Collider2D, RaycastHit2D>(ReferenceComparer.Instance);
            var exclude = new HashSet<Collider2D>(ReferenceComparer.Instance);
            foreach (var c in colliders) exclude.Add(c);
            var skipped = new HashSet<Collider2D>(ReferenceComparer.Instance);
            foreach (var c in colliders)
            {
                foreach (var f in c.m_Fixtures)
                {
                    var xf = pose ?? f.Body.GetTransform();
                    for (int child = 0; child < f.Shape.GetChildCount(); child++)
                        CastShape(f.Shape, xf, direction, distance, filter, best, exclude, ignoreBody, skipped, child);
                }
            }
            return Sorted(best);
        }

        static void CastShape(B2Shape shape, B2Transform pose, Vector2 direction, float distance, ContactFilter2D filter,
            Dictionary<Collider2D, RaycastHit2D> best, HashSet<Collider2D> exclude, Rigidbody2D ignoreBody, HashSet<Collider2D> skipped, int childIndex = 0)
        {
            if (float.IsNaN(distance) || distance < 0f) distance = 0f;
            var dir = direction.normalized;
            shape.ComputeAABB(out var start, pose, childIndex);
            var origin = W.ToUnity(pose.Position);
            float extent = (W.ToUnity(start.UpperBound) - W.ToUnity(start.LowerBound)).magnitude;
            float length = Reach(origin, distance, extent);
            float fractionScale = float.IsInfinity(distance) ? length : distance;
            var translation = W.ToB2(dir * length);
            var endPose = new B2Transform(pose.Position + translation, pose.Rotation);
            shape.ComputeAABB(out var end, endPose, childIndex);
            var swept = new AABB();
            swept.Combine(start, end);

            var proxyB = new DistanceProxy();
            proxyB.Set(shape, childIndex);
            foreach (var f in FixturesIn(swept))
            {
                var c = f.UserData as Collider2D;
                if (c == null || !Passes(c, filter)) continue;
                if (exclude != null && exclude.Contains(c)) continue;
                if (ignoreBody != null && ReferenceEquals(c.m_AttachedBody, ignoreBody)) continue;
                if (skipped.Contains(c)) continue;
                var xfA = f.Body.GetTransform();
                for (int child = 0; child < f.Shape.GetChildCount(); child++)
                {
                    var proxyA = new DistanceProxy();
                    proxyA.Set(f.Shape, child);

                    // Starting in the collider?
                    var input = new DistanceInput { ProxyA = proxyA, ProxyB = proxyB, TransformA = xfA, TransformB = pose, UseRadii = false };
                    var cache = new SimplexCache();
                    DistanceAlgorithm.Distance(out var separation, ref cache, input);
                    if (QueryParityCast.StartsInside(proxyA, proxyB, separation.Distance))
                    {
                        if (!Physics2D.s_QueriesStartInColliders)
                        {
                            // A cast that starts in a collider does not see it at all.
                            skipped.Add(c);
                            best.Remove(c);
                            break;
                        }
                        // Unity's hit is the contact's: the normal that separates the shapes
                        // (from the collider to the cast shape) and the point between them.
                        var normal = -dir;
                        var point = W.ToUnity(0.5f * (separation.PointA + separation.PointB));
                        if (StartContact(f.Shape, child, xfA, shape, childIndex, pose, out var startNormal, out var startPoint))
                        {
                            normal = W.ToUnity(startNormal);
                            point = W.ToUnity(startPoint);
                        }
                        if (filter.IsFilteringNormalAngle(normal)) continue;
                        Keep(best, new RaycastHit2D(c, point, normal, 0f, 0f, origin));
                        continue;
                    }
                    if (length <= 0f || dir == Vector2.zero) continue;
                    var cast = new ShapeCastInput { ProxyA = proxyA, ProxyB = proxyB, TransformA = xfA, TransformB = pose, TranslationB = translation };
                    // Unity's casts stop a linear slop deeper than Box2D's target separation.
                    if (!QueryParityCast.Cast(out var output, cast)) continue;
                    var hitPose = new B2Transform(pose.Position + output.Lambda * translation, pose.Rotation);
                    var (b2Point, b2Normal) = Contact(proxyA, xfA, proxyB, hitPose, output.Point, output.Normal);
                    if (f.Shape is PolygonShape polygonA && shape is PolygonShape polygonB
                        && (polygonA.Count == 2 || polygonA.Radius <= B2Settings.PolygonRadius
                            || c is BoxCollider2D box && box.edgeRadius == 0f)
                        && (polygonB.Count == 2 || polygonB.Radius <= B2Settings.PolygonRadius))
                    {
                        var manifold = new Manifold();
                        QueryParityManifold.CollidePolygons(ref manifold, polygonB, hitPose, polygonA, xfA, out float coreSpread);
                        if (manifold.PointCount > 0)
                        {
                            var world = new WorldManifold();
                            world.Initialize(manifold, hitPose, shape.Radius, xfA, f.Shape.Radius);
                            b2Normal = -world.Normal;
                            int selected = manifold.PointCount == 2 && world.Separations[1] < world.Separations[0] ? 1 : 0;
                            b2Point = world.Points[selected];
                            // Native averages contacts within the fixed query tolerance of the
                            // deepest separation. Measured across angles, scales and offsets.
                            if (manifold.PointCount == 2 && coreSpread <= QueryParityCast.ContactSeparationTolerance)
                                b2Point = 0.5f * (world.Points[0] + world.Points[1]);
                        }
                    }
                    var hitNormal = W.ToUnity(b2Normal);
                    if (filter.IsFilteringNormalAngle(hitNormal)) continue;
                    float hitDistance = output.Lambda * length;
                    Keep(best, new RaycastHit2D(c, W.ToUnity(b2Point), hitNormal, hitDistance,
                        fractionScale > 0f ? hitDistance / fractionScale : 0f, origin + dir * hitDistance));
                }
            }
        }

        /// <summary>
        /// The point and normal of a cast's hit, as Unity reports them: the normal from the hit
        /// shape (A) to the cast one (B) at the hit pose, and the point midway between their
        /// surfaces; where they touch along parallel features (two faces), at the middle of the
        /// part they share.
        /// </summary>
        static (B2 Point, B2 Normal) Contact(DistanceProxy a, B2Transform xfA, DistanceProxy b, B2Transform xfB, B2 point, B2 normal)
        {
            var input = new DistanceInput { ProxyA = a, ProxyB = b, TransformA = xfA, TransformB = xfB, UseRadii = false };
            var cache = new SimplexCache();
            DistanceAlgorithm.Distance(out var core, ref cache, input);
            var n = core.PointB - core.PointA;
            if (core.Distance <= 1e-6f || n.LengthSquared() <= 0f) return (point, normal);
            n = B2.Normalize(n);
            if (QueryParityContact.TryContact(a, xfA, b, xfB, cache, out var clippedPoint, out var clippedNormal))
                return (clippedPoint, clippedNormal);
            float rA = Math.Max(a.Radius, B2Settings.PolygonRadius);
            float rB = Math.Max(b.Radius, B2Settings.PolygonRadius);
            // Midway between the surfaces along the normal.
            float surfaceA = B2.Dot(core.PointA, n) + rA;
            float surfaceB = B2.Dot(core.PointB, n) - rB;
            float across = 0.5f * (surfaceA + surfaceB);
            var t = new B2(-n.Y, n.X);
            float along = 0.5f * (B2.Dot(core.PointA, t) + B2.Dot(core.PointB, t));
            // The features facing each other: A's vertices furthest along n, B's least.
            const float flat = 1e-3f;
            float maxA = float.MinValue, minB = float.MaxValue;
            for (int i = 0; i < a.Count; i++) maxA = Math.Max(maxA, B2.Dot(Mul(xfA, a.Vertices[i]), n));
            for (int i = 0; i < b.Count; i++) minB = Math.Min(minB, B2.Dot(Mul(xfB, b.Vertices[i]), n));
            float loA = float.MaxValue, hiA = float.MinValue, loB = float.MaxValue, hiB = float.MinValue;
            int countA = 0, countB = 0;
            for (int i = 0; i < a.Count; i++)
            {
                var v = Mul(xfA, a.Vertices[i]);
                if (B2.Dot(v, n) < maxA - flat) continue;
                float s = B2.Dot(v, t);
                loA = Math.Min(loA, s);
                hiA = Math.Max(hiA, s);
                countA++;
            }
            for (int i = 0; i < b.Count; i++)
            {
                var v = Mul(xfB, b.Vertices[i]);
                if (B2.Dot(v, n) > minB + flat) continue;
                float s = B2.Dot(v, t);
                loB = Math.Min(loB, s);
                hiB = Math.Max(hiB, s);
                countB++;
            }
            if (countA >= 2 && countB >= 2)
            {
                float lo = Math.Max(loA, loB), hi = Math.Min(hiA, hiB);
                if (hi > lo) along = 0.5f * (lo + hi);
            }
            return (across * n + along * t, n);
        }

        /// <summary>The contact of two overlapping shapes (Box2D's manifold): its normal from A to
        /// B and its deepest point; false when Box2D finds no contact point.</summary>
        static bool StartContact(B2Shape a, int childA, B2Transform xfA, B2Shape b, int childB, B2Transform xfB, out B2 normal, out B2 point)
        {
            normal = default;
            point = default;
            if (a is ChainShape chainA)
            {
                chainA.GetChildEdge(out var edge, childA);
                a = edge;
            }
            if (b is ChainShape chainB)
            {
                chainB.GetChildEdge(out var edge, childB);
                b = edge;
            }
            var manifold = new Manifold();
            bool flip = false;
            switch (a, b)
            {
                // Query shape first also fixes the otherwise ambiguous normal when
                // the two centers coincide, without changing noncoincident contacts.
                case (CircleShape ca, CircleShape cb): CollisionUtils.CollideCircles(ref manifold, cb, xfB, ca, xfA); flip = true; break;
                case (PolygonShape pa, CircleShape cb): CollisionUtils.CollidePolygonAndCircle(ref manifold, pa, xfA, cb, xfB); break;
                case (CircleShape ca, PolygonShape pb): CollisionUtils.CollidePolygonAndCircle(ref manifold, pb, xfB, ca, xfA); flip = true; break;
                case (PolygonShape pa, PolygonShape pb): CollisionUtils.CollidePolygons(ref manifold, pa, xfA, pb, xfB); break;
                case (EdgeShape ea, CircleShape cb): CollisionUtils.CollideEdgeAndCircle(ref manifold, ea, xfA, cb, xfB); break;
                case (EdgeShape ea, PolygonShape pb): CollisionUtils.CollideEdgeAndPolygon(ref manifold, ea, xfA, pb, xfB); break;
                case (CircleShape ca, EdgeShape eb): CollisionUtils.CollideEdgeAndCircle(ref manifold, eb, xfB, ca, xfA); flip = true; break;
                case (PolygonShape pa, EdgeShape eb): CollisionUtils.CollideEdgeAndPolygon(ref manifold, eb, xfB, pa, xfA); flip = true; break;
                default: return false;
            }
            if (manifold.PointCount == 0) return false;
            var world = new WorldManifold();
            if (flip) world.Initialize(manifold, xfB, b.Radius, xfA, a.Radius);
            else world.Initialize(manifold, xfA, a.Radius, xfB, b.Radius);
            normal = flip ? -world.Normal : world.Normal;
            // The deepest of the contact's points.
            int deepest = manifold.PointCount > 1 && world.Separations[1] < world.Separations[0] ? 1 : 0;
            point = world.Points[deepest];
            if (manifold.PointCount == 2
                && Math.Abs(world.Separations[0] - world.Separations[1]) <= QueryParityCast.ContactSeparationTolerance)
                point = 0.5f * (world.Points[0] + world.Points[1]);
            return true;
        }

        static B2 Mul(B2Transform xf, B2 v) => Unbound.Box2D.Common.MathUtils.Mul(xf, v);

        // ------------------------------------------------------------------ overlaps

        static List<Collider2D> ByDepth(List<Collider2D> colliders)
        {
            var keyed = new List<(float Z, int Index, Collider2D C)>(colliders.Count);
            for (int i = 0; i < colliders.Count; i++) keyed.Add((colliders[i].m_GameObject.m_Transform.position.z, i, colliders[i]));
            keyed.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Index.CompareTo(b.Index));
            for (int i = 0; i < keyed.Count; i++) colliders[i] = keyed[i].C;
            return colliders;
        }

        internal static List<Collider2D> OverlapPoint(Vector2 point, ContactFilter2D filter)
        {
            W.PrepareQuery();
            var result = new List<Collider2D>();
            var seen = new HashSet<Collider2D>(ReferenceComparer.Instance);
            var p = W.ToB2(point);
            foreach (var f in FixturesIn(new AABB(p - new B2(1e-4f), p + new B2(1e-4f))))
            {
                var c = f.UserData as Collider2D;
                if (c == null || seen.Contains(c) || !Passes(c, filter) || !f.TestPoint(p)) continue;
                seen.Add(c);
                result.Add(c);
            }
            return ByDepth(result);
        }

        internal static List<Collider2D> OverlapShape(B2Shape shape, B2Transform pose, ContactFilter2D filter)
        {
            W.PrepareQuery();
            var result = new List<Collider2D>();
            var seen = new HashSet<Collider2D>(ReferenceComparer.Instance);
            Overlap(shape, pose, filter, seen, result, null, null);
            return ByDepth(result);
        }

        internal static List<Collider2D> OverlapColliders(IEnumerable<Collider2D> colliders, B2Transform? pose, ContactFilter2D filter, Rigidbody2D ignoreBody)
        {
            W.PrepareQuery();
            var result = new List<Collider2D>();
            var seen = new HashSet<Collider2D>(ReferenceComparer.Instance);
            var exclude = new HashSet<Collider2D>(ReferenceComparer.Instance);
            foreach (var c in colliders) exclude.Add(c);
            foreach (var c in colliders)
                foreach (var f in c.m_Fixtures)
                {
                    var xf = pose ?? f.Body.GetTransform();
                    for (int child = 0; child < f.Shape.GetChildCount(); child++)
                        Overlap(f.Shape, xf, filter, seen, result, exclude, c.m_AttachedBody ?? ignoreBody, child);
                }
            return ByDepth(result);
        }

        static void Overlap(B2Shape shape, B2Transform pose, ContactFilter2D filter, HashSet<Collider2D> seen, List<Collider2D> result,
            HashSet<Collider2D> exclude, Rigidbody2D ignoreBody, int childIndex = 0)
        {
            shape.ComputeAABB(out var aabb, pose, childIndex);
            foreach (var f in FixturesIn(aabb))
            {
                var c = f.UserData as Collider2D;
                if (c == null || seen.Contains(c) || !Passes(c, filter)) continue;
                if (exclude != null && exclude.Contains(c)) continue;
                if (ignoreBody != null && ReferenceEquals(c.m_AttachedBody, ignoreBody)) continue;
                var xf = f.Body.GetTransform();
                for (int child = 0; child < f.Shape.GetChildCount(); child++)
                {
                    if (!CollisionUtils.TestOverlap(f.Shape, child, shape, childIndex, xf, pose, null)) continue;
                    seen.Add(c);
                    result.Add(c);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ distance

        /// <summary>The distance between two colliders (at their bodies, or at the given poses).</summary>
        internal static ColliderDistance2D Distance(Collider2D a, B2Transform? poseA, Collider2D b, B2Transform? poseB, bool overrideB)
        {
            W.PrepareQuery();
            var best = new ColliderDistance2D { isValid = false };
            if (a == null || b == null || ReferenceEquals(a, b)) return best;
            foreach (var fa in a.m_Fixtures)
            {
                var xfA = poseA ?? fa.Body.GetTransform();
                foreach (var fb in b.m_Fixtures)
                {
                    var xfB = overrideB && poseB.HasValue ? poseB.Value : fb.Body.GetTransform();
                    for (int ia = 0; ia < fa.Shape.GetChildCount(); ia++)
                        for (int ib = 0; ib < fb.Shape.GetChildCount(); ib++)
                        {
                            var d = ShapeDistance(fa.Shape, ia, xfA, fb.Shape, ib, xfB);
                            if (!best.isValid || d.distance < best.distance) best = d;
                        }
                }
            }
            return best;
        }

        static ColliderDistance2D ShapeDistance(B2Shape shapeA, int indexA, B2Transform xfA, B2Shape shapeB, int indexB, B2Transform xfB)
        {
            var input = new DistanceInput { TransformA = xfA, TransformB = xfB, UseRadii = true };
            input.ProxyA.Set(shapeA, indexA);
            input.ProxyB.Set(shapeB, indexB);
            var cache = new SimplexCache();
            DistanceAlgorithm.Distance(out var output, ref cache, input);
            var pointA = W.ToUnity(output.PointA);
            var pointB = W.ToUnity(output.PointB);
            if (output.Distance > 10f * B2Settings.Epsilon)
                return new ColliderDistance2D(pointA, pointB, (pointA - pointB) / output.Distance, output.Distance);

            // Overlapping: the deepest point of their contact manifold gives the penetration.
            if (TryManifold(shapeA, indexA, xfA, shapeB, indexB, xfB, out var normalAB, out var separation, out var point))
            {
                var cA = point - 0.5f * separation * normalAB;
                var cB = point + 0.5f * separation * normalAB;
                return new ColliderDistance2D(cA, cB, -normalAB, separation);
            }
            var away = pointA - W.ToUnity(xfB.Position);
            return new ColliderDistance2D(pointA, pointB, away.sqrMagnitude > 0f ? away.normalized : Vector2.up, 0f);
        }

        /// <summary>The contact manifold of two shapes: the normal from A to B, the smallest
        /// separation and its point.</summary>
        static bool TryManifold(B2Shape shapeA, int indexA, B2Transform xfA, B2Shape shapeB, int indexB, B2Transform xfB,
            out Vector2 normalAB, out float separation, out Vector2 point)
        {
            normalAB = default;
            separation = 0f;
            point = default;
            B2Shape first = shapeA, second = shapeB;
            B2Transform xfFirst = xfA, xfSecond = xfB;
            int indexFirst = indexA;
            bool swapped = false;
            // Box2D's functions take the shape kinds in a fixed order.
            if (Rank(shapeA) < Rank(shapeB))
            {
                first = shapeB; second = shapeA; xfFirst = xfB; xfSecond = xfA; indexFirst = indexB;
                swapped = true;
            }
            var manifold = new Manifold();
            switch (first)
            {
                case CircleShape ca when second is CircleShape cb:
                    CollisionUtils.CollideCircles(ref manifold, ca, xfFirst, cb, xfSecond);
                    break;
                case PolygonShape pa when second is CircleShape cb:
                    CollisionUtils.CollidePolygonAndCircle(ref manifold, pa, xfFirst, cb, xfSecond);
                    break;
                case PolygonShape pa when second is PolygonShape pb:
                    CollisionUtils.CollidePolygons(ref manifold, pa, xfFirst, pb, xfSecond);
                    break;
                case EdgeShape ea when second is CircleShape cb:
                    CollisionUtils.CollideEdgeAndCircle(ref manifold, ea, xfFirst, cb, xfSecond);
                    break;
                case EdgeShape ea when second is PolygonShape pb:
                    CollisionUtils.CollideEdgeAndPolygon(ref manifold, ea, xfFirst, pb, xfSecond);
                    break;
                case ChainShape chain when second is CircleShape cb:
                {
                    chain.GetChildEdge(out var edge, indexFirst);
                    CollisionUtils.CollideEdgeAndCircle(ref manifold, edge, xfFirst, cb, xfSecond);
                    break;
                }
                case ChainShape chain when second is PolygonShape pb:
                {
                    chain.GetChildEdge(out var edge, indexFirst);
                    CollisionUtils.CollideEdgeAndPolygon(ref manifold, edge, xfFirst, pb, xfSecond);
                    break;
                }
                default:
                    return false;
            }
            if (manifold.PointCount == 0) return false;
            var world = new WorldManifold();
            world.Initialize(manifold, xfFirst, first.Radius, xfSecond, second.Radius);
            int deepest = 0;
            for (int i = 1; i < manifold.PointCount; i++)
                if (world.Separations[i] < world.Separations[deepest]) deepest = i;
            var n = W.ToUnity(world.Normal);
            separation = world.Separations[deepest];
            point = W.ToUnity(world.Points[deepest]);
            normalAB = swapped ? -n : n;
            if (swapped)
            {
                // The manifold point is symmetric; the separation and normal now read from A to B.
            }
            return true;
        }

        /// <summary>Box2D's collide functions take edges and chains first, then polygons, then circles.</summary>
        static int Rank(B2Shape s) => s switch
        {
            ChainShape _ => 3,
            EdgeShape _ => 3,
            PolygonShape _ => 2,
            _ => 1,
        };

        internal static Vector2 ClosestPoint(Vector2 position, IEnumerable<Collider2D> colliders)
        {
            W.PrepareQuery();
            var p = W.ToB2(position);
            float best = float.PositiveInfinity;
            var result = position;
            var proxyB = new DistanceProxy();
            proxyB.Set(new[] { p }, 1, 0f);
            foreach (var c in colliders)
            {
                foreach (var f in c.m_Fixtures)
                {
                    if (f.TestPoint(p)) return position;
                    var xf = f.Body.GetTransform();
                    for (int child = 0; child < f.Shape.GetChildCount(); child++)
                    {
                        var input = new DistanceInput { ProxyB = proxyB, TransformA = xf, TransformB = new B2Transform(B2.Zero, 0f), UseRadii = true };
                        input.ProxyA.Set(f.Shape, child);
                        var cache = new SimplexCache();
                        DistanceAlgorithm.Distance(out var output, ref cache, input);
                        if (output.Distance < best)
                        {
                            best = output.Distance;
                            result = W.ToUnity(output.PointA);
                        }
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ results

        internal static int CopyHits(List<RaycastHit2D> hits, RaycastHit2D[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int count = Math.Min(hits.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = hits[i];
            return count;
        }

        internal static int CopyHits(List<RaycastHit2D> hits, List<RaycastHit2D> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(hits);
            return results.Count;
        }

        internal static int CopyColliders(List<Collider2D> found, Collider2D[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int count = Math.Min(found.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = found[i];
            return count;
        }

        internal static int CopyColliders(List<Collider2D> found, List<Collider2D> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(found);
            return results.Count;
        }

        /// <summary>Colliders by identity (a destroyed collider equals null under Unity's equality).</summary>
        internal sealed class ReferenceComparer : IEqualityComparer<Collider2D>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(Collider2D x, Collider2D y) => ReferenceEquals(x, y);

            public int GetHashCode(Collider2D obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
