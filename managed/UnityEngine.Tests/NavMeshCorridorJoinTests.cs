using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An agent's corridor after a move that leaves its first polygons (Unity 6000.0.71f1,
    /// tests/projects/avoidance `corners`): the polygon it reaches is joined to the furthest corridor
    /// polygon it borders. The level is a 20 m floor built in tiles of 32 voxels (5.33 m), four of
    /// them meeting at the origin; agents start on that corner and walk along the tile edge z = 0.</summary>
    [Collection("Frames")]
    public sealed class NavMeshCorridorJoinTests : IDisposable
    {
        const float Dt = 0.02f;
        const float Tile = 32f / 6f;

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshCorridorJoinTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var settings = NavMesh.GetSettingsByID(0);
            settings.overrideTileSize = true;
            settings.tileSize = 32;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(20f, 0.1f, 20f),
                    transform = Matrix4x4.Translate(new Vector3(0f, -0.05f, 0f)),
                    area = 0,
                },
            };
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(24f, 4f, 24f)), Vector3.zero, Quaternion.identity);
            m_Data = NavMesh.AddNavMeshData(data);
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            NavMesh.RemoveNavMeshData(m_Data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        /// <summary>An agent without avoidance on the tile corner, given its path to (8, 0) along
        /// the edge, then moved by <paramref name="step"/>; its steering target after the next update.</summary>
        Vector3 SteeringAfterMove(Vector3 step)
        {
            var go = new GameObject("walker");
            m_Objects.Add(go);
            go.transform.position = Vector3.zero;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.speed = 0.5f;
            Assert.True(agent.SetDestination(new Vector3(8f, 0f, 0f)));
            AgentSystem.Step();
            Assert.True(agent.hasPath);
            agent.Move(step);
            AgentSystem.Step();
            return agent.steeringTarget;
        }

        /// <summary>Stepping off the corner into the tile beside its path's first polygons, the
        /// agent goes on along the edge to the next tile corner (the polygons of the other two tiles
        /// round the corner are left out of its corridor); Detour's merge alone, which other releases
        /// keep (unmeasured), turns it back to the corner it stood on.</summary>
        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void AStepRoundATileCornerJoinsTheCorridorToItsFurthestNeighbour(string release)
        {
            using var pinned = TestRelease.Pin(release);
            var steer = SteeringAfterMove(new Vector3(0.05f, 0f, 0.05f));
            var expected = Compat.NavMeshCorridorJoinMeasured ? new Vector3(Tile, 0f, 0f) : Vector3.zero;
            Assert.True(new Vector2(steer.x - expected.x, steer.z - expected.z).magnitude < 1e-3f, $"steering to {steer}");
        }

        /// <summary>Control: a step within the path's own polygons leaves the corridor as it was,
        /// whatever the release; the agent steers straight to its destination.</summary>
        [Theory]
        [InlineData(TestRelease.Measured)]
        [InlineData(TestRelease.Unmeasured)]
        public void AStepAlongThePathKeepsTheCorridor(string release)
        {
            using var pinned = TestRelease.Pin(release);
            var steer = SteeringAfterMove(new Vector3(0.05f, 0f, -0.05f));
            Assert.True(new Vector2(steer.x - 8f, steer.z).magnitude < 1e-3f, $"steering to {steer}");
        }
    }
}
