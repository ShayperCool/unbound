using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A Lighting Settings asset (<c>.lighting</c>): the settings the Editor precomputes a scene's
    /// lighting with (Manual "Lighting Settings Asset"; Scripting API "LightingSettings",
    /// 6000.0). Scenes reference one through <c>LightmapSettings.m_LightingSettings</c>
    /// (UnityEditor.Lightmapping.lightingSettings).
    /// <para>In Play mode the settings are bake inputs: the frames come from the scene's lighting
    /// data (lightmaps, light probes, each light's baking output). Changing a scene's settings, or
    /// assigning other settings, without baking again changes nothing drawn (measured:
    /// tests/projects/lightingassets, a scene baked non-directional with baked indirect whose
    /// settings were then switched to subtractive, directional, compressed, baked GI off, realtime
    /// GI on; and again at runtime). Enlighten Realtime GI precomputed into the lighting data does
    /// change the frames; Unbound does not draw it and says so (<see cref="BakedLighting"/>).</para>
    /// <para>Setters clamp and round as Unity 6000.0.71f1's do (tests/projects/lightingassets).</para>
    /// </summary>
    [ClassId(850595691)]
    public sealed partial class LightingSettings : Object, ISerializedAsset, Instantiation.ICloneable
    {
        /// <summary>The serialized state; its defaults are a new object's (`new LightingSettings()`,
        /// Lightmapping.lightingSettingsDefaults).</summary>
        struct State
        {
            internal bool BakedGI;
            internal bool RealtimeGI;
            internal bool RealtimeEnvironmentLighting;
            internal MixedLightingMode MixedBakeMode;
            internal float AlbedoBoost;
            internal float IndirectScale;
            internal float BounceScale;
            internal Lightmapper Lightmapper;
            internal int LightmapMaxSize;
            internal float LightmapResolution;
            internal int LightmapPadding;
            internal LightmapCompression LightmapCompression;
            internal bool AO;
            internal float AOMaxDistance;
            internal float AOExponentIndirect;
            internal float AOExponentDirect;
            internal bool ExtractAO;
            internal LightmapsMode DirectionalityMode;
            internal UnityEngine.FilterMode LightmapFilterMode;
            internal bool ExportTrainingData;
            internal string TrainingDataDestination;
            internal float IndirectResolution;
            internal bool ForceWhiteAlbedo;
            internal bool ForceUpdates;
            internal bool PrioritizeView;
            internal Sampling Sampling;
            internal int DirectSampleCount;
            internal int IndirectSampleCount;
            internal int EnvironmentSampleCount;
            internal int EnvironmentReferencePointCount;
            internal float LightProbeSampleCountMultiplier;
            internal int MaxBounces;
            internal int MinBounces;
            internal bool EnvironmentImportanceSampling;
            internal FilterMode FilteringMode;
            internal DenoiserType DenoiserTypeDirect;
            internal DenoiserType DenoiserTypeIndirect;
            internal DenoiserType DenoiserTypeAO;
            internal FilterType FilterTypeDirect;
            internal FilterType FilterTypeIndirect;
            internal FilterType FilterTypeAO;
            internal float GaussianRadiusDirect;
            internal float GaussianRadiusIndirect;
            internal float GaussianRadiusAO;
            internal float AtrousSigmaDirect;
            internal float AtrousSigmaIndirect;
            internal float AtrousSigmaAO;
            internal bool RespectSceneVisibility;

            internal static State Defaults => new State
            {
                BakedGI = true,
                RealtimeEnvironmentLighting = true,
                MixedBakeMode = MixedLightingMode.Shadowmask,
                AlbedoBoost = 1f,
                IndirectScale = 1f,
                BounceScale = 1f,
                Lightmapper = Lightmapper.ProgressiveCPU,
                LightmapMaxSize = 1024,
                LightmapResolution = 40f,
                LightmapPadding = 2,
                LightmapCompression = LightmapCompression.HighQuality,
                AOMaxDistance = 1f,
                AOExponentIndirect = 1f,
                DirectionalityMode = LightmapsMode.CombinedDirectional,
                LightmapFilterMode = UnityEngine.FilterMode.Bilinear,
                TrainingDataDestination = "TrainingData",
                IndirectResolution = 2f,
                PrioritizeView = true,
                Sampling = Sampling.Fixed,
                DirectSampleCount = 32,
                IndirectSampleCount = 512,
                EnvironmentSampleCount = 256,
                EnvironmentReferencePointCount = 2048,
                LightProbeSampleCountMultiplier = 4f,
                MaxBounces = 2,
                MinBounces = 2,
                EnvironmentImportanceSampling = true,
                FilteringMode = FilterMode.Auto,
                DenoiserTypeDirect = DenoiserType.Optix,
                DenoiserTypeIndirect = DenoiserType.Optix,
                DenoiserTypeAO = DenoiserType.Optix,
                FilterTypeDirect = FilterType.Gaussian,
                FilterTypeIndirect = FilterType.Gaussian,
                FilterTypeAO = FilterType.Gaussian,
                GaussianRadiusDirect = 1f,
                GaussianRadiusIndirect = 1f,
                GaussianRadiusAO = 1f,
                AtrousSigmaDirect = 0.5f,
                AtrousSigmaIndirect = 2f,
                AtrousSigmaAO = 1f,
            };
        }

        State m_State = State.Defaults;

        // The limits Unity 6000.0.71f1's setters apply (tests/projects/lightingassets).
        const int k_MinLightmapSize = 32, k_MaxLightmapSize = 4096;
        const int k_MinPadding = 2, k_MaxPadding = 100;
        const float k_MinResolution = 0.0001f;
        const float k_MaxIndirectScale = 5f;
        const float k_MinAlbedoBoost = 1f, k_MaxAlbedoBoost = 10f;
        const int k_MaxBounces = 100;
        const float k_MaxAOExponent = 10f;
        const float k_MinProbeSampleMultiplier = 1f / 64f;
        const float k_MaxGaussianRadius = 5f;
        const float k_MaxAtrousSigma = 2f;
        /// <summary>The largest sample count kept a power of two without overflow (no upper limit
        /// was measured: 100000 rounds to 131072).</summary>
        const int k_MaxSampleCount = 1 << 30;

        public LightingSettings() { }

        public bool bakedGI { get => m_State.BakedGI; set => m_State.BakedGI = value; }
        public bool realtimeGI { get => m_State.RealtimeGI; set => m_State.RealtimeGI = value; }

        /// <summary>Whether environment lighting goes to the realtime lightmaps: always without
        /// baked GI, else only with realtime GI and the setting on (the setting itself is kept).</summary>
        public bool realtimeEnvironmentLighting
        {
            get => !m_State.BakedGI || (m_State.RealtimeGI && m_State.RealtimeEnvironmentLighting);
            set => m_State.RealtimeEnvironmentLighting = value;
        }

        /// <summary>Always false; setting it does nothing (measured).</summary>
        [Obsolete("LightingSettings.autoGenerate is obsolete.")]
        [Unbound.EditorFaithful("measured: tests/projects/lightingassets (reads false after setting true)")]
        public bool autoGenerate { get => false; set { } }

        /// <summary>A value that is not a mixed lighting mode stores Shadowmask.</summary>
        public MixedLightingMode mixedBakeMode
        {
            get => m_State.MixedBakeMode;
            set => m_State.MixedBakeMode = value >= MixedLightingMode.IndirectOnly && value <= MixedLightingMode.Shadowmask ? value : MixedLightingMode.Shadowmask;
        }

        public float albedoBoost { get => m_State.AlbedoBoost; set => m_State.AlbedoBoost = Mathf.Clamp(value, k_MinAlbedoBoost, k_MaxAlbedoBoost); }
        public float indirectScale { get => m_State.IndirectScale; set => m_State.IndirectScale = Mathf.Clamp(value, 0f, k_MaxIndirectScale); }

        /// <summary>The obsolete Enlighten backend bakes with the CPU lightmapper.</summary>
        public Lightmapper lightmapper
        {
            get => m_State.Lightmapper;
            set => m_State.Lightmapper = (int)value == 0 ? Lightmapper.ProgressiveCPU : value;
        }

        /// <summary>The closest power of two within 32..4096.</summary>
        public int lightmapMaxSize
        {
            get => m_State.LightmapMaxSize;
            set => m_State.LightmapMaxSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(value, k_MinLightmapSize, k_MaxLightmapSize));
        }

        public float lightmapResolution { get => m_State.LightmapResolution; set => m_State.LightmapResolution = Mathf.Max(k_MinResolution, value); }
        public int lightmapPadding { get => m_State.LightmapPadding; set => m_State.LightmapPadding = Mathf.Clamp(value, k_MinPadding, k_MaxPadding); }

        /// <summary>Values past the last compression level store the last one (only values above
        /// the range were measured).</summary>
        public LightmapCompression lightmapCompression
        {
            get => m_State.LightmapCompression;
            set => m_State.LightmapCompression = (LightmapCompression)Mathf.Clamp((int)value, (int)LightmapCompression.None, (int)LightmapCompression.HighQuality);
        }

        [Obsolete("Use LightingSettings.lightmapCompression instead.")]
        public bool compressLightmaps
        {
            get => m_State.LightmapCompression != LightmapCompression.None;
            set => m_State.LightmapCompression = value ? LightmapCompression.HighQuality : LightmapCompression.None;
        }

        public bool ao { get => m_State.AO; set => m_State.AO = value; }
        public float aoMaxDistance { get => m_State.AOMaxDistance; set => m_State.AOMaxDistance = Mathf.Max(0f, value); }
        public float aoExponentIndirect { get => m_State.AOExponentIndirect; set => m_State.AOExponentIndirect = Mathf.Clamp(value, 0f, k_MaxAOExponent); }
        public float aoExponentDirect { get => m_State.AOExponentDirect; set => m_State.AOExponentDirect = Mathf.Clamp(value, 0f, k_MaxAOExponent); }
        public bool extractAO { get => m_State.ExtractAO; set => m_State.ExtractAO = value; }

        /// <summary>Anything but CombinedDirectional stores NonDirectional.</summary>
        public LightmapsMode directionalityMode
        {
            get => m_State.DirectionalityMode;
            set => m_State.DirectionalityMode = value == LightmapsMode.CombinedDirectional ? LightmapsMode.CombinedDirectional : LightmapsMode.NonDirectional;
        }

        internal UnityEngine.FilterMode lightmapFilterMode { get => m_State.LightmapFilterMode; set => m_State.LightmapFilterMode = value; }

        [Obsolete("Training data export for baked lighting is deprecated.")]
        public bool exportTrainingData { get => m_State.ExportTrainingData; set => m_State.ExportTrainingData = value; }

        /// <summary>Null leaves the destination as it is.</summary>
        [Obsolete("Training data export for baked lighting is deprecated.")]
        public string trainingDataDestination { get => m_State.TrainingDataDestination; set { if (value != null) m_State.TrainingDataDestination = value; } }

        public float indirectResolution { get => m_State.IndirectResolution; set => m_State.IndirectResolution = Mathf.Max(k_MinResolution, value); }
        internal bool realtimeForceWhiteAlbedo { get => m_State.ForceWhiteAlbedo; set => m_State.ForceWhiteAlbedo = value; }
        internal bool realtimeForceUpdates { get => m_State.ForceUpdates; set => m_State.ForceUpdates = value; }

        /// <summary>Values past Fixed store Fixed (only values above the range were measured).</summary>
        public Sampling sampling
        {
            get => m_State.Sampling;
            set => m_State.Sampling = (Sampling)Mathf.Clamp((int)value, (int)Sampling.Auto, (int)Sampling.Fixed);
        }

        public int directSampleCount { get => m_State.DirectSampleCount; set => m_State.DirectSampleCount = SampleCount(value); }
        public int indirectSampleCount { get => m_State.IndirectSampleCount; set => m_State.IndirectSampleCount = SampleCount(value); }
        public int environmentSampleCount { get => m_State.EnvironmentSampleCount; set => m_State.EnvironmentSampleCount = SampleCount(value); }
        internal int environmentReferencePointCount { get => m_State.EnvironmentReferencePointCount; set => m_State.EnvironmentReferencePointCount = value; }

        /// <summary>Sample counts are powers of two, at least one: the closest one.</summary>
        static int SampleCount(int value) => value <= 1 ? 1 : Mathf.ClosestPowerOfTwo(Math.Min(value, k_MaxSampleCount));

        [Obsolete("Use LightingSettings.maxBounces instead. (UnityUpgradable) -> UnityEngine.LightingSettings.maxBounces")]
        public int bounces { get => maxBounces; set => maxBounces = value; }

        /// <summary>Within 0..100; a minimum above it comes down with it.</summary>
        public int maxBounces
        {
            get => m_State.MaxBounces;
            set
            {
                m_State.MaxBounces = Mathf.Clamp(value, 0, k_MaxBounces);
                if (m_State.MinBounces > m_State.MaxBounces) m_State.MinBounces = m_State.MaxBounces;
            }
        }

        [Obsolete("Use LightingSettings.minBounces instead. (UnityUpgradable) -> UnityEngine.LightingSettings.minBounces")]
        public int russianRouletteStartBounce { get => minBounces; set => minBounces = value; }

        /// <summary>Within 0..maxBounces.</summary>
        public int minBounces { get => m_State.MinBounces; set => m_State.MinBounces = Mathf.Clamp(value, 0, m_State.MaxBounces); }

        public bool prioritizeView { get => m_State.PrioritizeView; set => m_State.PrioritizeView = value; }
        public FilterMode filteringMode { get => m_State.FilteringMode; set => m_State.FilteringMode = value; }
        public DenoiserType denoiserTypeDirect { get => m_State.DenoiserTypeDirect; set => m_State.DenoiserTypeDirect = value; }
        public DenoiserType denoiserTypeIndirect { get => m_State.DenoiserTypeIndirect; set => m_State.DenoiserTypeIndirect = value; }
        public DenoiserType denoiserTypeAO { get => m_State.DenoiserTypeAO; set => m_State.DenoiserTypeAO = value; }
        public FilterType filterTypeDirect { get => m_State.FilterTypeDirect; set => m_State.FilterTypeDirect = value; }
        public FilterType filterTypeIndirect { get => m_State.FilterTypeIndirect; set => m_State.FilterTypeIndirect = value; }
        public FilterType filterTypeAO { get => m_State.FilterTypeAO; set => m_State.FilterTypeAO = value; }
        public float filteringGaussianRadiusDirect { get => m_State.GaussianRadiusDirect; set => m_State.GaussianRadiusDirect = Mathf.Clamp(value, 0f, k_MaxGaussianRadius); }
        public float filteringGaussianRadiusIndirect { get => m_State.GaussianRadiusIndirect; set => m_State.GaussianRadiusIndirect = Mathf.Clamp(value, 0f, k_MaxGaussianRadius); }
        public float filteringGaussianRadiusAO { get => m_State.GaussianRadiusAO; set => m_State.GaussianRadiusAO = Mathf.Clamp(value, 0f, k_MaxGaussianRadius); }
        public float filteringAtrousPositionSigmaDirect { get => m_State.AtrousSigmaDirect; set => m_State.AtrousSigmaDirect = Mathf.Clamp(value, 0f, k_MaxAtrousSigma); }
        public float filteringAtrousPositionSigmaIndirect { get => m_State.AtrousSigmaIndirect; set => m_State.AtrousSigmaIndirect = Mathf.Clamp(value, 0f, k_MaxAtrousSigma); }
        public float filteringAtrousPositionSigmaAO { get => m_State.AtrousSigmaAO; set => m_State.AtrousSigmaAO = Mathf.Clamp(value, 0f, k_MaxAtrousSigma); }
        public bool environmentImportanceSampling { get => m_State.EnvironmentImportanceSampling; set => m_State.EnvironmentImportanceSampling = value; }
        public float lightProbeSampleCountMultiplier { get => m_State.LightProbeSampleCountMultiplier; set => m_State.LightProbeSampleCountMultiplier = Mathf.Max(k_MinProbeSampleMultiplier, value); }
        public bool respectSceneVisibilityWhenBakingGI { get => m_State.RespectSceneVisibility; set => m_State.RespectSceneVisibility = value; }

        // The integer radii are the float ones truncated.
        [Obsolete("LightingSettings.filteringGaussRadiusDirect is deprecated. Use filteringGaussianRadiusDirect instead.")]
        public int filteringGaussRadiusDirect { get => (int)filteringGaussianRadiusDirect; set => filteringGaussianRadiusDirect = value; }
        [Obsolete("LightingSettings.filteringGaussRadiusIndirect is deprecated. Use filteringGaussianRadiusIndirect instead.")]
        public int filteringGaussRadiusIndirect { get => (int)filteringGaussianRadiusIndirect; set => filteringGaussianRadiusIndirect = value; }
        [Obsolete("LightingSettings.filteringGaussRadiusAO is deprecated. Use filteringGaussianRadiusAO instead.")]
        public int filteringGaussRadiusAO { get => (int)filteringGaussianRadiusAO; set => filteringGaussianRadiusAO = value; }

        /// <summary>The asset's values as written (the Editor clamped them when it wrote them).
        /// Keys an older serializedVersion lacks keep the defaults.</summary>
        internal static bool ReadRealtimeGI(YNode body, bool fallback = false) => body.Bool("m_EnableRealtimeLightmaps", fallback);

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_State.BakedGI = body.Bool("m_EnableBakedLightmaps", m_State.BakedGI);
            m_State.RealtimeGI = ReadRealtimeGI(body, m_State.RealtimeGI);
            m_State.RealtimeEnvironmentLighting = body.Bool("m_RealtimeEnvironmentLighting", m_State.RealtimeEnvironmentLighting);
            m_State.BounceScale = body.Float("m_BounceScale", m_State.BounceScale);
            m_State.AlbedoBoost = body.Float("m_AlbedoBoost", m_State.AlbedoBoost);
            m_State.IndirectScale = body.Float("m_IndirectOutputScale", m_State.IndirectScale);
            m_State.Lightmapper = (Lightmapper)body.Int("m_BakeBackend", (int)m_State.Lightmapper);
            m_State.LightmapMaxSize = body.Int("m_LightmapMaxSize", m_State.LightmapMaxSize);
            m_State.LightmapResolution = body.Float("m_BakeResolution", m_State.LightmapResolution);
            m_State.LightmapPadding = body.Int("m_Padding", m_State.LightmapPadding);
            m_State.LightmapCompression = (LightmapCompression)body.Int("m_LightmapCompression", (int)m_State.LightmapCompression);
            m_State.AO = body.Bool("m_AO", m_State.AO);
            m_State.AOMaxDistance = body.Float("m_AOMaxDistance", m_State.AOMaxDistance);
            m_State.AOExponentIndirect = body.Float("m_CompAOExponent", m_State.AOExponentIndirect);
            m_State.AOExponentDirect = body.Float("m_CompAOExponentDirect", m_State.AOExponentDirect);
            m_State.ExtractAO = body.Bool("m_ExtractAO", m_State.ExtractAO);
            m_State.MixedBakeMode = (MixedLightingMode)body.Int("m_MixedBakeMode", (int)m_State.MixedBakeMode);
            m_State.DirectionalityMode = (LightmapsMode)body.Int("m_LightmapsBakeMode", (int)m_State.DirectionalityMode);
            m_State.LightmapFilterMode = (UnityEngine.FilterMode)body.Int("m_FilterMode", (int)m_State.LightmapFilterMode);
            m_State.ExportTrainingData = body.Bool("m_ExportTrainingData", m_State.ExportTrainingData);
            m_State.TrainingDataDestination = body.Str("m_TrainingDataDestination", m_State.TrainingDataDestination);
            m_State.IndirectResolution = body.Float("m_RealtimeResolution", m_State.IndirectResolution);
            m_State.ForceWhiteAlbedo = body.Bool("m_ForceWhiteAlbedo", m_State.ForceWhiteAlbedo);
            m_State.ForceUpdates = body.Bool("m_ForceUpdates", m_State.ForceUpdates);
            m_State.PrioritizeView = body.Bool("m_PVRCulling", m_State.PrioritizeView);
            m_State.Sampling = (Sampling)body.Int("m_PVRSampling", (int)m_State.Sampling);
            m_State.DirectSampleCount = body.Int("m_PVRDirectSampleCount", m_State.DirectSampleCount);
            m_State.IndirectSampleCount = body.Int("m_PVRSampleCount", m_State.IndirectSampleCount);
            m_State.EnvironmentSampleCount = body.Int("m_PVREnvironmentSampleCount", m_State.EnvironmentSampleCount);
            m_State.EnvironmentReferencePointCount = body.Int("m_PVREnvironmentReferencePointCount", m_State.EnvironmentReferencePointCount);
            m_State.LightProbeSampleCountMultiplier = body.Float("m_LightProbeSampleCountMultiplier", m_State.LightProbeSampleCountMultiplier);
            m_State.MaxBounces = body.Int("m_PVRBounces", m_State.MaxBounces);
            m_State.MinBounces = body.Int("m_PVRMinBounces", m_State.MinBounces);
            // Older assets name it after multiple importance sampling.
            m_State.EnvironmentImportanceSampling = body.Bool("m_PVREnvironmentImportanceSampling", body.Bool("m_PVREnvironmentMIS", m_State.EnvironmentImportanceSampling));
            m_State.FilteringMode = (FilterMode)body.Int("m_PVRFilteringMode", (int)m_State.FilteringMode);
            m_State.DenoiserTypeDirect = (DenoiserType)body.Int("m_PVRDenoiserTypeDirect", (int)m_State.DenoiserTypeDirect);
            m_State.DenoiserTypeIndirect = (DenoiserType)body.Int("m_PVRDenoiserTypeIndirect", (int)m_State.DenoiserTypeIndirect);
            m_State.DenoiserTypeAO = (DenoiserType)body.Int("m_PVRDenoiserTypeAO", (int)m_State.DenoiserTypeAO);
            m_State.FilterTypeDirect = (FilterType)body.Int("m_PVRFilterTypeDirect", (int)m_State.FilterTypeDirect);
            m_State.FilterTypeIndirect = (FilterType)body.Int("m_PVRFilterTypeIndirect", (int)m_State.FilterTypeIndirect);
            m_State.FilterTypeAO = (FilterType)body.Int("m_PVRFilterTypeAO", (int)m_State.FilterTypeAO);
            m_State.GaussianRadiusDirect = body.Float("m_PVRFilteringGaussRadiusDirect", m_State.GaussianRadiusDirect);
            m_State.GaussianRadiusIndirect = body.Float("m_PVRFilteringGaussRadiusIndirect", m_State.GaussianRadiusIndirect);
            m_State.GaussianRadiusAO = body.Float("m_PVRFilteringGaussRadiusAO", m_State.GaussianRadiusAO);
            m_State.AtrousSigmaDirect = body.Float("m_PVRFilteringAtrousPositionSigmaDirect", m_State.AtrousSigmaDirect);
            m_State.AtrousSigmaIndirect = body.Float("m_PVRFilteringAtrousPositionSigmaIndirect", m_State.AtrousSigmaIndirect);
            m_State.AtrousSigmaAO = body.Float("m_PVRFilteringAtrousPositionSigmaAO", m_State.AtrousSigmaAO);
            m_State.RespectSceneVisibility = body.Bool("m_RespectSceneVisibilityWhenBakingGI", m_State.RespectSceneVisibility);
        }

        Object Instantiation.ICloneable.CloneObject() => new LightingSettings { m_State = m_State };

        public enum Lightmapper
        {
            [Obsolete("Use Lightmapper.ProgressiveCPU instead. (UnityUpgradable) -> UnityEngine.LightingSettings/Lightmapper.ProgressiveCPU")] Enlighten = 0,
            ProgressiveCPU = 1,
            ProgressiveGPU = 2,
        }

        public enum Sampling
        {
            Auto = 0,
            Fixed = 1,
        }

        public enum FilterMode
        {
            None = 0,
            Auto = 1,
            Advanced = 2,
        }

        public enum DenoiserType
        {
            None = 0,
            Optix = 1,
            OpenImage = 2,
            [Obsolete("DenoiserType.RadeonPro is obsolete, use DenoiserType.OpenImage instead.")] RadeonPro = 3,
        }

        public enum FilterType
        {
            Gaussian = 0,
            ATrous = 1,
            None = 2,
        }
    }

    public enum LightmapCompression
    {
        None = 0,
        LowQuality = 1,
        NormalQuality = 2,
        HighQuality = 3,
    }
}
