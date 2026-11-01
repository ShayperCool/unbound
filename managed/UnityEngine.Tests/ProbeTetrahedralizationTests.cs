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

        static string Key(IEnumerable<int> cell) => string.Join(",", cell.OrderBy(i => i));

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
