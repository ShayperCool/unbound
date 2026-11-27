using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NavMeshObstacle in the agents' local avoidance.
    /// Documented (Scripting API 6000.0 NavMeshObstacle, Manual "NavMesh Obstacle", AI Navigation
    /// 2.0): an obstacle for agents to avoid; without carving it affects only their avoidance, not
    /// their paths (they plot paths ignoring it and sidestep it as they move); a carving obstacle
    /// cuts a hole in the NavMesh (NavMeshCarvingTests); once its hole is cut it is no longer an
    /// avoidance obstacle (Unbound's choice, unmeasured). The obstacles' shapes in avoidance
    /// (circles covering a box's outline, a capsule's radius), the scripted velocity as the
    /// obstacle's motion and the height-span test are Unbound's choices (unmeasured); the bounds the
    /// tests check include per-step box clearance, without guaranteeing progress round long walls;
    /// other tests state the cover's bound (C5 in ObstacleSystem: circles at most sqrt(2) times a
    /// box's half-width) or the crowd's 64 neighbour candidates (C7).
    /// The level is one 20 m by 10 m platform; an agent walks from x = -8 to x = 8 along z = 0.
    /// The agent update runs directly at a fixed 0.02 s step, so this shares the frame-stepping
    /// collection (the global clock).</summary>
    [Collection("Frames")]
    public sealed class NavMeshObstacleTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 Start = new Vector3(-8f, 0f, 0f);
        static readonly Vector3 Goal = new Vector3(8f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly bool m_WarnedLongBox = ObstacleSystem.s_WarnedLongBox;
        readonly bool m_WarnedCandidates = ObstacleSystem.s_WarnedCandidates;
        readonly NavMeshDataInstance m_Data;
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public NavMeshObstacleTests()
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
            ObstacleSystem.ResetCarving();
            ObstacleSystem.s_WarnedLongBox = m_WarnedLongBox;
            ObstacleSystem.s_WarnedCandidates = m_WarnedCandidates;
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        static void Step() => AgentSystem.Step();

        NavMeshAgent Agent() => Agent(Start, Goal);

        NavMeshAgent Agent(Vector3 start, Vector3 goal)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = start;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(goal));
            return agent;
        }

        NavMeshObstacle Box(Vector3 position, Vector3 size, Quaternion? rotation = null)
        {
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = position;
            go.transform.rotation = rotation ?? Quaternion.identity;
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = size;
            return obstacle;
        }

        NavMeshObstacle Capsule(Vector3 position, float radius)
        {
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = position;
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = Vector3.zero;
            obstacle.radius = radius;
            obstacle.height = 2f;
            return obstacle;
        }

        /// <summary>The agent's walk: the furthest it went from the straight line z = 0, and whether
        /// it got past x = 2 (beyond the obstacles) and to the goal.</summary>
        static (float Deviation, bool Passed, bool Arrived) Walk(NavMeshAgent agent, int steps = 600,
            NavMeshObstacle clearanceObstacle = null)
        {
            float deviation = 0f;
            bool passed = false;
            for (int i = 0; i < steps; i++)
            {
                Step();
                var p = agent.nextPosition;
                if (clearanceObstacle != null) AssertOutsideBoxFootprint(agent, clearanceObstacle, p);
                deviation = Mathf.Max(deviation, Mathf.Abs(p.z));
                if (p.x > 2f) passed = true;
                if (new Vector2(p.x - Goal.x, p.z - Goal.z).magnitude <= 1e-2f) return (deviation, passed, true);
            }
            return (deviation, passed, false);
        }

        // A safety invariant for box clearance, not a promise that local avoidance plans a route
        // around every wall. Check every original step, including stalls. Under the unmeasured
        // circle cover, circles protrude past the box faces so clearance stays >= -1e-4 m; under
        // the exact box outline, 4 relaxation iterations at collisionResolveFactor = 0.7 leave at
        // most (1 - 0.7)^4 of a single step (~0.0005 m at 3.5 m/s, 1/50 s).
        static void AssertOutsideBoxFootprint(NavMeshAgent agent, NavMeshObstacle obstacle, Vector3 position)
        {
            var center = obstacle.transform.TransformPoint(obstacle.center);
            var x = obstacle.transform.TransformVector(new Vector3(obstacle.size.x * 0.5f, 0f, 0f));
            var z = obstacle.transform.TransformVector(new Vector3(0f, 0f, obstacle.size.z * 0.5f));
            var relative = position - center;
            float hx = new Vector2(x.x, x.z).magnitude, hz = new Vector2(z.x, z.z).magnitude;
            float qx = Mathf.Abs((relative.x * x.x + relative.z * x.z) / hx) - hx;
            float qz = Mathf.Abs((relative.x * z.x + relative.z * z.z) / hz) - hz;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qz, 0f) * Mathf.Max(qz, 0f));
            float clearance = outside + Mathf.Min(Mathf.Max(qx, qz), 0f) - agent.radius;
            float limit = Compat.NavMeshObstacleShapesMeasured ? -1e-3f : -1e-4f;
            Assert.True(clearance >= limit, $"agent penetrated the box footprint by {-clearance} m at {position}");
        }

        /// <summary>Two agents' walks: the closest they came to each other (centre to centre).</summary>
        static float Closest(NavMeshAgent a, NavMeshAgent b, int steps = 400)
        {
            float closest = float.MaxValue;
            for (int i = 0; i < steps; i++)
            {
                Step();
                var p = a.nextPosition;
                var q = b.nextPosition;
                closest = Mathf.Min(closest, new Vector2(p.x - q.x, p.z - q.z).magnitude);
            }
            return closest;
        }

        void Remove(Component component)
        {
            m_Objects.Remove(component.gameObject);
            Object.DestroyImmediate(component.gameObject);
        }

        static int CrowdMembers(NavMeshAgent agent) => AgentSystem.CrowdOf(agent.m_Surface).GetActiveAgents().Count;

        // ------------------------------------------------------------------ avoidance

        [Fact]
        public void AnAgentWalksAroundAnObstacleOnItsWay()
        {
            // Documented: agents sidestep the obstacle as they move along their path. The box (1 m,
            // a little off the line) with the agent's 0.5 m radius leaves no straight way past.
            Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
            var agent = Agent();
            var walk = Walk(agent);
            Assert.True(walk.Deviation > 0.75f, $"went through the obstacle, {walk.Deviation} m from the line");
            Assert.True(walk.Passed);
            Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
            // Documented: the obstacle is not part of the path (no carving).
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
        }

        [Fact]
        public void WithoutObstaclesTheCrowdAndTheWalkAreUnchanged()
        {
            // Guard: no obstacle, nothing added to the crowd, the straight walk to the goal.
            var agent = Agent();
            Step();
            Assert.Equal(1, CrowdMembers(agent));
            var walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"left the line by {walk.Deviation} m");
            Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
            Assert.Equal(1, CrowdMembers(agent));
        }

        [Fact]
        public void AnObstacleBesideThePathLeavesTheWalkStraight()
        {
            // Guard: a capsule whose radius and the agent's leave room (1.6 m from the line, 1 m
            // together) and that does not move is no collision to avoid.
            Capsule(new Vector3(0f, 1f, 1.6f), 0.5f);
            var agent = Agent();
            var walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"left the line by {walk.Deviation} m");
            Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
        }

        [Fact]
        public void TheObstaclesVelocityIsAvoided()
        {
            // The same capsule beside the path, given a velocity towards the agent's line: the
            // agent foresees the collision and turns away, though the obstacle never moves (its
            // velocity property is its motion in avoidance; Unbound's choice, unmeasured).
            var still = Capsule(new Vector3(0f, 1f, 1.6f), 0.5f);
            var agent = Agent();
            var straight = Walk(agent);
            Assert.True(straight.Deviation < 1e-3f, $"left the line by {straight.Deviation} m");
            Remove(agent);
            still.velocity = new Vector3(0f, 0f, -3f);
            agent = Agent();
            var avoiding = Walk(agent);
            Assert.True(avoiding.Deviation > 0.05f, $"did not avoid the moving obstacle, {avoiding.Deviation} m from the line");
            Assert.True(avoiding.Passed);
        }

        [Fact]
        public void TheShapeAndSizeAreRespected()
        {
            // A box 5.5 m long across the path takes the agent further out than a 1 m box; turned a
            // quarter, a box 5.5 m along x is the same; raised above the agents' height it is no
            // obstacle; a capsule's radius counts.
            // The 1 m box (z -0.3 to 0.7) needs the agent 0.8 m off the line whatever covers it; its
            // cover's bound (C5: one circle of 0.71 m) clears at 1.01 m, and 2 m leaves the agent's
            // avoidance a metre of lead.
            var small = Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
            var agent = Agent();
            float smallDeviation = Walk(agent, clearanceObstacle: small).Deviation;
            Assert.True(smallDeviation > 0.75f && smallDeviation < 2f, $"1 m box: {smallDeviation} m from the line");
            Remove(agent);
            Remove(small);

            // Both ends fit inside the platform, but this does not guarantee that short-range
            // local avoidance escapes the wall. AI Navigation 2.0 AboutObstacles documents that
            // avoidance can fail to find a way round. This exact uncarved six-circle case stalls
            // without penetration in the saved managed trace; Unity has not been measured here.
            // Check clearance on every step. If it gets past the wall, it must have gone round
            // an end, retaining the original 2.6 m deviation bound for that reached outcome.
            var across = Box(new Vector3(0f, 0.5f, 0.45f), new Vector3(1f, 1f, 5.5f));
            agent = Agent();
            var walk = Walk(agent, clearanceObstacle: across);
            if (walk.Passed)
                Assert.True(walk.Deviation > 2.6f, $"5.5 m box: {walk.Deviation} m from the line");
            Remove(agent);
            Remove(across);

            var turned = Box(new Vector3(0f, 0.5f, 0.45f), new Vector3(5.5f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            agent = Agent();
            walk = Walk(agent, clearanceObstacle: turned);
            if (walk.Passed)
                Assert.True(walk.Deviation > 2.6f, $"turned 5.5 m box: {walk.Deviation} m from the line");
            Remove(agent);
            Remove(turned);

            var raised = Box(new Vector3(0f, 4f, 0.2f), Vector3.one);
            agent = Agent();
            walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"raised box: {walk.Deviation} m from the line");
            Remove(agent);
            Remove(raised);

            var wide = Capsule(new Vector3(0f, 1f, 0.2f), 1.5f);
            agent = Agent();
            walk = Walk(agent);
            Assert.True(walk.Deviation > 1.6f, $"1.5 m capsule: {walk.Deviation} m from the line");
            Remove(agent);
            Remove(wide);
        }

        [Fact]
        public void ALongThinBoxBesideThePathLeavesTheWalkStraight()
        {
            // A wall 12 m long and 0.8 m thick beside the line (its side 1.1 m from it; 0.6 m clear
            // of the agent's 0.5 m radius). Its cover (C5) is 15 circles of 0.57 m, at most
            // (sqrt(2) - 1) half-widths beyond its sides: 0.43 m clear of the agent, and their
            // centres stay beyond the agent's keep-apart distance (2.5 radii), so it walks
            // straight. A cover whose circles grow with the wall's length (four circles of 1.55 m)
            // would reach past the line and push the agent half a metre out.
            Box(new Vector3(0f, 0.5f, 1.5f), new Vector3(12f, 1f, 0.8f));
            var agent = Agent();
            var walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"pushed {walk.Deviation} m off its line by a wall beside it");
            Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
        }

        [Fact]
        public void FarObstaclesLeaveTwoAgentsAvoidingEachOther()
        {
            // Control: two agents walking at each other, 0.3 m apart sideways, keep apart (their
            // radii together are 1 m) and get past each other.
            var other = new Vector3(8f, 0f, 0.3f);
            var otherGoal = new Vector3(-8f, 0f, 0.3f);
            var a = Agent();
            var b = Agent(other, otherGoal);
            float control = Closest(a, b);
            Assert.True(control > 0.7f, $"without obstacles the agents came {control} m close");
            Assert.True(a.nextPosition.x > 2f && b.nextPosition.x < -2f, $"stuck at {a.nextPosition}, {b.nextPosition}");
            Remove(a);
            Remove(b);

            // 40 obstacles 20 m off the line, beyond every agent's neighbour range (15 m) plus their
            // radius, in the proximity-grid cells the agents' neighbour queries scan first. They are
            // no neighbours of anyone, so they must not take the crowd's 32 neighbour candidates
            // (C7) and leave the agents out of each other's avoidance.
            for (int i = 0; i < 40; i++)
                Capsule(new Vector3(-11.5f + 0.55f * i, 1f, -20f), 0.2f);
            a = Agent();
            b = Agent(other, otherGoal);
            float crowded = Closest(a, b);
            Assert.True(crowded > 0.7f, $"with far obstacles the agents came {crowded} m close");
            Assert.True(a.nextPosition.x > 2f && b.nextPosition.x < -2f, $"stuck at {a.nextPosition}, {b.nextPosition}");
            Assert.Equal(2, CrowdMembers(a));
        }

        [Fact]
        public void ALongBoxAndACrowdedNeighbourQueryAreReportedOnce()
        {
            var warnings = new List<string>();
            WithWarnings(warnings, () =>
            {
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    ObstacleSystem.s_WarnedLongBox = false;
                    ObstacleSystem.s_WarnedCandidates = false;
                    Agent();
                    // On unmeasured releases, a box 10 m by 0.2 m needs 50 circles of its width,
                    // more than the 16 it gets (C5).
                    Box(new Vector3(0f, 0.5f, 3f), new Vector3(10f, 1f, 0.2f));
                    Step();
                    Step();
                    Assert.Single(warnings, w => w.Contains("times as long as it is wide"));
                    Assert.DoesNotContain(warnings, w => w.Contains("neighbours its avoidance considers"));

                    // 60 small obstacles near the agent: with the box's 16 circles and the agent, more
                    // than the 64 neighbour candidates the crowd gathers (C7).
                    for (int i = 0; i < 60; i++)
                        Capsule(new Vector3(-7f + 0.1f * i, 1f, -3f), 0.05f);
                    Step();
                    Step();
                    Assert.Single(warnings, w => w.Contains("times as long as it is wide"));
                    Assert.Single(warnings, w => w.Contains("neighbours its avoidance considers"));
                }
            });
        }

        [Fact]
        public void OrientedBoxOutlineAvoidanceContactAndSteeringMatchMeasuredRules()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);

            // 1. A long thin box on the measured release is a single crowd obstacle (no circle cover
            //    or long-box warning), pushing both a touching outside agent and an interior agent
            //    (exercising TryOverlapBox's side < 0f branch) out across the centroid-wedge edge.
            var warnings = new List<string>();
            WithWarnings(warnings, () =>
            {
                ObstacleSystem.s_WarnedLongBox = false;
                var box = Box(new Vector3(0f, 0.5f, 0f), new Vector3(10f, 1f, 0.2f));
                var standing = new GameObject("agent-box");
                m_Objects.Add(standing);
                standing.transform.position = new Vector3(3.5f, 0f, 0.25f);
                var agent = standing.AddComponent<NavMeshAgent>();
                for (int i = 0; i < 15; i++) Step();
                Assert.DoesNotContain(warnings, w => w.Contains("times as long as it is wide"));
                Assert.Equal(2, CrowdMembers(agent));
                // Closest point on the z = +0.1 side is (3.5, 0.1), so combined clearance pushes to z = 0.6.
                Assert.True(Mathf.Abs(agent.nextPosition.x - 3.5f) < 2e-3f && Mathf.Abs(agent.nextPosition.z - 0.6f) < 2e-3f,
                    $"expected (3.5, 0.6), at {agent.nextPosition}");
                Remove(agent);

                // Interior placement: (0.5, 0.05) lies strictly inside [-5, +5] x [-0.1, +0.1]
                // in the top-edge centroid wedge (slope 0.05/0.5 > 0.1/5), so TryOverlapBox hits side < 0f.
                var interiorAg = new DotRecast.Detour.Crowd.DtCrowdAgent(0)
                {
                    npos = new DotRecast.Core.Numerics.RcVec3f(0.5f, 0f, 0.05f),
                    option = new DotRecast.Detour.Crowd.DtCrowdAgentParams { height = 2f, radius = 0.5f },
                };
                var boxAg = new DotRecast.Detour.Crowd.DtCrowdAgent(1)
                {
                    npos = new DotRecast.Core.Numerics.RcVec3f(0f, 0.5f, 0f),
                    option = new DotRecast.Detour.Crowd.DtCrowdAgentParams
                    {
                        boxObstacle = true,
                        boxHalfExtents = new DotRecast.Core.Numerics.RcVec3f(5f, 0.5f, 0.1f),
                        boxAxisX = new DotRecast.Core.Numerics.RcVec3f(1f, 0f, 0f),
                        boxAxisY = new DotRecast.Core.Numerics.RcVec3f(0f, 1f, 0f),
                        boxAxisZ = new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 1f),
                    },
                };
                Assert.True(DotRecast.Detour.Crowd.DtCrowd.TryOverlapBox(interiorAg, boxAg, out float cx, out float cz, out float pen));
                Assert.True(MathF.Abs(cx) < 1e-5f && MathF.Abs(cz - 0.05f) < 1e-5f, $"expected closest (0, 0.05), got ({cx}, {cz})");
                Assert.True(MathF.Abs(pen - 0.55f) < 1e-5f, $"expected interior pen 0.55 (dist 0.05 + radius 0.5), got {pen}");

                Remove(box);
            });

            // 2. Control outside the probe's exact angles: a 22-degree yawed, 3 x 1 x 1 box at y = 0.5
            //    pushes a touching agent along its rotated face normal rather than a circle cover.
            var yawed = Box(new Vector3(0f, 0.5f, 0f), new Vector3(3f, 1f, 1f), Quaternion.Euler(0f, 22f, 0f));
            var yawAgentGo = new GameObject("agent-yaw");
            m_Objects.Add(yawAgentGo);
            // Normal of the +Z face turned +22 deg about Y (left-handed: +Z rotates toward +X): (sin 22, cos 22).
            float rad = 22f * Mathf.Deg2Rad;
            Vector2 n = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            // Place at distance 0.75 from center along face normal (half-depth 0.5 + 0.25 -> overlaps agent radius 0.5).
            yawAgentGo.transform.position = new Vector3(n.x * 0.75f, 0f, n.y * 0.75f);
            var yawAgent = yawAgentGo.AddComponent<NavMeshAgent>();
            for (int i = 0; i < 15; i++) Step();
            Assert.True(Mathf.Abs(yawAgent.nextPosition.x - n.x) < 2e-3f && Mathf.Abs(yawAgent.nextPosition.z - n.y) < 2e-3f,
                $"expected ({n.x:F4}, {n.y:F4}), at {yawAgent.nextPosition}");
            Remove(yawAgent);
            Remove(yawed);

            // 3. Exact-height box spanning [feet, head] strictly produces no clipped points (0 hull vertices).
            var standingExact = new GameObject("agent-exact");
            m_Objects.Add(standingExact);
            standingExact.transform.position = new Vector3(0f, 0f, 0f);
            var exactAgent = standingExact.AddComponent<NavMeshAgent>();
            float feet = exactAgent.nextPosition.y;
            var exactBox = Box(new Vector3(0f, feet + 1f, 0f), new Vector3(1f, 2f, 1f));
            for (int i = 0; i < 5; i++) Step();
            Assert.True(Mathf.Abs(exactAgent.nextPosition.x) < 1e-4f && Mathf.Abs(exactAgent.nextPosition.z) < 1e-4f,
                $"exact-height box should not displace agent, moved to {exactAgent.nextPosition}");
            Remove(exactAgent);
            Remove(exactBox);

            // 4. Non-finite box inputs never produce NaN or throw.
            var badAg = new DotRecast.Detour.Crowd.DtCrowdAgent(0)
            {
                npos = new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 0f),
                option = new DotRecast.Detour.Crowd.DtCrowdAgentParams { height = 2f, radius = 0.5f },
            };
            var badBox = new DotRecast.Detour.Crowd.DtCrowdAgent(1)
            {
                npos = new DotRecast.Core.Numerics.RcVec3f(float.NaN, 1f, 0f),
                option = new DotRecast.Detour.Crowd.DtCrowdAgentParams
                {
                    boxObstacle = true,
                    boxHalfExtents = new DotRecast.Core.Numerics.RcVec3f(0.5f, 0.5f, 0.5f),
                    boxAxisX = new DotRecast.Core.Numerics.RcVec3f(1f, 0f, 0f),
                    boxAxisY = new DotRecast.Core.Numerics.RcVec3f(0f, 1f, 0f),
                    boxAxisZ = new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 1f),
                },
            };
            Assert.False(DotRecast.Detour.Crowd.DtCrowd.TryOverlapBox(badAg, badBox, out _, out _, out _));
        }

        [Fact]
        public void PaddedMovingSegmentMidpointTaperReducesTimeToImpactNearCenter()
        {
            // Moving segment TOI applies midpoint taper 0.5 + 0.5*(1 - 2s)^2, giving half the TOI
            // for a center hit (s = 0.5) compared to an endpoint graze (s = 0 or 1).
            var query = new DotRecast.Detour.Crowd.DtObstacleAvoidanceQuery(16, 8, 16);
            query.AddMovingSegment(-2f, 2f, 2f, 2f, 0f, 0f, 0.5f, 1f);

            // Both rays meet the front face z = 1.5 at tEnter = 1.0 s: center at (0, 1.5) (s = 0.5)
            // and right endpoint at (2, 1.5) (s = 1.0). Midpoint taper halves the center TOI.
            float toiCenter = query.TimeToImpactForTests(new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 1.5f), default, 5f);
            float toiEndpoint = query.TimeToImpactForTests(new DotRecast.Core.Numerics.RcVec3f(2f, 0f, 1.5f), default, 5f);
            Assert.True(MathF.Abs(toiEndpoint - 1.0f) < 1e-5f, $"expected endpoint TOI 1.0, got {toiEndpoint}");
            Assert.True(MathF.Abs(toiCenter - 0.5f) < 1e-5f, $"expected tapered center TOI 0.5, got {toiCenter}");

            var p = new DotRecast.Detour.Crowd.DtObstacleAvoidanceParams { horizTime = 5f, adaptiveDivs = 5, adaptiveRings = 2, adaptiveDepth = 2 };
            query.SampleVelocityAdaptive(3.5f, new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 3.5f),
                new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 3.5f), out var velWithSeg, p, null);
            // Walking straight into a 4 m wide horizontal segment at z = 2 must deflect laterally.
            Assert.True(MathF.Abs(velWithSeg.X) > 0.5f, $"expected lateral deflection around segment, got {velWithSeg}");
        }

        [Theory]
        [InlineData(10000f, 0f, 0)]
        [InlineData(10000f, 0f, 9999)]
        [InlineData(0.5f, -10000f, 9999)]
        public void LargeAndFastObstaclesHaveBoundedGridStorageWithoutLosingCoverage(float radius, float speed, int distantCount)
        {
            string release = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            try
            {
                var agent = Agent();
                agent.speed = 1f;
                var obstacle = Capsule(new Vector3(2f, 1f, 2f), radius);
                obstacle.velocity = new Vector3(speed, 0f, speed);
                for (int i = 0; i < distantCount; i++) Capsule(new Vector3(1000000f + 2f * i, 1f, 1000000f), 0.5f);
                Step();
                var crowd = AgentSystem.CrowdOf(agent.m_Surface);
                // Two footprints, each at most four cells per axis after sizing from both.
                int entries = crowd.GetGrid().StoredItemCount;
                Assert.InRange(entries, 1, distantCount == 0 ? 2 * 4 * 4 : 2 * 64);
                var member = crowd.GetActiveAgents().Single(a => ReferenceEquals(a.option.userData, obstacle));
                Assert.Contains(member, Enumerable.Range(0, agent.m_Crowd.nneis).Select(i => agent.m_Crowd.neis[i].agent));
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        /// <summary>Runs <paramref name="body"/> collecting the warnings logged, forwarding every call.</summary>
        static void WithWarnings(List<string> warnings, Action body)
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            logger.logHandler = new Capture(previous, warnings);
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                body();
            }
            finally
            {
                logger.logHandler = previous;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
            }
        }

        [Theory]
        // Unity 6000.0.71f1 (tests/projects/navpath obstacle-shapes caplying, captilt, captiltx):
        // a lying or tilted capsule pushes an agent to the two radii from its shaft seen from
        // above, along +x when the agent stands on it.
        [InlineData(0f, 0f, 0.3f, 0f, 1f, 0f)]          // upright: from its axis (obstacle-separation)
        [InlineData(90f, 0f, 0.6f, -0.2f, 0.6f, -1f)]   // lying along x, beside the shaft
        [InlineData(90f, 0f, 0f, 0f, 2f, 0f)]           // on the shaft: along +x past its end
        [InlineData(90f, 0f, 1.2f, 0.45f, 1.406f, 0.914f)] // beyond the end, towards its sphere
        [InlineData(45f, 0f, 0f, 0f, 1.7071f, 0f)]      // tilted 45 degrees about z, on the shaft
        [InlineData(0f, 45f, -0.25f, 0.1f, -1f, 0.1f)]  // tilted 45 degrees about x: the shaft along z
        public void ALyingOrTiltedCapsulePushesAgentsOutOfItsShaftSeenFromAbove(float aboutZ, float aboutX,
            float x, float z, float expectedX, float expectedZ)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = new Vector3(0f, 1f, 0f);
            go.transform.rotation = Quaternion.Euler(aboutX, 0f, aboutZ);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.5f;
            obstacle.height = 3f;
            obstacle.carving = false;
            // Placed with no destination, as the probe's agents are.
            var standing = new GameObject("agent");
            m_Objects.Add(standing);
            standing.transform.position = new Vector3(x, 0f, z);
            var agent = standing.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            for (int i = 0; i < 20; i++) Step();
            Assert.True(Mathf.Abs(agent.nextPosition.x - expectedX) < 2e-3f && Mathf.Abs(agent.nextPosition.z - expectedZ) < 2e-3f,
                $"expected ({expectedX}, {expectedZ}), at {agent.nextPosition}");
        }

        [Theory]
        // Unity 6000.0.71f1 (tests/projects/navpath obstacle-shapes: cap45h382, cap45h384, cap30h6,
        // cap60h8, cap5h6, cap45sunk, cap45raised, cap135sunk, cap5low, cap10low, cap10high,
        // caplyinglow, caplyinghigh, caplyingabove):
        // capsules clip their shaft to the agent's height span with the horizontal cap margin
        // (1 - axis.y) * r; shafts wholly beyond the agent's height use clipped radius sqrt(r^2 - dy^2)
        // and collapse to a single inward-pushing cap circle only when axis.y > 0.99.
        [InlineData(45f, 3.82f, 1f, 0f, 0f, 1.9970f, 0f)]      // 45 deg h3.82 (inside height)
        [InlineData(45f, 3.84f, 1f, 0f, 0f, 2.0041f, 0f)]      // 45 deg h3.84 (4 mm outside: continuous)
        [InlineData(45f, 3.84f, 1f, 1.5f, 0f, 2.0041f, 0f)]    // 45 deg h3.84 off-center
        [InlineData(30f, 6f, 1f, -1.05f, 0f, -1.6160f, 0f)]    // 30 deg pole clipped at both ends
        [InlineData(30f, 6f, 1f, -1.6f, 0f, -1.6160f, 0f)]     // 30 deg pole inside cap-margin reach
        [InlineData(30f, 6f, 1f, -1.7f, 0f, -1.7f, 0f)]        // 30 deg pole beyond cap-margin reach
        [InlineData(60f, 8f, 1f, -3.0f, 0f, -3.1651f, 0f)]     // 60 deg pole clipped at both ends
        [InlineData(45f, 4f, 0f, 0.8f, 0f, 1.1464f, 0f)]       // sunk: bottom clipped, top inside
        [InlineData(135f, 4f, 0f, 0.8f, 0f, 2.0607f, 0f)]      // flipped axis (axis.y < 0)
        [InlineData(5f, 3f, -1.25f, 0f, 0f, -1.0179f, 0f)]     // 5 deg (axis.y > 0.99): single cap, inward push
        [InlineData(10f, 3f, -1.25f, 0f, 0f, 0.7502f, 0f)]     // 10 deg (axis.y <= 0.99): two-point segment, outward
        [InlineData(10f, 3f, 3.25f, 0f, 0f, -0.7983f, 0f)]     // 10 deg above head: two-point segment
        [InlineData(90f, 3f, -0.25f, 0.6f, -0.2f, 0.6f, -0.9330f)] // lying below feet: clipped radius 0.433
        [InlineData(90f, 3f, 2.25f, 0.6f, -0.2f, 0.6f, -0.9330f)]  // lying above head: clipped radius 0.433
        [InlineData(90f, 3f, 2.6f, 0f, 0f, 0f, 0f)]            // lying beyond radius above head: no contact
        public void AClippedOrOutOfSpanCapsuleFollowsTheMeasuredClippingRule(float aboutZ, float height,
            float centerY, float x, float z, float expectedX, float expectedZ)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var standing = new GameObject("agent");
            m_Objects.Add(standing);
            standing.transform.position = new Vector3(x, 0f, z);
            var agent = standing.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            float feet = agent.nextPosition.y;
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = new Vector3(0f, feet + centerY, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, aboutZ);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.5f;
            obstacle.height = height;
            obstacle.carving = false;
            for (int i = 0; i < 20; i++) Step();
            Assert.True(Mathf.Abs(agent.nextPosition.x - expectedX) < 2e-3f && Mathf.Abs(agent.nextPosition.z - expectedZ) < 2e-3f,
                $"expected ({expectedX}, {expectedZ}), at {agent.nextPosition}");
        }

        [Fact]
        public void ATiltedCapsuleRanksByItsHorizontalExtentsAheadOfCloserObstacles()
        {
            // Unity 6000.0.71f1 (tests/projects/navpath obstacle-shapes caprank): 8 upright obstacles
            // at 1.6 m do not crowd out a lying capsule centered 2.5 m away whose shaft reaches the agent.
            using var release = TestRelease.Pin(TestRelease.Measured);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Capsule(new Vector3(-0.3f + Mathf.Cos(a) * 1.6f, 1f, Mathf.Sin(a) * 1.6f), 0.2f);
            }
            var bar = new GameObject("bar");
            m_Objects.Add(bar);
            bar.transform.position = new Vector3(2.5f, 1f, 0f);
            bar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var ob = bar.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Capsule;
            ob.radius = 0.5f;
            ob.height = 6f;
            ob.carving = false;

            var standing = new GameObject("agent");
            m_Objects.Add(standing);
            standing.transform.position = new Vector3(-0.3f, 0f, 0f);
            var agent = standing.AddComponent<NavMeshAgent>();
            for (int i = 0; i < 10; i++) Step();
            Assert.True(Mathf.Abs(agent.nextPosition.x - (-1f)) < 2e-3f && Mathf.Abs(agent.nextPosition.z) < 2e-3f,
                $"expected (-1, 0), at {agent.nextPosition}");
        }

        [Fact]
        public void UnmeasuredReleaseUsesLegacyCircleForTiltedCapsule()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = new GameObject("obstacle");
            m_Objects.Add(go);
            go.transform.position = new Vector3(0f, 1f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.5f;
            obstacle.height = 3f;
            obstacle.carving = false;
            var standing = new GameObject("agent");
            m_Objects.Add(standing);
            standing.transform.position = new Vector3(1.2f, 0f, 0f);
            var agent = standing.AddComponent<NavMeshAgent>();
            for (int i = 0; i < 10; i++) Step();
            // Plain circle at center has combined radius 1.0, so x = 1.2 does not contact.
            Assert.True(Mathf.Abs(agent.nextPosition.x - 1.2f) < 2e-3f, $"moved to {agent.nextPosition}");
        }

        [Fact]
        public void ClippedCapsuleHandlesOutOfProbeAngleAndNonFiniteInputs()
        {
            // Control outside the probe's exact angles/radii: 20 deg tilt, radius 0.7, height 5.0
            // at center.y = 1.0. Half-shaft = 1.8, u.y = cos(20 deg) = 0.9396926, u.x = -sin(20 deg) = -0.3420201.
            // Bottom endpoint y0 = 1 - 1.8 * u.y = -0.69145 (< 0); top y1 = 2.69145 (> 2.0).
            // Clipped endpoints reach x = +/- (1.0 + (1 - u.y) * 0.7) * tan(20 deg) = +/- 0.37934 m,
            // so combined radius 0.5 + 0.7 = 1.2 m pushes an agent at x = -1.35 to -1.5793 m.
            using var release = TestRelease.Pin(TestRelease.Measured);
            var standing = new GameObject("agent20");
            m_Objects.Add(standing);
            standing.transform.position = new Vector3(-1.35f, 0f, 0f);
            var agent = standing.AddComponent<NavMeshAgent>();
            float feet = agent.nextPosition.y;

            var go = new GameObject("ob20");
            m_Objects.Add(go);
            go.transform.position = new Vector3(0f, feet + 1f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.7f;
            obstacle.height = 5f;
            obstacle.carving = false;

            for (int i = 0; i < 20; i++) Step();
            Assert.True(Mathf.Abs(agent.nextPosition.x - (-1.5793f)) < 2e-3f && Mathf.Abs(agent.nextPosition.z) < 2e-3f,
                $"expected (-1.5793, 0), at {agent.nextPosition}");

            // Non-finite obstacle parameters must never produce NaN displacement.
            var badAg = new DotRecast.Detour.Crowd.DtCrowdAgent(0)
            {
                npos = new DotRecast.Core.Numerics.RcVec3f(0f, 0f, 0f),
                option = new DotRecast.Detour.Crowd.DtCrowdAgentParams { height = 2f },
            };
            var badCap = new DotRecast.Detour.Crowd.DtCrowdAgent(1)
            {
                npos = new DotRecast.Core.Numerics.RcVec3f(0f, float.NaN, 0f),
                option = new DotRecast.Detour.Crowd.DtCrowdAgentParams
                {
                    radius = 0.5f,
                    capsuleObstacle = true,
                    capsuleAxis = new DotRecast.Core.Numerics.RcVec3f(0f, 1f, 0f),
                    capsuleHalfShaft = 1f,
                },
            };
            Assert.Equal(0, DotRecast.Detour.Crowd.DtCrowd.ClipCapsule(badAg, badCap,
                out _, out _, out _, out _, out _));
        }

        [Fact]
        public void OutOfSpanTiltedCapsuleBranchesThresholdAndYawedRankingMatchMeasuredControls()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);

            Vector3 RunOne(float radius, float height, Vector3 euler, Vector3 offset, Vector2 agentStart, float speed = 3.5f)
            {
                var standing = new GameObject("agent");
                m_Objects.Add(standing);
                standing.transform.position = new Vector3(agentStart.x, 0f, agentStart.y);
                var agent = standing.AddComponent<NavMeshAgent>();
                agent.speed = speed;
                float feet = agent.nextPosition.y;

                var go = new GameObject("ob");
                m_Objects.Add(go);
                go.transform.position = new Vector3(offset.x, feet + offset.y, offset.z);
                go.transform.rotation = Quaternion.Euler(euler);
                var ob = go.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Capsule;
                ob.radius = radius;
                ob.height = height;
                ob.carving = false;

                for (int i = 0; i < 3; i++) Step();
                var result = agent.nextPosition;
                Remove(agent);
                Object.DestroyImmediate(go);
                return result;
            }

            // Finding 1: 45-deg short capsule below feet jumps when -y0 crosses r (centerY -0.25 vs -0.29),
            // above head keeps unclipped p1 when y1 - maxY < r (centerY 2.25), and 45-deg h=3.0 at -0.75 clips p1.
            var shortLow = RunOne(0.5f, 1.6f, new Vector3(0f, 0f, 45f), new Vector3(0f, -0.25f, 0f), new Vector2(0.8f, 0f));
            var shortLow2 = RunOne(0.5f, 1.6f, new Vector3(0f, 0f, 45f), new Vector3(0f, -0.29f, 0f), new Vector2(0.8f, 0f));
            var shortHigh = RunOne(0.5f, 1.6f, new Vector3(0f, 0f, 45f), new Vector3(0f, 2.25f, 0f), new Vector2(-0.8f, 0f));
            var tiltLow = RunOne(0.5f, 3f, new Vector3(0f, 0f, 45f), new Vector3(0f, -0.75f, 0f), new Vector2(0.3f, 0f));
            Assert.True(Mathf.Abs(shortLow.x - 1.2107f) < 2e-3f, $"shortLow {shortLow}");
            Assert.True(Mathf.Abs(shortLow2.x - 0.8503f) < 2e-3f, $"shortLow2 {shortLow2}");
            Assert.True(Mathf.Abs(shortHigh.x - (-1.2107f)) < 2e-3f, $"shortHigh {shortHigh}");
            Assert.True(Mathf.Abs(tiltLow.x - 0.3946f) < 2e-3f, $"tiltLow {tiltLow}");

            // Finding 3 & Q2: 8 deg (cos > 0.99) pushes inward across the cap center; 8.5 deg (cos <= 0.99)
            // and 45-deg sphere-like (h <= 2r) push outward.
            var deg8 = RunOne(0.5f, 3f, new Vector3(0f, 0f, 8f), new Vector3(0f, -1.25f, 0f), new Vector2(-0.3f, 0.1f));
            var deg85 = RunOne(0.5f, 3f, new Vector3(0f, 0f, 8.5f), new Vector3(0f, -1.25f, 0f), new Vector2(-0.3f, 0.1f));
            var sphereTilt = RunOne(0.5f, 0.5f, new Vector3(0f, 0f, 45f), new Vector3(0f, -0.25f, 0f), new Vector2(0.1f, 0f));
            Assert.True(deg8.x > 0.5f && deg8.z < -0.3f, $"8 deg should push inward (+x,-z), got {deg8}");
            Assert.True(deg85.x < -0.8f && deg85.z > 0.4f, $"8.5 deg should push outward (-x,+z), got {deg85}");
            Assert.True(sphereTilt.x > 0.9f, $"45 deg sphere-like capsule should push outward (+x), got {sphereTilt}");

            // Finding 2: 45-deg yawed lying bar (r=0.5, h=6) with zero-speed agent separates sqrt(ex^2+ez^2)=3.207
            // from true horizontal reach (3.0) and 3D half-diagonal (3.246).
            var yawAdmit = RunOne(0.5f, 6f, new Vector3(0f, 45f, 90f), new Vector3(2.4395f, 1.0f, -2.4395f), Vector2.zero, speed: 0f);
            var yawMidReject = RunOne(0.5f, 6f, new Vector3(0f, 45f, 90f), new Vector3(2.4395f, 1.42f, -2.4395f), Vector2.zero, speed: 0f);
            var yawFarReject = RunOne(0.5f, 6f, new Vector3(0f, 45f, 90f), new Vector3(2.4395f, 1.6f, -2.4395f), Vector2.zero, speed: 0f);
            Assert.True(yawAdmit.x < -0.02f && yawAdmit.z > 0.02f, $"ry=1.0 should be admitted and pushed, got {yawAdmit}");
            Assert.True(Mathf.Abs(yawMidReject.x) < 1e-4f && Mathf.Abs(yawMidReject.z) < 1e-4f, $"ry=1.42 should be rejected, got {yawMidReject}");
            Assert.True(Mathf.Abs(yawFarReject.x) < 1e-4f && Mathf.Abs(yawFarReject.z) < 1e-4f, $"ry=1.6 should be rejected, got {yawFarReject}");

            // Zero-radius shaft (non-single-cap) with a tiny negative offset keeps its -X normal.
            var zeroRadius = RunOne(0f, 2f, Vector3.zero, new Vector3(0f, 1f, 0f), new Vector2(-5e-5f, 0f));
            Assert.True(zeroRadius.x < -0.4f, $"zero-radius shaft at -5e-5 should push along -X, got {zeroRadius}");
        }

        [Fact]
        public void DisablingTheObstacleRestoresTheStraightWalk()
        {
            var obstacle = Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
            var agent = Agent();
            Assert.True(Walk(agent).Deviation > 0.75f);
            Remove(agent);

            obstacle.enabled = false;
            agent = Agent();
            var walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"disabled obstacle: {walk.Deviation} m from the line");
            Assert.True(walk.Arrived);
            Assert.Equal(1, CrowdMembers(agent));
            Remove(agent);

            // The GameObject deactivated: the same.
            obstacle.enabled = true;
            obstacle.gameObject.SetActive(false);
            agent = Agent();
            walk = Walk(agent);
            Assert.True(walk.Deviation < 1e-3f, $"inactive obstacle: {walk.Deviation} m from the line");
            Assert.Equal(1, CrowdMembers(agent));
        }

        // ------------------------------------------------------------------ carving

        [Fact]
        public void ACarvingObstacleIsCarvedInsteadOfAvoided()
        {
            // Documented: a carving obstacle's hole is recognised by the pathfinding, so paths are
            // plotted around it. Once cut, the obstacle is no longer among the avoidance
            // obstacles (Unbound's choice, unmeasured).
            var obstacle = Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
            obstacle.carving = true;
            var agent = Agent();
            Step();
            foreach (var corner in agent.path.corners) Assert.True(Mathf.Abs(corner.z) < 1e-4f);
            ObstacleSystem.UpdateCarving();
            ObstacleSystem.UpdateCarving();
            Assert.True(ObstacleSystem.IsCarved(obstacle));
            Step();
            Assert.Contains(agent.path.corners, c => Mathf.Abs(c.z) > 0.5f);
            Assert.Equal(NavMeshPathStatus.PathComplete, agent.pathStatus);
            Assert.False(agent.isPathStale);
            Assert.Equal(1, CrowdMembers(agent));
            var walk = Walk(agent, clearanceObstacle: obstacle);
            Assert.True(walk.Deviation > 0.75f, $"went through the carving obstacle, {walk.Deviation} m from the line");
            Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
            Assert.Equal(1, CrowdMembers(agent));
        }

        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Next;
            readonly List<string> m_Warnings;

            internal Capture(ILogHandler next, List<string> warnings)
            {
                m_Next = next;
                m_Warnings = warnings;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning)
                    m_Warnings.Add(args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                m_Next.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Next.LogException(exception, context);
        }
    }
}
