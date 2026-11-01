using System;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class TexturePixelStorageAllocationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReadingHdrPixelsAllocatesTheResultWithoutTemporaryArraysPerTexel(bool cube)
        {
            const int width = 64;
            var texture = cube ? (Texture)new Cubemap(width, TextureFormat.RGBAFloat, false) : new Texture2D(width, width, TextureFormat.RGBAFloat, false);
            try
            {
                if (cube) ((Cubemap)texture).GetPixelData<byte>(0, CubemapFace.PositiveX);
                else ((Texture2D)texture).GetPixelData<byte>(0);
                Color[] Read() => cube ? ((Cubemap)texture).GetPixels(CubemapFace.PositiveX) : ((Texture2D)texture).GetPixels();
                Read();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var pixels = Read();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(width * width, pixels.Length);
                Assert.InRange(allocated, width * width * 16L, width * width * 16L + 4096);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Theory]
        [InlineData(TextureFormat.RGBA32)]
        [InlineData(TextureFormat.RGBAFloat)]
        public void CubemapStoreCreationAllocatesItsStorageWithoutTemporaryArraysPerTexel(TextureFormat format)
        {
            const int width = 64;
            var warm = new Cubemap(1, format, false);
            warm.GetPixelData<byte>(0, CubemapFace.PositiveX);
            Object.DestroyImmediate(warm);
            var cube = new Cubemap(width, format, false);
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var data = cube.GetPixelData<byte>(0, CubemapFace.PositiveX);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                long storage = 6L * width * width * (format == TextureFormat.RGBA32 ? 4 : 16);
                Assert.InRange(allocated, storage, storage + 16384);
                Assert.Equal(storage / 6, data.Length);
            }
            finally { Object.DestroyImmediate(cube); }
        }
    }
}
