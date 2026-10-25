using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A terrain's data (a binary `.asset` the Editor writes): the heightmap (heights in 1/32766 of
    /// the height, rows along z), the holes, the splat maps (four layers per RGBA texture) with their
    /// terrain layers, and the trees. Height and normal queries follow the Editor's (measured,
    /// tests/projects/terrain): a cell's two triangles share the diagonal from (x, z) to
    /// (x + 1, z + 1); a vertex's normal sums its quads' diagonal cross products, the heights
    /// extrapolated past the edges.
    /// </summary>
    [ClassId(156)]
    public sealed partial class TerrainData : Object, ISerializedAsset
    {
        internal const int MaxHeight = 32766;
        internal static readonly int k_MaximumResolution = 4097;
        internal static readonly int k_MinimumDetailResolutionPerPatch = 8;
        internal static readonly int k_MaximumDetailResolutionPerPatch = 128;
        internal static readonly int k_MaximumDetailPatchCount = 32;
        internal static readonly int k_MinimumAlphamapResolution = 16;
        internal static readonly int k_MaximumAlphamapResolution = 4096;
        internal static readonly int k_MinimumBaseMapResolution = 16;
        internal static readonly int k_MaximumBaseMapResolution = 2048;

        int m_Resolution = 33;
        ushort[] m_Heights = new ushort[33 * 33];
        Vector3 m_Scale = new Vector3(1f, 600f, 1f) * 1f;
        bool[] m_Solid = Filled(32 * 32);
        bool m_EnableHolesTextureCompression = true;
        int m_AlphamapResolution = 512;
        int m_BaseMapResolution = 1024;
        Texture2D[] m_AlphaTextures = new Texture2D[0];
        TerrainLayer[] m_Layers = new TerrainLayer[0];
        TreeInstance[] m_TreeInstances = new TreeInstance[0];
        TreePrototype[] m_TreePrototypes = new TreePrototype[0];
        DetailPrototype[] m_DetailPrototypes = new DetailPrototype[0];
        int m_DetailResolution, m_DetailResolutionPerPatch = 32;
        float m_WavingGrassStrength = 0.5f, m_WavingGrassAmount = 0.5f, m_WavingGrassSpeed = 0.5f;
        Color m_WavingGrassTint = new Color(0.7f, 0.6f, 0.5f, 0f);
        /// <summary>Bumped when the heights or holes change: colliders and meshes rebuild.</summary>
        internal int m_HeightsVersion;
        /// <summary>Bumped when the splat maps or layers change.</summary>
        internal int m_SplatVersion;
        /// <summary>Bumped when the trees or their prototypes change.</summary>
        internal int m_TreesVersion;
        /// <summary>The terrains and colliders that use this data.</summary>
        internal readonly List<Component> m_Users = new List<Component>();

        public TerrainData()
        {
            m_Scale = new Vector3(1000f / 32f, 600f, 1000f / 32f);
        }

        static bool[] Filled(int n)
        {
            var a = new bool[n];
            Array.Fill(a, true);
            return a;
        }

        // ------------------------------------------------------------ heightmap

        public int heightmapResolution
        {
            get => m_Resolution;
            set
            {
                // A power of two plus one, from 33 to 4097; the size stays.
                int resolution = GetAdjustedSize(value);
                var size = this.size;
                m_Resolution = resolution;
                m_Heights = new ushort[resolution * resolution];
                m_Solid = Filled((resolution - 1) * (resolution - 1));
                this.size = size;
                HeightsChanged();
            }
        }

        internal int GetAdjustedSize(int size)
        {
            int n = Mathf.Clamp(size, 33, k_MaximumResolution) - 1;
            return Mathf.ClosestPowerOfTwo(n) + 1;
        }

        [Obsolete("Please use heightmapResolution instead. (UnityUpgradable) -> heightmapResolution")]
        public int heightmapWidth => m_Resolution;
        [Obsolete("Please use heightmapResolution instead. (UnityUpgradable) -> heightmapResolution")]
        public int heightmapHeight => m_Resolution;

        public Vector3 heightmapScale => m_Scale;

        public Vector3 size
        {
            get => new Vector3(m_Scale.x * (m_Resolution - 1), m_Scale.y, m_Scale.z * (m_Resolution - 1));
            set
            {
                m_Scale = new Vector3(value.x / (m_Resolution - 1), value.y, value.z / (m_Resolution - 1));
                HeightsChanged();
            }
        }

        public Bounds bounds
        {
            get
            {
                int min = ushort.MaxValue, max = 0;
                foreach (var h in m_Heights)
                {
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
                if (m_Heights.Length == 0) min = max = 0;
                var s = size;
                float low = min / (float)MaxHeight * s.y, high = max / (float)MaxHeight * s.y;
                return new Bounds(new Vector3(s.x * 0.5f, (low + high) * 0.5f, s.z * 0.5f), new Vector3(s.x, high - low, s.z));
            }
        }

        [Obsolete("Terrain thickness is no longer required by the physics engine. Set appropriate continuous collision detection modes to fast moving bodies.")]
        public float thickness { get; set; }

        /// <summary>A heightmap sample (0..1) at a clamped grid point.</summary>
        internal float Sample(int x, int y)
        {
            x = Mathf.Clamp(x, 0, m_Resolution - 1);
            y = Mathf.Clamp(y, 0, m_Resolution - 1);
            return m_Heights[y * m_Resolution + x] / (float)MaxHeight;
        }

        internal ushort[] RawHeights => m_Heights;

        public float GetHeight(int x, int y) => Sample(x, y) * m_Scale.y;

        public float[,] GetHeights(int xBase, int yBase, int width, int height)
        {
            if (xBase < 0 || yBase < 0 || width < 0 || height < 0 || xBase + width > m_Resolution || yBase + height > m_Resolution)
                throw new ArgumentException("Trying to access out-of-bounds terrain height information.");
            var result = new float[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    result[y, x] = m_Heights[(yBase + y) * m_Resolution + xBase + x] / (float)MaxHeight;
            return result;
        }

        public void SetHeights(int xBase, int yBase, float[,] heights)
        {
            if (heights == null) throw new NullReferenceException();
            int height = heights.GetLength(0), width = heights.GetLength(1);
            if (xBase < 0 || yBase < 0 || xBase + width > m_Resolution || yBase + height > m_Resolution)
                throw new ArgumentException("X or Y base out of bounds. Setting up to " + (xBase + width) + "x" + (yBase + height) + " while map size is " + m_Resolution + "x" + m_Resolution);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    m_Heights[(yBase + y) * m_Resolution + xBase + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(heights[y, x]) * MaxHeight);
            HeightsChanged();
        }

        public void SetHeightsDelayLOD(int xBase, int yBase, float[,] heights) => SetHeights(xBase, yBase, heights);

        public void SyncHeightmap() => HeightsChanged();

        public void DirtyHeightmapRegion(RectInt region, TerrainHeightmapSyncControl syncControl) => HeightsChanged();

        [Obsolete("Please use DirtyHeightmapRegion instead.")]
        public void UpdateDirtyRegion(int x, int y, int width, int height, bool syncHeightmapTextureImmediately) => HeightsChanged();

        /// <summary>The trees changed: the terrains redraw them, the colliders rebuild theirs.</summary>
        internal void TreesChanged()
        {
            m_TreesVersion++;
            foreach (var user in m_Users.ToArray())
                if (user is TerrainCollider collider) collider.ShapeChanged();
        }

        internal void HeightsChanged()
        {
            m_HeightsVersion++;
            foreach (var user in m_Users.ToArray())
                if (user is TerrainCollider collider) collider.ShapeChanged();
        }

        /// <summary>The height (in units) at normalized coordinates, on the cell's triangles.</summary>
        public float GetInterpolatedHeight(float x, float y)
        {
            float fx = Mathf.Clamp01(x) * (m_Resolution - 1), fy = Mathf.Clamp01(y) * (m_Resolution - 1);
            int cx = Math.Min((int)fx, m_Resolution - 2), cy = Math.Min((int)fy, m_Resolution - 2);
            float tx = fx - cx, ty = fy - cy;
            float h00 = Sample(cx, cy), h10 = Sample(cx + 1, cy), h01 = Sample(cx, cy + 1), h11 = Sample(cx + 1, cy + 1);
            float h = tx > ty
                ? h00 + (h10 - h00) * tx + (h11 - h10) * ty
                : h00 + (h01 - h00) * ty + (h11 - h01) * tx;
            return h * m_Scale.y;
        }

        public float[,] GetInterpolatedHeights(float xBase, float yBase, int xCount, int yCount, float xInterval, float yInterval)
        {
            var result = new float[yCount, xCount];
            GetInterpolatedHeights(result, 0, 0, xBase, yBase, xCount, yCount, xInterval, yInterval);
            return result;
        }

        public void GetInterpolatedHeights(float[,] results, int resultXOffset, int resultYOffset, float xBase, float yBase, int xCount, int yCount, float xInterval, float yInterval)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            for (int y = 0; y < yCount; y++)
                for (int x = 0; x < xCount; x++)
                    results[resultYOffset + y, resultXOffset + x] = GetInterpolatedHeight(xBase + x * xInterval, yBase + y * yInterval);
        }

        /// <summary>A height (0..1) on the grid or past its edge, extrapolated linearly outward
        /// (diagonally past a corner), as the Editor's normals read it.</summary>
        float Extrapolated(int x, int y)
        {
            int n = m_Resolution - 1;
            int dx = x < 0 ? 1 : x > n ? -1 : 0, dy = y < 0 ? 1 : y > n ? -1 : 0;
            if (dx == 0 && dy == 0) return Sample(x, y);
            int ex = Mathf.Clamp(x, 0, n), ey = Mathf.Clamp(y, 0, n);
            return 2f * Sample(ex, ey) - Sample(ex + dx, ey + dy);
        }

        /// <summary>A grid point's normal: the sum of its four quads' normals (each the cross
        /// product of its diagonals), with heights extrapolated past the edges (measured).</summary>
        Vector3 VertexNormal(int x, int y)
        {
            float sx = m_Scale.x, sz = m_Scale.z;
            Vector3 sum = Vector3.zero;
            for (int cy = y - 1; cy <= y; cy++)
                for (int cx = x - 1; cx <= x; cx++)
                {
                    var p00 = new Vector3(cx * sx, Extrapolated(cx, cy) * m_Scale.y, cy * sz);
                    var p10 = new Vector3((cx + 1) * sx, Extrapolated(cx + 1, cy) * m_Scale.y, cy * sz);
                    var p01 = new Vector3(cx * sx, Extrapolated(cx, cy + 1) * m_Scale.y, (cy + 1) * sz);
                    var p11 = new Vector3((cx + 1) * sx, Extrapolated(cx + 1, cy + 1) * m_Scale.y, (cy + 1) * sz);
                    var n = Vector3.Cross(p01 - p10, p11 - p00);
                    if (n.y < 0f) n = -n;
                    sum += n;
                }
            return sum.normalized;
        }

        /// <summary>A grid point's normal (the terrain mesh's vertex normal).</summary>
        internal Vector3 GridNormal(int x, int y) => VertexNormal(x, y);

        public Vector3 GetInterpolatedNormal(float x, float y)
        {
            float fx = Mathf.Clamp01(x) * (m_Resolution - 1), fy = Mathf.Clamp01(y) * (m_Resolution - 1);
            int cx = Math.Min((int)fx, m_Resolution - 2), cy = Math.Min((int)fy, m_Resolution - 2);
            float tx = fx - cx, ty = fy - cy;
            var n = (VertexNormal(cx, cy) * (1f - tx) + VertexNormal(cx + 1, cy) * tx) * (1f - ty)
                + (VertexNormal(cx, cy + 1) * (1f - tx) + VertexNormal(cx + 1, cy + 1) * tx) * ty;
            return n.normalized;
        }

        /// <summary>The slope in degrees: the arc cosine of the normal's y, as the Editor computes it
        /// (on nearly flat ground a last-digit difference of the normal shows: 0.8673 vs 0.8675).</summary>
        public float GetSteepness(float x, float y) => Mathf.Acos(Mathf.Clamp(GetInterpolatedNormal(x, y).y, -1f, 1f)) * Mathf.Rad2Deg;

        public RenderTexture heightmapTexture
        {
            get
            {
                ReportUnsupported("heightmapTexture");
                return null;
            }
        }

        public void CopyActiveRenderTextureToHeightmap(RectInt sourceRect, Vector2Int dest, TerrainHeightmapSyncControl syncControl) => ReportUnsupported("CopyActiveRenderTextureToHeightmap");

        public PatchExtents[] GetPatchMinMaxHeights()
        {
            ReportUnsupported("GetPatchMinMaxHeights");
            return new PatchExtents[0];
        }

        public void OverrideMinMaxPatchHeights(PatchExtents[] minMaxHeights) { }

        public float[] GetMaximumHeightError()
        {
            ReportUnsupported("GetMaximumHeightError");
            return new float[0];
        }

        public void OverrideMaximumHeightError(float[] maxError) { }

        // ------------------------------------------------------------ holes

        public int holesResolution => m_Resolution - 1;

        public bool enableHolesTextureCompression
        {
            get => m_EnableHolesTextureCompression;
            set => m_EnableHolesTextureCompression = value;
        }

        internal bool IsHolesTextureCompressed() => m_EnableHolesTextureCompression;

        /// <summary>Whether the cell at (x, y) of the holes map is solid.</summary>
        internal bool Solid(int x, int y)
        {
            int n = m_Resolution - 1;
            if (x < 0 || y < 0 || x >= n || y >= n) return true;
            return m_Solid[y * n + x];
        }

        internal bool HasHoles => Array.IndexOf(m_Solid, false) >= 0;

        public bool IsHole(int x, int y) => !Solid(x, y);

        public bool[,] GetHoles(int xBase, int yBase, int width, int height)
        {
            int n = m_Resolution - 1;
            if (xBase < 0 || yBase < 0 || width < 0 || height < 0 || xBase + width > n || yBase + height > n)
                throw new ArgumentException("Trying to access out-of-bounds terrain holes information.");
            var result = new bool[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    result[y, x] = m_Solid[(yBase + y) * n + xBase + x];
            return result;
        }

        public void SetHoles(int xBase, int yBase, bool[,] holes)
        {
            if (holes == null) throw new NullReferenceException();
            int n = m_Resolution - 1, height = holes.GetLength(0), width = holes.GetLength(1);
            if (xBase < 0 || yBase < 0 || xBase + width > n || yBase + height > n)
                throw new ArgumentException("Trying to access out-of-bounds terrain holes information.");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    m_Solid[(yBase + y) * n + xBase + x] = holes[y, x];
            HeightsChanged();
        }

        public void SetHolesDelayLOD(int xBase, int yBase, bool[,] holes) => SetHoles(xBase, yBase, holes);

        public Texture holesTexture
        {
            get
            {
                ReportUnsupported("holesTexture");
                return null;
            }
        }

        // ------------------------------------------------------------ splat maps

        public static string AlphamapTextureName => "alphamap";
        public static string HolesTextureName => "holes";

        public int alphamapLayers => m_Layers.Length;

        public int alphamapResolution
        {
            get => m_AlphamapResolution;
            set
            {
                m_AlphamapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), k_MinimumAlphamapResolution, k_MaximumAlphamapResolution);
                ResetAlphamaps();
            }
        }

        public int alphamapWidth => m_AlphamapResolution;
        public int alphamapHeight => m_AlphamapResolution;

        public int baseMapResolution
        {
            get => m_BaseMapResolution;
            set => m_BaseMapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), k_MinimumBaseMapResolution, k_MaximumBaseMapResolution);
        }

        public int alphamapTextureCount => m_AlphaTextures.Length;

        public Texture2D[] alphamapTextures => (Texture2D[])m_AlphaTextures.Clone();

        internal Texture2D[] AlphaTextures => m_AlphaTextures;

        internal int SplatVersion => m_SplatVersion;

        public Texture2D GetAlphamapTexture(int index)
        {
            if (index < 0 || index >= m_AlphaTextures.Length) throw new ArgumentException("Invalid alphamap texture index.");
            return m_AlphaTextures[index];
        }

        internal float GetAlphamapResolutionInternal() => m_AlphamapResolution;

        public TerrainLayer[] terrainLayers
        {
            get => (TerrainLayer[])m_Layers.Clone();
            set
            {
                var layers = value ?? new TerrainLayer[0];
                bool wasEmpty = m_Layers.Length == 0;
                m_Layers = (TerrainLayer[])layers.Clone();
                EnsureAlphaTextures();
                // The first layer of a terrain without any covers it all.
                if (wasEmpty && m_Layers.Length > 0)
                {
                    var t = m_AlphaTextures[0];
                    var pixels = new Color32[m_AlphamapResolution * m_AlphamapResolution];
                    Array.Fill(pixels, new Color32(255, 0, 0, 0));
                    t.SetPixels32(pixels);
                    t.Apply();
                }
                m_SplatVersion++;
            }
        }

        public void SetTerrainLayersRegisterUndo(TerrainLayer[] terrainLayers, string undoName) => this.terrainLayers = terrainLayers;

        [Obsolete("TerrainData.splatPrototypes is obsolete. Use TerrainData.terrainLayers instead.")]
        public SplatPrototype[] splatPrototypes
        {
            get
            {
                ReportUnsupported("splatPrototypes");
                return new SplatPrototype[0];
            }
            set => ReportUnsupported("splatPrototypes");
        }

        void EnsureAlphaTextures()
        {
            int needed = (m_Layers.Length + 3) / 4;
            if (m_AlphaTextures.Length >= needed) return;
            int old = m_AlphaTextures.Length;
            Array.Resize(ref m_AlphaTextures, needed);
            for (int i = old; i < needed; i++) m_AlphaTextures[i] = NewAlphaTexture(i);
        }

        Texture2D NewAlphaTexture(int index)
        {
            var t = new Texture2D(m_AlphamapResolution, m_AlphamapResolution, TextureFormat.RGBA32, true, true)
            {
                name = "SplatAlpha " + index,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            t.SetPixels32(new Color32[m_AlphamapResolution * m_AlphamapResolution]);
            t.Apply();
            return t;
        }

        void ResetAlphamaps()
        {
            m_AlphaTextures = new Texture2D[0];
            var layers = m_Layers;
            m_Layers = new TerrainLayer[0];
            terrainLayers = layers;
        }

        public float[,,] GetAlphamaps(int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || width < 0 || height < 0 || x + width > m_AlphamapResolution || y + height > m_AlphamapResolution)
                throw new ArgumentException("Invalid argument for GetAlphaMaps");
            int layers = m_Layers.Length;
            var result = new float[height, width, layers];
            for (int t = 0; t * 4 < layers && t < m_AlphaTextures.Length; t++)
            {
                var texture = m_AlphaTextures[t];
                if (texture == null) continue;
                var pixels = texture.GetPixels32();
                int w = texture.width;
                for (int j = 0; j < height; j++)
                    for (int i = 0; i < width; i++)
                    {
                        var c = pixels[(y + j) * w + x + i];
                        for (int k = 0; k < 4 && t * 4 + k < layers; k++)
                            result[j, i, t * 4 + k] = (k switch { 0 => c.r, 1 => c.g, 2 => c.b, _ => c.a }) * (1f / 255f);
                    }
            }
            return result;
        }

        public void SetAlphamaps(int x, int y, float[,,] map)
        {
            if (map == null) throw new NullReferenceException();
            int height = map.GetLength(0), width = map.GetLength(1), layers = map.GetLength(2);
            if (layers != m_Layers.Length) throw new ArgumentException("Float array size wrong (layers should be " + m_Layers.Length + ")");
            if (x < 0 || y < 0 || x + width > m_AlphamapResolution || y + height > m_AlphamapResolution)
                throw new ArgumentException("Invalid argument for SetAlphamaps");
            EnsureAlphaTextures();
            for (int t = 0; t < m_AlphaTextures.Length; t++)
            {
                var texture = m_AlphaTextures[t];
                var pixels = texture.GetPixels32();
                int w = texture.width;
                for (int j = 0; j < height; j++)
                    for (int i = 0; i < width; i++)
                    {
                        ref var c = ref pixels[(y + j) * w + x + i];
                        for (int k = 0; k < 4 && t * 4 + k < layers; k++)
                        {
                            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(map[j, i, t * 4 + k]) * 255f);
                            switch (k)
                            {
                                case 0: c.r = b; break;
                                case 1: c.g = b; break;
                                case 2: c.b = b; break;
                                default: c.a = b; break;
                            }
                        }
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
            }
            m_SplatVersion++;
        }

        public void SetBaseMapDirty() => m_SplatVersion++;

        public void SyncTexture(string textureName) => m_SplatVersion++;

        public void DirtyTextureRegion(string textureName, RectInt region, bool allowDelayedCPUSync) => m_SplatVersion++;

        public void CopyActiveRenderTextureToTexture(string textureName, int textureIndex, RectInt sourceRect, Vector2Int dest, bool allowDelayedCPUSync) =>
            ReportUnsupported("CopyActiveRenderTextureToTexture");

        // ------------------------------------------------------------ trees and details

        public TreeInstance[] treeInstances
        {
            get => (TreeInstance[])m_TreeInstances.Clone();
            set
            {
                m_TreeInstances = value != null ? (TreeInstance[])value.Clone() : new TreeInstance[0];
                TreesChanged();
            }
        }

        public int treeInstanceCount => m_TreeInstances.Length;

        public TreeInstance GetTreeInstance(int index)
        {
            if (index < 0 || index >= m_TreeInstances.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return m_TreeInstances[index];
        }

        public void SetTreeInstance(int index, TreeInstance instance)
        {
            if (index < 0 || index >= m_TreeInstances.Length) throw new ArgumentOutOfRangeException(nameof(index));
            m_TreeInstances[index] = instance;
            TreesChanged();
        }

        public void SetTreeInstances(TreeInstance[] instances, bool snapToHeightmap)
        {
            var copy = instances != null ? (TreeInstance[])instances.Clone() : new TreeInstance[0];
            if (snapToHeightmap)
                for (int i = 0; i < copy.Length; i++)
                    copy[i].position.y = GetInterpolatedHeight(copy[i].position.x, copy[i].position.z) / Mathf.Max(m_Scale.y, 1e-6f);
            m_TreeInstances = copy;
            TreesChanged();
        }

        internal void AddTree(ref TreeInstance tree)
        {
            Array.Resize(ref m_TreeInstances, m_TreeInstances.Length + 1);
            m_TreeInstances[^1] = tree;
            TreesChanged();
        }

        internal int RemoveTrees(Vector2 position, float radius, int prototypeIndex)
        {
            var kept = new List<TreeInstance>();
            int removed = 0;
            foreach (var t in m_TreeInstances)
            {
                var d = new Vector2(t.position.x - position.x, t.position.z - position.y);
                if (d.sqrMagnitude <= radius * radius && (prototypeIndex < 0 || t.prototypeIndex == prototypeIndex)) removed++;
                else kept.Add(t);
            }
            m_TreeInstances = kept.ToArray();
            TreesChanged();
            return removed;
        }

        public TreePrototype[] treePrototypes
        {
            get => (TreePrototype[])m_TreePrototypes.Clone();
            set
            {
                m_TreePrototypes = value != null ? (TreePrototype[])value.Clone() : new TreePrototype[0];
                TreesChanged();
            }
        }

        internal void RemoveTreePrototype(int index)
        {
            var list = new List<TreePrototype>(m_TreePrototypes);
            list.RemoveAt(index);
            m_TreePrototypes = list.ToArray();
            TreesChanged();
        }

        public void RefreshPrototypes() => TreesChanged();

        public DetailPrototype[] detailPrototypes
        {
            get => (DetailPrototype[])m_DetailPrototypes.Clone();
            set
            {
                m_DetailPrototypes = value != null ? (DetailPrototype[])value.Clone() : new DetailPrototype[0];
                if (m_DetailLayers.Length > m_DetailPrototypes.Length) Array.Resize(ref m_DetailLayers, m_DetailPrototypes.Length);
                m_DetailsVersion++;
            }
        }

        public void RemoveDetailPrototype(int index)
        {
            var list = new List<DetailPrototype>(m_DetailPrototypes);
            list.RemoveAt(index);
            m_DetailPrototypes = list.ToArray();
            if (index < m_DetailLayers.Length)
            {
                var layers = new List<byte[]>(m_DetailLayers);
                layers.RemoveAt(index);
                m_DetailLayers = layers.ToArray();
            }
            m_DetailsVersion++;
        }

        public int detailWidth => m_DetailResolution;
        public int detailHeight => m_DetailResolution;
        public int detailResolution => m_DetailResolution;
        public int detailResolutionPerPatch => m_DetailResolutionPerPatch;
        public int detailPatchCount => m_DetailResolutionPerPatch > 0 ? (m_DetailResolution + m_DetailResolutionPerPatch - 1) / m_DetailResolutionPerPatch : 0;

        public void SetDetailResolution(int detailResolution, int resolutionPerPatch)
        {
            m_DetailResolution = Mathf.Clamp(detailResolution, 0, 4048);
            m_DetailResolutionPerPatch = Mathf.Clamp(resolutionPerPatch, k_MinimumDetailResolutionPerPatch, k_MaximumDetailResolutionPerPatch);
            // A new resolution starts the layers empty.
            m_DetailLayers = new byte[m_DetailPrototypes.Length][];
            m_DetailsVersion++;
        }

        public float wavingGrassStrength { get => m_WavingGrassStrength; set => m_WavingGrassStrength = value; }
        public float wavingGrassAmount { get => m_WavingGrassAmount; set => m_WavingGrassAmount = value; }
        public float wavingGrassSpeed { get => m_WavingGrassSpeed; set => m_WavingGrassSpeed = value; }
        public Color wavingGrassTint { get => m_WavingGrassTint; set => m_WavingGrassTint = value; }

        internal Terrain[] users
        {
            get
            {
                var list = new List<Terrain>();
                foreach (var u in m_Users) if (u is Terrain t) list.Add(t);
                return list.ToArray();
            }
        }

        /// <summary>A serialized ColorRGBA: packed (`rgba`, red in the low byte) or by channel.</summary>
        static Color32 ColorRGBA(YNode node)
        {
            if (node == null) return new Color32(255, 255, 255, 255);
            if (node.Get("rgba") is YNode packed)
            {
                uint v = (uint)packed.AsLong();
                return new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
            }
            return (Color32)new Color(node.Float("r", 1f), node.Float("g", 1f), node.Float("b", 1f), node.Float("a", 1f));
        }

        static readonly HashSet<string> s_Reported = new HashSet<string>();

        static void ReportUnsupported(string what)
        {
            if (s_Reported.Add(what))
                Debug.LogWarning($"Unbound: TerrainData.{what} is not supported yet.");
        }

        // ------------------------------------------------------------ the asset

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var heightmap = body.Get("m_Heightmap");
            if (heightmap != null)
            {
                m_Resolution = Math.Max(2, heightmap.Int("m_Resolution", 33));
                var scale = heightmap.Get("m_Scale");
                if (scale != null) m_Scale = heightmap.Vector3("m_Scale");
                m_Heights = new ushort[m_Resolution * m_Resolution];
                int i = 0;
                foreach (var h in heightmap.Get("m_Heights")?.Seq() ?? Array.Empty<YNode>())
                {
                    if (i >= m_Heights.Length) break;
                    m_Heights[i++] = (ushort)Math.Clamp(h.AsInt(), 0, MaxHeight);
                }
                int n = m_Resolution - 1;
                m_Solid = Filled(n * n);
                var holes = heightmap.Str("m_Holes");
                if (holes.Length >= 2)
                {
                    var bytes = Convert.FromHexString(holes.Length % 2 == 0 ? holes : holes.Substring(0, holes.Length - 1));
                    for (int k = 0; k < bytes.Length && k < m_Solid.Length; k++) m_Solid[k] = bytes[k] != 0;
                }
                m_EnableHolesTextureCompression = heightmap.Bool("m_EnableHolesTextureCompression", true);
            }
            var splat = body.Get("m_SplatDatabase");
            if (splat != null)
            {
                var layers = new List<TerrainLayer>();
                foreach (var r in splat.Get("m_TerrainLayers")?.Seq() ?? Array.Empty<YNode>())
                {
                    var reference = r.AsRef();
                    layers.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(TerrainLayer)) as TerrainLayer);
                }
                m_Layers = layers.ToArray();
                var textures = new List<Texture2D>();
                foreach (var r in splat.Get("m_AlphaTextures")?.Seq() ?? Array.Empty<YNode>())
                {
                    var reference = r.AsRef();
                    textures.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(Texture2D)) as Texture2D);
                }
                m_AlphaTextures = textures.ToArray();
                m_AlphamapResolution = splat.Int("m_AlphamapResolution", 512);
                m_BaseMapResolution = splat.Int("m_BaseMapResolution", 1024);
            }
            var detail = body.Get("m_DetailDatabase");
            if (detail != null)
            {
                var trees = new List<TreeInstance>();
                foreach (var t in detail.Get("m_TreeInstances")?.Seq() ?? Array.Empty<YNode>())
                {
                    trees.Add(new TreeInstance
                    {
                        position = t.Vector3("position"),
                        widthScale = t.Float("widthScale", 1f),
                        heightScale = t.Float("heightScale", 1f),
                        rotation = t.Float("rotation"),
                        color = ColorRGBA(t.Get("color")),
                        lightmapColor = ColorRGBA(t.Get("lightmapColor")),
                        prototypeIndex = t.Int("index"),
                    });
                }
                m_TreeInstances = trees.ToArray();
                var prototypes = new List<TreePrototype>();
                foreach (var p in detail.Get("m_TreePrototypes")?.Seq() ?? Array.Empty<YNode>())
                {
                    var prefab = p.Ref("prefab");
                    prototypes.Add(new TreePrototype
                    {
                        prefab = prefab.IsNull ? null : resolver.Resolve(prefab, typeof(GameObject)) as GameObject,
                        bendFactor = p.Float("bendFactor"),
                        navMeshLod = p.Int("navMeshLod"),
                    });
                }
                m_TreePrototypes = prototypes.ToArray();
                m_WavingGrassStrength = detail.Float("m_WavingGrassStrength", 0.5f);
                m_WavingGrassAmount = detail.Float("m_WavingGrassAmount", 0.5f);
                m_WavingGrassSpeed = detail.Float("m_WavingGrassSpeed", 0.5f);
                m_WavingGrassTint = detail.Color("WavingGrassTint", m_WavingGrassTint);
                ReadDetails(detail, resolver);
            }
            m_HeightsVersion++;
            m_SplatVersion++;
            m_TreesVersion++;
        }
    }

    /// <summary>A tree a terrain places: its prefab and how it bends in the wind.</summary>
    public sealed partial class TreePrototype
    {
        internal GameObject m_Prefab;
        internal float m_BendFactor;
        internal int m_NavMeshLod;

        public TreePrototype() { }

        public TreePrototype(TreePrototype other)
        {
            m_Prefab = other.m_Prefab;
            m_BendFactor = other.m_BendFactor;
            m_NavMeshLod = other.m_NavMeshLod;
        }

        public GameObject prefab { get => m_Prefab; set => m_Prefab = value; }
        public float bendFactor { get => m_BendFactor; set => m_BendFactor = value; }
        public int navMeshLod { get => m_NavMeshLod; set => m_NavMeshLod = value; }

        public override bool Equals(object obj) =>
            obj is TreePrototype o && o.m_Prefab == m_Prefab && o.m_BendFactor == m_BendFactor && o.m_NavMeshLod == m_NavMeshLod;

        public override int GetHashCode() => HashCode.Combine(m_Prefab, m_BendFactor, m_NavMeshLod);

        internal bool Validate(out string errorMessage) => ValidateTreePrototype(this, out errorMessage);

        internal static bool ValidateTreePrototype(TreePrototype prototype, out string errorMessage)
        {
            errorMessage = prototype?.m_Prefab == null ? "Tree prefab is null" : "";
            return prototype?.m_Prefab != null;
        }
    }
}
