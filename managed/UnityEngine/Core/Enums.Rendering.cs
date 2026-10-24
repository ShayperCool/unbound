// Generated from the metadata of Unity 6000.0's reference assemblies (the rendering enums the
// hand-written graphics code uses): names and values must match exactly. Do not edit by hand;
// regenerate with the enum dumper:
//   dotnet run --project managed/Unbound.EnumDumper -- <Editor>/Data/Managed/UnityEngine UnityEngine.RenderTextureMemoryless UnityEngine.VRTextureUsage UnityEngine.RenderTextureCreationFlags UnityEngine.Rendering.RenderBufferLoadAction UnityEngine.Rendering.RenderBufferStoreAction UnityEngine.Rendering.RTClearFlags UnityEngine.Rendering.CommandBufferExecutionFlags UnityEngine.Rendering.FormatSwizzle UnityEngine.Rendering.LightEvent UnityEngine.Rendering.SinglePassStereoMode UnityEngine.Rendering.FoveatedRenderingMode UnityEngine.Rendering.ShadowMapPass UnityEngine.Rendering.RenderTargetFlags UnityEngine.Rendering.ComputeQueueType UnityEngine.Rendering.SynchronisationStageFlags UnityEngine.Rendering.GraphicsFenceType
using System;

namespace UnityEngine.Rendering
{
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

    public enum GraphicsFenceType
    {
        AsyncQueueSynchronisation = 0,
        CPUSynchronisation = 1,
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

    public enum SynchronisationStageFlags
    {
        VertexProcessing = 1,
        PixelProcessing = 2,
        ComputeProcessing = 4,
        AllGPUOperations = 7,
    }

}

namespace UnityEngine
{
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

    public enum VRTextureUsage
    {
        None = 0,
        OneEye = 1,
        TwoEyes = 2,
        DeviceSpecific = 3,
    }

}

