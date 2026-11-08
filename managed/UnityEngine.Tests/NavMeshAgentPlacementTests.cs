using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>When an agent's transform goes onto the NavMesh. Documented (Scripting API 6000.0,
    /// NavMeshAgent.Warp, updatePosition, nextPosition): Warp moves the agent to a position;
    /// updatePosition lets the agent move the transform; nextPosition can be set to move the
    /// agent. Measured (tests/projects/navcost place.*, Unity 6000.0.71f1): an agent enabled by its
    /// scene, by activation or by enabled = true, and one warped, keeps its transform where it is
    /// until the next frame's early AI update (PreUpdate), while its nextPosition is on the NavMesh
    /// at once; AddComponent and setting baseOffset write the transform at once; a transform moved
    /// by script is followed by nextPosition at once and written back at the next early update;
    /// turning updatePosition on writes it at the next early update; the update after the scripts
    /// leaves standing agents' transforms alone. The level is one runtime-built 12 m ground (top at
    /// y = 0) away from other tests' levels.</summary>
    [Collection("Frames")]
    public sealed class NavMeshAgentPlacementTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 Center = new Vector3(-40f, 0f, 40f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly float m_Height;

        public NavMeshAgentPlacementTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            var ground = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(12f, 1f, 12f),
                transform = Matrix4x4.Translate(Center + new Vector3(0f, -0.5f, 0f)),
                area = 0,
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { ground },
                new Bounds(Center, new Vector3(14f, 4f, 14f)), Vector3.zero, Quaternion.identity);
            m_Data.Add(NavMesh.AddNavMeshData(data));
            Assert.True(NavMesh.SamplePosition(Center, out var hit, 1f, NavMesh.AllAreas));
            m_Height = hit.position.y;
            Assert.NotEqual(0.3f, m_Height);
        }

        public void Dispose()
        {
            foreach (var go in m_Objects) Object.DestroyImmediate(go);
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
        }

        /// <summary>A GameObject at <paramref name="local"/> (from the ground's centre), with an agent
        /// added while it is <paramref name="active"/>.</summary>
        NavMeshAgent Agent(Vector3 local, bool active = true, Action<NavMeshAgent> before = null)
        {
            var go = new GameObject("agent");
            m_Objects.Add(go);
            go.SetActive(active);
            go.transform.position = Center + local;
            var agent = go.AddComponent<NavMeshAgent>();
            before?.Invoke(agent);
            return agent;
        }

        /// <summary>One frame of the agents: the early update, then the update after the scripts.</summary>
        static void Frame()
        {
            AgentSystem.SyncTransforms();
            AgentSystem.Step();
        }

        [Fact]
        public void AddComponentPlacesTheTransformAtOnce()
        {
            var agent = Agent(new Vector3(0f, 0.3f, 0f));
            Assert.True(agent.isOnNavMesh);
            Assert.Equal(m_Height, agent.transform.position.y, 4);
        }

        [Fact]
        public void ActivationKeepsTheTransformUntilTheEarlyUpdate()
        {
            var agent = Agent(new Vector3(1f, 0.3f, 0f), active: false);
            // Not enabled: no NavMesh position, nextPosition is the transform's.
            Assert.False(agent.isOnNavMesh);
            Assert.Equal(Center + new Vector3(1f, 0.3f, 0f), agent.nextPosition);
            agent.gameObject.SetActive(true);
            Assert.True(agent.isOnNavMesh);
            Assert.Equal(m_Height, agent.nextPosition.y, 4);
            Assert.Equal(0.3f, agent.transform.position.y, 4);
            // The update after the scripts leaves a standing agent's transform alone.
            AgentSystem.Step();
            Assert.Equal(0.3f, agent.transform.position.y, 4);
            AgentSystem.SyncTransforms();
            Assert.Equal(m_Height, agent.transform.position.y, 4);
        }

        [Fact]
        public void ReenablingKeepsTheTransformUntilTheEarlyUpdate()
        {
            var agent = Agent(new Vector3(2f, 0f, 0f));
            agent.enabled = false;
            agent.transform.position = Center + new Vector3(2f, 0.4f, 0f);
            agent.enabled = true;
            Assert.Equal(0.4f, agent.transform.position.y, 4);
            Frame();
            Assert.Equal(m_Height, agent.transform.position.y, 4);
        }

        [Fact]
        public void WarpKeepsThePositionGivenUntilTheEarlyUpdate()
        {
            var agent = Agent(new Vector3(-1f, 0f, 0f));
            var target = Center + new Vector3(-1f, 0.5f, 1f);
            Assert.True(agent.Warp(target));
            Assert.Equal(target, agent.transform.position);
            Assert.Equal(m_Height, agent.nextPosition.y, 4);
            AgentSystem.Step();
            Assert.Equal(target, agent.transform.position);
            AgentSystem.SyncTransforms();
            Assert.Equal(new Vector3(target.x, m_Height, target.z), agent.transform.position);
        }

        [Fact]
        public void AScriptMoveIsFollowedAtOnceAndWrittenBackAtTheEarlyUpdate()
        {
            var agent = Agent(new Vector3(-2f, 0f, -2f));
            var moved = Center + new Vector3(-2f, 0.5f, -1.5f);
            agent.transform.position = moved;
            Assert.Equal(new Vector3(moved.x, m_Height, moved.z), agent.nextPosition);
            AgentSystem.Step();
            Assert.Equal(moved, agent.transform.position);
            AgentSystem.SyncTransforms();
            Assert.Equal(new Vector3(moved.x, m_Height, moved.z), agent.transform.position);
        }

        [Fact]
        public void AWalkingAgentKeepsAScriptMove()
        {
            var agent = Agent(new Vector3(-4f, 0f, 3f));
            agent.SetDestination(Center + new Vector3(4f, 0f, 3f));
            for (int i = 0; i < 5; i++) Frame();
            var p = agent.transform.position;
            agent.transform.position = new Vector3(p.x, 0.5f, p.z - 0.4f);
            Frame();
            Assert.Equal(p.z - 0.4f, agent.transform.position.z, 2);
            Assert.True(agent.transform.position.x > p.x);
            Assert.Equal(m_Height, agent.transform.position.y, 4);
        }

        [Fact]
        public void BaseOffsetMovesTheTransformAtOnce()
        {
            var agent = Agent(new Vector3(0f, 0f, 2f));
            agent.baseOffset = 1f;
            Assert.Equal(m_Height + 1f, agent.transform.position.y, 4);
            Assert.Equal(m_Height + 1f, agent.nextPosition.y, 4);
        }

        [Fact]
        public void TurningUpdatePositionOnWritesAtTheEarlyUpdate()
        {
            var agent = Agent(new Vector3(3f, 0.3f, 2f), active: false, before: a => a.updatePosition = false);
            agent.gameObject.SetActive(true);
            Frame();
            Assert.Equal(0.3f, agent.transform.position.y, 4);
            agent.updatePosition = true;
            Assert.Equal(0.3f, agent.transform.position.y, 4);
            AgentSystem.Step();
            Assert.Equal(0.3f, agent.transform.position.y, 4);
            AgentSystem.SyncTransforms();
            Assert.Equal(m_Height, agent.transform.position.y, 4);
        }
    }
}
