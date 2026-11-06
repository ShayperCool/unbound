using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Normalized 16-bit Texture2D texels, readback encodings and refused formats, against
    /// the Editor's values (tests/projects/textureformats/Reference/unity-6000.0.71f1.jsonl).</summary>
    public class TextureNorm16TexelsTests
    {
        static readonly Color A = new Color(0.1234567f, 0.7654321f, 0.5f, 0.6789f);
        static readonly Color B = new Color(1.25f, -0.25f, 0.000004f, 0.33333334f);

        static string Bits(Color c) => string.Join(",", Enumerable.Range(0, 4).Select(i => BitConverter.SingleToInt32Bits(c[i]).ToString("x8")));
        static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        [Theory]
        [InlineData(TextureFormat.R16, "3dfcd8fd,3f800000,3f800000,3f800000;3f800000,3f800000,3f800000,3f800000", "9b1fffff")]
        [InlineData(TextureFormat.RG32, "3dfcd8fd,3f43f3c4,3f800000,3f800000;3f800000,00000000,3f800000,3f800000", "9b1ff3c3ffff0000")]
        [InlineData(TextureFormat.RGBA64, "3dfcd8fd,3f43f3c4,3f000080,3f2dccae;3f800000,00000000,00000000,3eaaaaab", "9b1ff3c30080ccadffff000000005555")]
        public void SetPixelKeepsSixteenBits(TextureFormat format, string texels, string raw)
        {
            var t = new Texture2D(2, 1, format, false, true);
            try
            {
                t.SetPixel(0, 0, A);
                t.SetPixel(1, 0, B);
                Assert.Equal(texels, Bits(t.GetPixel(0, 0)) + ";" + Bits(t.GetPixel(1, 0)));
                Assert.Equal(raw, Hex(t.GetRawTextureData()));
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Theory]
        [InlineData(TextureFormat.RG32, "3e402cc0,3ef4aaf5,3f800000,3f800000;3f449fc5,3d6e90ef,3f800000,3f800000")]
        [InlineData(TextureFormat.RGBA64, "3e402cc0,3ef4aaf5,3f449fc5,3d6e90ef;3eb066b0,3f227da2,3f6cc7ed,3e5844d8")]
        public void PixelDataReadsAsSixteenBitTexels(TextureFormat format, string texels)
        {
            var t = new Texture2D(2, 1, format, false, true);
            try
            {
                var bytes = new byte[t.GetRawTextureData().Length];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 37 + 11);
                t.SetPixelData(bytes, 0);
                Assert.Equal(texels, Bits(t.GetPixel(0, 0)) + ";" + Bits(t.GetPixel(1, 0)));
                Assert.Equal(Hex(bytes), Hex(t.GetRawTextureData()));
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Theory]
        [InlineData(GraphicsFormat.A2B10G10R10_UNormPack32)]
        [InlineData(GraphicsFormat.R8G8B8A8_UInt)]
        [InlineData(GraphicsFormat.R32G32B32A32_SInt)]
        public void FormatsTheDeviceCannotSampleAreRefused(GraphicsFormat format)
        {
            var t = new Texture2D(2, 1, format, TextureCreationFlags.None);
            Assert.True(t == null);
        }

        [Fact]
        public void SampledNormalizedFormatsAreCreated()
        {
            var t = new Texture2D(2, 1, GraphicsFormat.R16G16B16A16_UNorm, TextureCreationFlags.None);
            try { Assert.False(t == null); }
            finally { Object.DestroyImmediate(t); }
        }

        [Fact]
        public void ReadbacksIntoSixteenBitFormatsKeepTheirSteps()
        {
            // Four floats per texel, as the renderer reads a texture: 8091 / 65535 and 1.
            var texels = new byte[16];
            BitConverter.GetBytes(8091f / 65535f).CopyTo(texels, 0);
            BitConverter.GetBytes(1f).CopyTo(texels, 12);
            Assert.Equal("9b1f", Hex(ReadbackFormat.Of(GraphicsFormat.R16_UNorm).Encode(texels, 1, true)));
            Assert.Equal("9b1f00000000ffff", Hex(ReadbackFormat.Of(TextureFormat.RGBA64).Encode(texels, 1, true)));
            // From a one-channel source, the channels it lacks read as 1.
            var floats = ReadbackFormat.Of(TextureFormat.RGBAFloat).Encode(texels, 1, true, sourceChannels: 1);
            Assert.Equal(new[] { 8091f / 65535f, 1f, 1f, 1f }, Enumerable.Range(0, 4).Select(i => BitConverter.ToSingle(floats, i * 4)));
        }
    }
}
