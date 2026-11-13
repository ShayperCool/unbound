using UnityEngine;

namespace UnityEditor.SpeedTree.Importer
{
    /// <summary>The SpeedTree importers' constants (Unity 6000.0.71f1's values).</summary>
    internal partial class SpeedTreeConstants
    {
        internal static readonly float kFeetToMetersRatio = 0.3048f;
        internal static readonly float kCentimetersToMetersRatio = 0.01f;
        internal static readonly float kInchesToMetersRatio = 0.0254f;
        internal static readonly float kAlphaTestRef = 0.33f;
        internal static readonly string kBillboardMaterialName = "Billboard";

        public SpeedTreeConstants() { }
    }

    internal static partial class SpeedTreeImporterCommon
    {
        internal enum STUnitConversion
        {
            kLeaveAsIs = 0,
            kFeetToMeters = 1,
            kCentimetersToMeters = 2,
            kInchesToMeters = 3,
            kCustomConversion = 4,
        }

        /// <summary>The shader properties the SpeedTree 9 importer sets on its materials.</summary>
        internal static partial class MaterialProperties
        {
            internal static readonly int MainTexID = Shader.PropertyToID("_MainTex");
            internal static readonly int ColorTintID = Shader.PropertyToID("_ColorTint");
            internal static readonly int NormalMapKwToggleID = Shader.PropertyToID("_NormalMapKwToggle");
            internal static readonly int NormalMapID = Shader.PropertyToID("_NormalMap");
            internal static readonly int ExtraMapKwToggleID = Shader.PropertyToID("_ExtraMapKwToggle");
            internal static readonly int ExtraTexID = Shader.PropertyToID("_ExtraTex");
            internal static readonly int GlossinessID = Shader.PropertyToID("_Glossiness");
            internal static readonly int MetallicID = Shader.PropertyToID("_Metallic");
            internal static readonly int SubsurfaceKwToggleID = Shader.PropertyToID("_SubsurfaceKwToggle");
            internal static readonly int SubsurfaceTexID = Shader.PropertyToID("_SubsurfaceTex");
            internal static readonly int SubsurfaceColorID = Shader.PropertyToID("_SubsurfaceColor");
            internal static readonly int AlphaClipThresholdID = Shader.PropertyToID("_AlphaClipThreshold");
            internal static readonly int TransmissionScaleID = Shader.PropertyToID("_TransmissionScale");
            internal static readonly int DiffusionProfileAssetID = Shader.PropertyToID("_Diffusion_Profile_Asset");
            internal static readonly int DiffusionProfileID = Shader.PropertyToID("_DiffusionProfile");
            internal static readonly int HueVariationKwToggleID = Shader.PropertyToID("_HueVariationKwToggle");
            internal static readonly int HueVariationColorID = Shader.PropertyToID("_HueVariationColor");
            internal static readonly int LeafFacingKwToggleID = Shader.PropertyToID("_LeafFacingKwToggle");
            internal static readonly int BillboardKwToggleID = Shader.PropertyToID("_BillboardKwToggle");
            internal static readonly int DoubleSidedToggleID = Shader.PropertyToID("_DoubleSidedKwToggle");
            internal static readonly int DoubleSidedNormalModeID = Shader.PropertyToID("_DoubleSidedNormalMode");
            internal static readonly int BackfaceNormalModeID = Shader.PropertyToID("_BackfaceNormalMode");
            internal static readonly int TwoSidedID = Shader.PropertyToID("_TwoSided");
            internal static readonly int WindSharedKwToggle = Shader.PropertyToID("_WIND_SHARED");
            internal static readonly int WindBranch2KwToggle = Shader.PropertyToID("_WIND_BRANCH2");
            internal static readonly int WindBranch1KwToggle = Shader.PropertyToID("_WIND_BRANCH1");
            internal static readonly int WindRippleKwToggle = Shader.PropertyToID("_WIND_RIPPLE");
            internal static readonly int WindShimmerKwToggle = Shader.PropertyToID("_WIND_SHIMMER");
        }

        /// <summary>The keywords of the SpeedTree shaders' vertex setups and billboards.</summary>
        internal static partial class MaterialKeywords
        {
            internal static readonly string VBSetupStandardID = "VB_SETUP_STANDARD";
            internal static readonly string VBSetupBranch2ID = "VB_SETUP_BRANCH2";
            internal static readonly string VBSetupCameraFacingID = "VB_SETUP_CAMERA_FACING";
            internal static readonly string VBSetupBranch2AndCameraFacingID = "VB_SETUP_BRANCH2_AND_CAMERA_FACING";
            internal static readonly string VBSetupBillboardID = "VB_SETUP_BILLBOARD";
            internal static readonly string VBSetupID = "VB_SETUP";
            internal static readonly string BillboardID = "_BILLBOARD";
        }
    }
}

namespace UnityEditor.SpeedTree.Importer
{
    /// <summary>Marks a static method the SpeedTree 9 importer calls with each imported tree's main
    /// object (render pipelines post-process the materials).</summary>
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
    public partial class MaterialSettingsCallbackAttribute : System.Attribute
    {
        public int MethodVersion;

        public MaterialSettingsCallbackAttribute(int methodVersion) => MethodVersion = methodVersion;
    }

    public partial class SpeedTree9Importer
    {
        public delegate void OnAssetPostProcess(GameObject mainObject);
    }
}
