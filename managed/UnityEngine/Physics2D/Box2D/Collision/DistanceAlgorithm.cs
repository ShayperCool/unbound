using System;
using System.Diagnostics;
using System.Numerics;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision
{
    internal static class DistanceAlgorithm
    {
        /// <summary>
        /// GJK碰撞检测
        /// </summary>
        /// <param name="output"></param>
        /// <param name="cache"></param>
        /// <param name="input"></param>
        /// <param name="gJkProfile"></param>
        public static void Distance(
            out DistanceOutput output,
            ref SimplexCache cache,
            in DistanceInput input,
            in GJkProfile gJkProfile = null)
        {
            if (gJkProfile != null)
            {
                ++gJkProfile.GjkCalls;
            }

            output = new DistanceOutput();
            ref readonly var proxyA = ref input.ProxyA;
            ref readonly var proxyB = ref input.ProxyB;

            var transformA = input.TransformA;
            var transformB = input.TransformB;

            // Initialize the simplex.
            var simplex = new Simplex();
            simplex.ReadCache(
                ref cache,
                proxyA,
                transformA,
                proxyB,
                transformB);

            // Get simplex vertices as an array.
            ref var vertices = ref simplex.Vertices;
            const int maxIters = 20;

            // These store the vertices of the last simplex so that we
            // can check for duplicates and prevent cycling.
            Span<int> saveA = stackalloc int[3];
            Span<int> saveB = stackalloc int[3];

            // Main iteration loop.
            var iter = 0;
            while (iter < maxIters)
            {
                // Copy simplex so we can identify duplicates.
                var saveCount = simplex.Count;
                for (var i = 0; i < simplex.Count; ++i)
                {
                    saveA[i] = vertices[i].IndexA;
                    saveB[i] = vertices[i].IndexB;
                }

                switch (simplex.Count)
                {
                    case 1:
                        break;

                    case 2:
                        simplex.Solve2();
                        break;

                    case 3:
                        simplex.Solve3();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(simplex.Count));
                }

                // If we have 3 points, then the origin is in the corresponding triangle.
                if (simplex.Count == 3)
                {
                    break;
                }

                // Get search direction.
                var d = simplex.GetSearchDirection();

                // Ensure the search direction is numerically fit.
                if (d.LengthSquared() < Settings.Epsilon * Settings.Epsilon)
                {
                    // The origin is probably contained by a line segment
                    // or triangle. Thus the shapes are overlapped.

                    // We can't return zero here even though there may be overlap.
                    // In case the simplex is a point, segment, or triangle it is difficult
                    // to determine if the origin is contained in the CSO or very close to it.
                    break;
                }

                // Compute a tentative new simplex vertex using support points.
                ref var vertex = ref vertices[simplex.Count];
                vertex.IndexA = proxyA.GetSupport(MathUtils.MulT(transformA.Rotation, -d));
                vertex.Wa = MathUtils.Mul(transformA, proxyA.GetVertex(vertex.IndexA));

                vertex.IndexB = proxyB.GetSupport(MathUtils.MulT(transformB.Rotation, d));
                vertex.Wb = MathUtils.Mul(transformB, proxyB.GetVertex(vertex.IndexB));
                vertex.W = vertex.Wb - vertex.Wa;

                // Iteration count is equated to the number of support point calls.
                ++iter;
                if (gJkProfile != null)
                {
                    ++gJkProfile.GjkIters;
                }

                // Check for duplicate support points. This is the main termination criteria.
                var duplicate = false;
                for (var i = 0; i < saveCount; ++i)
                {
                    if (vertex.IndexA == saveA[i] && vertex.IndexB == saveB[i])
                    {
                        duplicate = true;
                        break;
                    }
                }

                // If we found a duplicate support point we must exit to avoid cycling.
                if (duplicate)
                {
                    break;
                }

                // New vertex is ok and needed.
                ++simplex.Count;
            }

            if (gJkProfile != null)
            {
                gJkProfile.GjkMaxIters = Math.Max(gJkProfile.GjkMaxIters, iter);
            }

            // Prepare output.
            simplex.GetWitnessPoints(out output.PointA, out output.PointB);
            output.Distance = Vector2.Distance(output.PointA, output.PointB);
            output.Iterations = iter;

            // Cache the simplex.
            simplex.WriteCache(ref cache);

            // Apply radii if requested.
            if (input.UseRadii)
            {
                if (output.Distance < Settings.Epsilon)
                {
                    // Shapes are too close to safely compute normal
                    var p = 0.5f * (output.PointA + output.PointB);
                    output.PointA = p;
                    output.PointB = p;
                    output.Distance = 0.0f;
                }
                else
                {
                    // Keep closest points on perimeter even if overlapped, this way
                    // the points move smoothly.
                    var rA = proxyA.Radius;
                    var rB = proxyB.Radius;
                    var normal = output.PointB - output.PointA;
                    normal.Normalize();
                    output.Distance = Math.Max(0.0f, output.Distance - rA - rB);
                    output.PointA += rA * normal;
                    output.PointB -= rB * normal;
                }
            }
        }

        /// <summary>
        /// The point and normal of a cast's hit, as Unity reports them: the normal from the hit
        /// shape (A) to the cast one (B) at the hit pose, and the point midway between their
        /// surfaces; where they touch along parallel features (two faces), at the middle of the
        /// part they share.
        /// </summary>
        public static (Vector2 Point, Vector2 Normal) SurfaceContact(DistanceProxy a, Transform xfA, DistanceProxy b, Transform xfB, Vector2 point, Vector2 normal, float minimumRadius)
        {
            var input = new DistanceInput { ProxyA = a, ProxyB = b, TransformA = xfA, TransformB = xfB, UseRadii = false };
            var cache = new SimplexCache();
            DistanceAlgorithm.Distance(out var core, ref cache, input);
            var n = core.PointB - core.PointA;
            if (core.Distance <= 1e-6f || n.LengthSquared() <= 0f) return (point, normal);
            n = Vector2.Normalize(n);
            if (CollisionUtils.TryGetFaceContact(a, xfA, b, xfB, cache, out var clippedPoint, out var clippedNormal))
                return (clippedPoint, clippedNormal);
            float rA = Math.Max(a.Radius, minimumRadius);
            float rB = Math.Max(b.Radius, minimumRadius);
            // Midway between the surfaces along the normal.
            float surfaceA = Vector2.Dot(core.PointA, n) + rA;
            float surfaceB = Vector2.Dot(core.PointB, n) - rB;
            float across = 0.5f * (surfaceA + surfaceB);
            var t = new Vector2(-n.Y, n.X);
            float along = 0.5f * (Vector2.Dot(core.PointA, t) + Vector2.Dot(core.PointB, t));
            // The features facing each other: A's vertices furthest along n, B's least.
            const float flat = 1e-3f;
            float maxA = float.MinValue, minB = float.MaxValue;
            for (int i = 0; i < a.Count; i++) maxA = Math.Max(maxA, Vector2.Dot(MathUtils.Mul(xfA, a.Vertices[i]), n));
            for (int i = 0; i < b.Count; i++) minB = Math.Min(minB, Vector2.Dot(MathUtils.Mul(xfB, b.Vertices[i]), n));
            float loA = float.MaxValue, hiA = float.MinValue, loB = float.MaxValue, hiB = float.MinValue;
            int countA = 0, countB = 0;
            for (int i = 0; i < a.Count; i++)
            {
                var v = MathUtils.Mul(xfA, a.Vertices[i]);
                if (Vector2.Dot(v, n) < maxA - flat) continue;
                float s = Vector2.Dot(v, t);
                loA = Math.Min(loA, s);
                hiA = Math.Max(hiA, s);
                countA++;
            }
            for (int i = 0; i < b.Count; i++)
            {
                var v = MathUtils.Mul(xfB, b.Vertices[i]);
                if (Vector2.Dot(v, n) > minB + flat) continue;
                float s = Vector2.Dot(v, t);
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

    }
}
