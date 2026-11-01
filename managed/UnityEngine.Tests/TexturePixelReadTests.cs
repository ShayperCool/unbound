using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class TexturePixelReadTests
    {
        // tests/projects/texturepixels: two independent Unity 6000.0.71f1 captures.
        [Theory]
        [InlineData(TextureFormat.RGBA32)]
        [InlineData(TextureFormat.RGB24)]
        [InlineData(TextureFormat.R8)]
        public void GetPixelsMultipliesByTheReciprocalAndGetPixelDivides(TextureFormat format)
        {
            var texture = new Texture2D(2, 2, format, true, false) { hideFlags = HideFlags.HideAndDontSave };
            // 51 * (1 / 255) is 0.200000018, 51 / 255 is 0.2.
            var pixels = new Color32[] { new(51, 51, 51, 51), new(3, 3, 3, 3), new(255, 255, 255, 255), new(0, 0, 0, 0) };
            texture.SetPixels32(pixels);
            texture.SetPixels32(new[] { new Color32(51, 51, 51, 51) }, 1);
            const float scale = 1f / 255f;
            Assert.Equal(51 * scale, texture.GetPixels()[0].r);
            Assert.Equal(3 * scale, texture.GetPixels(0)[1].r);
            Assert.Equal(51 * scale, texture.GetPixels(1)[0].r);
            Assert.Equal(51 * scale, texture.GetPixels(0, 0, 1, 1)[0].r);
            Assert.Equal(51 * scale, texture.GetPixels(0, 0, 1, 1, 1)[0].r);
            Assert.Equal(51f / 255f, texture.GetPixel(0, 0).r);
            Assert.NotEqual(51f / 255f, 51 * scale);
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void Alpha8GetPixelsDivides()
        {
            var texture = new Texture2D(1, 1, TextureFormat.Alpha8, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(new[] { new Color32(0, 0, 0, 102) });
            Assert.Equal(new Color(1, 1, 1, 102f / 255f), texture.GetPixels()[0]);
            Object.DestroyImmediate(texture);
        }
    }
}
