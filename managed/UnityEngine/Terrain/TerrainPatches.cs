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
    // many of the coarsest levels split. Drawn neighbours then differ by one level at most, and a
    // node's edge next to a coarser one keeps only its even vertices, so the edges meet.
    internal sealed partial class TerrainRenderer
    {
        const int PatchCells = 16, PatchVertices = PatchCells + 1;
        internal const byte Hidden = 0, Split = 1, Drawn = 2;

        /// <summary>The quadtree of one heightmap: per level (0 the finest) its nodes row by row.</summary>
        internal sealed class PatchTree
        {
            internal int Levels;
            internal float[][] Error;
            internal Bounds[][] Bounds;
            internal byte[][] State;
            internal Mesh[][] Meshes;
            internal int[][] MeshMask;

            internal int Side(int level) => 1 << (Levels - level);
        }

        PatchTree m_Patches;
        TerrainData m_PatchData;
        int m_PatchVersion = -1;
        /// <summary>The index lists of a patch per edge mask (bit 1: the x = 0 edge is whole, 2: x =
        /// 16, 4: z = 16, 8: z = 0; a cleared bit folds that edge's odd vertices onto even ones).</summary>
        static readonly int[][] s_PatchIndices = new int[16][];

        void CollectPatches(Terrain terrain, TerrainData data, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var tree = PatchesOf(data);
            if (tree == null) return;
            var origin = terrain.GetPosition();
            int levels = tree.Levels;
            float pixelError = terrain.heightmapPixelError;
            float height = camera.Viewport.height > 0f ? camera.Viewport.height : Screen.height;
            bool orthographic = camera.Orthographic;
            float factor;
            if (orthographic)
            {
                float size = camera.Camera != null ? camera.Camera.orthographicSize : 5f;
                factor = height / (size + size);
            }
            else
            {
                float fov = camera.Camera != null ? camera.Camera.fieldOfView : 60f;
                float tangent = Math.Abs((float)Math.Tan(fov * 0.5f / 360f * (2f * Mathf.PI)));
                factor = 1f / tangent / ((pixelError + pixelError) / height);
            }
            UpdateLod(terrain, tree, camera.Position, camera.Projection * camera.View, orthographic, factor);
            var model = Matrix4x4.Translate(origin);
            for (int level = 0; level <= levels; level++)
            {
                int side = tree.Side(level);
                var state = tree.State[level];
                for (int z = 0; z < side; z++)
                    for (int x = 0; x < side; x++)
                        if (state[z * side + x] == Drawn)
                            FrameBuilder.AddMeshDraws(this, PatchMesh(tree, data, level, x, z, EdgeMask(tree, level, x, z)), model, Color.white, camera, into);
            }
        }

        /// <summary>Which nodes the camera draws (their state), the error factor as
        /// <see cref="CollectPatches"/> derives it from the camera.</summary>
        internal static void UpdateLod(Terrain terrain, PatchTree tree, Vector3 camera, Matrix4x4 frustum, bool orthographic, float factor)
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
            Enforce(tree, levels, 0, 0);
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

        static byte StateOf(PatchTree tree, int level, int x, int z)
        {
            int side = tree.Side(level);
            return x < 0 || z < 0 || x >= side || z >= side ? (byte)255 : tree.State[level][z * side + x];
        }

        /// <summary>A drawn node whose neighbour lies under a node two or more levels coarser
        /// splits that coarser node, so drawn neighbours differ by one level at most.</summary>
        static void Enforce(PatchTree tree, int level, int x, int z)
        {
            byte state = StateOf(tree, level, x, z);
            if (state == Split)
            {
                for (int c = 0; c < 4; c++) Enforce(tree, level - 1, x * 2 + (c & 1), z * 2 + (c >> 1));
                return;
            }
            if (state != Drawn || level >= tree.Levels) return;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (StateOf(tree, level, nx, nz) != Hidden) continue;
                int px = nx / 2, pz = nz / 2;
                if (StateOf(tree, level + 1, px, pz) == Hidden) SplitAbove(tree, level + 1, px, pz);
            }
        }

        /// <summary>Makes the hidden node a drawn one by splitting its drawn ancestor down to it.</summary>
        static void SplitAbove(PatchTree tree, int level, int x, int z)
        {
            if (level >= tree.Levels) return;
            int px = x / 2, pz = z / 2;
            if (StateOf(tree, level + 1, px, pz) == Hidden) SplitAbove(tree, level + 1, px, pz);
            if (StateOf(tree, level + 1, px, pz) != Drawn) return;
            int side = tree.Side(level);
            for (int c = 0; c < 4; c++)
            {
                int cx = px * 2 + (c & 1), cz = pz * 2 + (c >> 1);
                tree.State[level][cz * side + cx] = Drawn;
                HideBelow(tree, level, cx, cz);
            }
            tree.State[level + 1][pz * tree.Side(level + 1) + px] = Split;
            for (int c = 0; c < 4; c++) Enforce(tree, level, px * 2 + (c & 1), pz * 2 + (c >> 1));
        }

        /// <summary>An edge is whole next to the terrain's border or a neighbour drawn at the same
        /// level or finer; else it follows the coarser neighbour.</summary>
        internal static int EdgeMask(PatchTree tree, int level, int x, int z)
        {
            int mask = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                byte state = StateOf(tree, level, nx, nz);
                if (state == 255 || state == Split || state == Drawn) mask |= 1 << d;
            }
            return mask;
        }

        internal PatchTree PatchesOf(TerrainData data)
        {
            if (m_Patches != null && m_PatchData == data && m_PatchVersion == data.m_HeightsVersion) return m_Patches;
            DestroyPatches();
            m_PatchData = data;
            m_PatchVersion = data.m_HeightsVersion;
            int cells = data.heightmapResolution - 1;
            if (cells < PatchCells || (cells & (cells - 1)) != 0)
            {
                Warn($"terrain data '{data.name}' has a heightmap of {data.heightmapResolution} samples a side, not a power of two plus one from 17 on: it is not drawn.");
                return null;
            }
            int levels = 0;
            while ((PatchCells << levels) < cells) levels++;
            var tree = new PatchTree
            {
                Levels = levels,
                Error = new float[levels + 1][],
                Bounds = new Bounds[levels + 1][],
                State = new byte[levels + 1][],
                Meshes = new Mesh[levels + 1][],
                MeshMask = new int[levels + 1][],
            };
            var scale = data.heightmapScale;
            float heightUnit = data.size.y / TerrainData.MaxHeight;
            for (int level = 0; level <= levels; level++)
            {
                int side = tree.Side(level), count = side * side;
                tree.Error[level] = new float[count];
                tree.Bounds[level] = new Bounds[count];
                tree.State[level] = new byte[count];
                tree.Meshes[level] = new Mesh[count];
                tree.MeshMask[level] = new int[count];
                int span = PatchCells << level;
                for (int z = 0; z < side; z++)
                    for (int x = 0; x < side; x++)
                    {
                        int i = z * side + x;
                        tree.Error[level][i] = MaximumHeightError(data, x * span, z * span, level) * heightUnit;
                        int lowest = int.MaxValue, highest = int.MinValue;
                        for (int sz = z * span; sz <= (z + 1) * span; sz++)
                            for (int sx = x * span; sx <= (x + 1) * span; sx++)
                            {
                                int h = data.RawHeight(sx, sz);
                                if (h < lowest) lowest = h;
                                if (h > highest) highest = h;
                            }
                        var min = new Vector3(x * span * scale.x, lowest * heightUnit, z * span * scale.z);
                        var max = new Vector3((x + 1) * span * scale.x, highest * heightUnit, (z + 1) * span * scale.z);
                        var bounds = new Bounds();
                        bounds.SetMinMax(min, max);
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
            int step = 1 << level, span = PatchCells << level;
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

        /// <summary>A node's mesh: its 17 × 17 vertices (positions, the heightmap's normals, the
        /// heightmap's normalized coordinates in every uv channel), with the edge mask's indices.</summary>
        static Mesh PatchMesh(PatchTree tree, TerrainData data, int level, int x, int z, int mask)
        {
            int side = tree.Side(level), index = z * side + x;
            var mesh = tree.Meshes[level][index];
            if (mesh == null)
            {
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
                mesh = new Mesh { name = $"Terrain patch {level}:{x},{z}", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uvs;
                // Native terrain supplies normalized XZ coordinates in all four UV channels,
                // including TEXCOORD1 used by the installed terrain lightmap shaders.
                mesh.uv2 = uvs;
                mesh.uv3 = uvs;
                mesh.uv4 = uvs;
                tree.Meshes[level][index] = mesh;
                tree.MeshMask[level][index] = -1;
            }
            if (tree.MeshMask[level][index] != mask)
            {
                mesh.SetTriangles(PatchIndices(mask), 0, true);
                tree.MeshMask[level][index] = mask;
            }
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
            if (m_Patches == null) return;
            foreach (var level in m_Patches.Meshes)
                foreach (var mesh in level)
                    if (mesh != null) DestroyImmediate(mesh);
            m_Patches = null;
        }
    }
}
