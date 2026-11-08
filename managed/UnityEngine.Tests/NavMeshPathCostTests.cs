using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The route the NavMesh path search picks.
    /// Documented (AI Navigation 2.0 manual, Areas and Costs,
    /// https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/AreasAndCosts.html): the
    /// cost of moving between nodes is distance times the cost of the area of the polygon, nodes are
    /// placed on the polygons, and the search does not always find the shortest path.
    /// Measured (tests/projects/navcost, Unity 6000.0.71f1): a polygon's node is placed once, at the
    /// point of the portal from the polygon it is first reached from nearest that polygon's node
    /// (kept 5% of the portal from its ends); a cheaper way found later changes its cost, not its
    /// place; a polygon's heuristic is the distance from the end to the polygon. The level is the
    /// probe's strip rig, its four polygons as Unity baked them: a 1 m Mud strip (area 4) across a
    /// 12 m ground from x -5.33 to 4, open at its +x end.</summary>
    public sealed class NavMeshPathCostTests
    {
        const int Mud = 4;
        const float CellSize = 1f / 6f, CellHeight = 1f / 12f;
        // Rig coordinates in cells (the polygons' vertices lie on the 1/6 m grid), shifted so that
        // every vertex has non-negative cells: x and z of the rig's centre are 6 m.
        const int Shift = 36;

        // The probe's strip rig polygons (x, z in metres from the rig's centre), in Unity's order.
        static readonly (int Area, (float X, float Z)[] Verts)[] s_Strip =
        {
            (0, new[] { (-16f / 3f, -16f / 3f), (-16f / 3f, -0.5f), (4f, -0.5f), (5.5f, -16f / 3f) }),
            (0, new[] { (4f, 0.5f), (-16f / 3f, 0.5f), (-16f / 3f, 5.5f), (5.5f, 5.5f) }),
            (0, new[] { (5.5f, -16f / 3f), (4f, -0.5f), (4f, 0.5f), (5.5f, 5.5f) }),
            (Mud, new[] { (-16f / 3f, 0.5f), (4f, 0.5f), (4f, -0.5f), (-16f / 3f, -0.5f) }),
        };


        /// <summary>A one-tile Detour NavMesh of the strip rig's polygons (neighbours by shared edges).</summary>
        static DtNavMeshQuery StripQuery()
        {
            var verts = new List<int>();
            var index = new Dictionary<(int, int), int>();
            int Vertex(float x, float z)
            {
                var key = ((int)MathF.Round(x / CellSize) + Shift, (int)MathF.Round(z / CellSize) + Shift);
                if (!index.TryGetValue(key, out int i))
                {
                    i = index[key] = verts.Count / 3;
                    verts.AddRange(new[] { key.Item1, 1, key.Item2 });
                }
                return i;
            }
            int nvp = DtDetour.DT_VERTS_PER_POLYGON;
            var polys = new int[s_Strip.Length * 2 * nvp];
            Array.Fill(polys, 0xffff);
            var areas = new int[s_Strip.Length];
            for (int p = 0; p < s_Strip.Length; p++)
            {
                areas[p] = s_Strip[p].Area;
                for (int j = 0; j < s_Strip[p].Verts.Length; j++) polys[p * 2 * nvp + j] = Vertex(s_Strip[p].Verts[j].X, s_Strip[p].Verts[j].Z);
            }
            // Edge j of a polygon (vertex j to j + 1) borders the polygon holding the same edge reversed.
            for (int p = 0; p < s_Strip.Length; p++)
            for (int j = 0, n = s_Strip[p].Verts.Length; j < n; j++)
            {
                int a = polys[p * 2 * nvp + j], b = polys[p * 2 * nvp + (j + 1) % n];
                for (int q = 0; q < s_Strip.Length; q++)
                for (int k = 0, m = s_Strip[q].Verts.Length; k < m; k++)
                    if (q != p && polys[q * 2 * nvp + k] == b && polys[q * 2 * nvp + (k + 1) % m] == a)
                        polys[p * 2 * nvp + nvp + j] = q;
            }
            var data = DtNavMeshBuilder.CreateNavMeshData(new DtNavMeshCreateParams
            {
                verts = verts.ToArray(),
                vertCount = verts.Count / 3,
                polys = polys,
                polyAreas = areas,
                polyFlags = new[] { 1, 1, 1, 1 },
                polyCount = s_Strip.Length,
                nvp = nvp,
                walkableHeight = 2f,
                walkableRadius = 0.5f,
                walkableClimb = 0.4f,
                bmin = RcVec3f.Zero,
                bmax = new RcVec3f(12f, 1f, 12f),
                cs = CellSize,
                ch = CellHeight,
                buildBvTree = true,
            });
            var mesh = new DtNavMesh();
            Assert.True(mesh.Init(data, nvp, 0).Succeeded());
            return new DtNavMeshQuery(mesh);
        }

        static RcVec3f At(float x, float z) => new RcVec3f(x + Shift * CellSize, CellHeight, z + Shift * CellSize);

        /// <summary>The interior corners of the path between two rig points at a Mud cost, as
        /// "x,z" pairs in the rig's coordinates ("direct" without any).</summary>
        static string Route(DtNavMeshQuery query, (float X, float Z) from, (float X, float Z) to, float mudCost)
        {
            var costs = new float[32];
            Array.Fill(costs, 1f);
            costs[Mud] = mudCost;
            var filter = AreaFilter.Of(NavMesh.AllAreas, costs);
            var extents = new RcVec3f(0.5f, 1f, 0.5f);
            Assert.True(query.FindNearestPoly(At(from.X, from.Z), extents, filter, out long start, out var a, out _).Succeeded());
            Assert.True(query.FindNearestPoly(At(to.X, to.Z), extents, filter, out long end, out var b, out _).Succeeded());
            var path = new long[16];
            Assert.True(query.FindPath(start, end, a, b, filter, path, out int count, path.Length).Succeeded());
            var corners = new DtStraightPath[16];
            Assert.True(query.FindStraightPath(a, b, path, count, corners, out int n, corners.Length, 0).Succeeded());
            var route = new List<string>();
            for (int i = 1; i + 1 < n; i++)
                route.Add($"{corners[i].pos.X - Shift * CellSize:0.###},{corners[i].pos.Z - Shift * CellSize:0.###}");
            return route.Count == 0 ? "direct" : string.Join(" ", route);
        }

        const string Around = "4,-0.5 4,0.5";

        /// <summary>Across the strip from x = 0: Unity crosses up to a Mud cost of 6.9154 and goes
        /// round from there (navcost strip.strip1 x0); nodes that moved to cheaper parents' portal
        /// points gave 6.299.</summary>
        [Fact]
        public void CrossingTheStripFlipsAtTheMeasuredCost()
        {
            var query = StripQuery();
            Assert.Equal("direct", Route(query, (0f, -4f), (0f, 4f), 6.88f));
            Assert.Equal(Around, Route(query, (0f, -4f), (0f, 4f), 6.95f));
            // The same crossing the other way (back0: 6.9171).
            Assert.Equal("direct", Route(query, (0f, 4f), (0f, -4f), 6.88f));
            Assert.Equal("4,0.5 4,-0.5", Route(query, (0f, 4f), (0f, -4f), 6.95f));
        }

        /// <summary>Near the strip's open end the polygon heuristic makes the route round the end
        /// cheap early: Unity goes round from a Mud cost of 1.401 for a crossing at x = 2 (strip1 x2;
        /// the node-distance heuristic gave 4.04).</summary>
        [Fact]
        public void NearTheOpenEndTheRouteRoundIsTakenEarly()
        {
            var query = StripQuery();
            Assert.Equal("direct", Route(query, (2f, -4f), (2f, 4f), 1.38f));
            Assert.Equal(Around, Route(query, (2f, -4f), (2f, 4f), 1.42f));
        }

        /// <summary>A diagonal from the same start shares the crossing's threshold (strip1 diag and
        /// x-4: 13.6871): the end polygon's node, placed when the polygon is first reached, is where
        /// both routes end, so the end's position does not weigh between them.</summary>
        [Fact]
        public void TheFirstPlacedNodeDecidesBetweenRoutesToTheSamePolygon()
        {
            var query = StripQuery();
            foreach (var to in new[] { (-4f, 4f), (2f, 4f) })
            {
                Assert.Equal("direct", Route(query, (-4f, -4f), to, 13.6f));
                Assert.NotEqual("direct", Route(query, (-4f, -4f), to, 13.75f));
            }
        }

        /// <summary>FindPath runs the same search as the sliced one (a start and end in one polygon
        /// is that polygon).</summary>
        [Fact]
        public void FindPathInOnePolygonIsThatPolygon()
        {
            var query = StripQuery();
            Assert.Equal("direct", Route(query, (-4f, -4f), (-2f, -2f), 3f));
        }
    }
}
