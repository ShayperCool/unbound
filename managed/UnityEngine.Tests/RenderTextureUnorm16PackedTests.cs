using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The renderer formats of normalized 16-bit and packed 10-10-10-2 textures: their own
    /// packet codes (13 RG16, 14 RGBA16, 15 RGB10A2, 16 R16), not RGBA8 (nor R32F for R16). Which precision the GPU then keeps
    /// depends on the device (the renderer falls back to RGBA8, with a warning, where it cannot);
    /// these tests check only the code the managed side asks for.</summary>
    public class RenderTextureUnorm16PackedTests
    {
        static int PacketFormatOf(RenderTextureDescriptor descriptor)
        {
            var rt = new RenderTexture(descriptor);
            try { return rt.PacketFormat(); }
            finally { Object.DestroyImmediate(rt); }
        }

        [Theory]
        [InlineData(RenderTextureFormat.RG32, RenderTextureReadWrite.Linear, 13)]
        [InlineData(RenderTextureFormat.RG32, RenderTextureReadWrite.sRGB, 13)]
        [InlineData(RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear, 14)]
        [InlineData(RenderTextureFormat.ARGB64, RenderTextureReadWrite.sRGB, 14)]
        [InlineData(RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear, 15)]
        [InlineData(RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.sRGB, 15)]
        [InlineData(RenderTextureFormat.R16, RenderTextureReadWrite.Linear, 16)]
        [InlineData(RenderTextureFormat.R16, RenderTextureReadWrite.sRGB, 16)]
        public void RenderTextureFormatsGetTheirOwnCode(RenderTextureFormat format, RenderTextureReadWrite readWrite, int code)
        {
            // These formats have no sRGB variant: the read-write mode does not change them.
            Assert.Equal(code, PacketFormatOf(new RenderTextureDescriptor(8, 4, format, 0, 1, readWrite)));
        }

        [Theory]
        [InlineData(GraphicsFormat.R16G16_UNorm, 13)]
        [InlineData(GraphicsFormat.R16G16B16A16_UNorm, 14)]
        [InlineData(GraphicsFormat.A2B10G10R10_UNormPack32, 15)]
        [InlineData(GraphicsFormat.R16_UNorm, 16)]
        public void GraphicsFormatsGetTheirOwnCode(GraphicsFormat format, int code)
        {
            Assert.Equal(code, PacketFormatOf(new RenderTextureDescriptor(8, 4, format, GraphicsFormat.None)));
            // A depth buffer, MSAA or kernel writes do not change the code (the renderer decides
            // whether the device can keep the format for that use).
            var withUses = new RenderTextureDescriptor(8, 4, format, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                msaaSamples = 4,
                enableRandomWrite = true,
            };
            Assert.Equal(code, PacketFormatOf(withUses));
        }

        [Theory]
        [InlineData(GraphicsFormat.R8G8B8A8_SRGB, 0)]
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, 1)]
        [InlineData(GraphicsFormat.R16G16B16A16_SFloat, 2)]
        [InlineData(GraphicsFormat.B10G11R11_UFloatPack32, 4)]
        [InlineData(GraphicsFormat.R32_SFloat, 6)]
        [InlineData(GraphicsFormat.R16G16_SFloat, 12)]
        public void OtherFormatsKeepTheirCodes(GraphicsFormat format, int code)
        {
            Assert.Equal(code, PacketFormatOf(new RenderTextureDescriptor(8, 4, format, GraphicsFormat.None)));
        }

        [Theory]
        [InlineData(GraphicsFormat.R16G16B16A16_UInt)]
        [InlineData(GraphicsFormat.R32G32B32A32_SInt)]
        [InlineData(GraphicsFormat.R16G16_SNorm)]
        [InlineData(GraphicsFormat.A2R10G10B10_UNormPack32)]
        public void OnlyTheNormalizedFormatsGetTheNewCodes(GraphicsFormat format)
        {
            // Integer, signed and other packed formats are not part of this change.
            Assert.DoesNotContain(PacketFormatOf(new RenderTextureDescriptor(8, 4, format, GraphicsFormat.None)), new[] { 13, 14, 15, 16 });
        }

        [Theory]
        [InlineData(TextureFormat.RG32, 13)]
        [InlineData(TextureFormat.RGBA64, 14)]
        [InlineData(TextureFormat.R16, 16)]
        public void Texture2DFormatsShareTheRenderTextureCodes(TextureFormat format, int code)
        {
            // Their CPU texels are 16-bit cells, uploaded in the render texture's code.
            Assert.True(Texture2DTexels.FloatStored(format));
            Assert.Equal(code, PixelFormats.GpuFloatFormat(format));
            // A Texture2D and a render texture of one graphics format get one code (Graphics.CopyTexture
            // between them needs the same GPU format), linear or not.
            foreach (bool srgb in new[] { false, true })
            {
                var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, srgb);
                Assert.Equal(code, PixelFormats.GpuNormFormat(graphics));
                Assert.Equal(code, PacketFormatOf(new RenderTextureDescriptor(8, 4, graphics, GraphicsFormat.None)));
            }
        }
    }
}
