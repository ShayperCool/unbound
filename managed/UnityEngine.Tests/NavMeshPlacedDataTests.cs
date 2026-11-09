using System;
using System.Collections.Generic;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NavMesh data added at a position and rotation (NavMesh.AddNavMeshData with a pose).
    /// Documented (Scripting API 6000.0, NavMesh.AddNavMeshData, NavMeshDataInstance): the data is
    /// added at the given position and rotation. Measured (Unity 6000.0.71f1, tests/projects/navpath
    /// `place.moved`, `place.adjacent`): a moved or turned NavMesh of several tiles answers queries
    /// on every tile as it does where it was built (in its own frame); NavMeshes built and added
    /// separately do not connect even where their edges meet; a runtime build ends within one voxel
    /// past its bounds.
    /// The level: a 16 m floor with two walls built in 5.33 m tiles (4 x 4).</summary>
    [Collection("Frames")]
    public sealed class NavMeshPlacedDataTests : IDisposable
    {
        const int Blocked = 1;
        readonly List<NavMeshDataInstance> m_Data = new List<NavMeshDataInstance>();

        public void Dispose()
        {
            foreach (var data in m_Data) NavMesh.RemoveNavMeshData(data);
        }

        static NavMeshBuildSource Box(Vector3 centre, Vector3 size, int area) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.Translate(centre),
            area = area,
        };

        static NavMeshBuildSettings SmallTiles()
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.overrideTileSize = true;
            settings.tileSize = 32;
            return settings;
        }

        static NavMeshData Level() => NavMeshBuilder.BuildNavMeshData(SmallTiles(), new List<NavMeshBuildSource>
        {
            Box(new Vector3(0.037f, -0.05f, 0.053f), new Vector3(16f, 0.1f, 16f), 0),
            Box(new Vector3(-2.463f, 0.5f, -1.947f), new Vector3(0.6f, 1f, 9f), Blocked),
            Box(new Vector3(2.837f, 0.5f, 3.053f), new Vector3(7f, 1f, 0.6f), Blocked),
        }, new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f)), Vector3.zero, Quaternion.identity);

        static readonly float[] Grid = { -6.9f, -5.5f, -5.2f, -2.6f, -0.1f, 0.1f, 2.75f, 5.2f, 5.5f, 6.9f };

        public static IEnumerable<object[]> Poses() => new[]
        {
            new object[] { 31.3f, 0.4f, -7.9f, 0f },
            new object[] { -40.2f, 0f, 3.7f, 30f },
            new object[] { 5.1f, -0.3f, 47.6f, 90f },
            new object[] { 2.6667f, 0f, 2.6667f, 45f },
        };

        /// <summary>Every point over the floor (every tile, both sides of each tile edge) samples
        /// the NavMesh right under it, as where the data was built: before, polygons of a moved tile
        /// lying in another tile cell of the world than its centre were never found.</summary>
        [Theory]
        [MemberData(nameof(Poses))]
        public void SamplePositionFindsEveryTileOfAMovedOrTurnedNavMesh(float x, float y, float z, float yaw)
        {
            var pos = new Vector3(x, y, z);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var data = Level();
            m_Data.Add(NavMesh.AddNavMeshData(data, pos, rot));
            foreach (float lx in Grid)
            foreach (float lz in Grid)
            {
                // Off the walls (with the agent radius round them).
                if (lx > -3.4f && lx < -1.5f && lz < 3f) continue;
                if (lz > 2f && lz < 4.2f && lx > -1.2f) continue;
                var at = pos + rot * new Vector3(lx, 0.4f, lz);
                Assert.True(NavMesh.SamplePosition(at, out var hit, 0.6f, NavMesh.AllAreas), $"({lx}, {lz})");
                var local = Quaternion.Inverse(rot) * (hit.position - pos);
                Assert.Equal(lx, local.x, 2);
                Assert.Equal(lz, local.z, 2);
            }
        }

        /// <summary>Paths across tiles and round the walls (their lengths), and rays across tile
        /// edges, are those of the NavMesh where it was built, turned with it: ground polygons connect across the
        /// tiles' edges in the data's own frame (before, a turned NavMesh's tiles did not connect).</summary>
        [Theory]
        [MemberData(nameof(Poses))]
        public void PathsAndRaysOnAMovedOrTurnedNavMeshAreThoseWhereItWasBuilt(float x, float y, float z, float yaw)
        {
            var data = Level();
            var built = NavMesh.AddNavMeshData(data);
            var expected = new List<Vector3[]>();
            var rays = new List<(bool, float, Vector3)>();
            var ends = new[]
            {
                (new Vector3(-7f, 0f, -6f), new Vector3(6.5f, 0f, 6.5f)),
                (new Vector3(-6f, 0f, 7f), new Vector3(6f, 0f, -7f)),
                (new Vector3(7f, 0f, 1f), new Vector3(-7f, 0f, 1.5f)),
            };
            var rayEnds = new[]
            {
                (new Vector3(-7f, 0f, -7f), new Vector3(7f, 0f, 7f)),
                (new Vector3(-7f, 0f, 1f), new Vector3(7f, 0f, 1f)),
                (new Vector3(4f, 0f, -7f), new Vector3(4f, 0f, 7f)),
                (new Vector3(-7f, 0f, -7.2f), new Vector3(7.2f, 0f, -7.1f)),
            };
            foreach (var (a, b) in ends)
            {
                var path = new NavMeshPath();
                Assert.True(NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path));
                Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
                expected.Add(path.corners);
            }
            foreach (var (a, b) in rayEnds)
            {
                bool blocked = NavMesh.Raycast(a, b, out var hit, NavMesh.AllAreas);
                rays.Add((blocked, hit.distance, hit.normal));
            }
            NavMesh.RemoveNavMeshData(built);

            var pos = new Vector3(x, y, z);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            m_Data.Add(NavMesh.AddNavMeshData(data, pos, rot));
            for (int i = 0; i < ends.Length; i++)
            {
                var path = new NavMeshPath();
                Assert.True(NavMesh.CalculatePath(pos + rot * ends[i].Item1, pos + rot * ends[i].Item2, NavMesh.AllAreas, path));
                Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
                // The same way round the walls (the straight path's corners in line may differ:
                // its funnel works in world axes, Unity's too, navpath `place.moved` r90.p2).
                Assert.Equal(Length(expected[i]), Length(path.corners), 3);
                Assert.True((path.corners[path.corners.Length - 1] - (pos + rot * expected[i][expected[i].Length - 1])).magnitude < 2e-3f);
            }
            for (int i = 0; i < rayEnds.Length; i++)
            {
                bool blocked = NavMesh.Raycast(pos + rot * rayEnds[i].Item1, pos + rot * rayEnds[i].Item2, out var hit, NavMesh.AllAreas);
                Assert.Equal(rays[i].Item1, blocked);
                Assert.Equal(rays[i].Item2, hit.distance, 3);
                Assert.True((hit.normal - rot * rays[i].Item3).magnitude < 1e-4f, $"ray {i} normal {hit.normal}");
            }
            // The straight ray along the bottom crosses three tile edges and reaches its end.
            Assert.False(rays[3].Item1);
        }

        static float Length(Vector3[] corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>Two NavMeshes built from one floor with bounds meeting at a tile edge (x = 0) are
        /// not connected: a path from one to the other is partial, ending on the first. Each ends
        /// within a voxel (1/6 m) past its bounds.</summary>
        [Fact]
        public void SeparatelyBuiltNavMeshesMeetingAtAnEdgeAreNotConnected()
        {
            var floor = new List<NavMeshBuildSource> { Box(new Vector3(0.037f, -0.05f, 0.053f), new Vector3(20f, 0.1f, 12f), 0) };
            m_Data.Add(NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(SmallTiles(), floor,
                new Bounds(new Vector3(-5.333f, 0f, 0f), new Vector3(10.667f, 10f, 16f)), Vector3.zero, Quaternion.identity)));
            m_Data.Add(NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(SmallTiles(), floor,
                new Bounds(new Vector3(5.333f, 0f, 0f), new Vector3(10.667f, 10f, 16f)), Vector3.zero, Quaternion.identity)));
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(new Vector3(-6f, 0f, -1f), new Vector3(6f, 0f, 2f), NavMesh.AllAreas, path));
            Assert.Equal(NavMeshPathStatus.PathPartial, path.status);
            Assert.Equal(1f / 6f, path.corners[path.corners.Length - 1].x, 3);
            Assert.True(NavMesh.Raycast(new Vector3(-6f, 0f, 1f), new Vector3(6f, 0f, 1f), out var hit, NavMesh.AllAreas));
            Assert.Equal(1f / 6f, hit.position.x, 3);
        }

        /// <summary>A NavMesh ends within a voxel past its build bounds, also inside a tile (the
        /// floor runs on beyond the bounds).</summary>
        [Fact]
        public void ARuntimeBuildEndsWithinAVoxelPastItsBounds()
        {
            var floor = new List<NavMeshBuildSource> { Box(new Vector3(0.037f, -0.05f, 0.053f), new Vector3(20f, 0.1f, 12f), 0) };
            m_Data.Add(NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(SmallTiles(), floor,
                new Bounds(new Vector3(-1f, 0f, 0f), new Vector3(6.1f, 10f, 16f)), Vector3.zero, Quaternion.identity)));
            // Bounds x from -4.05 to 2.05: voxels from -4.167 to 2.167.
            Assert.True(NavMesh.Raycast(new Vector3(0f, 0f, 1f), new Vector3(6f, 0f, 1f), out var hit, NavMesh.AllAreas));
            Assert.Equal(13f / 6f, hit.position.x, 3);
            Assert.True(NavMesh.Raycast(new Vector3(0f, 0f, 1f), new Vector3(-6f, 0f, 1f), out hit, NavMesh.AllAreas));
            Assert.Equal(-25f / 6f, hit.position.x, 3);
        }
    }
}
