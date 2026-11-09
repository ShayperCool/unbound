using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Carving NavMeshObstacles: the hole's shape and the NavMesh left around it, and when
    /// the hole is cut or removed.
    /// Documented (AI Navigation 2.0 Manual, NavMesh Obstacle; Scripting API 6000.0
    /// NavMeshObstacle): a carving obstacle cuts a hole that paths go around; with
    /// carveOnlyStationary the hole is removed while the obstacle moves (more than
    /// carvingMoveThreshold) and comes back once it has stood still for more than
    /// carvingTimeToStationary; otherwise the hole follows it whenever it has moved more than the
    /// threshold; the result is available in the next frame. Measured (tests/projects/navmesh): the
    /// hole is the box's outline grown by the agent radius with sharp corners; a change seen by one
    /// carving update takes effect at the next; a new obstacle is carved at once.
    /// The level is one 20 m by 10 m platform (agent radius 0.5: walkable from x = -9.5 to 9.5 and
    /// z = -4.5 to 4.5).</summary>
    [Collection("Frames")]
    public sealed class NavMeshCarvingTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly double m_Time = Time.s_Time;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly float m_Area;

        public NavMeshCarvingTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            ObstacleSystem.ResetCarving();
            var platform = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(20f, 1f, 10f),
                transform = Matrix4x4.Translate(new Vector3(0f, -0.5f, 0f)),
                area = 0,
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { platform },
                new Bounds(Vector3.zero, new Vector3(24f, 4f, 14f)), Vector3.zero, Quaternion.identity);
            m_Data = NavMesh.AddNavMeshData(data);
            m_Area = Area();
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            ObstacleSystem.ResetCarving();
            NavMesh.RemoveNavMeshData(m_Data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
            Time.s_Time = m_Time;
        }

        /// <summary>One frame: the carving update (PreUpdate), then the agents (PreLateUpdate).</summary>
        static void Frame()
        {
            Time.s_Time += Dt;
            ObstacleSystem.UpdateCarving();
            AgentSystem.Step();
        }

        NavMeshObstacle Box(Vector3 position, Vector3 size, float yaw = 0f, bool onlyStationary = true)
        {
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = size;
            obstacle.carveOnlyStationary = onlyStationary;
            obstacle.carving = true;
            return obstacle;
        }

        static float Area()
        {
            var t = NavMesh.CalculateTriangulation();
            float area = 0f;
            for (int i = 0; i + 2 < t.indices.Length; i += 3)
                area += Vector3.Cross(t.vertices[t.indices[i + 1]] - t.vertices[t.indices[i]], t.vertices[t.indices[i + 2]] - t.vertices[t.indices[i]]).magnitude * 0.5f;
            return area;
        }

        /// <summary>The NavMesh is at (x, z): a sample within 2 cm of the platform's NavMesh height.</summary>
        static bool OnNavMesh(float x, float z)
        {
            Assert.True(NavMesh.SamplePosition(new Vector3(-9f, 0f, -4f), out var ground, 1f, NavMesh.AllAreas));
            return NavMesh.SamplePosition(new Vector3(x, ground.position.y, z), out _, 0.02f, NavMesh.AllAreas);
        }

        static NavMeshPath Path(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
            return path;
        }

        /// <summary>The ray from <paramref name="from"/> towards <paramref name="to"/> stops at the
        /// hole's edge at <paramref name="edge"/>.</summary>
        static void AssertEdge(Vector3 from, Vector3 to, Vector3 edge)
        {
            Assert.True(NavMesh.Raycast(from, to, out var hit, NavMesh.AllAreas), $"no edge from {from} to {to}");
            Assert.True(Mathf.Abs(hit.position.x - edge.x) < 1e-3f && Mathf.Abs(hit.position.z - edge.z) < 1e-3f,
                $"edge at {hit.position}, expected {edge}");
        }

        // ------------------------------------------------------------------ the hole

        [Fact]
        public void AStationaryBoxCutsItsOutlineGrownByTheAgentRadius()
        {
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 2f));
            Frame();
            Frame();
            // 1 x 2 grown by 0.5 on each side: 2 x 3, sharp corners.
            Assert.Equal(m_Area - 6f, Area(), 3);
            Assert.False(OnNavMesh(0f, 0f));
            Assert.False(OnNavMesh(-0.95f, 1.45f));
            Assert.True(OnNavMesh(-1.02f, 0f));
            Assert.True(OnNavMesh(0f, 1.52f));
            AssertEdge(new Vector3(-4f, 0f, 0f), Vector3.zero, new Vector3(-1f, 0f, 0f));
            AssertEdge(new Vector3(0f, 0f, 4f), Vector3.zero, new Vector3(0f, 0f, 1.5f));
            AssertEdge(new Vector3(3f, 0f, 0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(1f, 0f, 0.5f));
            // Every place of the platform outside the hole stays connected.
            foreach (var target in new[] { new Vector3(8f, 0f, 3f), new Vector3(8f, 0f, -3f), new Vector3(0f, 0f, 3f), new Vector3(0f, 0f, -3f) })
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(new Vector3(-8f, 0f, 0f), target).status);
        }

        [Fact]
        public void ATurnedBoxCutsItsTurnedOutline()
        {
            Box(new Vector3(2f, 0.5f, 1f), Vector3.one, 30f);
            Frame();
            Frame();
            Assert.Equal(m_Area - 4f, Area(), 3);
            // The measured probe case: local +x (cos 30, -sin 30) face grown to 1 m from the centre.
            var normal = new Vector3(Mathf.Cos(30f * Mathf.Deg2Rad), 0f, -Mathf.Sin(30f * Mathf.Deg2Rad));
            AssertEdge(new Vector3(5f, 0f, 1f), new Vector3(2f, 0f, 1f), new Vector3(2f + 1f / normal.x, 0f, 1f));
            Assert.False(OnNavMesh(2f + 1.3f, 1f + 0.3f));
            Assert.Equal(NavMeshPathStatus.PathComplete, Path(new Vector3(-8f, 0f, 1f), new Vector3(8f, 0f, 1f)).status);
        }

        [Fact]
        public void ABoxOverTheEdgeCutsOnlyWhatIsThere()
        {
            Box(new Vector3(0f, 0.5f, 4.5f), Vector3.one);
            Frame();
            Frame();
            // z from 3.5 to 4.5 within the walkable area (it ends at 4.5): 2 x 1.
            Assert.Equal(m_Area - 2f, Area(), 3);
            Assert.False(OnNavMesh(0f, 4f));
            Assert.True(OnNavMesh(0f, 3.45f));
        }

        [Fact]
        public void OverlappingHolesCutTheirUnion()
        {
            Box(new Vector3(0f, 0.5f, 0f), Vector3.one);
            Box(new Vector3(1f, 0.5f, 0.5f), Vector3.one);
            Frame();
            Frame();
            // Two 2 x 2 squares overlapping by 1 x 1.5.
            Assert.Equal(m_Area - 6.5f, Area(), 3);
            Assert.Equal(NavMeshPathStatus.PathComplete, Path(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f)).status);
        }

        [Fact]
        public void AWallAcrossThePlatformCutsItInTwo()
        {
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, 12f));
            Frame();
            Frame();
            var path = Path(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f));
            Assert.Equal(NavMeshPathStatus.PathPartial, path.status);
            Assert.True(path.corners[path.corners.Length - 1].x <= -0.75f + 1e-3f);
        }

        [Fact]
        public void AnObstacleAboveTheAgentsHeightDoesNotCarve()
        {
            Box(new Vector3(0f, 3f, 0f), Vector3.one);
            Frame();
            Frame();
            Assert.Equal(m_Area, Area(), 3);
        }

        NavMeshObstacle Capsule(Vector3 position, float radius, float height, Quaternion rotation)
        {
            var go = new GameObject("capsule");
            m_Objects.Add(go);
            go.transform.SetPositionAndRotation(position, rotation);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = Vector3.zero;
            obstacle.radius = radius;
            obstacle.height = height;
            obstacle.carveOnlyStationary = false;
            obstacle.carving = true;
            return obstacle;
        }

        [Fact]
        public void ACapsuleCutsAnOctagonRoundItsCircleGrownByTheAgentRadius()
        {
            Capsule(new Vector3(0f, 1f, 0f), 1f, 2f, Quaternion.identity);
            Frame();
            Frame();
            // Corners towards its x and z axes and between, at (1 + 0.5) / cos 22.5 degrees.
            float corner = 1.5f * 1.0823922f;
            Assert.Equal(m_Area - 2f * Mathf.Sqrt(2f) * corner * corner, Area(), 2);
            Assert.False(OnNavMesh(corner - 0.03f, 0f));
            Assert.True(OnNavMesh(corner + 0.03f, 0f));
            // Its sides are 1.5 m from the centre, nearer than its corners.
            float side = 1.5f + 0.03f;
            Assert.True(OnNavMesh(side * Mathf.Cos(22.5f * Mathf.Deg2Rad), side * Mathf.Sin(22.5f * Mathf.Deg2Rad)));
        }

        [Fact]
        public void ACapsuleLyingOnItsSideCutsItsLengthToo()
        {
            Capsule(new Vector3(0f, 1f, 0f), 1f, 4f, Quaternion.Euler(0f, 0f, 90f));
            Frame();
            Frame();
            // Along x: the straight part (1 m each side), then the cap's tip at r / cos 22.5
            // degrees, grown; across: the octagon's side, grown.
            float tip = 1f + 1.0823922f + 0.5f * 1.0823922f;
            Assert.False(OnNavMesh(tip - 0.03f, 0f));
            Assert.True(OnNavMesh(tip + 0.03f, 0f));
            Assert.False(OnNavMesh(0f, 1.0823922f + 0.5f - 0.03f));
        }

        [Theory]
        [InlineData(2.4f, true)]   // its bottom 1.9 m over the floor: within the agent height (2)
        [InlineData(2.6f, false)]  // its bottom 2.1 m over it
        [InlineData(-0.8f, false)] // its top 0.3 m under the floor
        [InlineData(-0.35f, true)] // its top 15 cm over it (the NavMesh lies about 7 cm over it)
        public void AnObstacleCarvesWhenItsHeightReachesTheFloorOrTheAgentsAboveIt(float y, bool carves)
        {
            Box(new Vector3(0f, y, 0f), Vector3.one, onlyStationary: false);
            Frame();
            Frame();
            Assert.Equal(carves, !OnNavMesh(0f, 0f));
        }

        [Fact]
        public void ACornerSharperThanARightAngleIsCutAcross()
        {
            // A flat 3 m square tilted 60 degrees about its diagonal along x: a rhombus from
            // above, its corners at x = +-2.12 turning by 127 degrees.
            var obstacle = Box(new Vector3(0f, 0.5f, 0f), new Vector3(3f, 0.01f, 3f), onlyStationary: false);
            obstacle.transform.rotation = Quaternion.AngleAxis(60f, Vector3.right) * Quaternion.Euler(0f, 45f, 0f);
            Frame();
            Frame();
            // Cut across at 2.66 m (the edges moved out by 0.5 and pushed on by 0.7 of it along
            // the edge coming in), not at the sides' meeting point 3.24 m out.
            Assert.False(OnNavMesh(2.6f, 0f));
            Assert.True(OnNavMesh(2.72f, 0f));
            Assert.True(OnNavMesh(-2.72f, 0f));
            Assert.False(OnNavMesh(2.6f, 0.2f));
        }

        [Fact]
        public void HolesInOnePolygonAreCutInTheSameOrderWhateverOrderTheObstaclesCameIn()
        {
            string[] Cut(bool reversed)
            {
                // Both in the polygon x -9.33 to 0, z 0 to 4.5: a turned capsule on its edge and
                // one lying along x above it; the second cut splits a piece the first left.
                var at = new[] { new Vector3(-5f, 1f, 0f), new Vector3(-5f, 1f, 3f) };
                var rotations = new[] { Quaternion.Euler(0f, 22.5f, 0f), Quaternion.Euler(0f, 0f, 90f) };
                var radii = new[] { 1f, 0.5f };
                var heights = new[] { 2f, 3f };
                var made = new List<NavMeshObstacle>();
                for (int i = 0; i < 2; i++)
                {
                    int k = reversed ? 1 - i : i;
                    made.Add(Capsule(at[k], radii[k], heights[k], rotations[k]));
                }
                Frame();
                Frame();
                var polygons = Polygons();
                foreach (var o in made) Object.DestroyImmediate(o.gameObject);
                // A fresh start: the next obstacles come in in the order they are made.
                ObstacleSystem.ResetCarving();
                Assert.Equal(m_Area, Area(), 3);
                return polygons;
            }
            Assert.Equal(Cut(false), Cut(true));
        }

        /// <summary>The NavMesh's polygons (a fan of CalculateTriangulation's triangles is one
        /// polygon), each as its vertices (x,z) in order from the smallest, sorted.</summary>
        static string[] Polygons()
        {
            var t = NavMesh.CalculateTriangulation();
            var result = new List<string>();
            var current = new List<int>();
            void Flush()
            {
                if (current.Count == 0) return;
                var names = new List<string>();
                foreach (int i in current)
                    names.Add(Name(t.vertices[i].x) + "," + Name(t.vertices[i].z));
                int first = 0;
                for (int i = 1; i < names.Count; i++) if (string.CompareOrdinal(names[i], names[first]) < 0) first = i;
                var rotated = new List<string>();
                for (int i = 0; i < names.Count; i++) rotated.Add(names[(first + i) % names.Count]);
                result.Add(string.Join(" ", rotated));
                current.Clear();
            }
            for (int i = 0; i + 2 < t.indices.Length; i += 3)
            {
                int a = t.indices[i], b = t.indices[i + 1], c = t.indices[i + 2];
                if (current.Count > 0 && current[0] == a && current[current.Count - 1] == b)
                {
                    current.Add(c);
                    continue;
                }
                Flush();
                current.Add(a);
                current.Add(b);
                current.Add(c);
            }
            Flush();
            result.Sort(string.CompareOrdinal);
            return result.ToArray();
        }

        static string Name(float v)
        {
            var s = v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        [Fact]
        public void ACutPolygonIsSplitAlongTheHolesEdgesToItsFarthestCorners()
        {
            // As Unity splits it (tests/projects/navpath carve.sides): every hole edge inside the
            // polygon makes a triangle with the polygon's corner farthest outside it, the corners
            // between are fanned from the hole's corners, and the triangles merge in order: here
            // three quads in each of the two tiles (x > 0) the hole reaches.
            Box(new Vector3(2f, 0.5f, 0.5f), new Vector3(1f, 1f, 2f));
            Frame();
            Frame();
            Assert.Equal(new[]
            {
                "-9.333,-4.333 -9.333,0 0,0 0,-4.333",
                "-9.333,0 -9.333,4.5 0,4.5 0,0",
                "0,-4.333 0,0 1,0 1,-1",
                "0,-4.333 1,-1 3,-1 9.5,-4.333",
                "0,0 0,4.5 1,2 1,0",
                "0,4.5 9.5,4.5 3,2 1,2",
                "3,-1 3,0 9.5,0 9.5,-4.333",
                "3,0 3,2 9.5,4.5 9.5,0",
            }, Polygons());
            Assert.Equal(m_Area - 6f, Area(), 3);
        }

        [Fact]
        public void AHoleThatOnlyTouchesTheNavMeshCutsNothing()
        {
            var before = Polygons();
            // Grown by the radius, the outline ends on the platform's edge (z = 4.5).
            Box(new Vector3(0f, 0.5f, 5.5f), Vector3.one);
            Frame();
            Frame();
            Assert.Equal(before, Polygons());
            Assert.Equal(m_Area, Area(), 3);
        }

        [Fact]
        public void AHoleOverTheWholePolygonRemovesIt()
        {
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(30f, 1f, 20f));
            Frame();
            Frame();
            Assert.Empty(Polygons());
            Assert.False(NavMesh.SamplePosition(Vector3.zero, out _, 20f, NavMesh.AllAreas));
        }

        // ------------------------------------------------------------------ when

        [Fact]
        public void TheHoleTakesEffectAtTheSecondCarvingUpdate()
        {
            var obstacle = Box(Vector3.zero, Vector3.one);
            int version = NavWorld.Changes;
            ObstacleSystem.UpdateCarving();
            Assert.True(OnNavMesh(0f, 0f));
            ObstacleSystem.UpdateCarving();
            Assert.False(OnNavMesh(0f, 0f));
            Assert.NotEqual(version, NavWorld.Changes);
            // Unchanged: no further NavMesh change.
            version = NavWorld.Changes;
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            Assert.Equal(version, NavWorld.Changes);
            // Carving off: gone after two updates again.
            obstacle.carving = false;
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            Frame();
            Assert.True(OnNavMesh(0f, 0f));
            Assert.Equal(m_Area, Area(), 3);
        }

        [Fact]
        public void ACarveOnlyStationaryHoleWaitsUntilTheObstacleStandsStill()
        {
            var obstacle = Box(Vector3.zero, Vector3.one);
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            // Below the threshold: the hole stays where it is.
            obstacle.transform.position = new Vector3(0.05f, 0f, 0f);
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            Assert.True(OnNavMesh(1.04f, 0f));
            // Moved: the hole is removed, and cut at the new place once it has stood still for more
            // than 0.5 s: in the 27th frame after the move (tests/projects/navpath carve.rules, the
            // decision late in one frame and the cut early in the next).
            obstacle.transform.position = new Vector3(4f, 0f, 0f);
            Frame();
            Frame();
            Assert.True(OnNavMesh(0f, 0f));
            Assert.True(OnNavMesh(4f, 0f));
            int frames = 2;
            while (OnNavMesh(4f, 0f) && frames < 100)
            {
                Frame();
                frames++;
            }
            Assert.Equal(27, frames);
        }

        [Theory]
        [InlineData(3f, 1f, false)]    // 0.052 rad x |scale| sqrt 3 x |extents| 0.87: 0.078 < 0.1
        [InlineData(5.7f, 1f, true)]   // 0.149
        [InlineData(0f, 1.04f, false)] // 0.04 sqrt 3 x 0.87: 0.06
        [InlineData(0f, 1.08f, true)]  // 0.12
        public void TurningOrScalingAStationaryOnlyObstacleCountsAsMovingPastTheThreshold(float yaw, float scale, bool moved)
        {
            var obstacle = Box(new Vector3(0f, 0.5f, 0f), Vector3.one);
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            obstacle.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            obstacle.transform.localScale = Vector3.one * scale;
            Frame();
            Frame();
            Assert.Equal(moved, OnNavMesh(0f, 0f));
        }

        [Fact]
        public void AMovingCarvingHoleFollowsPastTheThreshold()
        {
            var obstacle = Box(Vector3.zero, Vector3.one, onlyStationary: false);
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            for (int i = 1; i <= 20; i++)
            {
                obstacle.transform.position = new Vector3(0.2f * i, 0f, 0f);
                Frame();
                // Cut where the previous update saw it, never removed while it moves.
                Assert.False(OnNavMesh(0.2f * (i - 1), 0f), $"no hole at {0.2f * (i - 1)} after move {i}");
            }
            Frame();
            Frame();
            Assert.False(OnNavMesh(4f, 0f));
            Assert.True(OnNavMesh(0f, 0f));
        }

        NavMeshAgent Walker(Vector3 start, Vector3 goal, float stoppingDistance = 0f)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = start;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.stoppingDistance = stoppingDistance;
            Assert.True(agent.SetDestination(goal));
            return agent;
        }

        [Fact]
        public void ACutReplacesOnlyTheTilesTheHoleReachesAndTheirAgentsPaths()
        {
            // The platform is four tiles (x and z either side of 0); the hole lies in the one of
            // x < 0, z > 0. The walker's path stays in the tiles of x > 0, the crosser's crosses
            // the hole's tile.
            var walker = Walker(new Vector3(2f, 0f, -3f), new Vector3(8f, 0f, -3f));
            var crosser = Walker(new Vector3(-8f, 0f, 3f), new Vector3(-1f, 0f, 3f));
            Box(new Vector3(-5f, 0.5f, 3f), Vector3.one);
            // The hole is decided now and cut at the next carving update.
            Frame();
            var surface = NavWorld.Get(0);
            var refs = new List<long>();
            foreach (var placed in surface.Tiles) refs.Add(placed.Ref);
            var walking = walker.m_Crowd;
            var crossing = crosser.m_Crowd;
            int version = NavWorld.Version;
            Frame();
            Assert.False(OnNavMesh(-5f, 3f));
            Assert.Equal(version, NavWorld.Version);
            int changed = 0;
            for (int i = 0; i < refs.Count; i++)
                if (surface.Tiles[i].Ref != refs[i]) changed++;
            Assert.Equal(1, changed);
            // The walker keeps its corridor; the crosser rejoins with a path round the hole.
            Assert.Same(walking, walker.m_Crowd);
            Assert.NotSame(crossing, crosser.m_Crowd);
            Assert.True(crosser.hasPath);
            for (int i = 0; i < 400 && (walker.hasPath || crosser.hasPath); i++) Frame();
            Assert.True((crosser.nextPosition - new Vector3(-1f, crosser.nextPosition.y, 3f)).magnitude < 0.05f, $"crosser at {crosser.nextPosition}");
            Assert.True((walker.nextPosition - new Vector3(8f, walker.nextPosition.y, -3f)).magnitude < 0.05f, $"walker at {walker.nextPosition}");
        }

        [Fact]
        public void ATurnedBoxsCornerAcrossATileEdgeIsCutAndRemovedThereToo()
        {
            // Turned 45 degrees at x = 1.3, the box's outline ends at x = 0.59, but grown by the
            // radius with sharp corners it reaches x = -0.11: into the tiles of x < 0. Cut and
            // removed tile by tile, the NavMesh must be what a whole rebuild makes.
            var before = Polygons();
            var obstacle = Box(new Vector3(1.3f, 0.5f, -2f), Vector3.one, 45f, onlyStationary: false);
            Frame();
            Frame();
            var cut = Polygons();
            Assert.False(OnNavMesh(-0.05f, -2f));
            NavWorld.MarkDirty();
            NavWorld.RefreshNow();
            Assert.Equal(Polygons(), cut);
            obstacle.carving = false;
            Frame();
            Frame();
            Assert.True(OnNavMesh(-0.05f, -2f));
            Assert.Equal(before, Polygons());
        }

        [Fact]
        public void APartialPathOpenedByRemovedCarvingGoesOnToTheDestination()
        {
            // A wall carved across the platform: the agent gets a partial path. Carving (no link
            // change) does not make the path stale, so when the wall's hole goes the agent's path is
            // planned again to its destination (Unbound's choice, unmeasured), not kept to the old end.
            var wall = Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, 12f), 0f, onlyStationary: false);
            Frame();
            Frame();
            var agent = Walker(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f));
            Frame();
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            Assert.False(agent.isPathStale);
            // The obstacle goes (carving off alone would leave it to be avoided).
            wall.enabled = false;
            Frame();
            Frame();
            Frame();
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            for (int i = 0; i < 400; i++) Frame();
            Assert.True((agent.nextPosition - new Vector3(8f, agent.nextPosition.y, 0f)).magnitude < 0.05f, $"at {agent.nextPosition}");
            Assert.True((agent.nextPosition - new Vector3(8f, agent.nextPosition.y, 0f)).magnitude < 0.05f, $"at {agent.nextPosition}");
        }

        [Fact]
        public void AHoleCutElsewhereLetsAnArrivingAgentFinish()
        {
            // The rebuild after a cut must not stop an agent in its last centimetre (hasPath
            // already false, still moving to the end). The hole is decided first, then cut in
            // exactly such a frame.
            var goal = new Vector3(8f, 0f, 0f);
            var agent = Walker(new Vector3(4f, 0f, 0f), goal);
            Box(new Vector3(-8f, 0.5f, 3f), Vector3.one);
            ObstacleSystem.UpdateCarving();
            bool finishing = false;
            for (int i = 0; i < 400 && !finishing; i++)
            {
                Time.s_Time += Dt;
                AgentSystem.Step();
                var p = agent.nextPosition;
                finishing = !agent.hasPath && new Vector2(p.x - goal.x, p.z - goal.z).magnitude > 1e-4f;
            }
            Assert.True(finishing, "no frame within the last centimetre");
            int version = NavWorld.Changes;
            Frame();
            Assert.NotEqual(version, NavWorld.Changes);
            Assert.False(OnNavMesh(-8f, 3f));
            for (int i = 0; i < 20; i++) Frame();
            var end = agent.nextPosition;
            Assert.True(new Vector2(end.x - goal.x, end.z - goal.z).magnitude <= 1e-4f, $"stopped at {end}");
            Assert.False(agent.hasPath);
        }

        [Fact]
        public void AnAgentStoppedWithinItsStoppingDistanceStaysStopped()
        {
            var agent = Walker(new Vector3(-8f, 0f, 0f), new Vector3(4f, 0f, 0f), 3f);
            for (int i = 0; i < 400; i++) Frame();
            var stopped = agent.nextPosition;
            Assert.True(stopped.x > 0.5f && stopped.x < 1.5f, $"at {stopped}");
            Assert.Equal(Vector3.zero, agent.velocity);
            // A hole cut between it and its destination: it does not set off again.
            Box(new Vector3(2.5f, 0.5f, 0f), Vector3.one);
            for (int i = 0; i < 30; i++) Frame();
            Assert.False(OnNavMesh(2.5f, 0f));
            Assert.True((agent.nextPosition - stopped).magnitude < 1e-4f, $"moved to {agent.nextPosition}");
            Assert.False(agent.hasPath);
        }

        [Fact]
        public void APendingPathIsFoundWhileAHoleMovesEveryFrame()
        {
            int iterations = NavMesh.pathfindingIterationsPerFrame;
            try
            {
                // Many small holes make the search need many iterations; one per frame.
                NavMesh.pathfindingIterationsPerFrame = 1;
                for (int k = -3; k <= 3; k++)
                {
                    Box(new Vector3(k * 2.5f, 0.5f, 0f), new Vector3(0.3f, 1f, 0.3f));
                    Box(new Vector3(k * 2.5f + 1.25f, 0.5f, -2.5f), new Vector3(0.3f, 1f, 0.3f));
                }
                var carver = Box(new Vector3(-8f, 0.5f, 3.5f), Vector3.one, onlyStationary: false);
                Frame();
                Frame();
                var agent = Walker(new Vector3(-8f, 0f, -3f), new Vector3(8f, 0f, -3f));
                int version = NavWorld.Changes;
                int frames = 0;
                float x = -8f;
                while (agent.pathPending && frames < 300)
                {
                    x += 0.15f;
                    carver.transform.position = new Vector3(x, 0.5f, 3.5f);
                    Frame();
                    frames++;
                }
                Assert.False(agent.pathPending, $"still pending after {frames} frames");
                Assert.True(NavWorld.Changes - version >= frames - 1, "the NavMesh did not change every frame");
                Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            }
            finally
            {
                NavMesh.pathfindingIterationsPerFrame = iterations;
            }
        }

        [Fact]
        public void ABoxWithNoThicknessCarvesAStrip()
        {
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(4f, 1f, 0f));
            Frame();
            Frame();
            // A 4 m segment grown by 0.5: 5 x 1.
            Assert.Equal(m_Area - 5f, Area(), 3);
            Assert.False(OnNavMesh(0f, 0.3f));
            Assert.False(OnNavMesh(2.3f, 0f));
            Assert.True(OnNavMesh(0f, 0.55f));
        }

        [Fact]
        public void ABoxOfNoWidthOrDepthCarvesNothing()
        {
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 1f, 0f));
            Frame();
            Frame();
            // One point seen from above: no outline to cut.
            Assert.Equal(m_Area, Area(), 3);
        }

        [Fact]
        public void AnAgentReplansAroundANewHole()
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-8f, 0f, 0f);
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.SetDestination(new Vector3(8f, 0f, 0f)));
            Frame();
            // Straight along z = 0 (a collinear waypoint at a tile seam allowed).
            foreach (var corner in agent.path.corners) Assert.True(Mathf.Abs(corner.z) < 1e-4f);
            Box(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 3f));
            Frame();
            foreach (var corner in agent.path.corners) Assert.True(Mathf.Abs(corner.z) < 1e-4f, $"replanned too early: {corner}");
            Frame();
            var corners = agent.path.corners;
            Assert.Contains(corners, c => Mathf.Abs(c.z) >= 2f - 1e-3f);
            foreach (var corner in corners)
                Assert.True(Mathf.Abs(corner.x) >= 1f - 1e-3f || Mathf.Abs(corner.z) >= 2f - 1e-3f, $"corner {corner} in the hole");
            Assert.False(agent.isPathStale);
            for (int i = 0; i < 600 && agent.remainingDistance > 0.01f; i++)
            {
                Frame();
                var p = agent.nextPosition;
                Assert.False(Mathf.Abs(p.x) < 0.99f && Mathf.Abs(p.z) < 1.99f, $"agent in the hole at {p}");
            }
            Assert.True((agent.nextPosition - new Vector3(8f, agent.nextPosition.y, 0f)).magnitude < 0.05f, $"stopped at {agent.nextPosition}");
        }

        /// <summary>A hole cut under a standing agent near its edge (within the agent's radius of
        /// it) places the agent at the edge, and the early AI update after the carving update writes
        /// its transform there (Unbound's order within PreUpdate; unmeasured).</summary>
        [Fact]
        public void AStandingAgentInANewHoleStandsAtItsEdgeFromTheEarlyUpdate()
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-6f, 0f, 2f);
            var agent = go.AddComponent<NavMeshAgent>();
            var before = agent.transform.position;
            // The hole (the box grown by the 0.5 m radius) reaches x = -5.7: 0.3 m past the agent.
            Box(new Vector3(-7.2f, 0.5f, 2f), new Vector3(2f, 1f, 2f), onlyStationary: false);
            for (int i = 0; i < 3 && OnNavMesh(-6f, 2f); i++) ObstacleSystem.UpdateCarving();
            Assert.False(OnNavMesh(-6f, 2f));
            AgentSystem.SyncTransforms();
            var p = agent.transform.position;
            Assert.True(agent.isOnNavMesh);
            Assert.True(p.x >= -5.7f - 1e-3f, $"inside the hole at {p}");
            Assert.NotEqual(before, p);
            Assert.Equal(agent.nextPosition, p);
        }
    }
}
