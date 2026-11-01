using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Unbound.Runtime
{
    /// <summary>The footprint a carving obstacle cuts out of a NavMesh: a convex polygon in the
    /// plane (counter-clockwise: the inside is on the left of each edge) and its height span.</summary>
    internal sealed class CarveHull
    {
        internal double[] X;
        internal double[] Z;
        internal float MinY;
        internal float MaxY;
        internal double MinX, MaxX, MinZ, MaxZ;

        internal CarveHull(List<(double X, double Z)> points, float minY, float maxY)
        {
            int n = points.Count;
            X = new double[n];
            Z = new double[n];
            for (int i = 0; i < n; i++)
            {
                // The corners are where Unity's carve puts them, in single precision.
                X[i] = (float)points[i].X;
                Z[i] = (float)points[i].Z;
            }
            MinY = minY;
            MaxY = maxY;
            MinX = MaxX = X[0];
            MinZ = MaxZ = Z[0];
            for (int i = 1; i < n; i++)
            {
                MinX = Math.Min(MinX, X[i]); MaxX = Math.Max(MaxX, X[i]);
                MinZ = Math.Min(MinZ, Z[i]); MaxZ = Math.Max(MaxZ, Z[i]);
            }
        }

        internal int Count => X.Length;

        /// <summary>A box's footprint (the plane hull of its corners) grown by a distance with
        /// sharp corners (measured: a rotated box carves its rectangle grown by the agent's
        /// radius, tests/projects/navobstacle).</summary>
        internal static CarveHull Box(IList<(double X, double Z)> corners, double grow, float minY, float maxY)
        {
            var hull = ConvexHull(corners);
            int n = hull.Count;
            var lines = new (double Px, double Pz, double Dx, double Dz)[n];
            for (int i = 0; i < n; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % n];
                double dx = b.X - a.X, dz = b.Z - a.Z, len = Math.Sqrt(dx * dx + dz * dz);
                dx /= len;
                dz /= len;
                // Outward normal of a counter-clockwise edge: (dz, -dx).
                lines[i] = (a.X + dz * grow, a.Z - dx * grow, dx, dz);
            }
            var points = new List<(double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                var l0 = lines[(i + n - 1) % n];
                var l1 = lines[i];
                double den = l0.Dx * l1.Dz - l0.Dz * l1.Dx;
                double t = ((l1.Px - l0.Px) * l1.Dz - (l1.Pz - l0.Pz) * l1.Dx) / den;
                points.Add((l0.Px + l0.Dx * t, l0.Pz + l0.Dz * t));
            }
            return new CarveHull(points, minY, maxY);
        }

        /// <summary>A capsule's footprint: the octagon around its circle grown by a distance, with
        /// corners on the axes and the diagonals (measured, tests/projects/navobstacle).</summary>
        internal static CarveHull Capsule(double cx, double cz, double radius, float minY, float maxY)
        {
            double r = radius / Math.Cos(Math.PI / 8.0);
            var points = new List<(double, double)>(8);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0;
                points.Add((cx + r * Math.Cos(a), cz + r * Math.Sin(a)));
            }
            return new CarveHull(points, minY, maxY);
        }

        static List<(double X, double Z)> ConvexHull(IList<(double X, double Z)> input)
        {
            var pts = new List<(double X, double Z)>(input);
            pts.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            var hull = new List<(double X, double Z)>();
            double Cross((double X, double Z) o, (double X, double Z) a, (double X, double Z) b) =>
                (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-12) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }
    }

    /// <summary>Carving: the polygons of a NavMesh tile with the carving obstacles' footprints cut
    /// out of them, exactly (measured: Unity cuts the baked polygons, the cut's corners lying at
    /// the footprint's own corners, not on the voxel grid; tests/projects/navobstacle).</summary>
    internal static class NavCarving
    {
        sealed class Vtx
        {
            internal double X, Z;
            internal float Y;
            internal readonly HashSet<int> Lines = new HashSet<int>();
        }

        /// <summary>The kind of an edge of a piece: part of an edge of the original tile (Edge,
        /// its canonical vertex pair; Nei, the original edge's neighbour code) or of a cut line.</summary>
        struct Tag
        {
            internal long Edge;
            internal int Nei;
            internal int Line;
        }

        sealed class Piece
        {
            internal int Source;
            internal bool Changed;
            internal List<Vtx> V = new List<Vtx>();
            internal List<Tag> E = new List<Tag>();
        }

        /// <summary>The tile with the hulls cut out of its ground polygons, or the tile itself when
        /// none of them touches it.</summary>
        internal static DtMeshData Carve(DtMeshData tile, IReadOnlyList<CarveHull> hulls, float reach)
        {
            var h = tile.header;
            var touching = new List<CarveHull>();
            foreach (var hull in hulls)
            {
                if (hull.MaxX < h.bmin.X || hull.MinX > h.bmax.X || hull.MaxZ < h.bmin.Z || hull.MinZ > h.bmax.Z) continue;
                if (hull.MaxY < h.bmin.Y - reach || hull.MinY > h.bmax.Y + reach) continue;
                touching.Add(hull);
            }
            if (touching.Count == 0) return tile;
            return new Cutter(tile, touching, reach).Run();
        }

        sealed class Cutter
        {
            readonly DtMeshData m_Tile;
            readonly List<CarveHull> m_Hulls;
            readonly float m_Reach;
            readonly List<(double Ax, double Az, double Bx, double Bz)> m_Lines = new List<(double, double, double, double)>();
            readonly List<(int First, int Count)> m_HullLines = new List<(int, int)>();
            readonly Dictionary<(long, int), Vtx> m_EdgeCuts = new Dictionary<(long, int), Vtx>();
            readonly Dictionary<(int, int), Vtx> m_LineCuts = new Dictionary<(int, int), Vtx>();
            readonly Vtx[] m_Original;
            int m_GroundCount;

            internal Cutter(DtMeshData tile, List<CarveHull> hulls, float reach)
            {
                m_Tile = tile;
                m_Hulls = hulls;
                m_Reach = reach;
                foreach (var hull in hulls)
                {
                    m_HullLines.Add((m_Lines.Count, hull.Count));
                    for (int i = 0; i < hull.Count; i++)
                    {
                        int j = (i + 1) % hull.Count;
                        m_Lines.Add((hull.X[i], hull.Z[i], hull.X[j], hull.Z[j]));
                    }
                }
                m_Original = new Vtx[tile.header.vertCount];
                for (int i = 0; i < m_Original.Length; i++)
                    m_Original[i] = new Vtx { X = tile.verts[i * 3], Y = tile.verts[i * 3 + 1], Z = tile.verts[i * 3 + 2] };
                m_GroundCount = 0;
                for (int i = 0; i < tile.header.polyCount; i++)
                    if (tile.polys[i].GetPolyType() == DtPolyTypes.DT_POLYTYPE_GROUND) m_GroundCount = i + 1;
            }

            static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            double Side(Vtx v, int line)
            {
                if (v.Lines.Contains(line)) return 0.0;
                var l = m_Lines[line];
                // Positive on the left of the line (inside the hull).
                return (l.Bx - l.Ax) * (v.Z - l.Az) - (l.Bz - l.Az) * (v.X - l.Ax);
            }

            /// <summary>Where a cut line crosses an edge of a piece; the same point for every piece
            /// that shares the edge.</summary>
            Vtx Cross(Tag edge, Vtx from, Vtx to, int line, int source)
            {
                if (edge.Line < 0)
                {
                    if (m_EdgeCuts.TryGetValue((edge.Edge, line), out var cached)) return cached;
                    var a = m_Original[(int)(edge.Edge >> 32)];
                    var b = m_Original[(int)(edge.Edge & 0xffffffff)];
                    var l = m_Lines[line];
                    double dx = b.X - a.X, dz = b.Z - a.Z, lx = l.Bx - l.Ax, lz = l.Bz - l.Az;
                    double den = lx * dz - lz * dx;
                    double t = den == 0.0 ? 0.0 : (lz * (a.X - l.Ax) - lx * (a.Z - l.Az)) / den;
                    t = Math.Clamp(t, 0.0, 1.0);
                    var v = new Vtx { X = (float)(a.X + dx * t), Z = (float)(a.Z + dz * t), Y = (float)(a.Y + (b.Y - a.Y) * t) };
                    v.Lines.Add(line);
                    m_EdgeCuts[(edge.Edge, line)] = v;
                    return v;
                }
                var key = edge.Line < line ? (edge.Line, line) : (line, edge.Line);
                if (m_LineCuts.TryGetValue(key, out var corner)) return corner;
                var p = m_Lines[edge.Line];
                var q = m_Lines[line];
                double pdx = p.Bx - p.Ax, pdz = p.Bz - p.Az, qdx = q.Bx - q.Ax, qdz = q.Bz - q.Az;
                double d = pdx * qdz - pdz * qdx;
                double s = d == 0.0 ? 0.0 : ((q.Ax - p.Ax) * qdz - (q.Az - p.Az) * qdx) / d;
                var c = new Vtx { X = (float)(p.Ax + pdx * s), Z = (float)(p.Az + pdz * s) };
                c.Y = Height(source, c.X, c.Z);
                c.Lines.Add(edge.Line);
                c.Lines.Add(line);
                m_LineCuts[key] = c;
                return c;
            }

            /// <summary>The height of an original polygon's surface at a point (its detail
            /// triangles, else its vertices' plane).</summary>
            float Height(int poly, double x, double z)
            {
                var p = m_Tile.polys[poly];
                var a = m_Original[p.verts[0]];
                for (int i = 1; i + 1 < p.vertCount; i++)
                {
                    var b = m_Original[p.verts[i]];
                    var c = m_Original[p.verts[i + 1]];
                    double det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                    if (Math.Abs(det) < 1e-12) continue;
                    double w0 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
                    double w1 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
                    double w2 = 1.0 - w0 - w1;
                    if (w0 >= -1e-6 && w1 >= -1e-6 && w2 >= -1e-6) return (float)(w0 * a.Y + w1 * b.Y + w2 * c.Y);
                }
                return a.Y;
            }

            /// <summary>The parts of a piece left (inside) and right (outside) of a cut line.</summary>
            (Piece Inside, Piece Outside) Split(Piece r, int line)
            {
                int n = r.V.Count;
                var side = new double[n];
                bool anyIn = false, anyOut = false;
                for (int i = 0; i < n; i++)
                {
                    side[i] = Side(r.V[i], line);
                    if (side[i] > 0) anyIn = true;
                    if (side[i] < 0) anyOut = true;
                }
                if (!anyOut) return (r, null);
                if (!anyIn) return (null, r);
                return (Half(r, line, side, true), Half(r, line, side, false));
            }

            Piece Half(Piece r, int line, double[] side, bool inside)
            {
                int n = r.V.Count;
                var verts = new List<(Vtx V, int Edge, bool Original)>();
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    bool keep = inside ? side[i] >= 0 : side[i] <= 0;
                    if (keep) verts.Add((r.V[i], i, true));
                    if ((side[i] > 0 && side[j] < 0) || (side[i] < 0 && side[j] > 0))
                        verts.Add((Cross(r.E[i], r.V[i], r.V[j], line, r.Source), i, false));
                }
                if (verts.Count < 3) return null;
                var piece = new Piece { Source = r.Source, Changed = true };
                for (int m = 0; m < verts.Count; m++)
                {
                    var cur = verts[m];
                    var next = verts[(m + 1) % verts.Count];
                    // Along the same edge of r: that edge's kind; across: the cut line.
                    bool along = cur.Original
                        ? (next.Original ? next.Edge == (cur.Edge + 1) % n : next.Edge == cur.Edge)
                        : (next.Original && next.Edge == (cur.Edge + 1) % n);
                    piece.V.Add(cur.V);
                    piece.E.Add(along ? r.E[cur.Edge] : new Tag { Line = line, Edge = -1, Nei = 0 });
                }
                return Area(piece) > 1e-9 ? piece : null;
            }

            static double Area(Piece p)
            {
                double a = 0;
                for (int i = 0; i < p.V.Count; i++)
                {
                    var u = p.V[i];
                    var w = p.V[(i + 1) % p.V.Count];
                    a += u.X * w.Z - w.X * u.Z;
                }
                return Math.Abs(a) * 0.5;
            }

            internal DtMeshData Run()
            {
                var pieces = new List<Piece>();
                bool any = false;
                for (int p = 0; p < m_GroundCount; p++)
                {
                    var poly = m_Tile.polys[p];
                    var piece = new Piece { Source = p };
                    float minY = float.MaxValue, maxY = float.MinValue;
                    double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    for (int i = 0; i < poly.vertCount; i++)
                    {
                        int j = (i + 1) % poly.vertCount;
                        var v = m_Original[poly.verts[i]];
                        piece.V.Add(v);
                        piece.E.Add(new Tag { Edge = Key(poly.verts[i], poly.verts[j]), Nei = poly.neis[i], Line = -1 });
                        minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
                        minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
                        minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z);
                    }
                    var current = new List<Piece> { piece };
                    for (int k = 0; k < m_Hulls.Count; k++)
                    {
                        var hull = m_Hulls[k];
                        if (hull.MaxX <= minX || hull.MinX >= maxX || hull.MaxZ <= minZ || hull.MinZ >= maxZ) continue;
                        if (hull.MaxY < minY - m_Reach || hull.MinY > maxY + m_Reach) continue;
                        var next = new List<Piece>();
                        var (first, count) = m_HullLines[k];
                        foreach (var r0 in current)
                        {
                            var r = r0;
                            for (int e = 0; e < count && r != null; e++)
                            {
                                var (inside, outside) = Split(r, first + e);
                                if (outside != null) next.Add(outside);
                                r = inside;
                            }
                            // What is left lies inside the hull: carved away.
                            if (r != null) any = true;
                        }
                        current = next;
                    }
                    foreach (var c in current) any |= c.Changed;
                    pieces.AddRange(current);
                }
                if (!any) return m_Tile;
                return Build(pieces);
            }

            DtMeshData Build(List<Piece> pieces)
            {
                // Weld the vertices where they lie (a cut can fall on a corner of the tile).
                var index = new Dictionary<(float, float), int>();
                var verts = new List<Vtx>();
                int Weld(Vtx v)
                {
                    var key = ((float)v.X, (float)v.Z);
                    if (!index.TryGetValue(key, out int i))
                    {
                        i = verts.Count;
                        verts.Add(v);
                        index[key] = i;
                    }
                    return i;
                }
                var polys = new List<(List<int> V, List<Tag> E, int Source, bool Changed)>();
                foreach (var piece in pieces)
                {
                    var vi = new List<int>();
                    var tags = new List<Tag>();
                    for (int k = 0; k < piece.V.Count; k++)
                    {
                        int w = Weld(piece.V[k]);
                        // A repeated vertex closes a zero-length edge: the edge after it goes on.
                        if (vi.Count > 0 && vi[vi.Count - 1] == w)
                        {
                            tags[tags.Count - 1] = piece.E[k];
                            continue;
                        }
                        vi.Add(w);
                        tags.Add(piece.E[k]);
                    }
                    while (vi.Count > 1 && vi[vi.Count - 1] == vi[0])
                    {
                        vi.RemoveAt(vi.Count - 1);
                        tags.RemoveAt(tags.Count - 1);
                    }
                    if (vi.Count < 3) continue;
                    polys.Add((vi, tags, piece.Source, piece.Changed));
                }

                // No T-junctions: a vertex lying inside another polygon's edge splits that edge.
                for (int p = 0; p < polys.Count; p++)
                {
                    var (vi, tags, source, changed) = polys[p];
                    bool split = false;
                    for (int i = 0; i < vi.Count; i++)
                    {
                        var a = verts[vi[i]];
                        var b = verts[vi[(i + 1) % vi.Count]];
                        double dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                        if (len2 < 1e-12) continue;
                        var inner = new List<(double T, int V)>();
                        for (int k = 0; k < verts.Count; k++)
                        {
                            if (k == vi[i] || k == vi[(i + 1) % vi.Count]) continue;
                            var c = verts[k];
                            double t = ((c.X - a.X) * dx + (c.Z - a.Z) * dz) / len2;
                            if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                            double cross = (c.Z - a.Z) * dx - (c.X - a.X) * dz;
                            if (Math.Abs(cross) > 1e-5 * Math.Sqrt(len2)) continue;
                            inner.Add((t, k));
                        }
                        if (inner.Count == 0) continue;
                        inner.Sort((u, w) => u.T.CompareTo(w.T));
                        var tag = tags[i];
                        for (int m = 0; m < inner.Count; m++)
                        {
                            vi.Insert(i + 1 + m, inner[m].V);
                            tags.Insert(i + 1 + m, tag);
                        }
                        i += inner.Count;
                        split = true;
                    }
                    if (split) polys[p] = (vi, tags, source, true);
                }

                // At most DT_VERTS_PER_POLYGON vertices: a long polygon becomes a fan of parts.
                const int nvp = DtDetour.DT_VERTS_PER_POLYGON;
                var final = new List<(List<int> V, List<Tag> E, int Source, bool Changed)>();
                foreach (var poly in polys)
                {
                    var (vi, tags, source, changed) = poly;
                    if (vi.Count <= nvp)
                    {
                        final.Add(poly);
                        continue;
                    }
                    var diagonal = new Tag { Edge = -1, Line = -2, Nei = 0 };
                    int start = 1;
                    while (start < vi.Count - 1)
                    {
                        int end = Math.Min(vi.Count - 1, start + nvp - 2);
                        var pv = new List<int> { vi[0] };
                        var pt = new List<Tag> { start == 1 ? tags[0] : diagonal };
                        for (int k = start; k <= end; k++)
                        {
                            pv.Add(vi[k]);
                            pt.Add(k < end ? tags[k] : (end == vi.Count - 1 ? tags[vi.Count - 1] : diagonal));
                        }
                        final.Add((pv, pt, source, true));
                        start = end;
                    }
                }

                // Neighbours: the polygon on the other side of an edge, a portal to the next tile
                // where the original edge was one, else a wall.
                var edgeOwner = new Dictionary<(int, int), int>();
                for (int p = 0; p < final.Count; p++)
                {
                    var vi = final[p].V;
                    for (int i = 0; i < vi.Count; i++) edgeOwner[(vi[i], vi[(i + 1) % vi.Count])] = p;
                }

                var tile = m_Tile;
                var h = tile.header;
                int offCount = h.offMeshConCount;
                var outVerts = new List<float>(verts.Count * 3 + offCount * 6);
                foreach (var v in verts)
                {
                    outVerts.Add((float)v.X);
                    outVerts.Add(v.Y);
                    outVerts.Add((float)v.Z);
                }
                var outPolys = new DtPoly[final.Count + offCount];
                var details = new DtPolyDetail[final.Count];
                var detailVerts = new List<float>();
                var detailTris = new List<int>();
                int edges = 0;
                for (int p = 0; p < final.Count; p++)
                {
                    var (vi, tags, source, changed) = final[p];
                    var src = tile.polys[source];
                    var poly = new DtPoly(p, nvp);
                    poly.vertCount = vi.Count;
                    poly.flags = src.flags;
                    poly.areaAndtype = src.areaAndtype;
                    for (int i = 0; i < vi.Count; i++)
                    {
                        poly.verts[i] = vi[i];
                        var tag = tags[i];
                        if (tag.Line == -1 && (tag.Nei & DtDetour.DT_EXT_LINK) != 0)
                            poly.neis[i] = tag.Nei;
                        else if (edgeOwner.TryGetValue((vi[(i + 1) % vi.Count], vi[i]), out int other))
                            poly.neis[i] = other + 1;
                        else
                            poly.neis[i] = 0;
                    }
                    edges += vi.Count;
                    outPolys[p] = poly;
                    if (!changed)
                    {
                        // An untouched polygon keeps its detail surface.
                        var d = tile.detailMeshes[source];
                        details[p] = new DtPolyDetail(detailVerts.Count / 3, detailTris.Count / 4, (byte)d.vertCount, (byte)d.triCount);
                        for (int k = 0; k < d.vertCount * 3; k++) detailVerts.Add(tile.detailVerts[d.vertBase * 3 + k]);
                        for (int k = 0; k < d.triCount * 4; k++) detailTris.Add(tile.detailTris[d.triBase * 4 + k]);
                    }
                    else
                    {
                        // A cut polygon is flat between its vertices: a fan of triangles.
                        int tris = vi.Count - 2;
                        details[p] = new DtPolyDetail(detailVerts.Count / 3, detailTris.Count / 4, 0, (byte)tris);
                        for (int k = 1; k <= tris; k++)
                        {
                            int flags = (k == 1 ? 1 : 0) | (1 << 2) | ((k == tris ? 1 : 0) << 4);
                            detailTris.Add(0);
                            detailTris.Add(k);
                            detailTris.Add(k + 1);
                            detailTris.Add(flags);
                        }
                    }
                }

                // Off-mesh connections stay as they were, after the ground polygons.
                var cons = new DtOffMeshConnection[offCount];
                for (int c = 0; c < offCount; c++)
                {
                    var con = tile.offMeshCons[c];
                    var srcPoly = tile.polys[con.poly];
                    int v0 = outVerts.Count / 3;
                    for (int k = 0; k < 2; k++)
                        for (int a = 0; a < 3; a++) outVerts.Add(tile.verts[srcPoly.verts[k] * 3 + a]);
                    var poly = new DtPoly(final.Count + c, nvp);
                    poly.vertCount = 2;
                    poly.verts[0] = v0;
                    poly.verts[1] = v0 + 1;
                    poly.flags = srcPoly.flags;
                    poly.areaAndtype = srcPoly.areaAndtype;
                    outPolys[final.Count + c] = poly;
                    cons[c] = new DtOffMeshConnection
                    {
                        pos = con.pos,
                        rad = con.rad,
                        poly = final.Count + c,
                        flags = con.flags,
                        side = con.side,
                        userId = con.userId,
                    };
                }

                var header = new DtMeshHeader
                {
                    magic = h.magic,
                    version = h.version,
                    x = h.x,
                    y = h.y,
                    layer = h.layer,
                    userId = h.userId,
                    polyCount = outPolys.Length,
                    vertCount = outVerts.Count / 3,
                    maxLinkCount = edges * 3 + offCount * 4 + 8,
                    detailMeshCount = final.Count,
                    detailVertCount = detailVerts.Count / 3,
                    detailTriCount = detailTris.Count / 4,
                    bvNodeCount = 0,
                    offMeshConCount = offCount,
                    offMeshBase = final.Count,
                    walkableHeight = h.walkableHeight,
                    walkableRadius = h.walkableRadius,
                    walkableClimb = h.walkableClimb,
                    bmin = h.bmin,
                    bmax = h.bmax,
                    bvQuantFactor = h.bvQuantFactor,
                };
                return new DtMeshData
                {
                    header = header,
                    polys = outPolys,
                    verts = outVerts.ToArray(),
                    detailMeshes = details,
                    detailVerts = detailVerts.ToArray(),
                    detailTris = detailTris.ToArray(),
                    bvTree = null,
                    offMeshCons = cons,
                };
            }
        }
    }
}
