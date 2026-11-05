using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NavMeshAgent.isPathStale and NavMeshAgent.navMeshOwner.
    /// Documented (Scripting API 6000.0): the path is stale after a change of the agent's areaMask,
    /// an off-mesh link enabled or disabled, or changed NavMesh area costs; navMeshOwner is the
    /// owning object of the NavMesh (or link instance) the agent is placed on, null when none is set.
    /// Nothing here is measured in Unity. Unbound's choices (unmeasured) are marked so: the flag is
    /// set at once by the change, only a changed value counts, it applies to the path last applied
    /// and is cleared by a newly applied path or ResetPath, adding or removing a link counts as
    /// enabling or disabling it, and an agent on a link reports the link's owner.
    /// The level is two 6 m platforms with a 4 m gap that only a link from x = -3 to x = 3 crosses,
    /// one NavMeshData for both. The agent update runs directly at a fixed 0.02 s step, so this
    /// shares the frame-stepping collection (the global clock).</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentPathStaleTests : IDisposable
    {
        const float Dt = 0.02f;
        /// <summary>An area no polygon uses; its cost is restored afterwards.</summary>
        const int SpareArea = 3;
        static readonly Vector3 LinkStart = new Vector3(-3f, 0f, 0f);
        static readonly Vector3 LinkEnd = new Vector3(3f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float m_SpareCost;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();
        readonly NavMeshLinkInstance m_Link;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshAgentPathStaleTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            m_SpareCost = NavMesh.GetAreaCost(SpareArea);
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
            NavMesh.SetAreaCost(SpareArea, m_SpareCost);
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

        GameObject Owner(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go;
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

        /// <summary>An agent on the left platform following a path along it (no link), applied by
        /// one agent update; a fresh path is not stale.</summary>
        NavMeshAgent AgentWithPath(float z = 0f)
        {
            var agent = Agent(new Vector3(-7f, 0f, z));
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(new Vector3(-3.5f, 0f, z)));
            Step();
            Assert.True(agent.hasPath);
            Assert.False(agent.isPathStale);
            return agent;
        }

        static void Step() => AgentSystem.Step();

        // ------------------------------------------------------------------ isPathStale

        [Fact]
        public void ChangedAreaCostsMakeThePathStaleAndTheAgentKeepsIt()
        {
            var agent = AgentWithPath();
            var destination = agent.destination;
            NavMesh.SetAreaCost(SpareArea, m_SpareCost + 4f);
            // Documented; set at once by the change (Unbound's choice, unmeasured).
            Assert.True(agent.isPathStale);
            // Only the flag reports it: the agent goes on along the path it has.
            var before = agent.nextPosition;
            Step();
            Assert.True(agent.isPathStale);
            Assert.True(agent.hasPath);
            Assert.Equal(destination, agent.destination);
            Assert.True(agent.nextPosition.x > before.x, $"stopped at {agent.nextPosition}");
        }

        [Fact]
        public void ChangedAreaMaskMakesThatAgentsPathStale()
        {
            var agent = AgentWithPath(-1f);
            var other = AgentWithPath(1f);
            agent.areaMask = NavMesh.AllAreas & ~(1 << SpareArea);
            // Documented.
            Assert.True(agent.isPathStale);
            // Another agent's mask is its own (Unbound's choice, unmeasured).
            Assert.False(other.isPathStale);
            Step();
            Assert.True(agent.isPathStale);
            Assert.True(agent.hasPath);
        }

        [Fact]
        public void ChangedAgentAreaCostMakesThatAgentsPathStale()
        {
            // The agent's own area costs count as changed area costs for it (Unbound's choice,
            // unmeasured).
            var agent = AgentWithPath(-1f);
            var other = AgentWithPath(1f);
            agent.SetAreaCost(SpareArea, agent.GetAreaCost(SpareArea) + 4f);
            Assert.True(agent.isPathStale);
            Assert.False(other.isPathStale);
        }

        [Fact]
        public void UnchangedValuesLeaveThePathFresh()
        {
            // Only a changed value counts (Unbound's choice, unmeasured).
            var agent = AgentWithPath();
            NavMesh.SetAreaCost(SpareArea, NavMesh.GetAreaCost(SpareArea));
            agent.areaMask = agent.areaMask;
            agent.SetAreaCost(SpareArea, agent.GetAreaCost(SpareArea));
            NavMesh.SetLinkActive(m_Link, NavMesh.IsLinkActive(m_Link));
            Assert.False(agent.isPathStale);
        }

        [Fact]
        public void DeactivatingALinkMakesThePathStale()
        {
            var agent = AgentWithPath();
            NavMesh.SetLinkActive(m_Link, false);
            // Documented (a link enabled or disabled), for every agent, not only one whose path
            // uses the link (Unbound's choice, unmeasured).
            Assert.True(agent.isPathStale);
        }

        [Fact]
        public void AddingOrRemovingALinkMakesThePathStale()
        {
            // The link components enable and disable by adding and removing their link (Unbound's
            // reading of "an OffMeshLink enabled or disabled", unmeasured).
            var agent = AgentWithPath();
            var extra = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = new Vector3(-3f, 0f, 1.5f),
                endPosition = new Vector3(3f, 0f, 1.5f),
                bidirectional = true,
                costModifier = -1f,
                width = 0f,
                area = 0,
                agentTypeID = 0,
            });
            try
            {
                Assert.True(agent.isPathStale);
            }
            finally
            {
                NavMesh.RemoveLink(extra);
            }
            var second = AgentWithPath(1f);
            var link = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = new Vector3(-3f, 0f, -1.5f),
                endPosition = new Vector3(3f, 0f, -1.5f),
                bidirectional = true,
                costModifier = -1f,
                width = 0f,
                area = 0,
                agentTypeID = 0,
            });
            // A path applied after the link was added, then the link removed.
            Assert.True(second.SetDestination(new Vector3(-3.5f, 0f, 1f)));
            Step();
            Assert.False(second.isPathStale);
            NavMesh.RemoveLink(link);
            Assert.True(second.isPathStale);
        }

        [Fact]
        public void ANewPathClearsTheFlag()
        {
            var agent = AgentWithPath();

            // A path request: the path followed while it is pending stays stale; the new path,
            // once applied, is not (Unbound's choice, unmeasured).
            NavMesh.SetAreaCost(SpareArea, m_SpareCost + 4f);
            Assert.True(agent.isPathStale);
            Assert.True(agent.SetDestination(new Vector3(-3.5f, 0f, 1f)));
            Assert.True(agent.pathPending);
            Assert.True(agent.isPathStale);
            Step();
            Assert.False(agent.pathPending);
            Assert.True(agent.hasPath);
            Assert.False(agent.isPathStale);

            // SetPath applies its path at once.
            agent.areaMask = NavMesh.AllAreas & ~(1 << SpareArea);
            Assert.True(agent.isPathStale);
            var path = new NavMeshPath();
            Assert.True(agent.CalculatePath(new Vector3(-3.5f, 0f, -1f), path));
            Assert.True(agent.SetPath(path));
            Assert.False(agent.isPathStale);

            // ResetPath leaves no path to be stale.
            NavMesh.SetAreaCost(SpareArea, m_SpareCost + 8f);
            Assert.True(agent.isPathStale);
            agent.ResetPath();
            Assert.False(agent.isPathStale);
        }

        // ------------------------------------------------------------------ navMeshOwner

        [Fact]
        public void NavMeshOwnerIsTheOwnerOfTheDataTheAgentStandsOn()
        {
            var owner = Owner("left owner");
            var data = m_Data[0];
            data.owner = owner;
            // A second instance with its own owner, apart from the first.
            var otherOwner = Owner("far owner");
            var far = AddData(new Bounds(new Vector3(0f, 0f, 20f), new Vector3(10f, 4f, 10f)), Platform(new Vector3(0f, 0f, 20f)));
            m_Data.Add(far);
            far.owner = otherOwner;

            var agent = Agent(new Vector3(-7f, 0f, 0f));
            Assert.True(agent.isOnNavMesh);
            Assert.Same(owner, agent.navMeshOwner);
            var right = Agent(new Vector3(7f, 0f, 0f));
            Assert.Same(owner, right.navMeshOwner);
            var onFar = Agent(new Vector3(0f, 0f, 20f));
            Assert.True(onFar.isOnNavMesh);
            Assert.Same(otherOwner, onFar.navMeshOwner);

            // No owner set: null (documented).
            far.owner = null;
            Assert.Null(onFar.navMeshOwner);
        }

        [Fact]
        public void NavMeshOwnerIsNullOffTheNavMesh()
        {
            var owner = Owner("owner");
            var data = m_Data[0];
            data.owner = owner;
            var agent = Agent(new Vector3(-7f, 0f, 0f));
            Assert.Same(owner, agent.navMeshOwner);

            // Warped where there is no NavMesh, the agent is placed on none.
            Assert.False(agent.Warp(new Vector3(0f, 0f, -30f)));
            Assert.False(agent.isOnNavMesh);
            Assert.Null(agent.navMeshOwner);

            var offMesh = Agent(new Vector3(0f, 0f, 30f));
            Assert.False(offMesh.isOnNavMesh);
            Assert.Null(offMesh.navMeshOwner);
        }

        [Fact]
        public void AnAgentOnALinkReportsTheLinksOwner()
        {
            // The page names NavMesh and link instance owners; that an agent on a link reports the
            // link's is Unbound's choice (unmeasured).
            var owner = Owner("navmesh owner");
            var data = m_Data[0];
            data.owner = owner;
            var linkOwner = Owner("link owner");
            NavMesh.SetLinkOwner(m_Link, linkOwner);
            var agent = Agent(new Vector3(-7f, 0f, 0f), autoTraverse: false);
            Assert.True(agent.SetDestination(new Vector3(7f, 0f, 0f)));
            Assert.Same(owner, agent.navMeshOwner);
            int steps = 0;
            while (!agent.isOnOffMeshLink)
            {
                Assert.True(++steps <= 300, "the agent never reached the link");
                Step();
            }
            Assert.Same(linkOwner, agent.navMeshOwner);
            agent.CompleteOffMeshLink();
            Step();
            Assert.False(agent.isOnOffMeshLink);
            Assert.Same(owner, agent.navMeshOwner);
        }
    }
}
