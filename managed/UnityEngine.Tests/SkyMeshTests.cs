using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The mesh a sky is drawn with: what the procedural sky's per-vertex colours are spread over.
    /// What it draws is measured against the Editor in tests/projects/bakedbuffers (sky).
    /// </summary>
    public class SkyMeshTests
    {
        [Fact]
        public void TheSphereIsSplitEvenlyAndThenTowardsTheHorizon()
        {
            var points = FrameBuilder.SkyboxTriangles();
            // 8 x 4^3 triangles; the 224 with no corner above a half are cut in three, then the
            // 360 with none above a quarter.
            Assert.Equal(3 * (512 + 2 * 224 + 2 * 360), points.Count);
            Assert.Equal(842, new HashSet<Vector3>(points).Count);
            foreach (var p in points) Assert.InRange(p.magnitude, 1f - 1e-6f, 1f + 1e-6f);

            // Away from the horizon the triangles are the even split's: 11.25 degrees a side
            // along the octahedron's edges.
            int high = 0;
            for (int t = 0; t < points.Count; t += 3)
            {
                float top = Math.Max(Math.Abs(points[t].y), Math.Max(Math.Abs(points[t + 1].y), Math.Abs(points[t + 2].y)));
                float bottom = Math.Min(Math.Abs(points[t].y), Math.Min(Math.Abs(points[t + 1].y), Math.Abs(points[t + 2].y)));
                if (top > 0.5f) high++;
                // Near the horizon no triangle climbs more than a quarter of an even one.
                else if (top <= 0.25f) Assert.True(top - bottom < 0.06f, $"triangle {t / 3} climbs {top - bottom}");
            }
            Assert.Equal(512 - 224, high);
        }
    }
}
