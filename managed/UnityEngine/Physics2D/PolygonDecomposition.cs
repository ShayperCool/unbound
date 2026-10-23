using System;
using System.Collections.Generic;
using V = System.Numerics.Vector2;

namespace Unbound.Runtime
{
    /// <summary>
    /// Splits a simple polygon outline into convex pieces Box2D can use (at most eight vertices
    /// each): ear clipping into triangles, then Hertel–Mehlhorn merging of triangles across their
    /// diagonals while the result stays convex.
    /// </summary>
    internal static class PolygonDecomposition
    {
        const int MaxVertices = Unbound.Box2D.Common.Settings.MaxPolygonVertices;

        internal static List<List<V>> ConvexPieces(IReadOnlyList<V> outline)
        {
            var result = new List<List<V>>();
            var points = Clean(outline);
            if (points.Count < 3) return result;
            if (SignedArea(points) < 0f) points.Reverse();
            if (points.Count <= MaxVertices && IsConvex(points))
            {
                result.Add(points);
                return result;
            }

            var triangles = Triangulate(points);
            var polygons = new List<List<int>>();
            foreach (var t in triangles) polygons.Add(new List<int> { t.A, t.B, t.C });
            Merge(points, polygons);
            foreach (var polygon in polygons)
            {
                var piece = new List<V>(polygon.Count);
                foreach (var i in polygon) piece.Add(points[i]);
                result.Add(piece);
            }
            return result;
        }

        static List<V> Clean(IReadOnlyList<V> outline)
        {
            const float epsilon = 1e-6f;
            var points = new List<V>(outline.Count);
            foreach (var p in outline)
                if (points.Count == 0 || V.DistanceSquared(points[points.Count - 1], p) > epsilon * epsilon)
                    points.Add(p);
            while (points.Count > 1 && V.DistanceSquared(points[0], points[points.Count - 1]) <= epsilon * epsilon)
                points.RemoveAt(points.Count - 1);
            // Collinear points add nothing but degenerate triangles.
            bool removed = true;
            while (removed && points.Count > 3)
            {
                removed = false;
                for (int i = 0; i < points.Count; i++)
                {
                    var a = points[(i + points.Count - 1) % points.Count];
                    var b = points[i];
                    var c = points[(i + 1) % points.Count];
                    float scale = Math.Max(1e-12f, V.Distance(a, c));
                    if (Math.Abs(Cross(b - a, c - a)) / scale <= 1e-6f && V.Dot(b - a, c - b) >= 0f)
                    {
                        points.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            }
            return points;
        }

        static float Cross(V a, V b) => a.X * b.Y - a.Y * b.X;

        static float SignedArea(List<V> points)
        {
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
                area += Cross(points[i], points[(i + 1) % points.Count]);
            return area * 0.5f;
        }

        static bool IsConvex(List<V> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var c = points[(i + 2) % points.Count];
                if (Cross(b - a, c - b) < -1e-9f) return false;
            }
            return true;
        }

        static bool IsConvex(List<V> points, List<int> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = points[polygon[i]];
                var b = points[polygon[(i + 1) % polygon.Count]];
                var c = points[polygon[(i + 2) % polygon.Count]];
                if (Cross(b - a, c - b) < -1e-9f) return false;
            }
            return true;
        }

        struct Triangle
        {
            public int A, B, C;
        }

        static List<Triangle> Triangulate(List<V> points)
        {
            var triangles = new List<Triangle>();
            var remaining = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++) remaining.Add(i);
            int guard = 0;
            while (remaining.Count > 3 && guard++ < points.Count * points.Count)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int ib = remaining[i];
                    int ic = remaining[(i + 1) % remaining.Count];
                    if (!IsEar(points, remaining, ia, ib, ic)) continue;
                    triangles.Add(new Triangle { A = ia, B = ib, C = ic });
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Not a simple polygon (self-intersecting or degenerate): clip the most convex
                    // corner and go on, as there is no exact answer.
                    int best = 0;
                    float bestCross = float.NegativeInfinity;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        var a = points[remaining[(i + remaining.Count - 1) % remaining.Count]];
                        var b = points[remaining[i]];
                        var c = points[remaining[(i + 1) % remaining.Count]];
                        float cross = Cross(b - a, c - b);
                        if (cross > bestCross) { bestCross = cross; best = i; }
                    }
                    int ja = remaining[(best + remaining.Count - 1) % remaining.Count];
                    int jc = remaining[(best + 1) % remaining.Count];
                    if (bestCross > 0f) triangles.Add(new Triangle { A = ja, B = remaining[best], C = jc });
                    remaining.RemoveAt(best);
                }
            }
            if (remaining.Count == 3)
            {
                var a = points[remaining[0]];
                var b = points[remaining[1]];
                var c = points[remaining[2]];
                if (Cross(b - a, c - b) > 0f)
                    triangles.Add(new Triangle { A = remaining[0], B = remaining[1], C = remaining[2] });
            }
            return triangles;
        }

        static bool IsEar(List<V> points, List<int> remaining, int ia, int ib, int ic)
        {
            var a = points[ia];
            var b = points[ib];
            var c = points[ic];
            if (Cross(b - a, c - b) <= 0f) return false;
            foreach (var j in remaining)
            {
                if (j == ia || j == ib || j == ic) continue;
                var p = points[j];
                if (p == a || p == b || p == c) continue;
                if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f) return false;
            }
            return true;
        }

        /// <summary>Merges polygons across shared diagonals while they stay convex and small enough.</summary>
        static void Merge(List<V> points, List<List<int>> polygons)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < polygons.Count && !merged; i++)
                {
                    for (int j = i + 1; j < polygons.Count && !merged; j++)
                    {
                        var joined = Join(polygons[i], polygons[j]);
                        if (joined == null || joined.Count > MaxVertices || !IsConvex(points, joined)) continue;
                        polygons[i] = joined;
                        polygons.RemoveAt(j);
                        merged = true;
                    }
                }
            }
        }

        /// <summary>Two counter-clockwise polygons sharing an edge (u→v in one, v→u in the other) as one.</summary>
        static List<int> Join(List<int> p, List<int> q)
        {
            for (int i = 0; i < p.Count; i++)
            {
                int u = p[i], v = p[(i + 1) % p.Count];
                for (int j = 0; j < q.Count; j++)
                {
                    if (q[j] != v || q[(j + 1) % q.Count] != u) continue;
                    var result = new List<int>(p.Count + q.Count - 2);
                    // p from v around to u, then q's vertices strictly between u and v.
                    for (int k = 0; k < p.Count; k++) result.Add(p[(i + 1 + k) % p.Count]);
                    for (int k = 2; k < q.Count; k++) result.Add(q[(j + k) % q.Count]);
                    return result;
                }
            }
            return null;
        }
    }
}
