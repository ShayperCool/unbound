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

        // ------------------------------------------------------------------ when

        [Fact]
        public void TheHoleTakesEffectAtTheSecondCarvingUpdate()
        {
            var obstacle = Box(Vector3.zero, Vector3.one);
            int version = NavWorld.Version;
            ObstacleSystem.UpdateCarving();
            Assert.True(OnNavMesh(0f, 0f));
            ObstacleSystem.UpdateCarving();
            Assert.False(OnNavMesh(0f, 0f));
            Assert.NotEqual(version, NavWorld.Version);
            // Unchanged: no further NavMesh change.
            version = NavWorld.Version;
            Frame();
            Frame();
            Assert.False(OnNavMesh(0f, 0f));
            Assert.Equal(version, NavWorld.Version);
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
            // than 0.5 s (the measured 26th frame after the one that saw the move, then one more).
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
            Assert.Equal(28, frames);
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
            int version = NavWorld.Version;
            Frame();
            Assert.NotEqual(version, NavWorld.Version);
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
                int version = NavWorld.Version;
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
                Assert.True(NavWorld.Version - version >= frames - 1, "the NavMesh did not change every frame");
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
