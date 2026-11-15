using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Where agents arrive and how close path points may lie (Unity 6000.0.71f1,
    /// tests/projects/navarrival): the same flat runtime NavMesh and lanes as the probe, whose
    /// Editor arrival frames the measured release reproduces; another release keeps the previous
    /// rules.</summary>
    [Collection("Frames")]
    public sealed class NavMeshArrivalTests
    {
        const float Dt = 0.02f;

        static void Level(Action body)
        {
            float deltaTime = Time.s_DeltaTime;
            bool fixedStep = Time.s_InFixedTimeStep;
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(40f, 0.2f, 200f), transform = Matrix4x4.Translate(new Vector3(0f, -0.1f, 0f)), area = 0 } };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(42f, 4f, 202f)), Vector3.zero, Quaternion.identity);
            var instance = NavMesh.AddNavMeshData(data);
            try { body(); }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
                Time.s_DeltaTime = deltaTime;
                Time.s_InFixedTimeStep = fixedStep;
            }
        }

        /// <summary>The frame (counted as the probe counts its LateUpdates) whose step leaves the
        /// agent standing.</summary>
        static int Arrival(float speed, float acceleration, bool braking, float distance, float z)
        {
            var go = new GameObject("lane");
            try
            {
                var start = new Vector3(-distance / 2f - 3f, 0f, z);
                go.transform.position = start;
                var agent = go.AddComponent<NavMeshAgent>();
                agent.speed = speed;
                agent.acceleration = acceleration;
                agent.autoBraking = braking;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                Assert.True(agent.SetDestination(start + new Vector3(distance, 0f, 0f)));
                for (int frame = 1; frame <= 400; frame++)
                {
                    AgentSystem.Step();
                    if (frame > 3 && agent.velocity == Vector3.zero) return frame;
                }
                return 0;
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Theory]
        // k22.b1: the last braking step leaves 0.000131 m, so it walks once more (Editor: 254).
        [InlineData("6000.0.71f1", 1f, 8f, true, 4.0959f, -95f + 44 * 1.5f, 254)]
        // k8.b1: the step falls 0.0000313 m short of the end and lands on it (Editor: 71).
        [InlineData("6000.0.71f1", 3.5f, 60f, true, 4.1096f, -95f + 16 * 1.5f, 71)]
        // fast.b0: without braking, a step ending 0.0000042 m short leaves the end a corner of its
        // own; the next step passes and lands (Editor: 54).
        [InlineData("6000.0.71f1", 6f, 60f, false, 6f, -95f + 120 * 1.5f, 54)]
        [InlineData("6000.0.72f1", 1f, 8f, true, 4.0959f, -95f + 44 * 1.5f, 253)]
        public void AgentsArriveOnTheMeasuredFrame(string release, float speed, float acceleration, bool braking, float distance, float z, int frame)
        {
            NavigationTestRelease.At(release, () => Level(() =>
                Assert.Equal(frame, Arrival(speed, acceleration, braking, distance, z))));
        }

        /// <summary>fast.b0 at frame 53, 0.0000042 m before its end: the path still has the end as
        /// a corner of its own and a positive remaining distance (Editor: c2 r+).</summary>
        [Theory]
        [InlineData("6000.0.71f1", 2, true)]
        [InlineData("6000.0.72f1", 1, false)]
        public void AnEndMicrometresAwayStaysACorner(string release, int corners, bool remaining)
        {
            NavigationTestRelease.At(release, () => Level(() =>
            {
                var go = new GameObject("lane");
                try
                {
                    var start = new Vector3(-6f, 0f, -95f + 120 * 1.5f);
                    go.transform.position = start;
                    var agent = go.AddComponent<NavMeshAgent>();
                    agent.speed = 6f;
                    agent.acceleration = 60f;
                    agent.autoBraking = false;
                    agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                    Assert.True(agent.SetDestination(start + new Vector3(6f, 0f, 0f)));
                    for (int frame = 1; frame <= 53; frame++) AgentSystem.Step();
                    Assert.NotEqual(Vector3.zero, agent.velocity);
                    Assert.False(agent.hasPath);
                    Assert.Equal(corners, agent.path.corners.Length);
                    Assert.Equal(remaining, agent.remainingDistance > 0f);
                }
                finally { Object.DestroyImmediate(go); }
            }));
        }

        [Theory]
        [InlineData("6000.0.71f1", 0f, 1)]
        [InlineData("6000.0.71f1", 4e-6f, 2)]
        [InlineData("6000.0.71f1", 5e-5f, 2)]
        [InlineData("6000.0.72f1", 4e-6f, 1)]
        [InlineData("6000.0.72f1", 1e-4f, 2)]
        public void StraightPathsMergeOnlyEqualPoints(string release, float distance, int corners)
        {
            NavigationTestRelease.At(release, () => Level(() =>
            {
                Assert.True(NavMesh.SamplePosition(new Vector3(1.3f, 0f, 2.7f), out var hit, 1f, NavMesh.AllAreas));
                var path = new NavMeshPath();
                Assert.True(NavMesh.CalculatePath(hit.position, hit.position + Vector3.right * distance, NavMesh.AllAreas, path));
                Assert.Equal(corners, path.corners.Length);
            }));
        }
    }
}
