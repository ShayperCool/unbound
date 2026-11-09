using System;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>tests/projects/texturecompression: two identical Unity 6000.0.71f1 captures.</summary>
    public class TextureCompressionTests
    {
        /// <summary>Uncompressed integer and normalized formats are supported through a CPU
        /// conversion; compressed and float formats need the device's own support.</summary>
        [Theory]
        [InlineData(TextureFormat.ARGB4444, true)]
        [InlineData(TextureFormat.R8_SIGNED, true)]
        [InlineData(TextureFormat.RGBA64_SIGNED, true)]
        [InlineData(TextureFormat.EAC_R, true)]
        [InlineData(TextureFormat.EAC_RG_SIGNED, true)]
        [InlineData(TextureFormat.DXT1, true)]
        [InlineData(TextureFormat.BC7, true)]
        [InlineData(TextureFormat.RGBAFloat, true)]
        [InlineData(TextureFormat.ASTC_4x4, false)]
        [InlineData(TextureFormat.PVRTC_RGB4, false)]
        [InlineData(TextureFormat.YUY2, false)]
        public void SupportsTextureFormatAsTheEditor(TextureFormat format, bool supported)
        {
            Assert.Equal(supported, SystemInfo.SupportsTextureFormat(format));
        }

        [Theory]
        [InlineData(TextureFormat.DXT1, 64, 64, 2048)]
        [InlineData(TextureFormat.DXT5, 64, 64, 4096)]
        [InlineData(TextureFormat.BC4, 2, 2, 8)]
        [InlineData(TextureFormat.BC7, 1, 1, 16)]
        [InlineData(TextureFormat.DXT5, 20, 12, 240)]
        [InlineData(TextureFormat.RGBA32, 4, 4, 0)]
        public void BlockLevelsAreWholeBlocks(TextureFormat format, int width, int height, int bytes)
        {
            Assert.Equal(bytes, BlockFormats.LevelSize(format, width, height));
        }

        /// <summary>GetPixels of an unreadable texture has no data to read, compressed or not.</summary>
        [Fact]
        public void GetPixelsOfAnUnreadableTextureHasNoData()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Locked", hideFlags = HideFlags.HideAndDontSave };
            texture.Apply(false, true);
            var e = Assert.Throws<ArgumentException>(() => texture.GetPixels());
            Assert.Equal("Texture2D.GetPixels: texture data is either not readable, corrupted or does not exist. (Texture 'Locked')", e.Message);
            // GetPixelData keeps the not-readable error.
            Assert.Throws<UnityException>(() => texture.GetPixelData<byte>(0));
            Object.DestroyImmediate(texture);
        }
    }
}
