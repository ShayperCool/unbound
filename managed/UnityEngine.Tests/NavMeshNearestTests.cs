using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which NavMesh point a query starts from. Documented (Scripting API 6000.0,
    /// NavMesh.SamplePosition): "The nearest point is found by projecting the input point onto nearby
    /// NavMesh instances along the vertical axis. If this step does not find a projected point within
    /// the specified distance, then sampling is extended to surrounding NavMesh positions." Measured
    /// (tests/projects/navbaked `sample`): a point over a floor beside a nearer raised platform samples
    /// the floor. Paths and agents take the nearer of the two instead, Raycast and FindClosestEdge the
    /// one below (measured: tests/projects/navpath nearest.sides).
    /// The level is a 20 m floor and a 1.5 m high platform from z = 1 to 5.</summary>
    [Collection("Frames")]
    public sealed class NavMeshNearestTests : IDisposable
    {
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();

        static NavMeshBuildSource Box(Vector3 center, Vector3 size) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.Translate(center),
            area = 0,
        };

        static NavMeshData Build(Bounds bounds, params NavMeshBuildSource[] sources) =>
            NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource>(sources), bounds, Vector3.zero, Quaternion.identity);

        public void Dispose()
        {
            foreach (var d in m_Data) NavMesh.RemoveNavMeshData(d);
        }

        void FloorAndPlatform()
        {
            m_Data.Add(NavMesh.AddNavMeshData(Build(new Bounds(Vector3.zero, new Vector3(24f, 6f, 24f)),
                Box(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f)),
                Box(new Vector3(0f, 0.75f, 3f), new Vector3(6f, 1.5f, 4f)))));
        }

        [Fact]
        public void SamplePositionProjectsDownBeforeTakingANearerPointBeside()
        {
            FloorAndPlatform();
            // 2.3 m over the floor, about 1.45 m from the platform's edge: nearer the platform even
            // by more than the agent climb.
            var from = new Vector3(0f, 2.4f, 0.3f);
            Assert.True(NavMesh.SamplePosition(from, out var hit, 2.5f, NavMesh.AllAreas));
            Assert.True(hit.position.y < 0.5f, $"sampled {hit.position}");
            Assert.Equal(0.3f, hit.position.z, 3);
            Assert.Equal(Vector3.Distance(from, hit.position), hit.distance, 4);
            // Within reach of nothing below: the nearest point beside, the platform's edge.
            var platform = NavMesh.SamplePosition(new Vector3(0f, 2.2f, 0.9f), out var edge, 1.2f, NavMesh.AllAreas);
            Assert.True(platform);
            Assert.True(edge.position.y > 1f && edge.position.z > 1f, $"sampled {edge.position}");
        }

        [Fact]
        public void PathsAndAgentsTakeTheNearerOfThePointsBelowAndBeside()
        {
            FloorAndPlatform();
            var surface = NavWorld.Get(0);
            var from = new Vector3(0f, 2f, 0.2f);
            var reach = new RcVec3f(2.5f, 2.5f, 2.5f);
            Assert.True(NavMesh.Nearest(surface, from, reach, AreaFilter.Of(NavMesh.AllAreas), out _, out var nearer));
            Assert.True(nearer.y > 1f, $"nearest {nearer}");
            Assert.True(NavMesh.Nearest(surface, from, reach, AreaFilter.Of(NavMesh.AllAreas), out _, out var below, preferOver: true));
            Assert.True(below.y < 0.5f, $"projected {below}");
            // Nearer in height than beside: the point below.
            Assert.True(NavMesh.Nearest(surface, new Vector3(0f, 0.6f, 0.2f), reach, AreaFilter.Of(NavMesh.AllAreas), out _, out var low));
            Assert.True(low.y < 0.5f, $"nearest {low}");
        }

        [Fact]
        public void ATurnedNavMeshIsFoundWhereItIs()
        {
            // An 8 x 4 m floor turned a quarter round about y (its tiles land on the grid cells
            // they are in): about 3 m wide in x, 7 m long in z where it can be walked on.
            var data = Build(new Bounds(Vector3.zero, new Vector3(12f, 4f, 12f)), Box(new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 4f)));
            m_Data.Add(NavMesh.AddNavMeshData(data, Vector3.zero, Quaternion.Euler(0f, 90f, 0f)));
            foreach (float x in new[] { -1f, -0.5f, 0.5f, 1f })
            foreach (float z in new[] { -2.8f, -2.1f, -1.4f, -0.7f, 0.7f, 1.4f, 2.1f, 2.8f })
            {
                Assert.True(NavMesh.SamplePosition(new Vector3(x, 0.5f, z), out var hit, 0.6f, NavMesh.AllAreas), $"nothing at {x}, {z}");
                Assert.Equal(x, hit.position.x, 3);
                Assert.Equal(z, hit.position.z, 3);
            }
            Assert.True(NavMesh.SamplePosition(new Vector3(3f, 0.2f, 1f), out var side, 2f, NavMesh.AllAreas));
            Assert.InRange(side.position.x, 1.2f, 1.6f);
            Assert.Equal(1f, side.position.z, 3);
            Assert.False(NavMesh.SamplePosition(new Vector3(0.5f, 0.2f, 6f), out _, 1f, NavMesh.AllAreas));
        }
    }
}
