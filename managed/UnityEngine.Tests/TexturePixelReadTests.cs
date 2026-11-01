using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        static Texture2D Filled(TextureFormat format, params byte[] data)
        {
            var texture = new Texture2D(data.Length / Bytes(format), 1, format, false, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixelData(data, 0);
            return texture;
        }

        static int Bytes(TextureFormat format) => format switch
        {
            TextureFormat.Alpha8 or TextureFormat.R8 => 1,
            TextureFormat.RG16 or TextureFormat.R16 or TextureFormat.RGBA4444 or TextureFormat.ARGB4444 or TextureFormat.RGB565 => 2,
            _ => 4,
        };

        [Fact]
        public void SingleChannelFormatsReadOneInTheMissingChannels()
        {
            var r8 = Filled(TextureFormat.R8, 51);
            Assert.Equal(new Color32(51, 255, 255, 255), r8.GetPixels32()[0]);
            Assert.Equal(new Color(51f / 255f, 1, 1, 1), r8.GetPixel(0, 0));
            var rg = Filled(TextureFormat.RG16, 51, 3);
            Assert.Equal(new Color32(51, 3, 255, 255), rg.GetPixels32()[0]);
            Assert.Equal(new Color(51 * (1f / 255f), 3 * (1f / 255f), 1, 1), rg.GetPixels()[0]);
            var alpha = Filled(TextureFormat.Alpha8, 102);
            Assert.Equal(new Color32(0, 0, 0, 102), alpha.GetPixels32()[0]);
            Assert.Equal(new Color(1, 1, 1, 102f / 255f), alpha.GetPixel(0, 0));
            Object.DestroyImmediate(r8);
            Object.DestroyImmediate(rg);
            Object.DestroyImmediate(alpha);
        }

        [Fact]
        public void PackedFormatsReadTheirOwnScales()
        {
            // Little-endian 0x4101: RGBA4444 (4, 1, 0, 1), ARGB4444 a 4 r 1 g 0 b 1, RGB565 (8, 8, 1).
            var rgba = Filled(TextureFormat.RGBA4444, 0x01, 0x41);
            Assert.Equal(new Color32(68, 17, 0, 17), rgba.GetPixels32()[0]);
            Assert.Equal(new Color(68 * (1f / 255f), 17 * (1f / 255f), 0, 17 * (1f / 255f)), rgba.GetPixel(0, 0));
            var argb = Filled(TextureFormat.ARGB4444, 0x01, 0x41);
            Assert.Equal(new Color32(17, 0, 17, 68), argb.GetPixels32()[0]);
            var rgb = Filled(TextureFormat.RGB565, 0x01, 0x41, 0xFF, 0x3F);
            Assert.Equal(new Color32(66, 32, 8, 255), rgb.GetPixels32()[0]);
            Assert.Equal(new Color32(57, 255, 255, 255), rgb.GetPixels32()[1]);
            Assert.Equal(new Color(8 * (1f / 31f), 8 * (1f / 63f), 1 * (1f / 31f), 1), rgb.GetPixels()[0]);
            var r16 = Filled(TextureFormat.R16, 0x00, 0x40);
            Assert.Equal(new Color32(64, 64, 64, 255), r16.GetPixels32()[0]);
            Assert.Equal(16384 * (1f / 65535f), r16.GetPixels()[0].r);
            Assert.Equal(new Color(16384f / 65535f, 1, 1, 1), r16.GetPixel(0, 0));
            foreach (var t in new[] { rgba, argb, rgb, r16 })
            {
                var bytes = t.GetPixelData<byte>(0).ToArray();
                Assert.Equal(t.GetRawTextureData(), bytes);
                Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void PixelDataIsAViewOfTheTexture()
        {
            var texture = Filled(TextureFormat.ARGB32, 1, 2, 3, 4);
            var view = texture.GetPixelData<byte>(0);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, view.ToArray());
            view[0] = 200;
            Assert.Equal(new Color32(2, 3, 4, 200), texture.GetPixels32()[0]);
            texture.SetPixel(0, 0, new Color32(10, 20, 30, 40));
            Assert.Equal(new byte[] { 40, 10, 20, 30 }, view.ToArray());
            texture.Apply(false);
            Assert.Equal(new Color32(10, 20, 30, 40), texture.GetPixels32()[0]);
            texture.Reinitialize(1, 1);
            Assert.ThrowsAny<System.Exception>(() => view[0]);
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void GetPixelBilinearMixesNeighboursAtTexelCentres()
        {
            var texture = Filled(TextureFormat.RGBA32, 3, 0, 0, 0, 4, 0, 0, 0);
            texture.wrapMode = TextureWrapMode.Clamp;
            float a = 3f / 255f, b = 4f / 255f;
            Assert.Equal(a * (1f - 0.5f) + b * 0.5f, texture.GetPixelBilinear(0.25f, 0.5f).r);
            Assert.Equal(b, texture.GetPixelBilinear(0.75f, 0.5f).r);
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void GraphicsFormatsWithoutNames()
        {
            var argb = new Texture2D(1, 1, TextureFormat.ARGB32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var alpha = new Texture2D(1, 1, TextureFormat.Alpha8, false, true) { hideFlags = HideFlags.HideAndDontSave };
            Assert.Equal((GraphicsFormat)88, argb.graphicsFormat);
            Assert.Equal((GraphicsFormat)54, alpha.graphicsFormat);
            Object.DestroyImmediate(argb);
            Object.DestroyImmediate(alpha);
        }

        [Fact]
        public void CubemapPixelDataHoldsTheFaceBytes()
        {
            var cube = new Cubemap(1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            cube.SetPixels(new[] { new Color32(1, 2, 3, 4) }.Select(c => (Color)c).ToArray(), CubemapFace.NegativeY);
            var view = cube.GetPixelData<byte>(0, CubemapFace.NegativeY);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, view.ToArray());
            view[1] = 51;
            Assert.Equal(51 * (1f / 255f), cube.GetPixels(CubemapFace.NegativeY)[0].g);
            Assert.Equal(51 * (1f / 255f), cube.GetPixel(CubemapFace.NegativeY, 0, 0).g);
            Object.DestroyImmediate(cube);
        }
    }
}
