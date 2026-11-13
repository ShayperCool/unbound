using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unbound.Box3D;

namespace UnityEngine
{
    /// <summary>
    /// A terrain: its <see cref="TerrainData"/> placed at the transform's position (terrains ignore
    /// rotation and scale), drawn with its material template and the data's splat maps.
    /// </summary>
    [ClassId(218)]
    public sealed partial class Terrain : Behaviour
    {
        static readonly List<Terrain> s_Active = new List<Terrain>();

        TerrainData m_TerrainData;
        float m_TreeDistance = 5000f, m_TreeBillboardDistance = 50f, m_TreeCrossFadeLength = 5f;
        int m_TreeMaximumFullLODCount = 50;
        float m_DetailObjectDistance = 80f, m_DetailObjectDensity = 1f;
        float m_HeightmapPixelError = 5f, m_SplatMapDistance = 1000f;
        int m_HeightmapMinimumLODSimplification, m_HeightmapMaximumLOD;
        ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.TwoSided;
        bool m_DrawHeightmap = true, m_DrawInstanced, m_DrawTreesAndFoliage = true, m_IgnoreQualitySettings, m_AllowAutoConnect = !Compat.DefaultConstructorStateMeasured;
        bool m_BakeLightProbesForTrees = true, m_DeringLightProbesForTrees = true, m_PreserveTreePrototypeLayers;
        bool m_EnableHeightmapRayTracing = true, m_KeepUnusedRenderingResources, m_CollectDetailPatches = true;
        ReflectionProbeUsage m_ReflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        Material m_MaterialTemplate;
        int m_GroupingID;
        uint m_RenderingLayerMask = 1;
        int m_LightmapIndex = -1, m_RealtimeLightmapIndex = -1;
        Vector4 m_LightmapScaleOffset = new Vector4(1, 1, 0, 0), m_RealtimeLightmapScaleOffset = new Vector4(1, 1, 0, 0);
        TreeMotionVectorModeOverride m_TreeMotionVectorModeOverride = TreeMotionVectorModeOverride.InheritFromPrototype;
        Vector3 m_PatchBoundsMultiplier = Vector3.one;
        float m_TreeLODBiasMultiplier = 1f;
        Terrain m_Left, m_Top, m_Right, m_Bottom;
        MaterialPropertyBlock m_SplatProperties;

        public Terrain() { }

        internal static List<Terrain> ActiveList => s_Active;

        public static Terrain activeTerrain => s_Active.Count > 0 ? s_Active[0] : null;

        public static Terrain[] activeTerrains => s_Active.ToArray();

        public static void GetActiveTerrains(List<Terrain> terrainList)
        {
            if (terrainList == null) throw new ArgumentNullException(nameof(terrainList));
            terrainList.Clear();
            terrainList.AddRange(s_Active);
        }

        TerrainRenderer m_Renderer;
        /// <summary>The renderer drawing the terrain (null until it is first enabled).</summary>
        internal TerrainRenderer Renderer => m_Renderer;

        internal override void OnEngineEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            s_TilesChanged = true;
            m_Renderer ??= TerrainRenderer.For(this);
            m_Renderer.renderingLayerMask = m_RenderingLayerMask;
            m_Renderer.OnEngineEnable();
        }

        internal override void OnEngineDisable()
        {
            s_Active.Remove(this);
            s_TilesChanged = true;
            m_Renderer?.OnEngineDisable();
        }

        internal override void OnEngineDestroy()
        {
            s_Active.Remove(this);
            s_TilesChanged = true;
            m_TerrainData?.m_Users.Remove(this);
            if (m_Renderer != null)
            {
                m_Renderer.OnEngineDisable();
                m_Renderer.OnEngineDestroy();
                m_Renderer = null;
            }
        }

        public TerrainData terrainData
        {
            get => m_TerrainData;
            set
            {
                if (m_TerrainData == value) return;
                m_TerrainData?.m_Users.Remove(this);
                m_TerrainData = value;
                if (value != null && !value.m_Users.Contains(this)) value.m_Users.Add(this);
            }
        }

        // These read the quality level's value when it overrides the setting and the terrain does
        // not ignore quality settings (matches the Unity 6000.0.71f1 Editor, tests/projects/grass).
        public float treeDistance { get => Overridden(TerrainQualityOverrides.TreeDistance) ? QualitySettings.terrainTreeDistance : m_TreeDistance; set => m_TreeDistance = value; }
        public float treeBillboardDistance { get => Overridden(TerrainQualityOverrides.BillboardStart) ? QualitySettings.terrainBillboardStart : m_TreeBillboardDistance; set => m_TreeBillboardDistance = value; }
        public float treeCrossFadeLength { get => Overridden(TerrainQualityOverrides.FadeLength) ? QualitySettings.terrainFadeLength : m_TreeCrossFadeLength; set => m_TreeCrossFadeLength = value; }
        public int treeMaximumFullLODCount { get => Overridden(TerrainQualityOverrides.MaxTrees) ? (int)QualitySettings.terrainMaxTrees : m_TreeMaximumFullLODCount; set => m_TreeMaximumFullLODCount = value; }
        public float detailObjectDistance { get => Overridden(TerrainQualityOverrides.DetailDistance) ? QualitySettings.terrainDetailDistance : m_DetailObjectDistance; set => m_DetailObjectDistance = value; }
        public float detailObjectDensity { get => Overridden(TerrainQualityOverrides.DetailDensity) ? QualitySettings.terrainDetailDensityScale : m_DetailObjectDensity; set => m_DetailObjectDensity = Mathf.Clamp01(value); }
        public float heightmapPixelError { get => Overridden(TerrainQualityOverrides.PixelError) ? QualitySettings.terrainPixelError : m_HeightmapPixelError; set => m_HeightmapPixelError = Mathf.Clamp(value, 1f, 200f); }

        bool Overridden(TerrainQualityOverrides setting) => !m_IgnoreQualitySettings && (QualitySettings.terrainQualityOverrides & setting) != 0;
        // Each setter keeps the value at zero or more and at most the heightmap's levels (11 without
        // terrain data) less the other setting, as the Unity 6000.0.71f1 Editor does; measured:
        // the simplification kept within the levels less the maximum LOD (tests/projects/terrainlod
        // maxlod3minsimpl2). A value set before the terrain data shrinks is not clamped again.
        public int heightmapMaximumLOD { get => m_HeightmapMaximumLOD; set => m_HeightmapMaximumLOD = value < 0 ? 0 : Math.Min(value, PatchLevels() - m_HeightmapMinimumLODSimplification); }
        public int heightmapMinimumLODSimplification { get => m_HeightmapMinimumLODSimplification; set => m_HeightmapMinimumLODSimplification = value < 0 ? 0 : Math.Min(value, PatchLevels() - m_HeightmapMaximumLOD); }

        int PatchLevels() => m_TerrainData != null ? TerrainData.PatchLevels(m_TerrainData.heightmapResolution) : 11;
        public float basemapDistance { get => Overridden(TerrainQualityOverrides.BasemapDistance) ? QualitySettings.terrainBasemapDistance : m_SplatMapDistance; set => m_SplatMapDistance = Mathf.Clamp(value, 0f, 20000f); }

        [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)] [global::System.@ObsoleteAttribute("splatmapDistance is deprecated, please use basemapDistance instead. (UnityUpgradable) -> basemapDistance", true)]
        public float splatmapDistance { get => basemapDistance; set => basemapDistance = value; }

        public int lightmapIndex { get => m_LightmapIndex; set => m_LightmapIndex = NormalizeLightmapIndex(value); }
        public int realtimeLightmapIndex { get => m_RealtimeLightmapIndex; set => m_RealtimeLightmapIndex = NormalizeLightmapIndex(value); }
        // The native terrain stores 16-bit indices. 0xFFFE is the no-lightmap sentinel
        // exposed as 65534; negative and out-of-range assignments read back as -1.
        // Measured in tests/projects/terrainlightmaps on Unity 6000.0.71f1.
        static int NormalizeLightmapIndex(int index) => index < 0 || index >= 0xFFFF ? -1 : index;

        public Vector4 lightmapScaleOffset { get => m_LightmapScaleOffset; set => m_LightmapScaleOffset = value; }
        public Vector4 realtimeLightmapScaleOffset { get => m_RealtimeLightmapScaleOffset; set => m_RealtimeLightmapScaleOffset = value; }

        [Obsolete("Terrain.freeUnusedRenderingResources is obsolete; use keepUnusedRenderingResources instead.")]
        public bool freeUnusedRenderingResources { get => !m_KeepUnusedRenderingResources; set => m_KeepUnusedRenderingResources = !value; }

        public bool keepUnusedRenderingResources { get => m_KeepUnusedRenderingResources; set => m_KeepUnusedRenderingResources = value; }
        public bool GetKeepUnusedCameraRenderingResources(int cameraInstanceID) => m_KeepUnusedRenderingResources;
        [Unbound.EditorFaithful("no-op is equivalent: only decides when per-camera GPU resources are freed; nothing a game observes")]
        public void SetKeepUnusedCameraRenderingResources(int cameraInstanceID, bool keepUnused) { }

        public ShadowCastingMode shadowCastingMode { get => m_ShadowCastingMode; set => m_ShadowCastingMode = value; }

        [Obsolete("castShadows is deprecated, please use shadowCastingMode instead.")]
        public bool castShadows
        {
            get => m_ShadowCastingMode != ShadowCastingMode.Off;
            set => m_ShadowCastingMode = value ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
        }

        public ReflectionProbeUsage reflectionProbeUsage { get => m_ReflectionProbeUsage; set => m_ReflectionProbeUsage = value; }
        public Material materialTemplate { get => m_MaterialTemplate; set => m_MaterialTemplate = value; }
        public bool drawHeightmap { get => m_DrawHeightmap; set => m_DrawHeightmap = value; }
        public bool allowAutoConnect { get => m_AllowAutoConnect; set => m_AllowAutoConnect = value; }
        public int groupingID { get => m_GroupingID; set => m_GroupingID = value; }
        public bool drawInstanced { get => m_DrawInstanced; set => m_DrawInstanced = value; }
        public bool enableHeightmapRayTracing { get => m_EnableHeightmapRayTracing; set => m_EnableHeightmapRayTracing = value; }
        public RenderTexture normalmapTexture
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Terrain.normalmapTexture", "is not made yet: it answers null");
                return null;
            }
        }
        public bool drawTreesAndFoliage { get => m_DrawTreesAndFoliage; set => m_DrawTreesAndFoliage = value; }
        public Vector3 patchBoundsMultiplier { get => m_PatchBoundsMultiplier; set => m_PatchBoundsMultiplier = value; }
        public float treeLODBiasMultiplier { get => m_TreeLODBiasMultiplier; set => m_TreeLODBiasMultiplier = value; }
        public bool collectDetailPatches { get => m_CollectDetailPatches; set => m_CollectDetailPatches = value; }
        public bool ignoreQualitySettings { get => m_IgnoreQualitySettings; set => m_IgnoreQualitySettings = value; }
        public TerrainRenderFlags editorRenderFlags { get; set; } = TerrainRenderFlags.All;
        public bool bakeLightProbesForTrees { get => m_BakeLightProbesForTrees; set => m_BakeLightProbesForTrees = value; }
        public bool deringLightProbesForTrees { get => m_DeringLightProbesForTrees; set => m_DeringLightProbesForTrees = value; }
        public TreeMotionVectorModeOverride treeMotionVectorModeOverride { get => m_TreeMotionVectorModeOverride; set => m_TreeMotionVectorModeOverride = value; }
        public bool preserveTreePrototypeLayers { get => m_PreserveTreePrototypeLayers; set => m_PreserveTreePrototypeLayers = value; }
        /// <summary>The rendering layers the terrain's renderer is on: renderer lists filter it and
        /// light layers light it by this mask (tests/projects/urp terrain_light_layers).</summary>
        public uint renderingLayerMask
        {
            get => m_RenderingLayerMask;
            set
            {
                m_RenderingLayerMask = value;
                if (m_Renderer != null) m_Renderer.renderingLayerMask = value;
            }
        }

        public static GraphicsFormat heightmapFormat => GraphicsFormat.R16_UNorm;
        public static TextureFormat heightmapTextureFormat => TextureFormat.R16;
        public static RenderTextureFormat heightmapRenderTextureFormat => RenderTextureFormat.R16;
        public static GraphicsFormat normalmapFormat => GraphicsFormat.R8G8B8A8_UNorm;
        public static TextureFormat normalmapTextureFormat => TextureFormat.RGBA32;
        public static RenderTextureFormat normalmapRenderTextureFormat => RenderTextureFormat.ARGB32;
        public static GraphicsFormat holesFormat => GraphicsFormat.R8_UNorm;
        public static RenderTextureFormat holesRenderTextureFormat => RenderTextureFormat.R8;
        public static GraphicsFormat compressedHolesFormat => GraphicsFormat.RGBA_DXT1_UNorm;
        public static TextureFormat compressedHolesTextureFormat => TextureFormat.DXT1;

        public Terrain leftNeighbor { get { ConnectTiles(); return m_Left; } }
        public Terrain rightNeighbor { get { ConnectTiles(); return m_Right; } }
        public Terrain topNeighbor { get { ConnectTiles(); return m_Top; } }
        public Terrain bottomNeighbor { get { ConnectTiles(); return m_Bottom; } }

        /// <summary>
        /// Sets this tile's four links; the other tiles keep theirs (the Scripting API: "it isn't
        /// enough to call this function on one Terrain; you need to set the neighbors of each
        /// Terrain"). A pending auto-connect pass runs first so it cannot overwrite these links
        /// later; the next pass after the connectivity is marked dirty again reassigns all four links
        /// of an auto-connect tile (Unbound's choice, unmeasured).
        /// </summary>
        public void SetNeighbors(Terrain left, Terrain top, Terrain right, Terrain bottom)
        {
            ConnectTiles();
            m_Left = left;
            m_Top = top;
            m_Right = right;
            m_Bottom = bottom;
            s_AutoConnected.Remove(this);
        }

        /// <summary>Marks the connectivity invalid; the tiles are reconnected when a neighbour is
        /// next read or set.</summary>
        public static void SetConnectivityDirty() => s_TilesChanged = true;

        // ------------------------------------------------------------ auto-connect
        //
        // Documented (6000.0 Scripting API and Manual): tiles that allow auto-connect and share a
        // grouping ID connect to their adjacent tiles; both tiles must allow it; neighbours lie
        // left = -X, right = +X, top = +Z, bottom = -Z; SetConnectivityDirty marks the connectivity
        // invalid after tiles are added or removed. The docs give no adjacency tolerance, size
        // rule or rebuild time. Unbound's choices (unmeasured): only active terrains with data
        // take part; a neighbour has the same X/Z size and sits exactly one tile over on one axis
        // and level on the other (heights ignored), within a ten-thousandth of the tile size; a
        // side with several candidates stays empty with a warning, given once per tile and side
        // (the warning would otherwise repeat on every rebuild); the connectivity is also marked
        // dirty when a terrain is enabled, disabled or destroyed; it is rebuilt lazily, on the
        // next neighbour read or SetNeighbors call, not on moves or setting changes. The adjacency
        // is the tile-grid rule of TryGetTileCoord, which TerrainMap.CreateFromPlacement uses too;
        // TerrainUtility.AutoConnect runs this pass.

        static bool s_TilesChanged;
        static readonly List<Terrain> s_AutoConnected = new List<Terrain>();
        static readonly HashSet<(int tile, string side)> s_WarnedOverlap = new HashSet<(int tile, string side)>();
        const float k_TileTolerance = 1e-4f;

        /// <summary>Rebuilds the auto-connect links when the connectivity is dirty.</summary>
        internal static void ConnectTiles()
        {
            if (!s_TilesChanged) return;
            s_TilesChanged = false;
            // Links made by the previous pass go; tiles still taking part get theirs again.
            foreach (var tile in s_AutoConnected)
                tile.m_Left = tile.m_Top = tile.m_Right = tile.m_Bottom = null;
            s_AutoConnected.Clear();
            var tiles = new List<Terrain>();
            foreach (var terrain in s_Active)
                if (terrain.m_AllowAutoConnect && terrain.m_TerrainData != null) tiles.Add(terrain);
            foreach (var tile in tiles)
            {
                tile.m_Left = tile.AdjacentTile(tiles, -1, 0, "left");
                tile.m_Right = tile.AdjacentTile(tiles, 1, 0, "right");
                tile.m_Top = tile.AdjacentTile(tiles, 0, 1, "top");
                tile.m_Bottom = tile.AdjacentTile(tiles, 0, -1, "bottom");
                s_AutoConnected.Add(tile);
            }
        }

        /// <summary>The tile one step of (<paramref name="stepX"/>, <paramref name="stepZ"/>) tiles
        /// away in the same group, with the same X/Z size; null when there is none, or when several
        /// overlapping tiles qualify. The links stay one-sided: each overlapping tile still takes
        /// this tile as its own neighbour on the opposite side.</summary>
        Terrain AdjacentTile(List<Terrain> tiles, int stepX, int stepZ, string side)
        {
            var size = m_TerrainData.size;
            var origin = GetPosition();
            var gridOrigin = new Vector2(origin.x, origin.z);
            var gridSize = new Vector2(size.x, size.z);
            Terrain found = null;
            int count = 0;
            foreach (var other in tiles)
            {
                if (other == this || other.m_GroupingID != m_GroupingID) continue;
                if (!TryGetTileCoord(other, gridOrigin, gridSize, out int tileX, out int tileZ) || tileX != stepX || tileZ != stepZ) continue;
                found = other;
                count++;
            }
            if (count <= 1) return found;
            if (s_WarnedOverlap.Add((GetInstanceID(), side)))
                Debug.LogWarning($"Unbound: terrain '{name}' has {count} overlapping auto-connect tiles on its {side} (grouping ID {m_GroupingID}); that side is left unconnected.", this);
            return null;
        }

        /// <summary>
        /// The tile-grid rule shared by auto-connect and <c>TerrainMap.CreateFromPlacement</c>
        /// (Unbound's choice, unmeasured; the docs only say the grid size is "typically" the
        /// terrain's size.x and size.z): a terrain with data sits on the grid of cells
        /// <paramref name="gridSize"/> (X, Z) anchored at <paramref name="gridOrigin"/> (X, Z) when
        /// its X/Z size equals the cell size and its position is a whole number of cells from the
        /// origin on both axes, each within a ten-thousandth of the cell size. Heights are ignored.
        /// A cell size that is not positive and finite, or a position that is not finite, places
        /// nothing.
        /// </summary>
        internal static bool TryGetTileCoord(Terrain terrain, Vector2 gridOrigin, Vector2 gridSize, out int tileX, out int tileZ)
        {
            tileX = tileZ = 0;
            var data = terrain.m_TerrainData;
            if (data == null) return false;
            if (!(gridSize.x > 0f && gridSize.x < float.PositiveInfinity && gridSize.y > 0f && gridSize.y < float.PositiveInfinity)) return false;
            float toleranceX = gridSize.x * k_TileTolerance, toleranceZ = gridSize.y * k_TileTolerance;
            var size = data.size;
            if (Mathf.Abs(size.x - gridSize.x) > toleranceX || Mathf.Abs(size.z - gridSize.y) > toleranceZ) return false;
            var position = terrain.GetPosition();
            float offsetX = position.x - gridOrigin.x, offsetZ = position.z - gridOrigin.y;
            double cellX = Math.Round((double)offsetX / gridSize.x), cellZ = Math.Round((double)offsetZ / gridSize.y);
            if (!(Math.Abs(cellX) <= int.MaxValue && Math.Abs(cellZ) <= int.MaxValue)) return false;
            tileX = (int)cellX;
            tileZ = (int)cellZ;
            if (Mathf.Abs(offsetX - tileX * gridSize.x) > toleranceX || Mathf.Abs(offsetZ - tileZ * gridSize.y) > toleranceZ)
            {
                tileX = tileZ = 0;
                return false;
            }
            return true;
        }

        public Vector3 GetPosition() => transform.position;

        [Unbound.EditorFaithful("no-op is equivalent: Unbound applies terrain changes when it next draws")]
        public void Flush() { }

        /// <summary>The terrain's height (relative to its position) under a world position.</summary>
        public float SampleHeight(Vector3 worldPosition)
        {
            var data = m_TerrainData;
            if (data == null) return 0f;
            var local = worldPosition - GetPosition();
            var size = data.size;
            return data.GetInterpolatedHeight(local.x / size.x, local.z / size.z);
        }

        public void AddTreeInstance(TreeInstance instance) => m_TerrainData?.AddTree(ref instance);

        internal void RemoveTrees(Vector2 position, float radius, int prototypeIndex) => m_TerrainData?.RemoveTrees(position, radius, prototypeIndex);

        public void SetSplatMaterialPropertyBlock(MaterialPropertyBlock properties) => m_SplatProperties = properties;

        public void GetSplatMaterialPropertyBlock(MaterialPropertyBlock dest)
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            dest.Clear();
            if (m_SplatProperties != null) dest.CopyFrom(m_SplatProperties);
        }

        internal MaterialPropertyBlock SplatProperties => m_SplatProperties;

        public void GetClosestReflectionProbes(List<ReflectionProbeBlendInfo> result) => result?.Clear();

        [global::System.@ObsoleteAttribute("Use TerrainData.SyncHeightmap to notify all Terrain instances using the TerrainData.", false)]
        public void ApplyDelayedHeightmapModification() => m_TerrainData?.SyncHeightmap();

        /// <summary>A GameObject with a Terrain and a TerrainCollider of the data.</summary>
        public static GameObject CreateTerrainGameObject(TerrainData assignTerrain)
        {
            var go = new GameObject("Terrain", typeof(Terrain), typeof(TerrainCollider));
            go.isStatic = true;
            go.GetComponent<Terrain>().terrainData = assignTerrain;
            go.GetComponent<TerrainCollider>().terrainData = assignTerrain;
            return go;
        }

        [global::System.@ObsoleteAttribute("Property materialType is not used any more. Set materialTemplate directly.", false)]
        [Unbound.EditorFaithful("removed material selection returns Custom and ignores writes; materialTemplate selects the material") ]
        public MaterialType materialType { get => MaterialType.Custom; set { } }

        [global::System.@ObsoleteAttribute("Property legacySpecular is not used any more. Set materialTemplate directly.", false)]
        public Color legacySpecular { get; set; } = new Color(0.5f, 0.5f, 0.5f, 1f);

        [global::System.@ObsoleteAttribute("Property legacyShininess is not used any more. Set materialTemplate directly.", false)]
        public float legacyShininess { get; set; } = 0.078125f;

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var data = body.Ref("m_TerrainData");
            terrainData = data.IsNull ? null : resolver.Resolve(data, typeof(TerrainData)) as TerrainData;
            m_TreeDistance = body.Float("m_TreeDistance", 5000f);
            m_TreeBillboardDistance = body.Float("m_TreeBillboardDistance", 50f);
            m_TreeCrossFadeLength = body.Float("m_TreeCrossFadeLength", 5f);
            m_TreeMaximumFullLODCount = body.Int("m_TreeMaximumFullLODCount", 50);
            m_DetailObjectDistance = body.Float("m_DetailObjectDistance", 80f);
            m_DetailObjectDensity = body.Float("m_DetailObjectDensity", 1f);
            m_HeightmapPixelError = body.Float("m_HeightmapPixelError", 5f);
            m_SplatMapDistance = body.Float("m_SplatMapDistance", 1000f);
            m_HeightmapMinimumLODSimplification = body.Int("m_HeightmapMinimumLODSimplification");
            m_HeightmapMaximumLOD = body.Int("m_HeightmapMaximumLOD");
            m_ShadowCastingMode = (ShadowCastingMode)body.Int("m_ShadowCastingMode", 2);
            m_DrawHeightmap = body.Bool("m_DrawHeightmap", true);
            m_DrawInstanced = body.Bool("m_DrawInstanced");
            m_DrawTreesAndFoliage = body.Bool("m_DrawTreesAndFoliage", true);
            m_IgnoreQualitySettings = body.Bool("m_IgnoreQualitySettings");
            m_ReflectionProbeUsage = (ReflectionProbeUsage)body.Int("m_ReflectionProbeUsage", 1);
            var material = body.Ref("m_MaterialTemplate");
            m_MaterialTemplate = material.IsNull ? null : resolver.Resolve(material, typeof(Material)) as Material;
            m_BakeLightProbesForTrees = body.Bool("m_BakeLightProbesForTrees", true);
            m_PreserveTreePrototypeLayers = body.Bool("m_PreserveTreePrototypeLayers");
            m_DeringLightProbesForTrees = body.Bool("m_DeringLightProbesForTrees", true);
            m_GroupingID = body.Int("m_GroupingID");
            m_RenderingLayerMask = (uint)body.Long("m_RenderingLayerMask", 1);
            m_AllowAutoConnect = body.Bool("m_AllowAutoConnect", true);
            m_EnableHeightmapRayTracing = body.Bool("m_EnableHeightmapRayTracing", true);
            m_TreeMotionVectorModeOverride = (TreeMotionVectorModeOverride)body.Int("m_TreeMotionVectorModeOverride", 3);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Terrain)source;
            terrainData = remap(s.m_TerrainData) as TerrainData;
            m_TreeDistance = s.m_TreeDistance;
            m_TreeBillboardDistance = s.m_TreeBillboardDistance;
            m_TreeCrossFadeLength = s.m_TreeCrossFadeLength;
            m_TreeMaximumFullLODCount = s.m_TreeMaximumFullLODCount;
            m_DetailObjectDistance = s.m_DetailObjectDistance;
            m_DetailObjectDensity = s.m_DetailObjectDensity;
            m_HeightmapPixelError = s.m_HeightmapPixelError;
            m_SplatMapDistance = s.m_SplatMapDistance;
            m_HeightmapMinimumLODSimplification = s.m_HeightmapMinimumLODSimplification;
            m_HeightmapMaximumLOD = s.m_HeightmapMaximumLOD;
            m_ShadowCastingMode = s.m_ShadowCastingMode;
            m_DrawHeightmap = s.m_DrawHeightmap;
            m_DrawInstanced = s.m_DrawInstanced;
            m_DrawTreesAndFoliage = s.m_DrawTreesAndFoliage;
            m_IgnoreQualitySettings = s.m_IgnoreQualitySettings;
            m_ReflectionProbeUsage = s.m_ReflectionProbeUsage;
            m_MaterialTemplate = remap(s.m_MaterialTemplate) as Material;
            m_BakeLightProbesForTrees = s.m_BakeLightProbesForTrees;
            m_PreserveTreePrototypeLayers = s.m_PreserveTreePrototypeLayers;
            m_DeringLightProbesForTrees = s.m_DeringLightProbesForTrees;
            m_GroupingID = s.m_GroupingID;
            m_RenderingLayerMask = s.m_RenderingLayerMask;
            m_AllowAutoConnect = s.m_AllowAutoConnect;
            m_EnableHeightmapRayTracing = s.m_EnableHeightmapRayTracing;
            m_TreeMotionVectorModeOverride = s.m_TreeMotionVectorModeOverride;
            // Lightmap indices and STs are not serialized or copied by Unity. The scene's
            // LightingData assigns baked bindings after load; Instantiate starts unbound.

        }
    }

    /// <summary>A collider of a terrain's heightmap (holes included): a Box3D height field whose
    /// cells split along the same diagonal as Unity's (its grid turned a quarter about up, since
    /// Box3D's diagonal runs the other way).</summary>
    [ClassId(154)]
    public partial class TerrainCollider : Collider
    {
        TerrainData m_TerrainData;
        bool m_EnableTreeColliders = true;
        bool m_WarnedRigidbody;

        public TerrainCollider() { }

        public TerrainData terrainData
        {
            get => m_TerrainData;
            set
            {
                if (m_TerrainData == value) return;
                m_TerrainData?.m_Users.Remove(this);
                m_TerrainData = value;
                if (value != null && !value.m_Users.Contains(this)) value.m_Users.Add(this);
                ShapeChanged();
            }
        }

        internal bool enableTreeColliders { get => m_EnableTreeColliders; set => m_EnableTreeColliders = value; }

        /// <summary>A ray against this terrain; with <paramref name="hitHoles"/> the holes are solid
        /// too (not supported yet: they stay open).</summary>
        internal bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, bool hitHoles) => Raycast(ray, out hitInfo, maxDistance);

        internal override unsafe ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody)
        {
            var data = m_TerrainData;
            if (data == null) return 0;
            if (rigidbody != null)
            {
                if (!m_WarnedRigidbody)
                {
                    m_WarnedRigidbody = true;
                    Debug.LogWarning($"Unbound: the TerrainCollider of '{name}' is on a Rigidbody; terrains collide only as static colliders here.", this);
                }
                return 0;
            }
            // PhysX's height field as Unity builds it: rows along x, columns along z, Unity's own
            // 16-bit heights scaled by the terrain's height over the largest sample.
            int resolution = data.heightmapResolution;
            var raw = data.RawHeights;
            var size = data.size;
            var heights = new short[resolution * resolution];
            var holes = new byte[(resolution - 1) * (resolution - 1)];
            for (int x = 0; x < resolution; x++)
                for (int z = 0; z < resolution; z++)
                    heights[x * resolution + z] = unchecked((short)raw[z * resolution + x]);
            for (int x = 0; x < resolution - 1; x++)
                for (int z = 0; z < resolution - 1; z++)
                    holes[x * (resolution - 1) + z] = data.Solid(x, z) ? (byte)0 : (byte)1;
            fixed (short* h = heights)
            fixed (byte* m = holes)
                m_HeightFieldData = B3.ua3_height_field_samples(h, m, resolution, resolution, 1);
            if (m_HeightFieldData == IntPtr.Zero) return 0;
            var shape = B3.ua3_shape_height_field_scaled(body, def, m_HeightFieldData, size.y / TerrainData.MaxHeight,
                data.heightmapScale.x, data.heightmapScale.z, Vector3.zero);
            if (shape != 0 && m_EnableTreeColliders) CreateTreeShapes(body, def, data);
            return shape;
        }

        /// <summary>A capsule per tree whose prototype's root has a CapsuleCollider: scaled by the
        /// prefab's scale and the tree's (its width across, its height along).</summary>
        PhysicsMaterial m_TreeMaterial;
        int m_TreeMaterialVersion;

        // Unity 6000.0.71f1, physicsbatch: trees retain their creation material on assignment,
        // while edits to that material's values still reach their shapes.
        internal void UpdateTreeMaterial()
        {
            int version = m_TreeMaterial != null ? m_TreeMaterial.m_ChangeVersion : 0;
            if (m_TreeMaterialVersion == version) return;
            m_TreeMaterialVersion = version;
            Physics.MaterialValues(m_TreeMaterial, out float friction, out float staticFriction, out float bounce, out ulong modes);
            foreach (var shape in m_ExtraShapes) B3.ua3_shape_set_material(shape, friction, bounce, modes, staticFriction);
        }

        internal void SetTreeMaterial(PhysicsMaterial material)
        {
            m_TreeMaterial = material;
            m_TreeMaterialVersion = -1;
            UpdateTreeMaterial();
        }

        void CreateTreeShapes(ulong body, in B3.ShapeDef def, TerrainData data)
        {
            m_TreeMaterial = Physics.MaterialOf(this);
            m_TreeMaterialVersion = m_TreeMaterial != null ? m_TreeMaterial.m_ChangeVersion : 0;
            var prototypes = data.treePrototypes;
            var origin = transform.position;
            var frame = Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one).inverseDoublePrecision;
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex]?.prefab;
                var capsule = prefab != null ? prefab.GetComponent<CapsuleCollider>() : null;
                if (capsule == null) continue;
                var prefabScale = prefab.transform.localScale;
                var scale = new Vector3(prefabScale.x * tree.widthScale, prefabScale.y * tree.heightScale, prefabScale.z * tree.widthScale);
                var world = origin + Vector3.Scale(tree.position, data.size);
                var rotation = Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up);
                var axis = capsule.m_Direction == 0 ? Vector3.right : capsule.m_Direction == 2 ? Vector3.forward : Vector3.up;
                float along = Mathf.Abs(Vector3.Dot(axis, scale));
                float across = capsule.m_Direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
                    : capsule.m_Direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                float radius = capsule.m_Radius * across;
                float half = Mathf.Max(capsule.m_Height * along * 0.5f - radius, 0f);
                var center = world + rotation * Vector3.Scale(capsule.m_Center, scale);
                var a = frame.MultiplyPoint3x4(center - rotation * axis * half);
                var b = frame.MultiplyPoint3x4(center + rotation * axis * half);
                var id = half > 0f ? B3.ua3_shape_capsule(body, def, a, b, radius) : B3.ua3_shape_sphere(body, def, a, radius);
                if (id != 0) m_ExtraShapes.Add(id);
            }
        }

        internal override Bounds LocalBounds() => m_TerrainData != null ? m_TerrainData.bounds : new Bounds(Vector3.zero, Vector3.zero);

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            m_TerrainData?.m_Users.Remove(this);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            var data = body.Ref("m_TerrainData");
            terrainData = data.IsNull ? null : resolver.Resolve(data, typeof(TerrainData)) as TerrainData;
            m_EnableTreeColliders = body.Bool("m_EnableTreeColliders", true);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TerrainCollider)source;
            CopyColliderData(s);
            terrainData = remap(s.m_TerrainData) as TerrainData;
            m_EnableTreeColliders = s.m_EnableTreeColliders;
        }
    }
}
