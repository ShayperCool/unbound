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
        /// <summary>Bumped when the details or their prototypes change.</summary>
        internal int m_DetailsVersion;

        public DetailScatterMode detailScatterMode => m_DetailScatterMode;

        public void SetDetailScatterMode(DetailScatterMode detailScatterMode)
        {
            m_DetailScatterMode = detailScatterMode;
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
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int gx = xBase + x, gy = yBase + y;
                    if (gx >= 0 && gy >= 0 && gx < m_DetailResolution && gy < m_DetailResolution)
                        values[gy * m_DetailResolution + gx] = (byte)Mathf.Clamp(details[y, x], 0, 255);
                }
            m_DetailsVersion++;
        }

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
            float footprint = Footprint(p);
            return footprint > 0f ? p.m_Density / footprint : 0f;
        }

        /// <summary>The width an instance covers: the average width times the prototype's width
        /// across x (<see cref="PrototypeExtentX"/> twice).</summary>
        static float Footprint(DetailPrototype p)
        {
            float extent = PrototypeExtentX(p);
            return (p.m_MaxWidth + p.m_MinWidth) * (extent + extent) * 0.5f;
        }

        /// <summary>Half the prototype's width across x as the Editor sizes it: a grass texture is
        /// a unit quad (0.5); a mesh drawn without instancing, any turn of its vertices (their
        /// largest distance from the y axis); a mesh drawn with instancing, its bounds.</summary>
        static float PrototypeExtentX(DetailPrototype p)
        {
            if (!p.usePrototypeMesh || p.m_Prototype == null) return 0.5f;
            var mesh = p.m_Prototype.GetComponent<MeshFilter>() is MeshFilter filter ? filter.sharedMesh : null;
            if (mesh == null) return 0f;
            if (p.useInstancing) return mesh.bounds.extents.x;
            float radius = 0f;
            foreach (var v in mesh.m_Vertices)
                radius = Math.Max(radius, (float)Math.Sqrt(v.z * v.z + v.x * v.x));
            return radius;
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

        /// <summary>
        /// A patch's instances of a layer, sample by sample (rows of the patch, then columns). A
        /// sample's generator is seeded by noiseSeed + the sample's index in the patch + 1013 × the
        /// patch's index (patches column by column). Instance count mode: the sample's value (at
        /// most 16) is its count. Coverage mode: the count comes from
        /// <see cref="CoverageCandidates"/>, and each candidate is kept when a draw is at most
        /// the coverage squared. Each instance then draws three numbers: its place in the sample, and
        /// its turn. The place is a point of the R2 sequence (numbered from (seed mod 4095) + 2 +
        /// 255 × the sample's row, one more per candidate) jittered by positionJitter × (draw -
        /// 0.5) / √count in x and z, or with QualitySettings.useLegacyDetailDistribution the two
        /// draws themselves. Sizes follow Mathf.PerlinNoise of the position × noiseSpread.
        /// </summary>
        public DetailInstanceTransform[] ComputeDetailInstanceTransforms(int patchX, int patchY, int layer, float density, out Bounds bounds)
        {
            bounds = default;
            var list = new List<DetailInstanceTransform>();
            if (layer < 0 || layer >= m_DetailPrototypes.Length) return list.ToArray();
            var prototype = m_DetailPrototypes[layer];
            var values = DetailLayer(layer, false);
            int patches = detailPatchCount, perPatch = m_DetailResolutionPerPatch;
            if (values == null || !prototype.Validate() || patchX < 0 || patchY < 0 || patchX >= patches || patchY >= patches)
                return list.ToArray();
            var size = this.size;
            bool coverage = m_DetailScatterMode == DetailScatterMode.CoverageMode;
            bool legacy = QualitySettings.useLegacyDetailDistribution;
            float cellX = size.x * (1f / perPatch / patches), cellZ = size.z * (1f / perPatch / patches);
            float perUnit = 0f;
            if (coverage)
            {
                float footprint = Footprint(prototype);
                perUnit = footprint > 0f ? 1f / footprint * prototype.m_Density : 0f;
            }
            float sampleUv = 1f / (patches * perPatch);
            int patchTerm = 1013 * (patchX * patches + patchY);
            for (int ly = 0; ly < perPatch; ly++)
                for (int lx = 0; lx < perPatch; lx++)
                {
                    int gx = patchX * perPatch + lx, gy = patchY * perPatch + ly;
                    if (gx >= m_DetailResolution || gy >= m_DetailResolution) continue;
                    int value = values[gy * m_DetailResolution + gx];
                    if (value == 0) continue;
                    int seed = prototype.m_NoiseSeed + lx + ly * perPatch + patchTerm;
                    var random = new Rand((uint)seed);
                    float keep = 0f;
                    int count;
                    if (coverage)
                    {
                        keep = value / 255f * (value / 255f);
                        count = CoverageCandidates(perUnit, cellX, cellZ, ref random, lx, ly);
                    }
                    else count = Math.Min(16, value);
                    if (count <= 0) continue;
                    float spread = (float)Math.Sqrt(count);
                    float cornerU = ((float)lx / perPatch + patchX) / patches;
                    float cornerV = ((float)ly / perPatch + patchY) / patches;
                    int point = (int)((uint)seed % 4095u) + 2 + 255 * ly;
                    for (int c = 0; c < count; c++, point++)
                    {
                        if (coverage && !(random.Float() <= keep)) continue;
                        float u = random.Float(), v = random.Float(), w = random.Float();
                        float fx = u, fz = v;
                        if (!legacy)
                        {
                            fx = Frac(Frac(point * s_R2X + 0.5f) - prototype.m_PositionJitter * ((u - 0.5f) / spread));
                            fz = Frac(Frac(point * s_R2Z + 0.5f) - prototype.m_PositionJitter * ((v - 0.5f) / spread));
                        }
                        list.Add(Instance(prototype, Frac(fx * sampleUv + cornerU), Frac(fz * sampleUv + cornerV), w));
                    }
                }
            bounds = InstanceBounds(prototype, list);
            return list.ToArray();
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

        /// <summary>An instance at the terrain's normalized position (u, v).</summary>
        DetailInstanceTransform Instance(DetailPrototype prototype, float u, float v, float turn)
        {
            var size = this.size;
            float x = size.x * u, z = size.z * v;
            float noise = Mathf.PerlinNoise(x * prototype.m_NoiseSpread, z * prototype.m_NoiseSpread);
            return new DetailInstanceTransform
            {
                posX = x,
                posY = GetInterpolatedHeight(u, v),
                posZ = z,
                scaleXZ = prototype.m_MinWidth * (1f - noise) + prototype.m_MaxWidth * noise,
                scaleY = prototype.m_MinHeight * (1f - noise) + prototype.m_MaxHeight * noise,
                rotationY = turn * 2f * Mathf.PI,
            };
        }

        /// <summary>The instances' bounds (measured): a grass quad spans its width along its
        /// turned x axis, at its position's height only; a mesh, its mesh's bounds turned and
        /// scaled.</summary>
        static Bounds InstanceBounds(DetailPrototype prototype, List<DetailInstanceTransform> instances)
        {
            if (instances.Count == 0) return default;
            Bounds? mesh = null;
            if (prototype.usePrototypeMesh && prototype.m_Prototype != null && prototype.m_Prototype.GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null)
                mesh = filter.sharedMesh.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var i in instances)
            {
                float c = Mathf.Cos(i.rotationY), s = Mathf.Sin(i.rotationY);
                var position = new Vector3(i.posX, i.posY, i.posZ);
                if (mesh is Bounds b)
                {
                    // The turned box: center turned, extents by |cos| and |sin|.
                    var center = Vector3.Scale(b.center, new Vector3(i.scaleXZ, i.scaleY, i.scaleXZ));
                    var e = Vector3.Scale(b.extents, new Vector3(i.scaleXZ, i.scaleY, i.scaleXZ));
                    var turned = new Vector3(c * center.x + s * center.z, center.y, -s * center.x + c * center.z);
                    var extent = new Vector3(Math.Abs(c) * e.x + Math.Abs(s) * e.z, e.y, Math.Abs(s) * e.x + Math.Abs(c) * e.z);
                    min = Vector3.Min(min, position + turned - extent);
                    max = Vector3.Max(max, position + turned + extent);
                }
                else
                {
                    var extent = new Vector3(Math.Abs(c) * i.scaleXZ * 0.5f, 0f, Math.Abs(s) * i.scaleXZ * 0.5f);
                    min = Vector3.Min(min, position - extent);
                    max = Vector3.Max(max, position + extent);
                }
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
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
            int samples = m_DetailResolutionPerPatch * m_DetailResolutionPerPatch, index = 0;
            foreach (var patch in detail.Get("m_Patches")?.Seq() ?? Array.Empty<YNode>())
            {
                int px = index % Math.Max(1, patchCount), py = index / Math.Max(1, patchCount);
                index++;
                var layers = Bytes(patch.Get("layerIndices"));
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
