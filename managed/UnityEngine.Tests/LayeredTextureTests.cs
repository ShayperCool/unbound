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
            // GetPixel reads a byte b as b / 255, GetPixels as b * (1 / 255)
            // (tests/projects/texturepixels Volume*.pixel); float formats agree.
            bool bytes = format is TextureFormat.RGBA32 or TextureFormat.BGRA32 or TextureFormat.R8;
            Color Read(Color c) => bytes ? (Color)new Color32((byte)Mathf.Round(c.r * 255), (byte)Mathf.Round(c.g * 255), (byte)Mathf.Round(c.b * 255), (byte)Mathf.Round(c.a * 255)) : c;
            for (int z = 0; z < 2; z++)
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 4; x++)
                        Assert.Equal(Read(level[(z * 3 + y) * 4 + x]), volume.GetPixel(x, y, z));
            Assert.Equal(Read(level[(1 * 3 + 2) * 4 + 3]), volume.GetPixel(9, 7, 5)); // clamped
            if (bytes) Assert.Equal(51f / 255f, Read(new Color(51 * (1f / 255f), 0, 0, 0)).r);
            volume.SetPixel(0, 0, 0, Color.green, 1);
            Assert.Equal(Read(volume.GetPixels(1)[0]), volume.GetPixel(0, 0, 0, 1));
            Object.DestroyImmediate(volume);
            Object.DestroyImmediate(expected);
        }

        // tests/projects/texturepixels VolumeSetPixel: 2.5 / 255 stores 3 (Color32's rounding gives 2).
        [Fact]
        public void VolumeSetPixelRoundsHalfUp()
        {
            var volume = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            volume.SetPixel(0, 0, 0, new Color(2.5f / 255f, 0.5f / 255f, 254.5f / 255f, 1));
            Assert.Equal(new byte[] { 3, 1, 255, 255 }, volume.GetPixelData<byte>(0).ToArray());
            Object.DestroyImmediate(volume);
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
