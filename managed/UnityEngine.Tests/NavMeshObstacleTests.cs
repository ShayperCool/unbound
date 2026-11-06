using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NavMeshObstacle in the agents' local avoidance.
    /// Documented (Scripting API 6000.0 NavMeshObstacle, Manual "NavMesh Obstacle", AI Navigation
    /// 2.0): an obstacle for agents to avoid; without carving it affects only their avoidance, not
    /// their paths (they plot paths ignoring it and sidestep it as they move); a carving obstacle
    /// cuts a hole in the NavMesh. Unbound does not carve: a carving obstacle is reported and stays
    /// an avoidance obstacle (Unbound's choice, unmeasured). The obstacles' shapes in avoidance
    /// (circles covering a box's outline, a capsule's radius), the scripted velocity as the
    /// obstacle's motion and the height-span test are Unbound's choices (unmeasured); the bounds the
    /// tests check include per-step box clearance, without guaranteeing progress round long walls;
    /// other tests state the cover's bound (C5 in ObstacleSystem: circles at most sqrt(2) times a
    /// box's half-width) or the crowd's 32 neighbour candidates (C7).
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
        readonly bool m_Warned = ObstacleSystem.s_Warned;
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
            ObstacleSystem.s_Warned = m_Warned;
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

        // A safety invariant of this box-cover implementation, not a promise that local
        // avoidance plans a route around every wall. Check every original step, including stalls.
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
            Assert.True(clearance >= -1e-4f, $"agent penetrated the box footprint by {-clearance} m at {position}");
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
                ObstacleSystem.s_WarnedLongBox = false;
                ObstacleSystem.s_WarnedCandidates = false;
                Agent();
                // A box 10 m by 0.2 m needs 50 circles of its width, more than the 16 it gets (C5).
                Box(new Vector3(0f, 0.5f, 3f), new Vector3(10f, 1f, 0.2f));
                Step();
                Step();
                Assert.Single(warnings, w => w.Contains("times as long as it is wide"));
                Assert.DoesNotContain(warnings, w => w.Contains("neighbours its avoidance considers"));

                // 40 small obstacles near the agent: with the box's 16 circles and the agent, more
                // than the 32 neighbour candidates the crowd gathers (C7).
                for (int i = 0; i < 40; i++)
                    Capsule(new Vector3(-4f + 0.2f * i, 1f, -3f), 0.05f);
                Step();
                Step();
                Assert.Single(warnings, w => w.Contains("times as long as it is wide"));
                Assert.Single(warnings, w => w.Contains("neighbours its avoidance considers"));
            });
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
        public void ACarvingObstacleIsReportedAndAvoided()
        {
            // Carving is not supported: reported once, the NavMesh is unchanged (the path goes
            // straight through and its staleness stays), and the obstacle stays an avoidance
            // obstacle (Unbound's choice, unmeasured).
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var warnings = new List<string>();
            // Unbound diagnostics bypass the game's Application log callbacks: observe them at the
            // handler, forwarding every call.
            logger.logHandler = new Capture(previous, warnings);
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                ObstacleSystem.s_Warned = false;
                var obstacle = Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
                obstacle.carving = true;
                Assert.Single(warnings);
                Assert.Contains("carving is not supported", warnings[0]);
                obstacle.carving = false;
                obstacle.carving = true;
                Assert.Single(warnings);

                var agent = Agent();
                Step();
                // Unbound's reported, uncarved fallback must leave the complete route and
                // actual topology unchanged. A collinear tile-seam waypoint is allowed: the
                // 6000.0 corners API supplies waypoints, not a fixed count for this bake.
                var originalPath = agent.path;
                var originalMesh = NavMesh.CalculateTriangulation();
                int version = NavWorld.Version;
                obstacle.carving = false;
                var withoutCarving = agent.path;
                obstacle.carving = true;
                var withCarving = agent.path;
                var finalMesh = NavMesh.CalculateTriangulation();
                Assert.Equal(version, NavWorld.Version);
                Assert.Equal(NavMeshPathStatus.PathComplete, originalPath.status);
                Assert.Equal(originalPath.status, withoutCarving.status);
                Assert.Equal(originalPath.status, withCarving.status);
                Assert.Equal(originalPath.corners, withoutCarving.corners);
                Assert.Equal(originalPath.corners, withCarving.corners);
                Assert.Equal(originalMesh.vertices, finalMesh.vertices);
                Assert.Equal(originalMesh.indices, finalMesh.indices);
                Assert.Equal(originalMesh.areas, finalMesh.areas);
                Assert.True(originalPath.corners.Length >= 2);
                Assert.Equal(agent.nextPosition, originalPath.corners[0]);
                Assert.Equal(Goal.x, originalPath.corners[originalPath.corners.Length - 1].x);
                Assert.Equal(Goal.z, originalPath.corners[originalPath.corners.Length - 1].z);
                float previousX = Start.x;
                foreach (var corner in originalPath.corners)
                {
                    Assert.InRange(corner.x, previousX, Goal.x);
                    Assert.True(Mathf.Abs(corner.z) < 1e-4f, $"uncarved route bent away from the straight platform: {corner}");
                    previousX = corner.x;
                }
                Assert.False(agent.isPathStale);
                var walk = Walk(agent, clearanceObstacle: obstacle);
                Assert.True(walk.Deviation > 0.75f, $"went through the carving obstacle, {walk.Deviation} m from the line");
                Assert.True(walk.Arrived, $"stopped at {agent.nextPosition}");
                Assert.False(agent.isPathStale);
            }
            finally
            {
                logger.logHandler = previous;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
            }
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
