using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Runtime links (NavMesh.AddLink) whose ends lie in different NavMesh tiles. A
    /// runtime-built NavMesh is cut into square tiles from the world origin (256 voxels, 42.67 m at
    /// the default 0.5 m radius), so a level round x = 0 or z = 0 has its ground in several tiles.
    /// Each level is two 6 m platforms with a 4 m gap that only the link crosses: a path over it must
    /// be complete (connectivity) and an agent must cross the link and arrive (landing).
    /// Documented (Scripting API 6000.0, NavMeshLinkData and NavMesh.AddLink): a link connects the
    /// NavMesh at its start to the NavMesh at its end, both ways when bidirectional. The tiles are
    /// Unbound's own representation (Unity documents no tile rule for links); a link reaching beyond
    /// a neighbouring tile is not covered here.</summary>
    [Collection("Frames")]
    public sealed class NavMeshLinkConnectivityTests : IDisposable
    {
        const float Dt = 0.02f;

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();
        readonly List<NavMeshLinkInstance> m_Links = new List<NavMeshLinkInstance>();
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshLinkConnectivityTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
        }

        public void Dispose()
        {
            foreach (var go in m_Objects) Object.DestroyImmediate(go);
            foreach (var link in m_Links) NavMesh.RemoveLink(link);
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        /// <summary>Two 6 x 6 m platforms (top at y = 0) centred at a and b, and a link from start
        /// to end.</summary>
        void Level(Vector3 a, Vector3 b, Vector3 start, Vector3 end)
        {
            var sources = new List<NavMeshBuildSource> { Platform(a), Platform(b) };
            var bounds = new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x) + 10f, 4f, Mathf.Abs(b.z - a.z) + 10f));
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            m_Data.Add(NavMesh.AddNavMeshData(data));
            m_Links.Add(NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = start,
                endPosition = end,
                bidirectional = true,
                costModifier = -1f,
                width = 0f,
                area = 0,
                agentTypeID = 0,
            }));
        }

        static NavMeshBuildSource Platform(Vector3 centre) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(6f, 1f, 6f),
            transform = Matrix4x4.Translate(centre + new Vector3(0f, -0.5f, 0f)),
            area = 0,
        };

        static float Dist2D(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>The NavMesh tile (x, z) over a position.</summary>
        static (int X, int Z) Tile(Vector3 p)
        {
            var surface = NavWorld.Get(0);
            Assert.NotNull(surface);
            surface.Mesh.CalcTileLoc(new RcVec3f(p.x, p.y, p.z), out int tx, out int tz);
            return (tx, tz);
        }

        /// <summary>A path from one platform to the other is complete, ends at the target and goes
        /// over the link, entering it at <paramref name="entry"/>.</summary>
        static void AssertCompleteRoute(Vector3 from, Vector3 to, Vector3 entry)
        {
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path), "no path search");
            var corners = path.corners;
            string route = string.Join(" ", corners);
            bool complete = path.status == NavMeshPathStatus.PathComplete;
            Assert.True(complete, $"{path.status}: {route}");
            Assert.True(Dist2D(corners[corners.Length - 1], to) <= 1e-2f, $"ends short: {route}");
            bool overLink = Array.Exists(corners, c => Dist2D(c, entry) <= NavWorld.SnapRadius(NavWorld.Get(0)) + 1e-3f);
            Assert.True(overLink, $"not over the link: {route}");
        }

        /// <summary>An agent sent from one platform to the other gets a complete path, crosses the
        /// link (Detour's automatic traversal) and arrives.</summary>
        void AssertAgentLands(Vector3 from, Vector3 to)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = from;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(to));
            for (int i = 0; agent.pathPending; i++)
            {
                Assert.True(i < 10, "the path stayed pending");
                AgentSystem.Step();
            }
            bool complete = agent.pathStatus == NavMeshPathStatus.PathComplete;
            Assert.True(complete, $"path {agent.pathStatus}, at {agent.nextPosition}");
            bool crossed = agent.isOnOffMeshLink;
            for (int i = 0; i < 600 && !(crossed && Dist2D(agent.nextPosition, to) <= 1e-2f); i++)
            {
                AgentSystem.Step();
                crossed |= agent.isOnOffMeshLink;
            }
            Assert.True(crossed, $"never on the link, at {agent.nextPosition}");
            Assert.True(Dist2D(agent.nextPosition, to) <= 1e-2f, $"stopped at {agent.nextPosition}");
        }

        // Same tile: the whole level inside the tile at the origin's north-east.
        static readonly Vector3 SameA = new Vector3(15f, 0f, 10f), SameB = new Vector3(25f, 0f, 10f);
        static readonly Vector3 SameStart = new Vector3(17f, 0f, 10f), SameEnd = new Vector3(23f, 0f, 10f);

        void SameTileLevel()
        {
            Level(SameA, SameB, SameStart, SameEnd);
            Assert.Equal(Tile(SameStart), Tile(SameEnd));
        }

        [Fact]
        public void ALinkInsideOneTileGivesACompleteRoute()
        {
            SameTileLevel();
            AssertCompleteRoute(new Vector3(13f, 0f, 10f), new Vector3(27f, 0f, 10f), SameStart);
        }

        [Fact]
        public void AnAgentCrossesALinkInsideOneTile()
        {
            SameTileLevel();
            AssertAgentLands(new Vector3(13f, 0f, 10f), new Vector3(27f, 0f, 10f));
        }

        // Adjacent east: the start in the tile west of x = 0, the end in the tile east of it.
        static readonly Vector3 EastA = new Vector3(-5f, 0f, 10f), EastB = new Vector3(5f, 0f, 10f);
        static readonly Vector3 EastStart = new Vector3(-3f, 0f, 10f), EastEnd = new Vector3(3f, 0f, 10f);

        void EastLevel()
        {
            Level(EastA, EastB, EastStart, EastEnd);
            var s = Tile(EastStart);
            Assert.Equal((s.X + 1, s.Z), Tile(EastEnd));
        }

        [Fact]
        public void ALinkIntoTheTileEastGivesACompleteRoute()
        {
            EastLevel();
            AssertCompleteRoute(new Vector3(-7f, 0f, 10f), new Vector3(7f, 0f, 10f), EastStart);
        }

        [Fact]
        public void AnAgentLandsOverALinkIntoTheTileEast()
        {
            EastLevel();
            AssertAgentLands(new Vector3(-7f, 0f, 10f), new Vector3(7f, 0f, 10f));
        }

        // Adjacent north-east: the start south-west of the origin, the end north-east of it.
        static readonly Vector3 NorthEastA = new Vector3(-5f, 0f, -5f), NorthEastB = new Vector3(5f, 0f, 5f);
        static readonly Vector3 NorthEastStart = new Vector3(-3f, 0f, -3f), NorthEastEnd = new Vector3(3f, 0f, 3f);

        void NorthEastLevel()
        {
            Level(NorthEastA, NorthEastB, NorthEastStart, NorthEastEnd);
            var s = Tile(NorthEastStart);
            Assert.Equal((s.X + 1, s.Z + 1), Tile(NorthEastEnd));
        }

        [Fact]
        public void ALinkIntoTheTileNorthEastGivesACompleteRoute()
        {
            NorthEastLevel();
            AssertCompleteRoute(new Vector3(-7f, 0f, -7f), new Vector3(7f, 0f, 7f), NorthEastStart);
        }

        [Fact]
        public void AnAgentLandsOverALinkIntoTheTileNorthEast()
        {
            NorthEastLevel();
            AssertAgentLands(new Vector3(-7f, 0f, -7f), new Vector3(7f, 0f, 7f));
        }

        // Reverse: the east level's bidirectional link crossed from its end back to its start.
        [Fact]
        public void ABidirectionalLinkAcrossTilesGivesACompleteRouteBack()
        {
            EastLevel();
            AssertCompleteRoute(new Vector3(7f, 0f, 10f), new Vector3(-7f, 0f, 10f), EastEnd);
        }

        [Fact]
        public void AnAgentLandsOverABidirectionalLinkAcrossTilesBack()
        {
            EastLevel();
            AssertAgentLands(new Vector3(7f, 0f, 10f), new Vector3(-7f, 0f, 10f));
        }

        // A start on a tile edge: z = 0 is the edge between two tile rows, and the end lies in the
        // next tile column east.
        static readonly Vector3 EdgeA = new Vector3(-5f, 0f, 0f), EdgeB = new Vector3(5f, 0f, 0f);
        static readonly Vector3 EdgeStart = new Vector3(-3f, 0f, 0f), EdgeEnd = new Vector3(3f, 0f, 0f);

        void EdgeLevel()
        {
            Level(EdgeA, EdgeB, EdgeStart, EdgeEnd);
            var south = Tile(EdgeStart - new Vector3(0f, 0f, 0.01f));
            var north = Tile(EdgeStart + new Vector3(0f, 0f, 0.01f));
            Assert.Equal((south.X, south.Z + 1), north);
            Assert.Equal(north.X + 1, Tile(EdgeEnd).X);
        }

        [Fact]
        public void ALinkStartingOnATileEdgeGivesACompleteRoute()
        {
            EdgeLevel();
            AssertCompleteRoute(new Vector3(-7f, 0f, 0f), new Vector3(7f, 0f, 0f), EdgeStart);
        }

        [Fact]
        public void AnAgentLandsOverALinkStartingOnATileEdge()
        {
            EdgeLevel();
            AssertAgentLands(new Vector3(-7f, 0f, 0f), new Vector3(7f, 0f, 0f));
        }

        // Many links landing in one tile: the east level with twenty bidirectional links side by side,
        // 0.2 m apart, all landing on the east platform. Each landing takes a link slot of the
        // landing tile for its way back, more slots than the tile as built holds.
        const int ManyLinks = 20;

        [Fact]
        public void ManyLinksLandingInOneTileAllConnectBothWays()
        {
            var sources = new List<NavMeshBuildSource> { Platform(EastA), Platform(EastB) };
            var bounds = new Bounds((EastA + EastB) * 0.5f, new Vector3(Mathf.Abs(EastB.x - EastA.x) + 10f, 4f, 10f));
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            var starts = new Vector3[ManyLinks];
            var ends = new Vector3[ManyLinks];
            for (int i = 0; i < ManyLinks; i++)
            {
                float z = EastStart.z - 2f + i * 0.2f;
                starts[i] = new Vector3(EastStart.x, 0f, z);
                ends[i] = new Vector3(EastEnd.x, 0f, z);
            }
            // The landing tile as built has fewer link slots in all than there are landings.
            int landingSlots = -1;
            foreach (var t in data.m_Tiles)
            {
                var h = t.header;
                if (EastEnd.x >= h.bmin.X && EastEnd.x <= h.bmax.X && EastEnd.z >= h.bmin.Z && EastEnd.z <= h.bmax.Z)
                    landingSlots = h.maxLinkCount;
            }
            bool tooFew = landingSlots >= 0 && landingSlots < ManyLinks;
            Assert.True(tooFew, $"landing tile slots {landingSlots}, landings {ManyLinks}");
            m_Data.Add(NavMesh.AddNavMeshData(data));
            for (int i = 0; i < ManyLinks; i++)
                m_Links.Add(NavMesh.AddLink(new NavMeshLinkData
                {
                    startPosition = starts[i],
                    endPosition = ends[i],
                    bidirectional = true,
                    costModifier = -1f,
                    width = 0f,
                    area = 0,
                    agentTypeID = 0,
                }));
            // The refresh with every link: it must not throw (out of landing-tile link slots).
            Assert.NotNull(NavWorld.Get(0));
            var s = Tile(starts[0]);
            for (int i = 0; i < ManyLinks; i++)
            {
                Assert.Equal(s, Tile(starts[i]));
                Assert.Equal((s.X + 1, s.Z), Tile(ends[i]));
            }
            // Every link connects both ways: the straight route across at its own z goes over it.
            for (int i = 0; i < ManyLinks; i++)
            {
                float z = starts[i].z;
                AssertCompleteRoute(new Vector3(-7f, 0f, z), new Vector3(7f, 0f, z), starts[i]);
                AssertCompleteRoute(new Vector3(7f, 0f, z), new Vector3(-7f, 0f, z), ends[i]);
            }
        }

        // Many links starting on a tile corner: the world origin is the corner of four tiles, and a
        // 6 m platform round it has ground in each of them, so each link's start is added to all four.
        // Every link lands on a platform in the tile north-east of the origin, from that tile's own
        // copy and from the three others, and each landing takes a link slot of the landing tile for
        // its way back. The ends lie on a line across the direction from the origin, 0.21 m apart.
        const int CornerLinks = 24;

        [Fact]
        public void ManyLinksStartingOnATileCornerAllConnectBothWays()
        {
            var corner = Vector3.zero;
            var landing = new Vector3(10f, 0f, 10f);
            var sources = new List<NavMeshBuildSource> { Platform(corner), Platform(landing) };
            var bounds = new Bounds((corner + landing) * 0.5f, new Vector3(20f, 4f, 20f));
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            var ends = new Vector3[CornerLinks];
            var behind = new Vector3[CornerLinks];
            for (int i = 0; i < CornerLinks; i++)
            {
                float t = -1.725f + i * 0.15f;
                ends[i] = new Vector3(landing.x + t, 0f, landing.z - t);
                // 0.3 m further from the corner, straight behind the end: from here the way back to
                // the corner over link i is strictly shorter than over any other link.
                behind[i] = ends[i] + ends[i].normalized * 0.3f;
            }
            // The landing tile as built has fewer link slots in all than there are links.
            int landingSlots = -1;
            foreach (var tile in data.m_Tiles)
            {
                var h = tile.header;
                if (ends[0].x >= h.bmin.X && ends[0].x <= h.bmax.X && ends[0].z >= h.bmin.Z && ends[0].z <= h.bmax.Z)
                    landingSlots = h.maxLinkCount;
            }
            bool tooFew = landingSlots >= 0 && landingSlots < CornerLinks;
            Assert.True(tooFew, $"landing tile slots {landingSlots}, links {CornerLinks}");
            m_Data.Add(NavMesh.AddNavMeshData(data));
            for (int i = 0; i < CornerLinks; i++)
                m_Links.Add(NavMesh.AddLink(new NavMeshLinkData
                {
                    startPosition = corner,
                    endPosition = ends[i],
                    bidirectional = true,
                    costModifier = -1f,
                    width = 0f,
                    area = 0,
                    agentTypeID = 0,
                }));
            // The refresh with every link: it must not throw (out of landing-tile link slots).
            Assert.NotNull(NavWorld.Get(0));
            var sw = Tile(new Vector3(-0.01f, 0f, -0.01f));
            var ne = Tile(new Vector3(0.01f, 0f, 0.01f));
            Assert.Equal((sw.X + 1, sw.Z + 1), ne);
            Assert.Equal((sw.X, sw.Z + 1), Tile(new Vector3(-0.01f, 0f, 0.01f)));
            Assert.Equal((sw.X + 1, sw.Z), Tile(new Vector3(0.01f, 0f, -0.01f)));
            for (int i = 0; i < CornerLinks; i++)
            {
                Assert.Equal(ne, Tile(ends[i]));
                Assert.Equal(ne, Tile(behind[i]));
            }
            // Every link connects both ways: back from behind its end, the route enters it at its
            // end; out from the corner platform, the route enters a link at the corner.
            var home = new Vector3(-2f, 0f, -2f);
            for (int i = 0; i < CornerLinks; i++)
            {
                AssertCompleteRoute(behind[i], home, ends[i]);
                AssertCompleteRoute(home, behind[i], corner);
            }
        }
    }
}
