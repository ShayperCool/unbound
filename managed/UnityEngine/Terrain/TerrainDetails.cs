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
            // Only coverage mode has a coverage (instance counts give 0).
            if (m_DetailScatterMode != DetailScatterMode.CoverageMode) return 0f;
            if (detailPrototypeIndex < 0 || detailPrototypeIndex >= m_DetailPrototypes.Length) return 0f;
            var p = m_DetailPrototypes[detailPrototypeIndex];
            float footprint = Footprint(p);
            return footprint > 0f ? p.m_Density / footprint : 0f;
        }

        /// <summary>The width an instance covers: the average width, for meshes drawn without
        /// instancing times the diagonal of the mesh's bounds across x and z (measured with a
        /// cube).</summary>
        static float Footprint(DetailPrototype p)
        {
            float width = 0.5f * (p.m_MinWidth + p.m_MaxWidth);
            if (p.usePrototypeMesh && !p.useInstancing && p.m_Prototype != null
                && p.m_Prototype.GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null)
            {
                var size = filter.sharedMesh.bounds.size;
                width *= Mathf.Sqrt(size.x * size.x + size.z * size.z);
            }
            return width;
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
        /// A patch's instances of a layer. Instance count mode: each sample's count (at most 16)
        /// of points of the R2 sequence, starting for the sample at (seed mod 4095) + 255 × row,
        /// where the seed (noiseSeed + the sample's index in the patch + 1013 × the patch's index,
        /// patches column by column) also seeds the sample's generator, which gives each instance
        /// three numbers: jitter in x and z (positionJitter / √count of a sample) and the rotation.
        /// Sizes follow Mathf.PerlinNoise of the position × noiseSpread.
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
            float cellX = size.x / m_DetailResolution, cellZ = size.z / m_DetailResolution;
            if (m_DetailScatterMode == DetailScatterMode.InstanceCountMode)
            {
                int patchTerm = 1013 * (patchX * patches + patchY);
                for (int ly = 0; ly < perPatch; ly++)
                    for (int lx = 0; lx < perPatch; lx++)
                    {
                        int gx = patchX * perPatch + lx, gy = patchY * perPatch + ly;
                        if (gx >= m_DetailResolution || gy >= m_DetailResolution) continue;
                        int count = Math.Min(16, (int)values[gy * m_DetailResolution + gx]);
                        if (count == 0) continue;
                        int seed = prototype.m_NoiseSeed + lx + ly * perPatch + patchTerm;
                        var random = new Rand((uint)seed);
                        int first = (int)((uint)seed % 4095u) + 255 * ly;
                        float jitter = prototype.m_PositionJitter / (float)Math.Sqrt(count);
                        for (int i = 0; i < count; i++)
                        {
                            float u = random.Float(), v = random.Float(), w = random.Float();
                            float k = first + i + 2;
                            float fx = Frac(0.5f + k * s_R2X), fz = Frac(0.5f + k * s_R2Z);
                            if (jitter != 0f)
                            {
                                fx = Frac(fx + jitter * (0.5f - u));
                                fz = Frac(fz + jitter * (0.5f - v));
                            }
                            list.Add(Instance(prototype, (gx + fx) * cellX, (gy + fz) * cellZ, w));
                        }
                    }
            }
            else
            {
                // Coverage mode: n candidates a side per sample (the sample's size × density over
                // the prototype's footprint), each kept when a draw falls under the coverage
                // squared. Under 0.75 a side, a sample gets one candidate when its first two draws
                // fall under n (measured on lone samples; next to covered samples and the terrain's
                // edge the Editor keeps more, by a rule not known yet).
                float footprint = Footprint(prototype);
                float n = footprint > 0f ? cellX * prototype.m_Density / footprint : 0f;
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
                        float o0 = random.Float(), o1 = random.Float();
                        int side = n >= 0.75f ? (int)Math.Floor(n + 0.5f) : (o0 < n && o1 < n ? 1 : 0);
                        if (side == 0) continue;
                        float keep = value / 255f;
                        keep *= keep;
                        int first = (int)((uint)seed % 4095u) + 255 * ly;
                        float jitter = prototype.m_PositionJitter / side;
                        for (int c = 0; c < side * side; c++)
                        {
                            if (!(random.Float() < keep)) continue;
                            float u = random.Float(), v = random.Float(), w = random.Float();
                            float k = first + c + 2;
                            float fx = Frac(0.5f + k * s_R2X), fz = Frac(0.5f + k * s_R2Z);
                            if (jitter != 0f)
                            {
                                fx = Frac(fx + jitter * (0.5f - u));
                                fz = Frac(fz + jitter * (0.5f - v));
                            }
                            list.Add(Instance(prototype, (gx + fx) * cellX, (gy + fz) * cellZ, w));
                        }
                    }
            }
            bounds = InstanceBounds(prototype, list);
            return list.ToArray();
        }

        DetailInstanceTransform Instance(DetailPrototype prototype, float x, float z, float turn)
        {
            float noise = Mathf.PerlinNoise(x * prototype.m_NoiseSpread, z * prototype.m_NoiseSpread);
            return new DetailInstanceTransform
            {
                posX = x,
                posY = GetInterpolatedHeight(x / size.x, z / size.z),
                posZ = z,
                scaleXZ = prototype.m_MinWidth + (prototype.m_MaxWidth - prototype.m_MinWidth) * noise,
                scaleY = prototype.m_MinHeight + (prototype.m_MaxHeight - prototype.m_MinHeight) * noise,
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
