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
        // XR targets are stored as half floats: 0.25, 0.5, 0.75 exactly (codes 511.5 and 766.5 round to even).
        [InlineData(GraphicsFormat.R10G10B10_XRUNormPack32, 0.25f, 0.5f, 0.75f, 1f, 3, "fefe09e0")]
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
                // Controls: a texture of a packed format (unmeasured) keeps failing, another XR
                // layout (unmeasured) too, and the other devices are unmeasured.
                var texture = new Texture2D(2, 1, TextureFormat.ARGB4444, false);
                Assert.Null(AsyncReadbacks.Own(texture));
                Object.DestroyImmediate(texture);
                var xr = new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormat.R10G10B10_XRUNormPack32, 0));
                Assert.NotNull(AsyncReadbacks.Own(xr));
                Object.DestroyImmediate(xr);
                var bgraXr = new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormat.A10R10G10B10_XRUNormPack32, 0));
                Assert.Null(AsyncReadbacks.Own(bgraXr));
                Object.DestroyImmediate(bgraXr);
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

        static float[] Values(byte[] bytes)
        {
            var values = new float[bytes.Length / 4];
            for (int i = 0; i < values.Length; i++) values[i] = BitConverter.ToSingle(bytes, i * 4);
            return values;
        }

        [Fact]
        public void ReadsOfALowPrecisionTargetKeepItsChannelBits()
        {
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select("Metal");
                // The cleared bytes 64, 128, 191, 153 read as the macOS Editor's Metal device reads
                // the target (textureformats metal.jsonl readback.*.read.RGBAFloat): 4/15, 8/15, 11/15,
                // 9/15 and 8/31, 32/63, 23/31 (alpha, which 5-6-5 lacks, as it was).
                var stored = new[] { 64 / 255f, 128 / 255f, 191 / 255f, 153 / 255f };
                foreach (var format in new[] { GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.B4G4R4A4_UNormPack16 })
                {
                    var texels = Floats(stored);
                    RenderTexture.QuantizeReadTexels(format, texels);
                    Assert.Equal(new[] { 4 / 15f, 8 / 15f, 11 / 15f, 9 / 15f }, Values(texels));
                }
                foreach (var format in new[] { GraphicsFormat.R5G6B5_UNormPack16, GraphicsFormat.B5G6R5_UNormPack16 })
                {
                    var texels = Floats(stored);
                    RenderTexture.QuantizeReadTexels(format, texels);
                    Assert.Equal(new[] { 8 / 31f, 32 / 63f, 23 / 31f, 153 / 255f }, Values(texels));
                }
                // An XR target reads its codes over 1023 at 8 bits (alpha as it was).
                var xr = Floats(0.25f, 0.5f, 0.75f, 1f);
                RenderTexture.QuantizeReadTexels(GraphicsFormat.R10G10B10_XRUNormPack32, xr);
                Assert.Equal(new[] { 128 / 255f, 159 / 255f, 191 / 255f, 1f }, Values(xr));
                // Out of range values clamp, XR's within its extended range; an 8-bit format keeps its texels.
                Assert.Equal(0u, RenderTexture.ExtendedRangeCode(-2f));
                Assert.Equal(1023u, RenderTexture.ExtendedRangeCode(2f));
                Assert.Equal(894u, RenderTexture.ExtendedRangeCode(1f));
                var outside = Floats(-0.5f, 1.5f, 0f, 1f);
                RenderTexture.QuantizeReadTexels(GraphicsFormat.R4G4B4A4_UNormPack16, outside);
                Assert.Equal(new[] { 0f, 1f, 0f, 1f }, Values(outside));
                var plain = Floats(stored);
                RenderTexture.QuantizeReadTexels(GraphicsFormat.R8G8B8A8_UNorm, plain);
                Assert.Equal(stored, Values(plain));
                // The other devices' reads are unmeasured: the stored values are read, whether
                // their caps say they draw into the format or not.
                foreach (var device in new[] { "OpenGLCore", "Vulkan" })
                {
                    GraphicsDeviceProfile.Select(device);
                    foreach (var format in new[] { GraphicsFormat.R5G6B5_UNormPack16, GraphicsFormat.R4G4B4A4_UNormPack16,
                                                   GraphicsFormat.R10G10B10_XRUNormPack32 })
                    {
                        var texels = Floats(stored);
                        RenderTexture.QuantizeReadTexels(format, texels);
                        Assert.Equal(stored, Values(texels));
                    }
                }
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }
    }
}
