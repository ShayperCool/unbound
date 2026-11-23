using System;
using System.Collections.Generic;
using BigInteger = System.Numerics.BigInteger;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Tetrahedralizes the probes of several scenes together, as LightProbes.Tetrahedralize does
    /// with additively loaded scenes. The cells follow the conventions of the Editor's own bakes
    /// (checked against them): the inner cells are the Delaunay tetrahedralization of the probes,
    /// each with det(p0 − p3, p1 − p3, p2 − p3) &lt; 0 and the matrix inverting those columns, its
    /// neighbor k across the face opposite vertex k; after them one outer cell per hull triangle
    /// (fourth index -1, neighbor 3 the inner cell, neighbors 0..2 the outer cells across its
    /// edges); a hull probe's ray is the angle-weighted mean of the outward normals of its hull
    /// triangles. The cells' matrices are rounded as the Editor's (<see cref="Matrix"/>). The cells' order and the order
    /// of each cell's vertices are Unbound's: the Editor's cells are consistent with the fourth
    /// vertex being the one inserted last (all 55 baked cells of tests/projects/lightprobes), but
    /// its insertion order depends on the probes (no sort by index, axis, lexicographic order or
    /// distance fits) and the rotation of the other three on its history, so neither is
    /// reproduced. Inside the hull the interpolation is the same blend; where Unbound's cell has
    /// the Editor's vertex order (recorded at two of five points of tests/projects/lightprobes
    /// tetrahedralized together) the weights are the Editor's to the bit, elsewhere they can
    /// differ in the last bit, and tetrahedron indices differ.
    /// </summary>
    internal static class ProbeTetrahedralization
    {
        internal static ProbeSet Combine(List<ProbeSet> sets)
        {
            var positions = new List<Vector3>();
            var probes = new List<SphericalHarmonicsL2>();
            var occlusion = new List<Vector4>();
            var occlusionChannels = new List<byte>();
            foreach (var set in sets)
            {
                positions.AddRange(set.Positions);
                // The baked coefficients (none: zeros): realtime GI's lighting stays with the set
                // it lights.
                if (set.Baked) probes.AddRange(set.BakedAlone ?? set.Probes);
                else probes.AddRange(new SphericalHarmonicsL2[set.Positions.Length]);
                for (int i = 0; i < set.Positions.Length; i++)
                {
                    occlusion.Add(i < set.Occlusion.Length ? set.Occlusion[i] : Vector4.one);
                    occlusionChannels.Add(set.OcclusionChannels == null ? (byte)15 : set.OcclusionChannels[i]);
                }
            }
            // A combination with any baked set reports every probe, zeros for realtime-only sets
            // (unmeasured; realtime GI lights each scene's own set, not the combination).
            if (sets.Exists(x => x.Baked) && sets.Exists(x => !x.Baked))
                Unbound.Runtime.Stubs.Ignored("LightProbes",
                    "of scenes precomputed for realtime GI only join baked probes with zero lighting");
            var result = new ProbeSet { Positions = positions.ToArray(), Probes = probes.ToArray(), Occlusion = occlusion.ToArray(), OcclusionChannels = occlusionChannels.ToArray(), Baked = sets.Exists(x => x.Baked) };
            Build(result);
            return result;
        }

        /// <summary>The vertex index of the point at infinity: the fourth vertex of a hull cell
        /// while building (it sorts after every probe).</summary>
        const int Infinite = int.MaxValue;

        /// <summary>Distance below which a probe counts as on another's position, line or plane,
        /// both for flatness (against probe 0 and the probes farthest from it and from their line)
        /// and when the first cell is chosen, relative to the largest of the probes' extent and
        /// coordinates: about eight float32 rounding steps, the noise a probe layer keeps after a
        /// rotation or a far offset moves it. Probes all that close to one plane make no cells, as
        /// probes exactly in one plane do.</summary>
        const double PlaneTolerance = 1e-6;

        /// <summary>Cells that may accumulate before the replaced ones are dropped from the scan.</summary>
        const int CompactAt = 4096;

        /// <summary>A cell while building: a probe tetrahedron, or a hull triangle (D =
        /// <see cref="Infinite"/>). Sign is the sign of <see cref="Predicates.Orient"/> of the
        /// tetrahedron, or of the hull triangle with a probe inside the hull.</summary>
        struct Tet
        {
            public int A, B, C, D;
            public int Sign;
            public bool Dead;
        }

        static double Distance(double[] x, double[] y, double[] z, int a, int b)
        {
            double dx = x[b] - x[a], dy = y[b] - y[a], dz = z[b] - z[a];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>(b − a) × (c − a).</summary>
        static Vector3d Cross(double[] x, double[] y, double[] z, int a, int b, int c) =>
            Vector3d.Cross(new Vector3d(x[b] - x[a], y[b] - y[a], z[b] - z[a]), new Vector3d(x[c] - x[a], y[c] - y[a], z[c] - z[a]));

        /// <summary>The cells of <paramref name="set"/>'s positions (none for fewer than four
        /// probes not in a plane).</summary>
        internal static void Build(ProbeSet set)
        {
            var p = set.Positions;
            set.UpdateBounds();
            int n = p.Length;
            set.Cells = Array.Empty<ProbeSet.Cell>();
            set.HullRays = new Vector3[n];
            if (n < 4) return;

            // Points in double.
            var x = new double[n];
            var y = new double[n];
            var z = new double[n];
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                x[i] = p[i].x; y[i] = p[i].y; z[i] = p[i].z;
                minX = Math.Min(minX, x[i]); minY = Math.Min(minY, y[i]); minZ = Math.Min(minZ, z[i]);
                maxX = Math.Max(maxX, x[i]); maxY = Math.Max(maxY, y[i]); maxZ = Math.Max(maxZ, z[i]);
            }
            double size = Math.Max(Math.Max(maxX - minX, maxY - minY), Math.Max(maxZ - minZ, 1e-6));
            if (!double.IsFinite(size)) return;
            double magnitude = Math.Max(Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)), Math.Max(Math.Abs(minY), Math.Abs(maxY))),
                Math.Max(Math.Abs(minZ), Math.Abs(maxZ)));

            var tets = new List<Tet>();

            // Flatness: probe 0, the probe farthest from it, the probe farthest from their line and
            // the probe farthest from the plane of those three (the first of equals). Probes all
            // within `flat` of that point, line or plane make no cells, and so do probes within
            // `flat` of the first cell's plane below.
            double flat = PlaneTolerance * Math.Max(size, magnitude);
            int f1 = -1, f2 = -1, f3 = -1;
            double far = flat;
            for (int i = 1; i < n; i++)
            {
                double d = Distance(x, y, z, 0, i);
                if (d > far) (far, f1) = (d, i);
            }
            if (f1 < 0) return;
            double baseline = Distance(x, y, z, 0, f1);
            far = flat;
            for (int i = 1; i < n; i++)
            {
                double d = Cross(x, y, z, 0, f1, i).Length() / baseline;
                if (d > far) (far, f2) = (d, i);
            }
            if (f2 < 0) return;
            var plane = Cross(x, y, z, 0, f1, f2);
            double area = plane.Length();
            far = flat;
            for (int i = 1; i < n; i++)
            {
                double d = Math.Abs(plane.Dot(x[i] - x[0], y[i] - y[0], z[i] - z[0])) / area;
                if (d > far) (far, f3) = (d, i);
            }
            if (f3 < 0) return;

            // The first tetrahedron: probe 0 and the first probes after it off its point, line and
            // plane.
            int i1 = -1, i2 = -1, i3 = -1;
            for (int i = 1; i < n && i1 < 0; i++)
                if (Distance(x, y, z, 0, i) > flat) i1 = i;
            for (int i = 1; i < n && i1 >= 0 && i2 < 0; i++)
                if (i != i1 && Cross(x, y, z, 0, i1, i).Length() > flat * Distance(x, y, z, 0, i1)) i2 = i;
            for (int i = 1; i < n && i2 >= 0 && i3 < 0; i++)
            {
                if (i == i1 || i == i2) continue;
                var normal = Cross(x, y, z, 0, i1, i2);
                if (Math.Abs(normal.Dot(x[i] - x[0], y[i] - y[0], z[i] - z[0])) > flat * normal.Length()) i3 = i;
            }
            // Probes within `flat` of that plane are flat too.
            if (i3 < 0) return;
            int[] firstCell = { 0, i1, i2, i3 };

            // Every test below is decided exactly (Predicates), so the probes a cell's replacement
            // depends on are judged consistently however close to a tie they are; ties (a probe on
            // a circumsphere) keep the cells already made.
            int Orient(int a, int b, int c, int d) => Predicates.Orient(x[a], y[a], z[a], x[b], y[b], z[b], x[c], y[c], z[c], x[d], y[d], z[d]);
            bool Cell(ref Tet t)
            {
                t.Sign = Orient(t.A, t.B, t.C, t.D);
                return t.Sign != 0;
            }
            // The unbounded hull: past each hull triangle a cell with the point at infinity, which a
            // probe beyond that triangle's plane (or in it, inside the triangle's circumcircle)
            // replaces, so the cells are those of the probes' own Delaunay tetrahedralization
            // however far a cell's circumsphere reaches. The first cell stays inside the hull and
            // tells the triangles' inner side.
            bool Hull(ref Tet t)
            {
                foreach (int inside in firstCell)
                {
                    t.Sign = Orient(t.A, t.B, t.C, inside);
                    if (t.Sign != 0) return true;
                }
                return false;
            }
            bool Conflicts(in Tet t, int i)
            {
                if (t.D != Infinite)
                    return Predicates.InSphere(x[t.A], y[t.A], z[t.A], x[t.B], y[t.B], z[t.B], x[t.C], y[t.C], z[t.C],
                        x[t.D], y[t.D], z[t.D], x[i], y[i], z[i]) * t.Sign > 0;
                int side = Orient(t.A, t.B, t.C, i);
                if (side != 0) return side == -t.Sign;
                return Predicates.InCircle(x[t.A], y[t.A], z[t.A], x[t.B], y[t.B], z[t.B], x[t.C], y[t.C], z[t.C], x[i], y[i], z[i]);
            }
            var first = new Tet { A = 0, B = i1, C = i2, D = i3 };
            Cell(ref first);
            tets.Add(first);
            foreach (var (a, b, c) in new[] { (i1, i2, i3), (0, i2, i3), (0, i1, i3), (0, i1, i2) })
            {
                var outer = new Tet { A = a, B = b, C = c, D = Infinite };
                Hull(ref outer);
                tets.Add(outer);
            }

            var faces = new Dictionary<(int, int, int), int>();
            var faceOrder = new List<(int, int, int)>();
            for (int i = 1; i < n; i++)
            {
                if (i == i1 || i == i2 || i == i3) continue;
                faces.Clear();
                faceOrder.Clear();
                for (int t = 0; t < tets.Count; t++)
                {
                    var tet = tets[t];
                    if (tet.Dead || !Conflicts(tet, i)) continue;
                    tet.Dead = true;
                    tets[t] = tet;
                    foreach (var f in new[] { (tet.B, tet.C, tet.D), (tet.A, tet.C, tet.D), (tet.A, tet.B, tet.D), (tet.A, tet.B, tet.C) })
                    {
                        var key = Sorted(f.Item1, f.Item2, f.Item3);
                        if (faces.TryGetValue(key, out var seen)) faces[key] = seen + 1;
                        else { faces[key] = 1; faceOrder.Add(key); }
                    }
                }
                foreach (var key in faceOrder)
                {
                    if (faces[key] != 1) continue;
                    // Infinite sorts last: a face on the hull makes a new hull cell with probe i.
                    var tet = key.Item3 == Infinite
                        ? new Tet { A = key.Item1, B = key.Item2, C = i, D = Infinite }
                        : new Tet { A = key.Item1, B = key.Item2, C = key.Item3, D = i };
                    if (tet.D == Infinite ? Hull(ref tet) : Cell(ref tet)) tets.Add(tet);
                }
                if (tets.Count > CompactAt) tets.RemoveAll(t => t.Dead);
            }

            // The inner cells, oriented as the Editor's.
            var inner = new List<int[]>();
            foreach (var t in tets)
            {
                if (t.Dead || t.A >= n || t.B >= n || t.C >= n || t.D >= n) continue;
                int a = t.A, b = t.B, c = t.C, d = t.D;
                // det(a − d, b − d, c − d) < 0, the Editor's orientation, is Orient(a, b, c, d) < 0.
                inner.Add(t.Sign < 0 ? new[] { a, b, c, d } : new[] { b, a, c, d });
            }
            if (inner.Count == 0) return;

            var faceCells = new Dictionary<(int, int, int), List<(int Cell, int Vertex)>>();
            for (int c = 0; c < inner.Count; c++)
                for (int k = 0; k < 4; k++)
                {
                    var f = Opposite(inner[c], k);
                    if (!faceCells.TryGetValue(f, out var list)) faceCells[f] = list = new List<(int, int)>();
                    list.Add((c, k));
                }

            var cells = new List<ProbeSet.Cell>();
            var neighbors = new int[inner.Count, 4];
            var hull = new List<(int[] Tri, int Inner, int Apex)>();
            for (int c = 0; c < inner.Count; c++)
                for (int k = 0; k < 4; k++)
                {
                    var list = faceCells[Opposite(inner[c], k)];
                    int other = -1;
                    foreach (var (cell, _) in list) if (cell != c) other = cell;
                    if (other < 0)
                    {
                        var tri = new int[3];
                        int m = 0;
                        for (int j = 0; j < 4; j++) if (j != k) tri[m++] = inner[c][j];
                        other = inner.Count + hull.Count;
                        hull.Add((tri, c, inner[c][k]));
                    }
                    neighbors[c, k] = other;
                }

            for (int c = 0; c < inner.Count; c++)
            {
                var v = inner[c];
                Vector3 p3 = p[v[3]];
                Matrix(p[v[0]] - p3, p[v[1]] - p3, p[v[2]] - p3, out var r0, out var r1, out var r2);
                cells.Add(new ProbeSet.Cell
                {
                    I0 = v[0], I1 = v[1], I2 = v[2], I3 = v[3],
                    N0 = neighbors[c, 0], N1 = neighbors[c, 1], N2 = neighbors[c, 2], N3 = neighbors[c, 3],
                    R0 = r0, R1 = r1, R2 = r2,
                });
            }

            // Outer cells: across each hull edge, the hull triangle sharing it.
            var edges = new Dictionary<(int, int), List<int>>();
            for (int h = 0; h < hull.Count; h++)
                for (int k = 0; k < 3; k++)
                {
                    var tri = hull[h].Tri;
                    var e = Edge(tri[(k + 1) % 3], tri[(k + 2) % 3]);
                    if (!edges.TryGetValue(e, out var list)) edges[e] = list = new List<int>();
                    list.Add(h);
                }
            for (int h = 0; h < hull.Count; h++)
            {
                var (tri, cell, _) = hull[h];
                var across = new int[3];
                for (int k = 0; k < 3; k++)
                {
                    across[k] = -1;
                    foreach (var o in edges[Edge(tri[(k + 1) % 3], tri[(k + 2) % 3])]) if (o != h) across[k] = inner.Count + o;
                }
                cells.Add(new ProbeSet.Cell
                {
                    I0 = tri[0], I1 = tri[1], I2 = tri[2], I3 = -1,
                    N0 = across[0], N1 = across[1], N2 = across[2], N3 = cell,
                });
            }
            var outward = new List<int[]>(hull.Count);
            foreach (var (tri, _, apex) in hull) outward.Add(Outward(p, tri, apex));
            set.HullRays = HullRays(p, outward);
            set.Cells = cells.ToArray();
        }

        /// <summary>A hull triangle with its corners in the order whose (p2 − p0) × (p1 − p0)
        /// points out of the hull (away from <paramref name="apex"/>, its inner cell's fourth
        /// probe).</summary>
        static int[] Outward(Vector3[] p, int[] tri, int apex)
        {
            var a = new Vector3d(p[tri[0]].x, p[tri[0]].y, p[tri[0]].z);
            var e1 = new Vector3d(p[tri[1]].x, p[tri[1]].y, p[tri[1]].z) - a;
            var e2 = new Vector3d(p[tri[2]].x, p[tri[2]].y, p[tri[2]].z) - a;
            var toward = new Vector3d(p[apex].x, p[apex].y, p[apex].z) - a;
            return Vector3d.Dot(Vector3d.Cross(e2, e1), toward) > 0 ? new[] { tri[0], tri[2], tri[1] } : (int[])tri.Clone();
        }

        /// <summary>A hull triangle's unit normal out of the hull, (p2 − p0) × (p1 − p0) in single
        /// precision.</summary>
        static Vector3 OutwardNormal(Vector3[] p, int[] tri)
        {
            Vector3 p0 = p[tri[0]], e1 = p[tri[1]] - p0, e2 = p[tri[2]] - p0;
            float nx = e1.z * e2.y - e1.y * e2.z, ny = e2.z * e1.x - e1.z * e2.x, nz = e2.x * e1.y - e2.y * e1.x;
            float l = MathF.Sqrt((nx * nx + ny * ny) + nz * nz);
            return new Vector3(nx / l, ny / l, nz / l);
        }

        static float Length(Vector3 v) => MathF.Sqrt((v.x * v.x + v.y * v.y) + v.z * v.z);

        /// <summary>Each probe's ray out of the hull (zeros for a probe on no hull triangle), in
        /// single precision as the Editor's bakes have them (tests/projects/bakedbuffers
        /// `Probes*`): the sum, over the hull triangles <paramref name="outer"/> in their order
        /// (each with <see cref="Outward"/>'s corner order), of the triangle's angle at the probe
        /// (the arc cosine of its unit edges' dot product) times its outward unit normal, then
        /// of unit length.</summary>
        internal static Vector3[] HullRays(Vector3[] p, IReadOnlyList<int[]> outer)
        {
            var sums = new Vector3[p.Length];
            foreach (var t in outer)
            {
                var normal = OutwardNormal(p, t);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 e1 = p[t[(k + 1) % 3]] - p[t[k]], e2 = p[t[(k + 2) % 3]] - p[t[k]];
                    float l1 = Length(e1), l2 = Length(e2);
                    float d = ((e1.x / l1) * (e2.x / l2) + (e2.y / l2) * (e1.y / l1)) + (e1.z / l1) * (e2.z / l2);
                    float angle = MathF.Acos(Math.Clamp(d, -1f, 1f));
                    ref var s = ref sums[t[k]];
                    s = new Vector3(s.x + angle * normal.x, s.y + angle * normal.y, s.z + angle * normal.z);
                }
            }
            for (int i = 0; i < sums.Length; i++)
            {
                var s = sums[i];
                if (s.x == 0f && s.y == 0f && s.z == 0f) continue;
                float l = Length(s);
                sums[i] = new Vector3(s.x / l, s.y / l, s.z / l);
            }
            return sums;
        }

        /// <summary>The cells of probes as a bake's lighting data keeps them (its LightProbes
        /// object's tetrahedralization): per cell its four probes (an outer cell's fourth -1, or
        /// -2 where its matrix is degenerate), the cells across its faces, its matrix of three
        /// rows of four, and whether it is valid; and the probes' hull rays.</summary>
        internal sealed class WrittenCells
        {
            internal int[][] Indices = Array.Empty<int[]>();
            internal int[][] Neighbors = Array.Empty<int[]>();
            internal float[][] Matrices = Array.Empty<float[]>();
            internal bool[] Valid = Array.Empty<bool>();
            internal Vector3[] HullRays = Array.Empty<Vector3>();
            /// <summary>Whether an outer cell's matrix is degenerate (not measured).</summary>
            internal bool Degenerate;
        }

        /// <summary>The cells of <paramref name="positions"/> as a bake's lighting data keeps them:
        /// Unbound's tetrahedralization (<see cref="Build"/>; its cells' order and the order of
        /// their corners are Unbound's, the Editor's come from its own construction) finished as
        /// <see cref="Finish"/> does. Null for probes that make no cells though there are four
        /// or more (all in one plane: not measured).</summary>
        internal static WrittenCells Written(Vector3[] positions)
        {
            var set = new ProbeSet { Positions = positions };
            Build(set);
            if (set.Cells.Length == 0) return positions.Length < 4 ? new WrittenCells() : null;
            var inner = new List<int[]>();
            var outer = new List<int[]>();
            var adjacent = new List<int>();
            foreach (var c in set.Cells)
            {
                if (c.I3 >= 0) inner.Add(new[] { c.I0, c.I1, c.I2, c.I3 });
                else
                {
                    var tri = new[] { c.I0, c.I1, c.I2 };
                    var apexCell = set.Cells[c.N3];
                    int apex = -1;
                    foreach (var v in new[] { apexCell.I0, apexCell.I1, apexCell.I2, apexCell.I3 })
                        if (Array.IndexOf(tri, v) < 0) apex = v;
                    outer.Add(Outward(positions, tri, apex));
                    adjacent.Add(c.N3);
                }
            }
            return Finish(positions, inner, outer, adjacent);
        }

        /// <summary>
        /// The cells of a tetrahedralization as the Editor's bakes keep them
        /// (tests/projects/bakedbuffers `Probes*`, bit for bit from the Editor's own cells):
        /// the inner cells <paramref name="inner"/> (det(p0 − p3, p1 − p3, p2 − p3) &lt; 0) with
        /// <see cref="Matrix"/>'s rows and a fourth column of zeros, valid where the unit edges
        /// from the fourth probe span at least 0.001 (their triple product), the neighbor k across
        /// the face opposite probe k; then one outer cell per hull triangle
        /// <paramref name="outer"/> (corners as <see cref="Outward"/> orders them) with -1 for its
        /// fourth probe, its neighbor (k + 2) mod 3 the outer cell across its edge (k, k + 1) and
        /// neighbor 3 its inner cell <paramref name="adjacent"/>, its matrix the cubic's that
        /// takes a point outside to the triangle along the probes' hull rays (divided by its
        /// determinant; one at most 1e-5 leaves it undivided and the fourth probe -2), valid.
        /// Last, the inner cell holding the probes' mean point (summed in order) trades places
        /// with the first, the neighbors following.
        /// </summary>
        internal static WrittenCells Finish(Vector3[] p, IReadOnlyList<int[]> inner, IReadOnlyList<int[]> outer, IReadOnlyList<int> adjacent)
        {
            int n = inner.Count, total = n + outer.Count;
            var w = new WrittenCells
            {
                Indices = new int[total][],
                Neighbors = new int[total][],
                Matrices = new float[total][],
                Valid = new bool[total],
            };
            var faces = new Dictionary<(int, int, int), List<int>>();
            var edges = new Dictionary<(int, int), List<int>>();
            void Add<TKey>(Dictionary<TKey, List<int>> map, TKey key, int cell)
            {
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
                list.Add(cell);
            }
            for (int c = 0; c < n; c++)
            {
                var t = inner[c];
                w.Indices[c] = (int[])t.Clone();
                Vector3 p3 = p[t[3]];
                Matrix(p[t[0]] - p3, p[t[1]] - p3, p[t[2]] - p3, out var r0, out var r1, out var r2);
                w.Matrices[c] = new[] { r0.x, r0.y, r0.z, 0f, r1.x, r1.y, r1.z, 0f, r2.x, r2.y, r2.z, 0f };
                w.Valid[c] = InnerValid(p, t);
                for (int k = 0; k < 4; k++) Add(faces, Opposite(t, k), c);
            }
            for (int h = 0; h < outer.Count; h++)
            {
                var t = outer[h];
                w.Indices[n + h] = new[] { t[0], t[1], t[2], -1 };
                w.Valid[n + h] = true;
                Add(faces, Sorted(t[0], t[1], t[2]), n + h);
                for (int k = 0; k < 3; k++) Add(edges, Edge(t[k], t[(k + 1) % 3]), n + h);
            }
            int Other(List<int> list, int self)
            {
                foreach (var c in list) if (c != self) return c;
                return -1;
            }
            for (int c = 0; c < n; c++)
            {
                w.Neighbors[c] = new int[4];
                for (int k = 0; k < 4; k++) w.Neighbors[c][k] = Other(faces[Opposite(inner[c], k)], c);
            }
            w.HullRays = HullRays(p, outer);
            for (int h = 0; h < outer.Count; h++)
            {
                var t = outer[h];
                var nb = new[] { -1, -1, -1, adjacent[h] };
                for (int k = 0; k < 3; k++) nb[(k + 2) % 3] = Other(edges[Edge(t[k], t[(k + 1) % 3])], n + h);
                w.Neighbors[n + h] = nb;
                if (!OuterMatrix(p, w.HullRays, t, out w.Matrices[n + h]))
                {
                    w.Indices[n + h][3] = -2;
                    w.Degenerate = true;
                }
            }
            if (p.Length > 3 && n > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (var q in p) sum = new Vector3(sum.x + q.x, sum.y + q.y, sum.z + q.z);
                float count = p.Length;
                var mean = new Vector3(sum.x / count, sum.y / count, sum.z / count);
                int k = Containing(p, w, n, mean);
                if (k > 0)
                {
                    (w.Indices[0], w.Indices[k]) = (w.Indices[k], w.Indices[0]);
                    (w.Neighbors[0], w.Neighbors[k]) = (w.Neighbors[k], w.Neighbors[0]);
                    (w.Matrices[0], w.Matrices[k]) = (w.Matrices[k], w.Matrices[0]);
                    (w.Valid[0], w.Valid[k]) = (w.Valid[k], w.Valid[0]);
                    foreach (var nb in w.Neighbors)
                        for (int j = 0; j < 4; j++)
                            nb[j] = nb[j] == 0 ? k : nb[j] == k ? 0 : nb[j];
                }
            }
            return w;
        }

        static Vector3 Unit(Vector3 v)
        {
            float l = Length(v);
            return new Vector3(v.x / l, v.y / l, v.z / l);
        }

        /// <summary>Whether an inner cell is valid: the triple product of its unit edges from its
        /// fourth probe is at least 0.001 in size.</summary>
        static bool InnerValid(Vector3[] p, int[] t)
        {
            Vector3 p3 = p[t[3]], a = Unit(p[t[0]] - p3), b = Unit(p[t[1]] - p3), c = Unit(p[t[2]] - p3);
            float triple = ((a.y * b.z - b.y * a.z) * c.x + (a.z * b.x - b.z * a.x) * c.y) + (a.x * b.y - a.y * b.x) * c.z;
            return MathF.Abs(triple) >= 0.001f;
        }

        /// <summary>The first of the <paramref name="inner"/> cells whose weights of
        /// <paramref name="x"/> are none below zero (0 for none).</summary>
        static int Containing(Vector3[] p, WrittenCells w, int inner, Vector3 x)
        {
            for (int c = 0; c < inner; c++)
            {
                var m = w.Matrices[c];
                Vector3 d = x - p[w.Indices[c][3]];
                float w0 = (m[0] * d.x + m[1] * d.y) + m[2] * d.z;
                float w1 = (m[4] * d.x + m[5] * d.y) + m[6] * d.z;
                float w2 = (m[8] * d.x + m[9] * d.y) + m[10] * d.z;
                if (Math.Min(w0, Math.Min(w1, w2)) >= 0f && 1f - ((w0 + w1) + w2) >= 0f) return c;
            }
            return 0;
        }

        /// <summary>An outer cell's matrix (three rows of four): the cubic that takes a point
        /// outside the hull to its place on the triangle <paramref name="t"/> along the corners'
        /// rays, divided by its determinant; false (undivided) where that is at most 1e-5 in
        /// size.</summary>
        static bool OuterMatrix(Vector3[] p, Vector3[] rays, int[] t, out float[] matrix)
        {
            float Ax = rays[t[0]].x, Ay = rays[t[0]].y, Az = rays[t[0]].z;
            float Bx = rays[t[1]].x, By = rays[t[1]].y, Bz = rays[t[1]].z;
            float Cx = rays[t[2]].x, Cy = rays[t[2]].y, Cz = rays[t[2]].z;
            float P0x = p[t[0]].x, P0y = p[t[0]].y, P0z = p[t[0]].z;
            float P1x = p[t[1]].x, P1y = p[t[1]].y, P1z = p[t[1]].z;
            float P2x = p[t[2]].x, P2y = p[t[2]].y, P2z = p[t[2]].z;
            float e00 = (((Bz - Cz) * (Ay - Cy)) - ((Az - Cz) * (By - Cy)));
            float e10 = (((((Ay - Cy) * (P1z - P2z)) + ((Bz - Cz) * (P0y - P2y))) - ((Az - Cz) * (P1y - P2y))) - ((By - Cy) * (P0z - P2z)));
            float e20 = (((P1z - P2z) * (P0y - P2y)) - ((P1y - P2y) * (P0z - P2z)));
            float e01 = (((Az - Cz) * (Bx - Cx)) - ((Bz - Cz) * (Ax - Cx)));
            float e11 = ((((-((P0x - P2x) * (Bz - Cz))) - ((Ax - Cx) * (P1z - P2z))) + ((Bx - Cx) * (P0z - P2z))) + ((Az - Cz) * (P1x - P2x)));
            float e21 = (((P1x - P2x) * (P0z - P2z)) - ((P0x - P2x) * (P1z - P2z)));
            float e02 = (((By - Cy) * (Ax - Cx)) - ((Bx - Cx) * (Ay - Cy)));
            float e12 = (((((P0x - P2x) * (By - Cy)) - ((P0y - P2y) * (Bx - Cx))) + ((Ax - Cx) * (P1y - P2y))) - ((Ay - Cy) * (P1x - P2x)));
            float e22 = (((P0x - P2x) * (P1y - P2y)) - ((P0y - P2y) * (P1x - P2x)));
            float e03 = (((((Ay - Cy) * (P1z - P2z)) * (-Cx)) + (((((Bz - Cz) * (P0y - P2y)) * (-Cx)) + ((((((Az - Cz) * (P1x - P2x)) * (-Cy)) + (((((Bx - Cx) * (P0z - P2z)) * (-Cy)) + ((((Ay - Cy) * (P1x - P2x)) * Cz) + (((((P0y - P2y) * (Bx - Cx)) * Cz) - (((P0x - P2x) * (By - Cy)) * Cz)) - (((Ax - Cx) * (P1y - P2y)) * Cz)))) - (((By - Cy) * (P0z - P2z)) * (-Cx)))) - (((Az - Cz) * (P1y - P2y)) * (-Cx))) - (((P0x - P2x) * (Bz - Cz)) * (-Cy)))) - (((Ax - Cx) * (P1z - P2z)) * (-Cy)))) - (((((By - Cy) * (Ax - Cx)) - ((Bx - Cx) * (Ay - Cy))) * P2z) + (((((Az - Cz) * (Bx - Cx)) - ((Bz - Cz) * (Ax - Cx))) * P2y) + ((((Bz - Cz) * (Ay - Cy)) - ((Az - Cz) * (By - Cy))) * P2x))));
            float e13 = (((((-Cy) * ((P1x - P2x) * (P0z - P2z))) + ((((P1z - P2z) * (P0y - P2y)) * (-Cx)) + (((Cz * ((P0y - P2y) * (P1x - P2x))) - (((P0x - P2x) * (P1y - P2y)) * Cz)) - (((P0x - P2x) * (P1z - P2z)) * (-Cy))))) - ((-Cx) * ((P1y - P2y) * (P0z - P2z)))) - ((P2z * (((((P0x - P2x) * (By - Cy)) - ((P0y - P2y) * (Bx - Cx))) + ((Ax - Cx) * (P1y - P2y))) - ((Ay - Cy) * (P1x - P2x)))) + ((P2y * ((((-((P0x - P2x) * (Bz - Cz))) - ((Ax - Cx) * (P1z - P2z))) + ((Bx - Cx) * (P0z - P2z))) + ((Az - Cz) * (P1x - P2x)))) + (P2x * (((((Ay - Cy) * (P1z - P2z)) + ((Bz - Cz) * (P0y - P2y))) - ((Az - Cz) * (P1y - P2y))) - ((By - Cy) * (P0z - P2z)))))));
            float e23 = (0f - ((P2z * (((P0x - P2x) * (P1y - P2y)) - ((P0y - P2y) * (P1x - P2x)))) + ((P2y * (((P1x - P2x) * (P0z - P2z)) - ((P0x - P2x) * (P1z - P2z)))) + (P2x * (((P1z - P2z) * (P0y - P2y)) - ((P1y - P2y) * (P0z - P2z)))))));
            float det = (((((Bz - Cz) * (Ay - Cy)) * (-Cx)) + (((((-Cz) * ((By - Cy) * (Ax - Cx))) - ((-Cz) * ((Bx - Cx) * (Ay - Cy)))) + (((Az - Cz) * (Bx - Cx)) * (-Cy))) - (((Az - Cz) * (By - Cy)) * (-Cx)))) - (((Bz - Cz) * (Ax - Cx)) * (-Cy)));
            matrix = new[] { e00, e01, e02, e03, e10, e11, e12, e13, e20, e21, e22, e23 };
            if (MathF.Abs(det) <= 1e-5f) return false;
            for (int k = 0; k < 12; k++) matrix[k] /= det;
            return true;
        }

        /// <summary>
        /// Rows of the inverse of the matrix with columns c0, c1, c2, rounded as the Editor's cells
        /// are: the general 4×4 inverse, in single precision, of that matrix extended by
        /// (0, 0, 0, 1), by Gauss–Jordan elimination with partial pivoting (pivots bubbled up from
        /// the last row on a strictly larger magnitude, the multipliers taken once per column, the
        /// right half's zero entries skipped in the first two eliminations, then back substitution
        /// from the last row). It reproduces bit for bit all 55 cell matrices of the Editor's own
        /// bakes in tests/projects/lightprobes (Main 45, Extra 10, from c_k = p_k − p3 in single
        /// precision); a double-precision inverse rounded to single precision matches 1 of them.
        /// The rounding decides a weight's last bit, so the interpolated probes and occlusion.
        /// </summary>
        internal static void Matrix(Vector3 c0, Vector3 c1, Vector3 c2, out Vector3 r0, out Vector3 r1, out Vector3 r2)
        {
            var w0 = new float[] { c0.x, c1.x, c2.x, 0f, 1f, 0f, 0f, 0f };
            var w1 = new float[] { c0.y, c1.y, c2.y, 0f, 0f, 1f, 0f, 0f };
            var w2 = new float[] { c0.z, c1.z, c2.z, 0f, 0f, 0f, 1f, 0f };
            var w3 = new float[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
            r0 = r1 = r2 = Vector3.zero;

            // First column.
            if (MathF.Abs(w3[0]) > MathF.Abs(w2[0])) (w3, w2) = (w2, w3);
            if (MathF.Abs(w2[0]) > MathF.Abs(w1[0])) (w2, w1) = (w1, w2);
            if (MathF.Abs(w1[0]) > MathF.Abs(w0[0])) (w1, w0) = (w0, w1);
            if (w0[0] == 0f) return;
            float m1 = w1[0] / w0[0], m2 = w2[0] / w0[0], m3 = w3[0] / w0[0];
            for (int j = 1; j < 8; j++)
            {
                float s = w0[j];
                if (j >= 4 && s == 0f) continue;
                w1[j] -= m1 * s; w2[j] -= m2 * s; w3[j] -= m3 * s;
            }

            // Second column.
            if (MathF.Abs(w3[1]) > MathF.Abs(w2[1])) (w3, w2) = (w2, w3);
            if (MathF.Abs(w2[1]) > MathF.Abs(w1[1])) (w2, w1) = (w1, w2);
            if (w1[1] == 0f) return;
            m2 = w2[1] / w1[1]; m3 = w3[1] / w1[1];
            for (int j = 2; j < 8; j++)
            {
                float s = w1[j];
                if (j >= 4 && s == 0f) continue;
                w2[j] -= m2 * s; w3[j] -= m3 * s;
            }

            // Third column.
            if (MathF.Abs(w3[2]) > MathF.Abs(w2[2])) (w3, w2) = (w2, w3);
            if (w2[2] == 0f) return;
            m3 = w3[2] / w2[2];
            for (int j = 3; j < 8; j++) w3[j] -= m3 * w2[j];
            if (w3[3] == 0f) return;

            // Back substitution.
            float inv = 1f / w3[3];
            for (int j = 4; j < 8; j++) w3[j] *= inv;
            float m = w2[3];
            inv = 1f / w2[2];
            for (int j = 4; j < 8; j++) w2[j] = inv * (w2[j] - w3[j] * m);
            m = w1[3];
            for (int j = 4; j < 8; j++) w1[j] -= w3[j] * m;
            m = w0[3];
            for (int j = 4; j < 8; j++) w0[j] -= w3[j] * m;
            m = w1[2];
            inv = 1f / w1[1];
            for (int j = 4; j < 8; j++) w1[j] = inv * (w1[j] - w2[j] * m);
            m = w0[2];
            for (int j = 4; j < 8; j++) w0[j] -= w2[j] * m;
            m = w0[1];
            inv = 1f / w0[0];
            for (int j = 4; j < 8; j++) w0[j] = inv * (w0[j] - w1[j] * m);

            r0 = new Vector3(w0[4], w0[5], w0[6]);
            r1 = new Vector3(w1[4], w1[5], w1[6]);
            r2 = new Vector3(w2[4], w2[5], w2[6]);
        }

        /// <summary>
        /// Signs of the orientation and in-sphere determinants of double-precision points, exact:
        /// the determinant in double precision when its rounding error bound (Shewchuk, "Adaptive
        /// Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997: the
        /// static bounds of orient3d and insphere) cannot change its sign, otherwise in integers
        /// (every double is an integer times a power of two, so the points scaled by the smallest
        /// such power have integer coordinates and the determinants' signs are unchanged).
        /// </summary>
        internal static class Predicates
        {
            const double Epsilon = 1.1102230246251565e-16; // 2^-53
            const double OrientBound = (7.0 + 56.0 * Epsilon) * Epsilon;
            const double InSphereBound = (16.0 + 224.0 * Epsilon) * Epsilon;

            /// <summary>The sign of det(a − d, b − d, c − d).</summary>
            internal static int Orient(double ax, double ay, double az, double bx, double by, double bz,
                double cx, double cy, double cz, double dx, double dy, double dz)
            {
                double adx = ax - dx, ady = ay - dy, adz = az - dz;
                double bdx = bx - dx, bdy = by - dy, bdz = bz - dz;
                double cdx = cx - dx, cdy = cy - dy, cdz = cz - dz;
                double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy, cdxady = cdx * ady, adxcdy = adx * cdy, adxbdy = adx * bdy, bdxady = bdx * ady;
                double det = adz * (bdxcdy - cdxbdy) + bdz * (cdxady - adxcdy) + cdz * (adxbdy - bdxady);
                double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * Math.Abs(adz)
                    + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * Math.Abs(bdz)
                    + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * Math.Abs(cdz);
                if (det > OrientBound * permanent) return 1;
                if (-det > OrientBound * permanent) return -1;
                Span<double> points = stackalloc double[] { ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz };
                Span<long> small = stackalloc long[12];
                if (Longs(points, small, SmallBits)) return OrientSmall(small);
                var v = Integers(points);
                return OrientExact(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11]);
            }

            /// <summary>Positive when e is inside the sphere through a, b, c and d and
            /// <see cref="Orient"/>(a, b, c, d) is positive (the sign reverses with the
            /// orientation), zero when e is on it.</summary>
            internal static int InSphere(double ax, double ay, double az, double bx, double by, double bz,
                double cx, double cy, double cz, double dx, double dy, double dz, double ex, double ey, double ez)
            {
                double aex = ax - ex, aey = ay - ey, aez = az - ez;
                double bex = bx - ex, bey = by - ey, bez = bz - ez;
                double cex = cx - ex, cey = cy - ey, cez = cz - ez;
                double dex = dx - ex, dey = dy - ey, dez = dz - ez;
                double aexbey = aex * bey, bexaey = bex * aey, bexcey = bex * cey, cexbey = cex * bey;
                double cexdey = cex * dey, dexcey = dex * cey, dexaey = dex * aey, aexdey = aex * dey;
                double aexcey = aex * cey, cexaey = cex * aey, bexdey = bex * dey, dexbey = dex * bey;
                double ab = aexbey - bexaey, bc = bexcey - cexbey, cd = cexdey - dexcey, da = dexaey - aexdey, ac = aexcey - cexaey, bd = bexdey - dexbey;
                double abc = aez * bc - bez * ac + cez * ab, bcd = bez * cd - cez * bd + dez * bc;
                double cda = cez * da + dez * ac + aez * cd, dab = dez * ab + aez * bd + bez * da;
                double alift = aex * aex + aey * aey + aez * aez, blift = bex * bex + bey * bey + bez * bez;
                double clift = cex * cex + cey * cey + cez * cez, dlift = dex * dex + dey * dey + dez * dez;
                double det = (dlift * abc - clift * dab) + (blift * cda - alift * bcd);
                double aezp = Math.Abs(aez), bezp = Math.Abs(bez), cezp = Math.Abs(cez), dezp = Math.Abs(dez);
                double abp = Math.Abs(aexbey) + Math.Abs(bexaey), bcp = Math.Abs(bexcey) + Math.Abs(cexbey);
                double cdp = Math.Abs(cexdey) + Math.Abs(dexcey), dap = Math.Abs(dexaey) + Math.Abs(aexdey);
                double acp = Math.Abs(aexcey) + Math.Abs(cexaey), bdp = Math.Abs(bexdey) + Math.Abs(dexbey);
                double permanent = (cdp * bezp + bdp * cezp + bcp * dezp) * alift
                    + (dap * cezp + acp * dezp + cdp * aezp) * blift
                    + (abp * dezp + bdp * aezp + dap * bezp) * clift
                    + (bcp * aezp + acp * bezp + abp * cezp) * dlift;
                if (det > InSphereBound * permanent) return 1;
                if (-det > InSphereBound * permanent) return -1;
                Span<double> points = stackalloc double[] { ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz, ex, ey, ez };
                Span<long> small = stackalloc long[15];
                if (Longs(points, small, SmallBits)) return InSphereSmall(small);
                return InSphereExact(Integers(points), 0, 3, 6, 9, 12);
            }

            /// <summary>Whether p, in the plane of a, b and c, is strictly inside their circumcircle.
            /// With a′ = a − p (and so on) and n = (b − a) × (c − a), that is
            /// (|a′|² b′ × c′ + |b′|² c′ × a′ + |c′|² a′ × b′) · n &gt; 0: the plane's own in-circle
            /// determinant, with a, b, c counterclockwise about n. Exact (only exactly coplanar
            /// probes ask).</summary>
            internal static bool InCircle(double ax, double ay, double az, double bx, double by, double bz,
                double cx, double cy, double cz, double px, double py, double pz)
            {
                Span<double> points = stackalloc double[] { ax, ay, az, bx, by, bz, cx, cy, cz, px, py, pz };
                Span<long> w = stackalloc long[12];
                if (Longs(points, w, SmallInCircleBits))
                {
                    Int128 apx = w[0] - w[9], apy = w[1] - w[10], apz = w[2] - w[11];
                    Int128 bpx = w[3] - w[9], bpy = w[4] - w[10], bpz = w[5] - w[11];
                    Int128 cpx = w[6] - w[9], cpy = w[7] - w[10], cpz = w[8] - w[11];
                    Int128 ux = bpx - apx, uy = bpy - apy, uz = bpz - apz, wx = cpx - apx, wy = cpy - apy, wz = cpz - apz;
                    Int128 nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                    Int128 la = apx * apx + apy * apy + apz * apz, lb = bpx * bpx + bpy * bpy + bpz * bpz, lc = cpx * cpx + cpy * cpy + cpz * cpz;
                    Int128 Dot(Int128 x0, Int128 y0, Int128 z0, Int128 x1, Int128 y1, Int128 z1) =>
                        (y0 * z1 - z0 * y1) * nx + (z0 * x1 - x0 * z1) * ny + (x0 * y1 - y0 * x1) * nz;
                    return la * Dot(bpx, bpy, bpz, cpx, cpy, cpz) + lb * Dot(cpx, cpy, cpz, apx, apy, apz) + lc * Dot(apx, apy, apz, bpx, bpy, bpz) > 0;
                }
                var v = Integers(points);
                BigInteger Apx = v[0] - v[9], Apy = v[1] - v[10], Apz = v[2] - v[11];
                BigInteger Bpx = v[3] - v[9], Bpy = v[4] - v[10], Bpz = v[5] - v[11];
                BigInteger Cpx = v[6] - v[9], Cpy = v[7] - v[10], Cpz = v[8] - v[11];
                BigInteger Ux = Bpx - Apx, Uy = Bpy - Apy, Uz = Bpz - Apz, Wx = Cpx - Apx, Wy = Cpy - Apy, Wz = Cpz - Apz;
                BigInteger Nx = Uy * Wz - Uz * Wy, Ny = Uz * Wx - Ux * Wz, Nz = Ux * Wy - Uy * Wx;
                BigInteger La = Apx * Apx + Apy * Apy + Apz * Apz, Lb = Bpx * Bpx + Bpy * Bpy + Bpz * Bpz, Lc = Cpx * Cpx + Cpy * Cpy + Cpz * Cpz;
                BigInteger BigDot(BigInteger x0, BigInteger y0, BigInteger z0, BigInteger x1, BigInteger y1, BigInteger z1) =>
                    (y0 * z1 - z0 * y1) * Nx + (z0 * x1 - x0 * z1) * Ny + (x0 * y1 - y0 * x1) * Nz;
                return (La * BigDot(Bpx, Bpy, Bpz, Cpx, Cpy, Cpz) + Lb * BigDot(Cpx, Cpy, Cpz, Apx, Apy, Apz) + Lc * BigDot(Apx, Apy, Apz, Bpx, Bpy, Bpz)).Sign > 0;
            }

            /// <summary>Each value as mantissa × 2^exponent, the mantissa odd (or zero); the
            /// smallest exponent of the nonzero values.</summary>
            static int Decompose(ReadOnlySpan<double> values, Span<long> mantissas, Span<int> exponents)
            {
                int smallest = int.MaxValue;
                for (int i = 0; i < values.Length; i++)
                {
                    long bits = BitConverter.DoubleToInt64Bits(values[i]);
                    int exponent = (int)((bits >> 52) & 0x7FF);
                    long mantissa = bits & 0xFFFFFFFFFFFFFL;
                    if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
                    exponent -= 1075;
                    if (mantissa != 0)
                    {
                        int zeros = System.Numerics.BitOperations.TrailingZeroCount(mantissa);
                        mantissa >>= zeros;
                        exponent += zeros;
                        smallest = Math.Min(smallest, exponent);
                    }
                    mantissas[i] = bits < 0 ? -mantissa : mantissa;
                    exponents[i] = exponent;
                }
                return smallest;
            }

            /// <summary>The values as integers of one common power of two, when all of them are
            /// below 2^<paramref name="bits"/>.</summary>
            static bool Longs(ReadOnlySpan<double> values, Span<long> result, int bits)
            {
                Span<int> exponents = stackalloc int[values.Length];
                int smallest = Decompose(values, result, exponents);
                for (int i = 0; i < values.Length; i++)
                {
                    if (result[i] == 0) continue;
                    int shift = exponents[i] - smallest;
                    int length = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)Math.Abs(result[i]));
                    if (length + shift >= bits) return false;
                    result[i] <<= shift;
                }
                return true;
            }

            /// <summary>The values as integers of one common power of two.</summary>
            static BigInteger[] Integers(ReadOnlySpan<double> values)
            {
                Span<long> mantissas = stackalloc long[values.Length];
                Span<int> exponents = stackalloc int[values.Length];
                int smallest = Decompose(values, mantissas, exponents);
                var result = new BigInteger[values.Length];
                for (int i = 0; i < values.Length; i++)
                    result[i] = mantissas[i] == 0 ? BigInteger.Zero : new BigInteger(mantissas[i]) << (exponents[i] - smallest);
                return result;
            }

            /// <summary>Below 2^SmallBits the integers' orientation (degree three) and in-sphere
            /// (degree five) determinants fit in 128 bits.</summary>
            const int SmallBits = 21;

            /// <summary>Below 2^SmallInCircleBits the coplanar in-circle expression (degree six)
            /// fits in 128 bits.</summary>
            const int SmallInCircleBits = 16;

            static int OrientSmall(ReadOnlySpan<long> v)
            {
                Int128 adx = v[0] - v[9], ady = v[1] - v[10], adz = v[2] - v[11];
                Int128 bdx = v[3] - v[9], bdy = v[4] - v[10], bdz = v[5] - v[11];
                Int128 cdx = v[6] - v[9], cdy = v[7] - v[10], cdz = v[8] - v[11];
                return Int128.Sign(adz * (bdx * cdy - cdx * bdy) + bdz * (cdx * ady - adx * cdy) + cdz * (adx * bdy - bdx * ady));
            }

            static int OrientExact(BigInteger ax, BigInteger ay, BigInteger az, BigInteger bx, BigInteger by, BigInteger bz,
                BigInteger cx, BigInteger cy, BigInteger cz, BigInteger dx, BigInteger dy, BigInteger dz)
            {
                BigInteger adx = ax - dx, ady = ay - dy, adz = az - dz;
                BigInteger bdx = bx - dx, bdy = by - dy, bdz = bz - dz;
                BigInteger cdx = cx - dx, cdy = cy - dy, cdz = cz - dz;
                return (adz * (bdx * cdy - cdx * bdy) + bdz * (cdx * ady - adx * cdy) + cdz * (adx * bdy - bdx * ady)).Sign;
            }

            /// <summary><see cref="InSphereExact"/> for five points below 2^SmallBits.</summary>
            static int InSphereSmall(ReadOnlySpan<long> v)
            {
                Int128 aex = v[0] - v[12], aey = v[1] - v[13], aez = v[2] - v[14];
                Int128 bex = v[3] - v[12], bey = v[4] - v[13], bez = v[5] - v[14];
                Int128 cex = v[6] - v[12], cey = v[7] - v[13], cez = v[8] - v[14];
                Int128 dex = v[9] - v[12], dey = v[10] - v[13], dez = v[11] - v[14];
                Int128 ab = aex * bey - bex * aey, bc = bex * cey - cex * bey, cd = cex * dey - dex * cey;
                Int128 da = dex * aey - aex * dey, ac = aex * cey - cex * aey, bd = bex * dey - dex * bey;
                Int128 abc = aez * bc - bez * ac + cez * ab, bcd = bez * cd - cez * bd + dez * bc;
                Int128 cda = cez * da + dez * ac + aez * cd, dab = dez * ab + aez * bd + bez * da;
                Int128 alift = aex * aex + aey * aey + aez * aez, blift = bex * bex + bey * bey + bez * bez;
                Int128 clift = cex * cex + cey * cey + cez * cez, dlift = dex * dex + dey * dey + dez * dez;
                return Int128.Sign((dlift * abc - clift * dab) + (blift * cda - alift * bcd));
            }

            /// <summary>The in-sphere sign of points v[a..a+2] … v[e..e+2], as <see cref="InSphere"/>.</summary>
            static int InSphereExact(BigInteger[] v, int a, int b, int c, int d, int e)
            {
                BigInteger aex = v[a] - v[e], aey = v[a + 1] - v[e + 1], aez = v[a + 2] - v[e + 2];
                BigInteger bex = v[b] - v[e], bey = v[b + 1] - v[e + 1], bez = v[b + 2] - v[e + 2];
                BigInteger cex = v[c] - v[e], cey = v[c + 1] - v[e + 1], cez = v[c + 2] - v[e + 2];
                BigInteger dex = v[d] - v[e], dey = v[d + 1] - v[e + 1], dez = v[d + 2] - v[e + 2];
                BigInteger ab = aex * bey - bex * aey, bc = bex * cey - cex * bey, cd = cex * dey - dex * cey;
                BigInteger da = dex * aey - aex * dey, ac = aex * cey - cex * aey, bd = bex * dey - dex * bey;
                BigInteger abc = aez * bc - bez * ac + cez * ab, bcd = bez * cd - cez * bd + dez * bc;
                BigInteger cda = cez * da + dez * ac + aez * cd, dab = dez * ab + aez * bd + bez * da;
                BigInteger alift = aex * aex + aey * aey + aez * aez, blift = bex * bex + bey * bey + bez * bez;
                BigInteger clift = cex * cex + cey * cey + cez * cez, dlift = dex * dex + dey * dey + dez * dez;
                return ((dlift * abc - clift * dab) + (blift * cda - alift * bcd)).Sign;
            }
        }

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        static (int, int, int) Opposite(int[] cell, int k)
        {
            Span<int> v = stackalloc int[3];
            int m = 0;
            for (int j = 0; j < 4; j++) if (j != k) v[m++] = cell[j];
            return Sorted(v[0], v[1], v[2]);
        }

        static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);

        struct Vector3d
        {
            public double X, Y, Z;
            public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector3d operator -(Vector3d a) => new Vector3d(-a.X, -a.Y, -a.Z);
            public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);
            public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public double Dot(double x, double y, double z) => X * x + Y * y + Z * z;
            public double Length() => Math.Sqrt(Dot(this, this));
            public Vector3d Normalized()
            {
                double l = Math.Sqrt(Dot(this, this));
                return l > 0 ? this * (1 / l) : this;
            }
            public Vector3 ToFloat() => new Vector3((float)X, (float)Y, (float)Z);
        }
    }
}
