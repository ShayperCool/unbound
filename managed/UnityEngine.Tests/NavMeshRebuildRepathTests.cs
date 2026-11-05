using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NavMeshAgent paths across NavMesh changes, and NavMesh area costs on agents.
    /// Documented (Scripting API 6000.0): a link enabled or disabled makes the path stale
    /// (isPathStale); pathPending is true while a path is being computed; NavMesh.SetAreaCost sets
    /// an area's cost on all agents, replacing their custom costs, and is the default for new
    /// agents; enabling or disabling an agent resets its costs to the defaults
    /// (NavMeshAgent.SetAreaCost). Observed, not documented (navlinks capture
    /// tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl, committed at 982cee91, not
    /// admitted): walking agents keep hasPath true and pathPending false, at speed, through link
    /// toggles. Unbound's choices (unmeasured) are marked so: the path is planned again at once when
    /// the NavMesh takes in the change, with no pathPending, the velocity kept and autoRepath not
    /// consulted; NavMesh.SetAreaCost replaces one area's custom cost, on every agent with custom
    /// costs, enabled or not, and makes the path of an agent whose cost it changes stale; costs are
    /// reset when the agent is enabled and when it is disabled.
    /// The level is two 6 m platforms with a 4 m gap that only a link from x = -3 to x = 3 crosses,
    /// one NavMeshData for both. Unbound takes in NavMesh changes at the next NavMesh query, so the
    /// tests make one. The agent update runs directly at a fixed 0.02 s step, so this shares the
    /// frame-stepping collection (the global clock).</summary>
    [Collection("Frames")]
    public sealed class NavMeshRebuildRepathTests : IDisposable
    {
        const float Dt = 0.02f;
        /// <summary>Areas no polygon uses; their costs are restored afterwards.</summary>
        const int SpareArea = 3;
        const int OtherArea = 4;
        const int ThirdArea = 5;
        static readonly int[] s_Areas = { SpareArea, OtherArea, ThirdArea };
        static readonly Vector3 LinkStart = new Vector3(-3f, 0f, 0f);
        static readonly Vector3 LinkEnd = new Vector3(3f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float[] m_Costs = new float[s_Areas.Length];
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();
        readonly NavMeshLinkInstance m_Link;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshRebuildRepathTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            for (int i = 0; i < s_Areas.Length; i++) m_Costs[i] = NavMesh.GetAreaCost(s_Areas[i]);
            m_Data.Add(AddData(new Bounds(Vector3.zero, new Vector3(20f, 4f, 10f)), Platform(new Vector3(-5f, 0f, 0f)), Platform(new Vector3(5f, 0f, 0f))));
            m_Link = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = LinkStart,
                endPosition = LinkEnd,
                bidirectional = true,
                costModifier = -1f,
                width = 0f,
                area = 0,
                agentTypeID = 0,
            });
        }

        public void Dispose()
        {
            foreach (var go in m_Objects) Object.DestroyImmediate(go);
            NavMesh.RemoveLink(m_Link);
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
            for (int i = 0; i < s_Areas.Length; i++) NavMesh.SetAreaCost(s_Areas[i], m_Costs[i]);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        static NavMeshBuildSource Platform(Vector3 center) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(6f, 1f, 4f),
            transform = Matrix4x4.Translate(center + new Vector3(0f, -0.5f, 0f)),
            area = 0,
        };

        static NavMeshDataInstance AddData(Bounds bounds, params NavMeshBuildSource[] sources)
        {
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource>(sources),
                bounds, Vector3.zero, Quaternion.identity);
            return NavMesh.AddNavMeshData(data);
        }

        /// <summary>A second NavMeshData, apart from the level (nothing walks there).</summary>
        NavMeshDataInstance AddFarData()
        {
            var far = AddData(new Bounds(new Vector3(0f, 0f, 20f), new Vector3(10f, 4f, 10f)), Platform(new Vector3(0f, 0f, 20f)));
            m_Data.Add(far);
            return far;
        }

        NavMeshAgent Agent(Vector3 at, bool autoTraverse = true)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = at;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.autoTraverseOffMeshLink = autoTraverse;
            return agent;
        }

        static void Step() => AgentSystem.Step();

        /// <summary>A NavMesh query: the NavMesh takes in the changes made since the last one.</summary>
        static void RefreshNavMesh() => NavMesh.SamplePosition(LinkEnd, out _, 1f, NavMesh.AllAreas);

        static float Dist2D(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static Vector3 Goal(float z) => new Vector3(-3f, 0f, z);

        /// <summary>An agent on the left platform walking along it (no link) to <see cref="Goal"/>,
        /// under way: its path applied by one update and ten more updates of motion.</summary>
        NavMeshAgent WalkingAgent(float z = 1f, bool autoRepath = true)
        {
            var agent = Agent(new Vector3(-7f, 0f, z));
            agent.autoRepath = autoRepath;
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(Goal(z)));
            for (int i = 0; i < 11; i++) Step();
            Assert.True(agent.hasPath);
            Assert.False(agent.pathPending);
            Assert.True(agent.velocity.magnitude > 1f, $"not under way: {agent.velocity}");
            return agent;
        }

        /// <summary>After the NavMesh took in a change: the agent still has its path to its
        /// destination, reports no pending path, keeps its speed, and walks on to the
        /// destination.</summary>
        static void GoesOn(NavMeshAgent agent, Vector3 goal)
        {
            float speed = agent.velocity.magnitude;
            var before = agent.nextPosition;
            // The NavMesh agents rejoin it (the rebuild) at the agent's next access.
            Assert.True(agent.isOnNavMesh);
            // Planned again at once, no pathPending (Unbound's choice, as the capture's walking
            // agents report).
            Assert.True(agent.hasPath);
            Assert.False(agent.pathPending);
            Assert.Equal(goal, agent.destination);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Step();
            Assert.True(agent.hasPath);
            Assert.False(agent.pathPending);
            Assert.True(agent.nextPosition.x > before.x, $"stopped at {agent.nextPosition}");
            // The velocity is kept (Unbound's choice; the capture's walking agents keep their speed).
            Assert.True(agent.velocity.magnitude >= 0.9f * speed, $"slowed from {speed} to {agent.velocity.magnitude}");
            for (int i = 0; i < 300 && Dist2D(agent.nextPosition, goal) > 1e-2f; i++) Step();
            Assert.True(Dist2D(agent.nextPosition, goal) <= 1e-2f, $"stopped at {agent.nextPosition}");
        }

        // ------------------------------------------------------------------ rebuilds

        [Fact]
        public void AfterALinkIsDisabledTheAgentGoesOnToItsDestination()
        {
            var agent = WalkingAgent();
            NavMesh.SetLinkActive(m_Link, false);
            // Documented.
            Assert.True(agent.isPathStale);
            RefreshNavMesh();
            GoesOn(agent, Goal(1f));
        }

        [Fact]
        public void TheNewPathIsNotStale()
        {
            // The path planned again is a newly applied path, which clears the flag (Unbound's
            // choice, as for any newly applied path; unmeasured).
            var agent = WalkingAgent();
            NavMesh.SetLinkActive(m_Link, false);
            Assert.True(agent.isPathStale);
            RefreshNavMesh();
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.hasPath);
            Assert.False(agent.isPathStale);
        }

        [Fact]
        public void AfterALinkIsAddedTheAgentGoesOnToItsDestination()
        {
            var agent = WalkingAgent();
            var extra = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = new Vector3(-3f, 0f, -1.5f),
                endPosition = new Vector3(3f, 0f, -1.5f),
                bidirectional = true,
                costModifier = -1f,
                width = 0f,
                area = 0,
                agentTypeID = 0,
            });
            try
            {
                RefreshNavMesh();
                GoesOn(agent, Goal(1f));
            }
            finally
            {
                NavMesh.RemoveLink(extra);
            }
        }

        [Fact]
        public void AfterNavMeshDataIsAddedTheAgentGoesOnToItsDestination()
        {
            var agent = WalkingAgent();
            AddFarData();
            RefreshNavMesh();
            GoesOn(agent, Goal(1f));
        }

        [Fact]
        public void AfterNavMeshDataIsRemovedTheAgentGoesOnToItsDestination()
        {
            var far = AddFarData();
            var agent = WalkingAgent();
            NavMesh.RemoveNavMeshData(far);
            m_Data.Remove(far);
            RefreshNavMesh();
            GoesOn(agent, Goal(1f));
        }

        [Fact]
        public void WithoutAutoRepathTheAgentAlsoGoesOn()
        {
            // autoRepath is not consulted: the rebuild discards Unbound's path whether or not it
            // became invalid (Unbound's choice, unmeasured).
            var agent = WalkingAgent(autoRepath: false);
            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            GoesOn(agent, Goal(1f));
            Assert.False(agent.autoRepath);
        }

        [Fact]
        public void APendingPathStaysPendingAndIsComputed()
        {
            // Guard: a request made before the rebuild is still computed by the agent update.
            var agent = Agent(new Vector3(-7f, 0f, 1f));
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(Goal(1f)));
            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.pathPending);
            Assert.False(agent.hasPath);
            Step();
            Assert.False(agent.pathPending);
            Assert.True(agent.hasPath);
        }

        [Fact]
        public void AnAgentWaitingOnALinkStaysThereThroughARebuild()
        {
            // Guard: an agent on an off-mesh link rejoins the rebuilt NavMesh on it, as batch 1's
            // traversal has it (navlinks capture for a link deactivated under it).
            var destination = new Vector3(7f, 0f, 0f);
            var agent = Agent(new Vector3(-7f, 0f, 0f), autoTraverse: false);
            Assert.True(agent.SetDestination(destination));
            int steps = 0;
            while (!agent.isOnOffMeshLink)
            {
                Assert.True(++steps <= 300, "the agent never reached the link");
                Step();
            }
            Step();
            var waiting = agent.nextPosition;
            AddFarData();
            RefreshNavMesh();
            for (int i = 0; i < 10; i++)
            {
                Step();
                Assert.True(agent.isOnOffMeshLink, $"left the link after {i + 1} updates");
                Assert.Equal(waiting, agent.nextPosition);
                Assert.False(agent.pathPending);
            }
            agent.CompleteOffMeshLink();
            Assert.Equal(LinkEnd, agent.nextPosition);
            for (int i = 0; i < 300 && Dist2D(agent.nextPosition, destination) > 1e-2f; i++) Step();
            Assert.True(Dist2D(agent.nextPosition, destination) <= 1e-2f, $"stopped at {agent.nextPosition}");
        }

        // ------------------------------------------------------------------ area costs

        [Fact]
        public void NavMeshSetAreaCostReplacesTheAgentsCosts()
        {
            float own = m_Costs[0] + 6f;
            float ownOther = m_Costs[1] + 5f;
            var agent = Agent(new Vector3(-7f, 0f, 1f));
            agent.SetAreaCost(SpareArea, own);
            agent.SetAreaCost(OtherArea, ownOther);
            Assert.Equal(own, agent.GetAreaCost(SpareArea));

            NavMesh.SetAreaCost(SpareArea, m_Costs[0] + 2f);
            // Documented: the NavMesh's cost replaces the agent's.
            Assert.Equal(m_Costs[0] + 2f, agent.GetAreaCost(SpareArea));
            // The agent's cost for another area stays (Unbound's choice, unmeasured).
            Assert.Equal(ownOther, agent.GetAreaCost(OtherArea));
            // An area the agent never set follows the NavMesh's cost.
            NavMesh.SetAreaCost(ThirdArea, m_Costs[2] + 3f);
            Assert.Equal(m_Costs[2] + 3f, agent.GetAreaCost(ThirdArea));
            // The agent's own cost set afterwards is its own again.
            agent.SetAreaCost(SpareArea, own);
            Assert.Equal(own, agent.GetAreaCost(SpareArea));
            // Documented: the default for an agent created after.
            var later = Agent(new Vector3(-7f, 0f, -1f));
            Assert.Equal(NavMesh.GetAreaCost(ThirdArea), later.GetAreaCost(ThirdArea));
        }

        [Fact]
        public void NavMeshSetAreaCostAlsoReplacesADisabledAgentsCosts()
        {
            // On every agent, enabled or not (Unbound's reading of "all agents", unmeasured).
            var agent = Agent(new Vector3(-7f, 0f, 1f));
            agent.enabled = false;
            agent.SetAreaCost(SpareArea, m_Costs[0] + 6f);
            Assert.Equal(m_Costs[0] + 6f, agent.GetAreaCost(SpareArea));
            NavMesh.SetAreaCost(SpareArea, m_Costs[0] + 2f);
            Assert.Equal(m_Costs[0] + 2f, agent.GetAreaCost(SpareArea));
        }

        [Fact]
        public void ReplacingAnAgentsCostMakesItsPathStale()
        {
            // The agent's cost changed by the replacement makes its path stale, as its own
            // SetAreaCost does; the NavMesh's unchanged cost leaves the others' fresh (Unbound's
            // choices, unmeasured).
            var agent = WalkingAgent(1f);
            var other = WalkingAgent(-1f);
            agent.SetAreaCost(SpareArea, m_Costs[0] + 6f);
            Assert.True(agent.isPathStale);
            Assert.True(agent.SetDestination(Goal(1f)));
            Step();
            Assert.False(agent.isPathStale);

            NavMesh.SetAreaCost(SpareArea, NavMesh.GetAreaCost(SpareArea));
            Assert.Equal(m_Costs[0], agent.GetAreaCost(SpareArea));
            Assert.True(agent.isPathStale);
            Assert.False(other.isPathStale);
        }

        [Fact]
        public void EnablingOrDisablingTheAgentResetsItsCosts()
        {
            float own = m_Costs[0] + 6f;
            var agent = Agent(new Vector3(-7f, 0f, 1f));
            agent.SetAreaCost(SpareArea, own);
            Assert.Equal(own, agent.GetAreaCost(SpareArea));

            // Documented: reset when the agent is disabled; at once (Unbound's choice, unmeasured).
            agent.enabled = false;
            Assert.Equal(NavMesh.GetAreaCost(SpareArea), agent.GetAreaCost(SpareArea));
            // Set while disabled, then reset when it is enabled.
            agent.SetAreaCost(SpareArea, own);
            Assert.Equal(own, agent.GetAreaCost(SpareArea));
            agent.enabled = true;
            Assert.Equal(NavMesh.GetAreaCost(SpareArea), agent.GetAreaCost(SpareArea));
            Assert.True(agent.isOnNavMesh);

            // The GameObject deactivated and activated disables and enables the agent.
            agent.SetAreaCost(SpareArea, own);
            agent.gameObject.SetActive(false);
            Assert.Equal(NavMesh.GetAreaCost(SpareArea), agent.GetAreaCost(SpareArea));
            agent.SetAreaCost(SpareArea, own);
            agent.gameObject.SetActive(true);
            Assert.Equal(NavMesh.GetAreaCost(SpareArea), agent.GetAreaCost(SpareArea));
        }
    }
}
