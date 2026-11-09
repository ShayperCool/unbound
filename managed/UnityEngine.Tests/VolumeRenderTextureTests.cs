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
        [InlineData("6000.0.71f1", TextureDimension.Tex2D, 1)]
        [InlineData("6000.0.71f1", TextureDimension.Tex3D, 3)]
        [InlineData("6000.0.70f1", TextureDimension.Tex2D, 3)]
        public void AllocatedMipCountDistinguishesMultisampledAndVolumeResources(string version, TextureDimension dimension, int expected)
        {
            var previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = version;
            var texture = new RenderTexture(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)
            { dimension = dimension, volumeDepth = dimension == TextureDimension.Tex3D ? 4 : 1, msaaSamples = 4, useMipMap = true });
            try
            {
                Assert.Equal(3, texture.mipmapCount);
                Assert.True(texture.Create());
                Assert.Equal(expected, texture.mipmapCount);
                texture.Release();
                Assert.Equal(expected, texture.mipmapCount);
            }
            finally { Object.DestroyImmediate(texture); ProjectInfo.UnityVersion = previous; }
        }

        [Fact]
        public void MipMetadataKeepsTheAllocatedCountUntilDimensionsOrMipSettingsChange()
        {
            var previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var texture = new RenderTexture(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)
            { msaaSamples = 4, useMipMap = true });
            try
            {
                texture.Create(); texture.Release(); texture.antiAliasing = 1;
                Assert.Equal(1, texture.mipmapCount);
                texture.Create(); Assert.Equal(3, texture.mipmapCount);
                texture.Release(); texture.antiAliasing = 4;
                texture.Create(); texture.Release(); texture.useMipMap = false; texture.useMipMap = true;
                Assert.Equal(3, texture.mipmapCount);
                texture.Create(); texture.Release(); texture.width = 8;
                Assert.Equal(4, texture.mipmapCount);
                texture.Create(); texture.Release(); texture.height = 16;
                Assert.Equal(5, texture.mipmapCount);
                texture.Create(); texture.Release(); var descriptor = texture.descriptor; descriptor.width = 32; texture.descriptor = descriptor;
                Assert.Equal(6, texture.mipmapCount);
            }
            finally { Object.DestroyImmediate(texture); ProjectInfo.UnityVersion = previous; }
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
