using System;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Supersized screen captures (Unity 6000.0.71f1, tests/projects/screencapture
    /// <c>asTexture.superSize*</c>, <c>mip.*</c>): N * N tiles at the screen's size, each with the
    /// cameras' projections moved by a fraction of a pixel, interleaved; mip bias and anisotropic
    /// limits raised for the capture's duration.</summary>
    public class ScreenCaptureSupersizeTests
    {
        static Vector2 Ndc(Matrix4x4 projection, Vector3 view)
        {
            var clip = projection * new Vector4(view.x, view.y, view.z, 1f);
            return new Vector2(clip.x / clip.w, clip.y / clip.w);
        }

        /// <summary>A tile moves every point of the image by minus its offset in pixels, for a
        /// perspective and an orthographic projection alike (so the tile's pixel x samples the
        /// screen at x + 0.5 + offset), and in a camera's own pixel rect.</summary>
        [Theory]
        [InlineData(false, 64f, 32f)]
        [InlineData(true, 64f, 32f)]
        [InlineData(false, 21f, 47f)]
        [InlineData(true, 21f, 47f)]
        public void TileMovesTheImageByMinusItsOffset(bool orthographic, float width, float height)
        {
            var projection = orthographic ? Matrix4x4.Ortho(-2f, 2f, -1f, 1f, 1f, 20f) : Matrix4x4.Perspective(60f, 2f, 1f, 20f);
            var rect = new Rect(5f, 3f, width, height);
            float ox = 1f / 3f - 0.5f, oy = 2f / 3f - 0.5f;
            var moved = ScreenCapture.TileProjection(projection, orthographic, rect, ox, oy);
            foreach (var point in new[] { new Vector3(0.3f, -0.2f, -5f), new Vector3(-1.1f, 0.7f, -12f) })
            {
                var delta = Ndc(moved, point) - Ndc(projection, point);
                Assert.Equal(-ox * 2f / width, delta.x, 5);
                Assert.Equal(-oy * 2f / height, delta.y, 5);
            }
            // Only the column of the move changes.
            for (int k = 0; k < 16; k++)
            {
                bool moves = orthographic ? (k == 12 || k == 13) : (k == 8 || k == 9);
                if (!moves) Assert.Equal(projection[k], moved[k]);
            }
        }

        /// <summary>Tile (i, j)'s pixel (x, y) is the output's pixel (x * N + i, y * N + j).</summary>
        [Fact]
        public void TilesInterleaveIntoTheOutput()
        {
            const int n = 3, w = 2, h = 2;
            var output = new byte[w * n * h * n * 4];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var tile = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int at = (y * w + x) * 4;
                    tile[at] = (byte)i; tile[at + 1] = (byte)j; tile[at + 2] = (byte)x; tile[at + 3] = (byte)y;
                }
                ScreenCapture.Interleave(tile, w, h, output, n, i, j);
            }
            for (int oy = 0; oy < h * n; oy++)
            for (int ox = 0; ox < w * n; ox++)
            {
                int at = (oy * w * n + ox) * 4;
                Assert.Equal(new byte[] { (byte)(ox % n), (byte)(oy % n), (byte)(ox / n), (byte)(oy / n) },
                    new[] { output[at], output[at + 1], output[at + 2], output[at + 3] });
            }
        }

        /// <summary>During the capture every 2D texture with mipmaps samples 1 + log2 N levels
        /// finer and the anisotropic limits are 16 and 16; textures without mipmaps and of other
        /// dimensions keep their bias, and everything is put back afterwards.</summary>
        [Fact]
        public void CaptureSettingsBiasMippedTexturesAndRaiseAnisotropyThenRestore()
        {
            var mipped = new Texture2D(8, 8, TextureFormat.RGBA32, true) { mipMapBias = 0.25f };
            var plain = new Texture2D(8, 8, TextureFormat.RGBA32, false) { mipMapBias = 0.5f };
            var cube = new Cubemap(8, TextureFormat.RGBA32, true) { mipMapBias = 0.75f };
            var target = new RenderTexture(8, 8, 0) { useMipMap = true, autoGenerateMips = false, mipMapBias = -0.5f };
            Texture.GetGlobalAnisotropicFilteringLimits(out int startForced, out int startMax);
            try
            {
                Texture.SetGlobalAnisotropicFilteringLimits(3, 7);
                var settings = ScreenCapture.SupersampleSettings.Apply(3);
                Texture.GetGlobalAnisotropicFilteringLimits(out int forced, out int max);
                Assert.Equal((16, 16), (forced, max));
                Assert.Equal(0.25f + (-1f - MathF.Log2(3f)), mipped.mipMapBias);
                Assert.Equal(-0.5f + (-1f - MathF.Log2(3f)), target.mipMapBias);
                Assert.Equal(0.5f, plain.mipMapBias);
                Assert.Equal(0.75f, cube.mipMapBias);
                settings.Restore();
                Texture.GetGlobalAnisotropicFilteringLimits(out forced, out max);
                Assert.Equal((3, 7), (forced, max));
                Assert.Equal(0.25f, mipped.mipMapBias);
                Assert.Equal(-0.5f, target.mipMapBias);
                Assert.Equal(0.5f, plain.mipMapBias);
                Assert.Equal(0.75f, cube.mipMapBias);
            }
            finally
            {
                Texture.SetGlobalAnisotropicFilteringLimits(startForced, startMax);
                Object.DestroyImmediate(mipped);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(target);
            }
        }
    }
}
