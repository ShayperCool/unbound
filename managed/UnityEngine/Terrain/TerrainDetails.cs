using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    // A terrain's details (grass, meshes): per prototype a layer of values over the detail grid
    // (instance counts, or coverage 0-255 in coverage mode), kept by the Editor in patches of
    // detailResolutionPerPatch samples. Where the instances go follows the Editor's
    // ComputeDetailInstanceTransforms (measured: tests/projects/grass).
    public sealed partial class TerrainData
    {
        DetailScatterMode m_DetailScatterMode = DetailScatterMode.CoverageMode;
        /// <summary>Per prototype, the values row by row (z), detailResolution squared; null: none.</summary>
        byte[][] m_DetailLayers = new byte[0][];
        /// <summary>Per patch (patch rows z, then columns x), its layers in the order they got values
        /// there: a layer joins at the end when it gets a value and leaves when all its values in
        /// the patch are zero again. The order decides how the patch's meshes are built. A layer
        /// is a byte, as in the Editor's patches (so at most 256 layers).</summary>
        List<byte>[] m_PatchLayers = new List<byte>[0];
        /// <summary>Bumped when the details or their prototypes change.</summary>
        internal int m_DetailsVersion;

        /// <summary>A patch's layers in order; null when it has none.</summary>
        internal List<byte> PatchLayers(int patchX, int patchY)
        {
            int patches = detailPatchCount, index = patchY * patches + patchX;
            return patchX >= 0 && patchY >= 0 && patchX < patches && patchY < patches && index < m_PatchLayers.Length ? m_PatchLayers[index] : null;
        }

        /// <summary>A layer's place in a patch's order, or -1.</summary>
        int PatchLayerSlot(int patchX, int patchY, int layer)
        {
            var list = PatchLayers(patchX, patchY);
            return list != null ? list.IndexOf((byte)layer) : -1;
        }

        /// <summary>Starts every patch without layers (a new resolution).</summary>
        void ResetPatchLayers()
        {
            int patches = detailPatchCount;
            m_PatchLayers = new List<byte>[patches * patches];
        }

        /// <summary>Drops a removed prototype's layer from every patch and renumbers the later ones.</summary>
        void RemovePatchLayer(int index)
        {
            foreach (var list in m_PatchLayers)
            {
                if (list == null) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == index) list.RemoveAt(i);
                    else if (list[i] > index) list[i]--;
                }
            }
        }

        /// <summary>Drops the layers of prototypes from count on (fewer prototypes).</summary>
        void TruncatePatchLayers(int count)
        {
            foreach (var list in m_PatchLayers)
                list?.RemoveAll(layer => layer >= count);
        }

        public DetailScatterMode detailScatterMode => m_DetailScatterMode;

        /// <summary>Switching the mode starts every patch empty (values and layer order).</summary>
        public void SetDetailScatterMode(DetailScatterMode detailScatterMode)
        {
            if (m_DetailScatterMode == detailScatterMode) return;
            m_DetailScatterMode = detailScatterMode;
            m_DetailLayers = new byte[m_DetailPrototypes.Length][];
            ResetPatchLayers();
            m_DetailsVersion++;
        }

        byte[] DetailLayer(int layer, bool create)
        {
            if (layer < 0) return null;
            if (m_DetailLayers.Length <= layer)
            {
                if (!create) return null;
                Array.Resize(ref m_DetailLayers, layer + 1);
            }
            int size = m_DetailResolution * m_DetailResolution;
            if (m_DetailLayers[layer] == null || m_DetailLayers[layer].Length != size)
            {
                if (!create) return null;
                m_DetailLayers[layer] = new byte[size];
            }
            return m_DetailLayers[layer];
        }

        public int[,] GetDetailLayer(int xBase, int yBase, int width, int height, int layer)
        {
            if (layer < 0 || layer >= m_DetailPrototypes.Length)
                throw new ArgumentException($"Invalid detail layer index {layer}", nameof(layer));
            var result = new int[height, width];
            var values = DetailLayer(layer, false);
            if (values == null) return result;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int gx = xBase + x, gy = yBase + y;
                    if (gx >= 0 && gy >= 0 && gx < m_DetailResolution && gy < m_DetailResolution)
                        result[y, x] = values[gy * m_DetailResolution + gx];
                }
            return result;
        }

        public int[,] GetDetailLayer(Vector2Int positionBase, Vector2Int size, int layer) => GetDetailLayer(positionBase.x, positionBase.y, size.x, size.y, layer);

        public void SetDetailLayer(int xBase, int yBase, int layer, int[,] details)
        {
            if (details == null) throw new ArgumentNullException(nameof(details));
            if (layer < 0 || layer >= m_DetailPrototypes.Length)
                throw new ArgumentException($"Invalid detail layer index {layer}", nameof(layer));
            var values = DetailLayer(layer, true);
            int height = details.GetLength(0), width = details.GetLength(1);
            // Instance counts keep at most 16 a sample, coverage at most 255.
            int max = m_DetailScatterMode == DetailScatterMode.InstanceCountMode ? MaxInstancesPerSample : 255;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int gx = xBase + x, gy = yBase + y;
                    if (gx >= 0 && gy >= 0 && gx < m_DetailResolution && gy < m_DetailResolution)
                        values[gy * m_DetailResolution + gx] = (byte)Mathf.Clamp(details[y, x], 0, max);
                }
            // Each patch the area touches keeps the layer in its order while it has values there.
            int perPatch = m_DetailResolutionPerPatch, patches = detailPatchCount;
            if (perPatch <= 0 || patches <= 0 || width <= 0 || height <= 0) { m_DetailsVersion++; return; }
            if (m_PatchLayers.Length != patches * patches) ResetPatchLayers();
            int fromX = Math.Clamp(xBase / perPatch, 0, patches - 1), toX = Math.Clamp((xBase + width - 1) / perPatch, 0, patches - 1);
            int fromY = Math.Clamp(yBase / perPatch, 0, patches - 1), toY = Math.Clamp((yBase + height - 1) / perPatch, 0, patches - 1);
            for (int py = fromY; py <= toY; py++)
                for (int px = fromX; px <= toX; px++)
                {
                    bool any = false;
                    for (int y = py * perPatch; y < (py + 1) * perPatch && !any; y++)
                        for (int x = px * perPatch; x < (px + 1) * perPatch; x++)
                            if (values[y * m_DetailResolution + x] != 0) { any = true; break; }
                    var list = m_PatchLayers[py * patches + px] ??= new List<byte>();
                    int slot = list.IndexOf((byte)layer);
                    if (any && slot < 0) list.Add((byte)layer);
                    else if (!any && slot >= 0) list.RemoveAt(slot);
                }
            m_DetailsVersion++;
        }

        /// <summary>An instance count sample's largest value.</summary>
        const int MaxInstancesPerSample = 16;

        public void SetDetailLayer(Vector2Int basePosition, int layer, int[,] details) => SetDetailLayer(basePosition.x, basePosition.y, layer, details);

        /// <summary>The layers with values in the area.</summary>
        public int[] GetSupportedLayers(int xBase, int yBase, int totalWidth, int totalHeight)
        {
            var result = new List<int>();
            for (int layer = 0; layer < m_DetailPrototypes.Length; layer++)
            {
                var values = DetailLayer(layer, false);
                if (values == null) continue;
                bool any = false;
                for (int y = Math.Max(0, yBase); y < Math.Min(m_DetailResolution, yBase + totalHeight) && !any; y++)
                    for (int x = Math.Max(0, xBase); x < Math.Min(m_DetailResolution, xBase + totalWidth); x++)
                        if (values[y * m_DetailResolution + x] != 0) { any = true; break; }
                if (any) result.Add(layer);
            }
            return result.ToArray();
        }

        public int[] GetSupportedLayers(Vector2Int positionBase, Vector2Int size) => GetSupportedLayers(positionBase.x, positionBase.y, size.x, size.y);

        public float ComputeDetailCoverage(int detailPrototypeIndex)
        {
            if (detailPrototypeIndex < 0 || detailPrototypeIndex >= m_DetailPrototypes.Length)
            {
                Debug.LogError("Invalid detail prototype index");
                return 0f;
            }
            // Only coverage mode has a coverage.
            if (m_DetailScatterMode != DetailScatterMode.CoverageMode)
            {
                Debug.LogError("Detail coverage unsupported when terrain is set to InstanceCount scattering mode.");
                return 0f;
            }
            var p = m_DetailPrototypes[detailPrototypeIndex];
            if (!p.Validate()) return 0f;
            float footprint = Footprint(p, PrototypeShape(p));
            return footprint > 0f ? p.m_Density / footprint : 0f;
        }

        /// <summary>The width an instance covers: the average width times the prototype's width
        /// across x (its shape's extent twice).</summary>
        static float Footprint(DetailPrototype p, in Bounds shape)
        {
            float extent = shape.extents.x;
            return (p.m_MaxWidth + p.m_MinWidth) * (extent + extent) * 0.5f;
        }

        /// <summary>The prototype's shape as the Editor sizes it, centered on the instance: a grass
        /// texture is a unit quad and a mesh drawn without instancing any turn of its vertices,
        /// both a segment along x as long as their largest distance from the y axis on each side;
        /// a mesh drawn with instancing, its bounds.</summary>
        static Bounds PrototypeShape(DetailPrototype p)
        {
            if (!p.usePrototypeMesh || p.m_Prototype == null) return new Bounds { extents = new Vector3(0.5f, 0f, 0f) };
            var mesh = p.m_Prototype.GetComponent<MeshFilter>() is MeshFilter filter ? filter.sharedMesh : null;
            if (mesh == null) return default;
            if (p.useInstancing) return mesh.bounds;
            float radius = 0f;
            foreach (var v in mesh.m_Vertices)
                radius = Math.Max(radius, (float)Math.Sqrt(v.z * v.z + v.x * v.x));
            return new Bounds { extents = new Vector3(radius, 0f, 0f) };
        }

        // ------------------------------------------------------------ instances

        /// <summary>Unity's xorshift128 generator (as UnityEngine.Random), seeded per cell.</summary>
        struct Rand
        {
            uint m_X, m_Y, m_Z, m_W;

            internal Rand(uint seed)
            {
                m_X = seed;
                m_Y = m_X * 1812433253u + 1u;
                m_Z = m_Y * 1812433253u + 1u;
                m_W = m_Z * 1812433253u + 1u;
            }

            internal float Float()
            {
                uint t = m_X ^ (m_X << 11);
                m_X = m_Y;
                m_Y = m_Z;
                m_Z = m_W;
                m_W = m_W ^ (m_W >> 19) ^ t ^ (t >> 8);
                return (m_W & 0x7FFFFFu) * (1f / 8388607f);
            }
        }

        // The R2 low-discrepancy sequence (1 / g and 1 / g² with g the plastic number), in floats.
        static readonly float s_R2X = 1f / 1.32471795724474602596f;
        static readonly float s_R2Z = s_R2X * s_R2X;

        static float Frac(float v) => v - (float)Math.Floor(v);

        /// <summary>What a layer's instances take from its prototype, found once for all patches.</summary>
        internal readonly struct DetailLayerSetup
        {
            internal readonly DetailPrototype Prototype;
            internal readonly int Index;
            internal readonly byte[] Values;
            internal readonly Bounds Shape;
            /// <summary>Coverage mode: the prototype's instances per unit (density over footprint).</summary>
            internal readonly float PerUnit;

            internal DetailLayerSetup(DetailPrototype prototype, int index, byte[] values, Bounds shape, float perUnit)
            {
                Prototype = prototype;
                Index = index;
                Values = values;
                Shape = shape;
                PerUnit = perUnit;
            }

            internal bool IsValid => Values != null;
        }

        /// <summary>A layer's setup; not valid without values or with a refused prototype.</summary>
        internal DetailLayerSetup PrepareDetailLayer(int layer)
        {
            if (layer < 0 || layer >= m_DetailPrototypes.Length) return default;
            var prototype = m_DetailPrototypes[layer];
            var values = DetailLayer(layer, false);
            if (values == null || !prototype.Validate()) return default;
            var shape = PrototypeShape(prototype);
            float perUnit = 0f;
            if (m_DetailScatterMode == DetailScatterMode.CoverageMode)
            {
                float footprint = Footprint(prototype, shape);
                perUnit = footprint > 0f ? 1f / footprint * prototype.m_Density : 0f;
            }
            return new DetailLayerSetup(prototype, layer, values, shape, perUnit);
        }

        public DetailInstanceTransform[] ComputeDetailInstanceTransforms(int patchX, int patchY, int layer, float density, out Bounds bounds)
        {
            var placements = new List<Matrix4x4>();
            PlaceDetails(PrepareDetailLayer(layer), patchX, patchY, density, placements, out bounds);
            var result = new DetailInstanceTransform[placements.Count];
            for (int i = 0; i < result.Length; i++) result[i] = InstanceTransform(placements[i]);
            return result;
        }

        /// <summary>
        /// A patch's instances of a layer as ComputeDetailInstanceTransforms reports them (and
        /// instanced meshes are drawn), as terrain-relative transforms, sample by sample (rows of
        /// the patch, then columns). A sample's generator is seeded by noiseSeed (or, when it is
        /// zero, the samples a patch holds × the layer's place in the patch's order) + the sample's
        /// index in the patch + 1013 × the patch's index (patches column by column). Instance count
        /// mode: the sample's value is its count (with useDensityScaling, the value × the density
        /// plus an ordered dither, rounded down). Coverage mode: the count comes from
        /// <see cref="CoverageCandidates"/> (with useDensityScaling, of the instances per unit ×
        /// the density), and each candidate is kept when a draw is at most the coverage squared. Each instance then draws three numbers: its place in the sample, and
        /// its turn. The place is a point of the R2 sequence (numbered from (seed mod 4095) + 2 +
        /// 255 × (rowOffset + the sample's row), one more per candidate) moved by positionJitter ×
        /// (draw - 0.5) / √count in x and z and wrapped into the sample, or with
        /// QualitySettings.useLegacyDetailDistribution the two draws themselves. Sizes follow
        /// Mathf.PerlinNoise of the position × noiseSpread; the transform turns the instance by the
        /// third draw × 2π about y, then tilts it from up towards the terrain's normal by
        /// alignToGround. The bounds hold every instance's transformed shape.
        /// </summary>
        internal void PlaceDetails(in DetailLayerSetup layer, int patchX, int patchY, float density, List<Matrix4x4> into, out Bounds bounds, int rowOffset = 0)
        {
            int used = 0;
            Place(layer, patchX, patchY, density, false, rowOffset, 0, ref used, into, out bounds);
        }

        /// <summary>
        /// A patch's instances of a layer as the patch's mesh of its render mode is built (grass,
        /// billboards and meshes drawn without instancing): as <see cref="PlaceDetails"/> but for
        /// the place, numbered from (seed mod 1000) + 2 + 255 × the number of the mesh's layers
        /// placed before this one, and moved from the sequence's point towards the draws by
        /// positionJitter; billboards draw no turn. The mesh holds at most 65536 vertices: a sample
        /// gets no more candidates than the room left, and the layer stops at a sample with no room
        /// for one more. False when it stopped so.
        /// </summary>
        internal bool PlacePatchMeshDetails(in DetailLayerSetup layer, int patchX, int patchY, float density, int placedLayers, int verticesPerInstance, ref int usedVertices, List<Matrix4x4> into)
            => Place(layer, patchX, patchY, density, true, placedLayers, verticesPerInstance, ref usedVertices, into, out _);

        /// <summary>The 8 × 8 ordered dither of instance counts scaled by the density (1 to 64; a
        /// sample at column x and row y of its patch reads [(x mod 8) × 8 + y mod 8]).</summary>
        static readonly float[] DensityDither =
        {
            1, 49, 13, 61, 4, 52, 16, 64, 33, 17, 45, 29, 36, 20, 48, 32,
            9, 57, 5, 53, 12, 60, 8, 56, 41, 25, 37, 21, 44, 28, 40, 24,
            3, 51, 15, 63, 2, 50, 14, 62, 35, 19, 47, 31, 34, 18, 46, 30,
            11, 59, 7, 55, 10, 58, 6, 54, 43, 27, 39, 23, 42, 26, 38, 22,
        };

        /// <summary>A patch mesh's largest vertex count.</summary>
        const int MaxPatchMeshVertices = 0x10000;

        bool Place(in DetailLayerSetup layer, int patchX, int patchY, float density, bool patchMesh, int offset, int verticesPerInstance,
            ref int usedVertices, List<Matrix4x4> into, out Bounds bounds)
        {
            bounds = default;
            into.Clear();
            int patches = detailPatchCount, perPatch = m_DetailResolutionPerPatch;
            if (!layer.IsValid || layer.Values.Length != m_DetailResolution * m_DetailResolution
                || patchX < 0 || patchY < 0 || patchX >= patches || patchY >= patches)
                return true;
            var prototype = layer.Prototype;
            int slot = PatchLayerSlot(patchX, patchY, layer.Index);
            if (slot < 0) return true;
            var values = layer.Values;
            var size = this.size;
            bool coverage = m_DetailScatterMode == DetailScatterMode.CoverageMode;
            bool legacy = QualitySettings.useLegacyDetailDistribution;
            bool turns = !patchMesh || prototype.renderMode != DetailRenderMode.GrassBillboard;
            float cellX = size.x * (1f / perPatch / patches), cellZ = size.z * (1f / perPatch / patches);
            float sampleUv = 1f / (patches * perPatch);
            int patchTerm = 1013 * (patchX * patches + patchY);
            int seedBase = prototype.m_NoiseSeed != 0 ? prototype.m_NoiseSeed : perPatch * perPatch * slot;
            // useDensityScaling: the density scales the coverage, and the counts with an ordered
            // dither over 8 × 8 samples of the patch.
            bool scaled = prototype.m_UseDensityScaling != 0;
            float scale = scaled ? density : 1f;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int ly = 0; ly < perPatch; ly++)
                for (int lx = 0; lx < perPatch; lx++)
                {
                    int gx = patchX * perPatch + lx, gy = patchY * perPatch + ly;
                    if (gx >= m_DetailResolution || gy >= m_DetailResolution) continue;
                    int value = values[gy * m_DetailResolution + gx];
                    if (value == 0) continue;
                    if (patchMesh && usedVertices + verticesPerInstance > MaxPatchMeshVertices) return false;
                    int seed = seedBase + lx + ly * perPatch + patchTerm;
                    var random = new Rand((uint)seed);
                    float keep = 0f;
                    int count;
                    if (coverage)
                    {
                        keep = value / 255f * (value / 255f);
                        count = CoverageCandidates(scale * layer.PerUnit, cellX, cellZ, ref random, lx, ly);
                    }
                    else if (scaled)
                        count = (int)((DensityDither[(lx & 7) * 8 + (ly & 7)] + -0.5f) * (1f / 64f) + scale * value);
                    else count = value;
                    if (patchMesh && verticesPerInstance > 0)
                        count = Math.Min(count, (MaxPatchMeshVertices - usedVertices) / verticesPerInstance);
                    if (count <= 0) continue;
                    float spread = (float)Math.Sqrt(count);
                    float cornerU = ((float)lx / perPatch + patchX) / patches;
                    float cornerV = ((float)ly / perPatch + patchY) / patches;
                    int point = patchMesh
                        ? (int)((uint)seed % 1000u) + 2 + 255 * offset
                        : (int)((uint)seed % 4095u) + 2 + 255 * (offset + ly);
                    for (int c = 0; c < count; c++, point++)
                    {
                        if (coverage && !(random.Float() <= keep)) continue;
                        float u = random.Float(), v = random.Float();
                        float w = turns ? random.Float() : 0f;
                        float fx = u, fz = v, x, z;
                        if (patchMesh)
                        {
                            if (!legacy)
                            {
                                float sx = Frac(point * s_R2X + 0.5f), sz = Frac(point * s_R2Z + 0.5f);
                                fx = (u - sx) * prototype.m_PositionJitter + sx;
                                fz = (v - sz) * prototype.m_PositionJitter + sz;
                            }
                            x = fx * sampleUv + cornerU;
                            z = fz * sampleUv + cornerV;
                        }
                        else
                        {
                            if (!legacy)
                            {
                                fx = Frac(Frac(point * s_R2X + 0.5f) - prototype.m_PositionJitter * ((u - 0.5f) / spread));
                                fz = Frac(Frac(point * s_R2Z + 0.5f) - prototype.m_PositionJitter * ((v - 0.5f) / spread));
                            }
                            x = Frac(fx * sampleUv + cornerU);
                            z = Frac(fz * sampleUv + cornerV);
                        }
                        var model = Placement(prototype, x, z, w);
                        into.Add(model);
                        usedVertices += verticesPerInstance;
                        if (!patchMesh) Encapsulate(model, layer.Shape, ref min, ref max);
                    }
                }
            if (!patchMesh && into.Count > 0) bounds = new Bounds { center = (min + max) * 0.5f, extents = (max - min) * 0.5f };
            return true;
        }

        /// <summary>
        /// Coverage mode's candidates of a sample: the prototype's instances per unit (density over
        /// its footprint) less a random offset of up to half an instance (two draws, scaled by the
        /// sample's size, at most 1 per unit), counted as the steps of that spacing that start
        /// within the sample's columns and rows, from the patch's corner.
        /// </summary>
        static int CoverageCandidates(float perUnit, float cellX, float cellZ, ref Rand random, int lx, int ly)
        {
            if (!(perUnit > 0f)) return 0;
            float r0 = random.Float(), r1 = random.Float();
            float scaled = Math.Min(perUnit, 1f);
            float ox = Math.Clamp((r0 + -0.5f) * scaled / cellX, -0.5f, 0.5f);
            float oz = Math.Clamp((r1 + -0.5f) * scaled / cellZ, -0.5f, 0.5f);
            float stepX = 1f / Math.Abs(perUnit - ox + CoverageEpsilon);
            float stepZ = 1f / Math.Abs(perUnit - oz + CoverageEpsilon);
            int columns = (int)(Round((lx + 1) * cellX / stepX) - Round(lx * cellX / stepX));
            int rows = (int)(Round((ly + 1) * cellZ / stepZ) - Round(ly * cellZ / stepZ));
            return rows * columns;
        }

        /// <summary>Keeps coverage mode's spacing finite where the offset cancels the density
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/grass).</summary>
        const float CoverageEpsilon = 1e-6f;

        static float Round(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

        /// <summary>An instance's terrain-relative transform at the terrain's normalized position
        /// (u, v): its height there, its size from the noise, turned by the turn draw (a fraction
        /// of a full turn) and then tilted from up towards the terrain's normal by alignToGround.</summary>
        Matrix4x4 Placement(DetailPrototype prototype, float u, float v, float turn)
        {
            var size = this.size;
            float x = size.x * u, z = size.z * v;
            float noise = Mathf.PerlinNoise(x * prototype.m_NoiseSpread, z * prototype.m_NoiseSpread);
            float width = prototype.m_MinWidth * (1f - noise) + prototype.m_MaxWidth * noise;
            float height = prototype.m_MinHeight * (1f - noise) + prototype.m_MaxHeight * noise;
            float half = turn * Mathf.PI;
            var rotation = new Quaternion(0f, (float)Math.Sin(half), 0f, (float)Math.Cos(half));
            if (prototype.m_AlignToGround != 0f)
            {
                var up = Vector3.up;
                var aligned = Vector3.Slerp(up, GetInterpolatedNormal(u, v), prototype.m_AlignToGround);
                rotation = Quaternion.FromToRotation(up, aligned) * rotation;
            }
            return Matrix4x4.TRS(new Vector3(x, GetInterpolatedHeight(u, v), z), rotation, new Vector3(width, height, width));
        }

        /// <summary>What ComputeDetailInstanceTransforms reports of a transform: its position, the
        /// lengths of its x and y axes, and a turn about y read back from its x axis (the half angle
        /// whose sine is √((1 - cos) / 2), past half a turn when the axis points to +z), which
        /// for a tilted instance includes the tilt.</summary>
        internal static DetailInstanceTransform InstanceTransform(in Matrix4x4 m)
        {
            float scaleXZ = (float)Math.Sqrt(m.m20 * m.m20 + m.m10 * m.m10 + m.m00 * m.m00);
            float scaleY = (float)Math.Sqrt(m.m21 * m.m21 + m.m11 * m.m11 + m.m01 * m.m01);
            float sine = (float)Math.Sqrt((1f - m.m00 / scaleXZ) * 0.5f);
            float angle = (float)Math.Asin(sine);
            if (m.m20 / sine > 0f) angle = Mathf.PI - angle;
            return new DetailInstanceTransform
            {
                posX = m.m03,
                posY = m.m13,
                posZ = m.m23,
                scaleXZ = scaleXZ,
                scaleY = scaleY,
                rotationY = angle + angle,
            };
        }

        /// <summary>Grows min and max by the shape transformed: its center moved, its extents
        /// through the transform's absolute values.</summary>
        static void Encapsulate(in Matrix4x4 m, in Bounds shape, ref Vector3 min, ref Vector3 max)
        {
            var center = m.MultiplyPoint3x4(shape.center);
            var e = shape.extents;
            var extent = new Vector3(
                Math.Abs(m.m00) * e.x + Math.Abs(m.m01) * e.y + Math.Abs(m.m02) * e.z,
                Math.Abs(m.m10) * e.x + Math.Abs(m.m11) * e.y + Math.Abs(m.m12) * e.z,
                Math.Abs(m.m20) * e.x + Math.Abs(m.m21) * e.y + Math.Abs(m.m22) * e.z);
            min = Vector3.Min(min, center - extent);
            max = Vector3.Max(max, center + extent);
        }

        // ------------------------------------------------------------ serialized

        /// <summary>The detail database's prototypes, scatter mode and patches (each with its layers'
        /// values, layer by layer, row by row).</summary>
        void ReadDetails(YNode detail, IReferenceResolver resolver)
        {
            var prototypes = new List<DetailPrototype>();
            foreach (var p in detail.Get("m_DetailPrototypes")?.Seq() ?? Array.Empty<YNode>())
            {
                var prefab = p.Ref("prototype");
                var texture = p.Ref("prototypeTexture");
                prototypes.Add(new DetailPrototype
                {
                    m_Prototype = prefab.IsNull ? null : resolver.Resolve(prefab, typeof(GameObject)) as GameObject,
                    m_PrototypeTexture = texture.IsNull ? null : resolver.Resolve(texture, typeof(Texture2D)) as Texture2D,
                    m_MinWidth = p.Float("minWidth", 1f),
                    m_MaxWidth = p.Float("maxWidth", 2f),
                    m_MinHeight = p.Float("minHeight", 1f),
                    m_MaxHeight = p.Float("maxHeight", 2f),
                    m_NoiseSeed = p.Int("noiseSeed"),
                    m_NoiseSpread = p.Float("noiseSpread", 0.1f),
                    m_HoleEdgePadding = p.Float("holeTestRadius"),
                    m_Density = p.Float("density", 1f),
                    m_HealthyColor = p.Color("healthyColor", DetailPrototype.DefaultHealthColor),
                    m_DryColor = p.Color("dryColor", DetailPrototype.DefaultDryColor),
                    m_RenderMode = p.Int("renderMode", (int)DetailRenderMode.Grass),
                    m_UsePrototypeMesh = p.Int("usePrototypeMesh"),
                    m_UseInstancing = p.Int("useInstancing"),
                    m_UseDensityScaling = p.Int("useDensityScaling"),
                    m_AlignToGround = p.Float("alignToGround"),
                    m_PositionJitter = p.Float("positionJitter"),
                    m_TargetCoverage = p.Float("targetCoverage", 1f),
                });
            }
            m_DetailPrototypes = prototypes.ToArray();
            m_DetailScatterMode = (DetailScatterMode)detail.Int("m_DetailScatterMode", (int)DetailScatterMode.InstanceCountMode);
            int patchCount = detail.Int("m_PatchCount");
            m_DetailResolutionPerPatch = detail.Int("m_PatchSamples", 32);
            m_DetailResolution = patchCount * m_DetailResolutionPerPatch;
            m_DetailLayers = new byte[m_DetailPrototypes.Length][];
            ResetPatchLayers();
            int samples = m_DetailResolutionPerPatch * m_DetailResolutionPerPatch, index = 0;
            foreach (var patch in detail.Get("m_Patches")?.Seq() ?? Array.Empty<YNode>())
            {
                int px = index % Math.Max(1, patchCount), py = index / Math.Max(1, patchCount);
                var layers = Bytes(patch.Get("layerIndices"));
                if (index < m_PatchLayers.Length)
                {
                    var order = m_PatchLayers[index] = new List<byte>(layers.Length);
                    foreach (var l in layers)
                        if (l < m_DetailPrototypes.Length && !order.Contains(l)) order.Add(l);
                }
                index++;
                var coverage = Bytes(patch.Get("coverage") ?? patch.Get("numberOfObjects"));
                for (int l = 0; l < layers.Length; l++)
                {
                    var values = DetailLayer(layers[l], true);
                    if (values == null) continue;
                    for (int s = 0; s < samples && l * samples + s < coverage.Length; s++)
                    {
                        int gx = px * m_DetailResolutionPerPatch + s % m_DetailResolutionPerPatch;
                        int gy = py * m_DetailResolutionPerPatch + s / m_DetailResolutionPerPatch;
                        values[gy * m_DetailResolution + gx] = coverage[l * samples + s];
                    }
                }
            }
            m_DetailsVersion++;
        }

        /// <summary>A byte array as the readers give it (a hex string, or a list of numbers).</summary>
        static byte[] Bytes(YNode node)
        {
            if (node == null) return Array.Empty<byte>();
            var seq = node.Seq();
            if (seq != null)
            {
                var list = new List<byte>();
                foreach (var item in seq) list.Add((byte)item.AsInt());
                if (list.Count > 0) return list.ToArray();
            }
            var hex = node.AsString();
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
            return Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex.Substring(0, hex.Length - 1));
        }
    }
}
