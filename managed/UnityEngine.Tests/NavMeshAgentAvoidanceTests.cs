using System;
using System.Collections.Generic;
using System.Linq;
using DotRecast.Core.Numerics;
using DotRecast.Detour.Crowd;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The agents' local avoidance as Unity 6000.0.71f1's agents avoid (tests/projects/avoidance,
    /// navcost `loop.redirect*`, navlinks `fast`). Documented (Scripting API 6000.0 NavMeshAgent:
    /// obstacleAvoidanceType, avoidancePriority, desiredVelocity "including any potential contribution
    /// from avoidance"): agents avoid other agents and obstacles, more important ones (lower
    /// avoidancePriority) fully. Measured: the sampling (candidates, weights, the desired velocity as
    /// the first candidate), which neighbours and NavMesh edges count, and when.
    /// The level is one 20 m by 10 m platform; the agent update runs at a fixed 0.02 s step.</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentAvoidanceTests : IDisposable
    {
        const float Dt = 0.02f;

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshAgentAvoidanceTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
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
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            NavMesh.RemoveNavMeshData(m_Data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        NavMeshAgent Agent(Vector3 start, Vector3? goal)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = start;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            if (goal.HasValue) Assert.True(agent.SetDestination(goal.Value));
            return agent;
        }

        static DtObstacleAvoidanceParams HighQuality() => new DtObstacleAvoidanceParams
        {
            horizTime = 2f,
            adaptiveDivs = 7,
            adaptiveRings = 3,
            adaptiveDepth = 3,
        };

        /// <summary>navlinks `fast`, the first update: an agent of speed 8 standing 2.5 m from the
        /// west edge of its platform and 6.33 m from the east edge it heads to (the north edge 2.5 m
        /// to its left, the south edge 2.33 m to its right) plans (6.232304, 0, -0.279492), Unity's
        /// desiredVelocity in that update.</summary>
        [Fact]
        public void TheSamplesWeighAsUnitysAgentsDo()
        {
            var query = new DtObstacleAvoidanceQuery(8, 8);
            void Edge(float px, float pz, float qx, float qz) => query.AddSegment(new RcVec3f(px, 0f, pz), new RcVec3f(qx, 0f, qz));
            Edge(6.333336f, -2.3333333f, -2.5f, -2.3333333f);
            Edge(-2.5f, 2.5f, 6.333336f, 2.5f);
            Edge(-2.5f, 0f, -2.5f, 2.5f);
            Edge(-2.5f, -2.3333333f, -2.5f, 0f);
            Edge(6.333336f, 0f, 6.333336f, -2.3333333f);
            Edge(6.333336f, 2.5f, 6.333336f, 0f);
            Assert.Equal(6, query.GetObstacleSegmentCount());
            query.SampleVelocityAdaptive(8f, RcVec3f.Zero, new RcVec3f(8f, 0f, 0f), out var nvel, HighQuality(), null);
            Assert.Equal(6.232304f, nvel.X, 4);
            Assert.Equal(-0.279492f, nvel.Z, 4);
            Assert.Equal(0f, nvel.Y);
        }

        [Fact]
        public void WithNothingToAvoidTheDesiredVelocityIsKeptWithItsHeight()
        {
            var query = new DtObstacleAvoidanceQuery(8, 8);
            var desired = new RcVec3f(1f, 0.5f, 2f);
            query.SampleVelocityAdaptive(3.5f, new RcVec3f(0.3f, 0f, 0.1f), desired, out var nvel, HighQuality(), null);
            Assert.Equal(desired, nvel);
        }

        /// <summary>The desired velocity is the first candidate: when it meets nothing within the
        /// prediction time it wins (its height dropped, as every sampled velocity's), whatever the
        /// current velocity.</summary>
        [Fact]
        public void AFreeDesiredVelocityIsTheSampledOne()
        {
            var query = new DtObstacleAvoidanceQuery(8, 8);
            // An edge alongside, 1 m to the left, and a neighbour 3 m to the right walking the same way.
            query.AddSegment(new RcVec3f(10f, 0f, 1f), new RcVec3f(-10f, 0f, 1f));
            query.AddCircle(new RcVec3f(0f, 0f, -3f), 1f, new RcVec3f(2f, 0f, 0f), 0.5f);
            query.SampleVelocityAdaptive(3.5f, new RcVec3f(-1f, 0f, 0.5f), new RcVec3f(3f, 0.2f, 0.4f), out var nvel, HighQuality(), null);
            Assert.Equal(new RcVec3f(3f, 0f, 0.4f), nvel);
        }

        /// <summary>An edge the agent stands behind does not count; one it touches counts in front
        /// of it.</summary>
        [Fact]
        public void EdgesCountFromTheirWalkableSide()
        {
            var query = new DtObstacleAvoidanceQuery(8, 8);
            // Walkable side towards the agent (as the NavMesh's edges around it): counts.
            query.AddSegment(new RcVec3f(-1f, 0f, 2f), new RcVec3f(1f, 0f, 2f));
            Assert.Equal(1, query.GetObstacleSegmentCount());
            // The same edge the other way round: the agent is behind it.
            query.AddSegment(new RcVec3f(1f, 0f, 2f), new RcVec3f(-1f, 0f, 2f));
            Assert.Equal(1, query.GetObstacleSegmentCount());
            // Behind but touching: counts, moved to 0.025 m from the agent on its outer side.
            query.AddSegment(new RcVec3f(1f, 0f, -0.01f), new RcVec3f(-1f, 0f, -0.01f));
            Assert.Equal(2, query.GetObstacleSegmentCount());
            var touching = query.GetObstacleSegment(1);
            Assert.Equal(-0.025f, touching.p.Z, 5);
            Assert.Equal(-0.025f, touching.q.Z, 5);
        }

        /// <summary>A walking agent's neighbours are those it could reach within the prediction time
        /// and not behind it along the way it goes; a standing agent sees every one around.</summary>
        [Fact]
        public void NeighboursBehindAWalkingAgentDoNotCount()
        {
            var walker = Agent(new Vector3(-4f, 0f, 0f), new Vector3(8f, 0f, 0f));
            var behind = Agent(new Vector3(-7f, 0f, 0f), null);
            var ahead = Agent(new Vector3(-1f, 0f, 2f), null);
            var less = Agent(new Vector3(-1f, 0f, -2f), null);
            less.avoidancePriority = walker.avoidancePriority + 1;
            for (int i = 0; i < 3; i++) AgentSystem.Step();

            var seen = Neighbours(walker);
            Assert.DoesNotContain(behind.m_Crowd, seen);
            Assert.Contains(ahead.m_Crowd, seen);
            // Less important agents are left to avoid the walker.
            Assert.DoesNotContain(less.m_Crowd, seen);
            Assert.Contains(walker.m_Crowd, Neighbours(behind));
        }

        /// <summary>An agent without avoidance gathers no neighbours: it avoids nobody and is not
        /// pushed out of overlaps, while an agent with avoidance still meets it and is pushed
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/navpath `replan.*`, `replan.none`); a stopped agent
        /// still sees its neighbours.</summary>
        [Fact]
        public void AgentsWithoutAvoidanceMeetNobodyAndStoppedAgentsStillDo()
        {
            var plain = Agent(new Vector3(0f, 0f, 0f), null);
            plain.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            var other = Agent(new Vector3(0.3f, 0f, 0f), null);
            var stopped = Agent(new Vector3(-3f, 0f, 2f), new Vector3(8f, 0f, 2f));
            stopped.isStopped = true;
            var near = Agent(new Vector3(-1.8f, 0f, 2f), null);
            for (int i = 0; i < 10; i++) AgentSystem.Step();

            Assert.Empty(Neighbours(plain));
            Assert.InRange(plain.nextPosition.x, -1e-4f, 1e-4f);
            Assert.Contains(plain.m_Crowd, Neighbours(other));
            Assert.True(other.nextPosition.x > 0.35f, $"pushed to {other.nextPosition}");
            Assert.Equal(0f, plain.desiredVelocity.magnitude, 4);
            Assert.Contains(near.m_Crowd, Neighbours(stopped));
        }

        [Theory]
        [InlineData("6000.0.71f1", -5f, 1f, true)]
        [InlineData("6000.0.71f1", 5f, 1f, false)]
        [InlineData("6000.0.71f1", -5f, -1f, true)]
        [InlineData("6000.0.72f1", -5f, 1f, false)]
        public void PredictedNeighbourTravelCanEnterTheQueryBeforeItsPosition(string release, float velocity, float direction, bool expected)
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = release;
            try
            {
                var observer = Agent(Vector3.zero, new Vector3(8f * direction, 0f, 0f));
                observer.speed = 1f;
                var neighbour = Agent(new Vector3(8f * direction, 0f, 2f), null);
                neighbour.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                neighbour.avoidancePriority = 40;
                neighbour.acceleration = 0f;
                neighbour.velocity = new Vector3(velocity * direction, 0f, 0f);
                AgentSystem.Step();
                Assert.Equal(expected, Neighbours(observer).Contains(neighbour.m_Crowd));
            }
            finally { ProjectInfo.UnityVersion = previous; }
        }

        [Theory]
        [InlineData(float.NaN, 0)]
        [InlineData(float.PositiveInfinity, 1)]
        [InlineData(float.NegativeInfinity, 2)]
        public void NonfiniteAssignmentsKeepThePreviousVelocityAndRadius(float invalid, int axis)
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            try
            {
                var agent = Agent(Vector3.zero, new Vector3(8f, 0f, 0f));
                agent.radius = 0.75f;
                var velocity = new Vector3(1.2f, 0f, 0.3f);
                agent.velocity = velocity;
                var invalidVelocity = velocity;
                invalidVelocity[axis] = invalid;
                agent.velocity = invalidVelocity;
                agent.radius = invalid;
                Assert.Equal(NavMesh.V(velocity), agent.m_Crowd.vel);
                Assert.Equal(NavMesh.V(velocity), agent.m_Crowd.pvel);
                Assert.Equal(0.75f, agent.radius);
                Assert.Equal(0.75f, agent.m_Crowd.option.radius);
            }
            finally { ProjectInfo.UnityVersion = previous; }
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.MaxValue)]
        public void InvalidOrOverflowingStoredPredictionsKeepPointInsertion(float speed)
        {
            var position = new RcVec3f(3.25f, 0f, 2.25f);
            DtCrowd.PredictionBounds(position, new RcVec3f(speed, 0f, 0f), 0.5f, 2f, out var min, out var max);
            var grid = new DtProximityGrid(1f);
            var neighbour = new DtCrowdAgent(42);
            grid.AddItem(neighbour, min.X, min.Z, max.X, max.Z);
            Span<int> found = stackalloc int[2];
            int count = grid.QueryItems(3f, 2f, 3.5f, 2.5f, found, found.Length);
            Assert.Equal(1, count);
            Assert.Equal(neighbour.idx, found[0]);
        }

        [Fact]
        public void AnOverflowingStoredRadiusKeepsPointInsertion()
        {
            var position = new RcVec3f(float.MaxValue, 0f, 2.25f);
            DtCrowd.PredictionBounds(position, RcVec3f.Zero, float.MaxValue, 2f, out var min, out var max);
            Assert.Equal(position, min);
            Assert.Equal(position, max);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(25)]
        [InlineData(100)]
        public void WideGridBoundsRetainCellOrderAndCandidateLimit(int reach)
        {
            var grid = new DtProximityGrid(1f);
            var rectangles = new List<(int id, int order, int x0, int z0, int x1, int z1)>();
            var random = new System.Random(715);
            for (int i = 0; i < 200; i++)
            {
                int x = random.Next(-20, 20), z = random.Next(-20, 20);
                int width = i == 73 ? 10000 : random.Next(1, 5);
                rectangles.Add((1000 - i, i, x - width, z - width, x + width, z + width));
                grid.AddItem(new DtCrowdAgent(1000 - i), x - width, z - width, x + width, z + width);
            }
            var expected = rectangles.Where(r => r.x0 <= reach && r.z0 <= reach && r.x1 >= -reach && r.z1 >= -reach)
                .OrderBy(r => Math.Max(r.z0, -reach)).ThenBy(r => Math.Max(r.x0, -reach))
                .ThenByDescending(r => r.order).Take(64).Select(r => r.id).ToArray();
            Span<int> found = stackalloc int[64];
            int count = grid.QueryItems(-reach, -reach, reach, reach, found, found.Length);
            Assert.Equal(expected, found.Slice(0, count).ToArray());
            Assert.Equal(200, grid.StoredItemCount);
            grid.Clear();
            grid.AddItem(new DtCrowdAgent(7), 0f, 0f, 0f, 0f);
            count = grid.QueryItems(-reach, -reach, reach, reach, found, found.Length);
            Assert.Equal(1, count);
            Assert.Equal(7, found[0]);
        }

        [Fact]
        public void WideGridRebuildsReuseTheirStorage()
        {
            var grid = new DtProximityGrid(1f);
            var agents = Enumerable.Range(0, 200).Select(i => new DtCrowdAgent(i)).ToArray();
            Span<int> found = stackalloc int[64];
            long allocated = 0;
            int count = 0;
            for (int repeat = 0; repeat < 20; repeat++)
            {
                grid.Clear();
                for (int i = 0; i < agents.Length; i++)
                    grid.AddItem(agents[i], -10000f, -10000f, 10000f, 10000f);
                count = grid.QueryItems(-1f, -1f, 1f, 1f, found, found.Length);
                if (repeat == 1) allocated = GC.GetAllocatedBytesForCurrentThread();
            }
            long delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.Equal(0, delta);
            Assert.Equal(64, count);
            Assert.Equal(199, found[0]);
            Assert.Equal(136, found[63]);
        }

        [Theory]
        [InlineData(float.MaxValue)]
        [InlineData(float.MinValue)]
        public void ExtremeGridCoordinatesDoNotWrapCellTraversal(float coordinate)
        {
            var grid = new DtProximityGrid(1f);
            grid.AddItem(new DtCrowdAgent(3), coordinate, coordinate, coordinate, coordinate);
            Span<int> found = stackalloc int[2];
            Assert.Equal(1, grid.QueryItems(coordinate, coordinate, coordinate, coordinate, found, found.Length));
            Assert.Equal(3, found[0]);
        }

        static List<DtCrowdAgent> Neighbours(NavMeshAgent agent)
        {
            var list = new List<DtCrowdAgent>();
            for (int i = 0; i < agent.m_Crowd.nneis; i++) list.Add(agent.m_Crowd.neis[i].agent);
            return list;
        }

        /// <summary>navcost `loop.redirect`: a walking agent given a destination just behind it, by
        /// the NavMesh's edge, plans a slow turn in the update that gives it the path (the edge
        /// behind it counts while the corner it last steered by is far) and its whole desired
        /// velocity from the next (the end of its path is near: no edges count).</summary>
        [Fact]
        public void AReversalByAnEdgeSlowsForOneUpdate()
        {
            var agent = Agent(new Vector3(-8.5f, 0f, 0f), new Vector3(8f, 0f, 0f));
            for (int i = 0; i < 6; i++) AgentSystem.Step();
            Assert.True(agent.velocity.x > 0.5f);

            Assert.True(agent.SetDestination(new Vector3(-9.3f, 0f, 0f)));
            AgentSystem.Step();
            Assert.False(agent.pathPending);
            var turning = agent.desiredVelocity;
            Assert.InRange(turning.x, -1.75f, -0.1f);

            AgentSystem.Step();
            var back = agent.desiredVelocity;
            Assert.Equal(-agent.speed, back.x, 4);
            Assert.Equal(0f, back.z, 4);
        }

        /// <summary>navlinks `incall`: a path given to a walking agent between updates shows in its
        /// steeringTarget from the next update; until then it keeps the corner it steered by.</summary>
        [Fact]
        public void AWalkingAgentsSteeringTargetChangesAtItsNextUpdate()
        {
            var agent = Agent(new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f));
            for (int i = 0; i < 3; i++) AgentSystem.Step();
            var before = agent.steeringTarget;
            Assert.Equal(NavMesh.V(agent.m_Crowd.corners[0].pos), before);
            Assert.True(before.x > -2f);

            // On the rest of its path, before its next corner: given in the call.
            Assert.True(agent.SetDestination(new Vector3(-3f, 0f, 0f)));
            Assert.False(agent.pathPending);
            Assert.Equal(before, agent.steeringTarget);

            AgentSystem.Step();
            Assert.Equal(-3f, agent.steeringTarget.x, 3);
        }
    }
}
