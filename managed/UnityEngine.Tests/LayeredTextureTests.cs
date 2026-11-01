using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class LayeredTextureTests
    {
        // 16384x16384x4 RGBA32 is 4 GiB, which 32-bit offsets wrapped to an empty array while
        // GetPixelData still described a 1 GiB level over it.
        [Fact]
        public void TexturesTooLargeForOneArrayFailInsteadOfWrapping()
        {
            var array = Assert.Throws<UnityException>(() => new Texture2DArray(16384, 16384, 4, TextureFormat.RGBA32, false));
            Assert.Contains("4294967296 bytes", array.Message);
            Assert.Throws<UnityException>(() => new Texture3D(2048, 2048, 2048, TextureFormat.R8, false));
            Assert.Throws<UnityException>(() => new CubemapArray(16384, 2, TextureFormat.RGBA32, false));
            // An R8 array whose RGBA upload alone would not fit.
            Assert.Throws<UnityException>(() => new Texture2DArray(16384, 16384, 2, TextureFormat.R8, false));
        }

        [Theory]
        [InlineData(TextureFormat.RGBA32)]
        [InlineData(TextureFormat.BGRA32)]
        [InlineData(TextureFormat.R8)]
        [InlineData(TextureFormat.RGBAHalf)]
        [InlineData(TextureFormat.RFloat)]
        public void VolumeTexelAccessMatchesTheWholeLevel(TextureFormat format)
        {
            var volume = new Texture3D(4, 3, 2, format, true) { hideFlags = HideFlags.HideAndDontSave };
            var colors = new Color[4 * 3 * 2];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(i / 23f, 1 - i / 31f, (i * 7 % 24) / 24f, 0.25f + i / 64f);
            volume.SetPixels(colors);
            volume.SetPixel(1, 2, 1, new Color(0.3f, 0.6f, 0.9f, 0.5f));
            volume.SetPixel(5, 0, 0, Color.red); // outside: ignored
            var level = volume.GetPixels();
            var expected = new Texture3D(4, 3, 2, format, true) { hideFlags = HideFlags.HideAndDontSave };
            colors[(1 * 3 + 2) * 4 + 1] = new Color(0.3f, 0.6f, 0.9f, 0.5f);
            expected.SetPixels(colors);
            Assert.Equal(expected.GetPixels(), level);
            for (int z = 0; z < 2; z++)
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                        Assert.Equal(level[(z * 3 + y) * 4 + x], volume.GetPixel(x, y, z));
            Assert.Equal(level[(1 * 3 + 2) * 4 + 3], volume.GetPixel(9, 7, 5)); // clamped
            volume.SetPixel(0, 0, 0, Color.green, 1);
            Assert.Equal(volume.GetPixels(1)[0], volume.GetPixel(0, 0, 0, 1));
            Object.DestroyImmediate(volume);
            Object.DestroyImmediate(expected);
        }

        // tests/projects/resources floatformats.*.array/volume/cubearray.cpu: two independent
        // Unity 6000.0.71f1 captures read one in the channels R and RG formats do not store.
        [Theory]
        [InlineData(TextureFormat.RHalf)]
        [InlineData(TextureFormat.RGHalf)]
        [InlineData(TextureFormat.RFloat)]
        [InlineData(TextureFormat.RGFloat)]
        public void FloatArraysAndVolumesReadOneInChannelsTheyDoNotStore(TextureFormat format)
        {
            bool two = format is TextureFormat.RGHalf or TextureFormat.RGFloat;
            var color = new Color(1.25f, -0.125f, 0.5f, 0.25f);
            var array = new Texture2DArray(1, 1, 2, format, false, true) { hideFlags = HideFlags.HideAndDontSave };
            array.SetPixels(new[] { color }, 1);
            var volume = new Texture3D(1, 1, 1, format, false) { hideFlags = HideFlags.HideAndDontSave };
            volume.SetPixels(new[] { color });
            foreach (var read in new[] { array.GetPixels(1)[0], volume.GetPixels()[0], volume.GetPixel(0, 0, 0) })
            {
                Assert.Equal(1.25f, read.r);
                Assert.Equal(two ? -0.125f : 1f, read.g);
                Assert.Equal(1f, read.b);
                Assert.Equal(1f, read.a);
            }
            Object.DestroyImmediate(array);
            Object.DestroyImmediate(volume);
        }
    }
}
