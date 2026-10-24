using System;

namespace UnityEngine.Rendering
{
    /// <summary>What the active pipeline supports (it sets its own; the defaults are the built-in
    /// pipeline's). Unbound reads <see cref="rendersUIOverlay"/>: overlay canvases are then drawn
    /// by the pipeline rather than after it.</summary>
    public partial class SupportedRenderingFeatures
    {
        static SupportedRenderingFeatures s_Active = new SupportedRenderingFeatures();

        public static SupportedRenderingFeatures active
        {
            get => s_Active ??= new SupportedRenderingFeatures();
            set => s_Active = value;
        }

        [Flags]
        public enum ReflectionProbeModes
        {
            None = 0,
            Rotation = 1,
        }

        [Flags]
        public enum LightmapMixedBakeModes
        {
            None = 0,
            IndirectOnly = 1,
            Subtractive = 2,
            Shadowmask = 4,
        }

        public ReflectionProbeModes reflectionProbeModes { get; set; } = ReflectionProbeModes.None;
        public LightmapMixedBakeModes defaultMixedLightingModes { get; set; } = LightmapMixedBakeModes.None;
        public LightmapMixedBakeModes mixedLightingModes { get; set; } = LightmapMixedBakeModes.IndirectOnly | LightmapMixedBakeModes.Subtractive | LightmapMixedBakeModes.Shadowmask;
        public LightmapBakeType lightmapBakeTypes { get; set; } = LightmapBakeType.Realtime | LightmapBakeType.Baked | LightmapBakeType.Mixed;
        public LightmapsMode lightmapsModes { get; set; } = LightmapsMode.CombinedDirectional;
        [Obsolete("Bake with the Progressive Lightmapper. The backend that uses Enlighten to bake is obsolete.")]
        public bool enlightenLightmapper { get; set; }
        public bool enlighten { get; set; } = true;
        public bool skyOcclusion { get; set; }
        public bool lightProbeProxyVolumes { get; set; } = true;
        public bool motionVectors { get; set; } = true;
        public bool receiveShadows { get; set; } = true;
        public bool reflectionProbes { get; set; } = true;
        public bool reflectionProbesBlendDistance { get; set; } = true;
        public bool rendererPriority { get; set; }
        public bool rendersUIOverlay { get; set; }
        public bool overridesEnvironmentLighting { get; set; }
        public bool overridesFog { get; set; }
        public bool overridesRealtimeReflectionProbes { get; set; }
        public bool overridesOtherLightingSettings { get; set; }
        public bool editableMaterialRenderQueue { get; set; } = true;
        public bool overridesLODBias { get; set; }
        public bool overridesMaximumLODLevel { get; set; }
        public bool overridesEnableLODCrossFade { get; set; }
        public bool rendererProbes { get; set; } = true;
        public bool particleSystemInstancing { get; set; } = true;

        [Obsolete("autoAmbientProbeBaking is obsolete. To enable or disable baking of the ambient probe, use ambientProbeBaking instead. (UnityUpgradable) -> ambientProbeBaking")]
        public bool autoAmbientProbeBaking { get => ambientProbeBaking; set => ambientProbeBaking = value; }

        [Obsolete("autoDefaultReflectionProbeBaking is obsolete. To enable or disable baking of the default reflection probe, use defaultReflectionProbeBaking instead. (UnityUpgradable) -> defaultReflectionProbeBaking")]
        public bool autoDefaultReflectionProbeBaking { get => defaultReflectionProbeBaking; set => defaultReflectionProbeBaking = value; }

        public bool ambientProbeBaking { get; set; } = true;
        public bool defaultReflectionProbeBaking { get; set; } = true;
        public bool overridesShadowmask { get; set; }
        public bool overridesLightProbeSystem { get; set; }
        public bool supportsHDR { get; set; }
        public bool supportsClouds { get; set; }
        public string overridesLightProbeSystemWarningMessage { get; set; } = "Light Probe Groups are unavailable as Probe Volumes have been enabled by the current Render Pipeline.";
        [Obsolete("terrainDetailUnsupported is deprecated.")]
        public bool terrainDetailUnsupported { get; set; }

        internal static bool IsMixedLightingModeSupported(MixedLightingMode mixedMode)
        {
            if (!IsLightmapBakeTypeSupported(LightmapBakeType.Mixed)) return false;
            var modes = active.mixedLightingModes;
            return mixedMode switch
            {
                MixedLightingMode.IndirectOnly => (modes & LightmapMixedBakeModes.IndirectOnly) != 0,
                MixedLightingMode.Subtractive => (modes & LightmapMixedBakeModes.Subtractive) != 0,
                MixedLightingMode.Shadowmask => (modes & LightmapMixedBakeModes.Shadowmask) != 0,
                _ => false,
            };
        }

        internal static bool IsLightmapBakeTypeSupported(LightmapBakeType bakeType)
        {
            if (bakeType == LightmapBakeType.Mixed && (!IsLightmapBakeTypeSupported(LightmapBakeType.Baked) || active.mixedLightingModes == LightmapMixedBakeModes.None))
                return false;
            return (active.lightmapBakeTypes & bakeType) == bakeType;
        }

        internal static bool IsLightmapsModeSupported(LightmapsMode mode) => (active.lightmapsModes & mode) == mode;
    }
}
