using System;
using System.Collections.Generic;
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

        /// <summary>Relative tolerance, in units of the probes' extent, below which a probe counts
        /// as on another's position, line or plane.</summary>
        const double Coplanar = 1e-12;

        /// <summary>A cell while building: a probe tetrahedron with its circumsphere, or a hull
        /// triangle (D = <see cref="Infinite"/>) with its outward normal and its circumcircle.</summary>
        struct Tet
        {
            public int A, B, C, D;
            public double Cx, Cy, Cz, R2;
            public double Nx, Ny, Nz, NLength;
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

        static double Det3(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz) =>
            ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);

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

            var tets = new List<Tet>();
            bool Sphere(ref Tet t)
            {
                double bx = x[t.B] - x[t.A], by = y[t.B] - y[t.A], bz = z[t.B] - z[t.A];
                double cx = x[t.C] - x[t.A], cy = y[t.C] - y[t.A], cz = z[t.C] - z[t.A];
                double dx = x[t.D] - x[t.A], dy = y[t.D] - y[t.A], dz = z[t.D] - z[t.A];
                double det = 2 * Det3(bx, by, bz, cx, cy, cz, dx, dy, dz);
                if (Math.Abs(det) < 1e-18) return false;
                double b2 = bx * bx + by * by + bz * bz, c2 = cx * cx + cy * cy + cz * cz, d2 = dx * dx + dy * dy + dz * dz;
                double ox = Det3(b2, by, bz, c2, cy, cz, d2, dy, dz) / det;
                double oy = Det3(bx, b2, bz, cx, c2, cz, dx, d2, dz) / det;
                double oz = Det3(bx, by, b2, cx, cy, c2, dx, dy, d2) / det;
                t.Cx = x[t.A] + ox; t.Cy = y[t.A] + oy; t.Cz = z[t.A] + oz;
                t.R2 = ox * ox + oy * oy + oz * oz;
                return true;
            }

            // The first tetrahedron: probe 0 and the first probes after it off its point, line and
            // plane. Without four such probes there are no cells.
            int i1 = -1, i2 = -1, i3 = -1;
            double flat = Coplanar * size;
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
            if (i3 < 0) return;
            // A point inside every hull the probes make from here on, which tells the hull
            // triangles' outward side.
            double ix = (x[0] + x[i1] + x[i2] + x[i3]) / 4, iy = (y[0] + y[i1] + y[i2] + y[i3]) / 4, iz = (z[0] + z[i1] + z[i2] + z[i3]) / 4;

            // The unbounded hull: past each hull triangle a cell with the point at infinity, which a
            // probe outside that triangle's plane (or in it, inside the triangle's circumcircle)
            // replaces, so the cells are those of the probes' own Delaunay tetrahedralization
            // however far a cell's circumsphere reaches.
            bool Hull(ref Tet t)
            {
                var normal = Cross(x, y, z, t.A, t.B, t.C);
                double length = normal.Length();
                if (length < 1e-18) return false;
                double side = normal.Dot(ix - x[t.A], iy - y[t.A], iz - z[t.A]);
                if (side > 0) normal = normal.Negated();
                (t.Nx, t.Ny, t.Nz, t.NLength) = (normal.X, normal.Y, normal.Z, length);
                // The triangle's circumcircle: center and squared radius.
                double bx = x[t.B] - x[t.A], by = y[t.B] - y[t.A], bz = z[t.B] - z[t.A];
                double cx = x[t.C] - x[t.A], cy = y[t.C] - y[t.A], cz = z[t.C] - z[t.A];
                double b2 = bx * bx + by * by + bz * bz, c2 = cx * cx + cy * cy + cz * cz;
                var u = new Vector3d(b2 * cx - c2 * bx, b2 * cy - c2 * by, b2 * cz - c2 * bz);
                var o = Vector3d.Cross(u, new Vector3d(normal.X, normal.Y, normal.Z)) * (1 / (2 * length * length));
                if (side > 0) o = -o;
                t.Cx = x[t.A] + o.X; t.Cy = y[t.A] + o.Y; t.Cz = z[t.A] + o.Z;
                t.R2 = Vector3d.Dot(o, o);
                return true;
            }
            bool Conflicts(in Tet t, int i)
            {
                double ex = x[i] - t.Cx, ey = y[i] - t.Cy, ez = z[i] - t.Cz;
                bool inside = ex * ex + ey * ey + ez * ez < t.R2 * (1 - 1e-12);
                if (t.D != Infinite) return inside;
                double above = t.Nx * (x[i] - x[t.A]) + t.Ny * (y[i] - y[t.A]) + t.Nz * (z[i] - z[t.A]);
                if (Math.Abs(above) <= flat * t.NLength) return inside;
                return above > 0;
            }
            var first = new Tet { A = 0, B = i1, C = i2, D = i3 };
            Sphere(ref first);
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
                    if (tet.D == Infinite ? Hull(ref tet) : Sphere(ref tet)) tets.Add(tet);
                }
                if (tets.Count > 4096) tets.RemoveAll(t => t.Dead);
            }

            // The inner cells, oriented as the Editor's.
            var inner = new List<int[]>();
            foreach (var t in tets)
            {
                if (t.Dead || t.A >= n || t.B >= n || t.C >= n || t.D >= n) continue;
                int a = t.A, b = t.B, c = t.C, d = t.D;
                double v = Det3(x[a] - x[d], y[a] - y[d], z[a] - z[d], x[b] - x[d], y[b] - y[d], z[b] - z[d], x[c] - x[d], y[c] - y[d], z[c] - z[d]);
                if (Math.Abs(v) < 1e-12) continue;
                inner.Add(v < 0 ? new[] { a, b, c, d } : new[] { b, a, c, d });
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
            var rays = new Vector3d[n];
            for (int h = 0; h < hull.Count; h++)
            {
                var (tri, cell, apex) = hull[h];
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
                // The outward normal (away from the cell's fourth probe), weighted at each corner
                // by the triangle's angle there.
                var a = new Vector3d(x[tri[0]], y[tri[0]], z[tri[0]]);
                var b = new Vector3d(x[tri[1]], y[tri[1]], z[tri[1]]);
                var c3 = new Vector3d(x[tri[2]], y[tri[2]], z[tri[2]]);
                var normal = Vector3d.Cross(b - a, c3 - a).Normalized();
                if (Vector3d.Dot(normal, new Vector3d(x[apex], y[apex], z[apex]) - a) > 0) normal = -normal;
                var corners = new[] { a, b, c3 };
                for (int k = 0; k < 3; k++)
                {
                    var e1 = (corners[(k + 1) % 3] - corners[k]).Normalized();
                    var e2 = (corners[(k + 2) % 3] - corners[k]).Normalized();
                    double angle = Math.Acos(Math.Clamp(Vector3d.Dot(e1, e2), -1, 1));
                    rays[tri[k]] += normal * angle;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var r = rays[i].Normalized();
                set.HullRays[i] = new Vector3((float)r.X, (float)r.Y, (float)r.Z);
            }
            set.Cells = cells.ToArray();
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
            public Vector3d Negated() => -this;
            public Vector3d Normalized()
            {
                double l = Math.Sqrt(Dot(this, this));
                return l > 0 ? this * (1 / l) : this;
            }
            public Vector3 ToFloat() => new Vector3((float)X, (float)Y, (float)Z);
        }
    }
}
