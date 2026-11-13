using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The heightmap's patch quadtree and its level of detail (tests/projects/terrainlod).</summary>
    public class TerrainPatchTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
        }

        /// <summary>Every edge mask tiles the patch's 16 × 16 cells once, clockwise from above, and
        /// uses no odd vertex of an edge next to a coarser node.</summary>
        [Fact]
        public void EveryEdgeMaskTilesThePatch()
        {
            for (int mask = 0; mask < 16; mask++)
            {
                var indices = TerrainRenderer.PatchIndices(mask);
                Assert.Equal(0, indices.Length % 3);
                double area = 0;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    double cross = (b % 17 - a % 17) * (c / 17 - a / 17) - (c % 17 - a % 17) * (b / 17 - a / 17);
                    Assert.True(cross < 0, $"mask {mask}: triangle {t / 3} is flat or turned");
                    area -= cross / 2;
                }
                Assert.Equal(256, area);
                foreach (var v in indices)
                {
                    int x = v % 17, z = v / 17;
                    if ((mask & 1) == 0 && x == 0) Assert.Equal(0, z & 1);
                    if ((mask & 2) == 0 && x == 16) Assert.Equal(0, z & 1);
                    if ((mask & 4) == 0 && z == 16) Assert.Equal(0, x & 1);
                    if ((mask & 8) == 0 && z == 0) Assert.Equal(0, x & 1);
                }
            }
        }

        (Terrain, TerrainData) Bumpy(int resolution, float amplitude)
        {
            var data = new TerrainData { heightmapResolution = resolution };
            m_Objects.Add(data);
            data.size = new Vector3(256f, 40f, 256f);
            var heights = new float[resolution, resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    heights[y, x] = 0.5f + amplitude * Mathf.Sin(x * 0.37f) * Mathf.Cos(y * 0.29f);
            data.SetHeights(0, 0, heights);
            var go = new GameObject("patches");
            m_Objects.Add(go);
            go.SetActive(false);
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            go.SetActive(true);
            return (terrain, data);
        }

        static List<(int Level, int X, int Z)> DrawnNodes(TerrainRenderer.PatchTree tree)
        {
            var drawn = new List<(int, int, int)>();
            for (int level = 0; level <= tree.Levels; level++)
            {
                int side = tree.Side(level);
                for (int i = 0; i < side * side; i++)
                    if (tree.State[level][i] == TerrainRenderer.Drawn) drawn.Add((level, i % side, i / side));
            }
            return drawn;
        }

        /// <summary>A flat heightmap has no error: one root patch at any distance.</summary>
        [Fact]
        public void AFlatTerrainDrawsItsRoot()
        {
            var (terrain, data) = Bumpy(129, 0f);
            var tree = terrain.Renderer.PatchesOf(data);
            Assert.Equal(3, tree.Levels);
            TerrainRenderer.UpdateLod(terrain, tree, new Vector3(128f, 30f, -10f), Matrix4x4.identity, false, 500f);
            Assert.Equal(new[] { (3, 0, 0) }, DrawnNodes(tree));
        }

        /// <summary>Drawn nodes cover the terrain once, neighbours differ by one level at most, and
        /// heightmapMaximumLOD keeps the finest drawn level at its value.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void DrawnNodesCoverTheTerrainAndMeetGently(int maximumLOD)
        {
            var (terrain, data) = Bumpy(257, 0.3f);
            terrain.heightmapPixelError = 3f;
            terrain.heightmapMaximumLOD = maximumLOD;
            var tree = terrain.Renderer.PatchesOf(data);
            var camera = new Vector3(20f, 25f, 20f);
            var frustum = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 2000f) * Matrix4x4.LookAt(camera, new Vector3(200f, 0f, 200f), Vector3.up).inverse;
            TerrainRenderer.UpdateLod(terrain, tree, camera, frustum, false, 400f);
            var drawn = DrawnNodes(tree);
            var cover = new int[256 / 16, 256 / 16];
            foreach (var (level, x, z) in drawn)
            {
                Assert.True(level >= maximumLOD);
                int span = 1 << level;
                for (int zz = z * span; zz < (z + 1) * span; zz++)
                    for (int xx = x * span; xx < (x + 1) * span; xx++)
                        cover[zz, xx]++;
                // A neighbour drawn two levels coarser would leave a crack.
                int mask = TerrainRenderer.EdgeMask(tree, level, x, z);
                for (int d = 0; d < 4; d++)
                {
                    if ((mask & (1 << d)) != 0) continue;
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    int px = nx / 2, pz = nz / 2, side = tree.Side(level + 1);
                    Assert.Equal(TerrainRenderer.Drawn, tree.State[level + 1][pz * side + px]);
                }
            }
            foreach (var c in cover) Assert.Equal(1, c);
            Assert.Contains(drawn, n => n.Level == maximumLOD);
            Assert.Contains(drawn, n => n.Level > maximumLOD + 1);
        }
    }
}
