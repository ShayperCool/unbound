using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class QualitySettings
    {
        public static bool enableLODCrossFade { get; set; } = true;
    }

    public partial class Graphics
    {
        /// <summary>Player Settings' "Preserve framebuffer alpha" (off by default).</summary>
        public static bool preserveFramebufferAlpha => false;
    }

    /// <summary>The frame debugger: Unbound has none.</summary>
    public static partial class FrameDebugger
    {
        public static bool enabled => false;
        internal static bool IsLocalEnabled() => false;
        internal static bool IsRemoteEnabled() => false;
    }
}

namespace UnityEngine.Rendering
{
    public sealed partial class GraphicsSettings
    {
        /// <summary>The built-in shader defines of the Linux Editor's OpenGLCore tier settings
        /// (measured; the color space one follows the project).</summary>
        public static bool HasShaderDefine(BuiltinShaderDefine defineHash) => defineHash switch
        {
            BuiltinShaderDefine.UNITY_ENABLE_REFLECTION_BUFFERS => true,
            BuiltinShaderDefine.UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS => true,
            BuiltinShaderDefine.UNITY_PBS_USE_BRDF1 => true,
            BuiltinShaderDefine.UNITY_SPECCUBE_BOX_PROJECTION => true,
            BuiltinShaderDefine.UNITY_SPECCUBE_BLENDING => true,
            BuiltinShaderDefine.UNITY_ENABLE_DETAIL_NORMALMAP => true,
            BuiltinShaderDefine.SHADER_API_DESKTOP => true,
            BuiltinShaderDefine.UNITY_COLORSPACE_GAMMA => QualitySettings.activeColorSpace == ColorSpace.Gamma,
            BuiltinShaderDefine.UNITY_LIGHT_PROBE_PROXY_VOLUME => true,
            BuiltinShaderDefine.UNITY_LIGHTMAP_FULL_HDR => true,
            BuiltinShaderDefine.UNITY_PLATFORM_SUPPORTS_DEPTH_FETCH => true,
            _ => false,
        };

        public static bool HasShaderDefine(GraphicsTier tier, BuiltinShaderDefine defineHash) => HasShaderDefine(defineHash);
    }

    public partial class CommandBuffer
    {
        public void BeginSample(Profiling.CustomSampler sampler) { }
        public void EndSample(Profiling.CustomSampler sampler) { }
    }
}
