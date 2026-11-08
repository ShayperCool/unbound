using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using UnityEngine;
using UnityEngine.AI;

namespace Unbound.Runtime
{
    /// <summary>
    /// Cuts carving obstacles' holes into NavMesh tiles: each ground polygon a hole reaches is
    /// replaced by convex polygons covering what is left of it.
    /// </summary>
    // Measured (tests/projects/navmesh, Reference/unity-6000.0.71f1.json, carve.carver and
    // carve.parked): the hole is the box's outline on the ground grown by the agent type's radius
    // with sharp corners (a turned box included), and the polygons left keep their vertices; new
    // vertices appear only where the hole's outline crosses a polygon's edge and at the hole's
    // corners inside a polygon.
    // Unbound's choices (unmeasured): a box's outline is the convex hull of its eight corners seen
    // from above; a capsule's is a 16-sided polygon around its circle; a polygon is carved when the
    // obstacle's height span overlaps the polygon's from agentClimb under it to agentHeight above
    // it; a crossing point's height is taken along the polygon's edge and a hole corner's from the
    // polygon's detail mesh; carved polygons (and those split to stay within six vertices) get a
    // flat detail mesh over their vertices; holes overlapping in one polygon are cut one after
    // the other.
    internal static class NavMeshCarving
    {
        /// <summary>An obstacle's outline on the ground (counterclockwise in x, z) and height span.</summary>
        internal sealed class Footprint
        {
            internal Vector2[] Outline;
            internal float Bottom;
            internal float Top;
        }

        const float Eps = 1e-5f;
        /// <summary>A hole could not be cut into a polygon (reported once per run).</summary>
        static bool s_WarnedUncut;
        const int MaxVerts = DtDetour.DT_VERTS_PER_POLYGON;
        internal const int CapsuleSides = 16;

        // ------------------------------------------------------------------ outlines

        /// <summary>The convex hull of the points (x, z), counterclockwise.</summary>
        internal static Vector2[] Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < points.Count; k++)
                {
                    var p = points[pass == 0 ? k : points.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-9f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull.ToArray();
        }

        /// <summary>The convex outline grown by <paramref name="r"/>: each edge moved out, the
        /// corners where the moved edges meet (sharp, as measured).</summary>
        internal static Vector2[] Grow(Vector2[] outline, float r)
        {
            int n = outline.Length;
            if (n == 0 || r <= 0f) return outline;
            if (n == 1) return new[] { outline[0] + new Vector2(-r, -r), outline[0] + new Vector2(r, -r), outline[0] + new Vector2(r, r), outline[0] + new Vector2(-r, r) };
            if (n == 2)
            {
                // No thickness seen from above (a box with a zero side): the rectangle around the
                // segment, counterclockwise.
                var d = (outline[1] - outline[0]).normalized * r;
                var side = new Vector2(-d.y, d.x);
                return new[] { outline[0] - d - side, outline[1] + d - side, outline[1] + d + side, outline[0] - d + side };
            }
            var normals = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var e = outline[(i + 1) % n] - outline[i];
                normals[i] = new Vector2(e.y, -e.x).normalized;
            }
            var grown = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var a = normals[(i + n - 1) % n];
                var b = normals[i];
                var bisector = a + b;
                float d = Vector2.Dot(bisector, b);
                grown[i] = outline[i] + (d > 1e-6f ? bisector * (r / d) : b * r);
            }
            return grown;
        }

        // ------------------------------------------------------------------ tiles

        sealed class Hole
        {
            internal Vector2[] Outline;
            internal float MinX, MaxX, MinZ, MaxZ;
            internal float Bottom, Top;
        }

        sealed class OutPoly
        {
            internal List<int> Verts;
            /// <summary>The tile polygon it came from.</summary>
            internal int Source;
            /// <summary>Its vertices are the source's own: keeps the source's detail mesh.</summary>
            internal bool Unchanged;
            /// <summary>The source's vertex positions in this polygon (unchanged polygons with
            /// vertices inserted), for remapping the detail mesh; null for a flat detail mesh.</summary>
            internal int[] DetailMap;
        }

        /// <summary>The tile with the footprints' holes cut, grown by the agent type's radius; the
        /// tile itself when no hole reaches its ground polygons.</summary>
        internal static DtMeshData Carve(DtMeshData tile, NavMeshBuildSettings settings, IReadOnlyList<Footprint> footprints)
        {
            if (footprints == null || footprints.Count == 0) return tile;
            var h = tile.header;
            var holes = new List<Hole>();
            foreach (var f in footprints)
            {
                var outline = Grow(f.Outline, settings.agentRadius);
                var hole = new Hole { Outline = outline, Bottom = f.Bottom, Top = f.Top, MinX = float.MaxValue, MinZ = float.MaxValue, MaxX = float.MinValue, MaxZ = float.MinValue };
                foreach (var p in outline)
                {
                    hole.MinX = Mathf.Min(hole.MinX, p.x);
                    hole.MaxX = Mathf.Max(hole.MaxX, p.x);
                    hole.MinZ = Mathf.Min(hole.MinZ, p.y);
                    hole.MaxZ = Mathf.Max(hole.MaxZ, p.y);
                }
                if (hole.MaxX <= h.bmin.X || hole.MinX >= h.bmax.X || hole.MaxZ <= h.bmin.Z || hole.MinZ >= h.bmax.Z) continue;
                holes.Add(hole);
            }
            if (holes.Count == 0) return tile;

            var verts = new List<Vector3>();
            // One vertex per place: the same x and z, within a climb in height (NavMeshes of
            // several levels keep their levels apart).
            var index = new Dictionary<(float, float), List<int>>();
            float climb = Mathf.Max(settings.agentClimb, 1e-3f);
            int Vertex(Vector3 p)
            {
                if (!index.TryGetValue((p.x, p.z), out var list)) index[(p.x, p.z)] = list = new List<int>();
                foreach (int i in list)
                    if (Mathf.Abs(verts[i].y - p.y) <= climb) return i;
                verts.Add(p);
                list.Add(verts.Count - 1);
                return verts.Count - 1;
            }
            for (int i = 0; i < h.vertCount; i++) Vertex(new Vector3(tile.verts[i * 3], tile.verts[i * 3 + 1], tile.verts[i * 3 + 2]));

            var output = new List<OutPoly>();
            var offMesh = new List<(int Poly, int[] Verts)>();
            var added = new HashSet<int>();
            bool carved = false;
            for (int pi = 0; pi < h.polyCount; pi++)
            {
                var poly = tile.polys[pi];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                {
                    var ends = new int[poly.vertCount];
                    for (int k = 0; k < ends.Length; k++)
                    {
                        int vi = poly.verts[k] * 3;
                        ends[k] = Vertex(new Vector3(tile.verts[vi], tile.verts[vi + 1], tile.verts[vi + 2]));
                    }
                    offMesh.Add((pi, ends));
                    continue;
                }
                var own = new List<int>();
                for (int k = 0; k < poly.vertCount; k++)
                {
                    int vi = poly.verts[k] * 3;
                    own.Add(Vertex(new Vector3(tile.verts[vi], tile.verts[vi + 1], tile.verts[vi + 2])));
                }
                var pieces = CarvePoly(tile, poly, settings, holes);
                if (pieces == null)
                {
                    output.Add(new OutPoly { Verts = own, Source = pi, Unchanged = true });
                    continue;
                }
                carved = true;
                foreach (var piece in pieces)
                {
                    var list = new List<int>();
                    foreach (var p in piece)
                    {
                        int v = Vertex(p);
                        if (list.Count == 0 || list[list.Count - 1] != v) list.Add(v);
                        added.Add(v);
                    }
                    if (list.Count > 1 && list[0] == list[list.Count - 1]) list.RemoveAt(list.Count - 1);
                    if (list.Count >= 3) output.Add(new OutPoly { Verts = list, Source = pi });
                }
            }
            if (!carved) return tile;

            FixTJunctions(output, verts, added, climb);
            output = SplitLarge(output, verts);
            return Build(tile, output, offMesh, verts);
        }

        /// <summary>The polygon's pieces left by the holes (in the polygon's winding), or null when
        /// no hole cuts it.</summary>
        static List<List<Vector3>> CarvePoly(DtMeshData tile, DtPoly poly, NavMeshBuildSettings settings, List<Hole> holes)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var points = new List<Vector3>();
            for (int k = 0; k < poly.vertCount; k++)
            {
                int vi = poly.verts[k] * 3;
                var p = new Vector3(tile.verts[vi], tile.verts[vi + 1], tile.verts[vi + 2]);
                points.Add(p);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            bool reversed = Area(points) < 0f;
            if (reversed) points.Reverse();
            List<List<Vector3>> pieces = null;
            foreach (var hole in holes)
            {
                if (hole.MaxX <= minX || hole.MinX >= maxX || hole.MaxZ <= minZ || hole.MinZ >= maxZ) continue;
                if (hole.Top < minY - settings.agentClimb || hole.Bottom > maxY + settings.agentHeight) continue;
                var current = pieces ?? new List<List<Vector3>> { points };
                var next = new List<List<Vector3>>();
                bool cut = false;
                foreach (var piece in current)
                {
                    var left = Subtract(piece, hole.Outline, (x, z) => DetailHeight(tile, poly, x, z));
                    if (left == null) next.Add(piece);
                    else
                    {
                        cut = true;
                        next.AddRange(left);
                    }
                }
                if (cut) pieces = next;
            }
            if (pieces != null && reversed)
                foreach (var piece in pieces) piece.Reverse();
            return pieces;
        }

        /// <summary>The height of the polygon's detail mesh at (x, z); its vertices' mean height
        /// outside it.</summary>
        static float DetailHeight(DtMeshData tile, DtPoly poly, float x, float z)
        {
            var pos = new RcVec3f(x, 0f, z);
            if (tile.detailMeshes != null && poly.index < tile.detailMeshes.Length)
            {
                var pd = tile.detailMeshes[poly.index];
                Span<RcVec3f> v = stackalloc RcVec3f[3];
                for (int j = 0; j < pd.triCount; j++)
                {
                    int t = (pd.triBase + j) * 4;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = tile.detailTris[t + k];
                        int at = i < poly.vertCount ? poly.verts[i] * 3 : (pd.vertBase + i - poly.vertCount) * 3;
                        var src = i < poly.vertCount ? tile.verts : tile.detailVerts;
                        v[k] = new RcVec3f(src[at], src[at + 1], src[at + 2]);
                    }
                    if (DtUtils.ClosestHeightPointTriangle(pos, v[0], v[1], v[2], out float height)) return height;
                }
            }
            float sum = 0f;
            for (int k = 0; k < poly.vertCount; k++) sum += tile.verts[poly.verts[k] * 3 + 1];
            return sum / Mathf.Max(1, poly.vertCount);
        }

        // ------------------------------------------------------------------ one polygon minus one hole

        /// <summary>Twice the signed area in x, z (positive counterclockwise).</summary>
        static float Area(List<Vector3> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].x * p[i].z - p[i].x * p[j].z;
            return a;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        static float Cross(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>-1 inside the convex counterclockwise polygon, 0 on its edge, 1 outside.</summary>
        static int Side(Vector2[] poly, Vector2 p)
        {
            bool onEdge = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var e = poly[i] - poly[j];
                float c = Cross(poly[j], poly[i], p);
                float tol = Eps * Mathf.Max(1f, e.magnitude);
                if (c < -tol) return 1;
                if (c <= tol) onEdge = true;
            }
            return onEdge ? 0 : -1;
        }

        static Vector2[] Flat(List<Vector3> poly)
        {
            var r = new Vector2[poly.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Flat(poly[i]);
            return r;
        }

        /// <summary>Whether two convex counterclockwise polygons overlap with some area.</summary>
        static bool Overlap(Vector2[] a, Vector2[] b) => !Separated(a, b) && !Separated(b, a);

        static bool Separated(Vector2[] a, Vector2[] b)
        {
            for (int i = 0, j = a.Length - 1; i < a.Length; j = i++)
            {
                float tol = Eps * Mathf.Max(1f, (a[i] - a[j]).magnitude);
                bool allOut = true;
                foreach (var p in b)
                    if (Cross(a[j], a[i], p) > tol) { allOut = false; break; }
                if (allOut) return true;
            }
            return false;
        }

        sealed class Segment
        {
            internal Vector3 From, To;
            internal bool Used;
        }

        /// <summary>The counterclockwise convex polygon minus the counterclockwise convex hole as
        /// convex counterclockwise polygons of at most six vertices, or null when the hole does not
        /// cut it.</summary>
        static List<List<Vector3>> Subtract(List<Vector3> poly, Vector2[] hole, Func<float, float, float> heightAt)
        {
            var flat = Flat(poly);
            if (!Overlap(flat, hole)) return null;
            bool inside = true;
            foreach (var p in flat)
                if (Side(hole, p) > 0) { inside = false; break; }
            if (inside) return new List<List<Vector3>>();

            int n = poly.Count, m = hole.Length;
            // Crossings of each polygon edge with each hole edge, computed once (the same point for
            // both edges and, by its canonical order, for the neighbouring polygon's edge).
            var polyCuts = new List<(float T, Vector3 P)>[n];
            var holeCuts = new List<(float T, Vector3 P)>[m];
            for (int i = 0; i < n; i++) polyCuts[i] = new List<(float, Vector3)>();
            for (int j = 0; j < m; j++) holeCuts[j] = new List<(float, Vector3)>();
            var holePoints = new Vector3[m];
            for (int j = 0; j < m; j++) holePoints[j] = new Vector3(hole[j].x, heightAt(hole[j].x, hole[j].y), hole[j].y);
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                for (int j = 0; j < m; j++)
                {
                    var c = hole[j];
                    var d = hole[(j + 1) % m];
                    Intersect(a, b, c, d, polyCuts[i], holeCuts[j], holePoints[j], holePoints[(j + 1) % m]);
                }
            }

            var segments = new List<Segment>();
            bool polyEdgeDropped = false;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                var cuts = polyCuts[i];
                cuts.Add((0f, a));
                cuts.Add((1f, b));
                cuts.Sort((x, y) => x.T.CompareTo(y.T));
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    var p = cuts[k].P;
                    var q = cuts[k + 1].P;
                    if (SamePlace(p, q)) continue;
                    var mid = (Flat(p) + Flat(q)) * 0.5f;
                    int side = Side(hole, mid);
                    // On the hole's edge: kept where the hole lies outside the polygon there (the
                    // edges run opposite ways).
                    bool keep = side > 0 || (side == 0 && !SameDirection(hole, mid, Flat(q) - Flat(p)));
                    if (keep) segments.Add(new Segment { From = p, To = q });
                    else polyEdgeDropped = true;
                }
            }
            bool holeEdgeKept = false;
            for (int j = 0; j < m; j++)
            {
                var cuts = holeCuts[j];
                cuts.Add((0f, holePoints[j]));
                cuts.Add((1f, holePoints[(j + 1) % m]));
                cuts.Sort((x, y) => x.T.CompareTo(y.T));
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    var p = cuts[k].P;
                    var q = cuts[k + 1].P;
                    if (SamePlace(p, q)) continue;
                    var mid = (Flat(p) + Flat(q)) * 0.5f;
                    if (Side(flat, mid) < 0)
                    {
                        // The hole's edge, reversed, bounds what is left.
                        segments.Add(new Segment { From = q, To = p });
                        holeEdgeKept = true;
                    }
                }
            }
            if (!holeEdgeKept && !polyEdgeDropped) return null;

            var loops = Link(segments);
            var outers = new List<List<Vector3>>();
            var inners = new List<List<Vector3>>();
            foreach (var loop in loops)
            {
                if (loop.Count < 3) continue;
                float area = Area(loop);
                if (area > Eps) outers.Add(loop);
                else if (area < -Eps) inners.Add(loop);
            }
            var result = new List<List<Vector3>>();
            foreach (var outer in outers)
            {
                var ring = outer;
                // A hole wholly inside the polygon: joined to it by a bridge.
                foreach (var inner in inners)
                    if (Side(Flat(outer), Flat(inner[0])) < 0) ring = Bridge(ring, inner);
                var triangles = Triangulate(ring);
                if (triangles == null)
                {
                    if (s_WarnedUncut) return null;
                    s_WarnedUncut = true;
                    Debug.LogWarning("Unbound: a NavMeshObstacle's carved hole could not be cut into a NavMesh polygon; the polygon is left whole.");
                    return null;
                }
                result.AddRange(Merge(triangles));
            }
            return result;
        }

        static bool SamePlace(Vector3 a, Vector3 b) => a.x == b.x && a.z == b.z;

        static bool Near(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) <= Eps && Mathf.Abs(a.z - b.z) <= Eps;

        /// <summary>Whether the hole's edge through <paramref name="mid"/> runs the way of
        /// <paramref name="direction"/>.</summary>
        static bool SameDirection(Vector2[] hole, Vector2 mid, Vector2 direction)
        {
            for (int i = 0, j = hole.Length - 1; i < hole.Length; j = i++)
            {
                var e = hole[i] - hole[j];
                if (Mathf.Abs(Cross(hole[j], hole[i], mid)) <= Eps * Mathf.Max(1f, e.magnitude))
                    return Vector2.Dot(e, direction) > 0f;
            }
            return false;
        }

        /// <summary>Where the polygon edge a-b and the hole edge c-d meet (crossings, touches and the
        /// ends of an overlap), added to both edges' cut lists by their parameter.</summary>
        static void Intersect(Vector3 a, Vector3 b, Vector2 c, Vector2 d, List<(float, Vector3)> onPoly, List<(float, Vector3)> onHole,
            Vector3 cPoint, Vector3 dPoint)
        {
            var pa = Flat(a);
            var pb = Flat(b);
            var r = pb - pa;
            var s = d - c;
            float rr = r.sqrMagnitude, ss = s.sqrMagnitude;
            if (rr <= 0f || ss <= 0f) return;
            float denom = r.x * s.y - r.y * s.x;
            float lenR = Mathf.Sqrt(rr), lenS = Mathf.Sqrt(ss);
            if (Mathf.Abs(denom) <= Eps * lenR * lenS)
            {
                // Parallel: only a collinear overlap matters; its ends cut both edges.
                if (Mathf.Abs(Cross(pa, pb, c)) > Eps * lenR) return;
                void End(Vector2 p, Vector3 point, bool onHoleEnd)
                {
                    float t = Vector2.Dot(p - pa, r) / rr;
                    float u = Vector2.Dot(p - c, s) / ss;
                    if (t <= Eps || t >= 1f - Eps || u < -Eps || u > 1f + Eps) return;
                    onPoly.Add((t, onHoleEnd ? point : Lerp(a, b, t)));
                    if (!onHoleEnd) onHole.Add((u, point));
                }
                End(c, cPoint, true);
                End(d, dPoint, true);
                End(pa, a, false);
                End(pb, b, false);
                return;
            }
            // Canonical order (the same point from both polygons sharing the edge).
            bool swap = pa.x > pb.x || (pa.x == pb.x && pa.y > pb.y);
            var e0 = swap ? b : a;
            var e1 = swap ? a : b;
            var q0 = Flat(e0);
            var q = Flat(e1) - q0;
            float den = q.x * s.y - q.y * s.x;
            float tc = ((c.x - q0.x) * s.y - (c.y - q0.y) * s.x) / den;
            float uc = ((c.x - q0.x) * q.y - (c.y - q0.y) * q.x) / den;
            float tolT = Eps / lenR, tolU = Eps / lenS;
            if (tc < -tolT || tc > 1f + tolT || uc < -tolU || uc > 1f + tolU) return;
            float t0 = swap ? 1f - tc : tc;
            Vector3 point;
            // At a polygon vertex, or at a hole corner: that point itself.
            if (t0 <= tolT) point = a;
            else if (t0 >= 1f - tolT) point = b;
            else if (uc <= tolU) point = new Vector3(c.x, Lerp(e0, e1, tc).y, c.y);
            else if (uc >= 1f - tolU) point = new Vector3(d.x, Lerp(e0, e1, tc).y, d.y);
            else point = Lerp(e0, e1, tc);
            if (t0 > tolT && t0 < 1f - tolT) onPoly.Add((t0, point));
            if (uc > tolU && uc < 1f - tolU) onHole.Add((uc, point));
        }

        static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        /// <summary>The kept segments joined end to start into closed loops.</summary>
        static List<List<Vector3>> Link(List<Segment> segments)
        {
            var loops = new List<List<Vector3>>();
            foreach (var first in segments)
            {
                if (first.Used) continue;
                var loop = new List<Vector3>();
                var current = first;
                while (current != null && !current.Used)
                {
                    current.Used = true;
                    loop.Add(current.From);
                    Segment next = null;
                    foreach (var s in segments)
                        if (!s.Used && SamePlace(s.From, current.To)) { next = s; break; }
                    if (next == null)
                        foreach (var s in segments)
                            if (!s.Used && Near(s.From, current.To)) { next = s; break; }
                    current = next;
                }
                loops.Add(loop);
            }
            return loops;
        }

        /// <summary>The outer ring joined to a clockwise inner ring by a bridge from the inner
        /// ring's first vertex to the nearest outer vertex it can see past the inner ring.</summary>
        static List<Vector3> Bridge(List<Vector3> outer, List<Vector3> inner)
        {
            var v = inner[0];
            var next = inner[1];
            var prev = inner[inner.Count - 1];
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                var w = outer[i];
                // The inner ring runs clockwise: the hole's inside is to the right of v->next.
                bool intoHole = Cross(v, next, w) < 0f && Cross(prev, v, w) < 0f;
                if (intoHole) continue;
                float distance = (Flat(w) - Flat(v)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            if (best < 0) best = 0;
            var ring = new List<Vector3>();
            for (int i = 0; i <= outer.Count; i++) ring.Add(outer[(best + i) % outer.Count]);
            for (int i = 0; i <= inner.Count; i++) ring.Add(inner[i % inner.Count]);
            return ring;
        }

        /// <summary>Ear clipping of a counterclockwise ring (bridges allowed); null when no ear is
        /// found.</summary>
        static List<List<Vector3>> Triangulate(List<Vector3> ring)
        {
            var pts = new List<Vector3>(ring);
            var triangles = new List<List<Vector3>>();
            int guard = pts.Count * pts.Count + 8;
            while (pts.Count > 3 && guard-- > 0)
            {
                int ear = -1;
                float bestTurn = float.MinValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[(i + pts.Count - 1) % pts.Count];
                    var b = pts[i];
                    var c = pts[(i + 1) % pts.Count];
                    float turn = Cross(a, b, c);
                    if (turn <= Eps * Eps) continue;
                    bool empty = true;
                    for (int k = 0; k < pts.Count && empty; k++)
                    {
                        var p = pts[k];
                        if (SamePlace(p, a) || SamePlace(p, b) || SamePlace(p, c)) continue;
                        if (Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    // Recast's order: the ear with the shortest new diagonal.
                    float score = -(Flat(c) - Flat(a)).sqrMagnitude;
                    if (score > bestTurn)
                    {
                        bestTurn = score;
                        ear = i;
                    }
                }
                if (ear < 0)
                {
                    // Only degenerate (collinear or repeated) vertices left: drop one.
                    int drop = -1;
                    for (int i = 0; i < pts.Count && drop < 0; i++)
                    {
                        var a = pts[(i + pts.Count - 1) % pts.Count];
                        var c = pts[(i + 1) % pts.Count];
                        if (Mathf.Abs(Cross(a, pts[i], c)) <= Eps * Eps || SamePlace(a, pts[i])) drop = i;
                    }
                    if (drop < 0) return null;
                    pts.RemoveAt(drop);
                    continue;
                }
                triangles.Add(new List<Vector3> { pts[(ear + pts.Count - 1) % pts.Count], pts[ear], pts[(ear + 1) % pts.Count] });
                pts.RemoveAt(ear);
            }
            if (pts.Count == 3 && Cross(pts[0], pts[1], pts[2]) > Eps * Eps) triangles.Add(pts);
            return triangles;
        }

        /// <summary>Triangles merged into convex polygons of at most six vertices, longest shared
        /// edge first (as Recast merges its polygons).</summary>
        static List<List<Vector3>> Merge(List<List<Vector3>> polys)
        {
            while (true)
            {
                int bestA = -1, bestB = -1, bestEa = 0, bestEb = 0;
                float bestLength = -1f;
                for (int i = 0; i < polys.Count; i++)
                for (int j = i + 1; j < polys.Count; j++)
                {
                    var a = polys[i];
                    var b = polys[j];
                    if (a.Count + b.Count - 2 > MaxVerts) continue;
                    for (int ea = 0; ea < a.Count; ea++)
                    {
                        var u = a[ea];
                        var v = a[(ea + 1) % a.Count];
                        for (int eb = 0; eb < b.Count; eb++)
                        {
                            if (!SamePlace(b[eb], v) || !SamePlace(b[(eb + 1) % b.Count], u)) continue;
                            // Convex at both ends of the removed edge.
                            var beforeU = a[(ea + a.Count - 1) % a.Count];
                            var afterU = b[(eb + 2) % b.Count];
                            var beforeV = b[(eb + b.Count - 1) % b.Count];
                            var afterV = a[(ea + 2) % a.Count];
                            if (Cross(beforeU, u, afterU) <= Eps * Eps || Cross(beforeV, v, afterV) <= Eps * Eps) continue;
                            float length = (Flat(u) - Flat(v)).sqrMagnitude;
                            if (length > bestLength)
                            {
                                bestLength = length;
                                bestA = i; bestB = j; bestEa = ea; bestEb = eb;
                            }
                        }
                    }
                }
                if (bestA < 0) return polys;
                var pa = polys[bestA];
                var pb = polys[bestB];
                var merged = new List<Vector3>();
                for (int k = 0; k < pa.Count; k++) merged.Add(pa[(bestEa + 1 + k) % pa.Count]);
                for (int k = 0; k < pb.Count - 2; k++) merged.Add(pb[(bestEb + 2 + k) % pb.Count]);
                polys[bestA] = merged;
                polys.RemoveAt(bestB);
            }
        }

        // ------------------------------------------------------------------ the new tile

        /// <summary>A vertex lying inside another polygon's edge (where a hole's outline meets an
        /// edge the neighbour shares) is added to that edge, so the two polygons stay connected.</summary>
        static void FixTJunctions(List<OutPoly> polys, List<Vector3> verts, HashSet<int> candidates, float climb)
        {
            if (candidates.Count == 0) return;
            var list = new List<int>(candidates);
            foreach (var poly in polys)
            {
                var vs = poly.Verts;
                List<int> result = null;
                for (int e = 0; e < vs.Count; e++)
                {
                    var a = verts[vs[e]];
                    var b = verts[vs[(e + 1) % vs.Count]];
                    var pa = Flat(a);
                    var d = Flat(b) - pa;
                    float len2 = d.sqrMagnitude;
                    List<(float, int)> inserts = null;
                    if (len2 > 0f)
                    {
                        float len = Mathf.Sqrt(len2);
                        foreach (int v in list)
                        {
                            if (v == vs[e] || v == vs[(e + 1) % vs.Count]) continue;
                            var p = Flat(verts[v]) - pa;
                            float t = Vector2.Dot(p, d) / len2;
                            if (t * len <= Eps * 10f || (1f - t) * len <= Eps * 10f) continue;
                            if (Mathf.Abs(d.x * p.y - d.y * p.x) / len > Eps * 10f) continue;
                            if (Mathf.Abs(a.y + (b.y - a.y) * t - verts[v].y) > climb) continue;
                            (inserts ??= new List<(float, int)>()).Add((t, v));
                        }
                    }
                    if (inserts != null && result == null)
                    {
                        result = new List<int>();
                        for (int k = 0; k < e; k++) result.Add(vs[k]);
                    }
                    result?.Add(vs[e]);
                    if (inserts != null)
                    {
                        inserts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                        foreach (var (_, v) in inserts) result.Add(v);
                    }
                }
                if (result == null) continue;
                if (poly.Unchanged)
                {
                    var map = new int[vs.Count];
                    for (int k = 0; k < vs.Count; k++) map[k] = result.IndexOf(vs[k]);
                    poly.DetailMap = map;
                    poly.Unchanged = false;
                }
                poly.Verts = result;
            }
        }

        /// <summary>Polygons over six vertices cut into convex runs from a corner.</summary>
        static List<OutPoly> SplitLarge(List<OutPoly> polys, List<Vector3> verts)
        {
            var result = new List<OutPoly>();
            foreach (var poly in polys)
            {
                var vs = poly.Verts;
                if (vs.Count <= MaxVerts)
                {
                    result.Add(poly);
                    continue;
                }
                // Start at the sharpest corner, so no run lies along one straight edge.
                int start = 0;
                float bestTurn = float.MinValue;
                for (int i = 0; i < vs.Count; i++)
                {
                    float turn = Mathf.Abs(Cross(verts[vs[(i + vs.Count - 1) % vs.Count]], verts[vs[i]], verts[vs[(i + 1) % vs.Count]]));
                    if (turn > bestTurn) { bestTurn = turn; start = i; }
                }
                var ordered = new List<int>();
                for (int i = 0; i < vs.Count; i++) ordered.Add(vs[(start + i) % vs.Count]);
                int at = 1;
                while (at < ordered.Count - 1)
                {
                    var run = new List<int> { ordered[0] };
                    int end = Math.Min(ordered.Count - 1, at + MaxVerts - 2);
                    for (int k = at; k <= end; k++) run.Add(ordered[k]);
                    result.Add(new OutPoly { Verts = run, Source = poly.Source });
                    at = end;
                }
            }
            return result;
        }

        static DtMeshData Build(DtMeshData tile, List<OutPoly> polys, List<(int Poly, int[] Verts)> offMesh, List<Vector3> verts)
        {
            var h = tile.header;
            int groundCount = polys.Count;
            int polyCount = groundCount + offMesh.Count;
            var newPolys = new DtPoly[polyCount];
            var details = new DtPolyDetail[groundCount];
            var detailVerts = new List<float>();
            var detailTris = new List<int>();
            var edges = new Dictionary<(int, int), (int Poly, int Edge)>();
            int edgeTotal = 0;
            for (int i = 0; i < groundCount; i++)
            {
                var src = tile.polys[polys[i].Source];
                var vs = polys[i].Verts;
                var poly = new DtPoly(i, MaxVerts);
                poly.vertCount = vs.Count;
                poly.flags = src.flags;
                poly.areaAndtype = src.areaAndtype;
                for (int k = 0; k < vs.Count; k++) poly.verts[k] = vs[k];
                newPolys[i] = poly;
                edgeTotal += vs.Count;

                // Neighbours: a shared edge (the same two vertices) inside the tile; an edge along
                // one of the source's portals to another tile keeps that portal.
                for (int k = 0; k < vs.Count; k++)
                {
                    int a = vs[k], b = vs[(k + 1) % vs.Count];
                    var key = a < b ? (a, b) : (b, a);
                    if (edges.TryGetValue(key, out var other))
                    {
                        poly.neis[k] = other.Poly + 1;
                        newPolys[other.Poly].neis[other.Edge] = i + 1;
                    }
                    else edges[key] = (i, k);
                    if (poly.neis[k] == 0) poly.neis[k] = PortalOf(tile, src, verts[a], verts[b]);
                }

                // Detail mesh: the source's own (its vertices remapped), else flat over the polygon.
                int triBase = detailTris.Count / 4;
                int vertBase = detailVerts.Count / 3;
                var keep = polys[i].Unchanged || polys[i].DetailMap != null;
                if (keep && tile.detailMeshes != null && polys[i].Source < tile.detailMeshes.Length)
                {
                    var pd = tile.detailMeshes[polys[i].Source];
                    for (int k = 0; k < pd.vertCount * 3; k++) detailVerts.Add(tile.detailVerts[pd.vertBase * 3 + k]);
                    var map = polys[i].DetailMap;
                    for (int t = 0; t < pd.triCount; t++)
                    {
                        int at = (pd.triBase + t) * 4;
                        for (int k = 0; k < 3; k++)
                        {
                            int idx = tile.detailTris[at + k];
                            detailTris.Add(idx < src.vertCount ? (map != null ? map[idx] : idx) : idx - src.vertCount + vs.Count);
                        }
                        detailTris.Add(tile.detailTris[at + 3]);
                    }
                    details[i] = new DtPolyDetail(vertBase, triBase, (byte)pd.vertCount, (byte)pd.triCount);
                }
                else if (ClipDetail(tile, src, vs, verts, detailVerts, detailTris, out int extra, out int tris))
                    details[i] = new DtPolyDetail(vertBase, triBase, (byte)extra, (byte)tris);
                else
                {
                    int n = vs.Count;
                    for (int k = 1; k + 1 < n; k++)
                    {
                        detailTris.Add(0);
                        detailTris.Add(k);
                        detailTris.Add(k + 1);
                        int flags = (k == 1 ? DtDetailTriEdgeFlags.DT_DETAIL_EDGE_BOUNDARY : 0)
                                    | (DtDetailTriEdgeFlags.DT_DETAIL_EDGE_BOUNDARY << 2)
                                    | (k + 1 == n - 1 ? DtDetailTriEdgeFlags.DT_DETAIL_EDGE_BOUNDARY << 4 : 0);
                        detailTris.Add(flags);
                    }
                    details[i] = new DtPolyDetail(vertBase, triBase, 0, (byte)(n - 2));
                }
            }

            var cons = (DtOffMeshConnection[])tile.offMeshCons.Clone();
            for (int k = 0; k < offMesh.Count; k++)
            {
                var src = tile.polys[offMesh[k].Poly];
                var poly = new DtPoly(groundCount + k, MaxVerts);
                Array.Copy(offMesh[k].Verts, poly.verts, offMesh[k].Verts.Length);
                Array.Copy(src.neis, poly.neis, poly.neis.Length);
                poly.flags = src.flags;
                poly.vertCount = src.vertCount;
                poly.areaAndtype = src.areaAndtype;
                newPolys[groundCount + k] = poly;
                for (int c = 0; c < cons.Length; c++)
                    if (cons[c].poly == offMesh[k].Poly)
                        cons[c] = new DtOffMeshConnection { pos = cons[c].pos, rad = cons[c].rad, poly = groundCount + k, flags = cons[c].flags, side = cons[c].side, userId = cons[c].userId, costModifier = cons[c].costModifier };
            }

            var flat = new float[verts.Count * 3];
            for (int i = 0; i < verts.Count; i++)
            {
                flat[i * 3] = verts[i].x;
                flat[i * 3 + 1] = verts[i].y;
                flat[i * 3 + 2] = verts[i].z;
            }
            var header = h;
            int oldEdges = 0;
            for (int i = 0; i < h.polyCount; i++) oldEdges += tile.polys[i].vertCount;
            header.polyCount = polyCount;
            header.vertCount = verts.Count;
            header.detailMeshCount = groundCount;
            header.detailVertCount = detailVerts.Count / 3;
            header.detailTriCount = detailTris.Count / 4;
            header.offMeshBase = groundCount;
            header.bvNodeCount = 0;
            // Room for every edge's links besides what the tile had (portals may link several).
            header.maxLinkCount = h.maxLinkCount + Math.Max(0, edgeTotal - oldEdges) * 2 + edgeTotal;
            return new DtMeshData
            {
                header = header,
                verts = flat,
                polys = newPolys,
                detailMeshes = details,
                detailVerts = detailVerts.ToArray(),
                detailTris = detailTris.ToArray(),
                bvTree = null,
                offMeshCons = cons,
            };
        }

        /// <summary>The source polygon's detail triangles cut to the new polygon, appended as its
        /// detail mesh (its vertices first, then the cut points): the surface keeps the source's
        /// heights. False when nothing is left (or more than a detail mesh holds).</summary>
        static bool ClipDetail(DtMeshData tile, DtPoly src, List<int> vs, List<Vector3> verts, List<float> detailVerts, List<int> detailTris,
            out int extraCount, out int triCount)
        {
            extraCount = 0;
            triCount = 0;
            if (tile.detailMeshes == null || src.index >= tile.detailMeshes.Length) return false;
            var pd = tile.detailMeshes[src.index];
            var poly = new List<Vector3>();
            foreach (int v in vs) poly.Add(verts[v]);
            bool ccw = Area(poly) > 0f;
            var extra = new List<Vector3>();
            var tris = new List<int>();
            int Index(Vector3 p)
            {
                for (int k = 0; k < poly.Count; k++)
                    if (Near(poly[k], p)) return k;
                for (int k = 0; k < extra.Count; k++)
                    if (Near(extra[k], p)) return poly.Count + k;
                extra.Add(p);
                return poly.Count + extra.Count - 1;
            }
            var piece = new List<Vector3>(3);
            for (int j = 0; j < pd.triCount; j++)
            {
                int t = (pd.triBase + j) * 4;
                piece.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int idx = tile.detailTris[t + k];
                    int at = idx < src.vertCount ? src.verts[idx] * 3 : (pd.vertBase + idx - src.vertCount) * 3;
                    var from = idx < src.vertCount ? tile.verts : tile.detailVerts;
                    piece.Add(new Vector3(from[at], from[at + 1], from[at + 2]));
                }
                // Sutherland-Hodgman against each edge of the convex polygon.
                for (int e = 0; e < poly.Count && piece.Count >= 3; e++)
                {
                    var a = poly[e];
                    var b = poly[(e + 1) % poly.Count];
                    var cut = new List<Vector3>(piece.Count + 1);
                    for (int k = 0; k < piece.Count; k++)
                    {
                        var p = piece[k];
                        var q = piece[(k + 1) % piece.Count];
                        float dp = Cross(a, b, p) * (ccw ? 1f : -1f), dq = Cross(a, b, q) * (ccw ? 1f : -1f);
                        if (dp >= 0f) cut.Add(p);
                        if ((dp >= 0f) != (dq >= 0f)) cut.Add(Lerp(p, q, dp / (dp - dq)));
                    }
                    piece = cut;
                }
                if (piece.Count < 3) continue;
                var ids = new List<int>();
                foreach (var p in piece)
                {
                    int id = Index(p);
                    if (ids.Count == 0 || (ids[ids.Count - 1] != id && ids[0] != id)) ids.Add(id);
                }
                for (int k = 1; k + 1 < ids.Count; k++)
                {
                    var a = Point(ids[0]);
                    var b = Point(ids[k]);
                    var c = Point(ids[k + 1]);
                    if (Mathf.Abs(Cross(a, b, c)) <= Eps * Eps) continue;
                    int flags = 0;
                    int[] tri = { ids[0], ids[k], ids[k + 1] };
                    for (int edge = 0; edge < 3; edge++)
                        if (OnBoundary(Point(tri[edge]), Point(tri[(edge + 1) % 3]))) flags |= DtDetailTriEdgeFlags.DT_DETAIL_EDGE_BOUNDARY << (edge * 2);
                    tris.AddRange(tri);
                    tris.Add(flags);
                }
            }
            Vector3 Point(int id) => id < poly.Count ? poly[id] : extra[id - poly.Count];
            bool OnBoundary(Vector3 p, Vector3 q)
            {
                for (int e = 0; e < poly.Count; e++)
                    if (OnSegment(Flat(poly[e]), Flat(poly[(e + 1) % poly.Count]), Flat(p)) && OnSegment(Flat(poly[e]), Flat(poly[(e + 1) % poly.Count]), Flat(q)))
                        return true;
                return false;
            }
            if (tris.Count == 0 || extra.Count > 255 || tris.Count / 4 > 255) return false;
            foreach (var p in extra)
            {
                detailVerts.Add(p.x);
                detailVerts.Add(p.y);
                detailVerts.Add(p.z);
            }
            detailTris.AddRange(tris);
            extraCount = extra.Count;
            triCount = tris.Count / 4;
            return true;
        }

        /// <summary>The source polygon's portal to another tile along whose edge a-b lies, else 0.</summary>
        static int PortalOf(DtMeshData tile, DtPoly src, Vector3 a, Vector3 b)
        {
            for (int k = 0; k < src.vertCount; k++)
            {
                int nei = src.neis[k];
                if ((nei & DtDetour.DT_EXT_LINK) == 0) continue;
                int i0 = src.verts[k] * 3, i1 = src.verts[(k + 1) % src.vertCount] * 3;
                var p = new Vector2(tile.verts[i0], tile.verts[i0 + 2]);
                var q = new Vector2(tile.verts[i1], tile.verts[i1 + 2]);
                if (OnSegment(p, q, Flat(a)) && OnSegment(p, q, Flat(b))) return nei;
            }
            return 0;
        }

        static bool OnSegment(Vector2 p, Vector2 q, Vector2 x)
        {
            var d = q - p;
            float len2 = d.sqrMagnitude;
            if (len2 <= 0f) return (x - p).sqrMagnitude <= Eps * Eps;
            float t = Vector2.Dot(x - p, d) / len2;
            if (t < -Eps || t > 1f + Eps) return false;
            return Mathf.Abs(d.x * (x.y - p.y) - d.y * (x.x - p.x)) / Mathf.Sqrt(len2) <= Eps * 10f;
        }
    }
}
