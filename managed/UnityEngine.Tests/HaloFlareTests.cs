using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The halo falloff and flare layout values the Editor shows (tests/projects/flares).</summary>
    public class HaloFlareTests
    {
        [Fact]
        public void FalloffIsTheEditorsTexture()
        {
            var falloff = Halos.Falloff;
            Assert.Equal("UnityHalo", falloff.name);
            Assert.Equal(64, falloff.width);
            Assert.Equal(FilterMode.Bilinear, falloff.filterMode);
            Assert.Equal(TextureWrapMode.Clamp, falloff.wrapMode);
            var row = new byte[64];
            for (int x = 0; x < 64; x++) row[x] = falloff.GetPixelData<byte>(0)[32 * 64 + x];
            // Row 32 of the Editor's UnityHalo, read back from its global _HaloFalloff.
            Assert.Equal(new byte[] { 8, 23, 38, 53, 67, 80, 93, 105, 117, 129, 140, 150, 160, 170, 179, 187 }, row[..16]);
            Assert.Equal(255, row[31]);
            Assert.Equal(0, falloff.GetPixelData<byte>(0)[0]);
        }

        [Fact]
        public void LayoutRectsAreInsetByTheTextureOutsideThem()
        {
            // 2x2 grid of a 256 texture: image 0 is the top left, inset 0.25 texel.
            var r = Halos.ImageRect(3, 0, 256, 256);
            Assert.Equal(0.25f / 256f, r.xMin, 6);
            Assert.Equal(127.75f / 256f, r.xMax, 6);
            Assert.Equal(128.25f / 256f, r.yMin, 6);
            // The single texture is not inset.
            r = Halos.ImageRect(2, 0, 256, 256);
            Assert.Equal(0f, r.xMin, 6);
            Assert.Equal(1f, r.yMax, 6);
            // 4x4 grid, image 5: second column, second row from the top, inset 0.375 texel.
            r = Halos.ImageRect(5, 5, 256, 256);
            Assert.Equal(64.375f / 256f, r.xMin, 6);
            Assert.Equal(128.375f / 256f, r.yMin, 6);
            // 1 Large 2 Medium 8 Small, image 10: the right column of the bottom row.
            r = Halos.ImageRect(1, 10, 256, 256);
            Assert.Equal((192f + 0.375f) / 256f, r.xMin, 6);
            Assert.Equal((0f + 0.4375f) / 256f, r.yMin, 6);
        }
    }
}
