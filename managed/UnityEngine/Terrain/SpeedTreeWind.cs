using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The wind configuration of a SpeedTree 9 tree (its importer makes one); Unbound keeps
    /// it for the trees' renderers. Wind animation is not drawn yet.</summary>
    [ClassId(228)]
    [ExcludeFromPreset]
    public partial class SpeedTreeWindAsset : Object
    {
        int m_Version;
        internal SpeedTreeWindConfig9 Config;

        internal SpeedTreeWindAsset(int version, SpeedTreeWindConfig9 config)
        {
            m_Version = version;
            Config = config;
        }

        public int Version
        {
            get { ThrowIfUncreated(); return m_Version; }
            set { ThrowIfUncreated(); m_Version = value; }
        }
    }

    /// <summary>The wind parameters of a SpeedTree 9 tree (Unity 6000.0.71f1's layout).</summary>
    internal unsafe partial struct SpeedTreeWindConfig9
    {
        /// <summary>The release's defaults (the gust and response settings; curves and switches
        /// zero).</summary>
        public SpeedTreeWindConfig9()
        {
            strengthResponse = 5f;
            directionResponse = 2.5f;
            gustStrengthMin = 0.5f;
            gustStrengthMax = 1f;
            gustDurationMin = 1f;
            gustDurationMax = 4f;
            gustRiseScalar = 1f;
            gustFallScalar = 1f;
            branch1StretchLimit = 1f;
            branch2StretchLimit = 1f;
            importScale = 1f;
        }

        public float strengthResponse;
        public float directionResponse;
        public float gustFrequency;
        public float gustStrengthMin;
        public float gustStrengthMax;
        public float gustDurationMin;
        public float gustDurationMax;
        public float gustRiseScalar;
        public float gustFallScalar;
        public float branch1StretchLimit;
        public float branch2StretchLimit;
        public float sharedHeightStart;
        public fixed float bendShared[20];
        public fixed float oscillationShared[20];
        public fixed float speedShared[20];
        public fixed float turbulenceShared[20];
        public fixed float flexibilityShared[20];
        public float independenceShared;
        public fixed float bendBranch1[20];
        public fixed float oscillationBranch1[20];
        public fixed float speedBranch1[20];
        public fixed float turbulenceBranch1[20];
        public fixed float flexibilityBranch1[20];
        public float independenceBranch1;
        public fixed float bendBranch2[20];
        public fixed float oscillationBranch2[20];
        public fixed float speedBranch2[20];
        public fixed float turbulenceBranch2[20];
        public fixed float flexibilityBranch2[20];
        public float independenceBranch2;
        public fixed float planarRipple[20];
        public fixed float directionalRipple[20];
        public fixed float speedRipple[20];
        public fixed float flexibilityRipple[20];
        public float independenceRipple;
        public float shimmerRipple;
        public float treeExtentX;
        public float treeExtentY;
        public float treeExtentZ;
        public float windIndependence;
        public int doShared;
        public int doBranch1;
        public int doBranch2;
        public int doRipple;
        public int doShimmer;
        public int lodFade;
        public float importScale;

        /// <summary>Whether any wind part is on (shimmer goes with ripple).</summary>
        public readonly bool IsWindEnabled => doShared != 0 || doBranch1 != 0 || doBranch2 != 0 || doRipple != 0;
    }

    /// <summary>A tree's component: the SpeedTree 9 importer gives each level of detail one, with
    /// the tree's wind asset.</summary>
    [ClassId(193)]
    [ExcludeFromPreset]
    public sealed partial class Tree : Component
    {
        SpeedTreeWindAsset m_WindAsset;

        public Tree() { }

        public SpeedTreeWindAsset windAsset
        {
            get => m_WindAsset;
            set => m_WindAsset = value;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var wind = body.Ref("m_SpeedTreeWindAsset");
            m_WindAsset = wind.IsNull ? null : resolver.Resolve(wind, typeof(SpeedTreeWindAsset)) as SpeedTreeWindAsset;
        }

        internal override void CopyFrom(Component source, System.Func<Object, Object> remap) =>
            m_WindAsset = ((Tree)source).m_WindAsset;

        internal override void WriteSerialized(UnityYamlWriter.Writer w) => w.Ref("m_SpeedTreeWindAsset", m_WindAsset);
    }
}
