using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>A block-compressed imported cube (tests/projects/cubecompression): it reports its
    /// blocks' format, and its texels and mips cannot be written.</summary>
    public class CompressedCubemapTests
    {
        static readonly Color Texel = new Color(0.25f, 0.5f, 0.75f, 1f);

        /// <summary>An imported readable 4x4 cube with three mips, `textureFormat` reported by the
        /// import (0: its pixels' own), every texel <see cref="Texel"/>.</summary>
        static Cubemap Imported(TextureFormat textureFormat)
        {
            var info = new ImportedTextureInfo
            {
                Width = 4, Height = 4, MipCount = 3, Srgb = 1, FilterMode = (int)FilterMode.Bilinear,
                AnisoLevel = 1, Readable = 1, Format = 0, Dimension = 4, TextureFormat = (int)textureFormat,
            };
            var cube = Cubemap.FromImport(0, null, "Cube", info);
            var pixels = new Color[6][][];
            for (int f = 0; f < 6; f++)
            {
                pixels[f] = new Color[3][];
                for (int mip = 0; mip < 3; mip++)
                {
                    int s = 4 >> mip;
                    pixels[f][mip] = new Color[s * s];
                    Array.Fill(pixels[f][mip], Texel);
                }
            }
            typeof(Cubemap).GetField("m_Pixels", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(cube, pixels);
            return cube;
        }

        static List<string> Errors(Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        [Fact]
        public void ACompressedCubeReportsItsBlocksFormat()
        {
            var cube = Imported(TextureFormat.DXT1);
            var plain = Imported(0);
            try
            {
                Assert.Equal(TextureFormat.DXT1, cube.format);
                Assert.Equal(GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.DXT1, QualitySettings.activeColorSpace == ColorSpace.Linear), cube.graphicsFormat);
                // Control: an uncompressed import reports its pixels' format.
                Assert.Equal(TextureFormat.RGBA32, plain.format);
            }
            finally
            {
                Object.DestroyImmediate(cube, true);
                Object.DestroyImmediate(plain, true);
            }
        }

        [Fact]
        public void ACompressedCubesTexelsAndMipsAreNotWritten()
        {
            var cube = Imported(TextureFormat.DXT1);
            try
            {
                // The format's linear graphics format, also for sRGB data (RGBA_DXT1_UNorm).
                Assert.Equal(new[] { "Unsupported GraphicsFormat(97) for SetPixel operations." },
                    Errors(() => cube.SetPixel(CubemapFace.PositiveX, 1, 1, Color.red)));
                Assert.Equal(Texel, cube.GetPixel(CubemapFace.PositiveX, 1, 1));

                var e = Assert.Throws<ArgumentException>(() => cube.SetPixels(new Color[16], CubemapFace.NegativeX));
                Assert.Equal("Cubemap.SetPixels: texture uses an unsupported format. (Texture 'Cube')", e.Message);
                Assert.Equal(Texel, cube.GetPixel(CubemapFace.NegativeX, 1, 1));

                Assert.Empty(Errors(() => cube.Apply(false)));
                Assert.Equal(new[] { "Rebuilding mipmaps of compressed textures is not supported" }, Errors(() => cube.Apply(true)));
                Assert.Equal(Texel, cube.GetPixel(CubemapFace.PositiveX, 0, 0, 2));
            }
            finally { Object.DestroyImmediate(cube, true); }
        }

        /// <summary>DXT1 blocks of a 4x4 cube with three mips, face by face (one 8-byte block per
        /// level), every texel of face f the 565 colour `colors[f]`.</summary>
        static byte[] SolidDxt1(ushort[] colors)
        {
            var raw = new byte[6 * 3 * 8];
            for (int f = 0; f < 6; f++)
                for (int mip = 0; mip < 3; mip++)
                {
                    int at = (f * 3 + mip) * 8;
                    raw[at] = (byte)colors[f];
                    raw[at + 1] = (byte)(colors[f] >> 8);
                    // color1 0 (below color0: four-colour mode), every index 0.
                }
            return raw;
        }

        [Fact]
        public void WritesThroughBlockViewsAreReadAndKept()
        {
            const ushort red = 0xF800, green = 0x07E0, blue = 0x001F;
            var cube = Imported(TextureFormat.DXT1);
            typeof(Cubemap).GetField("m_SourceRaw", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(cube, SolidDxt1(new[] { blue, blue, red, green, blue, blue }));
            Cubemap copy = null;
            try
            {
                var top = cube.GetPixelData<byte>(0, CubemapFace.PositiveY);
                Assert.Equal(8, top.Length);
                // Held blocks are what the texels read, before any write (not the import's pixels).
                Assert.Equal(new Color(1f, 0f, 0f, 1f), cube.GetPixel(CubemapFace.PositiveY, 1, 1));
                top.CopyFrom(cube.GetPixelData<byte>(0, CubemapFace.NegativeY));
                // Read before Apply (the Editor reads the write too), and after it.
                Assert.Equal(new Color(0f, 1f, 0f, 1f), cube.GetPixel(CubemapFace.PositiveY, 1, 1));
                Assert.Empty(Errors(() => cube.Apply(false)));
                Assert.Equal(new Color(0f, 1f, 0f, 1f), cube.GetPixel(CubemapFace.PositiveY, 3, 3));
                Assert.All(cube.GetPixels(CubemapFace.PositiveY), c => Assert.Equal(new Color(0f, 1f, 0f, 1f), c));
                // The other levels and faces keep their blocks.
                Assert.Equal(new Color(1f, 0f, 0f, 1f), cube.GetPixel(CubemapFace.PositiveY, 0, 0, 1));
                Assert.Equal(new Color(0f, 1f, 0f, 1f), cube.GetPixel(CubemapFace.NegativeY, 0, 0));
                Assert.Equal(new Color(0f, 0f, 1f, 1f), cube.GetPixel(CubemapFace.PositiveX, 0, 0));
                // A clone copies the written blocks, not the import's.
                copy = Object.Instantiate(cube);
                Assert.Equal(TextureFormat.DXT1, copy.format);
                Assert.Equal(new Color(0f, 1f, 0f, 1f), copy.GetPixel(CubemapFace.PositiveY, 1, 1));
                top[0] = (byte)blue;
                top[1] = 0;
                Assert.Equal(new Color(0f, 1f, 0f, 1f), copy.GetPixel(CubemapFace.PositiveY, 1, 1));
                Assert.Equal(new Color(0f, 0f, 1f, 1f), cube.GetPixel(CubemapFace.PositiveY, 1, 1));
            }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                Object.DestroyImmediate(cube, true);
            }
        }

        /// <summary>A BC6H cube's views are its 16-byte blocks (one per level here); its texels
        /// then decode from them natively (floats, unbound-assets decode_block_texel tests).</summary>
        [Fact]
        public void Bc6hBlockViewsAreWholeBlocks()
        {
            var cube = Imported(TextureFormat.BC6H);
            typeof(Cubemap).GetField("m_SourceRaw", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(cube, new byte[6 * 3 * 16]);
            try
            {
                Assert.Equal(16, cube.GetPixelData<byte>(0, CubemapFace.PositiveX).Length);
                Assert.Equal(16, cube.GetPixelData<byte>(2, CubemapFace.NegativeZ).Length);
                Assert.Empty(Errors(() => cube.Apply(false)));
            }
            finally { Object.DestroyImmediate(cube, true); }
        }

        [Fact]
        public void AnUncompressedImportedCubeIsWritten()
        {
            var cube = Imported(0);
            try
            {
                Assert.Empty(Errors(() => cube.SetPixel(CubemapFace.PositiveX, 1, 1, Color.red)));
                Assert.Equal(Color.red, cube.GetPixel(CubemapFace.PositiveX, 1, 1));
                var blue = new Color[16];
                Array.Fill(blue, Color.blue);
                cube.SetPixels(blue, CubemapFace.NegativeX);
                Assert.Equal(Color.blue, cube.GetPixel(CubemapFace.NegativeX, 1, 1));
            }
            finally { Object.DestroyImmediate(cube, true); }
        }
    }
}
