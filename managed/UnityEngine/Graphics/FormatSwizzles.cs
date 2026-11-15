using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>
    /// Which channel of a format each shader channel reads, and the crunched texture formats.
    /// Matches the Unity 6000.0.71f1 Editor for every GraphicsFormat and TextureFormat,
    /// tests/projects/formatmapping swizzle.*, swizzleTexture.*, crunch.*.
    /// </summary>
    public partial class GraphicsFormatUtility
    {
        public static FormatSwizzle GetSwizzleR(GraphicsFormat format) => Swizzle(format, 0);
        public static FormatSwizzle GetSwizzleR(TextureFormat format) => GetSwizzleR(GetGraphicsFormat(format, isSRGB: false));
        public static FormatSwizzle GetSwizzleG(GraphicsFormat format) => Swizzle(format, 1);
        public static FormatSwizzle GetSwizzleG(TextureFormat format) => GetSwizzleG(GetGraphicsFormat(format, isSRGB: false));
        public static FormatSwizzle GetSwizzleB(GraphicsFormat format) => Swizzle(format, 2);
        public static FormatSwizzle GetSwizzleB(TextureFormat format) => GetSwizzleB(GetGraphicsFormat(format, isSRGB: false));
        public static FormatSwizzle GetSwizzleA(GraphicsFormat format) => Swizzle(format, 3);
        public static FormatSwizzle GetSwizzleA(TextureFormat format) => GetSwizzleA(GetGraphicsFormat(format, isSRGB: false));

        public static bool IsCrunchFormat(TextureFormat format) =>
            format == TextureFormat.DXT1Crunched || format == TextureFormat.DXT5Crunched
            || format == TextureFormat.ETC_RGB4Crunched || format == TextureFormat.ETC2_RGBA8Crunched;

        static readonly FormatSwizzle[] s_Rgba = { FormatSwizzle.FormatSwizzleR, FormatSwizzle.FormatSwizzleG, FormatSwizzle.FormatSwizzleB, FormatSwizzle.FormatSwizzleA };
        static readonly FormatSwizzle[] s_Bgra = { FormatSwizzle.FormatSwizzleB, FormatSwizzle.FormatSwizzleG, FormatSwizzle.FormatSwizzleR, FormatSwizzle.FormatSwizzleA };

        /// <summary>
        /// A format's channels read in order (blue first for the blue-first families); the
        /// channels it lacks read 0, and a missing alpha 1. Formats without color (None, the Auto
        /// formats, YUV2, an unknown value) read (0, 0, 0, 1), depth and stencil formats R, G, B, A.
        /// The blue-first families are those measured so: B8G8R8(A8), B4G4R4A4, B5G5R5A1,
        /// A2R10G10B10, A10R10G10B10 and R10G10B10_XR; the other packed formats (B5G6R5,
        /// B10G11R11, E5B9G9R9, A1R5G5B5, A2B10G10R10) read in red-first order. RGBA_DXT1
        /// reads as three channels, RGB_A1_ETC2 as four. The TextureFormat overloads go through
        /// GetGraphicsFormat, so they share its known differences (formatmapping residuals.json).
        /// </summary>
        static FormatSwizzle Swizzle(GraphicsFormat format, int channel)
        {
            var info = Of(format);
            var name = info.Name;
            if (name == null || format == GraphicsFormat.None || name.EndsWith("Auto", System.StringComparison.Ordinal) || name == "YUV2")
                return channel == 3 ? FormatSwizzle.FormatSwizzle1 : FormatSwizzle.FormatSwizzle0;
            if (info.DepthBits > 0 || info.Stencil) return s_Rgba[channel];
            // RGBA_DXT1 reads as three channels; RGB_A1_ETC2's one-bit alpha is read.
            bool dxt1 = name.StartsWith("RGBA_DXT1", System.StringComparison.Ordinal);
            uint components = dxt1 ? 3u : info.Components;
            bool alpha = !dxt1 && (components >= 4 || info.AlphaComponents > 0);
            bool blueFirst = name.StartsWith("B8G8R8", System.StringComparison.Ordinal) || name.StartsWith("B4G4R4A4", System.StringComparison.Ordinal) || name.StartsWith("B5G5R5A1", System.StringComparison.Ordinal)
                || name.StartsWith("A2R10G10B10", System.StringComparison.Ordinal) || name.StartsWith("A10R10G10B10", System.StringComparison.Ordinal) || name.StartsWith("R10G10B10_XR", System.StringComparison.Ordinal);
            if (channel == 3) return alpha ? FormatSwizzle.FormatSwizzleA : FormatSwizzle.FormatSwizzle1;
            if (channel >= components) return FormatSwizzle.FormatSwizzle0;
            return blueFirst ? s_Bgra[channel] : s_Rgba[channel];
        }
    }
}
