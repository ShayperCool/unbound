using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    public partial class LightProbesTests
    {
        [Fact]
        public void InvalidBoundsDoNotHideFiniteNearestProbeLighting()
        {
            var set = new ProbeSet
            {
                Positions = new[] { Vector3.zero, Vector3.right, new Vector3(float.NaN, 0, 0) },
                Probes = new SphericalHarmonicsL2[3],
            };
            set.Probes[0][0, 0] = 2f;
            set.Probes[1][0, 0] = 7f;
            ProbeTetrahedralization.Build(set);
            int hint = -1;
            Assert.True(set.Evaluate(Vector3.right, ref hint, out var sh, out _));
            Assert.Equal(7f, sh[0, 0]);
        }

        [Fact]
        public void AnOpenCellInsideTheBoundsSuppliesLighting()
        {
            var positions = new[] { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 2) };
            var probes = Baked(positions, p => p.x + 2f * p.y + 4f * p.z);
            ProbeTetrahedralization.Build(probes.m_Self);
            var set = probes.m_Self.Snapshot();
            int hint = -1;
            Assert.True(set.Evaluate(new Vector3(1, 1, 1), ref hint, out var sh, out _));
            Assert.True(set.IsOuterCell(hint));
            Assert.InRange(MathF.Abs(sh[0, 0] - 14f / 3f), 0f, 0.000001f);
            Assert.False(set.Evaluate(new Vector3(2.01f, 1, 1), ref hint, out _, out var occ));
            Assert.Equal(-1, hint);
            Assert.Equal(Vector4.one, occ);
        }

        [Fact]
        public void LookupKeepsAnExactFaceHintAndCrossesImmediatelyBeyondIt()
        {
            var set = new ProbeSet
            {
                Positions = new[] { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 2), new Vector3(0, 0, -2) },
                Probes = new SphericalHarmonicsL2[5],
                Cells = new[]
                {
                    new ProbeSet.Cell { I0 = 0, I1 = 1, I2 = 2, I3 = 3, N0 = -1, N1 = -1, N2 = -1, N3 = 1,
                        R0 = new Vector3(-0.5f, -0.5f, -0.5f), R1 = new Vector3(0.5f, 0, 0), R2 = new Vector3(0, 0.5f, 0) },
                    new ProbeSet.Cell { I0 = 0, I1 = 1, I2 = 2, I3 = 4, N0 = -1, N1 = -1, N2 = -1, N3 = 0,
                        R0 = new Vector3(-0.5f, -0.5f, 0.5f), R1 = new Vector3(0.5f, 0, 0), R2 = new Vector3(0, 0.5f, 0) },
                },
            };
            for (int i = 0; i < set.Probes.Length; i++) set.Probes[i][0, 0] = set.Positions[i].z;
            for (int start = 0; start < 2; start++)
            {
                int hint = start;
                Assert.True(set.Evaluate(new Vector3(0.5f, 0.5f, 0), ref hint, out _, out _));
                Assert.Equal(start, hint);
                Assert.True(set.Evaluate(new Vector3(0.5f, 0.5f, -0.000001f), ref hint, out var sh, out _));
                Assert.Equal(1, hint);
                Assert.InRange(MathF.Abs(sh[0, 0] + 0.000001f), 0f, 0.0000001f);
                Assert.True(set.Evaluate(new Vector3(0.5f, 0.5f, 0.000001f), ref hint, out _, out _));
                Assert.Equal(0, hint);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void QueryBoundsRetainCenterAndExtentRounding(int axis)
        {
            var positions = new[] { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 2) };
            for (int i = 0; i < positions.Length; i++)
                positions[i][axis] = positions[i][axis] == 0f ? 0.14f : 2.45f;
            var probes = Baked(positions, _ => 8f);
            ProbeTetrahedralization.Build(probes.m_Self);
            foreach (var set in new[] { probes.m_Self, probes.m_Self.Snapshot(), ProbeTetrahedralization.Combine(new List<ProbeSet> { probes.m_Self }) })
            {
                int hint = 0;
                // The raw minimum is inside the point extrema but outside the rounded bounds.
                Assert.False(set.Evaluate(positions[0], ref hint, out _, out var occ));
                Assert.Equal(-1, hint);
                Assert.Equal(Vector4.one, occ);
                var inside = positions[0];
                inside[axis] += 0.000001f;
                Assert.True(set.Evaluate(inside, ref hint, out var sh, out _));
                Assert.InRange(MathF.Abs(sh[0, 0] - 8f), 0f, 0.000001f);
                var outside = positions[0];
                outside[axis] -= 0.000001f;
                Assert.False(set.Evaluate(outside, ref hint, out _, out _));
            }
        }

        [Fact]
        public void RendererQueryRemembersTheCellAfterMovingBetweenCells()
        {
            var saved = LightmapSettings.lightProbes;
            var renderer = new GameObject("Probe hint", typeof(MeshRenderer)).GetComponent<MeshRenderer>();
            try
            {
                var probes = Baked(Box, p => p.x + 2f * p.y);
                ProbeTetrahedralization.Build(probes.m_Self);
                LightmapSettings.lightProbes = probes;
                var first = new Vector3(4.3f, 1f, 0.1f);
                var second = new Vector3(5.7f, 1.7f, -0.3f);
                LightProbes.GetInterpolatedProbe(first, renderer, out var a);
                int firstCell = renderer.m_ProbeCell;
                Assert.InRange(firstCell, 0, probes.cellCount - 1);
                LightProbes.GetInterpolatedProbe(second, renderer, out var b);
                Assert.InRange(renderer.m_ProbeCell, 0, probes.cellCount - 1);
                Assert.NotEqual(firstCell, renderer.m_ProbeCell);
                Assert.NotEqual(a[0, 0], b[0, 0]);
                LightProbes.GetInterpolatedProbe(second, null, out var plain);
                Assert.InRange(MathF.Abs(plain[0, 0] - b[0, 0]), 0f, 0.00001f);
            }
            finally
            {
                LightmapSettings.lightProbes = saved;
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            }
        }
    }
}
