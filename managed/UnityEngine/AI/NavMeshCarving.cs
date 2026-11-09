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
    // Measured (tests/projects/navmesh carve.carver and carve.parked, tests/projects/navpath
    // carve.sides and carve.mud, Unity 6000.0.71f1): the hole is the box's outline on the ground
    // grown by the agent type's radius with sharp corners (a turned box included). The polygons a
    // hole reaches are taken in groups connected inside the tile; each group's hole is the convex
    // hull of what the hole covers of it, and each polygon of the group is cut by that hull's edges
    // into triangles merged in order into convex pieces (HullsOf, CutGroup); polygons the hole
    // misses keep their vertices.
    // Unbound's choices (unmeasured): a box's outline is the convex hull of its eight corners seen
    // from above; a capsule's is a 16-sided polygon around its circle; a polygon is carved when the
    // obstacle's height span overlaps the polygon's from agentClimb under it to agentHeight above
    // it; a cut piece's new vertices take the height of the polygon's detail mesh; carved
    // polygons get the source's detail triangles cut to them; holes overlapping in one polygon are
    // cut one after the other.
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

            // Distances within this count as zero, and points within it are one vertex.
            float eps = settings.EffectiveVoxelSize * WeldPerVoxel;
            // Every hole's group hulls come from the tile as built; they are then cut one after
            // the other, each into the pieces left of its polygons.
            var hulls = new List<GroupHull>();
            foreach (var hole in holes) hulls.AddRange(HullsOf(tile, hole, settings, eps));
            if (hulls.Count == 0) return tile;
            var work = new List<Piece>(h.polyCount);
            var offMeshPolys = new List<int>();
            for (int pi = 0; pi < h.polyCount; pi++)
            {
                var poly = tile.polys[pi];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                {
                    offMeshPolys.Add(pi);
                    continue;
                }
                var points = new List<Vector3>(poly.vertCount);
                for (int k = 0; k < poly.vertCount; k++)
                {
                    int vi = poly.verts[k] * 3;
                    points.Add(new Vector3(tile.verts[vi], tile.verts[vi + 1], tile.verts[vi + 2]));
                }
                work.Add(new Piece { Points = points, Source = pi, Unchanged = true });
            }
            bool carved = false;
            foreach (var hull in hulls) carved |= CutGroup(work, hull, eps);
            if (!carved) return tile;

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
            // A cut piece's point within eps of a vertex already placed is that vertex.
            var weld = new Dictionary<(int, int, int), List<int>>();
            float cell = eps > 0f ? eps : 1e-5f;
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
            int Weld(Vector3 p)
            {
                var c = Cell(p);
                int best = -1;
                float bestDistance = eps * eps;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!weld.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        float distance = (verts[i] - p).sqrMagnitude;
                        if (distance < bestDistance) { bestDistance = distance; best = i; }
                    }
                }
                if (best >= 0) return best;
                int v = Vertex(p);
                if (!weld.TryGetValue(c, out var at)) weld[c] = at = new List<int>();
                if (!at.Contains(v)) at.Add(v);
                return v;
            }
            for (int i = 0; i < verts.Count; i++)
            {
                var c = Cell(verts[i]);
                if (!weld.TryGetValue(c, out var at)) weld[c] = at = new List<int>();
                at.Add(i);
            }

            var output = new List<OutPoly>(work.Count);
            var added = new HashSet<int>();
            // The polygon whose detail surface gives a new vertex its height: the source of the
            // last cut piece using it.
            var heightFrom = new Dictionary<int, int>();
            foreach (var piece in work)
            {
                var list = new List<int>(piece.Points.Count);
                foreach (var p in piece.Points)
                {
                    int v = piece.Unchanged ? Vertex(p) : Weld(p);
                    if (list.Count == 0 || list[list.Count - 1] != v) list.Add(v);
                }
                if (list.Count > 1 && list[0] == list[list.Count - 1]) list.RemoveAt(list.Count - 1);
                if (!piece.Unchanged)
                {
                    if (list.Count < 3 || DegenerateArea(list, verts, eps)) continue;
                    foreach (int v in list)
                    {
                        added.Add(v);
                        if (v >= h.vertCount) heightFrom[v] = piece.Source;
                    }
                }
                output.Add(new OutPoly { Verts = list, Source = piece.Source, Unchanged = piece.Unchanged });
            }
            foreach (var (v, source) in heightFrom)
            {
                var p = verts[v];
                verts[v] = new Vector3(p.x, DetailHeight(tile, tile.polys[source], p.x, p.z, p.y), p.z);
            }
            var offMesh = new List<(int Poly, int[] Verts)>(offMeshPolys.Count);
            foreach (int pi in offMeshPolys)
            {
                var poly = tile.polys[pi];
                var ends = new int[poly.vertCount];
                for (int k = 0; k < ends.Length; k++)
                {
                    int vi = poly.verts[k] * 3;
                    ends[k] = Vertex(new Vector3(tile.verts[vi], tile.verts[vi + 1], tile.verts[vi + 2]));
                }
                offMesh.Add((pi, ends));
            }

            FixTJunctions(output, verts, added, climb);
            output = SplitLarge(output, verts);
            return Build(tile, output, offMesh, verts, eps);
        }

        /// <summary>Distances within a voxel size over this count as zero when cutting holes, and
        /// points that near are one vertex.</summary>
        const float WeldPerVoxel = 1f / 64f;

        /// <summary>A clockwise piece with no area: twice its area at most 1% of eps times its
        /// longest edge.</summary>
        static bool DegenerateArea(List<int> vs, List<Vector3> verts, float eps)
        {
            float area = 0f, longest = 0f;
            var a = verts[vs[0]];
            for (int k = 1; k + 1 < vs.Count; k++)
            {
                var b = verts[vs[k]];
                var c = verts[vs[k + 1]];
                area += Turn(a, b, c);
                longest = Mathf.Max(longest, Mathf.Max((b - a).sqrMagnitude, (c - b).sqrMagnitude));
            }
            longest = Mathf.Max(longest, (verts[vs[vs.Count - 1]] - a).sqrMagnitude);
            float tol = eps * MergeMinTurn;
            return area <= 0f || area * area <= tol * tol * longest;
        }

        // ------------------------------------------------------------------ holes per polygon group

        /// <summary>A plane through a hole's edge, vertical, its normal (x, z) pointing out of the
        /// hole: a point's distance is positive outside.</summary>
        readonly struct EdgePlane
        {
            internal readonly float X, Z, W;
            internal EdgePlane(float x, float z, float w) { X = x; Z = z; W = w; }
            internal float Distance(Vector3 p) => p.z * Z + p.x * X + W;
        }

        /// <summary>The hole as cut into one group of connected polygons: the planes of its edges
        /// and the polygons (by their place in the tile) it is cut into.</summary>
        sealed class GroupHull
        {
            internal List<EdgePlane> Planes;
            internal HashSet<int> Sources;
        }

        /// <summary>A polygon of the tile being cut: its vertices (clockwise seen from above, as
        /// the tile's) and the tile polygon it came from.</summary>
        sealed class Piece
        {
            internal List<Vector3> Points;
            internal int Source;
            internal bool Unchanged;
        }

        /// <summary>The hole's hulls in the tile: the ground polygons it reaches, joined into groups
        /// of polygons connected by their edges; each group's hull is the convex hull (from above)
        /// of what the hole covers of its polygons.</summary>
        static List<GroupHull> HullsOf(DtMeshData tile, Hole hole, NavMeshBuildSettings settings, float eps)
        {
            var h = tile.header;
            var reached = new byte[h.polyCount];
            bool any = false;
            for (int pi = 0; pi < h.polyCount; pi++)
            {
                var poly = tile.polys[pi];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int k = 0; k < poly.vertCount; k++)
                {
                    int vi = poly.verts[k] * 3;
                    minX = Mathf.Min(minX, tile.verts[vi]); maxX = Mathf.Max(maxX, tile.verts[vi]);
                    minY = Mathf.Min(minY, tile.verts[vi + 1]); maxY = Mathf.Max(maxY, tile.verts[vi + 1]);
                    minZ = Mathf.Min(minZ, tile.verts[vi + 2]); maxZ = Mathf.Max(maxZ, tile.verts[vi + 2]);
                }
                if (hole.MaxX < minX || hole.MinX > maxX || hole.MaxZ < minZ || hole.MinZ > maxZ) continue;
                if (hole.Top < minY - settings.agentClimb || hole.Bottom > maxY + settings.agentHeight) continue;
                reached[pi] = 1;
                any = true;
            }
            var hulls = new List<GroupHull>();
            if (!any) return hulls;

            var stack = new List<int>();
            var points = new List<Vector2>();
            var clipped = new List<Vector2>();
            var scratch = new List<Vector2>();
            for (int seed = 0; seed < h.polyCount; seed++)
            {
                if (reached[seed] != 1) continue;
                // The group: the reached polygons connected to the seed inside the tile.
                var group = new HashSet<int>();
                stack.Clear();
                stack.Add(seed);
                reached[seed] = 2;
                while (stack.Count > 0)
                {
                    int pi = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    group.Add(pi);
                    var poly = tile.polys[pi];
                    for (int k = 0; k < poly.vertCount; k++)
                    {
                        int nei = poly.neis[k];
                        if (nei == 0 || (nei & DtDetour.DT_EXT_LINK) != 0 || reached[nei - 1] != 1) continue;
                        reached[nei - 1] = 2;
                        stack.Add(nei - 1);
                    }
                }
                // What the hole covers of each polygon; polygons it does not cover leave the group.
                points.Clear();
                var cut = new HashSet<int>();
                foreach (int pi in group)
                {
                    var poly = tile.polys[pi];
                    clipped.Clear();
                    for (int k = 0; k < poly.vertCount; k++)
                    {
                        int vi = poly.verts[k] * 3;
                        clipped.Add(new Vector2(tile.verts[vi], tile.verts[vi + 2]));
                    }
                    ClipToConvex(clipped, hole.Outline, scratch);
                    if (clipped.Count == 0) continue;
                    cut.Add(pi);
                    points.AddRange(clipped);
                }
                var hull = ConvexHull(points);
                // Hull points nearly in line are dropped, unless the hull reaches the tile's side.
                if (Mathf.Min(Sq(hull.min.x - h.bmin.X), Sq(hull.min.y - h.bmin.Z), Sq(hull.max.x - h.bmax.X), Sq(hull.max.y - h.bmax.Z)) >= HullTileSideSq)
                    SimplifyHull(hull.points, eps);
                if (hull.points.Count < 3) continue;
                var planes = new List<EdgePlane>(hull.points.Count);
                for (int k = 1; k <= hull.points.Count; k++)
                {
                    var a = hull.points[k - 1];
                    var d = hull.points[k % hull.points.Count] - a;
                    float length = d.magnitude;
                    if (length <= Vector2.kEpsilon) continue;
                    d /= length;
                    // The hull is clockwise seen from above: (-dz, dx) points out of it.
                    planes.Add(new EdgePlane(-d.y, d.x, -(a.y * d.x + -d.y * a.x + 0f)));
                }
                hulls.Add(new GroupHull { Planes = planes, Sources = cut });
            }
            return hulls;
        }

        /// <summary>A hull reaching within 1 cm of the tile's side keeps its points nearly in line.</summary>
        const float HullTileSideSq = 1e-4f;

        static float Sq(float x) => x * x;

        /// <summary>The convex polygon (x, z) cut to the convex counterclockwise outline, in place
        /// (empty when they do not meet).</summary>
        static void ClipToConvex(List<Vector2> poly, Vector2[] outline, List<Vector2> scratch)
        {
            for (int e = 0; e < outline.Length && poly.Count > 0; e++)
            {
                var a = outline[e];
                var b = outline[(e + 1) % outline.Length];
                scratch.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    var p = poly[k];
                    var q = poly[(k + 1) % poly.Count];
                    float dp = Cross(a, b, p), dq = Cross(a, b, q);
                    if (dp >= 0f) scratch.Add(p);
                    if ((dp >= 0f) != (dq >= 0f)) scratch.Add(p + (q - p) * (dp / (dp - dq)));
                }
                poly.Clear();
                poly.AddRange(scratch);
            }
        }

        /// <summary>The convex hull of the points, clockwise seen from above (x right, z up), from
        /// the point of least x (then z); points in line are left out. Also its bounds.</summary>
        static (List<Vector2> points, Vector2 min, Vector2 max) ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>(points.Count + 1);
            // A point stays when the chain turns right at the last one.
            bool Right(Vector2 a, Vector2 b, Vector2 p) => (p.x - a.x) * (b.y - a.y) - (p.y - a.y) * (b.x - a.x) > 0f;
            foreach (var p in points)
            {
                while (hull.Count > 1 && !Right(hull[hull.Count - 2], hull[hull.Count - 1], p)) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int k = points.Count - 2; k >= 0; k--)
            {
                while (hull.Count >= lower && !Right(hull[hull.Count - 2], hull[hull.Count - 1], points[k])) hull.RemoveAt(hull.Count - 1);
                hull.Add(points[k]);
            }
            if (hull.Count > 0) hull.RemoveAt(hull.Count - 1);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in hull)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return (hull, min, max);
        }

        /// <summary>Hull points within <paramref name="tolerance"/> of the segment between their
        /// neighbours are removed.</summary>
        static void SimplifyHull(List<Vector2> hull, float tolerance)
        {
            int i = 0;
            while (hull.Count > 2 && i < hull.Count)
            {
                var a = hull[(i + hull.Count - 1) % hull.Count];
                var p = hull[i] - a;
                var d = hull[(i + 1) % hull.Count] - a;
                float len2 = d.sqrMagnitude;
                var off = p;
                if (len2 != 0f)
                {
                    float t = Mathf.Clamp01(Vector2.Dot(p, d) / len2);
                    off = d * t - p;
                }
                if (off.sqrMagnitude < tolerance * tolerance) hull.RemoveAt(i);
                else i++;
            }
        }

        // ------------------------------------------------------------------ cutting

        /// <summary>Cuts the group's hull out of each of its polygons: what the hull covers of the
        /// polygon is found by clipping it with the hull's planes; the rest is covered with
        /// triangles (one per hull edge inside the polygon, to the polygon's vertex farthest
        /// outside that edge, and fans from the hull's corners over the polygon's vertices
        /// between), which are merged in order into convex polygons of at most six vertices. The
        /// pieces replace the polygon: the polygons kept move to the front (each cut one's place
        /// taken by the first polygon), the pieces follow. True when a polygon was cut.</summary>
        static bool CutGroup(List<Piece> work, GroupHull hull, float eps)
        {
            int count = work.Count;
            int removed = 0;
            bool changed = false;
            var inner = new List<Vector3>();
            var next = new List<Vector3>();
            var codes = new List<byte>();
            var nextCodes = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                var piece = work[i];
                if (!hull.Sources.Contains(piece.Source)) continue;
                inner.Clear();
                inner.AddRange(piece.Points);
                codes.Clear();
                for (int k = 0; k < inner.Count; k++) codes.Add((byte)k);
                bool outside = false;
                for (int k = 0; k < hull.Planes.Count && k < MaxHullPlanes; k++)
                {
                    int r = Split(eps, inner, codes, hull.Planes[k], k, next, nextCodes);
                    if (r > 0) { outside = true; break; }
                    if (r == 0)
                    {
                        (inner, next) = (next, inner);
                        (codes, nextCodes) = (nextCodes, codes);
                    }
                }
                if (outside || inner.Count == 0) continue;
                var triangles = Remainder(eps, piece.Points, inner, codes, hull.Planes);
                MergeInOrder(triangles);
                foreach (var t in triangles) work.Add(new Piece { Points = t, Source = piece.Source });
                work[i] = work[removed];
                removed++;
                changed = true;
            }
            if (removed > 0) work.RemoveRange(0, removed);
            return changed;
        }

        /// <summary>A vertex's code says which edge ends at it: the polygon's edge ending at vertex
        /// <c>code</c>, or (with the high bit) the hull's plane <c>code &amp; 0x7f</c>.</summary>
        const byte PlaneCode = 0x80;
        const int MaxHullPlanes = 0x7f;

        /// <summary>The convex polygon clipped to the inside of the plane (distances within
        /// <paramref name="eps"/> count as on it) into <paramref name="output"/>: 0 clipped, 1
        /// wholly outside, -1 wholly inside (left as it is).</summary>
        static int Split(float eps, List<Vector3> poly, List<byte> codes, EdgePlane plane, int planeIndex, List<Vector3> output, List<byte> outCodes)
        {
            int n = poly.Count;
            Span<float> d = n <= 64 ? stackalloc float[n] : new float[n];
            float max = float.MinValue, min = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                float x = plane.Distance(poly[k]);
                if (Mathf.Abs(x) < eps) x = 0f;
                d[k] = x;
                max = Mathf.Max(max, x);
                min = Mathf.Min(min, x);
            }
            if (!(max > 0f)) return -1;
            if (min > 0f) return 1;
            output.Clear();
            outCodes?.Clear();
            byte own = (byte)(PlaneCode | planeIndex);
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                float dp = d[prev], dc = d[k];
                var a = poly[prev];
                var b = poly[k];
                if (dp > 0f && dc < 0f)
                {
                    float t = -dc / (dp - dc), s = 1f - t;
                    output.Add(new Vector3(a.x * t + b.x * s, a.y * t + b.y * s, a.z * t + b.z * s));
                    outCodes?.Add(own);
                }
                else if (dp < 0f && dc > 0f)
                {
                    float t = -dp / (dc - dp), s = 1f - t;
                    output.Add(new Vector3(a.x * s + b.x * t, a.y * s + b.y * t, a.z * s + b.z * t));
                    outCodes?.Add(codes[k]);
                }
                if (dc <= 0f)
                {
                    output.Add(b);
                    outCodes?.Add(dp > 0f && dc == 0f ? own : codes[k]);
                }
            }
            return 0;
        }

        static bool Degenerate(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).sqrMagnitude == 0f;

        /// <summary>The polygon minus the inner polygon (what the hull covers of it) as triangles.</summary>
        static List<List<Vector3>> Remainder(float eps, List<Vector3> poly, List<Vector3> inner, List<byte> codes, List<EdgePlane> planes)
        {
            int n = poly.Count, m = inner.Count;
            var used = new bool[n];
            foreach (byte c in codes)
                if ((c & PlaneCode) == 0) used[c] = true;
            var triangles = new List<List<Vector3>>();
            void Add(Vector3 a, Vector3 b, Vector3 c) => triangles.Add(new List<Vector3>(3) { a, b, c });
            if (m == 1)
            {
                for (int i = 0; i < n; i++)
                    if (!used[i] && !Degenerate(inner[0], poly[i], poly[(i + 1) % n])) Add(inner[0], poly[i], poly[(i + 1) % n]);
                return triangles;
            }
            // The polygon vertex each hull edge (ending at inner vertex k) is joined to.
            var toEdgeEnd = new int[m];
            var toEdgeStart = new int[m];
            for (int k = 0; k < m; k++) toEdgeEnd[k] = toEdgeStart[k] = -1;
            for (int k = 0, prev = m - 1; k < m; prev = k++)
            {
                if ((codes[k] & PlaneCode) == 0) continue;
                var plane = planes[codes[k] & ~PlaneCode];
                float best = eps;
                int farthest = -1;
                for (int j = 0; j < n; j++)
                {
                    float x = plane.Distance(poly[j]);
                    if (best < x) { best = x; farthest = j; }
                }
                if (farthest < 0) continue;
                toEdgeEnd[k] = farthest;
                toEdgeStart[prev] = farthest;
                if (!Degenerate(inner[k], inner[prev], poly[farthest])) Add(inner[k], inner[prev], poly[farthest]);
            }
            // Fans from each inner vertex over the polygon's vertices between its two edges' ones.
            for (int k = 0; k < m; k++)
            {
                int from = toEdgeEnd[k], to = toEdgeStart[k];
                if (from != -1 && from != to)
                {
                    for (int j = from; ;)
                    {
                        int after = (j + 1) % n;
                        if (used[after] || Degenerate(inner[k], poly[j], poly[after])) break;
                        Add(inner[k], poly[j], poly[after]);
                        used[after] = true;
                        j = after;
                        if (to == after) break;
                    }
                }
                if (to != -1 && to != from)
                {
                    for (int j = to; ;)
                    {
                        if (used[j]) break;
                        int before = (j + n - 1) % n;
                        if (Degenerate(inner[k], poly[before], poly[j])) break;
                        Add(inner[k], poly[before], poly[j]);
                        used[j] = true;
                        j = before;
                        if (from == before) break;
                    }
                }
            }
            return triangles;
        }

        /// <summary>The smallest turn (twice the area) of a detail triangle kept when a source
        /// polygon's detail triangles are cut to a piece.</summary>
        const float DetailMinTurn = 1e-6f;

        /// <summary>The smallest turn a merged polygon keeps at each vertex: twice the area of the
        /// triangle a vertex makes with its neighbours, in square metres.</summary>
        const float MergeMinTurn = 0.01f;

        /// <summary>Each polygon in order takes in the later ones (last first) it can be merged
        /// with into a convex polygon of at most six vertices.</summary>
        static void MergeInOrder(List<List<Vector3>> polys)
        {
            for (int i = 0; i < polys.Count; i++)
            {
                var current = polys[i];
                for (int j = polys.Count - 1; j > i; j--)
                {
                    var merged = MergePair(current, polys[j]);
                    if (merged == null) continue;
                    current = merged;
                    polys.RemoveAt(j);
                }
                polys[i] = current;
            }
        }

        /// <summary>Two clockwise polygons joined along the first edge of <paramref name="a"/>
        /// that <paramref name="b"/> has the other way round, when the result is convex (every
        /// vertex turning by more than <see cref="MergeMinTurn"/>) and has at most six vertices;
        /// else null.</summary>
        static List<Vector3> MergePair(List<Vector3> a, List<Vector3> b)
        {
            int na = a.Count, nb = b.Count;
            if (na < 3 || nb < 3 || na + nb - 2 > MaxVerts) return null;
            for (int i = 0; i < na; i++)
            {
                int i1 = (i + 1) % na;
                for (int j = 0; j < nb; j++)
                {
                    int j1 = (j + 1) % nb;
                    if (!Same(a[i], b[j1]) || !Same(a[i1], b[j])) continue;
                    // Convex where the joined edge's ends meet the other polygon's edges.
                    if (!(Turn(a[(i + na - 1) % na], a[i], b[(j1 + 1) % nb]) > 0f)) return null;
                    if (!(Turn(a[i1], a[(i1 + 1) % na], b[(j + nb - 1) % nb]) > 0f)) return null;
                    var merged = new List<Vector3>(na + nb - 2);
                    for (int k = i1; k != i; k = (k + 1) % na) merged.Add(a[k]);
                    for (int k = j1; k != j; k = (k + 1) % nb) merged.Add(b[k]);
                    for (int k = 0; k < merged.Count; k++)
                        if (!(Turn(merged[(k + merged.Count - 1) % merged.Count], merged[k], merged[(k + 1) % merged.Count]) > MergeMinTurn)) return null;
                    return merged;
                }
            }
            return null;
        }

        /// <summary>How far a clockwise outline turns at <paramref name="at"/> between
        /// <paramref name="before"/> and <paramref name="after"/> (twice the triangle's area,
        /// positive for a convex corner).</summary>
        static float Turn(Vector3 before, Vector3 at, Vector3 after) =>
            (after.x - before.x) * (at.z - before.z) - (after.z - before.z) * (at.x - before.x);

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        /// <summary>The height of the polygon's detail mesh at (x, z): of a detail triangle under
        /// it, else at the nearest point of the triangles' edges; <paramref name="fallback"/>
        /// without a detail mesh.</summary>
        static float DetailHeight(DtMeshData tile, DtPoly poly, float x, float z, float fallback)
        {
            if (tile.detailMeshes == null || poly.index >= tile.detailMeshes.Length) return fallback;
            var pos = new RcVec3f(x, 0f, z);
            var pd = tile.detailMeshes[poly.index];
            Span<RcVec3f> v = stackalloc RcVec3f[3];
            float nearest = float.MaxValue, height = fallback;
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
                if (DtUtils.ClosestHeightPointTriangle(pos, v[0], v[1], v[2], out float h)) return h;
                for (int k = 0; k < 3; k++)
                {
                    var a = v[k];
                    var b = v[(k + 1) % 3];
                    float d = DtUtils.DistancePtSegSqr2D(pos, a, b, out float s);
                    if (d < nearest)
                    {
                        nearest = d;
                        height = a.Y + (b.Y - a.Y) * s;
                    }
                }
            }
            return height;
        }

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

        static bool Near(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) <= Eps && Mathf.Abs(a.z - b.z) <= Eps;

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

        static DtMeshData Build(DtMeshData tile, List<OutPoly> polys, List<(int Poly, int[] Verts)> offMesh, List<Vector3> verts, float eps)
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
                else if (ClipDetail(tile, src, vs, verts, eps, detailVerts, detailTris, out int extra, out int tris))
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
        static bool ClipDetail(DtMeshData tile, DtPoly src, List<int> vs, List<Vector3> verts, float eps, List<float> detailVerts, List<int> detailTris,
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
            var piece = new List<Vector3>(8);
            var cut = new List<Vector3>(8);
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
                // Clipped by the planes of the polygon's edges in order, as the holes are cut, so
                // each clipped triangle starts where Unity's does.
                for (int e = 0; e < poly.Count && piece.Count >= 3; e++)
                {
                    var a = poly[e];
                    var d = Flat(poly[(e + 1) % poly.Count]) - Flat(a);
                    float length = d.magnitude;
                    if (length <= Vector2.kEpsilon) continue;
                    d /= length;
                    if (ccw) d = -d;
                    var plane = new EdgePlane(-d.y, d.x, -(a.z * d.x + -d.y * a.x + 0f));
                    int r = Split(eps, piece, null, plane, 0, cut, null);
                    if (r > 0) { piece.Clear(); break; }
                    if (r == 0) (piece, cut) = (cut, piece);
                }
                if (piece.Count < 3) continue;
                // Fanned from the first point; triangles with (almost) no area or a repeated
                // vertex are left out.
                int first = Index(piece[0]), previous = Index(piece[1]);
                for (int k = 2; k < piece.Count; k++)
                {
                    int current = Index(piece[k]);
                    float turn = Turn(piece[0], piece[k - 1], piece[k]) * (ccw ? -1f : 1f);
                    if (turn >= DetailMinTurn && current != first && first != previous && previous != current)
                    {
                        int flags = 0;
                        int[] tri = { first, previous, current };
                        for (int edge = 0; edge < 3; edge++)
                            if (OnBoundary(Point(tri[edge]), Point(tri[(edge + 1) % 3]))) flags |= DtDetailTriEdgeFlags.DT_DETAIL_EDGE_BOUNDARY << (edge * 2);
                        tris.AddRange(tri);
                        tris.Add(flags);
                    }
                    previous = current;
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
