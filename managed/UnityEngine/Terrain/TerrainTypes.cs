using System;

namespace UnityEngine
{
    /// <summary>A tree a terrain places: position normalized over the terrain (y over its height).</summary>
    public partial struct TreeInstance
    {
        public Vector3 position;
        public float widthScale;
        public float heightScale;
        public float rotation;
        public Color32 color;
        public Color32 lightmapColor;
        public int prototypeIndex;
        internal float temporaryDistance;
    }

    public enum TreeMotionVectorModeOverride
    {
        CameraMotionOnly = 0,
        PerObjectMotion = 1,
        ForceNoMotion = 2,
        InheritFromPrototype = 3,
    }

    public enum TerrainHeightmapSyncControl
    {
        None = 0,
        HeightOnly = 1,
        HeightAndLod = 2,
    }

    public enum TerrainLayerSmoothnessSource
    {
        Constant = 0,
        DiffuseAlphaChannel = 1,
    }

    public enum DetailRenderMode
    {
        GrassBillboard = 0,
        VertexLit = 1,
        Grass = 2,
    }

    [Flags]
    public enum TerrainQualityOverrides
    {
        None = 0,
        PixelError = 1,
        BasemapDistance = 2,
        DetailDensity = 4,
        DetailDistance = 8,
        TreeDistance = 16,
        BillboardStart = 32,
        FadeLength = 64,
        MaxTrees = 128,
    }

    public enum DetailScatterMode
    {
        CoverageMode = 0,
        InstanceCountMode = 1,
    }

    [Flags]
    public enum TerrainChangedFlags
    {
        Heightmap = 1,
        TreeInstances = 2,
        DelayedHeightmapUpdate = 4,
        FlushEverythingImmediately = 8,
        RemoveDirtyDetailsImmediately = 16,
        HeightmapResolution = 32,
        Holes = 64,
        DelayedHolesUpdate = 128,
        WillBeDestroyed = 256,
    }

    /// <summary>A heightmap patch's lowest and highest heights.</summary>
    public partial struct PatchExtents
    {
        internal float m_min;
        internal float m_max;

        public float min { get => m_min; set => m_min = value; }
        public float max { get => m_max; set => m_max = value; }
    }

    public sealed partial class Terrain
    {
        [global::System.@ObsoleteAttribute("Enum type MaterialType is not used any more.", false)]
        public enum MaterialType
        {
            BuiltInStandard = 0,
            BuiltInLegacyDiffuse = 1,
            BuiltInLegacySpecular = 2,
            Custom = 3,
        }
    }

    /// <summary>A detail instance: its position on the terrain, sizes and turn about y (radians).</summary>
    public partial struct DetailInstanceTransform
    {
        public float posX;
        public float posY;
        public float posZ;
        public float scaleXZ;
        public float scaleY;
        public float rotationY;
    }

    /// <summary>A kind of detail object (grass or a mesh) a terrain scatters.</summary>
    public sealed partial class DetailPrototype
    {
        internal static readonly Color DefaultHealthColor = new Color(0.2627451f, 0.9764706f, 42f / 255f, 1f);
        internal static readonly Color DefaultDryColor = new Color(0.8039216f, 0.7372549f, 26f / 255f, 1f);
        internal GameObject m_Prototype;
        internal Texture2D m_PrototypeTexture;
        internal Color m_HealthyColor = DefaultHealthColor;
        internal Color m_DryColor = DefaultDryColor;
        internal float m_MinWidth = 1f, m_MaxWidth = 2f, m_MinHeight = 1f, m_MaxHeight = 2f;
        internal int m_NoiseSeed;
        internal float m_NoiseSpread = 0.1f;
        internal float m_Density = 1f;
        internal float m_HoleEdgePadding;
        internal int m_RenderMode = (int)DetailRenderMode.Grass;
        internal int m_UsePrototypeMesh;
        internal int m_UseInstancing;
        internal int m_UseDensityScaling;
        internal float m_AlignToGround;
        internal float m_PositionJitter;
        internal float m_TargetCoverage = 1f;

        public DetailPrototype() { }

        public DetailPrototype(DetailPrototype other) => CopyFrom(other);

        void CopyFrom(DetailPrototype o)
        {
            m_Prototype = o.m_Prototype;
            m_PrototypeTexture = o.m_PrototypeTexture;
            m_HealthyColor = o.m_HealthyColor;
            m_DryColor = o.m_DryColor;
            m_MinWidth = o.m_MinWidth;
            m_MaxWidth = o.m_MaxWidth;
            m_MinHeight = o.m_MinHeight;
            m_MaxHeight = o.m_MaxHeight;
            m_NoiseSeed = o.m_NoiseSeed;
            m_NoiseSpread = o.m_NoiseSpread;
            m_Density = o.m_Density;
            m_HoleEdgePadding = o.m_HoleEdgePadding;
            m_RenderMode = o.m_RenderMode;
            m_UsePrototypeMesh = o.m_UsePrototypeMesh;
            m_UseInstancing = o.m_UseInstancing;
            m_UseDensityScaling = o.m_UseDensityScaling;
            m_AlignToGround = o.m_AlignToGround;
            m_PositionJitter = o.m_PositionJitter;
            m_TargetCoverage = o.m_TargetCoverage;
        }

        public GameObject prototype { get => m_Prototype; set => m_Prototype = value; }
        public Texture2D prototypeTexture { get => m_PrototypeTexture; set => m_PrototypeTexture = value; }
        public float minWidth { get => m_MinWidth; set => m_MinWidth = value; }
        public float maxWidth { get => m_MaxWidth; set => m_MaxWidth = value; }
        public float minHeight { get => m_MinHeight; set => m_MinHeight = value; }
        public float maxHeight { get => m_MaxHeight; set => m_MaxHeight = value; }
        public int noiseSeed { get => m_NoiseSeed; set => m_NoiseSeed = value; }
        public float noiseSpread { get => m_NoiseSpread; set => m_NoiseSpread = value; }
        public float density { get => m_Density; set => m_Density = value; }

        [global::System.@ObsoleteAttribute("bendFactor has no effect and is deprecated.", false)] [Unbound.EditorFaithful("obsolete: Unity says it has no effect")]
        public float bendFactor { get => 0f; set { } }

        public float holeEdgePadding { get => m_HoleEdgePadding; set => m_HoleEdgePadding = value; }
        public Color healthyColor { get => m_HealthyColor; set => m_HealthyColor = value; }
        public Color dryColor { get => m_DryColor; set => m_DryColor = value; }
        public DetailRenderMode renderMode { get => (DetailRenderMode)m_RenderMode; set => m_RenderMode = (int)value; }
        public bool usePrototypeMesh { get => m_UsePrototypeMesh != 0; set => m_UsePrototypeMesh = value ? 1 : 0; }
        public bool useInstancing { get => m_UseInstancing != 0; set => m_UseInstancing = value ? 1 : 0; }
        public float targetCoverage { get => m_TargetCoverage; set => m_TargetCoverage = value; }
        public bool useDensityScaling { get => m_UseDensityScaling != 0; set => m_UseDensityScaling = value ? 1 : 0; }
        public float alignToGround { get => m_AlignToGround; set => m_AlignToGround = value; }
        public float positionJitter { get => m_PositionJitter; set => m_PositionJitter = value; }

        public override bool Equals(object obj) =>
            obj is DetailPrototype o && o.m_Prototype == m_Prototype && o.m_PrototypeTexture == m_PrototypeTexture
            && o.m_HealthyColor == m_HealthyColor && o.m_DryColor == m_DryColor && o.m_MinWidth == m_MinWidth
            && o.m_MaxWidth == m_MaxWidth && o.m_MinHeight == m_MinHeight && o.m_MaxHeight == m_MaxHeight
            && o.m_NoiseSeed == m_NoiseSeed && o.m_NoiseSpread == m_NoiseSpread && o.m_Density == m_Density
            && o.m_HoleEdgePadding == m_HoleEdgePadding && o.m_RenderMode == m_RenderMode
            && o.m_UsePrototypeMesh == m_UsePrototypeMesh && o.m_UseInstancing == m_UseInstancing
            && o.m_UseDensityScaling == m_UseDensityScaling && o.m_AlignToGround == m_AlignToGround
            && o.m_PositionJitter == m_PositionJitter && o.m_TargetCoverage == m_TargetCoverage;

        public override int GetHashCode() => HashCode.Combine(m_Prototype, m_PrototypeTexture, m_RenderMode, m_MinWidth, m_MaxHeight);

        public bool Validate() => Validate(out _);

        public bool Validate(out string errorMessage) => ValidateDetailPrototype(this, out errorMessage);

        /// <summary>The Editor's checks of a prototype, in its order and with its messages
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/grass); a prototype that fails
        /// gets no instances and no coverage.</summary>
        internal static bool ValidateDetailPrototype(DetailPrototype prototype, out string errorMessage)
        {
            errorMessage = DetailPrototypeError(prototype) ?? "";
            return errorMessage.Length == 0;
        }

        static string DetailPrototypeError(DetailPrototype p)
        {
            var mode = (DetailRenderMode)p.m_RenderMode;
            if (!p.usePrototypeMesh)
            {
                if (p.m_PrototypeTexture == null) return "Prototype is missing texture.";
                if (mode == DetailRenderMode.VertexLit) return "Render mode cannot be VertexLit.";
                if (p.useInstancing) return "Detail instancing is only supported on mesh details.";
                return null;
            }
            var prefab = p.m_Prototype;
            if (prefab == null) return "Prototype is missing Game Object.";
            if (mode == DetailRenderMode.GrassBillboard) return "Render mode cannot be GrassBillboard.";
            if (mode != DetailRenderMode.VertexLit && p.useInstancing) return "Render mode must be VertexLit.";
            if (prefab.GetComponent<LODGroup>() != null) return "LOD Group component is not supported.";
            var renderer = prefab.GetComponent<Renderer>();
            if (renderer == null) return "Renderer component is missing.";
            var materials = renderer.sharedMaterials;
            if (materials.Length != 1) return "A detail prototype must have exactly one material.";
            if (materials[0] == null) return "Material is missing.";
            var filter = prefab.GetComponent<MeshFilter>();
            if (filter == null) return "Mesh Filter component is missing.";
            var mesh = filter.sharedMesh;
            if (mesh == null) return "Mesh is missing.";
            if (mesh.vertexCount == 0) return "No vertex is available.";
            if (p.useInstancing) return null;
            if (!mesh.HasVertexAttribute(Rendering.VertexAttribute.TexCoord0)) return "No texcoord is available.";
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetSubMesh(i).indexCount >= 3) return null;
            return "No triangle is available.";
        }

        internal static bool IsModeSupportedByRenderPipeline(DetailRenderMode renderMode, bool useInstancing, out string errorMessage)
        {
            errorMessage = "";
            return true;
        }
    }

    [global::System.@ObsoleteAttribute("SplatPrototype is obsolete. Use TerrainLayer instead.", false)] public sealed partial class SplatPrototype
    {
        internal Texture2D m_Texture;
        internal Texture2D m_NormalMap;
        internal Vector2 m_TileSize = new Vector2(15f, 15f);
        internal Vector2 m_TileOffset;
        internal Vector4 m_SpecularMetallic;
        internal float m_Smoothness;

        public SplatPrototype() { }

        public Texture2D texture { get => m_Texture; set => m_Texture = value; }
        public Texture2D normalMap { get => m_NormalMap; set => m_NormalMap = value; }
        public Vector2 tileSize { get => m_TileSize; set => m_TileSize = value; }
        public Vector2 tileOffset { get => m_TileOffset; set => m_TileOffset = value; }
        public Color specular { get => new Color(m_SpecularMetallic.x, m_SpecularMetallic.y, m_SpecularMetallic.z); set => m_SpecularMetallic = new Vector4(value.r, value.g, value.b, m_SpecularMetallic.w); }
        public float metallic { get => m_SpecularMetallic.w; set => m_SpecularMetallic.w = value; }
        public float smoothness { get => m_Smoothness; set => m_Smoothness = value; }
    }
}
