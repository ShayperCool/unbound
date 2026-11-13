using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
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

        (Terrain, TerrainData) Bumpy(int resolution, float amplitude, float placeX = 0f)
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
            go.transform.position = new Vector3(placeX, 0f, 0f);
            go.SetActive(false);
            var terrain = go.AddComponent<Terrain>();
            terrain.allowAutoConnect = false;
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
            var frustum = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 2000f) * View(camera, new Vector3(200f, 0f, 200f));
            TerrainRenderer.UpdateLod(terrain, tree, camera, frustum, false, 8f);
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

        /// <summary>heightmapMaximumLOD and heightmapMinimumLODSimplification keep their sum within
        /// the heightmap's levels (11 without terrain data), and neither goes below zero.</summary>
        [Fact]
        public void TheLodSettingsStayWithinTheLevels()
        {
            var (terrain, _) = Bumpy(257, 0.3f);
            terrain.heightmapMaximumLOD = 3;
            terrain.heightmapMinimumLODSimplification = 2;
            Assert.Equal(3, terrain.heightmapMaximumLOD);
            Assert.Equal(1, terrain.heightmapMinimumLODSimplification);
            terrain.heightmapMaximumLOD = 9;
            Assert.Equal(3, terrain.heightmapMaximumLOD);
            terrain.heightmapMaximumLOD = -2;
            Assert.Equal(0, terrain.heightmapMaximumLOD);
            terrain.heightmapMinimumLODSimplification = 7;
            Assert.Equal(4, terrain.heightmapMinimumLODSimplification);
            terrain.heightmapMinimumLODSimplification = -1;
            Assert.Equal(0, terrain.heightmapMinimumLODSimplification);

            var go = new GameObject("no data");
            m_Objects.Add(go);
            var bare = go.AddComponent<Terrain>();
            bare.heightmapMaximumLOD = 20;
            Assert.Equal(11, bare.heightmapMaximumLOD);
            bare.heightmapMinimumLODSimplification = 5;
            Assert.Equal(0, bare.heightmapMinimumLODSimplification);
        }

        /// <summary>The drawn level per finest patch of a tile.</summary>
        static int[,] LevelGrid(TerrainRenderer.PatchTree tree)
        {
            int side = tree.Side(0);
            var grid = new int[side, side];
            foreach (var (level, x, z) in DrawnNodes(tree))
                for (int zz = z << level; zz < (z + 1) << level; zz++)
                    for (int xx = x << level; xx < (x + 1) << level; xx++)
                        grid[zz, xx] = level;
            return grid;
        }

        /// <summary>A camera's world-to-camera matrix: Unity's camera space looks down -z.</summary>
        static Matrix4x4 View(Vector3 position, Vector3 target) => Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.LookAt(position, target, Vector3.up).inverse;

        static FrameBuilder.CameraContext Looking(Vector3 position, Vector3 target, bool orthographic = false)
        {
            var view = View(position, target);
            var projection = orthographic ? Matrix4x4.Ortho(-200f, 200f, -112.5f, 112.5f, 0.3f, 2000f) : Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 2000f);
            return new FrameBuilder.CameraContext { Position = position, View = view, Projection = projection, Viewport = new Rect(0f, 0f, 480f, 270f), Orthographic = orthographic };
        }

        /// <summary>Connected tiles meet like nodes of one tree: across the seam drawn patches differ
        /// by one level at most and the finer side follows the coarser one's edge. Unconnected,
        /// a flat tile beside a bumpy one keeps its root.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConnectedTilesMeetAcrossTheSeam(bool connected)
        {
            var (west, westData) = Bumpy(257, 0.3f);
            var (east, eastData) = Bumpy(257, 0f, 256f);
            if (connected)
            {
                west.SetNeighbors(null, null, east, null);
                east.SetNeighbors(west, null, null, null);
            }
            west.heightmapPixelError = east.heightmapPixelError = 2f;
            var camera = Looking(new Vector3(250f, 12f, 128f), new Vector3(150f, 0f, 128f));
            TerrainRenderer.UpdateGroupLod(west, camera, camera.Projection * camera.View, 270f);
            if (!connected) TerrainRenderer.UpdateGroupLod(east, camera, camera.Projection * camera.View, 270f);
            var westTree = west.Renderer.PatchesOf(westData);
            var eastTree = east.Renderer.PatchesOf(eastData);
            var westLevels = LevelGrid(westTree);
            var eastLevels = LevelGrid(eastTree);
            int side = westTree.Side(0), widest = 0;
            for (int z = 0; z < side; z++)
            {
                int fine = westLevels[z, side - 1], coarse = eastLevels[z, 0];
                widest = Math.Max(widest, Math.Abs(fine - coarse));
                if (!connected) continue;
                // The bumpy side's edge folds exactly where the flat side is coarser.
                int x = (side - 1) >> fine, nodeZ = z >> fine;
                bool whole = (TerrainRenderer.EdgeMask(westTree, fine, x, nodeZ) & 2) != 0;
                Assert.Equal(coarse <= fine, whole);
            }
            if (connected) Assert.Equal(1, widest);
            else
            {
                Assert.Equal(new[] { (4, 0, 0) }, DrawnNodes(eastTree));
                Assert.True(widest >= 2);
            }
        }

        /// <summary>Orthographic, a node is drawn whole when the pixel error exceeds its error × the
        /// viewport's height / (2 × the orthographic size), wherever the camera is; seen in
        /// perspective from afar the same tree keeps its root.</summary>
        [Fact]
        public void OrthographicLevelsFollowTheScaleNotTheDistance()
        {
            var (terrain, data) = Bumpy(129, 0.3f);
            terrain.heightmapPixelError = 4f;
            var tree = terrain.Renderer.PatchesOf(data);
            float rootError = tree.Error[tree.Levels][0];
            var top = Matrix4x4.Ortho(-200f, 200f, -200f, 200f, 0.3f, 5000f) * View(new Vector3(128f, 3000f, 128f), new Vector3(128f, 0f, 128.001f));
            TerrainRenderer.UpdateLod(terrain, tree, new Vector3(128f, 3000f, 128f), top, true, 2f / rootError);
            Assert.Equal(new[] { (tree.Levels, 0, 0) }, DrawnNodes(tree));
            TerrainRenderer.UpdateLod(terrain, tree, new Vector3(128f, 3000f, 128f), top, true, 8f / rootError);
            Assert.DoesNotContain(DrawnNodes(tree), n => n.Level == tree.Levels);
            // Perspective, the same factor and place draws the root: the distance hides the error.
            TerrainRenderer.UpdateLod(terrain, tree, new Vector3(128f, 3000f, 128f), top, false, 8f / rootError);
            Assert.Equal(new[] { (tree.Levels, 0, 0) }, DrawnNodes(tree));
        }

        /// <summary>A node outside the view is drawn whole however large its error.</summary>
        [Fact]
        public void NodesOutsideTheViewAreDrawnWhole()
        {
            var (terrain, data) = Bumpy(129, 0.3f);
            terrain.heightmapPixelError = 1f;
            var tree = terrain.Renderer.PatchesOf(data);
            var camera = new Vector3(64f, 20f, -5f);
            var away = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 2000f) * View(camera, camera + new Vector3(0f, 0f, -1f));
            TerrainRenderer.UpdateLod(terrain, tree, camera, away, false, 1000f);
            Assert.Equal(new[] { (tree.Levels, 0, 0) }, DrawnNodes(tree));
            var toward = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 2000f) * View(camera, camera + new Vector3(0f, 0f, 1f));
            TerrainRenderer.UpdateLod(terrain, tree, camera, toward, false, 1000f);
            Assert.Contains(DrawnNodes(tree), n => n.Level == 0);
        }

        /// <summary>A patch takes the base map from the base map distance on, measured to its bounds'
        /// nearest point; orthographic views and templates without a base map never do.</summary>
        [Fact]
        public void PatchesPastTheBaseMapDistanceTakeTheBaseMap()
        {
            var bounds = new Bounds(new Vector3(100f, 0f, 0f), new Vector3(40f, 10f, 40f));
            var camera = Vector3.zero;
            float limit = TerrainRenderer.SqrBaseMapDistance(false, true, 90f);
            // The centre lies past 90 but the near face (x = 80) does not.
            Assert.True(TerrainRenderer.DrawsSplats(bounds, camera, limit));
            Assert.False(TerrainRenderer.DrawsSplats(bounds, camera, TerrainRenderer.SqrBaseMapDistance(false, true, 80f)));
            // At distance zero even a patch around the camera takes the base map.
            Assert.False(TerrainRenderer.DrawsSplats(new Bounds(camera, Vector3.one), camera, TerrainRenderer.SqrBaseMapDistance(false, true, 0f)));
            Assert.True(TerrainRenderer.DrawsSplats(bounds, camera, TerrainRenderer.SqrBaseMapDistance(true, true, 0f)));
            Assert.True(TerrainRenderer.DrawsSplats(bounds, camera, TerrainRenderer.SqrBaseMapDistance(false, false, 0f)));
        }

        /// <summary>A base map generation pass's "Size" tag scales the base map resolution.</summary>
        [Theory]
        [InlineData("1", 1f)]
        [InlineData("1/4", 0.25f)]
        [InlineData("0.5", 0.5f)]
        [InlineData("", 1f)]
        [InlineData("1/0", 1f)]
        [InlineData("half", 1f)]
        public void TheSizeTagScalesTheBaseMap(string tag, float expected) => Assert.Equal(expected, TerrainRenderer.SizeOf(tag));

        /// <summary>A base map's "Format" tag names a render texture format (URP's) or a texture
        /// format (the built-in shaders'); the maps are linear.</summary>
        [Theory]
        [InlineData("RGBA32", GraphicsFormat.R8G8B8A8_UNorm)]
        [InlineData("ARGB32", GraphicsFormat.R8G8B8A8_UNorm)]
        [InlineData("R8", GraphicsFormat.R8_UNorm)]
        [InlineData("A2R10G10B10", GraphicsFormat.None)]
        [InlineData("7", GraphicsFormat.None)]
        [InlineData("", GraphicsFormat.None)]
        public void TheFormatTagNamesALinearFormat(string tag, GraphicsFormat expected) => Assert.Equal(expected, TerrainRenderer.BaseMapFormat(tag));

        /// <summary>GetMaximumHeightError and GetPatchMinMaxHeights list every patch level by level
        /// from the finest, row by row, in heights of 0 to 1: the finest patches have no error, a
        /// parent's range spans its children's, and a peak in one corner shows in that corner's
        /// patches only.</summary>
        [Fact]
        public void PatchErrorsAndRangesListEveryLevelFromTheFinest()
        {
            var data = new TerrainData { heightmapResolution = 65 };
            m_Objects.Add(data);
            var heights = new float[65, 65];
            heights[60, 3] = 0.5f; // a peak at x = 3, z = 60: the finest patch (0, 3)
            data.SetHeights(0, 0, heights);
            var errors = data.GetMaximumHeightError();
            var extents = data.GetPatchMinMaxHeights();
            Assert.Equal(16 + 4 + 1, errors.Length);
            Assert.Equal(errors.Length, extents.Length);
            for (int i = 0; i < 16; i++) Assert.Equal(0f, errors[i]);
            for (int i = 0; i < 16; i++) Assert.Equal(i == 3 * 4 + 0 ? 0.5f : 0f, extents[i].max, 4);
            // Level 1: the peak lies in node (0, 1), between its vertices; level 2: the root.
            for (int i = 0; i < 4; i++) Assert.Equal(i == 2 ? 0.5f : 0f, errors[16 + i], 4);
            Assert.Equal(0.5f, errors[20], 4);
            Assert.Equal(0.5f, extents[20].max, 4);
            Assert.Equal(0f, extents[20].min);
        }
    }
}
