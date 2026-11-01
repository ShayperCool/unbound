using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// What a shader samples for a render texture bound with a <see cref="RenderTextureSubElement"/>
    /// (measured: tests/projects/rtdepth, Unity 6000.0.71f1 on OpenGL Core).
    ///
    /// Default and Color sample the color (a depth-only target's Default its depth); Depth samples
    /// the depth as <c>(d, 0, 0, 1)</c>; Stencil samples the stencil where the device supports
    /// stencil sampling (OpenGL Core supports it for no format). An element the target lacks (a
    /// depth-only target's Color, Depth without a depth buffer, unsupported Stencil) binds nothing:
    /// a material then shows the global value, a global binding is the gray default texture.
    /// </summary>
    internal static class TextureElements
    {
        internal enum Binding
        {
            /// <summary>The texture as itself (its color; a depth-only target's depth).</summary>
            Texture,
            /// <summary>Nothing: the element does not exist or cannot be sampled.</summary>
            Nothing,
            /// <summary>The depth buffer of a target that also has color.</summary>
            Depth,
            /// <summary>The stencil of a target (where stencil sampling is supported).</summary>
            Stencil,
        }

        internal static Binding Of(UnityEngine.Texture texture, RenderTextureSubElement element)
        {
            if (texture is not UnityEngine.RenderTexture rt) return Binding.Texture;
            var d = rt.descriptor;
            bool color = d.graphicsFormat != GraphicsFormat.None;
            bool depth = d.depthStencilFormat != GraphicsFormat.None && GraphicsFormatUtility.IsDepthFormat(d.depthStencilFormat);
            switch (element)
            {
                case RenderTextureSubElement.Color:
                    return color ? Binding.Texture : Binding.Nothing;
                case RenderTextureSubElement.Depth:
                    if (!depth) return Binding.Nothing;
                    return color ? Binding.Depth : Binding.Texture;
                case RenderTextureSubElement.Stencil:
                    return GraphicsFormatUtility.IsStencilFormat(d.depthStencilFormat)
                        && UnityEngine.SystemInfo.IsFormatSupported(d.depthStencilFormat, GraphicsFormatUsage.StencilSampling)
                        ? Binding.Stencil : Binding.Nothing;
                default:
                    return Binding.Texture;
            }
        }

        /// <summary>The handle a material (or property block) binds: 0 for nothing, which leaves the
        /// global value to the shader.</summary>
        internal static ulong MaterialHandle(UnityEngine.Texture texture, RenderTextureSubElement element) =>
            Handle(texture, element, 0);

        /// <summary>The handle a global binding sends: the gray default texture for nothing.</summary>
        internal static ulong GlobalHandle(UnityEngine.Texture texture, RenderTextureSubElement element) =>
            Handle(texture, element, TextureHandles.Of(Gray));

        static ulong Handle(UnityEngine.Texture texture, RenderTextureSubElement element, ulong nothing)
        {
            if (texture == null) return 0;
            switch (Of(texture, element))
            {
                case Binding.Nothing: return nothing;
                case Binding.Depth: return DepthHandle((UnityEngine.RenderTexture)texture);
                case Binding.Stencil:
                    UnityEngine.Debug.LogWarning($"Unbound: sampling the stencil of render texture '{texture.name}' is not supported; its color is bound.");
                    return TextureHandles.Of(texture);
                default: return TextureHandles.Of(texture);
            }
        }

        /// <summary>A sampled binding's handle bit naming the render texture's depth element (render
        /// protocol 6, NativeApi 13: docs/RENDER_PROTOCOL.md). It exists only on the wire; getters
        /// return the texture.</summary>
        internal const ulong DepthElement = 1UL << 62;

        /// <summary>The depth element of a target with color and depth.</summary>
        static ulong DepthHandle(UnityEngine.RenderTexture rt) => TextureHandles.Of(rt) | DepthElement;

        static UnityEngine.Texture2D s_Gray;

        /// <summary>The texture a global binding of nothing samples: gray with half alpha, an sRGB
        /// 16x16 texture (measured: 0.2158 linear, alpha 0.502, over 16x16 texels).</summary>
        internal static UnityEngine.Texture2D Gray
        {
            get
            {
                if (s_Gray != null) return s_Gray;
                var t = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RGBA32, false) { name = "UnityGreyHalfAlpha" };
                var pixels = new UnityEngine.Color32[256];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new UnityEngine.Color32(128, 128, 128, 128);
                t.SetPixels32(pixels);
                t.Apply(false, false);
                return s_Gray = t;
            }
        }
    }
}
