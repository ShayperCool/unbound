using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Texture2D.PackTextures of block-compressed textures as the Unity 6000.0.71f1 Editor
    /// packs them (tests/projects/packtextures), on sets beside the probe's.</summary>
    public class TexturePackerBlocksTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
        }

        uint m_Seed = 7u;

        /// <summary>A texture of fixed pseudo-random blocks (or texels).</summary>
        Texture2D Make(int width, int height, TextureFormat format, bool mips = true)
        {
            var t = new Texture2D(width, height, format, mips, false);
            var data = new byte[t.GetRawTextureData().Length];
            for (int i = 0; i < data.Length; i++)
            {
                m_Seed = m_Seed * 1664525u + 1013904223u;
                data[i] = (byte)(m_Seed >> 24);
            }
            t.LoadRawTextureData(data);
            t.Apply(false, false);
            m_Objects.Add(t);
            return t;
        }

        Texture2D Atlas()
        {
            var atlas = new Texture2D(4, 4);
            m_Objects.Add(atlas);
            return atlas;
        }

        static byte[] Level(Texture2D t, int mip) => t.GetPixelData<byte>(mip).ToArray();

        /// <summary>The 16-byte block at block (bx, by) of an atlas level.</summary>
        static ReadOnlySpan<byte> Block(byte[] level, int levelWidth, int bx, int by, int bytes) =>
            new ReadOnlySpan<byte>(level, (by * ((levelWidth + 3) / 4) + bx) * bytes, bytes);

        [Fact]
        public void OneCompressedFormatStaysCompressedWithEachTexturesLevels()
        {
            var a = Make(64, 64, TextureFormat.DXT5);
            var b = Make(16, 32, TextureFormat.DXT5);
            var atlas = Atlas();
            var rects = atlas.PackTextures(new[] { a, b }, 0, 2048);
            Assert.Equal(TextureFormat.DXT5, atlas.format);
            Assert.Equal(new Vector2Int(128, 64), new Vector2Int(atlas.width, atlas.height));
            // Down to where the smallest side (16) is one block: 16, 8, 4.
            Assert.Equal(3, atlas.mipmapCount);
            for (int mip = 0; mip < atlas.mipmapCount; mip++)
            {
                var level = Level(atlas, mip);
                int lw = atlas.width >> mip;
                foreach (var (t, rect) in new[] { (a, rects[0]), (b, rects[1]) })
                {
                    var own = Level(t, mip);
                    int x = (int)(rect.x * atlas.width) >> mip, y = (int)(rect.y * atlas.height) >> mip;
                    int tw = (t.width >> mip) / 4, th = (t.height >> mip) / 4;
                    for (int by = 0; by < th; by++)
                        for (int bx = 0; bx < tw; bx++)
                            Assert.True(Block(level, lw, x / 4 + bx, y / 4 + by, 16).SequenceEqual(new ReadOnlySpan<byte>(own, (by * tw + bx) * 16, 16)));
                }
            }
            // Between the textures, empty (zero) blocks.
            Assert.True(Block(Level(atlas, 0), 128, 31, 15, 16).SequenceEqual(new byte[16]));
        }

        [Fact]
        public void Dxt1BlocksGoIntoADxt5AtlasBehindOpaqueAlpha()
        {
            var a = Make(32, 32, TextureFormat.DXT5);
            var b = Make(16, 16, TextureFormat.DXT1);
            var atlas = Atlas();
            var rects = atlas.PackTextures(new[] { a, b }, 0, 2048);
            Assert.Equal(TextureFormat.DXT5, atlas.format);
            int x = (int)(rects[1].x * atlas.width) / 4, y = (int)(rects[1].y * atlas.height) / 4;
            var block = Block(Level(atlas, 0), atlas.width, x, y, 16);
            var own = Level(b, 0);
            Assert.True(block.Slice(0, 8).SequenceEqual(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 }));
            Assert.True(block.Slice(8, 8).SequenceEqual(new ReadOnlySpan<byte>(own, 0, 8)));
        }

        [Fact]
        public void Etc2RgbBlocksGoIntoAnEtc2RgbaAtlasBehindOpaqueAlpha()
        {
            var a = Make(16, 16, TextureFormat.ETC2_RGB);
            var b = Make(16, 16, TextureFormat.ETC2_RGBA8);
            var atlas = Atlas();
            var rects = atlas.PackTextures(new[] { a, b }, 0, 2048);
            Assert.Equal(TextureFormat.ETC2_RGBA8, atlas.format);
            int x = (int)(rects[0].x * atlas.width) / 4, y = (int)(rects[0].y * atlas.height) / 4;
            var block = Block(Level(atlas, 0), atlas.width, x, y, 16);
            Assert.True(block.Slice(0, 8).SequenceEqual(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 }));
            Assert.True(block.Slice(8, 8).SequenceEqual(new ReadOnlySpan<byte>(Level(a, 0), 0, 8)));
        }

        public static IEnumerable<object[]> Formats() => new[]
        {
            // (formats, mips per texture, padding, the atlas's format)
            new object[] { new[] { TextureFormat.DXT1, TextureFormat.DXT1 }, new[] { true, true }, 0, TextureFormat.DXT1 },
            new object[] { new[] { TextureFormat.DXT1, TextureFormat.DXT5 }, new[] { true, true }, 0, TextureFormat.DXT5 },
            new object[] { new[] { TextureFormat.BC7, TextureFormat.BC7, TextureFormat.BC7 }, new[] { true, true, true }, 0, TextureFormat.BC7 },
            new object[] { new[] { TextureFormat.ASTC_4x4, TextureFormat.ASTC_4x4 }, new[] { true, true }, 0, TextureFormat.ASTC_4x4 },
            new object[] { new[] { TextureFormat.ETC2_RGB, TextureFormat.ETC2_RGB }, new[] { true, true }, 0, TextureFormat.ETC2_RGB },
            // Mipmaps on some only, or padding with mipmaps: uncompressed.
            new object[] { new[] { TextureFormat.DXT5, TextureFormat.DXT5 }, new[] { true, false }, 0, TextureFormat.RGBA32 },
            new object[] { new[] { TextureFormat.DXT5, TextureFormat.DXT5 }, new[] { true, true }, 1, TextureFormat.RGBA32 },
            // Without mipmaps padding keeps it compressed.
            new object[] { new[] { TextureFormat.DXT5, TextureFormat.DXT5 }, new[] { false, false }, 3, TextureFormat.DXT5 },
            // The first texture's format decides; an uncompressed one makes RGBA32.
            new object[] { new[] { TextureFormat.RGBA32, TextureFormat.DXT5 }, new[] { true, true }, 0, TextureFormat.RGBA32 },
            new object[] { new[] { TextureFormat.DXT5, TextureFormat.RGB24 }, new[] { true, true }, 0, TextureFormat.RGBA32 },
        };

        [Theory]
        [MemberData(nameof(Formats))]
        public void TheAtlasFormatFollowsTheTextures(TextureFormat[] formats, bool[] mips, int padding, TextureFormat expected)
        {
            var textures = new Texture2D[formats.Length];
            for (int i = 0; i < formats.Length; i++) textures[i] = Make(32 >> (i % 2), 32 >> (i % 2), formats[i], mips[i]);
            var atlas = Atlas();
            Assert.NotNull(atlas.PackTextures(textures, padding, 2048));
            Assert.Equal(expected, atlas.format);
            Assert.Equal(mips[0] || mips[^1] ? atlas.mipmapCount > 1 : false, atlas.mipmapCount > 1);
        }

        [Fact]
        public void AShrinkingTextureMakesRgba32()
        {
            var atlas = Atlas();
            atlas.PackTextures(new[] { Make(64, 64, TextureFormat.DXT5), Make(16, 16, TextureFormat.DXT5) }, 0, 32);
            Assert.Equal(TextureFormat.RGBA32, atlas.format);
        }

        /// <summary>In a compressed atlas the places are on whole blocks: with 2 texels of padding the
        /// second texture goes at 36, not 34 (tests/projects/packtextures `dxt5nomipspadding`).</summary>
        [Fact]
        public void CompressedPlacesAreOnWholeBlocks()
        {
            var atlas = Atlas();
            var rects = atlas.PackTextures(new[] { Make(32, 32, TextureFormat.DXT5, false), Make(16, 16, TextureFormat.DXT5, false) }, 2, 2048);
            Assert.Equal(TextureFormat.DXT5, atlas.format);
            Assert.Equal(36f / 64f, rects[1].x);
            var plain = Atlas();
            rects = plain.PackTextures(new[] { Make(32, 32, TextureFormat.RGBA32, false), Make(16, 16, TextureFormat.RGBA32, false) }, 2, 2048);
            Assert.Equal(34f / 64f, rects[1].x);
        }

        /// <summary>The 4444 formats make the one the device samples (ARGB4444 first).</summary>
        [Fact]
        public void The4444FormatsMakeTheOneTheDeviceSamples()
        {
            var atlas = Atlas();
            atlas.PackTextures(new[] { Make(32, 32, TextureFormat.ARGB4444), Make(16, 16, TextureFormat.RGBA4444) }, 0, 2048);
            var expected = SystemInfo.IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat.B4G4R4A4_UNormPack16, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Sample)
                ? TextureFormat.ARGB4444 : TextureFormat.RGBA4444;
            Assert.Equal(expected, atlas.format);
        }
    }
}
