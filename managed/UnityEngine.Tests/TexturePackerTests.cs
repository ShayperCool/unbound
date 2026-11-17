using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Texture2D.PackTextures' layouts as the Unity 6000.0.71f1 Editor packs them
    /// (tests/projects/grass `pack` rows), on sets of textures beside the probe's.</summary>
    public class TexturePackerTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
        }

        Texture2D Make(int width, int height, bool mips = true)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, mips);
            m_Objects.Add(t);
            return t;
        }

        static int[][] Texels(Texture2D atlas, Rect[] rects)
        {
            var result = new int[rects.Length][];
            for (int i = 0; i < rects.Length; i++)
                result[i] = new[]
                {
                    Mathf.RoundToInt(rects[i].x * atlas.width), Mathf.RoundToInt(rects[i].y * atlas.height),
                    Mathf.RoundToInt(rects[i].width * atlas.width), Mathf.RoundToInt(rects[i].height * atlas.height),
                };
            return result;
        }

        /// <summary>Squares: the atlas grows from the area's square root; padding moves the second
        /// row up (the Editor's 128 × 64 and 128 × 128 atlases).</summary>
        [Theory]
        [InlineData(0, 128, 64, new[] { 0, 0, 64, 64, 64, 0, 32, 32, 96, 0, 32, 32, 64, 32, 16, 16 })]
        [InlineData(2, 128, 128, new[] { 0, 0, 64, 64, 66, 0, 32, 32, 0, 66, 32, 32, 100, 0, 16, 16 })]
        public void SquaresPackLargestFirst(int padding, int width, int height, int[] expected)
        {
            var atlas = Make(1, 1);
            var rects = atlas.PackTextures(new[] { Make(64, 64), Make(32, 32), Make(32, 32), Make(16, 16) }, padding, 2048);
            Assert.Equal((width, height), (atlas.width, atlas.height));
            var texels = Texels(atlas, rects);
            for (int i = 0; i < rects.Length; i++)
                Assert.Equal(expected[(i * 4)..(i * 4 + 4)], texels[i]);
            Assert.Equal(8, atlas.mipmapCount);
            Assert.Equal(TextureFormat.RGBA32, atlas.format);
        }

        /// <summary>Odd sizes and a padding the right and top edges do not need.</summary>
        [Theory]
        [InlineData(0, new[] { 27, 0, 5, 3, 20, 0, 7, 9, 0, 20, 13, 2, 27, 3, 3, 3, 0, 0, 20, 20 })]
        [InlineData(2, new[] { 22, 11, 5, 3, 22, 0, 7, 9, 0, 22, 13, 2, 29, 11, 3, 3, 0, 0, 20, 20 })]
        public void OddSizesFillTheTree(int padding, int[] expected)
        {
            var atlas = Make(1, 1);
            var rects = atlas.PackTextures(new[] { Make(5, 3), Make(7, 9), Make(13, 2), Make(3, 3), Make(20, 20) }, padding, 2048);
            Assert.Equal((32, 32), (atlas.width, atlas.height));
            var texels = Texels(atlas, rects);
            for (int i = 0; i < rects.Length; i++)
                Assert.Equal(expected[(i * 4)..(i * 4 + 4)], texels[i]);
        }

        /// <summary>Over the maximum the textures shrink to 3/4 until they fit; a side over it
        /// halves first.</summary>
        [Fact]
        public void TexturesOverTheMaximumShrink()
        {
            var atlas = Make(1, 1);
            var rects = atlas.PackTextures(new[] { Make(50, 50), Make(50, 50), Make(40, 30), Make(60, 20) }, 0, 64);
            Assert.Equal((64, 64), (atlas.width, atlas.height));
            var texels = Texels(atlas, rects);
            Assert.Equal(new[] { 0, 0, 27, 27 }, texels[0]);
            Assert.Equal(new[] { 27, 0, 27, 27 }, texels[1]);
            Assert.Equal(new[] { 0, 38, 22, 16 }, texels[2]);
            Assert.Equal(new[] { 0, 27, 33, 11 }, texels[3]);
            rects = atlas.PackTextures(new[] { Make(100, 20), Make(20, 300) }, 0, 64);
            texels = Texels(atlas, rects);
            Assert.Equal(new[] { 0, 0, 50, 20 }, texels[0]);
            Assert.Equal(new[] { 0, 20, 20, 37 }, texels[1]);
        }

        /// <summary>A texture given twice takes one place, a missing one gets an empty rect;
        /// without mipmaps in any input the atlas has none.</summary>
        [Fact]
        public void SharedAndMissingTextures()
        {
            var atlas = Make(1, 1);
            var shared = Make(9, 9);
            var rects = atlas.PackTextures(new[] { shared, null, Make(6, 10, false), shared }, 0, 2048);
            Assert.Equal((16, 16), (atlas.width, atlas.height));
            var texels = Texels(atlas, rects);
            Assert.Equal(new[] { 6, 0, 9, 9 }, texels[0]);
            Assert.Equal(new[] { 0, 0, 0, 0 }, texels[1]);
            Assert.Equal(new[] { 0, 0, 6, 10 }, texels[2]);
            Assert.Equal(texels[0], texels[3]);
            rects = atlas.PackTextures(new[] { Make(10, 10, false), Make(20, 6, false) }, 0, 2048);
            Assert.Equal(1, atlas.mipmapCount);
            Assert.Equal(new[] { 0, 6, 10, 10 }, Texels(atlas, rects)[0]);
        }

        /// <summary>Textures that cannot fit (sides of 4 never shrink) give null and leave the
        /// atlas as it was (the Scripting API: "or null if packing fails"); no texture at all
        /// leaves it too.</summary>
        [Fact]
        public void UnpackableSetsGiveNull()
        {
            var atlas = Make(8, 8);
            var tiny = new Texture2D[300];
            for (int i = 0; i < tiny.Length; i++) tiny[i] = Make(4, 4, false);
            Assert.Null(atlas.PackTextures(tiny, 0, 64));
            Assert.Equal((8, 8), (atlas.width, atlas.height));
            Assert.Equal(new Rect[2], atlas.PackTextures(new Texture2D[] { null, null }, 0, 64));
            Assert.Equal((8, 8), (atlas.width, atlas.height));
        }

        /// <summary>A maximum size that is not a power of two is rounded up to one: 100 allows 128.</summary>
        [Fact]
        public void TheMaximumRoundsUpToAPowerOfTwo()
        {
            var atlas = Make(1, 1);
            var rects = atlas.PackTextures(new[] { Make(120, 20), Make(20, 100) }, 0, 100);
            Assert.Equal(128, atlas.width);
            Assert.Equal(new[] { 0, 0, 120, 20 }, Texels(atlas, rects)[0]);
        }

        /// <summary>The textures' pixels land at their places, transparent black elsewhere.</summary>
        [Fact]
        public void PixelsLandAtTheirPlaces()
        {
            var red = Make(4, 4, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 0, 0, 255);
            red.SetPixels32(px);
            red.Apply();
            var atlas = Make(1, 1);
            var rects = atlas.PackTextures(new[] { Make(16, 32, false), red }, 0, 2048);
            Assert.Equal(new[] { 16, 0, 4, 4 }, Texels(atlas, rects)[1]);
            var level0 = atlas.GetPixels32();
            Assert.Equal(new Color32(255, 0, 0, 255), level0[3 * 32 + 19]);
            Assert.Equal(new Color32(0, 0, 0, 0), level0[4 * 32 + 19]);
            Assert.Equal(new Color32(0, 0, 0, 0), level0[20]);
        }
    }
}
