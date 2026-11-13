using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // The heightmap drawn as the Editor draws a terrain without instancing (matches the Unity
    // 6000.0.71f1 Editor, tests/projects/terrainlod): a quadtree of patches, each node of level l
    // covering 16 × 2^l cells with 17 × 17 vertices every 2^l samples (level 0 the finest, the
    // root covering the whole heightmap). Each node knows its largest height error: how far the
    // samples it covers lie from the bilinear surface of its vertices. Per camera a node is drawn
    // whole when its error, seen from the camera, stays under the pixel error (perspective: the
    // error × the viewport's height / (2 × tan(fov / 2) × pixelError) is less than the node's
    // distance; orthographic: the error × the viewport's height / (2 × orthographic size) is less
    // than the pixel error), when it is at heightmapMaximumLOD's level, or when it is outside the
    // view; else its four children are considered. heightmapMinimumLODSimplification keeps that
    // many of the coarsest levels split. Drawn neighbours then differ by one level at most, across
    // connected tiles too (the terrain's neighbours of the same heightmap resolution and size), and
    // a node's edge next to a coarser one keeps only its even vertices, so the edges meet.
    internal sealed partial class TerrainRenderer
    {
        const int PatchCells = 16, PatchVertices = PatchCells + 1;
        internal const byte Hidden = 0, Split = 1, Drawn = 2;

        /// <summary>The quadtree of one heightmap: per level (0 the finest) its nodes row by row,
        /// and, while a camera's levels are worked out, the neighbouring tiles' trees.</summary>
        internal sealed class PatchTree
        {
            internal int Levels;
            internal float[][] Error;
            internal Bounds[][] Bounds;
            internal byte[][] State;
            /// <summary>A node's mesh per edge mask, made when first drawn so.</summary>
            internal readonly Dictionary<long, Mesh> Meshes = new Dictionary<long, Mesh>();
            /// <summary>The connected tiles: x - 1, x + 1, z + 1, z - 1.</summary>
            internal readonly PatchTree[] Neighbours = new PatchTree[4];

            internal int Side(int level) => 1 << (Levels - level);
        }

        PatchTree m_Patches;
        TerrainData m_PatchData;
        int m_PatchVersion = -1;
        bool m_PatchWarned;
        /// <summary>What the drawn levels were worked out for: the camera, its frame, place, view
        /// and viewport.</summary>
        Camera m_LodCamera;
        int m_LodFrame = -1;
        Vector3 m_LodPosition;
        Matrix4x4 m_LodFrustum;
        float m_LodHeight;
        Vector3 m_LodSettings;
        /// <summary>The index lists of a patch per edge mask (bit 1: the x = 0 edge is whole, 2: x =
        /// 16, 4: z = 16, 8: z = 0; a cleared bit folds that edge's odd vertices onto even ones).</summary>
        static readonly int[][] s_PatchIndices = new int[16][];
        static readonly List<Terrain> s_Group = new List<Terrain>();
        /// <summary>The group update a tile last joined, so joining is a compare, not a search.</summary>
        static int s_GroupStamp;
        int m_GroupStamp;

        void CollectPatches(Terrain terrain, TerrainData data, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var tree = PatchesOf(data);
            if (tree == null) return;
            float height = camera.Viewport.height > 0f ? camera.Viewport.height : Screen.height;
            var frustum = camera.Projection * camera.View;
            if (!LodCurrent(camera.Camera, camera.Position, frustum, height, Settings(terrain)))
                UpdateGroupLod(terrain, camera, frustum, height);
            var origin = terrain.GetPosition();
            var patches = new PatchDraw
            {
                Tree = tree, Data = data, Origin = origin, Model = Matrix4x4.Translate(origin),
                BaseDistance = SqrBaseMapDistance(camera.Orthographic, m_BaseMaterial != null, terrain.basemapDistance),
            };
            CollectNode(ref patches, tree.Levels, 0, 0, camera, into);
        }

        /// <summary>The squared distance from which patches take the base map: none in orthographic
        /// views or without a base map.</summary>
        internal static float SqrBaseMapDistance(bool orthographic, bool baseMap, float basemapDistance) =>
            orthographic || !baseMap ? float.PositiveInfinity : basemapDistance * basemapDistance;

        /// <summary>Whether a patch draws the splat passes: its bounds' nearest point is closer to
        /// the camera than the base map distance.</summary>
        internal static bool DrawsSplats(Bounds worldBounds, Vector3 camera, float sqrBaseMapDistance) =>
            worldBounds.SqrDistance(camera) < sqrBaseMapDistance;

        struct PatchDraw
        {
            internal PatchTree Tree;
            internal TerrainData Data;
            internal Vector3 Origin;
            internal Matrix4x4 Model;
            internal float BaseDistance;
        }

        /// <summary>Adds the drawn nodes under a node, walking down only through split ones.</summary>
        void CollectNode(ref PatchDraw patches, int level, int x, int z, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var tree = patches.Tree;
            int index = z * tree.Side(level) + x;
            byte state = tree.State[level][index];
            if (state == Drawn)
            {
                var bounds = tree.Bounds[level][index];
                bounds.center += patches.Origin;
                bool near = DrawsSplats(bounds, camera.Position, patches.BaseDistance);
                var mesh = PatchMesh(tree, patches.Data, level, x, z, EdgeMask(tree, level, x, z));
                FrameBuilder.AddMeshDraws(this, mesh, patches.Model, Color.white, camera, into,
                    firstMaterial: near ? 0 : m_Passes.Length, materialCount: near ? m_Passes.Length : 1);
            }
            else if (state == Split)
                for (int c = 0; c < 4; c++) CollectNode(ref patches, level - 1, x * 2 + (c & 1), z * 2 + (c >> 1), camera, into);
        }

        bool LodCurrent(Camera camera, Vector3 position, Matrix4x4 frustum, float height, Vector3 settings) =>
            m_LodCamera == camera && m_LodFrame == Time.frameCount && m_LodPosition == position && m_LodFrustum == frustum && m_LodHeight == height
            && m_LodSettings == settings;

        static Vector3 Settings(Terrain terrain) => new Vector3(terrain.heightmapPixelError, terrain.heightmapMaximumLOD, terrain.heightmapMinimumLODSimplification);

        /// <summary>Works out the drawn levels of the terrain and its connected tiles for the camera:
        /// each tile's own choice, then the one-level limit across all of them.</summary>
        internal static void UpdateGroupLod(Terrain terrain, FrameBuilder.CameraContext camera, Matrix4x4 frustum, float height)
        {
            var group = s_Group;
            group.Clear();
            group.Add(terrain);
            int stamp = ++s_GroupStamp;
            terrain.Renderer.m_GroupStamp = stamp;
            for (int i = 0; i < group.Count; i++)
            {
                var tile = group[i];
                var tileTree = tile.Renderer.PatchesOf(tile.terrainData);
                for (int d = 0; d < 4; d++)
                {
                    var neighbour = d == 0 ? tile.leftNeighbor : d == 1 ? tile.rightNeighbor : d == 2 ? tile.topNeighbor : tile.bottomNeighbor;
                    PatchTree neighbourTree = null;
                    if (neighbour != null && neighbour.Renderer != null && neighbour.terrainData != null && neighbour.drawHeightmap
                        && neighbour.terrainData.heightmapResolution == tile.terrainData.heightmapResolution && neighbour.terrainData.size == tile.terrainData.size)
                    {
                        neighbourTree = neighbour.Renderer.PatchesOf(neighbour.terrainData);
                        if (neighbourTree != null && neighbour.Renderer.m_GroupStamp != stamp)
                        {
                            neighbour.Renderer.m_GroupStamp = stamp;
                            group.Add(neighbour);
                        }
                    }
                    if (tileTree != null) tileTree.Neighbours[d] = neighbourTree;
                }
            }
            foreach (var tile in group)
            {
                var renderer = tile.Renderer;
                if (renderer.m_Patches == null) continue;
                UpdateLod(tile, renderer.m_Patches, camera.Position, frustum, camera.Orthographic, Factor(tile, camera, height), false);
                renderer.m_LodCamera = camera.Camera;
                renderer.m_LodFrame = Time.frameCount;
                renderer.m_LodPosition = camera.Position;
                renderer.m_LodFrustum = frustum;
                renderer.m_LodHeight = height;
                renderer.m_LodSettings = Settings(tile);
            }
            foreach (var tile in group)
                if (tile.Renderer.m_Patches is PatchTree t) Enforce(t, t.Levels, 0, 0);
            group.Clear();
        }

        /// <summary>The factor that turns a node's height error into the distance (perspective) or
        /// the pixels (orthographic) the pixel error compares it with.</summary>
        static float Factor(Terrain terrain, FrameBuilder.CameraContext camera, float height)
        {
            if (camera.Orthographic)
            {
                float size = camera.Camera != null ? camera.Camera.orthographicSize : 5f;
                return height / (size + size);
            }
            float fov = camera.Camera != null ? camera.Camera.fieldOfView : 60f;
            float tangent = Math.Abs((float)Math.Tan(fov * 0.5f / 360f * (2f * Mathf.PI)));
            float pixelError = terrain.heightmapPixelError;
            return 1f / tangent / ((pixelError + pixelError) / height);
        }

        /// <summary>Which nodes of one tile the camera draws (their state); with enforce, the
        /// one-level limit within the tile too.</summary>
        internal static void UpdateLod(Terrain terrain, PatchTree tree, Vector3 camera, Matrix4x4 frustum, bool orthographic, float factor, bool enforce = true)
        {
            int levels = tree.Levels;
            var lod = new Lod
            {
                Tree = tree,
                Origin = terrain.GetPosition(),
                Camera = camera,
                Frustum = frustum,
                Orthographic = orthographic,
                Factor = factor,
                PixelError = terrain.heightmapPixelError,
                Finest = Mathf.Clamp(terrain.heightmapMaximumLOD, 0, levels),
                Coarsest = Mathf.Clamp(levels - terrain.heightmapMinimumLODSimplification, 0, levels),
            };
            Calculate(ref lod, levels, 0, 0);
            if (enforce) Enforce(tree, levels, 0, 0);
        }

        struct Lod
        {
            internal PatchTree Tree;
            internal Vector3 Origin, Camera;
            internal Matrix4x4 Frustum;
            internal bool Orthographic;
            internal float Factor, PixelError;
            internal int Finest, Coarsest;
        }

        static void Calculate(ref Lod lod, int level, int x, int z)
        {
            var tree = lod.Tree;
            int side = tree.Side(level), index = z * side + x;
            float error = tree.Error[level][index];
            var bounds = tree.Bounds[level][index];
            bounds.center += lod.Origin;
            bool whole;
            if (lod.Orthographic)
                whole = !(lod.PixelError <= error * lod.Factor) || level == lod.Finest;
            else
            {
                float seen = lod.Factor * error;
                whole = seen * seen < bounds.SqrDistance(lod.Camera) || level == lod.Finest;
            }
            bool outside = !whole && !FrameBuilder.InFrustum(lod.Frustum, bounds);
            if (((whole || outside) && level <= lod.Coarsest) || level == 0)
            {
                tree.State[level][index] = Drawn;
                HideBelow(tree, level, x, z);
                return;
            }
            tree.State[level][index] = Split;
            for (int c = 0; c < 4; c++) Calculate(ref lod, level - 1, x * 2 + (c & 1), z * 2 + (c >> 1));
        }

        static void HideBelow(PatchTree tree, int level, int x, int z)
        {
            for (int l = level - 1, span = 2; l >= 0; l--, span *= 2)
            {
                int side = tree.Side(l);
                var state = tree.State[l];
                for (int zz = z * span; zz < (z + 1) * span; zz++)
                    for (int xx = x * span; xx < (x + 1) * span; xx++)
                        state[zz * side + xx] = Hidden;
            }
        }

        /// <summary>The node beside a node (d: x - 1, x + 1, z + 1, z - 1), in a connected tile past
        /// the edge; false past an unconnected edge.</summary>
        static bool Beside(ref PatchTree tree, int level, ref int x, ref int z, int d)
        {
            int side = tree.Side(level);
            x += d == 0 ? -1 : d == 1 ? 1 : 0;
            z += d == 2 ? 1 : d == 3 ? -1 : 0;
            if (x >= 0 && z >= 0 && x < side && z < side) return true;
            var neighbour = tree.Neighbours[d];
            if (neighbour == null || neighbour.Levels != tree.Levels) return false;
            tree = neighbour;
            x = (x + side) % side;
            z = (z + side) % side;
            return true;
        }

        /// <summary>A drawn node whose neighbour lies under a node two or more levels coarser
        /// splits that coarser node, so drawn neighbours differ by one level at most.</summary>
        static void Enforce(PatchTree tree, int level, int x, int z)
        {
            byte state = tree.State[level][z * tree.Side(level) + x];
            if (state == Split)
            {
                for (int c = 0; c < 4; c++) Enforce(tree, level - 1, x * 2 + (c & 1), z * 2 + (c >> 1));
                return;
            }
            if (state != Drawn || level >= tree.Levels) return;
            for (int d = 0; d < 4; d++)
            {
                var other = tree;
                int nx = x, nz = z;
                if (!Beside(ref other, level, ref nx, ref nz, d)) continue;
                if (other.State[level][nz * other.Side(level) + nx] != Hidden) continue;
                int px = nx / 2, pz = nz / 2;
                if (other.State[level + 1][pz * other.Side(level + 1) + px] == Hidden) SplitAbove(other, level + 1, px, pz);
            }
        }

        /// <summary>Makes the hidden node a drawn one by splitting its drawn ancestor down to it.</summary>
        static void SplitAbove(PatchTree tree, int level, int x, int z)
        {
            if (level >= tree.Levels) return;
            int px = x / 2, pz = z / 2, parentSide = tree.Side(level + 1);
            if (tree.State[level + 1][pz * parentSide + px] == Hidden) SplitAbove(tree, level + 1, px, pz);
            if (tree.State[level + 1][pz * parentSide + px] != Drawn) return;
            int side = tree.Side(level);
            for (int c = 0; c < 4; c++)
            {
                int cx = px * 2 + (c & 1), cz = pz * 2 + (c >> 1);
                tree.State[level][cz * side + cx] = Drawn;
                HideBelow(tree, level, cx, cz);
            }
            tree.State[level + 1][pz * parentSide + px] = Split;
            for (int c = 0; c < 4; c++) Enforce(tree, level, px * 2 + (c & 1), pz * 2 + (c >> 1));
        }

        /// <summary>An edge is whole next to an unconnected border or a neighbour drawn at the same
        /// level or finer; else it follows the coarser neighbour.</summary>
        internal static int EdgeMask(PatchTree tree, int level, int x, int z)
        {
            int mask = 0;
            for (int d = 0; d < 4; d++)
            {
                var other = tree;
                int nx = x, nz = z;
                if (!Beside(ref other, level, ref nx, ref nz, d) || other.State[level][nz * other.Side(level) + nx] != Hidden)
                    mask |= 1 << d;
            }
            return mask;
        }

        /// <summary>The quadtree levels above the finest patches of a heightmap of that many samples
        /// a side.</summary>
        internal static int PatchLevels(int resolution)
        {
            int levels = 0;
            while ((PatchCells << levels) < resolution - 1) levels++;
            return levels;
        }

        internal PatchTree PatchesOf(TerrainData data)
        {
            if (m_PatchData == data && m_PatchVersion == data.m_HeightsVersion) return m_Patches;
            DestroyPatches();
            m_PatchData = data;
            m_PatchVersion = data.m_HeightsVersion;
            int cells = data.heightmapResolution - 1;
            if (cells < PatchCells || (cells & (cells - 1)) != 0)
            {
                if (!m_PatchWarned)
                    Warn($"terrain data '{data.name}' has a heightmap of {data.heightmapResolution} samples a side, not a power of two plus one from 17 on: it is not drawn.");
                m_PatchWarned = true;
                return null;
            }
            int levels = PatchLevels(data.heightmapResolution);
            var tree = new PatchTree
            {
                Levels = levels,
                Error = new float[levels + 1][],
                Bounds = new Bounds[levels + 1][],
                State = new byte[levels + 1][],
            };
            var scale = data.heightmapScale;
            float heightUnit = data.size.y / TerrainData.MaxHeight;
            var lowest = new int[levels + 1][];
            var highest = new int[levels + 1][];
            for (int level = 0; level <= levels; level++)
            {
                int side = tree.Side(level), count = side * side;
                tree.Error[level] = new float[count];
                tree.Bounds[level] = new Bounds[count];
                tree.State[level] = new byte[count];
                lowest[level] = new int[count];
                highest[level] = new int[count];
                int span = PatchCells << level;
                for (int z = 0; z < side; z++)
                    for (int x = 0; x < side; x++)
                    {
                        int i = z * side + x;
                        tree.Error[level][i] = MaximumHeightError(data, x * span, z * span, level) * heightUnit;
                        int low = int.MaxValue, high = int.MinValue;
                        if (level == 0)
                        {
                            for (int sz = z * span; sz <= (z + 1) * span; sz++)
                                for (int sx = x * span; sx <= (x + 1) * span; sx++)
                                {
                                    int h = data.RawHeight(sx, sz);
                                    low = Math.Min(low, h);
                                    high = Math.Max(high, h);
                                }
                        }
                        else
                        {
                            // A node's heights are its four children's.
                            int childSide = tree.Side(level - 1);
                            for (int c = 0; c < 4; c++)
                            {
                                int child = (z * 2 + (c >> 1)) * childSide + x * 2 + (c & 1);
                                low = Math.Min(low, lowest[level - 1][child]);
                                high = Math.Max(high, highest[level - 1][child]);
                            }
                        }
                        lowest[level][i] = low;
                        highest[level][i] = high;
                        var bounds = new Bounds();
                        bounds.SetMinMax(new Vector3(x * span * scale.x, low * heightUnit, z * span * scale.z),
                            new Vector3((x + 1) * span * scale.x, high * heightUnit, (z + 1) * span * scale.z));
                        tree.Bounds[level][i] = bounds;
                    }
            }
            return m_Patches = tree;
        }

        /// <summary>A node's largest height error in raw heights: the largest distance of the
        /// samples it covers from the bilinear surface through its vertices (every 2^level samples).</summary>
        static float MaximumHeightError(TerrainData data, int x0, int z0, int level)
        {
            if (level == 0) return 0f;
            int step = 1 << level;
            float inverse = 1f / step, worst = 0f;
            for (int cz = 0; cz < PatchCells; cz++)
                for (int cx = 0; cx < PatchCells; cx++)
                {
                    int ax = x0 + cx * step, az = z0 + cz * step;
                    float h00 = data.RawHeight(ax, az), h10 = data.RawHeight(ax + step, az);
                    float h01 = data.RawHeight(ax, az + step), h11 = data.RawHeight(ax + step, az + step);
                    float dz = (h01 - h00) * inverse, dx = (h10 - h00) * inverse;
                    float dxz = (h11 - h10 - (h01 - h00)) * inverse * inverse;
                    for (int v = 0; v <= step; v++)
                        for (int u = 0; u <= step; u++)
                        {
                            float error = Math.Abs(u * v * dxz + u * dx + h00 + v * dz - data.RawHeight(ax + u, az + v));
                            if (error > worst) worst = error;
                        }
                }
            return worst;
        }

        /// <summary>A node's mesh for an edge mask: its 17 × 17 vertices (positions, the heightmap's
        /// normals, the heightmap's normalized coordinates in every uv channel) and the mask's
        /// indices; made once per node and mask, so cameras that see a node differently do not
        /// rebuild it.</summary>
        static Mesh PatchMesh(PatchTree tree, TerrainData data, int level, int x, int z, int mask)
        {
            long key = (((long)level << 40) | ((long)z << 20) | (uint)x) * 16 + mask;
            if (tree.Meshes.TryGetValue(key, out var mesh)) return mesh;
            int step = 1 << level, span = PatchCells << level, resolution = data.heightmapResolution;
            var scale = data.heightmapScale;
            var vertices = new Vector3[PatchVertices * PatchVertices];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            float inverse = 1f / (resolution - 1);
            for (int j = 0; j < PatchVertices; j++)
                for (int i = 0; i < PatchVertices; i++)
                {
                    int sx = x * span + i * step, sz = z * span + j * step, v = j * PatchVertices + i;
                    vertices[v] = new Vector3(sx * scale.x, data.Sample(sx, sz) * scale.y, sz * scale.z);
                    normals[v] = data.GridNormal(sx, sz);
                    uvs[v] = new Vector2(sx * inverse, sz * inverse);
                }
            mesh = new Mesh { name = "Terrain patch", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            // Native terrain supplies normalized XZ coordinates in all four UV channels,
            // including TEXCOORD1 used by the installed terrain lightmap shaders.
            mesh.uv2 = uvs;
            mesh.uv3 = uvs;
            mesh.uv4 = uvs;
            mesh.SetTriangles(PatchIndices(mask), 0, true);
            tree.Meshes[key] = mesh;
            return mesh;
        }

        /// <summary>Two triangles a cell across its (x, z)-(x + 1, z + 1) diagonal, an edge next to
        /// a coarser node keeping only its even vertices (its odd ones folded onto the even vertex
        /// before them), dropping the triangles that fold flat.</summary>
        internal static int[] PatchIndices(int mask)
        {
            var cached = s_PatchIndices[mask];
            if (cached != null) return cached;
            var list = new List<int>(PatchCells * PatchCells * 6);
            int Fold(int i, int j)
            {
                if ((mask & 1) == 0 && i == 0 && (j & 1) != 0) j--;
                if ((mask & 2) == 0 && i == PatchCells && (j & 1) != 0) j--;
                if ((mask & 4) == 0 && j == PatchCells && (i & 1) != 0) i--;
                if ((mask & 8) == 0 && j == 0 && (i & 1) != 0) i--;
                return j * PatchVertices + i;
            }
            void Triangle(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return;
                list.Add(a); list.Add(b); list.Add(c);
            }
            for (int j = 0; j < PatchCells; j++)
                for (int i = 0; i < PatchCells; i++)
                {
                    int v00 = Fold(i, j), v01 = Fold(i, j + 1), v11 = Fold(i + 1, j + 1), v10 = Fold(i + 1, j);
                    Triangle(v00, v01, v11);
                    Triangle(v00, v11, v10);
                }
            return s_PatchIndices[mask] = list.ToArray();
        }

        void DestroyPatches()
        {
            if (m_Patches != null)
                foreach (var mesh in m_Patches.Meshes.Values) DestroyImmediate(mesh);
            m_Patches = null;
            m_LodCamera = null;
            m_LodFrame = -1;
        }
    }
}
