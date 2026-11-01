using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class LayeredTextureTests
    {
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
