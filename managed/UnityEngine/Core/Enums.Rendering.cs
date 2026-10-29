// Generated from the metadata of Unity 6000.0's reference assemblies (the rendering enums the
// hand-written graphics code uses): names and values must match exactly. Do not edit by hand;
// regenerate with the enum dumper:
//   dotnet run --project managed/Unbound.EnumDumper -- <Editor>/Data/Managed/UnityEngine UnityEngine.RenderTextureMemoryless UnityEngine.VRTextureUsage UnityEngine.RenderTextureCreationFlags UnityEngine.Rendering.RenderBufferLoadAction UnityEngine.Rendering.RenderBufferStoreAction UnityEngine.Rendering.RTClearFlags UnityEngine.Rendering.CommandBufferExecutionFlags UnityEngine.Rendering.FormatSwizzle UnityEngine.Rendering.LightEvent UnityEngine.Rendering.SinglePassStereoMode UnityEngine.Rendering.FoveatedRenderingMode UnityEngine.Rendering.ShadowMapPass UnityEngine.Rendering.RenderTargetFlags UnityEngine.Rendering.ComputeQueueType UnityEngine.Rendering.SynchronisationStageFlags UnityEngine.Rendering.GraphicsFenceType UnityEngine.Rendering.DistanceMetric UnityEngine.Rendering.PerObjectData UnityEngine.Rendering.CullingOptions UnityEngine.Rendering.ReflectionProbeSortingCriteria UnityEngine.Rendering.RenderStateMask UnityEngine.Rendering.SubPassFlags UnityEngine.Rendering.GizmoSubset UnityEngine.Rendering.UISubset UnityEngine.ShadowObjectsFilter UnityEngine.Rendering.BatchCullingProjectionType UnityEngine.Rendering.RendererListStatus UnityEngine.Rendering.RenderingThreadingMode UnityEngine.MixedLightingMode UnityEngine.LODFadeMode UnityEngine.LightmapsMode UnityEngine.Rendering.ShaderKeywordType UnityEngine.ColorGamut UnityEngine.Experimental.Rendering.GraphicsFormatUsage UnityEngine.HDRDisplaySupportFlags UnityEngine.Rendering.BuiltinShaderDefine UnityEngine.Rendering.GraphicsTier UnityEngine.Rendering.ReflectionProbeClearFlags UnityEngine.Rendering.ReflectionProbeMode UnityEngine.Rendering.ReflectionProbeRefreshMode UnityEngine.Rendering.ReflectionProbeTimeSlicingMode UnityEngine.Rendering.ReflectionProbeType
using System;

namespace UnityEngine
{
    public enum ColorGamut
    {
        sRGB = 0,
        Rec709 = 1,
        Rec2020 = 2,
        DisplayP3 = 3,
        HDR10 = 4,
        DolbyHDR = 5,
        P3D65G22 = 6,
    }

    [Flags]
    public enum HDRDisplaySupportFlags
    {
        None = 0,
        Supported = 1,
        RuntimeSwitchable = 2,
        AutomaticTonemapping = 4,
    }

    [Flags]
    public enum LightmapsMode
    {
        NonDirectional = 0,
        CombinedDirectional = 1,
        [Obsolete] SeparateDirectional = 2,
        [Obsolete] Single = 0,
        [Obsolete] Dual = 1,
        [Obsolete] Directional = 2,
    }

    public enum LODFadeMode
    {
        None = 0,
        CrossFade = 1,
        SpeedTree = 2,
    }

    public enum MixedLightingMode
    {
        IndirectOnly = 0,
        Shadowmask = 2,
        Subtractive = 1,
    }

    [Flags]
    public enum RenderTextureCreationFlags
    {
        MipMap = 1,
        AutoGenerateMips = 2,
        SRGB = 4,
        EyeTexture = 8,
        EnableRandomWrite = 16,
        CreatedFromScript = 32,
        AllowVerticalFlip = 128,
        NoResolvedColorSurface = 256,
        DynamicallyScalable = 1024,
        BindMS = 2048,
        DynamicallyScalableExplicit = 131072,
    }

    [Flags]
    public enum RenderTextureMemoryless
    {
        None = 0,
        Color = 1,
        Depth = 2,
        MSAA = 4,
    }

    public enum ShadowObjectsFilter
    {
        AllObjects = 0,
        DynamicOnly = 1,
        StaticOnly = 2,
    }

    public enum VRTextureUsage
    {
        None = 0,
        OneEye = 1,
        TwoEyes = 2,
        DeviceSpecific = 3,
    }

}

namespace UnityEngine.Experimental.Rendering
{
    [Flags]
    public enum GraphicsFormatUsage
    {
        None = 0,
        Sample = 1,
        Linear = 2,
        Sparse = 4,
        Render = 16,
        Blend = 32,
        GetPixels = 64,
        SetPixels = 128,
        SetPixels32 = 256,
        ReadPixels = 512,
        LoadStore = 1024,
        MSAA2x = 2048,
        MSAA4x = 4096,
        MSAA8x = 8192,
        StencilSampling = 65536,
    }

}

namespace UnityEngine.Rendering
{
    public enum BatchCullingProjectionType
    {
        Unknown = 0,
        Perspective = 1,
        Orthographic = 2,
    }

    public enum BuiltinShaderDefine
    {
        UNITY_NO_DXT5nm = 0,
        UNITY_NO_RGBM = 1,
        [Obsolete] UNITY_USE_NATIVE_HDR = 2,
        UNITY_ENABLE_REFLECTION_BUFFERS = 3,
        UNITY_FRAMEBUFFER_FETCH_AVAILABLE = 4,
        UNITY_ENABLE_NATIVE_SHADOW_LOOKUPS = 5,
        UNITY_METAL_SHADOWS_USE_POINT_FILTERING = 6,
        UNITY_NO_CUBEMAP_ARRAY = 7,
        UNITY_NO_SCREENSPACE_SHADOWS = 8,
        UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS = 9,
        UNITY_PBS_USE_BRDF1 = 10,
        UNITY_PBS_USE_BRDF2 = 11,
        UNITY_PBS_USE_BRDF3 = 12,
        [Obsolete] UNITY_NO_FULL_STANDARD_SHADER = 13,
        UNITY_SPECCUBE_BOX_PROJECTION = 14,
        UNITY_SPECCUBE_BLENDING = 15,
        UNITY_ENABLE_DETAIL_NORMALMAP = 16,
        SHADER_API_MOBILE = 17,
        SHADER_API_DESKTOP = 18,
        UNITY_HARDWARE_TIER1 = 19,
        UNITY_HARDWARE_TIER2 = 20,
        UNITY_HARDWARE_TIER3 = 21,
        UNITY_COLORSPACE_GAMMA = 22,
        UNITY_LIGHT_PROBE_PROXY_VOLUME = 23,
        [Obsolete] UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS = 24,
        UNITY_LIGHTMAP_DLDR_ENCODING = 25,
        UNITY_LIGHTMAP_RGBM_ENCODING = 26,
        UNITY_LIGHTMAP_FULL_HDR = 27,
        UNITY_VIRTUAL_TEXTURING = 28,
        UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION = 29,
        UNITY_ASTC_NORMALMAP_ENCODING = 30,
        SHADER_API_GLES30 = 31,
        SHADER_API_GLES31 = 32,
        SHADER_API_GLES32 = 33,
        UNITY_UNIFIED_SHADER_PRECISION_MODEL = 34,
        UNITY_PLATFORM_SUPPORTS_WAVE_32 = 35,
        UNITY_PLATFORM_SUPPORTS_WAVE_64 = 36,
        UNITY_NEEDS_RENDERPASS_FBFETCH_FALLBACK = 37,
        UNITY_PLATFORM_SUPPORTS_DEPTH_FETCH = 38,
    }

    public enum CommandBufferExecutionFlags
    {
        None = 0,
        AsyncCompute = 2,
    }

    public enum ComputeQueueType
    {
        Default = 0,
        Background = 1,
        Urgent = 2,
    }

    [Flags]
    public enum CullingOptions
    {
        None = 0,
        ForceEvenIfCameraIsNotActive = 1,
        OcclusionCull = 2,
        NeedsLighting = 4,
        NeedsReflectionProbes = 8,
        Stereo = 16,
        DisablePerObjectCulling = 32,
        ShadowCasters = 64,
    }

    public enum DistanceMetric
    {
        Perspective = 0,
        Orthographic = 1,
        CustomAxis = 2,
    }

    public enum FormatSwizzle
    {
        FormatSwizzleR = 0,
        FormatSwizzleG = 1,
        FormatSwizzleB = 2,
        FormatSwizzleA = 3,
        FormatSwizzle0 = 4,
        FormatSwizzle1 = 5,
    }

    public enum FoveatedRenderingMode
    {
        Disabled = 0,
        Enabled = 1,
    }

    public enum GizmoSubset
    {
        PreImageEffects = 0,
        PostImageEffects = 1,
    }

    public enum GraphicsFenceType
    {
        AsyncQueueSynchronisation = 0,
        CPUSynchronisation = 1,
    }

    public enum GraphicsTier
    {
        Tier1 = 0,
        Tier2 = 1,
        Tier3 = 2,
    }

    public enum LightEvent
    {
        BeforeShadowMap = 0,
        AfterShadowMap = 1,
        BeforeScreenspaceMask = 2,
        AfterScreenspaceMask = 3,
        BeforeShadowMapPass = 4,
        AfterShadowMapPass = 5,
    }

    [Flags]
    public enum PerObjectData
    {
        None = 0,
        LightProbe = 1,
        ReflectionProbes = 2,
        LightProbeProxyVolume = 4,
        Lightmaps = 8,
        LightData = 16,
        MotionVectors = 32,
        LightIndices = 64,
        ReflectionProbeData = 128,
        OcclusionProbe = 256,
        OcclusionProbeProxyVolume = 512,
        ShadowMask = 1024,
    }

    public enum ReflectionProbeClearFlags
    {
        Skybox = 1,
        SolidColor = 2,
    }

    public enum ReflectionProbeMode
    {
        Baked = 0,
        Realtime = 1,
        Custom = 2,
    }

    public enum ReflectionProbeRefreshMode
    {
        OnAwake = 0,
        EveryFrame = 1,
        ViaScripting = 2,
    }

    public enum ReflectionProbeSortingCriteria
    {
        None = 0,
        Importance = 1,
        Size = 2,
        ImportanceThenSize = 3,
    }

    public enum ReflectionProbeTimeSlicingMode
    {
        AllFacesAtOnce = 0,
        IndividualFaces = 1,
        NoTimeSlicing = 2,
    }

    public enum ReflectionProbeType
    {
        Cube = 0,
        Card = 1,
    }

    public enum RenderBufferLoadAction
    {
        Load = 0,
        Clear = 1,
        DontCare = 2,
    }

    public enum RenderBufferStoreAction
    {
        Store = 0,
        Resolve = 1,
        StoreAndResolve = 2,
        DontCare = 3,
    }

    public enum RendererListStatus
    {
        kRendererListInvalid = -2,
        kRendererListProcessing = -1,
        kRendererListEmpty = 0,
        kRendererListPopulated = 1,
    }

    public enum RenderingThreadingMode
    {
        Direct = 0,
        SingleThreaded = 1,
        MultiThreaded = 2,
        LegacyJobified = 3,
        NativeGraphicsJobs = 4,
        NativeGraphicsJobsWithoutRenderThread = 5,
        NativeGraphicsJobsSplitThreading = 6,
    }

    [Flags]
    public enum RenderStateMask
    {
        Nothing = 0,
        Blend = 1,
        Raster = 2,
        Depth = 4,
        Stencil = 8,
        Everything = 15,
    }

    [Flags]
    public enum RenderTargetFlags
    {
        None = 0,
        ReadOnlyDepth = 1,
        ReadOnlyStencil = 2,
        ReadOnlyDepthStencil = 3,
    }

    [Flags]
    public enum RTClearFlags
    {
        None = 0,
        Color = 1,
        Depth = 2,
        Stencil = 4,
        All = 7,
        DepthStencil = 6,
        ColorDepth = 3,
        ColorStencil = 5,
    }

    public enum ShaderKeywordType
    {
        None = 0,
        BuiltinDefault = 2,
        [Obsolete] BuiltinExtra = 6,
        [Obsolete] BuiltinAutoStripped = 10,
        UserDefined = 16,
        Plugin = 32,
    }

    [Flags]
    public enum ShadowMapPass
    {
        PointlightPositiveX = 1,
        PointlightNegativeX = 2,
        PointlightPositiveY = 4,
        PointlightNegativeY = 8,
        PointlightPositiveZ = 16,
        PointlightNegativeZ = 32,
        DirectionalCascade0 = 64,
        DirectionalCascade1 = 128,
        DirectionalCascade2 = 256,
        DirectionalCascade3 = 512,
        Spotlight = 1024,
        AreaLight = 2048,
        Pointlight = 63,
        Directional = 960,
        All = 2047,
    }

    public enum SinglePassStereoMode
    {
        None = 0,
        SideBySide = 1,
        Instancing = 2,
        Multiview = 3,
    }

    [Flags]
    public enum SubPassFlags
    {
        None = 0,
        ReadOnlyDepth = 2,
        ReadOnlyStencil = 4,
        ReadOnlyDepthStencil = 6,
    }

    public enum SynchronisationStageFlags
    {
        VertexProcessing = 1,
        PixelProcessing = 2,
        ComputeProcessing = 4,
        AllGPUOperations = 7,
    }

    [Flags]
    public enum UISubset
    {
        UIToolkit_UGUI = 1,
        LowLevel = 2,
        All = -1,
    }

}

