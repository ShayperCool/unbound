using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Baked height meshes (Build Height Mesh). Documented (AI Navigation 2.0 manual,
    /// NavMesh Surface: Build Height Mesh): it gives a more accurate height for placing agents on the
    /// surface. Measured (Unity 6000.0.71f1, tests/projects/navbaked `height`, `height.agents`): a
    /// point takes the height of the highest part of the height mesh's triangles within a square of
    /// the voxel size round it, lower than the point plus the agent climb; SamplePosition, Raycast and
    /// agents (placed, moved, walking) use it, path corners and the closest edge do not.
    /// The mesh: a floor at y = 0 over x, z in [-5, 5], a 0.3 m step on x in [1, 2] with its riser.</summary>
    [Collection("Frames")]
    public sealed class NavMeshHeightMeshTests : IDisposable
    {
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();

        public void Dispose()
        {
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
        }

        static readonly Vector3[] Vertices =
        {
            new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, -5f), new Vector3(5f, 0f, 5f), new Vector3(-5f, 0f, 5f),
            // The step's top and its riser at x = 1.
            new Vector3(1f, 0.3f, -5f), new Vector3(2f, 0.3f, -5f), new Vector3(2f, 0.3f, 5f), new Vector3(1f, 0.3f, 5f),
            new Vector3(1f, 0f, -5f), new Vector3(1f, 0f, 5f),
        };

        static readonly int[] Indices = { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 9, 7, 8, 7, 4 };

        /// <summary>The mesh as Unity serializes it, with no tree, or with a root over two leaves
        /// (floor; step and riser).</summary>
        static NavMeshHeightMesh Mesh(bool tree)
        {
            var b = new StringBuilder();
            b.AppendLine("m_Vertices:");
            foreach (var v in Vertices)
                b.AppendLine(FormattableString.Invariant($"- {{x: {v.x}, y: {v.y}, z: {v.z}}}"));
            b.AppendLine("m_Indices:");
            foreach (int i in Indices) b.AppendLine("- " + i.ToString(CultureInfo.InvariantCulture));
            b.AppendLine("m_Bounds:");
            b.AppendLine("  m_Center: {x: 0, y: 0.15, z: 0}");
            b.AppendLine("  m_Extent: {x: 5, y: 0.15, z: 5}");
            if (tree)
            {
                b.AppendLine("m_Nodes:");
                b.AppendLine("- min: {x: -5, y: 0, z: -5}");
                b.AppendLine("  max: {x: 5, y: 0.3, z: 5}");
                b.AppendLine("  i: -3");
                b.AppendLine("  n: 0");
                b.AppendLine("- min: {x: -5, y: 0, z: -5}");
                b.AppendLine("  max: {x: 5, y: 0, z: 5}");
                b.AppendLine("  i: 0");
                b.AppendLine("  n: 2");
                b.AppendLine("- min: {x: 1, y: 0, z: -5}");
                b.AppendLine("  max: {x: 2, y: 0.3, z: 5}");
                b.AppendLine("  i: 2");
                b.AppendLine("  n: 4");
            }
            else
            {
                b.AppendLine("m_Nodes: []");
            }
            return NavMeshHeightMesh.Read(TestYaml.Parse(b.ToString()));
        }

        static float Height(NavMeshHeightMesh mesh, float x, float y, float z, float reach = 1f / 6f, float climb = 0.4f)
        {
            float best = float.NegativeInfinity;
            mesh.Highest(x, z, reach, y + climb, ref best);
            return best;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThePointTakesTheHighestPartWithinTheVoxelSquareBelowItsClimb(bool tree)
        {
            var mesh = Mesh(tree);
            Assert.NotNull(mesh);
            // On the floor, away from the step.
            Assert.Equal(0f, Height(mesh, 0.5f, 0f, 0f));
            // Within a voxel of the riser: the step's top within the square counts.
            Assert.Equal(0.3f, Height(mesh, 0.9f, 0f, 0f), 5);
            // On the step.
            Assert.Equal(0.3f, Height(mesh, 1.5f, 0.3f, 0f), 5);
            // The step is higher than the point plus the climb: the floor under it.
            Assert.Equal(0f, Height(mesh, 1.5f, -0.2f, 0f));
            // Off the mesh.
            Assert.True(float.IsNegativeInfinity(Height(mesh, 7f, 0f, 0f)));
            // A wider square reaches the step from further away.
            Assert.Equal(0f, Height(mesh, 0.5f, 0f, 0f, 0.4f));
            Assert.Equal(0.3f, Height(mesh, 0.5f, 0f, 0f, 0.6f), 5);
        }

        static NavMeshData Floor()
        {
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(10f, 0.1f, 10f),
                    transform = Matrix4x4.Translate(new Vector3(0.037f, -0.05f, 0.053f)), area = 0,
                },
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Vector3.zero, new Vector3(14f, 6f, 14f)), Vector3.zero, Quaternion.identity);
            // The floor's height mesh with a step on it the NavMesh does not have.
            data.m_HeightMeshes.Add(Mesh(true));
            return data;
        }

        /// <summary>SamplePosition, Raycast and agents placed on the NavMesh take the height mesh's
        /// height (moved with the data); path corners and the closest edge stay on the NavMesh.</summary>
        [Theory]
        [InlineData(0f, 0f, 0f, 0f)]
        [InlineData(12.3f, 0.5f, -4.1f, 90f)]
        public void QueriesAndAgentsUseTheHeightMeshPathsDoNot(float px, float py, float pz, float yaw)
        {
            var pos = new Vector3(px, py, pz);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
            m_Data.Add(NavMesh.AddNavMeshData(Floor(), pos, rot));

            Assert.True(NavMesh.SamplePosition(W(1.5f, 1f, 0f), out var hit, 2f, NavMesh.AllAreas));
            Assert.Equal(W(1.5f, 0.3f, 0f).y, hit.position.y, 3);
            // Its distance is the one to the NavMesh (below the step).
            Assert.True(hit.distance > 0.9f);
            Assert.True(NavMesh.SamplePosition(W(-2f, 1f, 0f), out hit, 2f, NavMesh.AllAreas));
            Assert.Equal(W(-2f, 0f, 0f).y, hit.position.y, 3);

            var from = W(-2f, 0f, 1f);
            Assert.False(NavMesh.Raycast(from, W(1.5f, 0f, 1f), out hit, NavMesh.AllAreas));
            Assert.Equal(W(1.5f, 0.3f, 1f).y, hit.position.y, 3);
            Assert.Equal(Vector3.Distance(from, hit.position), hit.distance, 3);

            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(W(-2f, 0f, 1f), W(1.5f, 0f, 1f), NavMesh.AllAreas, path));
            // The NavMesh's height (a voxel over the floor), not the step's.
            Assert.True(path.corners[path.corners.Length - 1].y < W(1.5f, 0.2f, 1f).y);

            var go = new GameObject("agent");
            try
            {
                go.transform.position = W(1.5f, 0f, -1f);
                var agent = go.AddComponent<NavMeshAgent>();
                Assert.True(agent.isOnNavMesh);
                Assert.Equal(W(1.5f, 0.3f, -1f).y, agent.nextPosition.y, 3);
                agent.nextPosition = W(-2f, 0f, -1f);
                Assert.Equal(W(-2f, 0f, -1f).y, agent.nextPosition.y, 3);
                agent.Warp(W(1.2f, 0f, -2f));
                Assert.Equal(W(1.2f, 0.3f, -2f).y, agent.nextPosition.y, 3);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
