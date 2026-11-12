using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Where the agent update falls in the frame and what scripts see between updates.
    /// Documented (Scripting API 6000.0, NavMeshAgent.nextPosition): the transform matches the
    /// simulated position when Update is called. Measured (tests/projects/navcost loop.*, Unity
    /// 6000.0.71f1): the whole agent update (paths, steering, motion, links, turning) runs once per
    /// frame before Update; SetDestination gives the path at once when it needs no search (the
    /// destination on the agent's polygon, or on the rest of its path), else at that update; a
    /// script's velocity, isStopped, Warp and nextPosition act as below. The level is a 12 m ground
    /// with a 2 m hole in the middle (several polygons), away from other tests' levels.</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentLoopTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 Center = new Vector3(-40f, 0f, -40f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshAgentLoopTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(12f, 1f, 12f),
                    transform = Matrix4x4.Translate(Center + new Vector3(0f, -0.5f, 0f)),
                    area = 0,
                },
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(2f, 2f, 2f),
                    transform = Matrix4x4.Translate(Center + new Vector3(0f, 1f, 0f)),
                    area = 1,
                },
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Center, new Vector3(14f, 4f, 14f)), Vector3.zero, Quaternion.identity);
            m_Data = NavMesh.AddNavMeshData(data);
        }

        public void Dispose()
        {
            foreach (var go in m_Objects) Object.DestroyImmediate(go);
            NavMesh.RemoveNavMeshData(m_Data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        NavMeshAgent Agent(Vector3 local)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = Center + local;
            return go.AddComponent<NavMeshAgent>();
        }

        static long PolygonOf(NavMeshAgent agent, Vector3 position)
        {
            Assert.True(NavMesh.Nearest(agent.m_Surface, position, NavMesh.AgentExtents(0), agent.Filter, out var polyRef, out _));
            return polyRef;
        }

        /// <summary>An agent walking along z = <paramref name="z"/> towards x = 5, after
        /// <paramref name="steps"/> updates.</summary>
        NavMeshAgent Walking(int steps, float z = -4f)
        {
            var agent = Agent(new Vector3(-5f, 0f, z));
            Assert.True(agent.SetDestination(Center + new Vector3(5f, 0f, z)));
            for (int i = 0; i < steps; i++) AgentSystem.Step();
            Assert.True(agent.hasPath);
            return agent;
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(2f)]
        public void BrakingTakesTheWholeSlowerPlanWhenItsHeadingChanges(float upward)
        {
            var agent = Agent(new Vector3(-4f, 0f, -4f));
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            Assert.True(agent.SetDestination(Center + new Vector3(-3.4f, 0f, -4f)));
            AgentSystem.Step();
            agent.velocity = new Vector3(0f, upward, 4f);
            AgentSystem.Step();
            var plan = agent.desiredVelocity;
            var before = agent.nextPosition;
            Assert.True(plan.x > 3f);
            AgentSystem.Step();
            Assert.InRange(MathF.Abs(agent.nextPosition.x - before.x - plan.x * Dt), 0f, 1e-4f);
            Assert.InRange(MathF.Abs(agent.nextPosition.z - before.z - plan.z * Dt), 0f, 1e-4f);
        }

        [Theory]
        [InlineData(0f, 20f)]
        [InlineData(-20f, 0f)]
        public void ScriptedUnbrakedMotionDoesNotArriveByDistanceAlone(float x, float z)
        {
            var agent = Agent(new Vector3(-4f, 0f, -4f));
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            Assert.True(agent.SetDestination(Center + new Vector3(-3.65f, 0f, -4f)));
            AgentSystem.Step();
            var before = agent.nextPosition;
            agent.velocity = new Vector3(x, 0f, z);
            AgentSystem.Step();
            Assert.InRange(MathF.Abs(agent.nextPosition.x - before.x - x * Dt), 0f, 1e-3f);
            Assert.InRange(MathF.Abs(agent.nextPosition.z - before.z - z * Dt), 0f, 1e-3f);
            Assert.True(agent.hasPath);
            Assert.True(agent.remainingDistance > 0.3f);
        }

        [Fact]
        public void ReachingTheDestinationEndsAPathWithoutAutomaticBraking()
        {
            var agent = Agent(new Vector3(-4f, 0f, -4f));
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            Assert.True(agent.SetDestination(Center + new Vector3(-3.65f, 0f, -4f)));
            for (int i = 0; i < 100; i++) AgentSystem.Step();
            Assert.InRange(Vector3.Distance(agent.nextPosition, agent.destination), 0f, 1e-3f);
            Assert.Equal(Vector3.zero, agent.velocity);
            Assert.False(agent.hasPath);
        }

        [Fact]
        public void TheAgentUpdateRunsAtPreUpdatesAIUpdateAndNothingAfterTheScripts()
        {
            Agent(new Vector3(-5f, 0f, 4f));
            Assert.Contains((Action)AgentSystem.Step, PlayerLoopDriver.Registered(typeof(UnityEngine.PlayerLoop.PreUpdate.AIUpdate)));
            foreach (var action in PlayerLoopDriver.Registered(typeof(UnityEngine.PlayerLoop.PreLateUpdate.AIUpdatePostScript)))
            {
                // Methods and lambdas (declared in a nested compiler-made class) of the agents.
                Assert.NotEqual(typeof(AgentSystem), action.Method.DeclaringType);
                Assert.NotEqual(typeof(AgentSystem), action.Method.DeclaringType?.DeclaringType);
            }
        }

        [Fact]
        public void ADestinationOnTheRestOfThePathKeepsTheCorridorUpToIt()
        {
            var agent = Agent(new Vector3(-5f, 0f, 0f));
            Assert.True(agent.SetDestination(Center + new Vector3(5f, 0f, 0f)));
            AgentSystem.Step();
            var ag = agent.m_Crowd;
            int count = ag.corridor.GetPathCount();
            Assert.True(count > 2, $"corridor of {count}");
            var path = ag.corridor.GetPath().ToArray();
            // A destination on the corridor's next-to-last polygon: the corridor is cut there.
            var target = PointOn(agent, path[count - 2]);
            Assert.True(agent.SetDestination(target));
            Assert.False(agent.pathPending);
            Assert.Equal(count - 1, ag.corridor.GetPathCount());
            for (int i = 0; i < count - 1; i++) Assert.Equal(path[i], ag.corridor.GetPath()[i]);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.True(Vector2.Distance(new Vector2(target.x, target.z), new Vector2(agent.pathEndPosition.x, agent.pathEndPosition.z)) < 1e-3f);
            // The corners lead to the destination: the remaining distance is at least the straight line.
            float straight = Vector2.Distance(new Vector2(target.x, target.z), new Vector2(agent.nextPosition.x, agent.nextPosition.z));
            Assert.InRange(agent.remainingDistance, straight - 1e-3f, straight + 6f);
        }

        [Fact]
        public void AStalePathIsSearchedAnewEvenForADestinationOnIt()
        {
            var agent = Walking(5);
            agent.SetAreaCost(0, 2f);
            Assert.True(agent.isPathStale);
            Assert.True(agent.SetDestination(Center + new Vector3(4f, 0f, -4f)));
            Assert.True(agent.pathPending);
            AgentSystem.Step();
            Assert.False(agent.pathPending);
            Assert.False(agent.isPathStale);
        }

        /// <summary>A target off the NavMesh is mapped at once to the nearest NavMesh point within
        /// 20 agent radii across and 15 up and down (measured: tests/projects/navlinks targets, also
        /// for radius 1); the destination reports that point and the path leads to it.</summary>
        [Fact]
        public void ATargetBeyondTheEdgeIsMappedToTheNearestPoint()
        {
            var agent = Agent(new Vector3(-4f, 0f, -4f));
            // 5 m beyond the ground's edge (z = -6).
            Assert.True(agent.SetDestination(Center + new Vector3(-4f, 0f, -11f)));
            var mapped = agent.destination;
            Assert.InRange(mapped.z - Center.z, -6f, -5f);
            Assert.InRange(mapped.x - Center.x, -4.001f, -3.999f);
            Assert.Equal(agent.nextPosition.y, mapped.y, 3);
            AgentSystem.Step();
            Assert.False(agent.pathPending);
            Assert.True(agent.hasPath);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.Equal(mapped, agent.pathEndPosition);
        }

        [Fact]
        public void ATargetBeyondTheAgentsReachIsRefusedAndChangesNothing()
        {
            var agent = Walking(3);
            var destination = agent.destination;
            var end = agent.pathEndPosition;
            // 12 m beyond the edge: more than 20 radii of 0.5 m.
            Assert.False(agent.SetDestination(Center + new Vector3(-4f, 0f, -18f)));
            Assert.False(agent.pathPending);
            Assert.True(agent.hasPath);
            Assert.Equal(destination, agent.destination);
            Assert.Equal(end, agent.pathEndPosition);
            // 8 m above the ground: more than 15 radii.
            Assert.False(agent.SetDestination(Center + new Vector3(0f, 8f, -4f)));
            Assert.True(agent.SetDestination(Center + new Vector3(0f, 7f, -4f)));
            // A larger agent reaches farther (its own radius counts).
            agent.radius = 1f;
            Assert.True(agent.SetDestination(Center + new Vector3(-4f, 0f, -18f)));
        }

        /// <summary>While a new path is searched for, the agent goes on following its current path
        /// and reports it (measured: tests/projects/navlinks pendingold, offedge).</summary>
        [Fact]
        public void APendingPathKeepsReportingTheCurrentOne()
        {
            var agent = Walking(5);
            var corners = agent.path.corners;
            float remaining = agent.remainingDistance;
            var end = agent.pathEndPosition;
            // Behind the agent: it needs a search.
            Assert.True(agent.SetDestination(Center + new Vector3(-5f, 0f, -3f)));
            Assert.True(agent.pathPending);
            Assert.True(agent.hasPath);
            Assert.Equal(end, agent.pathEndPosition);
            Assert.Equal(remaining, agent.remainingDistance);
            Assert.Equal(corners, agent.path.corners);
            Assert.Equal(Center.x - 5f, agent.destination.x, 3);
            AgentSystem.Step();
            Assert.False(agent.pathPending);
            Assert.Equal(agent.destination, agent.pathEndPosition);
        }

        /// <summary>The centre of a polygon.</summary>
        static Vector3 PointOn(NavMeshAgent agent, long polyRef)
        {
            var mesh = agent.m_Surface.Query.GetAttachedNavMesh();
            mesh.GetTileAndPolyByRefUnsafe(polyRef, out var tile, out var poly);
            var c = Vector3.zero;
            for (int i = 0; i < poly.vertCount; i++)
                c += new Vector3(tile.data.verts[poly.verts[i] * 3], tile.data.verts[poly.verts[i] * 3 + 1], tile.data.verts[poly.verts[i] * 3 + 2]);
            return c / poly.vertCount;
        }

        [Fact]
        public void ADestinationOnTheAgentsPolygonHasItsPathAtOnce()
        {
            var agent = Agent(new Vector3(-5f, 0f, 4f));
            var target = Center + new Vector3(-4.8f, 0f, 4f);
            Assert.Equal(agent.m_Crowd.corridor.GetFirstPoly(), PolygonOf(agent, target));
            Assert.True(agent.SetDestination(target));
            Assert.False(agent.pathPending);
            // The path is there (its end, its corners), but hasPath counts it from the next agent
            // update (measured: tests/projects/navlinks targets, incall).
            Assert.False(agent.hasPath);
            Assert.True(Vector2.Distance(new Vector2(target.x, target.z), new Vector2(agent.pathEndPosition.x, agent.pathEndPosition.z)) < 1e-3f);
            Assert.Equal(2, agent.path.corners.Length);
            // Nothing steers until the agent update.
            Assert.Equal(Vector3.zero, agent.desiredVelocity);
            AgentSystem.Step();
            Assert.True(agent.hasPath);
        }

        [Fact]
        public void ADestinationNeedingASearchWaitsForTheAgentUpdate()
        {
            var agent = Agent(new Vector3(-5f, 0f, 0f));
            var target = Center + new Vector3(5f, 0f, 0f);
            Assert.NotEqual(agent.m_Crowd.corridor.GetFirstPoly(), PolygonOf(agent, target));
            Assert.True(agent.SetDestination(target));
            Assert.True(agent.pathPending);
            AgentSystem.Step();
            Assert.False(agent.pathPending);
            // The update that finds the path plans with it but does not move yet.
            Assert.Equal(agent.speed, agent.desiredVelocity.magnitude, 3);
            Assert.Equal(Vector3.zero, agent.velocity);
            AgentSystem.Step();
            Assert.Equal(agent.acceleration * Dt, agent.velocity.magnitude, 3);
        }

        [Fact]
        public void AWalkingAgentsNewDestinationOnItsPathIsAppliedAtOnceAndOneBehindItWaits()
        {
            var ahead = Walking(5);
            Assert.True(ahead.SetDestination(Center + new Vector3(4f, 0f, -4f)));
            Assert.False(ahead.pathPending);
            Assert.Equal(Center.x + 4f - ahead.nextPosition.x, ahead.remainingDistance, 2);

            // Its own lane, clear of the first agent.
            var behind = Walking(5, 4f);
            Assert.True(behind.SetDestination(Center + new Vector3(-5.3f, 0f, 4f)));
            Assert.True(behind.pathPending);
            AgentSystem.Step();
            Assert.False(behind.pathPending);
            Assert.True(behind.desiredVelocity.x < 0f);
        }

        [Fact]
        public void AnAgentBeginsToTurnInTheUpdateThatPlansItsFirstStep()
        {
            // Straight along z = -4, clear of the hole.
            var agent = Agent(new Vector3(-5f, 0f, -4f));
            Assert.True(agent.SetDestination(Center + new Vector3(5f, 0f, -4f)));
            AgentSystem.Step();
            Assert.Equal(Vector3.zero, agent.velocity);
            // (1 + the next step's speed) times the 90 degrees left, per second.
            float expected = (1f + agent.acceleration * Dt) * 90f * Dt;
            Assert.Equal(expected, agent.transform.eulerAngles.y, 3);
            // Never more than angularSpeed.
            agent.angularSpeed = 30f;
            float before = agent.transform.eulerAngles.y;
            AgentSystem.Step();
            Assert.Equal(before + 30f * Dt, agent.transform.eulerAngles.y, 3);
        }

        [Fact]
        public void AStoppedAgentSlowsDownAtItsAcceleration()
        {
            var agent = Walking(10);
            float speed = agent.velocity.magnitude;
            agent.isStopped = true;
            Assert.Equal(speed, agent.velocity.magnitude, 4);
            // The step already planned is taken, then the agent slows down.
            AgentSystem.Step();
            Assert.Equal(speed + agent.acceleration * Dt, agent.velocity.magnitude, 3);
            AgentSystem.Step();
            Assert.Equal(speed, agent.velocity.magnitude, 3);
        }

        [Fact]
        public void AVelocitySetByScriptIsReportedFromTheNextUpdate()
        {
            var agent = Walking(10);
            var reported = agent.velocity;
            var p = agent.nextPosition;
            agent.velocity = new Vector3(0f, 0f, 2f);
            Assert.Equal(reported, agent.velocity);
            AgentSystem.Step();
            Assert.True((agent.velocity - new Vector3(0f, 0f, 2f)).magnitude < 1e-4f, $"velocity {agent.velocity}");
            Assert.Equal(p.z + 2f * Dt, agent.nextPosition.z, 4);
        }

        [Fact]
        public void WarpStopsTheAgentAtOnce()
        {
            var agent = Walking(10);
            Assert.NotEqual(Vector3.zero, agent.velocity);
            Assert.True(agent.Warp(Center + new Vector3(-5f, 0f, 4f)));
            Assert.Equal(Vector3.zero, agent.velocity);
            Assert.Equal(Vector3.zero, agent.desiredVelocity);
        }

        [Fact]
        public void SettingNextPositionMovesTheTransformAtOnce()
        {
            var agent = Walking(5);
            var moved = agent.nextPosition + new Vector3(0f, 0f, 0.4f);
            agent.nextPosition = moved;
            Assert.Equal(moved.z, agent.transform.position.z, 4);
            // With updatePosition off the transform stays.
            agent.updatePosition = false;
            var transform = agent.transform.position;
            agent.nextPosition = moved + new Vector3(0f, 0f, 0.4f);
            Assert.Equal(transform, agent.transform.position);
        }
    }
}
