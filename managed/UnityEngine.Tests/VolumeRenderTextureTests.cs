using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class VolumeRenderTextureTests
    {
        [Fact]
        public void DescriptorConvenienceConstructorsRequestTheFullMipChain()
        {
            Assert.Equal(-1, Texture.GenerateAllMips);
            var descriptors = new[]
            {
                new RenderTextureDescriptor(4, 2),
                new RenderTextureDescriptor(4, 2, RenderTextureFormat.ARGB32, 0),
                new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_UNorm, 0),
                new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None)
            };
            foreach (var descriptor in descriptors)
            {
                Assert.Equal(Texture.GenerateAllMips, descriptor.mipCount);
                Assert.False(descriptor.sRGB);
            }
        }

        [Theory]
        [InlineData(ColorSpace.Gamma, false)]
        [InlineData(ColorSpace.Linear, true)]
        public void TextureConstructorsKeepTheProjectsDefaultEncoding(ColorSpace space, bool srgb)
        {
            var previous = QualitySettings.s_ColorSpace;
            QualitySettings.s_ColorSpace = space;
            var implicitDefault = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGB32);
            var explicitDefault = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var custom = new CustomRenderTexture(4, 2);
            var linear = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Assert.Equal(srgb, implicitDefault.sRGB);
                Assert.Equal(srgb, explicitDefault.sRGB);
                Assert.Equal(srgb, custom.sRGB);
                Assert.False(linear.sRGB);
                Assert.False(new RenderTextureDescriptor(4, 2, RenderTextureFormat.ARGB32, 0).sRGB);
            }
            finally
            {
                foreach (var texture in new RenderTexture[] { implicitDefault, explicitDefault, custom, linear }) Object.DestroyImmediate(texture);
                QualitySettings.s_ColorSpace = previous;
            }
        }

        [Theory]
        [InlineData(TextureDimension.Tex2D, 1, -1, 3)]
        [InlineData(TextureDimension.Tex3D, 1, -1, 3)]
        [InlineData(TextureDimension.Tex3D, 8, -1, 3)]
        [InlineData(TextureDimension.Tex3D, 8, 1, 1)]
        public void RenderTextureMipsFollowWidthAndHeight(TextureDimension dimension, int depth, int requested, int expected)
        {
            var descriptor = new RenderTextureDescriptor(4, 2, RenderTextureFormat.ARGBFloat, 0, requested)
            { dimension = dimension, volumeDepth = depth, useMipMap = true, autoGenerateMips = false };
            var texture = new RenderTexture(descriptor);
            try { Assert.Equal(expected, texture.mipmapCount); }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
