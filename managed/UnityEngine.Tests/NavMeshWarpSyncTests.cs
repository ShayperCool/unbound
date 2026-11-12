using System;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using UnityEngine.AI;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Connected floor/ramp control without a height mesh: the nearest polygon is the
    /// floor edge, but following the requested transform can continue onto the ramp. The coupled
    /// and uncoupled cases are also recorded by tests/projects/navbaked warp.sync, Unity 6000.0.71f1.</summary>
    [Collection("Frames")]
    public sealed class NavMeshWarpSyncTests
    {
        [Theory]
        [InlineData(true, 0f, 0f)]
        [InlineData(false, 0f, 0f)]
        [InlineData(true, 0.3f, 90f)]
        [InlineData(false, 0.3f, 90f)]
        public void WarpRetainsTheTransformRequestOnlyWhenCoupled(bool coupled, float offset, float angle)
        {
            var origin = new Vector3(-160f, 0f, 150f);
            var rotation = Quaternion.Euler(0f, angle, 0f);
            var data = ConnectedFloorAndRamp();
            var instance = NavMesh.AddNavMeshData(data, origin, rotation);
            var go = new GameObject("warp sync agent");
            try
            {
                go.transform.position = origin + rotation * new Vector3(-1f, 0f, 0f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.baseOffset = offset;
                agent.updatePosition = coupled;
                var requested = origin + rotation * new Vector3(0.25f, 0f, 0f) + Vector3.up * offset;
                Assert.True(agent.Warp(requested));
                AssertNear(requested, agent.transform.position);
                // The fixture discriminates placement from the subsequent transform move.
                AssertNear(origin, agent.m_NextPosition);
                var expected = origin + rotation * (coupled ? new Vector3(0.25f, 0.375f, 0f) : Vector3.zero) + Vector3.up * offset;
                AssertNear(expected, agent.nextPosition);
                AssertNear(requested, agent.transform.position);
                AgentSystem.SyncTransforms();
                AssertNear(coupled ? expected : requested, agent.transform.position);
                AssertNear(expected, agent.nextPosition);
                if (!coupled)
                {
                    // Re-enabling must move a diverged transform to the simulation, not vice versa.
                    agent.updatePosition = true;
                    AssertNear(requested, agent.transform.position);
                    AgentSystem.SyncTransforms();
                    AssertNear(expected, agent.transform.position);
                    AssertNear(expected, agent.nextPosition);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        static void AssertNear(Vector3 expected, Vector3 actual) =>
            Assert.True((expected - actual).sqrMagnitude < 1e-8f, $"expected {expected}, actual {actual}");

        static NavMeshData ConnectedFloorAndRamp()
        {
            const int VerticesPerPoly = 6;
            var polygons = new int[2 * VerticesPerPoly * 2];
            Array.Fill(polygons, 0xffff);
            int[][] corners = { new[] { 0, 1, 2, 3 }, new[] { 3, 2, 4, 5 } };
            for (int polygon = 0; polygon < corners.Length; polygon++)
                for (int vertex = 0; vertex < corners[polygon].Length; vertex++)
                    polygons[polygon * VerticesPerPoly * 2 + vertex] = corners[polygon][vertex];
            polygons[VerticesPerPoly + 2] = 1;
            polygons[VerticesPerPoly * 3] = 0;
            var tile = DtNavMeshBuilder.CreateNavMeshData(new DtNavMeshCreateParams
            {
                verts = new[] { 0, 0, 0, 0, 0, 4, 2, 0, 4, 2, 0, 0, 4, 3, 4, 4, 3, 0 },
                vertCount = 6,
                polys = polygons,
                polyAreas = new[] { 0, 0 },
                polyFlags = new[] { 1, 1 },
                polyCount = 2,
                nvp = VerticesPerPoly,
                walkableHeight = 2f,
                walkableRadius = 0.5f,
                walkableClimb = 0.75f,
                bmin = new RcVec3f(-2f, 0f, -2f),
                bmax = new RcVec3f(2f, 3f, 2f),
                cs = 1f,
                ch = 1f,
                buildBvTree = true,
            });
            var data = new NavMeshData { m_Settings = NavMesh.GetSettingsByID(0) };
            data.m_Tiles.Add(tile);
            return data;
        }
    }
}
