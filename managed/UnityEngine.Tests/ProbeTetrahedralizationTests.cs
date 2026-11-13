using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The tetrahedralization of light probes against the Editor's own bake of
    /// tests/projects/lightprobes' Extra scene (its LightingData: 8 probes, 10 inner and 12 outer
    /// cells, the hull rays).</summary>
    public class ProbeTetrahedralizationTests
    {
        static readonly Vector3[] Positions =
        {
            new Vector3(4.17f, 0.28f, -0.85999995f), new Vector3(3.82f, 0.35999998f, 1.1f), new Vector3(3.87f, 2.33f, -0.82f),
            new Vector3(3.9299998f, 2.3799999f, 0.89f), new Vector3(6.15f, 0.26f, -1.16f), new Vector3(6.12f, 0.32999998f, 0.91999996f),
            new Vector3(6.0699997f, 2.46f, -1.01f), new Vector3(5.8599997f, 2.3999999f, 1.04f),
        };

        static readonly int[][] BakedCells =
        {
            new[] { 0, 2, 7, 6 }, new[] { 7, 2, 3, 6 }, new[] { 3, 0, 1, 2 }, new[] { 0, 3, 7, 2 }, new[] { 0, 3, 1, 5 },
            new[] { 1, 3, 7, 5 }, new[] { 7, 5, 0, 6 }, new[] { 0, 5, 4, 6 }, new[] { 4, 2, 0, 6 }, new[] { 7, 3, 0, 5 },
        };

        static readonly Vector3[] BakedRays =
        {
            new Vector3(-0.52006894f, -0.636148f, -0.56995094f), new Vector3(-0.60378116f, -0.5432318f, 0.58339304f),
            new Vector3(-0.637643f, 0.5407634f, -0.54862213f), new Vector3(-0.58069295f, 0.6192946f, 0.5284599f),
            new Vector3(0.5510188f, -0.56065214f, -0.6180999f), new Vector3(0.61473197f, -0.5625707f, 0.55282813f),
            new Vector3(0.5489091f, 0.621158f, -0.5593402f), new Vector3(0.4970735f, 0.6014077f, 0.6254811f),
        };

        /// <summary>The Editor's matrices of <see cref="BakedCells"/> (rows R0, R1, R2), as stored.</summary>
        static readonly float[][] BakedMatrices =
        {
            new[] { 0.0275950357f, -0.483545572f, -0.0113257878f, -0.482453048f, 0.417927593f, -0.0371899717f, 0.042696014f, -0.00335335685f, 0.49208048f },
            new[] { 0.598601282f, -9.74443626f, 0.263921142f, 0.129120052f, -10.2986183f, -0.288196981f, -0.658771276f, 11.5435953f, 0.27037847f },
            new[] { 2.75841594f, 0.394373775f, 0.476477474f, 2.38046288f, -0.13789317f, -0.0794929713f, -2.40712094f, -0.35411188f, 0.0948144421f },
            new[] { 0.00395475468f, -0.487501562f, 0.0141156539f, -0.564360082f, -0.0944402292f, 0.607358813f, 0.518932283f, 0.0763401985f, -0.0204403009f },
            new[] { -0.0410078987f, -0.051351361f, -0.515430927f, 0.00492227497f, 0.492781848f, -0.0192345623f, -0.404701918f, -0.425677031f, 0.455310404f },
            new[] { -0.216885313f, -0.190487444f, 2.81599021f, -0.259755701f, 0.161344081f, -3.34599018f, 0.260389239f, 0.326067269f, 3.27285028f },
            new[] { -0.42650187f, 0.403091788f, 0.455912292f, 0.489261121f, -0.423825055f, 0.0377147645f, -0.466300875f, -0.0557056069f, -0.0493978299f },
            new[] { -0.506246626f, -0.0179524086f, -0.00669748383f, 0.0728516504f, -0.0302716773f, 0.48283875f, 0.43111074f, -0.407447755f, -0.460839093f },
            new[] { -0.67751056f, 0.05321192f, -7.80844927f, -1.12775397f, 0.46860975f, -7.47441626f, 0.750977635f, -0.540360332f, 8.32580948f },
            new[] { 0.561079502f, 0.590159416f, -0.631243169f, -0.565398455f, -0.107097767f, 0.62240541f, 0.0473547615f, 0.0415910445f, -0.614843428f },
        };

        static string Key(IEnumerable<int> cell) => string.Join(",", cell.OrderBy(i => i));

        [Fact]
        public void AFaceRoundingCycleKeepsWeightsWithTheirOwnVertices()
        {
            var vertices = BakedCells[1];
            var matrix = BakedMatrices[1];
            var set = new ProbeSet
            {
                Positions = Positions,
                Probes = new UnityEngine.Rendering.SphericalHarmonicsL2[Positions.Length],
                HullRays = BakedRays,
                Cells = new[]
                {
                    new ProbeSet.Cell { I0 = vertices[0], I1 = vertices[1], I2 = vertices[2], I3 = vertices[3],
                        N0 = 1, N1 = -1, N2 = -1, N3 = -1,
                        R0 = new Vector3(matrix[0], matrix[1], matrix[2]),
                        R1 = new Vector3(matrix[3], matrix[4], matrix[5]), R2 = new Vector3(matrix[6], matrix[7], matrix[8]) },
                    new ProbeSet.Cell { I0 = vertices[1], I1 = vertices[2], I2 = vertices[3], I3 = -1,
                        N0 = -1, N1 = -1, N2 = -1, N3 = 0 },
                },
            };
            // The canonical Extra mesh has 22 cells. Only this adjacent pair is reachable
            // here; retain the full walk limit when isolating its face-rounding cycle.
            const int BakedCellCount = 22;
            Array.Resize(ref set.Cells, BakedCellCount);
            for (int i = 0; i < Positions.Length; i++) set.Probes[i][0, 0] = Positions[i].x + 2f * Positions[i].y - Positions[i].z;
            var point = new Vector3(5.498985290527344f, 2.438500165939331f, -0.5086919069290161f);
            for (int start = 0; start < 2; start++)
            {
                int hint = start;
                Assert.True(set.Evaluate(point, ref hint, out var sh, out _));
                Assert.Equal(start, hint);
                float expected = start == 0 ? point.x + 2f * point.y - point.z : 0f;
                Assert.InRange(MathF.Abs(sh[0, 0] - expected), 0f, 0.000002f);
            }
        }

        [Fact]
        public void DuplicateAndCoplanarPositionsDoNotRejectTheSet()
        {
            var duplicate = new ProbeSet { Positions = new[] { Vector3.zero, new Vector3(2, 0, 0),
                new Vector3(0, 2, 0), new Vector3(0, 0, 2), Vector3.zero } };
            ProbeTetrahedralization.Build(duplicate);
            Assert.Single(duplicate.Cells.Where(c => c.I3 >= 0));
            var planar = new ProbeSet { Positions = new[] { Vector3.zero, new Vector3(2, 0, 0),
                new Vector3(0, 0, 2), new Vector3(2, 0, 2), new Vector3(1, 0, 1) } };
            ProbeTetrahedralization.Build(planar);
            Assert.Empty(planar.Cells);
        }

        /// <summary>Every tetrahedron of the positions whose circumsphere has no other position
        /// strictly inside (a brute-force Delaunay tetrahedralization), with the closest call
        /// among the rejected and accepted ones so a test can require a layout without ties.</summary>
        static (HashSet<string> Cells, double ClosestTie) EmptySpheres(Vector3[] p)
        {
            var cells = new HashSet<string>();
            double closest = double.MaxValue;
            int n = p.Length;
            var bounds = new Bounds(p[0], Vector3.zero);
            foreach (var q in p) bounds.Encapsulate(q);
            double extent = Math.Max(bounds.size.x, Math.Max(bounds.size.y, bounds.size.z));
            for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
            for (int c = b + 1; c < n; c++)
            for (int d = c + 1; d < n; d++)
            {
                double[] P(int i) => new double[] { p[i].x, p[i].y, p[i].z };
                double[] pa = P(a), pb = P(b), pc = P(c), pd = P(d);
                double[] u = { pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2] };
                double[] v = { pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2] };
                double[] w = { pd[0] - pa[0], pd[1] - pa[1], pd[2] - pa[2] };
                double det = u[0] * (v[1] * w[2] - v[2] * w[1]) - u[1] * (v[0] * w[2] - v[2] * w[0]) + u[2] * (v[0] * w[1] - v[1] * w[0]);
                if (Math.Abs(det) < 1e-14) continue;
                double u2 = u[0] * u[0] + u[1] * u[1] + u[2] * u[2], v2 = v[0] * v[0] + v[1] * v[1] + v[2] * v[2], w2 = w[0] * w[0] + w[1] * w[1] + w[2] * w[2];
                double D(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
                    a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
                double ox = D(u2, u[1], u[2], v2, v[1], v[2], w2, w[1], w[2]) / (2 * det);
                double oy = D(u[0], u2, u[2], v[0], v2, v[2], w[0], w2, w[2]) / (2 * det);
                double oz = D(u[0], u[1], u2, v[0], v[1], v2, w[0], w[1], w2) / (2 * det);
                double r2 = ox * ox + oy * oy + oz * oz;
                bool empty = true;
                for (int e = 0; e < n; e++)
                {
                    if (e == a || e == b || e == c || e == d) continue;
                    double ex = p[e].x - pa[0] - ox, ey = p[e].y - pa[1] - oy, ez = p[e].z - pa[2] - oz;
                    // The distance from the sphere, relative to the layout's extent.
                    double relative = (ex * ex + ey * ey + ez * ez - r2) / (2 * Math.Sqrt(r2) * extent);
                    closest = Math.Min(closest, Math.Abs(relative));
                    if (relative < 0) empty = false;
                }
                if (empty) cells.Add(Key(new[] { a, b, c, d }));
            }
            return (cells, closest);
        }

        static HashSet<string> InnerCells(ProbeSet set) =>
            new HashSet<string>(set.Cells.Where(c => c.I3 >= 0).Select(c => Key(new[] { c.I0, c.I1, c.I2, c.I3 })));

        [Fact]
        public void CellsWithFarCircumspheresAreKept()
        {
            // A position just above the base triangle makes a flat cell whose circumsphere
            // (radius about 2100) reaches far beyond the probes: it is still a Delaunay cell, under
            // the three cells to the apex, and its face to the base is on the hull.
            var positions = new[] { Vector3.zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(0.3f, 0.3f, 1e-4f), new Vector3(0.3f, 0.3f, 1) };
            var set = new ProbeSet { Positions = positions };
            ProbeTetrahedralization.Build(set);
            var expected = EmptySpheres(positions);
            Assert.Contains(Key(new[] { 0, 1, 2, 3 }), expected.Cells);
            Assert.Equal(4, expected.Cells.Count);
            Assert.Equal(expected.Cells, InnerCells(set));
            // Its base face is a hull triangle, an outer cell.
            Assert.Contains(set.Cells, c => c.I3 < 0 && Key(new[] { c.I0, c.I1, c.I2 }) == Key(new[] { 0, 1, 2 }));
        }

        [Fact]
        public void CellsAreTheDelaunayTetrahedralization()
        {
            // Pseudo-random layouts: a cube of probes, and a thin slab whose hull cells are flat.
            for (int layout = 0; layout < 2; layout++)
            {
                uint s = 12345u + (uint)layout;
                float Next() { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216f; }
                var positions = new Vector3[18];
                for (int i = 0; i < positions.Length; i++)
                    positions[i] = new Vector3(Next() * 10, Next() * 10, layout == 0 ? Next() * 10 : Next() * 0.5f);
                var expected = EmptySpheres(positions);
                Assert.True(expected.ClosestTie > 1e-6, $"layout {layout} has a near tie {expected.ClosestTie}");
                var set = new ProbeSet { Positions = positions };
                ProbeTetrahedralization.Build(set);
                Assert.Equal(expected.Cells, InnerCells(set));
            }
        }

        [Theory]
        [InlineData(3, 3, 3, 1f)]
        [InlineData(4, 3, 2, 1f)]
        [InlineData(5, 2, 4, 0.5f)]
        [InlineData(6, 6, 1, 1f)]
        public void GridsAreFilledByCellsOnEveryProbe(int nx, int ny, int nz, float spacing) =>
            AssertGridTiled(nx, ny, nz, spacing, Quaternion.identity, Vector3.zero);

        [Theory]
        [InlineData(3, 3, 3, 0.1f, 0f, 0f, 0f)]
        [InlineData(4, 3, 3, 0.1f, 0f, 0f, 0f)]
        [InlineData(4, 3, 3, 0f, 0.05f, 0f, 0f)]
        [InlineData(2, 2, 3, 0.1f, 0f, 0f, 0f)]
        [InlineData(3, 3, 3, 0.1f, 0.2f, 0f, 100f)]
        [InlineData(5, 5, 1, 0.1f, 0.3f, 0f, 0f)]
        [InlineData(5, 5, 1, 0.1f, 0.3f, 0f, 100f)]
        [InlineData(4, 3, 3, 0.2f, 0.4f, 0f, 0f)]
        [InlineData(6, 6, 3, 0f, 0.5235988f, 0f, 0f)]
        [InlineData(5, 4, 3, 0.3f, 0.7f, 1.1f, 37f)]
        public void RotatedAndMovedGridsAreFilledByCellsOnEveryProbe(int nx, int ny, int nz, float ax, float ay, float az, float offset)
        {
            // Rotated (radians about x, y, z) or moved far from the origin, a grid's positions are
            // only within float rounding of its planes: that must make the same tiling, and a single
            // layer still makes no cells.
            var rotation = Quaternion.Euler(ax * Mathf.Rad2Deg, ay * Mathf.Rad2Deg, az * Mathf.Rad2Deg);
            AssertGridTiled(nx, ny, nz, 1f, rotation, new Vector3(offset, 2f * offset / 100f, offset / 2f));
        }

        [Fact]
        public void ALargeRotatedLayerFarAwayMakesNoCells()
        {
            // 50 x 50 probes 1 apart, rotated and moved to (1000, 20, 500): within float rounding of
            // one plane, though probes 0, 1 and 50 alone span a plane tilted by their own rounding.
            var rotation = Quaternion.Euler(0.1f * Mathf.Rad2Deg, 0.3f * Mathf.Rad2Deg, 0f);
            var positions = new List<Vector3>();
            for (int j = 0; j < 50; j++)
                for (int i = 0; i < 50; i++)
                    positions.Add(rotation * new Vector3(i, j, 0) + new Vector3(1000, 20, 500));
            var set = new ProbeSet { Positions = positions.ToArray() };
            ProbeTetrahedralization.Build(set);
            Assert.Empty(set.Cells);
        }

        [Theory]
        [InlineData(1.5e-5f, false)]
        [InlineData(3e-5f, true)]
        public void ProbesFlatByEitherPlaneMakeNoCells(float lift, bool cells)
        {
            // Within 2e-5 (1e-6 of the extent 20) of the plane z = 0 of probes 0, 1 and 2, though
            // the plane through probe 0 and the probes farthest from it and from their line leaves
            // one far probe 2 * lift from it; lifted twice as far, the probes make cells.
            var positions = new[] { Vector3.zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(10, 10, lift), new Vector3(10, -10, -lift), new Vector3(-10, 10, -lift) };
            var set = new ProbeSet { Positions = positions };
            ProbeTetrahedralization.Build(set);
            Assert.Equal(cells, set.Cells.Length > 0);
        }

        static void AssertGridTiled(int nx, int ny, int nz, float spacing, Quaternion rotation, Vector3 offset)
        {
            // A regular grid, as light probe groups usually are: every cube's corners are
            // cospherical and its faces coplanar, so the cells are one of many Delaunay choices,
            // but they must tile the grid's box without gaps or overlaps and use every probe.
            var positions = new List<Vector3>();
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                positions.Add(rotation * new Vector3(i * spacing + 0.25f, j * spacing - 1f, k * spacing * 1.5f) + offset);
            var set = new ProbeSet { Positions = positions.ToArray() };
            ProbeTetrahedralization.Build(set);
            var inner = set.Cells.Where(c => c.I3 >= 0).ToArray();
            if (nz == 1)
            {
                Assert.Empty(set.Cells);
                return;
            }
            double volume = 0;
            foreach (var c in inner)
            {
                var p3 = positions[c.I3];
                volume += Math.Abs(Vector3.Dot(Vector3.Cross(positions[c.I0] - p3, positions[c.I1] - p3), positions[c.I2] - p3)) / 6.0;
            }
            double box = (nx - 1) * spacing * (ny - 1) * spacing * (nz - 1) * spacing * 1.5;
            Assert.True(Math.Abs(volume - box) < 1e-4 * box, $"cells cover {volume}, the box {box}");
            var used = new HashSet<int>(inner.SelectMany(c => new[] { c.I0, c.I1, c.I2, c.I3 }));
            Assert.Equal(positions.Count, used.Count);
            // Every face is shared by two inner cells or one inner and one outer cell.
            var faces = new Dictionary<string, int>();
            foreach (var c in set.Cells)
            {
                var v = c.I3 >= 0 ? new[] { c.I0, c.I1, c.I2, c.I3 } : new[] { c.I0, c.I1, c.I2 };
                if (c.I3 < 0) { var key = Key(v); faces[key] = faces.GetValueOrDefault(key) + 1; continue; }
                for (int k = 0; k < 4; k++)
                {
                    var key = Key(v.Where((_, j) => j != k));
                    faces[key] = faces.GetValueOrDefault(key) + 1;
                }
            }
            Assert.All(faces, f => Assert.Equal(2, f.Value));
        }

        [Fact]
        public void TheFirstCellSkipsDuplicateCollinearAndCoplanarProbes()
        {
            // Probe 1 repeats probe 0, probe 2 is on their line... the first cell is 0, 2, 4, 5;
            // the repeated probe takes no cell and the others are all the Delaunay cells.
            var positions = new[] { Vector3.zero, Vector3.zero, new Vector3(1, 0, 0), new Vector3(2, 0, 0),
                new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Vector3(1.5f, 1.2f, 0.9f) };
            var set = new ProbeSet { Positions = positions };
            ProbeTetrahedralization.Build(set);
            var unique = positions.Select((p, i) => (p, i)).Where(t => t.i != 1).ToArray();
            var expected = EmptySpheres(unique.Select(t => t.p).ToArray()).Cells
                .Select(k => Key(k.Split(',').Select(int.Parse).Select(j => unique[j].i))).ToHashSet();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, InnerCells(set));
            Assert.DoesNotContain(set.Cells, c => c.I0 == 1 || c.I1 == 1 || c.I2 == 1 || c.I3 == 1);
        }

        [Fact]
        public void PredicatesAreExactAtTies()
        {
            // The unit corner tetrahedron: det(a − d, b − d, c − d) of (x, y, z, 0) is +1.
            Assert.Equal(1, ProbeTetrahedralization.Predicates.Orient(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));
            Assert.Equal(-1, ProbeTetrahedralization.Predicates.Orient(0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0));
            // Its circumsphere passes through (1, 1, 1): on it, inside at the center, outside beyond.
            int InSphere(double x, double y, double z) =>
                ProbeTetrahedralization.Predicates.InSphere(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, x, y, z);
            Assert.Equal(0, InSphere(1, 1, 1));
            Assert.Equal(1, InSphere(0.5, 0.5, 0.5));
            Assert.Equal(-1, InSphere(1.5, 1.5, 1.5));
            // Far from the origin the differences round in double; the decision stays exact. The
            // corners of a cube 2^-20 wide at 1e9 are cospherical and its faces coplanar.
            const double o = 1e9, h = 1.0 / (1 << 20);
            Assert.Equal(0, ProbeTetrahedralization.Predicates.Orient(o, o, o, o + h, o, o, o, o + h, o, o + h, o + h, o));
            Assert.Equal(0, ProbeTetrahedralization.Predicates.InSphere(o, o, o, o + h, o, o, o, o + h, o, o, o, o + h, o + h, o + h, o + h));
            Assert.Equal(1, ProbeTetrahedralization.Predicates.InSphere(o + h, o, o, o, o + h, o, o, o, o + h, o, o, o, o + h / 2, o + h / 2, o + h / 2));
            // In a triangle's plane: inside, on and outside its circumcircle.
            bool InCircle(double x, double y) => ProbeTetrahedralization.Predicates.InCircle(0, 0, 0, 2, 0, 0, 0, 2, 0, x, y, 0);
            Assert.True(InCircle(1, 1));
            Assert.False(InCircle(2, 2));
            Assert.False(InCircle(3, 3));
        }

        [Fact]
        public void CellMatricesAreRoundedAsTheEditors()
        {
            // Each baked cell's matrix from its own vertex order, to the bit (a double-precision
            // inverse rounded to single precision misses all ten).
            for (int i = 0; i < BakedCells.Length; i++)
            {
                var v = BakedCells[i];
                var p3 = Positions[v[3]];
                ProbeTetrahedralization.Matrix(Positions[v[0]] - p3, Positions[v[1]] - p3, Positions[v[2]] - p3, out var r0, out var r1, out var r2);
                var actual = new[] { r0.x, r0.y, r0.z, r1.x, r1.y, r1.z, r2.x, r2.y, r2.z };
                for (int k = 0; k < 9; k++)
                    Assert.True(BitConverter.SingleToInt32Bits(BakedMatrices[i][k]) == BitConverter.SingleToInt32Bits(actual[k]),
                        $"cell {i} element {k}: {actual[k]:R} vs the Editor's {BakedMatrices[i][k]:R}");
            }
        }

        [Fact]
        public void CellsAndHullRaysAreTheEditors()
        {
            var set = new ProbeSet { Positions = Positions };
            ProbeTetrahedralization.Build(set);
            var inner = set.Cells.Where(c => c.I3 >= 0).ToArray();
            var outer = set.Cells.Where(c => c.I3 < 0).ToArray();
            Assert.Equal(10, inner.Length);
            Assert.Equal(12, outer.Length);
            Assert.Equal(BakedCells.Select(Key).OrderBy(k => k), inner.Select(c => Key(new[] { c.I0, c.I1, c.I2, c.I3 })).OrderBy(k => k));
            for (int i = 0; i < Positions.Length; i++)
                Assert.True((set.HullRays[i] - BakedRays[i]).magnitude < 1e-6f, $"ray {i}: {set.HullRays[i]} vs {BakedRays[i]}");
            foreach (var c in inner)
            {
                var p3 = Positions[c.I3];
                // The Editor's orientation, and the matrix taking a point to its weights.
                Assert.True(Vector3.Dot(Vector3.Cross(Positions[c.I0] - p3, Positions[c.I1] - p3), Positions[c.I2] - p3) < 0f);
                Assert.True(Mathf.Abs(Vector3.Dot(c.R0, Positions[c.I0] - p3) - 1f) < 1e-5f);
                Assert.True(Mathf.Abs(Vector3.Dot(c.R1, Positions[c.I0] - p3)) < 1e-5f);
            }
            // Neighbor k is across the face opposite vertex k, an outer cell on the hull.
            for (int i = 0; i < set.Cells.Length; i++)
            {
                var c = set.Cells[i];
                if (c.I3 < 0) continue;
                var v = new[] { c.I0, c.I1, c.I2, c.I3 };
                var n = new[] { c.N0, c.N1, c.N2, c.N3 };
                for (int k = 0; k < 4; k++)
                {
                    var o = set.Cells[n[k]];
                    var face = v.Where((_, j) => j != k);
                    Assert.True(face.All(x => x == o.I0 || x == o.I1 || x == o.I2 || x == o.I3), $"cell {i} neighbor {k}");
                }
            }
        }

        [Fact]
        public void InterpolationInsideIsLinear()
        {
            var set = new ProbeSet { Positions = Positions, Probes = new UnityEngine.Rendering.SphericalHarmonicsL2[Positions.Length], Occlusion = new Vector4[Positions.Length] };
            for (int i = 0; i < Positions.Length; i++)
            {
                set.Probes[i][0, 0] = Positions[i].x + 2f * Positions[i].y - Positions[i].z;
                set.Occlusion[i] = new Vector4(Positions[i].y / 4f, 1f, 1f, 1f);
            }
            ProbeTetrahedralization.Build(set);
            int hint = -1;
            var p = new Vector3(5f, 1.2f, 0.1f);
            Assert.True(set.Evaluate(p, ref hint, out var sh, out var occlusion));
            Assert.True(hint >= 0);
            Assert.Equal(p.x + 2f * p.y - p.z, sh[0, 0], 4);
            Assert.Equal(p.y / 4f, occlusion.x, 5);
            hint = 3;
            Assert.False(set.Evaluate(new Vector3(9f, 1f, 0f), ref hint, out _, out occlusion));
            Assert.Equal(-1, hint);
            Assert.Equal(Vector4.one, occlusion);
        }
    }
}
