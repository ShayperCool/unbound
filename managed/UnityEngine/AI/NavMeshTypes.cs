using System;

namespace UnityEngine.AI
{
    public enum NavMeshPathStatus
    {
        PathComplete = 0,
        PathPartial = 1,
        PathInvalid = 2,
    }

    public enum ObstacleAvoidanceType
    {
        NoObstacleAvoidance = 0,
        LowQualityObstacleAvoidance = 1,
        MedQualityObstacleAvoidance = 2,
        GoodQualityObstacleAvoidance = 3,
        HighQualityObstacleAvoidance = 4,
    }

    public enum NavMeshObstacleShape
    {
        Capsule = 0,
        Box = 1,
    }

    public enum OffMeshLinkType
    {
        LinkTypeManual = 0,
        LinkTypeDropDown = 1,
        LinkTypeJumpAcross = 2,
    }

    public enum NavMeshCollectGeometry
    {
        RenderMeshes = 0,
        PhysicsColliders = 1,
    }

    public enum NavMeshBuildSourceShape
    {
        Mesh = 0,
        Terrain = 1,
        Box = 2,
        Sphere = 3,
        Capsule = 4,
        ModifierBox = 5,
    }

    [Flags]
    public enum NavMeshBuildDebugFlags
    {
        None = 0,
        InputGeometry = 1,
        Voxels = 2,
        Regions = 4,
        RawContours = 8,
        SimplifiedContours = 16,
        PolygonMeshes = 32,
        PolygonMeshesDetail = 64,
        All = 127,
    }

    public partial struct NavMeshBuildDebugSettings
    {
        byte m_Flags;

        public NavMeshBuildDebugFlags flags
        {
            get => (NavMeshBuildDebugFlags)m_Flags;
            set => m_Flags = (byte)value;
        }
    }

    /// <summary>The settings of one agent type, and how its NavMesh is voxelized.</summary>
    public partial struct NavMeshBuildSettings
    {
        int m_AgentTypeID;
        float m_AgentRadius;
        float m_AgentHeight;
        float m_AgentSlope;
        float m_AgentClimb;
        float m_LedgeDropHeight;
        float m_MaxJumpAcrossDistance;
        float m_MinRegionArea;
        int m_OverrideVoxelSize;
        float m_VoxelSize;
        int m_OverrideTileSize;
        int m_TileSize;
        int m_BuildHeightMesh;
        uint m_MaxJobWorkers;
        int m_PreserveTilesOutsideBounds;
        NavMeshBuildDebugSettings m_Debug;

        public int agentTypeID { get => m_AgentTypeID; set => m_AgentTypeID = value; }
        public float agentRadius { get => m_AgentRadius; set => m_AgentRadius = value; }
        public float agentHeight { get => m_AgentHeight; set => m_AgentHeight = value; }
        public float agentSlope { get => m_AgentSlope; set => m_AgentSlope = value; }
        public float agentClimb { get => m_AgentClimb; set => m_AgentClimb = value; }
        public float ledgeDropHeight { get => m_LedgeDropHeight; set => m_LedgeDropHeight = value; }
        public float maxJumpAcrossDistance { get => m_MaxJumpAcrossDistance; set => m_MaxJumpAcrossDistance = value; }
        public float minRegionArea { get => m_MinRegionArea; set => m_MinRegionArea = value; }
        public bool overrideVoxelSize { get => m_OverrideVoxelSize != 0; set => m_OverrideVoxelSize = value ? 1 : 0; }
        public float voxelSize { get => m_VoxelSize; set => m_VoxelSize = value; }
        public bool overrideTileSize { get => m_OverrideTileSize != 0; set => m_OverrideTileSize = value ? 1 : 0; }
        public int tileSize { get => m_TileSize; set => m_TileSize = value; }
        public uint maxJobWorkers { get => m_MaxJobWorkers; set => m_MaxJobWorkers = value; }
        public bool preserveTilesOutsideBounds { get => m_PreserveTilesOutsideBounds != 0; set => m_PreserveTilesOutsideBounds = value ? 1 : 0; }
        public bool buildHeightMesh { get => m_BuildHeightMesh != 0; set => m_BuildHeightMesh = value ? 1 : 0; }
        public NavMeshBuildDebugSettings debug { get => m_Debug; set => m_Debug = value; }

        /// <summary>Unity's defaults for a new agent type (the Humanoid's).</summary>
        internal static NavMeshBuildSettings Default => new NavMeshBuildSettings
        {
            m_AgentRadius = 0.5f,
            m_AgentHeight = 2f,
            m_AgentSlope = 45f,
            m_AgentClimb = 0.75f,
            m_MinRegionArea = 2f,
            m_VoxelSize = 0.16666667f,
            m_TileSize = 256,
        };

        /// <summary>The voxel size the build uses: a third of the radius unless overridden.</summary>
        internal float EffectiveVoxelSize => overrideVoxelSize ? m_VoxelSize : m_AgentRadius / 3f;

        internal int EffectiveTileSize => overrideTileSize ? m_TileSize : 256;

        public string[] ValidationReport(Bounds buildBounds)
        {
            var problems = new System.Collections.Generic.List<string>();
            if (m_AgentRadius <= 0f) problems.Add("Agent radius must be greater than zero.");
            if (m_AgentHeight <= 0f) problems.Add("Agent height must be greater than zero.");
            if (EffectiveVoxelSize <= 0f) problems.Add("Voxel size must be greater than zero.");
            if (EffectiveTileSize < 16) problems.Add("Tile size must be at least 16 voxels.");
            return problems.ToArray();
        }

        internal static NavMeshBuildSettings Read(Unbound.Runtime.YNode n)
        {
            var s = Default;
            if (n == null) return s;
            s.m_AgentTypeID = n.Int("agentTypeID");
            s.m_AgentRadius = n.Float("agentRadius", 0.5f);
            s.m_AgentHeight = n.Float("agentHeight", 2f);
            s.m_AgentSlope = n.Float("agentSlope", 45f);
            s.m_AgentClimb = n.Float("agentClimb", 0.75f);
            s.m_LedgeDropHeight = n.Float("ledgeDropHeight");
            s.m_MaxJumpAcrossDistance = n.Float("maxJumpAcrossDistance");
            s.m_MinRegionArea = n.Float("minRegionArea", 2f);
            s.m_OverrideVoxelSize = n.Int("manualCellSize");
            s.m_VoxelSize = n.Float("cellSize", 0.16666667f);
            s.m_OverrideTileSize = n.Int("manualTileSize");
            s.m_TileSize = n.Int("tileSize", 256);
            s.m_BuildHeightMesh = n.Int("buildHeightMesh");
            s.m_MaxJobWorkers = (uint)n.Long("maxJobWorkers");
            s.m_PreserveTilesOutsideBounds = n.Int("preserveTilesOutsideBounds");
            return s;
        }
    }

    public partial struct NavMeshBuildSource
    {
        Matrix4x4 m_Transform;
        Vector3 m_Size;
        NavMeshBuildSourceShape m_Shape;
        int m_Area;
        int m_InstanceID;
        int m_ComponentID;
        int m_GenerateLinks;

        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
        public Vector3 size { get => m_Size; set => m_Size = value; }
        public NavMeshBuildSourceShape shape { get => m_Shape; set => m_Shape = value; }
        public int area { get => m_Area; set => m_Area = value; }
        public bool generateLinks { get => m_GenerateLinks != 0; set => m_GenerateLinks = value ? 1 : 0; }

        public Object sourceObject
        {
            get => Unbound.Runtime.ObjectRegistry.FindById(m_InstanceID);
            set => m_InstanceID = value != null ? value.GetInstanceID() : 0;
        }

        public Component component
        {
            get => Unbound.Runtime.ObjectRegistry.FindById(m_ComponentID) as Component;
            set => m_ComponentID = value != null ? value.GetInstanceID() : 0;
        }
    }

    public partial struct NavMeshBuildMarkup
    {
        int m_OverrideArea;
        int m_Area;
        int m_OverrideIgnore;
        int m_IgnoreFromBuild;
        int m_OverrideGenerateLinks;
        int m_GenerateLinks;
        int m_InstanceID;
        int m_IgnoreChildren;

        public bool overrideArea { get => m_OverrideArea != 0; set => m_OverrideArea = value ? 1 : 0; }
        public int area { get => m_Area; set => m_Area = value; }
        public bool overrideIgnore { get => m_OverrideIgnore != 0; set => m_OverrideIgnore = value ? 1 : 0; }
        public bool ignoreFromBuild { get => m_IgnoreFromBuild != 0; set => m_IgnoreFromBuild = value ? 1 : 0; }
        public bool overrideGenerateLinks { get => m_OverrideGenerateLinks != 0; set => m_OverrideGenerateLinks = value ? 1 : 0; }
        public bool generateLinks { get => m_GenerateLinks != 0; set => m_GenerateLinks = value ? 1 : 0; }
        public bool applyToChildren { get => m_IgnoreChildren == 0; set => m_IgnoreChildren = value ? 0 : 1; }

        public Transform root
        {
            get => Unbound.Runtime.ObjectRegistry.FindById(m_InstanceID) as Transform;
            set => m_InstanceID = value != null ? value.GetInstanceID() : 0;
        }
    }

    public partial struct NavMeshHit
    {
        Vector3 m_Position;
        Vector3 m_Normal;
        float m_Distance;
        int m_Mask;
        int m_Hit;

        public Vector3 position { get => m_Position; set => m_Position = value; }
        public Vector3 normal { get => m_Normal; set => m_Normal = value; }
        public float distance { get => m_Distance; set => m_Distance = value; }
        public int mask { get => m_Mask; set => m_Mask = value; }
        public bool hit { get => m_Hit != 0; set => m_Hit = value ? 1 : 0; }
    }

    public partial struct NavMeshTriangulation
    {
        public Vector3[] vertices;
        public int[] indices;
        public int[] areas;

        [Obsolete("Use areas instead.")]
        public int[] layers => areas;
    }

    public partial struct NavMeshQueryFilter
    {
        const int AreaCount = 32;
        float[] m_AreaCost;
        int m_AreaMask;
        int m_AgentTypeID;

        internal float[] costs => m_AreaCost;

        public int areaMask { get => m_AreaMask; set => m_AreaMask = value; }
        public int agentTypeID { get => m_AgentTypeID; set => m_AgentTypeID = value; }

        public float GetAreaCost(int areaIndex)
        {
            if (m_AreaCost == null)
            {
                if (areaIndex < 0 || areaIndex >= AreaCount)
                    throw new IndexOutOfRangeException($"The valid range is [0:{AreaCount - 1}]");
                return 1f;
            }
            return m_AreaCost[areaIndex];
        }

        public void SetAreaCost(int areaIndex, float cost)
        {
            if (m_AreaCost == null)
            {
                m_AreaCost = new float[AreaCount];
                for (int j = 0; j < AreaCount; ++j) m_AreaCost[j] = 1f;
            }
            m_AreaCost[areaIndex] = cost;
        }
    }

    public partial struct NavMeshDataInstance
    {
        int m_Handle;

        public bool valid => m_Handle != 0 && NavMesh.IsValidNavMeshDataHandle(m_Handle);

        internal int id
        {
            get => m_Handle;
            set => m_Handle = value;
        }

        public void Remove() => NavMesh.RemoveNavMeshDataInternal(id);

        internal void FlagAsInSelectionHierarchy() { }

        public Object owner
        {
            get => NavMesh.InternalGetOwner(id);
            set
            {
                int ownerID = value != null ? value.GetInstanceID() : 0;
                if (!NavMesh.InternalSetOwner(id, ownerID))
                    Debug.LogError("Cannot set 'owner' on an invalid NavMeshDataInstance");
            }
        }
    }

    public partial struct NavMeshLinkData
    {
        Vector3 m_StartPosition;
        Vector3 m_EndPosition;
        float m_CostModifier;
        int m_Bidirectional;
        float m_Width;
        int m_Area;
        int m_AgentTypeID;

        public Vector3 startPosition { get => m_StartPosition; set => m_StartPosition = value; }
        public Vector3 endPosition { get => m_EndPosition; set => m_EndPosition = value; }
        public float costModifier { get => m_CostModifier; set => m_CostModifier = value; }
        public bool bidirectional { get => m_Bidirectional != 0; set => m_Bidirectional = value ? 1 : 0; }
        public float width { get => m_Width; set => m_Width = value; }
        public int area { get => m_Area; set => m_Area = value; }
        public int agentTypeID { get => m_AgentTypeID; set => m_AgentTypeID = value; }
    }

    public partial struct NavMeshLinkInstance
    {
        int m_Handle;

        internal int id
        {
            get => m_Handle;
            set => m_Handle = value;
        }

        [Obsolete("valid has been deprecated. Use NavMesh.IsLinkValid() instead.")]
        public bool valid => m_Handle != 0 && NavMesh.IsValidLinkHandle(m_Handle);

        [Obsolete("Remove() has been deprecated. Use NavMesh.RemoveLink() instead.")]
        public void Remove() => NavMesh.RemoveLinkInternal(id);

        [Obsolete("owner has been deprecated. Use NavMesh.GetLinkOwner() and NavMesh.SetLinkOwner() instead.")]
        public Object owner
        {
            get => NavMesh.InternalGetLinkOwner(id);
            set
            {
                int ownerID = value != null ? value.GetInstanceID() : 0;
                if (!NavMesh.InternalSetLinkOwner(id, ownerID))
                    Debug.LogError("Cannot set 'owner' on an invalid NavMeshLinkInstance");
            }
        }
    }

    public partial struct OffMeshLinkData
    {
        internal int m_Valid;
        internal int m_Activated;
        internal int m_InstanceID;
        internal OffMeshLinkType m_LinkType;
        internal Vector3 m_StartPos;
        internal Vector3 m_EndPos;

        public bool valid => m_Valid != 0;
        public bool activated => m_Activated != 0;
        public OffMeshLinkType linkType => m_LinkType;
        public Vector3 startPos => m_StartPos;
        public Vector3 endPos => m_EndPos;
        public Object owner => Unbound.Runtime.ObjectRegistry.FindById(m_InstanceID);

#pragma warning disable CS0618
        [Obsolete("offMeshLink has been deprecated. Use 'owner' instead.")]
        public OffMeshLink offMeshLink => owner as OffMeshLink;
#pragma warning restore CS0618
    }

    /// <summary>The corners of a path and whether it reaches its target.</summary>
    public sealed partial class NavMeshPath
    {
        internal IntPtr m_Ptr;
        internal Vector3[] m_Corners;
        internal NavMeshPathStatus m_Status = NavMeshPathStatus.PathInvalid;
        /// <summary>The polygons the path crosses (for agents).</summary>
        internal long[] m_Polygons = Array.Empty<long>();

        public NavMeshPath()
        {
            m_Corners = Array.Empty<Vector3>();
        }

        public int GetCornersNonAlloc(Vector3[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int n = Math.Min(results.Length, m_Corners.Length);
            Array.Copy(m_Corners, results, n);
            return n;
        }

        public void ClearCorners()
        {
            m_Corners = Array.Empty<Vector3>();
            m_Polygons = Array.Empty<long>();
            m_Status = NavMeshPathStatus.PathInvalid;
        }

        public Vector3[] corners => (Vector3[])m_Corners.Clone();

        public NavMeshPathStatus status => m_Status;

        internal void CopyTo(NavMeshPath other)
        {
            other.m_Corners = (Vector3[])m_Corners.Clone();
            other.m_Polygons = (long[])m_Polygons.Clone();
            other.m_Status = m_Status;
        }
    }
}
