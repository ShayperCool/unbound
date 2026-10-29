using System;
using System.Collections.Generic;
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
        public static bool preserveFramebufferAlpha => UnityEditor.ProjectSettingsText.Int("preserveFramebufferAlpha", 0) != 0;
    }

    /// <summary>The frame debugger: Unbound has none.</summary>
    public static partial class FrameDebugger
    {
        [Unbound.EditorFaithful("the frame debugger is only on while its window captures a frame, never in a game's Play mode run")]
        public static bool enabled => false;
        internal static bool IsLocalEnabled() => false;
        internal static bool IsRemoteEnabled() => false;
    }
}

namespace UnityEngine.Rendering
{
    public sealed partial class GraphicsSettings
    {
        /// <summary>The target's and its graphics tier's defines (what shaders compile with); the
        /// Linux Editor's OpenGLCore set until the session says.</summary>
        static HashSet<BuiltinShaderDefine> s_PlatformDefines = new()
        {
            BuiltinShaderDefine.SHADER_API_DESKTOP,
            BuiltinShaderDefine.UNITY_PBS_USE_BRDF1,
            BuiltinShaderDefine.UNITY_SPECCUBE_BOX_PROJECTION,
            BuiltinShaderDefine.UNITY_SPECCUBE_BLENDING,
            BuiltinShaderDefine.UNITY_ENABLE_DETAIL_NORMALMAP,
            BuiltinShaderDefine.UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS,
            BuiltinShaderDefine.UNITY_LIGHT_PROBE_PROXY_VOLUME,
        };

        internal static void SetPlatformShaderDefines(IEnumerable<string> names)
        {
            s_PlatformDefines = new HashSet<BuiltinShaderDefine>();
            foreach (var name in names)
                if (Enum.TryParse(name, out BuiltinShaderDefine define)) s_PlatformDefines.Add(define);
        }

        /// <summary>The built-in shader defines (measured; the color space one follows the project,
        /// the platform's ones the target's tier settings).</summary>
        public static bool HasShaderDefine(BuiltinShaderDefine defineHash) => defineHash switch
        {
            BuiltinShaderDefine.UNITY_ENABLE_REFLECTION_BUFFERS => true,
            BuiltinShaderDefine.UNITY_COLORSPACE_GAMMA => QualitySettings.activeColorSpace == ColorSpace.Gamma,
            BuiltinShaderDefine.UNITY_LIGHTMAP_FULL_HDR => true,
            BuiltinShaderDefine.UNITY_PLATFORM_SUPPORTS_DEPTH_FETCH => true,
            _ => s_PlatformDefines.Contains(defineHash),
        };

        public static bool HasShaderDefine(GraphicsTier tier, BuiltinShaderDefine defineHash) => HasShaderDefine(defineHash);
    }

    public partial class CommandBuffer
    {
        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void BeginSample(Profiling.CustomSampler sampler) { }
        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void EndSample(Profiling.CustomSampler sampler) { }
    }
}
