using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public partial class LightProbesTests
    {
        static ProbeSet SparseOcclusion()
        {
            var positions = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            var json = JsonSerializer.Serialize(new
            {
                positions = positions.SelectMany(p => new[] { p.x, p.y, p.z }),
                coefficients = new float[108],
                hullRays = new float[0],
                tetrahedra = new object[0],
                occlusion = new[]
                {
                    new { lights = new[] { 0, -1, -1, -1 }, values = new[] { 0.25f, 0f, 0f, 0f }, channels = new[] { 0, -1, -1, -1 } },
                    new { lights = new[] { 1, -1, -1, -1 }, values = new[] { 0.5f, 0f, 0f, 0f }, channels = new[] { 1, -1, -1, -1 } },
                    new { lights = new[] { 0, -1, -1, -1 }, values = new[] { 0.75f, 0f, 0f, 0f }, channels = new[] { 0, -1, -1, -1 } },
                    new { lights = new[] { -1, -1, -1, -1 }, values = new float[4], channels = new[] { -1, -1, -1, -1 } },
                },
            });
            var set = ProbeSet.FromJson(JsonDocument.Parse(json).RootElement);
            ProbeTetrahedralization.Build(set);
            return set;
        }

        [Fact]
        public void OcclusionBlendsOnlyChannelsPresentInEachVertex()
        {
            var set = SparseOcclusion();
            int hint = -1;
            Assert.True(set.Evaluate(new Vector3(0.25f, 0.25f, 0.25f), ref hint, out _, out var occ));
            Assert.Equal(new Vector4(0.25f, 0.125f, 1f, 1f), occ);
            // A vertex with no channel still sees the cell's channel presence, even when the
            // contributing vertices have zero weight. Channels absent from the cell stay one.
            Assert.True(set.Evaluate(Vector3.forward, ref hint, out _, out occ));
            Assert.Equal(new Vector4(0f, 0f, 1f, 1f), occ);
        }

        [Fact]
        public void OcclusionChannelPresenceSurvivesSnapshotsAndSceneCombinations()
        {
            var set = SparseOcclusion();
            var combined = ProbeTetrahedralization.Combine(new List<ProbeSet> { set });
            foreach (var candidate in new[] { set.Snapshot(), combined })
            {
                int hint = -1;
                Assert.True(candidate.Evaluate(new Vector3(0.125f, 0.5f, 0.125f), ref hint, out _, out var occ));
                Assert.Equal(new Vector4(0.4375f, 0.0625f, 1f, 1f), occ);
            }
        }

        [Fact]
        public void OcclusionSlotsSharingAChannelAccumulate()
        {
            Assert.Equal(new Vector4(1f, 1f, 0.75f, 1f), ProbeSet.ProbeOcclusion(
                new[] { 2, 7, -1, -1 }, new[] { 0.25f, 0.5f, 0f, 0f }, new[] { 2, 2, -1, -1 }));
        }
    }
}
