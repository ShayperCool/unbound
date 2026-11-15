namespace UnityEngine
{
    /// <summary>
    /// How shaders decode the HDR radiance a texture stores as bytes, by its usage mode: the
    /// decode values (<c>unity_Lightmap_HDR</c>, <c>unity_SpecCube0_HDR</c>, a reflection probe's
    /// <c>textureHDRDecodeValues</c>). Double LDR (lightmap usage 1, usage 7) is twice the stored
    /// value, RGBM (lightmap usage 2, usage 5) five times it scaled by its alpha; an sRGB texture
    /// in a linear project raises the factor (and RGBM's exponent) to 2.2. Anything else decodes
    /// as stored. Matches the Unity 6000.0.71f1 Editor (tests/projects/lightmapencoding).
    /// </summary>
    internal static class HdrDecode
    {
        internal const int LightmapDoubleLdr = 1, LightmapRgbm = 2, Rgbm = 5, DoubleLdr = 7;

        // Boxed once: draws bind them without allocating.
        static readonly object s_AsStored = new Vector4(1f, 1f, 0f, 0f);
        static readonly object s_DoubleLdrGamma = new Vector4(2f, 1f, 0f, 0f);
        static readonly object s_DoubleLdrLinear = new Vector4(Mathf.Pow(2f, 2.2f), 1f, 0f, 0f);
        static readonly object s_RgbmGamma = new Vector4(5f, 1f, 0f, 1f);
        static readonly object s_RgbmLinear = new Vector4(Mathf.Pow(5f, 2.2f), 2.2f, 0f, 1f);

        /// <summary>The decode values of <paramref name="usage"/>, boxed; <paramref name="srgb"/>:
        /// the texture's data is sRGB.</summary>
        internal static object Boxed(int usage, bool srgb)
        {
            bool linear = srgb && QualitySettings.activeColorSpace == ColorSpace.Linear;
            return usage switch
            {
                LightmapDoubleLdr or DoubleLdr => linear ? s_DoubleLdrLinear : s_DoubleLdrGamma,
                LightmapRgbm or Rgbm => linear ? s_RgbmLinear : s_RgbmGamma,
                _ => s_AsStored,
            };
        }

        internal static Vector4 Of(int usage, bool srgb) => (Vector4)Boxed(usage, srgb);
    }
}
