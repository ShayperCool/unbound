using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>RenderTextureDescriptor.colorFormat's setter: the format in the descriptor's own
    /// encoding, as the device renders it; a Shadowmap compares, any other format does not.</summary>
    public class RenderTextureDescriptorColorFormatTests
    {
        static GraphicsFormat Rendered(GraphicsFormat format) => SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);

        [Fact]
        public void ADescriptorKeepsItsOwnEncodingWhateverTheColorSpace()
        {
            var space = QualitySettings.activeColorSpace;
            try
            {
                foreach (var colorSpace in new[] { ColorSpace.Linear, ColorSpace.Gamma })
                {
                    QualitySettings.s_ColorSpace = colorSpace;
                    var linear = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, 0);
                    linear.colorFormat = RenderTextureFormat.ARGBHalf;
                    linear.colorFormat = RenderTextureFormat.ARGB32;
                    Assert.Equal(Rendered(GraphicsFormat.R8G8B8A8_UNorm), linear.graphicsFormat);
                    Assert.False(linear.sRGB);
                    var srgb = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_SRGB, 0);
                    srgb.colorFormat = RenderTextureFormat.ARGB32;
                    Assert.Equal(Rendered(GraphicsFormat.R8G8B8A8_SRGB), srgb.graphicsFormat);
                }
            }
            finally { QualitySettings.s_ColorSpace = space; }
        }

        [Fact]
        public void OnlyAShadowmapCompares()
        {
            var d = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, 24);
            d.colorFormat = RenderTextureFormat.Shadowmap;
            Assert.Equal(ShadowSamplingMode.CompareDepths, d.shadowSamplingMode);
            d.colorFormat = RenderTextureFormat.ARGB32;
            Assert.Equal(ShadowSamplingMode.None, d.shadowSamplingMode);
            d.colorFormat = RenderTextureFormat.Depth;
            Assert.Equal(ShadowSamplingMode.None, d.shadowSamplingMode);
        }
    }
}
