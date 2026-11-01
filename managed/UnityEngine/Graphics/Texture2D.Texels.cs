using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// A Texture2D's texels per format: the stored bytes, the CPU cell Unbound keeps for them, and
    /// what each pixel reader returns (tests/projects/texturepixels, Unity 6000.0.71f1).
    ///
    /// Byte formats keep a Color32 cell holding each channel as the GPU samples it: R8 (r, 0, 0, 255),
    /// RG16 (r, g, 0, 255), Alpha8 (255, 255, 255, a), RGB24 alpha 255, a 4-bit channel n as 17 n,
    /// RGB565's 5- and 6-bit channels widened by repeating their top bits. R16 and the float
    /// formats keep a Color cell (R16: n / 65535 in single precision, exact for every n).
    ///
    /// Readers, for a stored 8-bit channel b:
    /// <list type="bullet">
    /// <item><c>GetPixels32</c>: R8 (r, 255, 255, 255), RG16 (r, g, 255, 255), Alpha8 (0, 0, 0, a),
    /// R16 round(n / 257) in r, g and b with alpha 255, 4-bit 17 n, RGB565 the widened bits.</item>
    /// <item><c>GetPixels</c>: b × (1 / 255) of those bytes; Alpha8 (1, 1, 1, a / 255); RGB565 n × (1 / 31)
    /// and n × (1 / 63); R16 (n × (1 / 65535), 1, 1, 1).</item>
    /// <item><c>GetPixel</c>: b / 255 of the <c>GetPixels32</c> bytes; R16 (n / 65535, 1, 1, 1); the
    /// 4-bit formats and RGB565 read as <c>GetPixels</c> does.</item>
    /// </list>
    /// </summary>
    internal static class Texture2DTexels
    {
        /// <summary>Formats whose CPU cells are floats.</summary>
        internal static bool FloatStored(TextureFormat format) => GraphicsFormatUtility.IsHDRFormat(format) || format == TextureFormat.R16;

        static bool Packed16(TextureFormat format) => format is TextureFormat.RGBA4444 or TextureFormat.ARGB4444 or TextureFormat.RGB565;

        /// <summary>Bytes of one texel, or 0 for formats whose texels Unbound does not store.</summary>
        internal static int TexelBytes(TextureFormat format) =>
            format is TextureFormat.RG16 or TextureFormat.R16 || Packed16(format) ? 2 : PixelFormats.Size(format, 1, 1);

        /// <summary>Bytes of a level, or 0 for formats whose texels Unbound does not store.</summary>
        internal static int Size(TextureFormat format, int width, int height) => TexelBytes(format) * width * height;

        /// <summary>Whether Unbound decodes and encodes the format's texels itself.</summary>
        internal static bool Coded(TextureFormat format) => format switch
        {
            TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32 or TextureFormat.RGB24 or TextureFormat.Alpha8
                or TextureFormat.R8 or TextureFormat.RG16 or TextureFormat.R16 or TextureFormat.RGBA4444 or TextureFormat.ARGB4444
                or TextureFormat.RGB565 => true,
            _ => FloatStored(format) && PixelFormats.Size(format, 1, 1) > 0,
        };

        static byte Nibble17(byte b) => (byte)((b + 8) / 17 * 17);
        static byte Widen5(int n) => (byte)((n << 3) | (n >> 2));
        static byte Widen6(int n) => (byte)((n << 2) | (n >> 4));
        static int Quantize(float v, int max) => (int)Math.Floor(Mathf.Clamp01(v) * max + 0.5f);
        static int R16Of(Color cell) => (int)Math.Round(cell.r * 65535.0);
        static Color R16Cell(int n) => new Color(n / 65535f, 1f, 1f, 1f);

        /// <summary>The cell a byte-channel color becomes (channels the format lacks dropped, fewer
        /// bits rounded to the nearest value).</summary>
        internal static Color32 Canonical(TextureFormat format, Color32 c)
        {
            switch (format)
            {
                case TextureFormat.RGB24: c.a = 255; break;
                case TextureFormat.Alpha8: c.r = c.g = c.b = 255; break;
                case TextureFormat.R8: c.g = c.b = 0; c.a = 255; break;
                case TextureFormat.RG16: c.b = 0; c.a = 255; break;
                case TextureFormat.RGBA4444:
                case TextureFormat.ARGB4444:
                    c = new Color32(Nibble17(c.r), Nibble17(c.g), Nibble17(c.b), Nibble17(c.a));
                    break;
                case TextureFormat.RGB565:
                    c = new Color32(Widen5((c.r * 31 + 127) / 255), Widen6((c.g * 63 + 127) / 255), Widen5((c.b * 31 + 127) / 255), 255);
                    break;
            }
            return c;
        }

        /// <summary>The cell a set color becomes; a byte half way between two values rounds upward.</summary>
        internal static Color32 Quantize(TextureFormat format, Color c)
        {
            switch (format)
            {
                case TextureFormat.RGBA4444:
                case TextureFormat.ARGB4444:
                    return new Color32((byte)(17 * Quantize(c.r, 15)), (byte)(17 * Quantize(c.g, 15)), (byte)(17 * Quantize(c.b, 15)), (byte)(17 * Quantize(c.a, 15)));
                case TextureFormat.RGB565:
                    return new Color32(Widen5(Quantize(c.r, 31)), Widen6(Quantize(c.g, 63)), Widen5(Quantize(c.b, 31)), 255);
            }
            return Canonical(format, new Color32((byte)Quantize(c.r, 255), (byte)Quantize(c.g, 255), (byte)Quantize(c.b, 255), (byte)Quantize(c.a, 255)));
        }

        /// <summary>The float cell a set color becomes (R16: the nearest 16-bit value).</summary>
        internal static Color QuantizeFloat(TextureFormat format, Color c) =>
            format == TextureFormat.R16 ? R16Cell(Quantize(c.r, 65535)) : PixelFormats.QuantizeFloat(format, c);

        internal static Color32 DecodeCell(TextureFormat format, ReadOnlySpan<byte> t)
        {
            switch (format)
            {
                case TextureFormat.RGBA32: return new Color32(t[0], t[1], t[2], t[3]);
                case TextureFormat.ARGB32: return new Color32(t[1], t[2], t[3], t[0]);
                case TextureFormat.BGRA32: return new Color32(t[2], t[1], t[0], t[3]);
                case TextureFormat.RGB24: return new Color32(t[0], t[1], t[2], 255);
                case TextureFormat.Alpha8: return new Color32(255, 255, 255, t[0]);
                case TextureFormat.R8: return new Color32(t[0], 0, 0, 255);
                case TextureFormat.RG16: return new Color32(t[0], t[1], 0, 255);
            }
            int v = t[0] | t[1] << 8;
            return format switch
            {
                TextureFormat.RGBA4444 => new Color32((byte)(17 * (v >> 12)), (byte)(17 * ((v >> 8) & 15)), (byte)(17 * ((v >> 4) & 15)), (byte)(17 * (v & 15))),
                TextureFormat.ARGB4444 => new Color32((byte)(17 * ((v >> 8) & 15)), (byte)(17 * ((v >> 4) & 15)), (byte)(17 * (v & 15)), (byte)(17 * (v >> 12))),
                TextureFormat.RGB565 => new Color32(Widen5(v >> 11), Widen6((v >> 5) & 63), Widen5(v & 31), 255),
                _ => throw new UnityException($"Unbound: Texture2D texels in {format} are not supported."),
            };
        }

        internal static void EncodeCell(TextureFormat format, Color32 c, Span<byte> t)
        {
            switch (format)
            {
                case TextureFormat.RGBA32: t[0] = c.r; t[1] = c.g; t[2] = c.b; t[3] = c.a; return;
                case TextureFormat.ARGB32: t[0] = c.a; t[1] = c.r; t[2] = c.g; t[3] = c.b; return;
                case TextureFormat.BGRA32: t[0] = c.b; t[1] = c.g; t[2] = c.r; t[3] = c.a; return;
                case TextureFormat.RGB24: t[0] = c.r; t[1] = c.g; t[2] = c.b; return;
                case TextureFormat.Alpha8: t[0] = c.a; return;
                case TextureFormat.R8: t[0] = c.r; return;
                case TextureFormat.RG16: t[0] = c.r; t[1] = c.g; return;
            }
            int v = format switch
            {
                TextureFormat.RGBA4444 => (c.r / 17) << 12 | (c.g / 17) << 8 | (c.b / 17) << 4 | c.a / 17,
                TextureFormat.ARGB4444 => (c.a / 17) << 12 | (c.r / 17) << 8 | (c.g / 17) << 4 | c.b / 17,
                TextureFormat.RGB565 => (c.r >> 3) << 11 | (c.g >> 2) << 5 | c.b >> 3,
                _ => throw new UnityException($"Unbound: Texture2D texels in {format} are not supported."),
            };
            t[0] = (byte)v;
            t[1] = (byte)(v >> 8);
        }

        internal static Color DecodeFloatCell(TextureFormat format, ReadOnlySpan<byte> t)
        {
            if (format == TextureFormat.R16) return R16Cell(t[0] | t[1] << 8);
            return PixelFormats.QuantizeFloat(format, PixelFormats.DecodeFloatTexel(format, t));
        }

        internal static void EncodeFloatCell(TextureFormat format, Color c, Span<byte> t)
        {
            if (format == TextureFormat.R16)
            {
                int n = R16Of(c);
                t[0] = (byte)n;
                t[1] = (byte)(n >> 8);
                return;
            }
            PixelFormats.EncodeFloatTexel(format, c, t);
        }

        internal static Color32 Pixel32(TextureFormat format, Color32 cell) => format switch
        {
            TextureFormat.R8 => new Color32(cell.r, 255, 255, 255),
            TextureFormat.RG16 => new Color32(cell.r, cell.g, 255, 255),
            TextureFormat.Alpha8 => new Color32(0, 0, 0, cell.a),
            _ => cell,
        };

        internal static Color32 Pixel32(TextureFormat format, Color cell)
        {
            if (format != TextureFormat.R16) return cell;
            byte v = (byte)((R16Of(cell) * 255 + 32767) / 65535);
            return new Color32(v, v, v, 255);
        }

        const float Scale = 1f / 255f;

        /// <summary>As <c>GetPixels</c> reads a byte cell.</summary>
        internal static Color Pixels(TextureFormat format, Color32 cell)
        {
            if (format == TextureFormat.Alpha8) return new Color(1f, 1f, 1f, cell.a / 255f);
            if (format == TextureFormat.RGB565)
                return new Color((cell.r >> 3) * (1f / 31f), (cell.g >> 2) * (1f / 63f), (cell.b >> 3) * (1f / 31f), 1f);
            var c = Pixel32(format, cell);
            return new Color(c.r * Scale, c.g * Scale, c.b * Scale, c.a * Scale);
        }

        /// <summary>As <c>GetPixels</c> reads a float cell.</summary>
        internal static Color Pixels(TextureFormat format, Color cell) =>
            format == TextureFormat.R16 ? new Color(R16Of(cell) * (1f / 65535f), 1f, 1f, 1f) : cell;

        /// <summary>As <c>GetPixel</c> reads a byte cell.</summary>
        internal static Color Pixel(TextureFormat format, Color32 cell)
        {
            if (Packed16(format) || format == TextureFormat.Alpha8) return Pixels(format, cell);
            var c = Pixel32(format, cell);
            return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        }
    }
}
