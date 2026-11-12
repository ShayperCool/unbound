using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class NavMeshBuildBoundsTests
    {
        static NavMeshBuildSource Floor(Vector3 offset) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(12f, 0.4f, 10f),
            transform = Matrix4x4.Translate(offset + new Vector3(-2f, -0.2f, -1f)),
            area = 0,
        };

        static NavMeshData Build(List<NavMeshBuildSource> sources, Bounds bounds)
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.overrideTileSize = true;
            settings.tileSize = 64;
            return NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(1.37f, -2.19f)]
        public void RasterFootprintIsClippedToSourcesBeforeVoxelRounding(float x, float z)
        {
            // Unity 6000.0.71f1, navmesh build.bounds: padding is clipped to the sources.
            var offset = new Vector3(x, 0f, z);
            var requested = new Bounds(offset + new Vector3(-2f, 0f, -1f), new Vector3(24f, 8f, 20f));
            var data = Build(new List<NavMeshBuildSource> { Floor(offset) }, requested);
            try
            {
                Assert.NotEmpty(data.m_Tiles);
                float voxel = data.m_Settings.EffectiveVoxelSize;
                float expectedX = MathF.Floor((x - 8f) * (1f / voxel)) * voxel;
                float expectedZ = MathF.Floor((z - 6f) * (1f / voxel)) * voxel;
                Assert.InRange(MathF.Abs(data.m_Tiles.Min(t => t.header.bmin.X) - expectedX), 0f, 1e-5f);
                Assert.InRange(MathF.Abs(data.m_Tiles.Min(t => t.header.bmin.Z) - expectedZ), 0f, 1e-5f);
                Assert.Equal(requested, data.sourceBounds);
            }
            finally { Object.DestroyImmediate(data); }
        }

        [Theory]
        [InlineData("6000.0.71f1", 0f, 70)]
        [InlineData("6000.0.71f1", 2.4f, 70)]
        [InlineData("6000.0.72f1", 0f, 73)]
        public void OneCellBorderRegionsJoinTheirNeighbourInMeasuredRelease(string release, float height, int vertices)
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = release;
            var offset = new Vector3(1.37f, height, -2.19f);
            var sources = new List<NavMeshBuildSource>
            {
                Floor(offset),
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(2f, 3f, 4f),
                    transform = Matrix4x4.TRS(offset + new Vector3(-2f, 1.5f, -1f), Quaternion.Euler(0f, 23f, 0f), Vector3.one),
                },
            };
            NavMeshData data = null;
            try
            {
                data = Build(sources, new Bounds(offset + new Vector3(-2f, 0f, -1f), new Vector3(24f, 8f, 20f)));
                // The extra region adds a triangle to the floor at the rotated box's corner.
                // Unity 6000.0.71f1, navmesh build.bounds.translated and translated.elevated.
                Assert.Equal(vertices, data.m_Tiles.Sum(t => t.polys.Take(t.header.polyCount).Sum(poly => poly.vertCount)));
                Assert.All(data.m_Tiles, tile => Assert.All(tile.polys.Take(tile.header.polyCount),
                    poly => Assert.InRange(poly.vertCount, 3, 6)));
            }
            finally
            {
                if (data != null) Object.DestroyImmediate(data);
                ProjectInfo.UnityVersion = previous;
            }
        }

        [Fact]
        public void LocalBoundsStillClipSourcesAndDisjointBuildsRemainEmpty()
        {
            var sources = new List<NavMeshBuildSource> { Floor(Vector3.zero) };
            var data = Build(sources, new Bounds(new Vector3(-2f, 0f, -1f), new Vector3(7f, 8f, 6f)));
            var disjoint = Build(sources, new Bounds(new Vector3(40f, 0f, 40f), new Vector3(4f, 8f, 4f)));
            try
            {
                Assert.NotEmpty(data.m_Tiles);
                Assert.True(data.m_Tiles.All(t => t.header.bmin.X >= -5.5f - 1e-5f && t.header.bmin.Z >= -4f - 1e-5f));
                Assert.Empty(disjoint.m_Tiles);
            }
            finally { Object.DestroyImmediate(data); Object.DestroyImmediate(disjoint); }
        }
    }
}
