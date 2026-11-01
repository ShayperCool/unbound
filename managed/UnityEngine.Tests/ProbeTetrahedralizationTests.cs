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

        /// <summary>Main's probes of the same project, then Extra's: the set the Editor
        /// tetrahedralizes when both scenes are loaded (its positions record).</summary>
        static readonly Vector3[] MainPositions =
        {
            new Vector3(-1.87f, 0.229999989f, -1.88999999f), new Vector3(-2.16999984f, 0.349999994f, 0.189999998f),
            new Vector3(-1.96999991f, 0.179999992f, 1.90999997f), new Vector3(-1.83999991f, 2.25999999f, -1.91999996f),
            new Vector3(-2.14999986f, 2.40999985f, 0.0199999996f), new Vector3(-1.92999995f, 2.1099999f, 1.93999994f),
            new Vector3(0.170000002f, 0.310000002f, -2.0999999f), new Vector3(-0.0199999996f, 0.439999998f, -0.129999995f),
            new Vector3(0.0899999961f, 0.140000001f, 2.05999994f), new Vector3(0.179999992f, 2.22000003f, -2.02999997f),
            new Vector3(0.0399999991f, 2.45000005f, -0.189999998f), new Vector3(-0.00999999978f, 2.38999987f, 1.86000001f),
            new Vector3(2.1099999f, 0.329999983f, -2.04999995f), new Vector3(1.89999998f, 0.459999979f, 0.0700000003f),
            new Vector3(1.88f, 0.48999998f, 2.04999995f), new Vector3(2.01999998f, 2.11999989f, -1.88999999f),
            new Vector3(1.93999994f, 2.41999984f, -0.149999991f), new Vector3(2.07999992f, 2.30999994f, 1.90999997f),
        };

        [Fact]
        public void CellsCoverTheConvexHull()
        {
            // The Editor counts 121 cells for these 26 probes. The flat cell of probes 10, 11, 24
            // and 25 lies on the hull; without it two hull triangles meet at a reflex edge.
            var positions = MainPositions.Concat(Positions).ToArray();
            var set = new ProbeSet { Positions = positions };
            ProbeTetrahedralization.Build(set);
            Assert.Equal(121, set.Cells.Length);
            Assert.Contains(set.Cells, c => c.I3 >= 0 && Key(new[] { c.I0, c.I1, c.I2, c.I3 }) == "10,11,24,25");
            for (int i = 0; i < set.Cells.Length; i++)
            {
                var c = set.Cells[i];
                if (c.I3 >= 0) continue;
                Vector3 a = positions[c.I0], b = positions[c.I1], e = positions[c.I2];
                var inside = set.Cells[c.N3];
                int apex = new[] { inside.I0, inside.I1, inside.I2, inside.I3 }.First(v => v != c.I0 && v != c.I1 && v != c.I2);
                var normal = Vector3.Cross(b - a, e - a).normalized;
                if (Vector3.Dot(normal, positions[apex] - a) > 0f) normal = -normal;
                foreach (var p in positions)
                    Assert.True(Vector3.Dot(normal, p - a) < 1e-5f, $"hull triangle {c.I0}, {c.I1}, {c.I2}: a probe outside it");
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
