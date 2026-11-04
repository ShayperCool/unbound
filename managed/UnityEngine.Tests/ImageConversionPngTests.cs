using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Unity 6000.0 ImageConversion.EncodeToPNG documents 8-bit grayscale PNG data for
    /// single-channel red textures (R8, R16, RFloat, RHalf) and no gamma or colour profile
    /// information (https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/imageconversion/encodetopng;
    /// documented, no Native capture). These tests read the PNG structure itself (the test
    /// project has no standard PNG decoder); they do not fix the compressed stream, the filter
    /// choice, the compression level, Native byte identity or how Unity rounds R16 and float
    /// texels to 8 bits. That the gray value equals the red byte <c>GetPixels32</c> reads is
    /// Unbound's scoped choice, checked only on values every format holds exactly.
    /// </summary>
    public class ImageConversionPngTests
    {
        // Bottom texture row first, as SetPixels takes it; no value sits on a rounding tie.
        const int Width = 3, Height = 2;
        static readonly byte[] s_Gray = { 16, 64, 200, 255, 0, 96 };

        static readonly TextureFormat[] s_SingleChannelRed =
        {
            TextureFormat.R8, TextureFormat.R16, TextureFormat.RHalf, TextureFormat.RFloat,
        };

        public static TheoryData<TextureFormat> SingleChannelRed
        {
            get
            {
                var data = new TheoryData<TextureFormat>();
                foreach (var format in s_SingleChannelRed) data.Add(format);
                return data;
            }
        }

        // sRGB and linear textures: the encoder applies no colour-space conversion of its own.
        public static TheoryData<TextureFormat, bool> SingleChannelRedBothSpaces
        {
            get
            {
                var data = new TheoryData<TextureFormat, bool>();
                foreach (var format in s_SingleChannelRed)
                    foreach (bool linear in new[] { false, true }) data.Add(format, linear);
                return data;
            }
        }

        static Texture2D Filled(TextureFormat format, bool mipChain = false, bool linear = true)
        {
            var texture = new Texture2D(Width, Height, format, mipChain, linear) { hideFlags = HideFlags.HideAndDontSave };
            // Other channels differ from red, so a leak of green, blue or alpha would show.
            texture.SetPixels(s_Gray.Select(v => new Color(v / 255f, 0.9f, 0.1f, 0.3f)).ToArray());
            return texture;
        }

        [Theory]
        [MemberData(nameof(SingleChannelRedBothSpaces))]
        public void SingleChannelRedEncodesAsEightBitGrayscale(TextureFormat format, bool linear)
        {
            var texture = Filled(format, linear: linear);
            try
            {
                var png = Png.Read(texture.EncodeToPNG());
                Assert.Equal((Width, Height, 8, 0), (png.Width, png.Height, png.BitDepth, png.ColourType));
                // PNG defines only method 0 for compression and filtering.
                Assert.Equal((0, 0), (png.Compression, png.Filter));
                var rows = png.Rows(1);
                // PNG rows run top first: the texture's top row comes first.
                Assert.Equal(new byte[] { 255, 0, 96 }, rows[0]);
                Assert.Equal(new byte[] { 16, 64, 200 }, rows[1]);
                // Scoped choice: the gray value is the red byte GetPixels32 reads.
                var reds = texture.GetPixels32().Select(c => c.r).ToArray();
                Assert.Equal(reds.Skip(Width).Take(Width).ToArray(), rows[0]);
                Assert.Equal(reds.Take(Width).ToArray(), rows[1]);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Theory]
        [MemberData(nameof(SingleChannelRed))]
        public void GrayscaleCarriesNoGammaOrColourProfile(TextureFormat format)
        {
            var texture = Filled(format);
            try
            {
                var png = Png.Read(texture.EncodeToPNG());
                Assert.Equal("IHDR", png.Chunks[0]);
                Assert.Equal("IEND", png.Chunks[^1]);
                Assert.Contains("IDAT", png.Chunks);
                // PNG's colour-space chunks (gamma, chromaticities, sRGB intent, ICC profile), and a
                // palette, which grayscale PNGs must not have.
                foreach (var type in new[] { "gAMA", "cHRM", "sRGB", "iCCP", "PLTE" })
                    Assert.DoesNotContain(type, png.Chunks);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void GrayscaleEncodesTheFirstMipLevel()
        {
            var texture = Filled(TextureFormat.R8, mipChain: true);
            try
            {
                Assert.True(texture.mipmapCount > 1);
                var png = Png.Read(texture.EncodeToPNG());
                Assert.Equal((Width, Height), (png.Width, png.Height));
                Assert.Equal(new byte[] { 16, 64, 200 }, png.Rows(1)[1]);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void NullTextureThrows()
        {
            Texture2D texture = null;
            Assert.Throws<NullReferenceException>(() => texture.EncodeToPNG());
        }

        [Theory]
        [MemberData(nameof(SingleChannelRed))]
        [InlineData(TextureFormat.RGBA32)]
        public void UnreadableTextureThrows(TextureFormat format)
        {
            var texture = Filled(format);
            try
            {
                texture.Apply(false, true);
                Assert.False(texture.isReadable);
                Assert.Throws<ArgumentException>(() => texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // The other formats keep the native RGBA route byte for byte: the same pixels to the same
        // encoder (null when the native library is not loaded, as before).
        [Theory]
        [InlineData(TextureFormat.RGBA32)]
        [InlineData(TextureFormat.ARGB32)]
        [InlineData(TextureFormat.RGB24)]
        [InlineData(TextureFormat.RG16)]
        [InlineData(TextureFormat.Alpha8)]
        [InlineData(TextureFormat.RGBAHalf)]
        public void OtherFormatsKeepTheNativeRoute(TextureFormat format)
        {
            var texture = Filled(format);
            try
            {
                var expected = Native.EncodeImage<Color32>(0, 0, texture.GetPixels32(), Width, Height);
                Assert.Equal(expected, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // Only EncodeToPNG documents the grayscale output: single-channel JPG and TGA keep theirs.
        [Theory]
        [MemberData(nameof(SingleChannelRed))]
        public void SingleChannelJpgAndTgaKeepTheNativeRoute(TextureFormat format)
        {
            var texture = Filled(format);
            try
            {
                var pixels = texture.GetPixels32();
                Assert.Equal(Native.EncodeImage<Color32>(1, 75, pixels, Width, Height), texture.EncodeToJPG());
                Assert.Equal(Native.EncodeImage<Color32>(2, 0, pixels, Width, Height), texture.EncodeToTGA());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>A PNG's chunk sequence (each CRC checked), header fields and unfiltered rows.</summary>
        sealed class Png
        {
            public int Width, Height, BitDepth, ColourType, Compression, Filter, Interlace;
            public readonly List<string> Chunks = new List<string>();
            byte[] m_Data;

            static readonly byte[] s_Signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

            static uint BigEndian(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

            static uint Crc(byte[] b, int at, int length)
            {
                uint crc = 0xFFFFFFFFu;
                for (int i = at; i < at + length; i++)
                {
                    crc ^= b[i];
                    for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
                return crc ^ 0xFFFFFFFFu;
            }

            public static Png Read(byte[] bytes)
            {
                Assert.NotNull(bytes);
                Assert.True(bytes.Length > s_Signature.Length);
                Assert.Equal(s_Signature, bytes.Take(s_Signature.Length).ToArray());
                var png = new Png();
                var idat = new MemoryStream();
                int at = s_Signature.Length;
                while (at < bytes.Length)
                {
                    Assert.True(at + 12 <= bytes.Length, "truncated chunk");
                    int length = checked((int)BigEndian(bytes, at));
                    Assert.True(at + 12 + length <= bytes.Length, "truncated chunk data");
                    string type = new string(bytes.Skip(at + 4).Take(4).Select(b => (char)b).ToArray());
                    Assert.Equal(Crc(bytes, at + 4, 4 + length), BigEndian(bytes, at + 8 + length));
                    if (type == "IHDR")
                    {
                        Assert.Empty(png.Chunks);
                        Assert.Equal(13, length);
                        png.Width = (int)BigEndian(bytes, at + 8);
                        png.Height = (int)BigEndian(bytes, at + 12);
                        png.BitDepth = bytes[at + 16];
                        png.ColourType = bytes[at + 17];
                        png.Compression = bytes[at + 18];
                        png.Filter = bytes[at + 19];
                        png.Interlace = bytes[at + 20];
                    }
                    else if (type == "IDAT") idat.Write(bytes, at + 8, length);
                    png.Chunks.Add(type);
                    at += 12 + length;
                    if (type == "IEND") break;
                }
                Assert.Equal(bytes.Length, at);
                Assert.Equal("IEND", png.Chunks[^1]);
                idat.Position = 0;
                var data = new MemoryStream();
                using (var z = new ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(data);
                png.m_Data = data.ToArray();
                return png;
            }

            /// <summary>Rows top first, with the scanline filters (PNG 1.2, section 6) undone.</summary>
            public byte[][] Rows(int bytesPerPixel)
            {
                // This reader undoes non-interlaced scanlines only.
                Assert.Equal(0, Interlace);
                int stride = Width * bytesPerPixel;
                Assert.Equal((stride + 1) * Height, m_Data.Length);
                var rows = new byte[Height][];
                var previous = new byte[stride];
                for (int y = 0; y < Height; y++)
                {
                    int filter = m_Data[y * (stride + 1)];
                    var row = new byte[stride];
                    for (int x = 0; x < stride; x++)
                    {
                        int raw = m_Data[y * (stride + 1) + 1 + x];
                        int a = x >= bytesPerPixel ? row[x - bytesPerPixel] : 0, b = previous[x], c = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
                        int predictor = filter switch
                        {
                            0 => 0,
                            1 => a,
                            2 => b,
                            3 => (a + b) / 2,
                            4 => Paeth(a, b, c),
                            _ => throw new InvalidDataException($"filter {filter}"),
                        };
                        row[x] = (byte)(raw + predictor);
                    }
                    rows[y] = row;
                    previous = row;
                }
                return rows;
            }

            static int Paeth(int a, int b, int c)
            {
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
            }
        }
    }
}
