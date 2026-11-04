using System;
using System.IO;
using System.IO.Compression;

namespace UnityEngine
{
    public static partial class ImageConversion
    {
        // Unity 6000.0 ImageConversion.EncodeToPNG: "The encoded PNG data will be either 8bit
        // grayscale, RGB or RGBA (depending on the passed in format). For single-channel red
        // textures (R8, R16, RFloat and RHalf), the encoded PNG data will be in grayscale. PNG data
        // will not contain gamma correction or color profile information."
        // https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/imageconversion/encodetopng
        static bool EncodesAsGrayscale(TextureFormat format) =>
            format is TextureFormat.R8 or TextureFormat.R16 or TextureFormat.RHalf or TextureFormat.RFloat;

        /// <summary>
        /// An 8-bit grayscale PNG (signature, IHDR colour type 0, one IDAT, IEND; no gAMA, cHRM,
        /// sRGB or iCCP chunk) of <c>GetPixels32</c>-ordered pixels, rows given bottom first.
        /// The gray value is the red byte <c>GetPixels32</c> already returns (R8 the stored byte,
        /// R16 and the float formats as that reader rounds them): how Unity rounds R16 and float
        /// texels to 8 bits is not measured, so this keeps the existing conversion.
        /// </summary>
        static byte[] GrayscalePng(Color32[] pixels, int width, int height)
        {
            int stride = width + 1;
            var raw = new byte[checked(stride * height)];
            for (int y = 0; y < height; y++)
            {
                // PNG rows run top first; texture rows bottom first.
                int source = (height - 1 - y) * width, at = y * stride;
                raw[at++] = 0; // filter: none
                for (int x = 0; x < width; x++) raw[at++] = pixels[source + x].r;
            }
            var data = new MemoryStream();
            using (var z = new ZLibStream(data, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);

            var header = new byte[13];
            PngBigEndian(header, 0, (uint)width);
            PngBigEndian(header, 4, (uint)height);
            header[8] = 8; // bit depth
            header[9] = 0; // colour type: grayscale
            // compression, filter and interlace methods: 0

            var png = new MemoryStream();
            png.Write(s_PngSignature, 0, s_PngSignature.Length);
            PngChunk(png, "IHDR", header);
            PngChunk(png, "IDAT", data.ToArray());
            PngChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static readonly byte[] s_PngSignature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

        // The same chunk writer as ScreenCapture's RGB PNG; kept private here so the encoder
        // routes stay independent.
        static readonly uint[] s_PngCrc = PngCrcTable();

        static uint[] PngCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        static void PngBigEndian(byte[] buffer, int at, uint value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        /// <summary>Length, type, data and the CRC-32 of type and data (PNG 1.2, section 3.4).</summary>
        static void PngChunk(Stream png, string type, byte[] data)
        {
            var bytes = new byte[12 + data.Length];
            PngBigEndian(bytes, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) bytes[4 + i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, bytes, 8, data.Length);
            uint crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8 + data.Length; i++) crc = s_PngCrc[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            PngBigEndian(bytes, 8 + data.Length, crc ^ 0xFFFFFFFFu);
            png.Write(bytes, 0, bytes.Length);
        }
    }
}
