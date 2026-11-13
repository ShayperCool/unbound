using System;
using System.Linq;
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
        [InlineData(TextureFormat.ETC_RGB4, 64, 64, 2048)]
        [InlineData(TextureFormat.ETC2_RGB, 2, 2, 8)]
        [InlineData(TextureFormat.ETC2_RGBA8, 20, 12, 240)]
        [InlineData(TextureFormat.EAC_R, 4, 4, 8)]
        [InlineData(TextureFormat.EAC_RG, 5, 3, 32)]
        [InlineData(TextureFormat.RGBA32, 4, 4, 0)]
        public void BlockLevelsAreWholeBlocks(TextureFormat format, int width, int height, int bytes)
        {
            Assert.Equal(bytes, BlockFormats.LevelSize(format, width, height));
        }

        [Theory]
        [InlineData(TextureFormat.DXT1Crunched)]
        [InlineData(TextureFormat.DXT5Crunched)]
        [InlineData(TextureFormat.ETC_RGB4Crunched)]
        [InlineData(TextureFormat.ETC2_RGBA8Crunched)]
        public void NewCrunchTextureKeepsAnEmptyArchive(TextureFormat format)
        {
            var texture = new Texture2D(8, 8, format, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Assert.Equal(format, texture.format);
                Assert.NotEqual(UnityEngine.Experimental.Rendering.GraphicsFormat.None, texture.graphicsFormat);
                Assert.Empty(texture.GetRawTextureData());
                Assert.Equal(0, texture.GetRawTextureData<byte>().Length);
                var view = texture.GetPixelData<byte>(0);
                Assert.Equal(BlockFormats.LevelSize(format, 8, 8), view.Length);
                // Unmeasured bytes beyond an empty archive are safe padding, not a decoder input.
                Assert.Equal(0, view[view.Length - 1]);
                Assert.Empty(texture.GetRawTextureData());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void RetainedBlockCopiesChangeCpuDataWithoutAllocatingPerExecution()
        {
            var source = SerializedDxt1(true);
            var destination = SerializedDxt1(true);
            try
            {
                var view = destination.GetPixelData<byte>(0);
                view[0] = 31;
                view[1] = 0;
                Assert.Equal(Color.blue, destination.GetPixel(0, 0));
                Assert.True(Texture2D.CopyBlockData(source, 0, destination, 0));
                Assert.Equal(Color.white, destination.GetPixel(0, 0));
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    Texture2D.CopyBlockData(source, 0, destination, 0);
                    _ = destination.GetPixel(0, 0);
                }
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
                Assert.Equal(source.GetRawTextureData(), destination.GetRawTextureData());
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(destination); }
        }

        [Fact]
        public void BlockCopyRegionsUseTheirFootprintBeforeDeviceFallback()
        {
            var source = new Texture2D(12, 12, TextureFormat.ASTC_5x5, false, true);
            var destination = new Texture2D(12, 12, TextureFormat.ASTC_5x5, false, true);
            try
            {
                Assert.True(Texture2D.ValidateBlockCopy(source, 0, 0, 0, -1, -1, destination, 0, 0, 0));
                Assert.True(Texture2D.ValidateBlockCopy(source, 0, 5, 0, 5, 5, destination, 0, 0, 5));
                Assert.False(Texture2D.ValidateBlockCopy(source, 0, 4, 0, 4, 4, destination, 0, 0, 4));
                Assert.False(Texture2D.ValidateBlockCopy(source, 1, 0, 0, -1, -1, destination, 0, 0, 0));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(destination); }
        }

        [Theory]
        [InlineData(12, 8, 1, true)]
        [InlineData(12, 8, 4, false)]
        [InlineData(12, 4, 4, true)]
        [InlineData(16, 8, 5, true)]
        [InlineData(20, 12, 5, false)]
        public void AndroidRuntimeChecksIntermediateMipDimensions(int width, int height, int levels, bool valid)
        {
            Assert.Equal(valid, BlockFormats.RuntimeMipChainFits(width, height, levels));
        }

        [Theory]
        [InlineData(TextureFormat.ASTC_4x4, 47, 53)]
        [InlineData(TextureFormat.ASTC_12x12, 52, 58)]
        [InlineData(TextureFormat.ASTC_HDR_5x5, 60, 60)]
        [InlineData(TextureFormat.ASTC_HDR_12x12, 64, 64)]
        public void AstcPacketsPreserveFootprintAndChannel(TextureFormat format, int colour, int linear)
        {
            Assert.Equal(colour, BlockFormats.GpuCode(format, false));
            Assert.Equal(linear, BlockFormats.GpuCode(format, true));
        }

        /// <summary>A serialized (AssetBundle) DXT1 texture: one white block over decoded pixels.</summary>
        static Texture2D SerializedDxt1(bool readable)
        {
            const string yaml = @"Texture2D:
  m_Name: Bundled
  m_Width: 4
  m_Height: 4
  m_TextureFormat: 4
  m_MipCount: 1
  m_SourceTextureFormat: 10
  m_SourceMipCount: 1
  m_SourceTextureData: ffff000000000000
  m_IsReadable: READABLE
  m_ColorSpace: 1
  _typelessdata: ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
";
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            ((ISerializedAsset)texture).ReadSerialized(TestYaml.DocumentBody(yaml.Replace("READABLE", readable ? "1" : "0")), null);
            texture.name = "Bundled";
            return texture;
        }

        /// <summary>The blocks are a compressed texture's storage (tests/projects/texturecompression
        /// write.*): views write them and texel reads decode them; SetPixels and SetPixels32 throw,
        /// SetPixel leaves the texel; copies keep the blocks.</summary>
        [Fact]
        public void CompressedTexturesKeepTheirBlocks()
        {
            var texture = SerializedDxt1(true);
            Assert.Equal(TextureFormat.DXT1, texture.format);
            Assert.Equal(new byte[] { 0xff, 0xff, 0, 0, 0, 0, 0, 0 }, texture.GetRawTextureData());
            var e = Assert.Throws<ArgumentException>(() => texture.SetPixels(new Color[16]));
            Assert.Equal("Texture2D.SetPixels: texture uses an unsupported format. (Texture 'Bundled')", e.Message);
            Assert.Throws<ArgumentException>(() => texture.SetPixels32(new Color32[16]));
            texture.SetPixel(1, 1, Color.red);
            Assert.Equal(Color.white, texture.GetPixel(1, 1));

            var copy = Object.Instantiate(texture);
            Assert.Equal(texture.GetRawTextureData(), copy.GetRawTextureData());
            Object.DestroyImmediate(copy);

            // Colour 0 black, colour 1 white (c0 <= c1: three colours), texel 5 index 1, texel 6
            // index 3 (transparent black), the rest index 0.
            var view = texture.GetPixelData<byte>(0);
            view[0] = 0; view[1] = 0; view[2] = 0xff; view[3] = 0xff;
            view[5] = 0b0011_0100;
            Assert.Equal(new byte[] { 0, 0, 0xff, 0xff, 0, 0b0011_0100, 0, 0 }, texture.GetRawTextureData());
            Assert.Equal(Color.black, texture.GetPixel(0, 0));
            Assert.Equal(Color.white, texture.GetPixel(1, 1));
            Assert.Equal(new Color(0, 0, 0, 0), texture.GetPixel(2, 1));
            Object.DestroyImmediate(texture);
        }

        /// <summary>Compressed textures made by code (tests/projects/texturecompression new.*):
        /// block-sized sides only, blocks of 205 until written.</summary>
        [Fact]
        public void CompressedTexturesMadeByCodeHoldBlocksOf205()
        {
            var e = Assert.Throws<UnityException>(() => new Texture2D(30, 18, TextureFormat.DXT1, false));
            Assert.Equal("Failed to create texture because of invalid parameters.", e.Message);
            var texture = new Texture2D(20, 12, TextureFormat.DXT5, true) { hideFlags = HideFlags.HideAndDontSave };
            Assert.Equal(TextureFormat.DXT5, texture.format);
            Assert.Equal(5, texture.mipmapCount);
            var raw = texture.GetRawTextureData();
            // 20x12, 10x6, 5x3, 2x1, 1x1: 15, 6, 2, 1 and 1 blocks of 16 bytes.
            Assert.Equal((15 + 6 + 2 + 1 + 1) * 16, raw.Length);
            Assert.All(raw, b => Assert.Equal(205, b));
            Assert.Equal(new[] { 240, 96, 32, 16, 16 }, Enumerable.Range(0, 5).Select(m => texture.GetPixelData<byte>(m).Length));
            var shortData = Assert.Throws<UnityException>(() => texture.LoadRawTextureData(new byte[raw.Length - 1]));
            Assert.Equal("LoadRawTextureData: not enough data provided (will result in overread).", shortData.Message);
            Object.DestroyImmediate(texture);
        }

        /// <summary>CopyTexture copies whole levels of blocks between formats with the same block
        /// size and nothing between DXT1 and DXT5 (tests/projects/texturecompression copy.*).</summary>
        [Fact]
        public void CopyTextureCopiesBlocksOfTheSameSize()
        {
            var dxt5 = new Texture2D(8, 8, TextureFormat.DXT5, true) { hideFlags = HideFlags.HideAndDontSave };
            var bc7 = new Texture2D(8, 8, TextureFormat.BC7, true) { hideFlags = HideFlags.HideAndDontSave };
            var dxt1 = new Texture2D(8, 8, TextureFormat.DXT1, true) { hideFlags = HideFlags.HideAndDontSave };
            var blocks = Enumerable.Range(0, dxt5.GetRawTextureData().Length).Select(i => (byte)i).ToArray();
            // Written through a view (no decoding needed).
            var view = dxt5.GetRawTextureData<byte>();
            for (int i = 0; i < blocks.Length; i++) view[i] = blocks[i];
            Graphics.CopyTexture(dxt5, bc7);
            Assert.Equal(blocks, bc7.GetRawTextureData());
            Graphics.CopyTexture(dxt5, dxt1);
            Assert.All(dxt1.GetRawTextureData(), b => Assert.Equal(205, b));
            Object.DestroyImmediate(dxt5);
            Object.DestroyImmediate(bc7);
            Object.DestroyImmediate(dxt1);
        }

        /// <summary>Compress leaves any compressed texture alone (the documentation), also an
        /// AssetBundle texture Unbound keeps decoded (ETC2 here).</summary>
        [Fact]
        public void CompressLeavesCompressedTexturesAlone()
        {
            const string yaml = @"Texture2D:
  m_Width: 4
  m_Height: 4
  m_TextureFormat: 4
  m_MipCount: 1
  m_SourceTextureFormat: 47
  m_SourceMipCount: 1
  m_SourceTextureData: 00000000000000000000000000000000
  m_IsReadable: 1
  m_ColorSpace: 1
  _typelessdata: ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
";
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            ((ISerializedAsset)texture).ReadSerialized(TestYaml.DocumentBody(yaml), null);
            texture.Compress(false);
            Assert.Equal(TextureFormat.ETC2_RGBA8, texture.format);
            Assert.Equal(16, texture.GetRawTextureData().Length);
            Object.DestroyImmediate(texture);
        }

        /// <summary>ETC formats keep their blocks like BC ones, decode natively and go to the
        /// renderer as native packets, with decoding on devices without ETC2.</summary>
        [Theory]
        [InlineData(TextureFormat.ETC_RGB4, 8)]
        [InlineData(TextureFormat.ETC2_RGB, 8)]
        [InlineData(TextureFormat.ETC2_RGBA8, 16)]
        public void EtcFormatsAreBlockFormats(TextureFormat format, int blockBytes)
        {
            Assert.Equal(blockBytes, BlockFormats.BlockBytes(format));
            Assert.True(BlockFormats.NativeDecoded(format));
            int colour = format == TextureFormat.ETC2_RGBA8 ? 67 : 65;
            Assert.Equal(colour, BlockFormats.GpuCode(format, false));
            Assert.Equal(colour + 1, BlockFormats.GpuCode(format, true));
            Assert.False(BlockFormats.NativeDecoded(TextureFormat.DXT1));
        }

        /// <summary>Texture2D.Compress's format: DXT/BC on Standalone; with the Android target
        /// active ETC2_RGBA8 for formats with alpha and ETC2_RGB otherwise
        /// (tests/projects/texturecompression AndroidGamma compress.*).</summary>
        [Theory]
        [InlineData(TextureFormat.RGBA32, false, TextureFormat.DXT5)]
        [InlineData(TextureFormat.RGB24, false, TextureFormat.DXT1)]
        [InlineData(TextureFormat.R8, false, TextureFormat.BC4)]
        [InlineData(TextureFormat.RG16, false, TextureFormat.BC5)]
        [InlineData(TextureFormat.RGBA32, true, TextureFormat.ETC2_RGBA8)]
        [InlineData(TextureFormat.ARGB4444, true, TextureFormat.ETC2_RGBA8)]
        [InlineData(TextureFormat.RGB24, true, TextureFormat.ETC2_RGB)]
        [InlineData(TextureFormat.R8, true, TextureFormat.ETC2_RGB)]
        [InlineData(TextureFormat.RG16, true, TextureFormat.ETC2_RGB)]
        public void CompressTargetsFollowTheBuildTarget(TextureFormat format, bool android, TextureFormat want)
        {
            Assert.Equal(want, BlockFormats.CompressTarget(format, android));
            Assert.Null(BlockFormats.CompressTarget(TextureFormat.RHalf, android));
        }

        /// <summary>Reinitialize to sides a block format cannot have changes nothing
        /// (tests/projects/texturecompression reinit.*): it returns false.</summary>
        [Fact]
        public void ReinitializeRefusesSidesThatAreNotBlockSized()
        {
            var texture = new Texture2D(64, 64, TextureFormat.DXT1, true) { hideFlags = HideFlags.HideAndDontSave };
            Assert.True(texture.Reinitialize(32, 32));
            Assert.Equal((32, 32, 6, TextureFormat.DXT1), (texture.width, texture.height, texture.mipmapCount, texture.format));
            Assert.False(texture.Reinitialize(30, 18, TextureFormat.DXT1, false));
            Assert.Equal((32, 32, 6, TextureFormat.DXT1), (texture.width, texture.height, texture.mipmapCount, texture.format));
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void UnreadableCompressedTexturesAnswerTheirBlocks()
        {
            var texture = SerializedDxt1(false);
            Assert.Equal(new byte[] { 0xff, 0xff, 0, 0, 0, 0, 0, 0 }, texture.GetRawTextureData());
            Assert.Throws<UnityException>(() => texture.GetPixelData<byte>(0));
            Object.DestroyImmediate(texture);
        }

        /// <summary>R8 and RG16 go to the GPU in their channels unless sampled through an sRGB
        /// view (tests/projects/texturecompression channels.*); Alpha8 alone; others as RGBA8.</summary>
        [Fact]
        public void OneAndTwoChannelTexturesUploadTheirChannels()
        {
            int Code(TextureFormat format, bool linear)
            {
                var t = new Texture2D(4, 4, format, false, linear) { hideFlags = HideFlags.HideAndDontSave };
                int code = t.GpuByteFormat();
                Object.DestroyImmediate(t);
                return code;
            }
            Assert.Equal(45, Code(TextureFormat.R8, true));
            Assert.Equal(46, Code(TextureFormat.RG16, true));
            Assert.Equal(3, Code(TextureFormat.Alpha8, false));
            Assert.Equal(1, Code(TextureFormat.RGBA32, true));
            bool gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
            Assert.Equal(gamma ? 45 : 0, Code(TextureFormat.R8, false));
            Assert.Equal(gamma ? 46 : 0, Code(TextureFormat.RG16, false));
            // Render textures of the same formats share the storage, so copies between them work.
            foreach (var (format, code) in new[] { (RenderTextureFormat.R8, 45), (RenderTextureFormat.RG16, 46) })
            {
                var rt = new RenderTexture(4, 4, 0, format) { hideFlags = HideFlags.HideAndDontSave };
                Assert.Equal(code, rt.PacketFormat());
                Object.DestroyImmediate(rt);
            }
            Assert.Equal(45, PixelFormats.GpuChannelFormat(TextureFormat.R8, true));
            Assert.Equal(-1, PixelFormats.GpuChannelFormat(TextureFormat.Alpha8, true));
            var texels = new byte[4];
            PixelFormats.GpuTexels(46, new[] { new Color32(1, 2, 0, 255), new Color32(3, 4, 0, 255) }, texels);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, texels);
        }

        /// <summary>The engine's own reads of an unreadable compressed texture (particle shapes,
        /// terrain details) decode its blocks: a bundle's and one made by code and locked by Apply.</summary>
        [Fact]
        public void EngineReadsOfUnreadableCompressedTexturesDecodeTheirBlocks()
        {
            var bundled = SerializedDxt1(false);
            Assert.All(bundled.EnginePixels32(), p => Assert.Equal(new Color32(255, 255, 255, 255), p));
            Object.DestroyImmediate(bundled);

            var made = new Texture2D(8, 4, TextureFormat.DXT1, false) { hideFlags = HideFlags.HideAndDontSave };
            // Block 0 red, block 1 blue (index 0 = colour 0).
            made.LoadRawTextureData(new byte[] { 0x00, 0xf8, 0, 0, 0, 0, 0, 0, 0x1f, 0x00, 0, 0, 0, 0, 0, 0 });
            made.Apply(false, true);
            Assert.False(made.isReadable);
            var pixels = made.EnginePixels32();
            Assert.Equal(new Color32(255, 0, 0, 255), pixels[1 * 8 + 3]);
            Assert.Equal(new Color32(0, 0, 255, 255), pixels[2 * 8 + 4]);
            Object.DestroyImmediate(made);
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0xf8, 0x1f, 0x00, 0b1110_0100, 0, 0, 0 }, 0, 255, 0, 0)]
        [InlineData(new byte[] { 0x00, 0xf8, 0x1f, 0x00, 0b1110_0100, 0, 0, 0 }, 1, 0, 0, 255)]
        [InlineData(new byte[] { 0x00, 0xf8, 0x1f, 0x00, 0b1110_0100, 0, 0, 0 }, 2, 170, 0, 85)]
        [InlineData(new byte[] { 0x00, 0xf8, 0x1f, 0x00, 0b1110_0100, 0, 0, 0 }, 3, 85, 0, 170)]
        public void Dxt1TexelsUseTruncatingThirds(byte[] block, int k, byte r, byte g, byte b)
        {
            Assert.Equal(new Color32(r, g, b, 255), BlockFormats.DecodeTexel(TextureFormat.DXT1, block, k));
        }

        [Fact]
        public void AlphaStepsRoundToNearest()
        {
            // a0 = 255 > a1 = 0, texel 0 index 2: (255 * 6 + 3) / 7 = 219.
            var block = new byte[] { 255, 0, 2, 0, 0, 0, 0, 0 };
            Assert.Equal(new Color32(219, 0, 0, 255), BlockFormats.DecodeTexel(TextureFormat.BC4, block, 0));
            // a0 = 0 <= a1 = 255: index 6 is 0, 7 is 255; index 2 is (0 * 4 + 255 + 2) / 5 = 51.
            Assert.Equal(new Color32(51, 0, 0, 255), BlockFormats.DecodeTexel(TextureFormat.BC4, new byte[] { 0, 255, 2, 0, 0, 0, 0, 0 }, 0));
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
