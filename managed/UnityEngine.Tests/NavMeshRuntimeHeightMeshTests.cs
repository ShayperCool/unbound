using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Height meshes built at run time (NavMeshBuildSettings.buildHeightMesh). Measured
    /// (Unity 6000.0.71f1, tests/projects/navheight): the build's walkable source triangles give
    /// queries and agents their height; without the setting the polygons' heights stay.</summary>
    [Collection("Frames")]
    public sealed class NavMeshRuntimeHeightMeshTests
    {
        static NavMeshData Build(bool height, params NavMeshBuildSource[] extra)
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.buildHeightMesh = height;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(10f, 1f, 10f), transform = Matrix4x4.Translate(new Vector3(70f, -0.5f, 70f)), area = 0 },
                // A 0.2 m step on x in [71, 72].
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(1f, 0.2f, 4f), transform = Matrix4x4.Translate(new Vector3(71.5f, 0.1f, 70f)), area = 0 },
            };
            sources.AddRange(extra);
            return NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(70f, 0f, 70f), new Vector3(12f, 6f, 12f)), Vector3.zero, Quaternion.identity);
        }

        [Fact]
        public void TheSettingDecidesWhetherTheDataHasAHeightMesh()
        {
            Assert.True(Build(true).hasHeightMeshData);
            Assert.False(Build(false).hasHeightMeshData);
            // No walkable triangle: no height mesh.
            var settings = NavMesh.GetSettingsByID(0);
            settings.buildHeightMesh = true;
            var wall = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = Wall(), transform = Matrix4x4.identity, area = 0 };
            Assert.False(NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { wall }, new Bounds(Vector3.zero, Vector3.one * 4f), Vector3.zero, Quaternion.identity).hasHeightMeshData);
        }

        static Mesh Wall() => new Mesh { vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f) }, triangles = new[] { 0, 1, 2 } };

        [Theory]
        [InlineData(true, 0.2f)]
        [InlineData(false, -1f)]
        public void QueriesOnTheStepTakeTheHeightMeshsHeight(bool height, float expected)
        {
            var instance = NavMesh.AddNavMeshData(Build(height));
            try
            {
                Assert.True(NavMesh.SamplePosition(new Vector3(71.5f, 0.5f, 70f), out var hit, 1f, NavMesh.AllAreas));
                if (expected >= 0f) Assert.Equal(expected, hit.position.y, 5);
                // Without one, the polygon's height (a detail mesh over the step) is not the step's.
                else Assert.NotEqual(0.2f, hit.position.y);
            }
            finally { NavMesh.RemoveNavMeshData(instance); }
        }

        [Fact]
        public void SteepTrianglesAreNotPartOfIt()
        {
            // A 60 degree ramp next to the step: too steep for the default 45 degree agent.
            var steep = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(2f, 0.1f, 2f), transform = Matrix4x4.TRS(new Vector3(67f, 0.5f, 67f), Quaternion.Euler(0f, 0f, 60f), Vector3.one), area = 0 };
            var data = Build(true, steep);
            var mesh = Assert.Single(data.m_HeightMeshes);
            float best = float.NegativeInfinity;
            // Over the ramp's foot, below the climb of a point on the ground: the ground only.
            mesh.Highest(67.4f, 67f, 0.05f, 0.75f, ref best);
            Assert.Equal(0f, best, 5);
        }

        static NavMeshBuildSettings Tiled(bool preserve)
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.buildHeightMesh = true;
            settings.overrideTileSize = true;
            settings.tileSize = 32;
            settings.preserveTilesOutsideBounds = preserve;
            return settings;
        }

        static List<NavMeshBuildSource> Floor() => new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(20f, 1f, 20f), transform = Matrix4x4.Translate(new Vector3(70f, -0.5f, 70f)), area = 0 },
        };

        static readonly Bounds Whole = new Bounds(new Vector3(70f, 0f, 70f), new Vector3(22f, 6f, 22f));

        /// <summary>Documented (NavMeshBuildSettings.buildHeightMesh, 6000.0): an update preserving
        /// tiles outside its bounds creates no height mesh and removes an existing one. Measured
        /// (tests/projects/navheight `preserve.*`): it rebuilds only the tiles wholly inside the
        /// bounds and keeps the others; with none wholly inside, the data stays as it was.</summary>
        [Fact]
        public void PreservingUpdatesRebuildWholeTilesAndDropTheHeightMesh()
        {
            var data = NavMeshBuilder.BuildNavMeshData(Tiled(false), Floor(), Whole, Vector3.zero, Quaternion.identity);
            int tiles = data.m_Tiles.Count;
            Assert.True(data.hasHeightMeshData);
            var before = new HashSet<object>(data.m_Tiles);
            // 2 m bounds hold no whole 5.33 m tile: nothing changes.
            Assert.True(NavMeshBuilder.UpdateNavMeshData(data, Tiled(true), Floor(), new Bounds(new Vector3(70f, 0f, 70f), new Vector3(2f, 6f, 2f))));
            Assert.True(data.hasHeightMeshData);
            Assert.Equal(before, new HashSet<object>(data.m_Tiles));
            // Bounds from x, z 64 to 74.67 hold the tile [64, 69.33) x [64, 69.33) and its neighbours
            // up to 74.67: those are built again, the rest kept; the height mesh goes.
            float tile = 32 * NavMesh.GetSettingsByID(0).voxelSize;
            var inside = new Bounds(new Vector3(12 * tile + tile, 0f, 12 * tile + tile), new Vector3(2 * tile, 6f, 2 * tile));
            Assert.True(NavMeshBuilder.UpdateNavMeshData(data, Tiled(true), Floor(), inside));
            Assert.False(data.hasHeightMeshData);
            Assert.Equal(tiles, data.m_Tiles.Count);
            int rebuilt = 0;
            foreach (var t in data.m_Tiles)
                if (!before.Contains(t))
                {
                    rebuilt++;
                    Assert.InRange(t.header.x, 12, 13);
                    Assert.InRange(t.header.y, 12, 13);
                }
            Assert.Equal(4, rebuilt);
            // A first build preserving tiles builds only whole tiles in its bounds.
            var fresh = NavMeshBuilder.BuildNavMeshData(Tiled(true), Floor(), inside, Vector3.zero, Quaternion.identity);
            Assert.Equal(4, fresh.m_Tiles.Count);
            Assert.False(fresh.hasHeightMeshData);
        }

        /// <summary>Documented: a preserving update takes its settings from the data but for the
        /// region area, job workers, debug settings and buildHeightMesh, and removes the links the
        /// data's bake generated (links added by themselves stay). Measured: it keeps the data's
        /// source bounds.</summary>
        [Fact]
        public void PreservingUpdatesKeepTheDatasSettingsBoundsAndOwnLinksOnly()
        {
            var data = NavMeshBuilder.BuildNavMeshData(Tiled(false), Floor(), Whole, Vector3.zero, Quaternion.identity);
            data.m_Links.Add(new BakedLink { Start = new Vector3(66f, 0f, 66f), End = new Vector3(68f, 0f, 66f), Bidirectional = true });
            var instance = NavMesh.AddNavMeshData(data);
            var own = NavMesh.AddLink(new NavMeshLinkData { startPosition = new Vector3(72f, 0f, 72f), endPosition = new Vector3(74f, 0f, 72f), bidirectional = true });
            try
            {
                Assert.Equal(2, NavWorld.Links.Count(l => l.Data != 0 || l.Id == own.id));
                var changed = Tiled(true);
                changed.agentRadius = 0.9f;
                changed.tileSize = 64;
                changed.minRegionArea = 3f;
                float tile = 32 * NavMesh.GetSettingsByID(0).voxelSize;
                var inside = new Bounds(new Vector3(13 * tile, 0f, 13 * tile), new Vector3(2 * tile, 6f, 2 * tile));
                Assert.True(NavMeshBuilder.UpdateNavMeshData(data, changed, Floor(), inside));
                Assert.Equal(NavMesh.GetSettingsByID(0).agentRadius, data.buildSettings.agentRadius);
                Assert.Equal(32, data.buildSettings.tileSize);
                Assert.Equal(3f, data.buildSettings.minRegionArea);
                Assert.Equal(Whole, data.sourceBounds);
                Assert.Empty(data.m_Links);
                Assert.DoesNotContain(NavWorld.Links, l => l.Data != 0);
                Assert.Contains(NavWorld.Links, l => l.Id == own.id);
            }
            finally
            {
                NavMesh.RemoveLink(own);
                NavMesh.RemoveNavMeshData(instance);
            }
        }

        [Fact]
        public void ZeroAreaTrianglesAreNotPartOfIt()
        {
            // A sliver standing on the step's edge, its corners on one line up the step.
            var sliver = new Mesh
            {
                vertices = new[] { new Vector3(73f, 0f, 70f), new Vector3(73f, 0.5f, 70f), new Vector3(73f, 1f, 70f) },
                triangles = new[] { 0, 1, 2 },
            };
            var data = Build(true, new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = sliver, transform = Matrix4x4.identity, area = 0 });
            var mesh = Assert.Single(data.m_HeightMeshes);
            float best = float.NegativeInfinity;
            mesh.Highest(73f, 70f, 0.05f, 0.75f, ref best);
            Assert.Equal(0f, best, 5);
        }

        /// <summary>Data built and added at a position and a rotation: the height mesh is in the
        /// data's own frame, so the step stands where its source stands in the world.</summary>
        [Fact]
        public void AMovedAndTurnedBuildKeepsTheStepWhereItsSourceIs()
        {
            var position = new Vector3(5f, 1f, -3f);
            var rotation = Quaternion.Euler(0f, 30f, 0f);
            var settings = NavMesh.GetSettingsByID(0);
            settings.buildHeightMesh = true;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(10f, 1f, 10f), transform = Matrix4x4.Translate(new Vector3(70f, -0.5f, 70f)), area = 0 },
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(1f, 0.2f, 4f), transform = Matrix4x4.Translate(new Vector3(71.5f, 0.1f, 70f)), area = 0 },
            };
            var local = new Bounds(Quaternion.Inverse(rotation) * (new Vector3(70f, 0f, 70f) - position), new Vector3(16f, 8f, 16f));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, local, position, rotation);
            var instance = NavMesh.AddNavMeshData(data, position, rotation);
            try
            {
                Assert.True(NavMesh.SamplePosition(new Vector3(71.5f, 0.5f, 70f), out var hit, 1f, NavMesh.AllAreas));
                Assert.Equal(0.2f, hit.position.y, 4);
                Assert.True(NavMesh.SamplePosition(new Vector3(68f, 0.3f, 70f), out hit, 1f, NavMesh.AllAreas));
                Assert.Equal(0f, hit.position.y, 4);
            }
            finally { NavMesh.RemoveNavMeshData(instance); }
        }

        /// <summary>The bounding volume tree finds what a search of every triangle finds.</summary>
        [Fact]
        public void TheTreeAnswersAsEveryTriangleWould()
        {
            var random = new System.Random(7);
            var vertices = new List<float>();
            var triangles = new List<int>();
            for (int t = 0; t < 300; t++)
            {
                float x = (float)random.NextDouble() * 40f, z = (float)random.NextDouble() * 40f, y = (float)random.NextDouble() * 3f;
                for (int k = 0; k < 3; k++)
                {
                    triangles.Add(vertices.Count / 3);
                    vertices.Add(x + (float)random.NextDouble() * 2f);
                    vertices.Add(y + (float)random.NextDouble() * 0.5f);
                    vertices.Add(z + (float)random.NextDouble() * 2f);
                }
            }
            var v = vertices.ToArray();
            var tris = triangles.ToArray();
            var tree = NavMeshHeightMesh.FromTriangles(v, tris, t => t % 3 != 0);
            var single = new List<NavMeshHeightMesh>();
            for (int t = 0; t < tris.Length / 3; t++)
                if (t % 3 != 0) single.Add(NavMeshHeightMesh.FromTriangles(v, tris, u => u == t));
            for (int q = 0; q < 500; q++)
            {
                float x = (float)random.NextDouble() * 42f, z = (float)random.NextDouble() * 42f, top = (float)random.NextDouble() * 4f;
                float viaTree = float.NegativeInfinity, viaEach = float.NegativeInfinity;
                tree.Highest(x, z, 0.3f, top, ref viaTree);
                foreach (var mesh in single) mesh.Highest(x, z, 0.3f, top, ref viaEach);
                Assert.Equal(viaEach, viaTree);
            }
            Assert.Null(NavMeshHeightMesh.FromTriangles(v, tris, t => false));
        }
    }
}
