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

        /// <summary>An agent standing at a link's start is on the link in the update that finds its
        /// path (measured: tests/projects/navlinks startonlink).</summary>
        [Fact]
        public void AnAgentAtALinksStartCrossesFromTheFirstUpdate()
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = LinkStart;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.SetDestination(Destination));
            Step();
            Assert.True(agent.isOnOffMeshLink);
            Assert.Equal(LinkEnd, agent.currentOffMeshLinkData.endPos);
        }

        /// <summary>The crossing goes from the start on the NavMesh to the link's own end point
        /// (here on the ground, below the NavMesh's surface), along the link at the agent's speed in
        /// the plan; the update that ends it carries the agent on past the end onto the NavMesh,
        /// leaving at full speed away from the link's end (measured: tests/projects/navlinks drop,
        /// high, low, every automatic crossing).</summary>
        [Fact]
        public void ACrossingEndsAtTheLinksOwnEndAndLeavesAtSpeed()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            Assert.Equal(LinkEnd, agent.currentOffMeshLinkData.endPos);
            Assert.Equal(LinkEnd, agent.steeringTarget);
            var start = agent.currentOffMeshLinkData.startPos;
            Assert.True(start.y > 0.05f, $"start {start} not on the NavMesh");
            var last = agent.nextPosition;
            int steps = 0;
            while (agent.isOnOffMeshLink)
            {
                last = agent.nextPosition;
                Step();
                Assert.True(steps++ < 200, "the agent stayed on the link");
                if (agent.isOnOffMeshLink && agent.nextPosition.x > start.x + 0.5f)
                {
                    var along = (LinkEnd - start).normalized * agent.speed;
                    Assert.True((agent.desiredVelocity - along).magnitude < 1e-3f, $"plan {agent.desiredVelocity}");
                }
            }
            // The last step on the link went down towards the link's own end.
            Assert.True(last.y < start.y - 0.01f, $"last {last}");
            var left = agent.nextPosition;
            Assert.True(left.x > LinkEnd.x, $"left at {left}");
            Assert.True(left.y > last.y, "back on the NavMesh");
            var plan = agent.desiredVelocity;
            Assert.Equal(agent.speed, plan.magnitude, 3);
            Assert.True(Vector3.Dot(plan.normalized, (left - LinkEnd).normalized) > 0.999f, $"plan {plan}");
            // It walks on at that velocity at once, constrained to the NavMesh.
            Step();
            Assert.Equal(plan.x, agent.velocity.x, 2);
        }

        /// <summary>ResetPath on a link: the agent is off it at once, where it was; the next update
        /// puts it on the landing side's NavMesh nearest to where it was (here the far platform's
        /// edge) and it slides on to a stop; without a path it reports PathComplete (measured:
        /// tests/projects/navlinks resetmid, targets).</summary>
        [Fact]
        public void ResetPathOnALinkLandsTheAgentAtTheLinksEnd()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            Step();
            Step();
            var at = agent.nextPosition;
            agent.ResetPath();
            Assert.False(agent.isOnOffMeshLink);
            Assert.False(agent.hasPath);
            Assert.Equal(at, agent.nextPosition);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            // A NavMesh change before the next update keeps the landing and the slide.
            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            NavMesh.SetLinkActive(m_Link, true);
            RefreshNavMesh();
            Step();
            var landed = agent.nextPosition;
            // The far platform starts at x = 2; its NavMesh about a radius further.
            Assert.InRange(landed.x, 2.3f, 2.8f);
            Assert.True(landed.y > 0.05f, $"not on the NavMesh: {landed}");
            Assert.False(NavMesh.IsLinkOccupied(m_Link));
            for (int i = 0; i < 100; i++) Step();
            Assert.True(agent.nextPosition.x > landed.x, $"stopped at {agent.nextPosition}");
            Assert.Equal(Vector3.zero, agent.velocity);
        }

        /// <summary>A destination given after ResetPath on a link, before the agent lands: it lands
        /// and follows the new path from there (Unbound's choice; Unity measured only without one).</summary>
        [Fact]
        public void ANewDestinationAfterResetPathOnALinkStartsWhereTheAgentLands()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            for (int i = 0; i < 20; i++) Step();
            Assert.True(agent.isOnOffMeshLink);
            agent.ResetPath();
            Assert.True(agent.SetDestination(Destination));
            Step();
            Assert.False(agent.pathPending);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.True(agent.nextPosition.x > 2f, $"at {agent.nextPosition}");
            WalkTo(agent, Destination);
        }

        /// <summary>After ResetPath on a link, a destination on the landing polygon (no search needed
        /// otherwise), a path set by script, or a NavMesh change before it lands: the agent lands
        /// first and goes on from there, never from over the gap (Unbound's choices).</summary>
        [Theory]
        [InlineData("near")]
        [InlineData("nearRebuild")]
        [InlineData("setPath")]
        public void AnAgentResetOnALinkLandsBeforeItsNextPath(string how)
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            for (int i = 0; i < 20; i++) Step();
            agent.ResetPath();
            var target = new Vector3(6f, 0f, 1f);
            if (how == "setPath")
            {
                var path = new NavMeshPath();
                Assert.True(NavMesh.CalculatePath(new Vector3(5f, 0f, 1.5f), target, NavMesh.AllAreas, path));
                Assert.True(agent.SetPath(path));
            }
            else Assert.True(agent.SetDestination(target));
            if (how == "nearRebuild")
            {
                NavMesh.SetLinkActive(m_Link, false);
                RefreshNavMesh();
                NavMesh.SetLinkActive(m_Link, true);
                RefreshNavMesh();
            }
            Step();
            Assert.InRange(agent.nextPosition.x, 2.3f, 2.8f);
            WalkTo(agent, target);
        }

        /// <summary>A NavMesh change while an agent completes its link, or removes the link it crosses:
        /// the agent goes on to its destination, from the link's end or from where it stands.</summary>
        [Fact]
        public void ANavMeshChangeAroundALinkKeepsTheAgentsDestination()
        {
            var agent = Agent(autoTraverse: false);
            StepOntoLink(agent);
            agent.CompleteOffMeshLink();
            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            NavMesh.SetLinkActive(m_Link, true);
            RefreshNavMesh();
            Assert.True(agent.hasPath);
            WalkTo(agent, Destination);

            var go = new GameObject("second");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-7f, 0f, 1f);
            var second = go.AddComponent<NavMeshAgent>();
            Assert.True(second.SetDestination(new Vector3(-4f, 0f, -1f)));
            Step();
            Assert.True(second.SetDestination(Destination));
            StepOntoLink(second);
            NavMesh.RemoveLink(m_Link);
            RefreshNavMesh();
            // No way over any more: a partial path on its side, from where it stands.
            for (int i = 0; i < 20; i++) Step();
            Assert.Equal(NavMeshPathStatus.PathPartial, second.pathStatus);
            Assert.True(second.nextPosition.x < -1f, $"at {second.nextPosition}");
        }

        /// <summary>Without a path an agent reports a complete one: after a partial path, ResetPath
        /// and Warp report PathComplete (measured: tests/projects/navlinks targets).</summary>
        [Fact]
        public void APathlessAgentReportsAPathComplete()
        {
            NavMesh.SetLinkActive(m_Link, false);
            RefreshNavMesh();
            var agent = Agent(autoTraverse: true);
            Step();
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            agent.ResetPath();
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.True(agent.SetDestination(Destination));
            Step();
            Assert.Equal(NavMeshPathStatus.PathPartial, agent.pathStatus);
            agent.Warp(new Vector3(-6f, 0f, 1f));
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.False(agent.hasPath);
            NavMesh.SetLinkActive(m_Link, true);
        }

        /// <summary>An agent reaching a link another agent is on stops short of it, and takes it
        /// once it is free (measured: tests/projects/navlinks crosstile, two).</summary>
        [Fact]
        public void AnAgentWaitsForALinkAnotherAgentIsOn()
        {
            // A slow crossing (6 s), so that the second agent reaches the link while it lasts.
            var first = Agent(autoTraverse: true);
            first.speed = 0.5f;
            StepOntoLink(first);
            var go = new GameObject("second");
            m_Objects.Add(go);
            go.transform.position = new Vector3(-6f, 0f, 1f);
            var second = go.AddComponent<NavMeshAgent>();
            Assert.True(second.SetDestination(Destination));
            for (int i = 0; i < 120; i++) Step();
            Assert.True(first.isOnOffMeshLink);
            Assert.True(NavMesh.IsLinkOccupied(m_Link));
            Assert.False(second.isOnOffMeshLink);
            Assert.True(second.velocity.magnitude < 1e-3f, $"still moving: {second.velocity}");
            Assert.Equal(Vector3.zero, second.desiredVelocity);
            Assert.True(Dist2D(second.nextPosition, LinkStart) <= 2.25f * second.radius + 1e-3f, $"waiting at {second.nextPosition}");
            while (first.isOnOffMeshLink) Step();
            Step();
            Assert.True(second.isOnOffMeshLink);
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
            // The update that ends the crossing carries the agent past the end.
            Assert.True(agent.nextPosition.x > LinkEnd.x, $"left the link at {agent.nextPosition}");
        }

        [Fact]
        public void AutomaticTraversalKeepsDetoursTiming()
        {
            var agent = Agent(autoTraverse: true);
            StepOntoLink(agent);
            var start = agent.currentOffMeshLinkData.startPos;
            // Detour's traversal: half the link's length (in 3D) at full speed, the first 15% of it
            // to the link's start (measured: the Unity 6000.0.71f1 traversal is the same).
            float tmax = Vector3.Distance(start, LinkEnd) / agent.speed * 0.5f;
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
            // The update that ends the crossing carries the agent past the end.
            Assert.True(agent.nextPosition.x > LinkEnd.x, $"left the link at {agent.nextPosition}");
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
            float tmax = Vector3.Distance(start, LinkEnd) / agent.speed * 0.5f;
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
            bool overLink = Array.Exists(corners, c => Dist2D(c, LinkStart) <= NavWorld.SnapRadius(NavWorld.Get(0)) + 1e-3f);
            Assert.True(overLink, $"not over the link: {route}");
            Assert.True(Dist2D(corners[corners.Length - 1], Destination) <= 1e-2f, $"ends short: {route}");
        }
    }
}
