using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    /// <summary>The graphics tiers' settings (GetTierSettings): the project's, computed by the
    /// native side from GraphicsSettings (set by hand, else automatic: see
    /// unbound_project::settings::TierSettings::automatic) for the platform groups with a tier
    /// rule of their own, group 0 standing for every other group. Matches the Unity 6000.0.71f1
    /// Editor, tests/projects/tiersettings.</summary>
    public sealed partial class EditorGraphicsSettings
    {
        /// <summary>The group that stands for the groups without a tier rule of their own.</summary>
        const int OtherGroup = 0;

        static readonly Dictionary<(int Group, int Tier), TierSettings> s_Tiers = new Dictionary<(int, int), TierSettings>();

        /// <summary>The session's table (unbound-runtime's config): each platform group's and
        /// tier's settings.</summary>
        internal static void SetTierTable(IEnumerable<(int Group, int Tier, TierSettings Settings)> entries)
        {
            s_Tiers.Clear();
            foreach (var (group, tier, settings) in entries) s_Tiers[(group, tier)] = settings;
        }

        public static TierSettings GetTierSettings(BuildTargetGroup target, GraphicsTier tier)
        {
            // A tier out of range is refused (what the Editor does with one is unmeasured).
            if (tier < GraphicsTier.Tier1 || tier > GraphicsTier.Tier3)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unbound: not a graphics tier.");
            if (s_Tiers.TryGetValue(((int)target, (int)tier), out var settings) || s_Tiers.TryGetValue((OtherGroup, (int)tier), out settings))
                return settings;
            throw new InvalidOperationException("Unbound: the project's graphics tier settings were not given to this session.");
        }

        public static TierSettings GetTierSettings(NamedBuildTarget target, GraphicsTier tier) => GetTierSettings(target.ToBuildTargetGroup(), tier);

    }

    public enum ShaderQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    public partial struct TierSettings
    {
        public ShaderQuality standardShaderQuality;
        public CameraHDRMode hdrMode;
        public bool reflectionProbeBoxProjection;
        public bool reflectionProbeBlending;
        public bool hdr;
        public bool detailNormalMap;
        public bool cascadedShadowMaps;
        public bool prefer32BitShadowMaps;
        public bool enableLPPV;
        public bool semitransparentShadows;
        public UnityEngine.RenderingPath renderingPath;
        public RealtimeGICPUUsage realtimeGICPUUsage;
    }
}
