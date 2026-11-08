using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Agents at off-mesh links: with autoTraverseOffMeshLink off an agent waits on the
    /// link until a script completes it; with it on Detour's traversal carries it over unchanged.
    /// The level is two 6 m platforms with a 4 m gap that only a link from x = -3 to x = 3 crosses.
    /// The agent update runs directly at a fixed 0.02 s step, so this shares the frame-stepping
    /// collection (the global clock).
    /// Documented (Scripting API 6000.0): without autoTraverseOffMeshLink the agent does not cross
    /// by itself and waits until CompleteOffMeshLink. Assertions marked (measured) are observed
    /// only in the Unity 6000.0.71f1 navlinks capture
    /// (tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl, committed at 982cee91 and not
    /// yet admitted); a re-target while waiting is unmeasured.</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentOffMeshLinkTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 LinkStart = new Vector3(-3f, 0f, 0f);
        static readonly Vector3 LinkEnd = new Vector3(3f, 0f, 0f);
        static readonly Vector3 Destination = new Vector3(7f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly NavMeshDataInstance m_Data;
        readonly NavMeshLinkInstance m_Link;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshAgentOffMeshLinkTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var sources = new List<NavMeshBuildSource>
            {
                Platform(-5f),
                Platform(5f),
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(20f, 4f, 10f)), Vector3.zero, Quaternion.identity);
            m_Data = NavMesh.AddNavMeshData(data);
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
            NavMesh.RemoveNavMeshData(m_Data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        static NavMeshBuildSource Platform(float x) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(6f, 1f, 4f),
            transform = Matrix4x4.Translate(new Vector3(x, -0.5f, 0f)),
            area = 0,
        };

        NavMeshAgent Agent(bool autoTraverse)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-7f, 0f, 0f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.autoTraverseOffMeshLink = autoTraverse;
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(Destination));
            return agent;
        }

        static void Step() => AgentSystem.Step();

        /// <summary>Steps until the agent is on the link; the number of steps it took.</summary>
        static int StepOntoLink(NavMeshAgent agent)
        {
            for (int i = 1; i <= 300; i++)
            {
                Step();
                if (agent.isOnOffMeshLink) return i;
            }
            throw new Xunit.Sdk.XunitException("the agent never reached the link");
        }

        static float Dist2D(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>A NavMesh query: the NavMesh takes in changes (a link deactivated) at the next one.</summary>
        static void RefreshNavMesh() => NavMesh.SamplePosition(LinkEnd, out _, 1f, NavMesh.AllAreas);

        /// <summary>Steps until the agent stands at the destination, never back over the gap
        /// (centred on x = 0) to the link's start side.</summary>
        static void WalkTo(NavMeshAgent agent, Vector3 destination)
        {
            for (int i = 0; i < 200 && Dist2D(agent.nextPosition, destination) > 1e-2f; i++)
            {
                Step();
                Assert.True(agent.nextPosition.x > 0f, $"went back to {agent.nextPosition}");
            }
            Assert.True(Dist2D(agent.nextPosition, destination) <= 1e-2f, $"stopped at {agent.nextPosition}");
        }

        [Fact]
        public void WithoutAutoTraverseTheAgentWaitsOnTheLinkWithItsData()
        {
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            var waiting = agent.nextPosition;
            // It reached the link within Detour's trigger distance (2.25 radii) of its start.
            Assert.True(waiting.x <= LinkStart.x, $"waiting at {waiting}");
            Assert.True(Dist2D(waiting, LinkStart) <= 2.25f * agent.radius + 1e-3f, $"waiting at {waiting}");
            // On a link the rest of the path is unknown (measured: navlinks, every frame on a link).
            Assert.Equal(float.PositiveInfinity, agent.remainingDistance);
            // Waits in place with no velocity (measured).
            for (int i = 0; i < 60; i++)
            {
                Step();
                Assert.True(agent.isOnOffMeshLink, $"left the link after {i + 1} updates");
                Assert.Equal(waiting, agent.nextPosition);
                Assert.Equal(waiting, agent.transform.position);
                Assert.Equal(Vector3.zero, agent.velocity);
            }
            // The plan to go on is kept while it waits (measured).
            Assert.InRange(agent.desiredVelocity.x, agent.speed - 0.05f, agent.speed + 0.05f);

            var current = agent.currentOffMeshLinkData;
            Assert.True(current.valid);
            Assert.True(current.activated);
            Assert.InRange(current.startPos.x, LinkStart.x - 1e-3f, LinkStart.x + 1e-3f);
            Assert.InRange(current.startPos.z, LinkStart.z - 1e-3f, LinkStart.z + 1e-3f);
            // The link's own end point, as placed; the link occupied (measured).
            Assert.Equal(LinkEnd, current.endPos);
            Assert.False(agent.nextOffMeshLinkData.valid);
            Assert.True(NavMesh.IsLinkOccupied(m_Link));
        }

        /// <summary>Completing a link at the path's end: the agent does not walk on, and its transform
        /// reaches the link's end at the next early AI update (documented, NavMeshAgent.nextPosition:
        /// the transform matches the simulated position when Update is called).</summary>
        [Fact]
        public void CompletingALinkAtThePathsEndMovesTheTransformAtTheEarlyUpdate()
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-7f, 0f, 0f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.autoTraverseOffMeshLink = false;
            Assert.True(agent.SetDestination(LinkEnd));
            StepOntoLink(agent);
            var waiting = agent.transform.position;
            agent.CompleteOffMeshLink();
            Step();
            Step();
            Assert.Equal(LinkEnd.x, agent.nextPosition.x, 3);
            AgentSystem.SyncTransforms();
            Assert.NotEqual(waiting, agent.transform.position);
            Assert.Equal(agent.nextPosition, agent.transform.position);
        }

        [Fact]
        public void CompleteOffMeshLinkPutsTheAgentAtTheEndAndItWalksOn()
        {
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            for (int i = 0; i < 10; i++) Step();
            var waiting = agent.transform.position;

            agent.CompleteOffMeshLink();
            Assert.False(agent.isOnOffMeshLink);
            Assert.False(NavMesh.IsLinkOccupied(m_Link));
            Assert.Equal(LinkEnd, agent.nextPosition);
            // The transform stays until the next early update, which moves the agent (measured).
            Assert.Equal(waiting, agent.transform.position);
            Assert.Equal(waiting, agent.transform.position);

            Step();
            Assert.False(agent.isOnOffMeshLink);
            // On at its planned velocity at once: one full-speed step past the end (measured).
            Assert.InRange(agent.nextPosition.x, LinkEnd.x + agent.speed * Dt - 1e-2f, LinkEnd.x + agent.speed * Dt + 1e-2f);
            Assert.InRange(agent.velocity.x, agent.speed - 0.05f, agent.speed + 0.05f);
            Assert.Equal(agent.nextPosition, agent.transform.position);

            for (int i = 0; i < 200 && Dist2D(agent.nextPosition, Destination) > 1e-2f; i++) Step();
            Assert.True(Dist2D(agent.nextPosition, Destination) <= 1e-2f, $"stopped at {agent.nextPosition}");
        }

        [Fact]
        public void AScriptMovesAWaitingAgentOverTheLinkFreely()
        {
            // As Unity's link mover samples do: the transform straight to the end at the agent's
            // speed, then CompleteOffMeshLink.
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            var end = agent.currentOffMeshLinkData.endPos + Vector3.up * agent.baseOffset;
            int frames = 0;
            // Over the gap the transform is off the NavMesh; the agent follows it there, with no
            // velocity (measured).
            while (agent.transform.position != end)
            {
                Assert.True(frames++ < 200, "the script never reached the end");
                agent.transform.position = Vector3.MoveTowards(agent.transform.position, end, agent.speed * Dt);
                Step();
                Assert.True(agent.isOnOffMeshLink);
                Assert.Equal(agent.transform.position, agent.nextPosition);
                Assert.Equal(Vector3.zero, agent.velocity);
            }
            agent.CompleteOffMeshLink();
            Step();
            Step();
            Assert.False(agent.isOnOffMeshLink);
            Assert.True(agent.nextPosition.x > LinkEnd.x, $"at {agent.nextPosition}");
        }

        [Fact]
        public void ARequestOnALinkIsFoundWhileACarvedHoleMovesEveryFrame()
        {
            // Each move of a carving obstacle rebuilds the NavMesh; the waiting agent's search,
            // restarted by each rebuild, must still finish (see AgentSystem.RestartRequest).
            int iterations = NavMesh.pathfindingIterationsPerFrame;
            try
            {
                var agent = Agent(autoTraverse: false);
                StepOntoLink(agent);
                NavMesh.pathfindingIterationsPerFrame = 1;
                // Small holes on the far platform make the search need many iterations.
                for (int k = 0; k < 4; k++)
                {
                    var hole = new GameObject("hole");
                    m_Objects.Add(hole);
                    hole.transform.position = new Vector3(3.8f + k * 0.9f, 0.5f, k % 2 == 0 ? 0.6f : -0.6f);
                    var o = hole.AddComponent<NavMeshObstacle>();
                    o.size = new Vector3(0.2f, 1f, 0.2f);
                    o.carving = true;
                }
                var go = new GameObject("carver");
                m_Objects.Add(go);
                go.transform.position = new Vector3(-7f, 0.5f, 1.2f);
                var carver = go.AddComponent<NavMeshObstacle>();
                carver.size = new Vector3(0.2f, 1f, 0.2f);
                carver.carveOnlyStationary = false;
                carver.carving = true;
                ObstacleSystem.UpdateCarving();
                ObstacleSystem.UpdateCarving();
                Assert.True(agent.SetDestination(new Vector3(7f, 0f, 1f)));
                int frames = 0;
                while (agent.pathPending && frames < 300)
                {
                    go.transform.position += new Vector3(0.15f, 0f, 0f);
                    ObstacleSystem.UpdateCarving();
                    Step();
                    frames++;
                }
                Assert.False(agent.pathPending, $"still pending after {frames} frames");
                Assert.True(agent.isOnOffMeshLink);
            }
            finally
            {
                NavMesh.pathfindingIterationsPerFrame = iterations;
                ObstacleSystem.ResetCarving();
            }
        }

        [Fact]
        public void TurningAutoTraverseOffStopsATraversalWhereItIs()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            Step();
            Step();
            // It stops where it is and stays on the link (measured).
            agent.autoTraverseOffMeshLink = false;
            Step();
            var stopped = agent.nextPosition;
            for (int i = 0; i < 40; i++)
            {
                Step();
                Assert.True(agent.isOnOffMeshLink);
                Assert.Equal(stopped, agent.nextPosition);
            }
            agent.CompleteOffMeshLink();
            Assert.Equal(LinkEnd, agent.nextPosition);
        }

        [Fact]
        public void TurningAutoTraverseOnLetsAWaitingAgentCross()
        {
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            var waiting = agent.nextPosition;
            for (int i = 0; i < 20; i++) Step();
            Assert.True(agent.isOnOffMeshLink);
            Assert.Equal(waiting, agent.nextPosition);
            // Detour's traversal from where it waits (measured).
            agent.autoTraverseOffMeshLink = true;
            int steps = 0;
            while (agent.isOnOffMeshLink)
            {
                Assert.True(steps++ < 100, "the agent stayed on the link");
                Step();
            }
            // Detour ends a traversal at its last tweened step, short of the end.
            Assert.True(agent.nextPosition.x >= LinkEnd.x - 0.25f, $"left the link at {agent.nextPosition}");
        }

        [Fact]
        public void AutomaticTraversalKeepsDetoursTiming()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            var start = agent.currentOffMeshLinkData.startPos;
            // Detour's traversal: half the link's length at full speed, the first 15% of it to the
            // link's start (measured: the Unity 6000.0.71f1 traversal is the same).
            float tmax = Dist2D(start, LinkEnd) / agent.speed * 0.5f;
            int expected = (int)Math.Ceiling(tmax / Dt);
            float lastX = agent.nextPosition.x;
            int steps = 0;
            while (agent.isOnOffMeshLink)
            {
                Assert.True(steps++ < expected + 5, "the agent stayed on the link");
                Step();
                Assert.True(agent.nextPosition.x >= lastX, $"went back to {agent.nextPosition}");
                lastX = agent.nextPosition.x;
            }
            Assert.InRange(steps, expected - 1, expected + 1);
            // Detour ends a traversal at its last tweened step, short of the end.
            Assert.True(agent.nextPosition.x >= LinkEnd.x - 0.25f, $"left the link at {agent.nextPosition}");
        }

        [Fact]
        public void AWaitingAgentStaysOnALinkDeactivatedUnderIt()
        {
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            var waiting = agent.nextPosition;
            for (int i = 0; i < 10; i++) Step();

            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            // It waits on the link until a script completes it (measured).
            for (int i = 0; i < 20; i++)
            {
                Step();
                Assert.True(agent.isOnOffMeshLink, $"left the link after {i + 1} updates");
                Assert.Equal(waiting, agent.nextPosition);
                Assert.Equal(Vector3.zero, agent.velocity);
            }
            Assert.InRange(agent.desiredVelocity.x, agent.speed - 0.05f, agent.speed + 0.05f);
            Assert.True(NavMesh.IsLinkOccupied(m_Link));
            var current = agent.currentOffMeshLinkData;
            Assert.True(current.valid);
            Assert.False(current.activated);
            Assert.Equal(LinkEnd, current.endPos);

            // Then it crosses as over an active link (measured) and walks on.
            agent.CompleteOffMeshLink();
            Assert.False(agent.isOnOffMeshLink);
            Assert.Equal(LinkEnd, agent.nextPosition);
            Step();
            Assert.InRange(agent.nextPosition.x, LinkEnd.x + agent.speed * Dt - 1e-2f, LinkEnd.x + agent.speed * Dt + 1e-2f);
            WalkTo(agent, Destination);
        }

        [Fact]
        public void AnAutomaticTraversalGoesOnOverItsLinkDeactivated()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            var start = agent.currentOffMeshLinkData.startPos;
            float tmax = Dist2D(start, LinkEnd) / agent.speed * 0.5f;
            int expected = (int)Math.Ceiling(tmax / Dt);
            Step();
            Step();
            agent.ActivateCurrentOffMeshLink(false);
            Assert.False(NavMesh.IsLinkActive(m_Link));
            RefreshNavMesh();
            // The traversal under way goes on to its end (measured), as before the link was
            // deactivated: Detour's timing, no step back.
            int steps = 2;
            float lastX = agent.nextPosition.x;
            while (agent.isOnOffMeshLink)
            {
                Assert.True(steps++ < expected + 5, "the agent stayed on the link");
                Step();
                Assert.True(agent.nextPosition.x >= lastX, $"went back to {agent.nextPosition}");
                lastX = agent.nextPosition.x;
                if (agent.isOnOffMeshLink) Assert.False(agent.currentOffMeshLinkData.activated);
            }
            Assert.InRange(steps, expected - 1, expected + 1);
            Assert.True(agent.nextPosition.x >= LinkEnd.x - 0.25f, $"left the link at {agent.nextPosition}");
            WalkTo(agent, Destination);
        }

        [Fact]
        public void ANewDestinationWhileWaitingIsFollowedFromTheLinksEnd()
        {
            // Unmeasured while waiting; Unity's automatic traversal follows a new path from the
            // link's end (measured).
            var target = new Vector3(6f, 0f, 1f);
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            var waiting = agent.nextPosition;
            Step();
            // One re-target while it waits, then updates without one.
            Assert.True(agent.SetDestination(target));
            for (int i = 0; i < 10; i++)
            {
                Step();
                Assert.True(agent.isOnOffMeshLink, $"left the link after {i + 1} updates");
                Assert.Equal(waiting, agent.nextPosition);
            }
            Assert.False(agent.pathPending);

            agent.CompleteOffMeshLink();
            Assert.Equal(LinkEnd, agent.nextPosition);
            Step();
            Step();
            // On from the link's end, not back over the gap to where it waited.
            Assert.False(agent.isOnOffMeshLink);
            Assert.True(agent.nextPosition.x > LinkEnd.x, $"at {agent.nextPosition}");
            WalkTo(agent, target);
        }

        /// <summary>Diagnostic (added; the facts above are unchanged): this level's own route over
        /// the link, with no agent. The link crosses the NavMesh tile edge at x = 0 and starts on
        /// the one at z = 0, so a failure here as well as at StepOntoLink points at the link's
        /// connection to the NavMesh rather than at the agent's traversal.</summary>
        [Fact]
        public void TheLevelHasACompleteRouteOverTheLink()
        {
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(new Vector3(-7f, 0f, 0f), Destination, NavMesh.AllAreas, path), "no path search");
            var corners = path.corners;
            string route = string.Join(" ", corners);
            bool complete = path.status == NavMeshPathStatus.PathComplete;
            Assert.True(complete, $"{path.status}: {route}");
            bool overLink = Array.Exists(corners, c => Dist2D(c, LinkStart) <= NavWorld.LinkSnapRadius + 1e-3f);
            Assert.True(overLink, $"not over the link: {route}");
            Assert.True(Dist2D(corners[corners.Length - 1], Destination) <= 1e-2f, $"ends short: {route}");
        }
    }
}
