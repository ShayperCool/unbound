using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Agents shortening their corridor while they walk, as Unity 6000.0.71f1's agents do
    /// (tests/projects/navpath `replan.*`, `place.moved` walk, `carve.shortcut`): a shortcut towards
    /// the next corners at every update, and a short path search of one agent's corridor per update.
    /// The level is a 16 m floor whose middle is a strip of water (area 3) 2 m wide and 12 m long;
    /// the agent update runs at a fixed 0.02 s step.</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentReplanTests : IDisposable
    {
        const float Dt = 0.02f;
        const int Water = 3;

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshAgentReplanTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var sources = new List<NavMeshBuildSource>
            {
                Box(new Vector3(0f, -0.05f, 0f), new Vector3(16f, 0.1f, 16f), 0),
                Box(new Vector3(0.037f, -0.05f, -2.447f), new Vector3(2f, 0.1f, 12f), Water),
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f)), Vector3.zero, Quaternion.identity);
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

        static NavMeshBuildSource Box(Vector3 center, Vector3 size, int area) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.Translate(center),
            area = area,
        };

        /// <summary>An agent without avoidance, given the path round the water's north end.</summary>
        NavMeshAgent Walker(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(from, to, NavMesh.AllAreas & ~(1 << Water), path));
            Assert.True(path.corners.Length > 2);
            var go = new GameObject("walker");
            m_Objects.Add(go);
            go.transform.position = from;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.SetAreaCost(Water, 1f);
            Assert.True(agent.SetPath(path));
            return agent;
        }

        /// <summary>Unity's corridor merge: the corridor polygon farthest along that the visited
        /// polygons hold, with the last of the visited copies; the visited polygons before it replace
        /// the corridor's before it, also when it is the first visited polygon (the corridor's start
        /// is then dropped).</summary>
        [Fact]
        public void TheMergeKeepsTheCorridorFromItsFarthestVisitedPolygon()
        {
            Span<long> path = stackalloc long[8] { 1, 2, 3, 4, 5, 0, 0, 0 };
            int n = DtPathUtils.MergeCorridorStartShortcut(path, 5, 8, new long[] { 1, 9, 4, 4 }, 4);
            Assert.Equal(new long[] { 1, 9, 4, 4, 5 }, path.Slice(0, n).ToArray());

            Span<long> loop = stackalloc long[4] { 1, 2, 3, 0 };
            n = DtPathUtils.MergeCorridorStartShortcut(loop, 3, 4, new long[] { 3, 7 }, 2);
            Assert.Equal(new long[] { 3 }, loop.Slice(0, n).ToArray());

            Span<long> none = stackalloc long[3] { 1, 2, 3 };
            Assert.Equal(3, DtPathUtils.MergeCorridorStartShortcut(none, 3, 3, new long[] { 8, 9 }, 2));
        }

        /// <summary>A ray whose polygon list is full goes on: where it stops and what it costs do not
        /// depend on how many polygons it records (Unity's).</summary>
        [Fact]
        public void ARayGoesOnPastAFullPolygonList()
        {
            var surface = NavWorld.Get(0);
            var filter = AreaFilter.Of(NavMesh.AllAreas);
            var from = new RcVec3f(-6f, 0.1f, -5f);
            var to = new RcVec3f(6f, 0.1f, -4f);
            Assert.True(NavMesh.Nearest(surface, NavMesh.V(from), new RcVec3f(1f, 1f, 1f), filter, out var start, out _));
            var many = new long[64];
            var all = new DtRaycastHit { path = many, maxPath = many.Length };
            surface.Query.Raycast(start, from, to, filter, DtRaycastOptions.UNBOUND_RAYCAST_STRAIGHT_COSTS, ref all, 0);
            Assert.True(all.pathCount > 2, $"{all.pathCount} polygons");
            Assert.Equal(float.MaxValue, all.t);
            // The straight ray costs its length (every area here costs 1).
            Assert.Equal(RcVec3f.Distance(from, to), all.pathCost, 3);

            var one = new long[1];
            var first = new DtRaycastHit { path = one, maxPath = 1 };
            var status = surface.Query.Raycast(start, from, to, filter, DtRaycastOptions.UNBOUND_RAYCAST_STRAIGHT_COSTS, ref first, 0);
            Assert.True(status.Has(DtStatus.DT_BUFFER_TOO_SMALL));
            Assert.Equal(1, first.pathCount);
            Assert.Equal(all.t, first.t);
            Assert.Equal(all.pathCost, first.pathCost, 5);
            Assert.Equal(many[all.pathCount - 1], first.lastRef);
        }

        /// <summary>A walker given the path round the water shortcuts across it when it costs it 1,
        /// keeps going round when the water costs it 10 (its own area cost weighs the straight way).
        /// The two start far apart, east of the strip (outside the probe's setup).</summary>
        [Fact]
        public void TheShortcutWeighsTheAgentsOwnAreaCosts()
        {
            var cheap = Walker(new Vector3(4f, 0f, -6f), new Vector3(-4f, 0f, -6f));
            var dear = Walker(new Vector3(4f, 0f, -2f), new Vector3(-4f, 0f, -2f));
            dear.SetAreaCost(Water, 10f);
            for (int i = 0; i < 100; i++) AgentSystem.Step();

            // Two seconds on: the cheap walker has crossed to the west side low down, the dear one is
            // still heading north round the strip's end.
            Assert.True(cheap.nextPosition.x < 0f, $"cheap at {cheap.nextPosition}");
            Assert.True(cheap.nextPosition.z < -4f, $"cheap at {cheap.nextPosition}");
            Assert.True(dear.nextPosition.x > 1f, $"dear at {dear.nextPosition}");
            Assert.True(dear.nextPosition.z > 1f, $"dear at {dear.nextPosition}");
        }

        /// <summary>remainingDistance is the length along the corners from where the agent stands,
        /// also in the update whose step passes a corner (the corners it steered by still hold
        /// that corner then).</summary>
        [Fact]
        public void TheRemainingDistanceFollowsTheCornersFromWhereTheAgentStands()
        {
            var agent = Walker(new Vector3(-4f, 0f, -5f), new Vector3(4f, 0f, -5f));
            agent.SetAreaCost(Water, 10f);
            int passed = 0, last = agent.path.corners.Length;
            for (int i = 0; i < 300 && agent.hasPath; i++)
            {
                AgentSystem.Step();
                var corners = agent.path.corners;
                float along = 0f;
                for (int c = 1; c < corners.Length; c++) along += Vector3.Distance(corners[c - 1], corners[c]);
                Assert.Equal(along, agent.remainingDistance, 3);
                if (corners.Length < last) passed++;
                last = corners.Length;
            }
            Assert.True(passed >= 2, $"{passed} corners passed");
        }

        /// <summary>Slots: an agent takes the slot freed last; of agents that waited as long for a
        /// corridor search, the one in the lower slot is searched in one update, the other in the
        /// next.</summary>
        [Fact]
        public void OneCorridorIsSearchedPerUpdateInSlotOrder()
        {
            var first = Walker(new Vector3(-6f, 0f, -6f), new Vector3(6f, 0f, -6f));
            var second = Walker(new Vector3(-6f, 0f, -2f), new Vector3(6f, 0f, -2f));
            int firstSlot = first.m_CrowdSlot, secondSlot = second.m_CrowdSlot;
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);
            var a = Walker(new Vector3(-4f, 0f, -6f), new Vector3(4f, 0f, -6f));
            var b = Walker(new Vector3(-4f, 0f, -4f), new Vector3(4f, 0f, -4f));
            Assert.Equal(secondSlot, a.m_CrowdSlot);
            Assert.Equal(firstSlot, b.m_CrowdSlot);
            var lower = a.m_CrowdSlot < b.m_CrowdSlot ? a : b;
            var higher = lower == a ? b : a;

            int searched = -1;
            for (int i = 1; i <= 40 && searched < 0; i++)
            {
                AgentSystem.Step();
                if (lower.m_Crowd.topologyOptTime == 0f) searched = i;
            }
            Assert.InRange(searched, 25, 27);
            Assert.True(higher.m_Crowd.topologyOptTime > 0.5f);
            AgentSystem.Step();
            Assert.Equal(0f, higher.m_Crowd.topologyOptTime);
            Assert.True(lower.m_Crowd.topologyOptTime <= Dt);
        }
    }
}
