using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>AsyncGPUReadback of packed 10-10-10-2 sources and the packet codes of render
    /// textures kernels write. The renderer decides per device whether it keeps a format; these
    /// tests check only what the managed side asks for and how it encodes what it reads.</summary>
    public class FormatsReadbackComputeTests
    {
        [Theory]
        // Packed formats: one byte per component in the block, but not 8 bits per channel.
        [InlineData(GraphicsFormat.A2B10G10R10_UNormPack32, false)]
        [InlineData(GraphicsFormat.A2R10G10B10_UNormPack32, false)]
        [InlineData(GraphicsFormat.A2B10G10R10_UIntPack32, false)]
        // 8 bits per channel: read as the stored bytes.
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, true)]
        [InlineData(GraphicsFormat.R8G8B8A8_SRGB, true)]
        [InlineData(GraphicsFormat.B8G8R8A8_UNorm, true)]
        [InlineData(GraphicsFormat.R8G8B8_UNorm, true)]
        [InlineData(GraphicsFormat.R8G8_UNorm, true)]
        [InlineData(GraphicsFormat.R8_UNorm, true)]
        // Wider or float formats: read as floats.
        [InlineData(GraphicsFormat.R16G16_UNorm, false)]
        [InlineData(GraphicsFormat.R16G16B16A16_UNorm, false)]
        [InlineData(GraphicsFormat.R16G16B16A16_SFloat, false)]
        [InlineData(GraphicsFormat.R32G32B32A32_SFloat, false)]
        [InlineData(GraphicsFormat.B10G11R11_UFloatPack32, false)]
        [InlineData(GraphicsFormat.B5G6R5_UNormPack16, false)]
        public void ReadbackReadsOnlyEightBitFormatsAsBytes(GraphicsFormat format, bool eightBit)
        {
            Assert.Equal(eightBit, AsyncReadbacks.EightBit(format));
        }

        [Fact]
        public void TenBitRenderTexturesReadBackAsFloats()
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(8, 4, RenderTextureFormat.ARGB2101010, 0));
            try
            {
                Assert.Equal(GraphicsFormat.A2B10G10R10_UNormPack32, rt.graphicsFormat);
                Assert.False(AsyncReadbacks.EightBit(rt.graphicsFormat));
            }
            finally { Object.DestroyImmediate(rt); }
        }

        static byte[] Floats(params float[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 4, 4), values[i]);
            return bytes;
        }

        static float FloatAt(byte[] bytes, int channel) => BitConverter.ToSingle(bytes, channel * 4);

        /// <summary>A 10-bit step as the renderer reads it back in floats (r 1/1023, g 1022/1023,
        /// b 512/1023, a 2/3) keeps its value in each requested format's precision.</summary>
        [Fact]
        public void FloatReadbacksKeepTenBitSteps()
        {
            float r = 1f / 1023f, g = 1022f / 1023f, b = 512f / 1023f, a = 2f / 3f;
            var texel = Floats(r, g, b, a);

            var rgbaFloat = ReadbackFormat.Of(TextureFormat.RGBAFloat).Encode(texel, 1, floats: true);
            Assert.Equal(new[] { r, g, b, a }, new[] { FloatAt(rgbaFloat, 0), FloatAt(rgbaFloat, 1), FloatAt(rgbaFloat, 2), FloatAt(rgbaFloat, 3) });

            var rFloat = ReadbackFormat.Of(GraphicsFormat.R32_SFloat).Encode(texel, 1, floats: true);
            Assert.Equal(r, FloatAt(rFloat, 0));

            var rgbaHalf = ReadbackFormat.Of(TextureFormat.RGBAHalf).Encode(texel, 1, floats: true);
            Assert.Equal((Half)r, BitConverter.ToHalf(rgbaHalf, 0));
            Assert.Equal((Half)g, BitConverter.ToHalf(rgbaHalf, 2));

            // 8-bit requests round each channel to the nearest byte.
            Assert.Equal(new byte[] { 0, 255, 128, 170 }, ReadbackFormat.Of(TextureFormat.RGBA32).Encode(texel, 1, floats: true));
            Assert.Equal(new byte[] { 170, 0, 255, 128 }, ReadbackFormat.Of(TextureFormat.ARGB32).Encode(texel, 1, floats: true));
        }

        /// <summary>Why the classification matters: the byte path carries 8 bits, so a 10-bit step
        /// below 1/255 reads as 0 there.</summary>
        [Fact]
        public void TheBytePathCannotCarryATenBitStep()
        {
            // Bytes as the renderer would round r = 1/1023 to 8 bits.
            var bytes = new byte[] { (byte)Math.Round(255.0 / 1023.0), 255, 128, 170 };
            var rgbaFloat = ReadbackFormat.Of(TextureFormat.RGBAFloat).Encode(bytes, 1, floats: false);
            Assert.Equal(0f, FloatAt(rgbaFloat, 0));
        }

        static int PacketFormatOf(RenderTextureDescriptor descriptor)
        {
            var rt = new RenderTexture(descriptor);
            try { return rt.PacketFormat(); }
            finally { Object.DestroyImmediate(rt); }
        }

        [Theory]
        [InlineData(RenderTextureFormat.RG32, 13)]
        [InlineData(RenderTextureFormat.ARGB64, 14)]
        [InlineData(RenderTextureFormat.ARGB2101010, 15)]
        public void KernelWrittenRenderTexturesAskForTheirOwnFormat(RenderTextureFormat format, int code)
        {
            // Whether the device lets kernels write the format is the renderer's decision (it falls
            // back to RGBA8, with a warning, where it cannot): the managed side always asks.
            var descriptor = new RenderTextureDescriptor(8, 4, format, 0) { enableRandomWrite = true };
            Assert.Equal(code, PacketFormatOf(descriptor));
        }
    }
}
