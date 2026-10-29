using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>
    /// What a graphics format is, read from its name (Unity's names spell out channels, bits and
    /// numeric type: <c>R16G16B16A16_SFloat</c>, <c>D24_UNorm_S8_UInt</c>, <c>RGBA_ASTC4X4_SRGB</c>),
    /// and the mappings between texture, render texture and graphics formats.
    /// </summary>
    public partial class GraphicsFormatUtility
    {
        struct Info
        {
            internal string Name;
            internal uint Components;
            internal uint AlphaComponents;
            internal uint BlockSize;
            internal uint BlockWidth;
            internal uint BlockHeight;
            internal int DepthBits;
            internal bool Stencil;
            internal bool Srgb;
            internal bool Float;
            internal bool Half;
            internal bool Compressed;
            internal bool Packed;
            internal bool Signed;
            internal bool Norm;
            internal bool Integer;
        }

        static readonly Dictionary<GraphicsFormat, Info> s_Info = Build();

        static Dictionary<GraphicsFormat, Info> Build()
        {
            var table = new Dictionary<GraphicsFormat, Info>();
            foreach (GraphicsFormat f in Enum.GetValues(typeof(GraphicsFormat)))
            {
                var name = f.ToString();
                if (table.ContainsKey(f)) continue;
                table[f] = Describe(name);
            }
            return table;
        }

        static Info Describe(string name)
        {
            var info = new Info { Name = name, BlockWidth = 1, BlockHeight = 1 };
            info.Srgb = name.Contains("SRGB");
            info.Signed = name.Contains("SNorm") || name.Contains("SInt") || name.Contains("SFloat");
            info.Norm = name.Contains("Norm");
            info.Integer = name.EndsWith("UInt") && !name.StartsWith("D") || name.EndsWith("SInt") || name.Contains("_UIntPack") || name.Contains("_SIntPack");
            info.Float = name.Contains("Float");
            info.Packed = name.Contains("Pack");
            if (name.StartsWith("D16") || name.StartsWith("D24") || name.StartsWith("D32"))
            {
                info.DepthBits = int.Parse(name.Substring(1, 2));
                info.Stencil = name.Contains("S8");
                info.BlockSize = (uint)(info.DepthBits / 8 + (info.Stencil ? 1 : 0));
                if (name.StartsWith("D24")) info.BlockSize = 4;
                if (name.StartsWith("D32") && info.Stencil) info.BlockSize = 8;
                info.Components = 1;
                return info;
            }
            if (name == "S8_UInt")
            {
                info.Stencil = true;
                info.BlockSize = 1;
                info.Components = 1;
                return info;
            }
            foreach (var family in new[] { "DXT", "BC", "PVRTC", "ETC", "EAC", "ASTC" })
            {
                if (!name.Contains("_" + family) && !name.StartsWith(family)) continue;
                info.Compressed = true;
                info.BlockWidth = info.BlockHeight = 4;
                info.BlockSize = 16;
                if (name.Contains("DXT1") || name.Contains("BC4") || name.StartsWith("R_EAC") || name.Contains("RGB_ETC") || name.Contains("RGB_A1_ETC2")) info.BlockSize = 8;
                if (family == "PVRTC") { info.BlockSize = 8; info.BlockWidth = name.Contains("2Bpp") ? 8u : 4u; }
                if (family == "ASTC")
                {
                    int x = name.IndexOf("ASTC", StringComparison.Ordinal) + 4;
                    int sep = name.IndexOf('X', x);
                    info.BlockWidth = uint.Parse(name.Substring(x, sep - x));
                    int end = name.IndexOf('_', sep);
                    info.BlockHeight = uint.Parse(name.Substring(sep + 1, end - sep - 1));
                }
                info.Components = name.StartsWith("RGBA") ? 4u : name.StartsWith("RGB") ? 3u : name.StartsWith("RG") ? 2u : 1u;
                info.AlphaComponents = name.StartsWith("RGBA") || name.Contains("RGB_A1") ? 1u : 0u;
                info.Half = name.Contains("BC6H");
                return info;
            }
            // Channel letters followed by bit counts: R8G8B8A8, B10G11R11, A2B10G10R10, E5B9G9R9.
            var channels = name.Split('_')[0];
            int bits = 0;
            for (int i = 0; i < channels.Length;)
            {
                char c = channels[i++];
                int start = i;
                while (i < channels.Length && char.IsDigit(channels[i])) i++;
                int n = i > start ? int.Parse(channels.Substring(start, i - start)) : 0;
                bits += n;
                if (c == 'E') continue;
                if ("RGBA".IndexOf(c) >= 0) info.Components++;
                if (c == 'A') info.AlphaComponents = 1;
            }
            info.BlockSize = (uint)((bits + 7) / 8);
            info.Half = info.Float && name.Contains("16");
            if (name == "YUV2") { info.BlockSize = 2; info.Components = 3; }
            return info;
        }

        static Info Of(GraphicsFormat format) => s_Info.TryGetValue(format, out var info) ? info : default;

        static GraphicsFormat Named(string name, GraphicsFormat fallback) =>
            Enum.TryParse(name, out GraphicsFormat f) && s_Info.ContainsKey(f) ? f : fallback;

        public static bool IsSRGBFormat(GraphicsFormat format) => Of(format).Srgb;

        public static GraphicsFormat GetSRGBFormat(GraphicsFormat format)
        {
            var name = Of(format).Name;
            if (name == null || name.Contains("SRGB")) return format;
            return Named(name.Replace("_UNorm", "_SRGB"), format);
        }

        public static GraphicsFormat GetLinearFormat(GraphicsFormat format)
        {
            var name = Of(format).Name;
            if (name == null || !name.Contains("SRGB")) return format;
            return Named(name.Replace("_SRGB", "_UNorm"), format);
        }

        public static bool IsDepthFormat(GraphicsFormat format) => Of(format).DepthBits > 0;
        public static bool IsStencilFormat(GraphicsFormat format) => Of(format).Stencil;
        public static bool IsDepthStencilFormat(GraphicsFormat format) => IsDepthFormat(format) || IsStencilFormat(format);
        public static int GetDepthBits(GraphicsFormat format) => Of(format).DepthBits;

        public static GraphicsFormat GetDepthStencilFormat(int depthBits) => GetDepthStencilFormat(depthBits, depthBits >= 24 ? 8 : 0);

        public static GraphicsFormat GetDepthStencilFormat(int minimumDepthBits, int minimumStencilBits)
        {
            if (minimumDepthBits <= 0 && minimumStencilBits <= 0) return GraphicsFormat.None;
            if (minimumDepthBits <= 0) return GraphicsFormat.S8_UInt;
            if (minimumDepthBits <= 16 && minimumStencilBits <= 0) return GraphicsFormat.D16_UNorm;
            if (minimumDepthBits <= 24) return minimumStencilBits > 0 || minimumDepthBits > 16 ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D16_UNorm;
            return minimumStencilBits > 0 ? GraphicsFormat.D32_SFloat_S8_UInt : GraphicsFormat.D32_SFloat;
        }

        /// <summary>The depth-stencil format of a depth buffer bit count (<c>depthBufferBits</c>).</summary>
        internal static GraphicsFormat DepthStencilFormatForBits(int bits) => bits switch
        {
            <= 0 => GraphicsFormat.None,
            <= 16 => GraphicsFormat.D16_UNorm,
            <= 24 => GraphicsFormat.D24_UNorm_S8_UInt,
            _ => GraphicsFormat.D32_SFloat_S8_UInt,
        };

        public static bool IsIEEE754Format(GraphicsFormat format) => Of(format).Float && !Of(format).Packed && !Of(format).Compressed && Of(format).DepthBits == 0;
        public static bool IsFloatFormat(GraphicsFormat format) => Of(format).Float && !Of(format).Half;
        public static bool IsHalfFormat(GraphicsFormat format) => Of(format).Half;
        public static bool IsHDRFormat(GraphicsFormat format) => Of(format).Float && Of(format).DepthBits == 0;
        public static bool IsHDRFormat(TextureFormat format) => IsHDRFormat(GetGraphicsFormat(format, false));
        public static bool IsUnsignedFormat(GraphicsFormat format) => !Of(format).Signed;
        public static bool IsSignedFormat(GraphicsFormat format) => Of(format).Signed;
        public static bool IsNormFormat(GraphicsFormat format) => Of(format).Norm;
        public static bool IsUNormFormat(GraphicsFormat format) => Of(format).Norm && !Of(format).Signed;
        public static bool IsSNormFormat(GraphicsFormat format) => Of(format).Norm && Of(format).Signed;
        public static bool IsIntegerFormat(GraphicsFormat format) => Of(format).Integer;
        public static bool IsUIntFormat(GraphicsFormat format) => Of(format).Integer && !Of(format).Signed;
        public static bool IsSIntFormat(GraphicsFormat format) => Of(format).Integer && Of(format).Signed;
        public static bool IsXRFormat(GraphicsFormat format) => Of(format).Name?.Contains("_XR") == true;
        public static bool IsCompressedFormat(GraphicsFormat format) => Of(format).Compressed;
        public static bool IsPackedFormat(GraphicsFormat format) => Of(format).Packed;
        public static bool Is16BitPackedFormat(GraphicsFormat format) => Of(format).Name?.Contains("Pack16") == true;
        public static bool IsDXTCFormat(GraphicsFormat format) => Of(format).Name?.Contains("DXT") == true;
        public static bool IsRGTCFormat(GraphicsFormat format) => Of(format).Name?.Contains("BC4") == true || Of(format).Name?.Contains("BC5") == true;
        public static bool IsBPTCFormat(GraphicsFormat format) => Of(format).Name?.Contains("BC6H") == true || Of(format).Name?.Contains("BC7") == true;
        public static bool IsBCFormat(GraphicsFormat format) => IsDXTCFormat(format) || IsRGTCFormat(format) || IsBPTCFormat(format);
        public static bool IsPVRTCFormat(GraphicsFormat format) => Of(format).Name?.Contains("PVRTC") == true;
        public static bool IsETCFormat(GraphicsFormat format) => Of(format).Name?.Contains("_ETC") == true;
        public static bool IsEACFormat(GraphicsFormat format) => Of(format).Name?.Contains("_EAC") == true;
        public static bool IsASTCFormat(GraphicsFormat format) => Of(format).Name?.Contains("ASTC") == true;
        public static bool HasAlphaChannel(GraphicsFormat format) => Of(format).AlphaComponents > 0;
        [Unbound.EditorFaithful("6000.0's GraphicsFormat has no alpha-only format (Alpha8 is R8_UNorm), so no format is")]
        public static bool IsAlphaOnlyFormat(GraphicsFormat format) => false;
        public static uint GetColorComponentCount(GraphicsFormat format) => Of(format).Components - Of(format).AlphaComponents;
        public static uint GetAlphaComponentCount(GraphicsFormat format) => Of(format).AlphaComponents;
        public static uint GetComponentCount(GraphicsFormat format) => Of(format).Components;
        public static string GetFormatString(GraphicsFormat format) => Of(format).Name ?? format.ToString();
        public static uint GetBlockSize(GraphicsFormat format) => Of(format).BlockSize;
        public static uint GetBlockWidth(GraphicsFormat format) => Of(format).BlockWidth;
        public static uint GetBlockHeight(GraphicsFormat format) => Of(format).BlockHeight;

        public static uint ComputeMipmapSize(int width, int height, GraphicsFormat format) => ComputeMipmapSize(width, height, 1, format);

        public static uint ComputeMipmapSize(int width, int height, int depth, GraphicsFormat format)
        {
            var info = Of(format);
            uint w = (uint)(Math.Max(1, width) + info.BlockWidth - 1) / Math.Max(1, info.BlockWidth);
            uint h = (uint)(Math.Max(1, height) + info.BlockHeight - 1) / Math.Max(1, info.BlockHeight);
            return w * h * (uint)Math.Max(1, depth) * info.BlockSize;
        }

        public static uint ComputeMipChainSize(int width, int height, GraphicsFormat format, [Internal.DefaultValue("-1")] int mipCount) =>
            ComputeMipChainSize(width, height, 1, format, mipCount);

        public static uint ComputeMipChainSize(int width, int height, int depth, GraphicsFormat format, [Internal.DefaultValue("-1")] int mipCount)
        {
            int levels = mipCount <= 0 ? 1 + (int)Math.Floor(Math.Log(Math.Max(width, Math.Max(height, depth)), 2)) : mipCount;
            uint total = 0;
            for (int i = 0; i < levels; i++)
                total += ComputeMipmapSize(Math.Max(1, width >> i), Math.Max(1, height >> i), Math.Max(1, depth >> i), format);
            return total;
        }

        // ------------------------------------------------------------ texture formats

        static readonly (TextureFormat Texture, GraphicsFormat Linear, GraphicsFormat Srgb)[] s_TextureFormats =
        {
            (TextureFormat.Alpha8, GraphicsFormat.R8_UNorm, GraphicsFormat.R8_UNorm),
            (TextureFormat.ARGB4444, GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormat.B4G4R4A4_UNormPack16),
            (TextureFormat.RGB24, GraphicsFormat.R8G8B8_UNorm, GraphicsFormat.R8G8B8_SRGB),
            (TextureFormat.RGBA32, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.R8G8B8A8_SRGB),
            (TextureFormat.ARGB32, GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB),
            (TextureFormat.RGB565, GraphicsFormat.B5G6R5_UNormPack16, GraphicsFormat.B5G6R5_UNormPack16),
            (TextureFormat.R16, GraphicsFormat.R16_UNorm, GraphicsFormat.R16_UNorm),
            (TextureFormat.DXT1, GraphicsFormat.RGBA_DXT1_UNorm, GraphicsFormat.RGBA_DXT1_SRGB),
            (TextureFormat.DXT5, GraphicsFormat.RGBA_DXT5_UNorm, GraphicsFormat.RGBA_DXT5_SRGB),
            (TextureFormat.RGBA4444, GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.R4G4B4A4_UNormPack16),
            (TextureFormat.BGRA32, GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB),
            (TextureFormat.RHalf, GraphicsFormat.R16_SFloat, GraphicsFormat.R16_SFloat),
            (TextureFormat.RGHalf, GraphicsFormat.R16G16_SFloat, GraphicsFormat.R16G16_SFloat),
            (TextureFormat.RGBAHalf, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.R16G16B16A16_SFloat),
            (TextureFormat.RFloat, GraphicsFormat.R32_SFloat, GraphicsFormat.R32_SFloat),
            (TextureFormat.RGFloat, GraphicsFormat.R32G32_SFloat, GraphicsFormat.R32G32_SFloat),
            (TextureFormat.RGBAFloat, GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormat.R32G32B32A32_SFloat),
            (TextureFormat.RGB9e5Float, GraphicsFormat.E5B9G9R9_UFloatPack32, GraphicsFormat.E5B9G9R9_UFloatPack32),
            (TextureFormat.BC6H, GraphicsFormat.RGB_BC6H_UFloat, GraphicsFormat.RGB_BC6H_UFloat),
            (TextureFormat.BC7, GraphicsFormat.RGBA_BC7_UNorm, GraphicsFormat.RGBA_BC7_SRGB),
            (TextureFormat.BC4, GraphicsFormat.R_BC4_UNorm, GraphicsFormat.R_BC4_UNorm),
            (TextureFormat.BC5, GraphicsFormat.RG_BC5_UNorm, GraphicsFormat.RG_BC5_UNorm),
            (TextureFormat.ETC_RGB4, GraphicsFormat.RGB_ETC_UNorm, GraphicsFormat.RGB_ETC_UNorm),
            (TextureFormat.ETC2_RGB, GraphicsFormat.RGB_ETC2_UNorm, GraphicsFormat.RGB_ETC2_SRGB),
            (TextureFormat.ETC2_RGBA1, GraphicsFormat.RGB_A1_ETC2_UNorm, GraphicsFormat.RGB_A1_ETC2_SRGB),
            (TextureFormat.ETC2_RGBA8, GraphicsFormat.RGBA_ETC2_UNorm, GraphicsFormat.RGBA_ETC2_SRGB),
            (TextureFormat.ASTC_4x4, GraphicsFormat.RGBA_ASTC4X4_UNorm, GraphicsFormat.RGBA_ASTC4X4_SRGB),
            (TextureFormat.ASTC_5x5, GraphicsFormat.RGBA_ASTC5X5_UNorm, GraphicsFormat.RGBA_ASTC5X5_SRGB),
            (TextureFormat.ASTC_6x6, GraphicsFormat.RGBA_ASTC6X6_UNorm, GraphicsFormat.RGBA_ASTC6X6_SRGB),
            (TextureFormat.ASTC_8x8, GraphicsFormat.RGBA_ASTC8X8_UNorm, GraphicsFormat.RGBA_ASTC8X8_SRGB),
            (TextureFormat.ASTC_10x10, GraphicsFormat.RGBA_ASTC10X10_UNorm, GraphicsFormat.RGBA_ASTC10X10_SRGB),
            (TextureFormat.ASTC_12x12, GraphicsFormat.RGBA_ASTC12X12_UNorm, GraphicsFormat.RGBA_ASTC12X12_SRGB),
            (TextureFormat.R8, GraphicsFormat.R8_UNorm, GraphicsFormat.R8_UNorm),
            (TextureFormat.RG16, GraphicsFormat.R8G8_UNorm, GraphicsFormat.R8G8_UNorm),
            (TextureFormat.RG32, GraphicsFormat.R16G16_UNorm, GraphicsFormat.R16G16_UNorm),
            (TextureFormat.RGB48, GraphicsFormat.R16G16B16_UNorm, GraphicsFormat.R16G16B16_UNorm),
            (TextureFormat.RGBA64, GraphicsFormat.R16G16B16A16_UNorm, GraphicsFormat.R16G16B16A16_UNorm),
        };

        public static GraphicsFormat GetGraphicsFormat(TextureFormat format, bool isSRGB)
        {
            foreach (var (t, linear, srgb) in s_TextureFormats)
                if (t == format) return isSRGB ? srgb : linear;
            return GraphicsFormat.None;
        }

        public static TextureFormat GetTextureFormat(GraphicsFormat format)
        {
            foreach (var (t, linear, srgb) in s_TextureFormats)
                if (linear == format || srgb == format) return t;
            return 0;
        }

        // ------------------------------------------------------------ render texture formats

        public static GraphicsFormat GetGraphicsFormat(RenderTextureFormat format, bool isSRGB)
        {
            switch (format)
            {
                case RenderTextureFormat.ARGB32:
                case RenderTextureFormat.Default:
                    return isSRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
                case RenderTextureFormat.BGRA32: return isSRGB ? GraphicsFormat.B8G8R8A8_SRGB : GraphicsFormat.B8G8R8A8_UNorm;
                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.DefaultHDR: return GraphicsFormat.R16G16B16A16_SFloat;
                case RenderTextureFormat.ARGBFloat: return GraphicsFormat.R32G32B32A32_SFloat;
                case RenderTextureFormat.RGFloat: return GraphicsFormat.R32G32_SFloat;
                case RenderTextureFormat.RGHalf: return GraphicsFormat.R16G16_SFloat;
                case RenderTextureFormat.RFloat: return GraphicsFormat.R32_SFloat;
                case RenderTextureFormat.RHalf: return GraphicsFormat.R16_SFloat;
                case RenderTextureFormat.R8: return GraphicsFormat.R8_UNorm;
                case RenderTextureFormat.R16: return GraphicsFormat.R16_UNorm;
                case RenderTextureFormat.RG16: return GraphicsFormat.R8G8_UNorm;
                case RenderTextureFormat.RG32: return GraphicsFormat.R16G16_UNorm;
                case RenderTextureFormat.ARGB64: return GraphicsFormat.R16G16B16A16_UNorm;
                case RenderTextureFormat.RGBAUShort: return GraphicsFormat.R16G16B16A16_UInt;
                case RenderTextureFormat.ARGBInt: return GraphicsFormat.R32G32B32A32_SInt;
                case RenderTextureFormat.RGInt: return GraphicsFormat.R32G32_SInt;
                case RenderTextureFormat.RInt: return GraphicsFormat.R32_SInt;
                case RenderTextureFormat.RGB565: return GraphicsFormat.B5G6R5_UNormPack16;
                case RenderTextureFormat.ARGB4444: return GraphicsFormat.B4G4R4A4_UNormPack16;
                case RenderTextureFormat.ARGB1555: return GraphicsFormat.B5G5R5A1_UNormPack16;
                case RenderTextureFormat.ARGB2101010: return GraphicsFormat.A2B10G10R10_UNormPack32;
                case RenderTextureFormat.RGB111110Float: return GraphicsFormat.B10G11R11_UFloatPack32;
                case RenderTextureFormat.Depth:
                case RenderTextureFormat.Shadowmap: return GraphicsFormat.None;
                default: return GraphicsFormat.R8G8B8A8_UNorm;
            }
        }

        /// <summary><c>Default</c> read-write is sRGB in linear-space projects.</summary>
        public static GraphicsFormat GetGraphicsFormat(RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            bool srgb = readWrite == RenderTextureReadWrite.sRGB
                || readWrite == RenderTextureReadWrite.Default && QualitySettings.activeColorSpace == ColorSpace.Linear;
            return GetGraphicsFormat(format, srgb);
        }

        public static RenderTextureFormat GetRenderTextureFormat(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R8G8B8A8_SRGB:
                case GraphicsFormat.R8G8B8A8_UNorm: return RenderTextureFormat.ARGB32;
                case GraphicsFormat.B8G8R8A8_SRGB:
                case GraphicsFormat.B8G8R8A8_UNorm: return RenderTextureFormat.BGRA32;
                case GraphicsFormat.R16G16B16A16_SFloat: return RenderTextureFormat.ARGBHalf;
                case GraphicsFormat.R32G32B32A32_SFloat: return RenderTextureFormat.ARGBFloat;
                case GraphicsFormat.R32G32_SFloat: return RenderTextureFormat.RGFloat;
                case GraphicsFormat.R16G16_SFloat: return RenderTextureFormat.RGHalf;
                case GraphicsFormat.R32_SFloat: return RenderTextureFormat.RFloat;
                case GraphicsFormat.R16_SFloat: return RenderTextureFormat.RHalf;
                case GraphicsFormat.R8_UNorm: return RenderTextureFormat.R8;
                case GraphicsFormat.R16_UNorm: return RenderTextureFormat.R16;
                case GraphicsFormat.R8G8_UNorm: return RenderTextureFormat.RG16;
                case GraphicsFormat.R16G16_UNorm: return RenderTextureFormat.RG32;
                case GraphicsFormat.R16G16B16A16_UNorm: return RenderTextureFormat.ARGB64;
                case GraphicsFormat.R16G16B16A16_UInt: return RenderTextureFormat.RGBAUShort;
                case GraphicsFormat.R32G32B32A32_SInt: return RenderTextureFormat.ARGBInt;
                case GraphicsFormat.R32G32_SInt: return RenderTextureFormat.RGInt;
                case GraphicsFormat.R32_SInt: return RenderTextureFormat.RInt;
                case GraphicsFormat.B5G6R5_UNormPack16: return RenderTextureFormat.RGB565;
                case GraphicsFormat.B4G4R4A4_UNormPack16: return RenderTextureFormat.ARGB4444;
                case GraphicsFormat.B5G5R5A1_UNormPack16: return RenderTextureFormat.ARGB1555;
                case GraphicsFormat.A2B10G10R10_UNormPack32: return RenderTextureFormat.ARGB2101010;
                case GraphicsFormat.B10G11R11_UFloatPack32: return RenderTextureFormat.RGB111110Float;
                case GraphicsFormat.None: return RenderTextureFormat.Depth;
                default: return RenderTextureFormat.ARGB32;
            }
        }
    }
}
