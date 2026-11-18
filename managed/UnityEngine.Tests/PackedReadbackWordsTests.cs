using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The packed words AsyncGPUReadback reads from a packed target in its own format,
    /// as the macOS Editor's Metal device returns them (tests/projects/textureformats metal.jsonl
    /// readback.*.async: targets cleared to 0.25, 0.5, 0.75, 0.6, each texel's word in little-endian
    /// bytes), from the values Unbound stores.</summary>
    public class PackedReadbackWordsTests
    {
        static byte[] Floats(params float[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 4, 4), values[i]);
            return bytes;
        }

        static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        [Theory]
        // 4-bit targets are stored at 8 bits per channel: the cleared bytes 64, 128, 191, 153.
        [InlineData(GraphicsFormat.R4G4B4A4_UNormPack16, 64 / 255f, 128 / 255f, 191 / 255f, 153 / 255f, 4, "b948")]
        [InlineData(GraphicsFormat.B4G4R4A4_UNormPack16, 64 / 255f, 128 / 255f, 191 / 255f, 153 / 255f, 4, "8b94")]
        [InlineData(GraphicsFormat.B5G6R5_UNormPack16, 64 / 255f, 128 / 255f, 191 / 255f, 1f, 3, "1744")]
        [InlineData(GraphicsFormat.A2B10G10R10_UNormPack32, 256 / 1023f, 512 / 1023f, 767 / 1023f, 2 / 3f, 4, "0001f8af")]
        [InlineData(GraphicsFormat.B10G11R11_UFloatPack32, 0.25f, 0.5f, 0.75f, 1f, 3, "40031c74")]
        [InlineData(GraphicsFormat.E5B9G9R9_UFloatPack32, 0.25f, 0.5f, 0.75f, 1f, 3, "8000027e")]
        public void APackedTargetReadsItsWords(GraphicsFormat format, float r, float g, float b, float a, int channels, string word)
        {
            var packed = ReadbackFormat.Packed(format);
            Assert.NotNull(packed);
            Assert.Equal(word + word, Hex(packed.Encode(Floats(r, g, b, a, r, g, b, a), 2, true, sourceChannels: channels)));
        }

        [Fact]
        public void UnsignedFloatsRoundCarryAndSaturate()
        {
            // A mantissa that rounds up carries into the exponent; below the smallest normal value
            // the field is denormal; past the largest finite value it saturates; infinity and NaN
            // are the format's own; negatives are 0.
            Assert.Equal(16u << 6, ReadbackFormat.UnsignedFloat(1.99999f, 6));
            Assert.Equal(1u << 5, ReadbackFormat.UnsignedFloat(MathF.Pow(2, -15), 6));
            Assert.Equal((30u << 6) | 63u, ReadbackFormat.UnsignedFloat(1e9f, 6));
            Assert.Equal(0u, ReadbackFormat.UnsignedFloat(-1f, 6));
            Assert.Equal(31u << 6, ReadbackFormat.UnsignedFloat(float.PositiveInfinity, 6));
            Assert.Equal((31u << 5) | 31u, ReadbackFormat.UnsignedFloat(float.NaN, 5));
            Assert.Equal(0u, ReadbackFormat.SharedExponent(0f, 0f, 0f));
        }

        [Fact]
        public void OnlyTheMeasuredDeviceReadsPackedWords()
        {
            var previous = GraphicsDeviceProfile.Current;
            var rt = new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormat.B4G4R4A4_UNormPack16, 0));
            try
            {
                GraphicsDeviceProfile.Select("Metal");
                Assert.NotNull(AsyncReadbacks.Own(rt));
                // Controls: a texture of a packed format (unmeasured) keeps failing, XR's words are
                // not decoded, and the other devices are unmeasured.
                var texture = new Texture2D(2, 1, TextureFormat.ARGB4444, false);
                Assert.Null(AsyncReadbacks.Own(texture));
                Object.DestroyImmediate(texture);
                var xr = new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormat.R10G10B10_XRUNormPack32, 0));
                Assert.Null(AsyncReadbacks.Own(xr));
                Object.DestroyImmediate(xr);
                foreach (var device in new[] { "OpenGLCore", "Vulkan" })
                {
                    GraphicsDeviceProfile.Select(device);
                    Assert.Null(AsyncReadbacks.Own(rt));
                }
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
                Object.DestroyImmediate(rt);
            }
        }
    }
}
