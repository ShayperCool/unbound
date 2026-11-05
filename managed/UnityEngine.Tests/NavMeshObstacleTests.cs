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
    /// tests check are wide enough for any covering of the shape, not one fixed trajectory.
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
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        static void Step() => AgentSystem.Step();

        NavMeshAgent Agent()
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.transform.position = Start;
            var agent = go.AddComponent<NavMeshAgent>();
            Assert.True(agent.isOnNavMesh);
            Assert.True(agent.SetDestination(Goal));
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
        static (float Deviation, bool Passed, bool Arrived) Walk(NavMeshAgent agent, int steps = 600)
        {
            float deviation = 0f;
            bool passed = false;
            for (int i = 0; i < steps; i++)
            {
                Step();
                var p = agent.nextPosition;
                deviation = Mathf.Max(deviation, Mathf.Abs(p.z));
                if (p.x > 2f) passed = true;
                if (new Vector2(p.x - Goal.x, p.z - Goal.z).magnitude <= 1e-2f) return (deviation, passed, true);
            }
            return (deviation, passed, false);
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
            // A box 5 m long across the path takes the agent further out than a 1 m box; turned a
            // quarter, a box 5 m along x is the same; raised above the agents' height it is no
            // obstacle; a capsule's radius counts.
            var small = Box(new Vector3(0f, 0.5f, 0.2f), Vector3.one);
            var agent = Agent();
            float smallDeviation = Walk(agent).Deviation;
            Assert.True(smallDeviation > 0.75f && smallDeviation < 2f, $"1 m box: {smallDeviation} m from the line");
            Remove(agent);
            Remove(small);

            var across = Box(new Vector3(0f, 0.5f, 0.2f), new Vector3(1f, 1f, 5f));
            agent = Agent();
            var walk = Walk(agent);
            Assert.True(walk.Deviation > 2.6f, $"5 m box: {walk.Deviation} m from the line");
            Assert.True(walk.Passed);
            Remove(agent);
            Remove(across);

            var turned = Box(new Vector3(0f, 0.5f, 0.2f), new Vector3(5f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            agent = Agent();
            walk = Walk(agent);
            Assert.True(walk.Deviation > 2.6f, $"turned 5 m box: {walk.Deviation} m from the line");
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
                Assert.Equal(2, agent.path.corners.Length);
                Assert.False(agent.isPathStale);
                var walk = Walk(agent);
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
