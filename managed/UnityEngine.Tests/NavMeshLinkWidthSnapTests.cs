using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>How runtime links attach to the NavMesh and how a wide link is crossed, and when an
    /// agent's partial path is searched again after a link change.
    /// Documented (AI Navigation 2.0 Manual, NavMesh Link; Scripting API 6000.0, NavMeshLinkData,
    /// isPathStale, autoRepath): a link's width spreads its ends across it; enabling or disabling a
    /// link makes the agents' paths stale. Measured (Unity 6000.0.71f1, tests/projects/navpath
    /// link.snap and link.wide, tests/projects/navlinks snap*, wide, inactive, disableagent): an end
    /// attaches within the agent type's radius across and its climb in height; a wide link is
    /// crossed straight across, within the part of its ends over the NavMesh; a stale partial path
    /// is searched again once its end is the agent's next corner within its radius; a disabled
    /// agent has no destination.
    /// The level is a 24 x 24 m floor cut by a 2 m strip at x = 0 (the NavMesh ends near x = -1.5
    /// and 1.5 for the 0.5 m radius).</summary>
    [Collection("Frames")]
    public sealed class NavMeshLinkWidthSnapTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();
        readonly List<NavMeshLinkInstance> m_Links = new List<NavMeshLinkInstance>();
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshLinkWidthSnapTests()
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

        static NavMeshBuildSource Box(Vector3 centre, Vector3 size, int area) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.Translate(centre),
            area = area,
        };

        /// <summary>The floor with the strip (and further holes) for agents of the settings given.</summary>
        void Gap(NavMeshBuildSettings settings, params (Vector3 Centre, Vector3 Size)[] holes)
        {
            var sources = new List<NavMeshBuildSource>
            {
                Box(new Vector3(0f, -0.05f, 0f), new Vector3(24f, 0.1f, 24f), 0),
                Box(new Vector3(0.037f, 0.5f, 0.053f), new Vector3(2f, 1f, 30f), 1),
            };
            foreach (var (centre, size) in holes) sources.Add(Box(centre, size, 1));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(28f, 8f, 28f)), Vector3.zero, Quaternion.identity);
            m_Data.Add(NavMesh.AddNavMeshData(data));
        }

        NavMeshLinkInstance Link(Vector3 start, Vector3 end, float width = 0f, int agentType = 0)
        {
            var link = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = start,
                endPosition = end,
                width = width,
                bidirectional = true,
                costModifier = -1f,
                area = 0,
                agentTypeID = agentType,
            });
            m_Links.Add(link);
            return link;
        }

        static NavMeshPath Path(Vector3 from, Vector3 to, int agentType = 0)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(from, to, new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas }, path);
            return path;
        }

        /// <summary>Where the NavMesh ends along z = <paramref name="z"/> on the side of x below 0
        /// and above it.</summary>
        static (float Left, float Right) Edges(float z, int agentType = 0)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            Assert.True(NavMesh.Raycast(new Vector3(-6f, 0f, z), new Vector3(0f, 0f, z), out var left, filter));
            Assert.True(NavMesh.Raycast(new Vector3(6f, 0f, z), new Vector3(0f, 0f, z), out var right, filter));
            return (left.position.x, right.position.x);
        }

        static void Frame()
        {
            Time.s_Time += Dt;
            AgentSystem.Step();
        }

        NavMeshAgent Agent(Vector3 at, Vector3 goal)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = at;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.SetDestination(goal));
            return agent;
        }

        // ------------------------------------------------------------------ snap

        [Fact]
        public void AnEndAttachesWithinTheAgentTypesRadius()
        {
            var settings = NavMesh.GetSettingsByID(0);
            Gap(settings);
            var (left, right) = Edges(-8f);
            // 0.1 m inside the radius on both sides: crossed.
            float inside = settings.agentRadius - 0.1f;
            Link(new Vector3(left + inside, 0f, -8f), new Vector3(right - inside, 0f, -8f));
            var path = Path(new Vector3(-8f, 0f, -8f), new Vector3(8f, 0f, -8f));
            Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
            // An end 0.1 m beyond the radius does not attach: the path ends on the near side.
            Link(new Vector3(left + inside, 0f, 8f), new Vector3(right - settings.agentRadius - 0.1f, 0f, 8f));
            NavMesh.RemoveLink(m_Links[0]);
            m_Links.RemoveAt(0);
            path = Path(new Vector3(-8f, 0f, 8f), new Vector3(8f, 0f, 8f));
            Assert.Equal(NavMeshPathStatus.PathPartial, path.status);
            // The partial path ends on the ground, not on the link that leads nowhere.
            var last = path.corners[path.corners.Length - 1];
            Assert.True(last.x < 0f && Mathf.Abs(last.x - left) < 0.05f, $"ends at {last}");
        }

        [Fact]
        public void AWiderAgentTypeAttachesEndsFartherAway()
        {
            // A 2 m radius: ends 1.7 m from its NavMesh attach (they would not for the 0.5 m type).
            var big = NavMesh.CreateSettings();
            big.agentRadius = 2f;
            int type = big.agentTypeID;
            try
            {
                Gap(big);
                var (left, right) = Edges(0f, type);
                Link(new Vector3(left + 1.7f, 0f, 0f), new Vector3(right - 1.7f, 0f, 0f), 0f, type);
                Assert.Equal(NavMeshPathStatus.PathComplete, Path(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f), type).status);
            }
            finally
            {
                NavMesh.RemoveSettings(type);
            }
        }

        [Fact]
        public void AnEndAboveTheClimbDoesNotAttach()
        {
            var settings = NavMesh.GetSettingsByID(0);
            Gap(settings);
            var (left, right) = Edges(0f);
            // Over the NavMesh in x, but higher above it than the climb.
            Link(new Vector3(left - 0.2f, settings.agentClimb + 0.2f, 0f), new Vector3(right + 0.2f, settings.agentClimb + 0.2f, 0f));
            Assert.Equal(NavMeshPathStatus.PathPartial, Path(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f)).status);
            Link(new Vector3(left - 0.2f, settings.agentClimb - 0.2f, 4f), new Vector3(right + 0.2f, settings.agentClimb - 0.2f, 4f));
            Assert.Equal(NavMeshPathStatus.PathComplete, Path(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f)).status);
        }

        // ------------------------------------------------------------------ width

        [Fact]
        public void AWideLinkIsCrossedStraightAcrossWithinItsWidth()
        {
            Gap(NavMesh.GetSettingsByID(0));
            Link(new Vector3(-1.7f, 0f, -6f), new Vector3(1.7f, 0f, -6f), 4f);
            foreach (float z in new[] { -6f, -4.8f, -7.7f })
            {
                var corners = Path(new Vector3(-8f, 0f, z), new Vector3(8f, 0f, z)).corners;
                Assert.Equal(4, corners.Length);
                Assert.Equal(-1.7f, corners[1].x, 3);
                Assert.Equal(z, corners[1].z, 3);
                Assert.Equal(1.7f, corners[2].x, 3);
                Assert.Equal(z, corners[2].z, 3);
            }
            // Without width only its centre line is crossed.
            var narrow = Link(new Vector3(-1.7f, 0f, 6f), new Vector3(1.7f, 0f, 6f));
            var through = Path(new Vector3(-8f, 0f, 7.2f), new Vector3(8f, 0f, 7.2f)).corners;
            Assert.Equal(6f, through[1].z, 3);
        }

        [Fact]
        public void AWideLinksCrossingIsHeldToThePartOverTheNavMesh()
        {
            // A hole beside the strip leaves the start of the link over the NavMesh only for
            // z below about 6.
            Gap(NavMesh.GetSettingsByID(0), (new Vector3(-2.563f, 0.5f, 11.053f), new Vector3(3f, 1f, 9f)));
            Link(new Vector3(-1.7f, 0f, 6f), new Vector3(1.7f, 0f, 6f), 4f);
            var agent = Agent(new Vector3(-6f, 0f, 7.2f), new Vector3(6f, 0f, 7.2f));
            Frame();
            Frame();
            var next = agent.nextOffMeshLinkData;
            Assert.True(next.valid);
            Assert.InRange(next.startPos.z, 5.9f, 6.1f);
            // Straight across from there (Unity's: measured, navpath link.wide gb1.2).
            Assert.Equal(next.startPos.z, next.endPos.z, 3);
            bool crossed = false;
            for (int i = 0; i < 300 && !crossed; i++)
            {
                Frame();
                crossed = agent.isOnOffMeshLink;
            }
            Assert.True(crossed, "never reached the link");
            for (int i = 0; i < 300 && agent.hasPath; i++) Frame();
            Assert.True((agent.nextPosition - new Vector3(6f, agent.nextPosition.y, 7.2f)).magnitude < 0.05f, $"at {agent.nextPosition}");
        }

        // ------------------------------------------------------------------ staleness

        [Fact]
        public void AStalePartialPathIsSearchedAgainNearItsEndOnly()
        {
            Gap(NavMesh.GetSettingsByID(0));
            // The only link is inactive: a partial path to the strip.
            var link = Link(new Vector3(-1.7f, 0f, 0f), new Vector3(1.7f, 0f, 0f));
            NavMesh.SetLinkActive(link, false);
            var agent = Agent(new Vector3(-9f, 0f, 0f), new Vector3(8f, 0f, 0f));
            Frame();
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            // Far from the end: the link opens, the path is stale and stays partial.
            NavMesh.SetLinkActive(link, true);
            Assert.True(agent.isPathStale);
            Frame();
            Assert.True(agent.isPathStale);
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            // Near its end it is searched again: complete over the link, not stale.
            int frames = 0;
            for (; frames < 400 && agent.isPathStale; frames++) Frame();
            Assert.False(agent.isPathStale);
            Assert.True(agent.pathStatus == NavMeshPathStatus.PathComplete, $"{agent.pathStatus} after {frames} at {agent.nextPosition}, corners {string.Join(" ", agent.path.corners)} link {NavMesh.IsLinkActive(link)}");
            Assert.True(agent.nextPosition.x < -1f, $"searched again only at {agent.nextPosition}");
        }

        [Fact]
        public void AnAgentThatArrivedAtAPartialEndKeepsItsStalePath()
        {
            Gap(NavMesh.GetSettingsByID(0));
            var link = Link(new Vector3(-1.7f, 0f, 0f), new Vector3(1.7f, 0f, 0f));
            NavMesh.SetLinkActive(link, false);
            var agent = Agent(new Vector3(-4f, 0f, 0f), new Vector3(8f, 0f, 0f));
            for (int i = 0; i < 300; i++) Frame();
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            // Another link changes: stale, and it stays so standing at the end.
            var other = Link(new Vector3(-1.7f, 0f, 8f), new Vector3(1.7f, 0f, 8f));
            NavMesh.SetLinkActive(other, false);
            for (int i = 0; i < 10; i++) Frame();
            Assert.True(agent.isPathStale);
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
        }

        [Fact]
        public void ADisabledAgentHasNoDestination()
        {
            Gap(NavMesh.GetSettingsByID(0));
            var agent = Agent(new Vector3(-8f, 0f, 4f), new Vector3(-3f, 0f, 4f));
            Frame();
            Assert.True(agent.hasPath);
            agent.enabled = false;
            Assert.Equal(Vector3.positiveInfinity, agent.destination);
            Assert.Equal(float.PositiveInfinity, agent.remainingDistance);
            Assert.Equal(NavMeshPathStatus.PathInvalid, agent.pathStatus);
            agent.enabled = true;
            Frame();
            Assert.False(agent.hasPath);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.True((agent.destination - agent.nextPosition).sqrMagnitude < 1e-6f);
            // Not stale through a link change either (no destination).
            var link = Link(new Vector3(-1.7f, 0f, 0f), new Vector3(1.7f, 0f, 0f));
            NavMesh.SetLinkActive(link, false);
            Frame();
            Assert.False(agent.isPathStale);
        }
    }
}
