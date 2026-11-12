using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The depth buffer a render texture's depth-stencil format is stored with: 16 bits
    /// alone as 16 (no stencil, as documented for RenderTexture.depth), a 16-bit format with
    /// stencil with 24 bits and stencil (tests/projects/samplerstates Stencil.*), others as they
    /// say.</summary>
    public class RenderTextureDepthBufferTests
    {
        [Theory]
        [InlineData(GraphicsFormat.None, 0)]
        [InlineData(GraphicsFormat.D16_UNorm, 16)]
        [InlineData(GraphicsFormat.D16_UNorm_S8_UInt, 24)]
        [InlineData(GraphicsFormat.D24_UNorm_S8_UInt, 24)]
        [InlineData(GraphicsFormat.D32_SFloat, 32)]
        public void FormatsAreStoredWithTheirDepthBits(GraphicsFormat format, int bits) =>
            Assert.Equal(bits, RenderTexture.StoredDepthBits(format));

        /// <summary>A format the device cannot render is kept as its compatible one, a format it
        /// renders as it is.</summary>
        [Fact]
        public void AnUnrenderableDepthFormatIsKeptAsItsCompatibleOne()
        {
            var format = GraphicsFormat.D16_UNorm_S8_UInt;
            var expected = SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render) ? format
                : SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);
            if (expected == GraphicsFormat.None) expected = format;
            var texture = new RenderTexture(2, 1, GraphicsFormat.R8G8B8A8_UNorm, format);
            try
            {
                Assert.Equal(expected, texture.depthStencilFormat);
                texture.depthStencilFormat = GraphicsFormat.D16_UNorm;
                Assert.Equal(GraphicsFormat.D16_UNorm, texture.depthStencilFormat);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>A temporary texture of a format kept as another is found again in the pool.</summary>
        [Fact]
        public void ATemporaryOfAConvertedFormatIsReused()
        {
            var desc = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D16_UNorm_S8_UInt);
            var first = RenderTexture.GetTemporary(desc);
            RenderTexture.ReleaseTemporary(first);
            var second = RenderTexture.GetTemporary(desc);
            try { Assert.Same(first, second); }
            finally { RenderTexture.ReleaseTemporary(second); }
        }

        static GraphicsFormat Compatible(GraphicsFormat format)
        {
            if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)) return format;
            var compatible = SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);
            return compatible == GraphicsFormat.None ? format : compatible;
        }

        /// <summary>A render texture asset keeps the compatible format of one the device cannot
        /// render unless m_EnableCompatibleFormat is 0 (on when the field is missing); the
        /// descriptor setter converts as the constructor does.</summary>
        [Theory]
        [InlineData("  m_EnableCompatibleFormat: 1\n", true)]
        [InlineData("  m_EnableCompatibleFormat: 0\n", false)]
        [InlineData("", true)]
        public void AssetsKeepTheCompatibleFormatUnlessTheySayNot(string flag, bool converted)
        {
            var format = GraphicsFormat.D16_UNorm_S8_UInt;
            var body = UnityEngine.Tests.TestYaml.DocumentBody("RenderTexture:\n  m_Width: 8\n  m_Height: 4\n  m_ColorFormat: 8\n  m_DepthStencilFormat: "
                + (int)format + "\n" + flag);
            var texture = (RenderTexture)Activator.CreateInstance(typeof(RenderTexture), nonPublic: true);
            try
            {
                ((Unbound.Runtime.ISerializedAsset)texture).ReadSerialized(body, null);
                Assert.Equal(converted ? Compatible(format) : format, texture.depthStencilFormat);
                texture.descriptor = new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_UNorm, format);
                Assert.Equal(Compatible(format), texture.depthStencilFormat);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void ADepthOf16HasNoStencilAndKeepsItsFormat()
        {
            var texture = new RenderTexture(2, 1, 16);
            try
            {
                Assert.Equal(GraphicsFormat.D16_UNorm, texture.depthStencilFormat);
                Assert.Equal(16, RenderTexture.StoredDepthBits(texture.depthStencilFormat));
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
