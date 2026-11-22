using System;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        /// <summary>Whether the texture's own format is block-compressed (an import or a bundle
        /// texture kept as its blocks, <see cref="m_SourceRaw"/>; the pixels are what the blocks
        /// decode to).</summary>
        bool SourceBlocks => m_SourceFormat is TextureFormat f && BlockFormats.BlockBytes(f) > 0;

        /// <summary>Whether a pixel data store holds the texels (rather than a compressed
        /// texture's blocks, which the cells do not read).</summary>
        bool CellStore => m_Store != null && !SourceBlocks;

        /// <summary>The blocks of a block-compressed texture's mip chain: an import's are read from
        /// the import when first asked for.</summary>
        byte[] SourceRawData()
        {
            if (m_SourceRaw == null && m_AssetGuid != null)
            {
                var json = Native.Call("texture-raw", JsonSerializer.Serialize(new { guid = m_AssetGuid }));
                using var doc = JsonDocument.Parse(json);
                m_SourceRaw = Convert.FromBase64String(doc.RootElement.GetProperty("raw").GetString());
            }
            return m_SourceRaw;
        }

        /// <summary>The blocks the texture holds now: its pixel data store while views of it exist
        /// (writes through them land there), else its own.</summary>
        byte[] CurrentBlocks() => SourceCrunch ? CrunchBlocks() : m_Store ?? SourceRawData();

        /// <summary>While views of a block-compressed texture's blocks exist: per level, the hash
        /// of the blocks its pixel cells show.</summary>
        ulong[] m_BlockCellHashes;
        bool m_BlockCellsDirty;

        /// <summary>The pixel cells of a block-compressed texture are what its blocks decode to,
        /// as the Editor's pixel reads are (tests/projects/texturecompression write.*). The
        /// import's cells already are; SetPixelData and LoadRawTextureData decode what they write;
        /// while views of the blocks exist, each read decodes the levels whose blocks changed.</summary>
        void RefreshBlockCells()
        {
            if (!SourceBlocks) return;
            if (m_BlockCellsDirty) { EnsureBlockCells(); DecodeAllBlocks(); return; }
            if (m_Store == null) return;
            if (SourceCrunch) { RefreshCrunchCells(); return; }
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                var level = new ReadOnlySpan<byte>(CurrentBlocks(), at, size);
                ulong hash = BlockFormats.Fnv1a(level);
                if (m_BlockCellHashes == null || m_BlockCellHashes[mip] != hash) DecodeBlockLevel(level, mip);
                (m_BlockCellHashes ??= new ulong[m_SourceMips])[mip] = hash;
                at += size;
            }
        }

        /// <summary>The level hashes of blocks whose cells are current (views are being made).</summary>
        void HashBlockCells(byte[] blocks)
        {
            if (SourceCrunch) return;
            m_BlockCellHashes = new ulong[m_SourceMips];
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                m_BlockCellHashes[mip] = BlockFormats.Fnv1a(new ReadOnlySpan<byte>(blocks, at, size));
                at += size;
            }
        }

        /// <summary>Texel `i` of level `mip` as GetPixel reads it, decoded from its block in the
        /// pixel data store.</summary>
        Color BlockTexel(int mip, int i)
        {
            var format = BlockFormats.Uncrunched(m_SourceFormat.Value);
            int w = MipWidth(mip);
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            var level = new ReadOnlySpan<byte>(CurrentBlocks(), at, SourceLevelSize(mip));
            if (BlockFormats.Texel(format, level, w, i % w, i / w, out var pixel, out var texel))
            {
                if (Texture2DTexels.FloatStored(m_Format) && !BlockFormats.IsAstcHdr(format)) pixel.a = 1f;
                return pixel;
            }
            if (Texture2DTexels.FloatStored(m_Format)) return new Color(texel.r / 255f, texel.g / 255f, texel.b / 255f, 1f);
            return Texture2DTexels.Pixel(m_Format, texel);
        }

        /// <summary>A `width` x `height` level of blocks decoded to RGBA8 as GetPixels32 reads it
        /// (BC6H clamped to 0..1).</summary>
        byte[] DecodeBlockRgba(ReadOnlySpan<byte> blocks, int width, int height, bool floats = false, bool eac16 = false) =>
            BlockFormats.DecodeRgba(BlockFormats.Uncrunched(m_SourceFormat.Value), blocks, width, height, floats, eac16);

        /// <summary>The top level's pixels decoded from the blocks, leaving the texture's cells
        /// alone (engine reads of an unreadable texture); null when there are no blocks.</summary>
        Color32[] DecodeTopBlocks()
        {
            if (!SourceBlocks || BlockFormats.BlockBytes(m_SourceFormat.Value) == 0) return null;
            var blocks = CurrentBlocks();
            if (blocks == null) return null;
            var format = BlockFormats.Uncrunched(m_SourceFormat.Value);
            var pixels = new Color32[m_Width * m_Height];
            if (BlockFormats.NativeDecoded(format))
            {
                var rgba = DecodeBlockRgba(new ReadOnlySpan<byte>(blocks, 0, SourceLevelSize(0)), m_Width, m_Height);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                return pixels;
            }
            int size = BlockFormats.BlockBytes(format), across = (m_Width + 3) / 4;
            for (int y = 0; y < m_Height; y++)
            for (int x = 0; x < m_Width; x++)
            {
                var block = new ReadOnlySpan<byte>(blocks, ((y / 4) * across + x / 4) * size, size);
                pixels[y * m_Width + x] = BlockFormats.DecodeTexel(format, block, (y % 4) * 4 + x % 4);
            }
            return pixels;
        }

        /// <summary>Decodes one level of blocks into the pixel cells.</summary>
        void DecodeBlockLevel(ReadOnlySpan<byte> blocks, int mip)
        {
            var format = BlockFormats.Uncrunched(m_SourceFormat.Value);
            bool floats = BlockFormats.IsAstcHdr(format) || format == TextureFormat.BC6H;
            var rgba = DecodeBlockRgba(blocks, MipWidth(mip), MipHeight(mip), floats);
            if (floats)
            {
                m_Hdr ??= new Color[m_Width * m_Height];
                PixelFormats.DecodeFloat(TextureFormat.RGBAFloat, rgba, FloatLevel(mip));
            }
            else if (Texture2DTexels.FloatStored(m_Format))
            {
                m_Hdr ??= new Color[m_Width * m_Height];
                var cells = FloatLevel(mip);
                for (int i = 0; i < cells.Length && i * 4 + 3 < rgba.Length; i++)
                    cells[i] = new Color(rgba[i * 4] / 255f, rgba[i * 4 + 1] / 255f, rgba[i * 4 + 2] / 255f, 1f);
            }
            else
            {
                m_Pixels ??= new Color32[m_Width * m_Height];
                PixelFormats.Decode(TextureFormat.RGBA32, rgba, ByteLevel(mip));
            }
        }

        /// <summary>SetPixelData of a block-compressed texture: the level's blocks.</summary>
        void SetBlockLevel(ReadOnlySpan<byte> data, int mip)
        {
            if (SourceCrunch) throw new UnityException("SetPixelData of Crunch archives is unsupported; use LoadRawTextureData with a complete archive.");
            int size = SourceLevelSize(mip);
            DecodeRaw();
            var blocks = CurrentBlocks();
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            CheckStoreWrite();
            data.Slice(0, size).CopyTo(new Span<byte>(blocks, at, size));
            EnsureBlockCells();
            // Cells not made yet (a texture made by code) are decoded when first read.
            if (m_Pixels == null && m_Hdr == null) return;
            DecodeBlockLevel(new ReadOnlySpan<byte>(blocks, at, size), mip);
            if (m_BlockCellHashes != null) m_BlockCellHashes[mip] = BlockFormats.Fnv1a(new ReadOnlySpan<byte>(blocks, at, size));
        }

        /// <summary>The pixel cells exist before a level is decoded into them (an import's are
        /// read from the import).</summary>
        void EnsureBlockCells()
        {
            if (m_Pixels == null && m_Hdr == null && m_AssetGuid != null) FetchImportedPixels();
        }

        /// <summary>LoadRawTextureData of a block-compressed texture: the whole chain's blocks.</summary>
        void LoadBlocks(ReadOnlySpan<byte> data)
        {
            if (SourceCrunch) { LoadCrunch(data); return; }
            int total = 0;
            for (int mip = 0; mip < m_SourceMips; mip++) total += SourceLevelSize(mip);
            if (data.Length < total) throw new UnityException("LoadRawTextureData: not enough data provided (will result in overread).");
            DecodeRaw();
            var blocks = CurrentBlocks();
            CheckStoreWrite();
            data.Slice(0, total).CopyTo(blocks);
            EnsureBlockCells();
            if (m_Pixels == null && m_Hdr == null) return;
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                DecodeBlockLevel(new ReadOnlySpan<byte>(blocks, at, size), mip);
                at += size;
            }
            if (m_BlockCellHashes != null) HashBlockCells(blocks);
            m_BlockCellsDirty = false;
        }

        /// <summary>SetPixels and SetPixels32 refuse a block-compressed texture (the Editor's
        /// error, tests/projects/texturecompression write.setpixels*).</summary>
        void ThrowIfBlocks(string api)
        {
            if (SourceBlocks) throw new ArgumentException($"{api}: texture uses an unsupported format. (Texture '{name}')");
        }

        /// <summary>Uploads a block-compressed texture's blocks; ETC blocks as the RGBA8 texels
        /// they decode to where unsupported; EAC retains normalized 16-bit channels. ETC and ASTC stay compressed where supported.</summary>
        unsafe void UploadBlocks()
        {
            m_GpuMipCount = m_SourceMips;
            var blocks = BlockFormats.GpuData(BlockFormats.Uncrunched(m_SourceFormat.Value), m_Linear, CurrentBlocks(), 1, m_SourceMips, m_Width, m_Height, out int format);
            fixed (byte* b = blocks)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = format,
                    MipCount = m_SourceMips,
                    Data = b,
                    DataLength = blocks.Length,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    WrapW = (int)wrapModeW,
                    AnisoLevel = anisoLevel,
                };
                UploadTexture(&packet);
            }
        }

        /// <summary>A whole level's pixels as GetPixels and GetPixels32 answer them: a side of a
        /// compressed texture's level shorter than a block reads the whole block, 4 texels (the
        /// Unity 6000.0.71f1 Editor, tests/projects/texturecompression: 2x2 and 1x1 levels read 4x4,
        /// a 5x3 level 5x4; ASTC keeps the actual dimensions. An import's BC padding is tiled; new.* bytes of 205
        /// decode to whatever they hold).</summary>
        Color32[] BlockPadded(Color32[] level, int mip) =>
            BlockPadded(level, mip, c => c);

        Color[] BlockPadded(Color[] level, int mip) =>
            BlockPadded(level, mip, c => Texture2DTexels.FloatStored(m_Format)
                ? new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f)
                : Texture2DTexels.Pixel(m_Format, c));

        T[] BlockPadded<T>(T[] level, int mip, Func<Color32, T> read)
        {
            int w = MipWidth(mip), h = MipHeight(mip);
            if (!SourceBlocks || BlockFormats.IsAstc(m_SourceFormat.Value) || (w >= 4 && h >= 4)) return level;
            var blocks = CurrentBlocks();
            if (blocks == null) return level;
            // The decoded blocks, then the read size: sides below 4 grow to 4.
            int bw = (w + 3) / 4 * 4, bh = (h + 3) / 4 * 4;
            int rw = Math.Max(w, 4), rh = Math.Max(h, 4);
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            var json = Native.Call("texture-decode-blocks", JsonSerializer.Serialize(new
            {
                format = (int)BlockFormats.Uncrunched(m_SourceFormat.Value),
                width = bw,
                height = bh,
                data = Convert.ToBase64String(blocks, at, SourceLevelSize(mip)),
            }));
            using var doc = JsonDocument.Parse(json);
            var rgba = Convert.FromBase64String(doc.RootElement.GetProperty("rgba").GetString());
            var padded = new T[rw * rh];
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    int i = (y * bw + x) * 4;
                    padded[y * rw + x] = read(new Color32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]));
                }
            return padded;
        }

        /// <summary>A block-compressed texture made by code: its sides must be multiples of the
        /// block (30x18 DXT1 is refused, 20x12 DXT5 made, tests/projects/texturecompression
        /// new.*), its blocks are bytes of 205 until written (what the Editor's new textures hold)
        /// and its pixels what they decode to.</summary>
        void InitBlocks(TextureFormat format)
        {
            m_SourceFormat = format;
            m_SourceMips = m_MipCount;
            m_Format = format == TextureFormat.BC6H || BlockFormats.IsAstcHdr(format) ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
            int total = 0;
            for (int mip = 0; mip < m_MipCount; mip++) total += SourceLevelSize(mip);
            m_SourceRaw = new byte[SourceCrunch ? 0 : total];
            Array.Fill(m_SourceRaw, (byte)205);
            m_Pixels = null;
            m_Hdr = null;
            m_PixelsDirty = true;
        }

        /// <summary>Decodes every level of a block-compressed texture made by code into its cells.</summary>
        void DecodeAllBlocks()
        {
            var blocks = CurrentBlocks();
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                DecodeBlockLevel(new ReadOnlySpan<byte>(blocks, at, size), mip);
                at += size;
            }
            if (m_BlockCellHashes != null) HashBlockCells(blocks);
            m_BlockCellsDirty = false;
        }

        /// <summary>Texture2D.Compress: RGB24 (and RGB565) to DXT1, formats with alpha to DXT5, R8
        /// to BC4, RG16 to BC5, every level from its pixels (tests/projects/texturecompression
        /// compress.*); with the Android target active, formats with alpha to ETC2_RGBA8 and the
        /// others to ETC2_RGB; direct R8/RG16 become EAC_R/EAC_RG in the measured profile
        /// (AndroidGamma compress.seed.*). A compressed, unreadable or not block-sized texture is left alone. The
        /// runtime encoder is selected by the compatibility profile; highQuality dithers the
        /// DXT colour fit in the measured profile. Remaining block differences are recorded by
        /// the probe.</summary>
        void CompressToBlocks(bool highQuality)
        {
            // Any compressed texture is left alone (an AssetBundle's ETC or ASTC texture too), as
            // the documentation says.
            if (m_SourceFormat != null || !m_IsReadable || m_Width % 4 != 0 || m_Height % 4 != 0) return;
            bool etc = UnityEditor.EditorUserBuildSettingsInfo.ActiveGroup == UnityEditor.BuildTargetGroup.Android;
            if (BlockFormats.CompressTarget(m_Format, etc) is not TextureFormat target)
            {
                Debug.LogWarning($"Unbound: Texture2D.Compress of a {m_Format} texture is not supported (unmeasured); the texture keeps its format.");
                return;
            }
            if (etc && Compat.RuntimeTextureCompressionMeasured && !BlockFormats.RuntimeMipChainFits(m_Width, m_Height, m_MipCount)) return;
            Stubs.Ignored("Texture2D.Compress blocks", "some runtime compression blocks may differ from the Editor's; see the texturecompression probe");
            RequireReadable();
            LeaveStore();
            int total = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
                total += BlockFormats.LevelSize(target, MipWidth(mip), MipHeight(mip));
            var blocks = new byte[total];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                int w = MipWidth(mip), h = MipHeight(mip);
                var rgba = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    var cell = Cell(mip, i);
                    rgba[i * 4] = cell.r;
                    rgba[i * 4 + 1] = cell.g;
                    rgba[i * 4 + 2] = cell.b;
                    rgba[i * 4 + 3] = cell.a;
                }
                var json = Native.Call("texture-compress-blocks", JsonSerializer.Serialize(new
                {
                    format = (int)target,
                    width = w,
                    height = h,
                    highQuality,
                    data = Convert.ToBase64String(rgba),
                }));
                using var doc = JsonDocument.Parse(json);
                var level = Convert.FromBase64String(doc.RootElement.GetProperty("blocks").GetString());
                level.CopyTo(blocks, at);
                at += level.Length;
            }
            m_SourceFormat = target;
            m_SourceMips = m_MipCount;
            m_SourceRaw = blocks;
            m_Format = TextureFormat.RGBA32;
            m_Raw = null;
            m_MipPixels = null;
            m_BlockCellHashes = null;
            DecodeAllBlocks();
            m_PixelsDirty = true;
            Upload();
            m_ResizeMemoryPendingApply = false;
        }

        /// <summary>Graphics.CopyTexture where either texture is block-compressed (tests/projects/
        /// texturecompression copy.*): the formats must have the same block footprint and byte size (DXT5 and BC7 do,
        /// DXT1 and DXT5 or a compressed and an uncompressed one do not: nothing is copied). A whole
        /// texture or level copies the blocks, also to the CPU when both keep blocks (from an
        /// unreadable import too); a region copies on the GPU alone and must lie on block
        /// boundaries (else nothing is copied).</summary>
        internal static void CopyBlocks(Texture2D src, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight,
            Texture2D dst, int dstMip, int dstX, int dstY, Action gpuCopy, bool allLevels = false)
        {
            if (!ValidateBlockCopy(src, srcMip, srcX, srcY, srcWidth, srcHeight, dst, dstMip, dstX, dstY)) return;
            if (srcWidth >= 0) { gpuCopy(); return; }
            if (!CopyBlockData(src, srcMip, dst, dstMip, allLevels)) return;
            // The renderer's copy follows the blocks.
            dst.m_PixelsDirty = true;
            dst.Upload();
        }

        /// <summary>Block geometry is checked before a device may decode its storage.</summary>
        internal static bool ValidateBlockCopy(Texture2D src, int srcMip, int x, int y, int width, int height,
            Texture2D dst, int dstMip, int dx, int dy)
        {
            if (!src.SourceBlocks && !dst.SourceBlocks) return true;
            if (src.SourceCrunch || dst.SourceCrunch) throw new UnityException("CopyTexture of Crunch archives is unsupported.");
            if (!src.SourceBlocks || !dst.SourceBlocks
                || BlockFormats.BlockBytes(src.m_SourceFormat.Value) != BlockFormats.BlockBytes(dst.m_SourceFormat.Value)
                || BlockFormats.Footprint(src.m_SourceFormat.Value) != BlockFormats.Footprint(dst.m_SourceFormat.Value))
            {
                Stubs.Ignored("CopyTexture between block sizes", "copies nothing between formats whose blocks differ");
                return false;
            }
            if (!src.ValidMip(srcMip) || !dst.ValidMip(dstMip)) return false;
            if (width < 0) return true;
            int side = BlockFormats.Footprint(src.m_SourceFormat.Value);
            return src.m_SourceFormat == dst.m_SourceFormat && x % side == 0 && y % side == 0
                && dx % side == 0 && dy % side == 0 && width % side == 0 && height % side == 0;
        }

        /// <summary>Copies retained CPU blocks without uploading or allocating per execution.
        /// Whole-mip command-buffer copies use this at execution time; pixel cells decode lazily.</summary>
        internal static bool CopyBlockData(Texture2D src, int srcMip, Texture2D dst, int dstMip, bool allLevels = false)
        {
            if (!src.SourceBlocks || !dst.SourceBlocks) return false;
            if (!ValidateBlockCopy(src, srcMip, 0, 0, -1, -1, dst, dstMip, 0, 0)) return false;
            src.CheckStoreRead();
            var from = src.CurrentBlocks();
            var to = dst.CurrentBlocks();
            // Compressed textures keep their blocks on the CPU, readable or not: the copy reaches
            // them from an unreadable source and into an unreadable destination too (the Unity
            // 6000.0.71f1 Editor, tests/projects/texturecompression copy.unreadable,
            // copy.unreadabledst; the documentation's rule is about readable pixel data).
            if (from == null || to == null)
            {
                return false;
            }
            // A whole texture: every level both have.
            int levels = allLevels ? Math.Min(src.m_SourceMips - srcMip, dst.m_SourceMips - dstMip) : 1;
            bool copied = false;
            for (int i = 0; i < levels; i++)
            {
                int sm = srcMip + i, dm = dstMip + i;
                if (src.MipWidth(sm) != dst.MipWidth(dm) || src.MipHeight(sm) != dst.MipHeight(dm)) break;
                int fromAt = 0, toAt = 0;
                for (int m = 0; m < sm; m++) fromAt += src.SourceLevelSize(m);
                for (int m = 0; m < dm; m++) toAt += dst.SourceLevelSize(m);
                int bytes = src.SourceLevelSize(sm);
                dst.CheckStoreWrite();
                Buffer.BlockCopy(from, fromAt, to, toAt, bytes);
                copied = true;
            }
            if (copied) dst.m_BlockCellsDirty = true;
            return copied;
        }

        const int BlockSideAlignment = 4;

        static bool BlockSidesAligned(int width, int height) =>
            width % BlockSideAlignment == 0 && height % BlockSideAlignment == 0;

        /// <summary>BC and ETC require block-aligned sides; ASTC accepts odd sides.</summary>
        static bool BlockSized(TextureFormat format, int width, int height) =>
            BlockSidesAligned(width, height) || BlockFormats.BlockBytes(format) == 0 || BlockFormats.IsAstc(format);

        /// <summary>The constructor checks the requested compression and effective color space
        /// (Unity 6000.0.71f1, tests/projects/texturecreationerrors).</summary>
        static bool ConstructorBlockSized(int width, int height, TextureFormat format, bool linear,
            GraphicsFormat? requestedFormat)
        {
            if (BlockSidesAligned(width, height)) return true;
            if (requestedFormat == null && !GraphicsFormatUtility.IsCompressedFormat(format)) return true;

            bool srgb = !linear && QualitySettings.activeColorSpace == ColorSpace.Linear;
            GraphicsFormat graphics = requestedFormat ?? GraphicsFormatUtility.Texture2DGraphicsFormat(format, srgb);
            if (!GraphicsFormatUtility.IsCompressedFormat(graphics)) return BlockSized(format, width, height);
            if (!srgb)
                graphics = GraphicsFormatUtility.GetLinearFormat(graphics);
            // The sRGB TextureFormat request for ETC1 selects the ETC2 counterpart.
            if (requestedFormat == null && graphics == GraphicsFormat.RGB_ETC_UNorm && srgb)
                graphics = GraphicsFormat.RGB_ETC2_SRGB;
            string name = GraphicsFormatUtility.CompressedDisplayName(graphics);
            if (name == null) return BlockSized(format, width, height);

            Log.Engine(LogType.Error,
                $"Compressed GraphicsFormat {name} requires a texture size that is a multiple of {BlockSideAlignment}", null);
            return false;
        }

        /// <summary>Level <paramref name="mip"/>'s blocks of a block-compressed texture, readable or
        /// not (Texture2D.PackTextures copies them, as Graphics.CopyTexture does); empty when the
        /// texture keeps none.</summary>
        internal ReadOnlySpan<byte> BlockLevel(int mip)
        {
            if (!SourceBlocks || SourceCrunch || !ValidMip(mip)) return default;
            CheckStoreRead();
            var blocks = CurrentBlocks();
            if (blocks == null) return default;
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            return new ReadOnlySpan<byte>(blocks, at, SourceLevelSize(mip));
        }

        /// <summary>Makes the texture one of these sides, format and number of levels, keeping its
        /// colour space (Texture2D.PackTextures' atlas).</summary>
        internal void InitLevels(int width, int height, TextureFormat format, int mipCount) =>
            Init(width, height, format, mipCount, m_Linear, constructor: false);

        /// <summary>Bytes of a source level of a block-compressed texture.</summary>
        int SourceLevelSize(int mip) =>
            BlockFormats.LevelSize(m_SourceFormat.Value, Math.Max(1, m_Width >> mip), Math.Max(1, m_Height >> mip));
    }
}

namespace Unbound.Runtime
{
    /// <summary>Block-compressed texture formats, a level at least one block.</summary>
    internal static class BlockFormats
    {
        // Android compression checks every level while both dimensions still span a block.
        internal static bool RuntimeMipChainFits(int width, int height, int levels)
        {
            for (int mip = 0; mip < levels; mip++)
            {
                int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
                if (w < 3 || h < 3) break;
                if ((w | h) % 4 != 0) return false;
            }
            return true;
        }

        internal static bool IsCrunch(UnityEngine.TextureFormat format) => (int)format is 28 or 29 or 64 or 65;
        internal static UnityEngine.TextureFormat Uncrunched(UnityEngine.TextureFormat format) => (int)format switch
        {
            28 => UnityEngine.TextureFormat.DXT1,
            29 => UnityEngine.TextureFormat.DXT5,
            64 => UnityEngine.TextureFormat.ETC_RGB4,
            65 => UnityEngine.TextureFormat.ETC2_RGBA8,
            _ => format,
        };
        internal static bool IsEac(UnityEngine.TextureFormat format) =>
            format is UnityEngine.TextureFormat.EAC_R or UnityEngine.TextureFormat.EAC_RG;

        internal static bool IsSignedEac(UnityEngine.TextureFormat format) =>
            format is UnityEngine.TextureFormat.EAC_R_SIGNED or UnityEngine.TextureFormat.EAC_RG_SIGNED;

        internal static bool IsPvrtc(UnityEngine.TextureFormat format) =>
            format is UnityEngine.TextureFormat.PVRTC_RGB2 or UnityEngine.TextureFormat.PVRTC_RGBA2
                or UnityEngine.TextureFormat.PVRTC_RGB4 or UnityEngine.TextureFormat.PVRTC_RGBA4;

        /// <summary>Bytes of one block of a format a layered texture keeps as blocks: those of
        /// <see cref="BlockBytes"/>, and ETC2_RGBA1, signed EAC and PVRTC, which only layered
        /// textures keep (as the macOS Editor's do: tests/projects/nativegfx array.make.*); 0 for
        /// the others.</summary>
        internal static int LayeredBlockBytes(UnityEngine.TextureFormat format) => BlockBytes(format) switch
        {
            > 0 and var bytes => bytes,
            _ => format switch
            {
                UnityEngine.TextureFormat.ETC2_RGBA1 or UnityEngine.TextureFormat.EAC_R_SIGNED => 8,
                UnityEngine.TextureFormat.EAC_RG_SIGNED => 16,
                _ when IsPvrtc(format) => 8,
                _ => 0,
            },
        };

        /// <summary>Bytes a texel of a block format takes on its way to the renderer when its
        /// blocks are decoded first: EAC's 16-bit channels (one or two, signed for signed EAC),
        /// RGBA8 for ETC2_RGBA1 and PVRTC; 0 where the blocks themselves go up.</summary>
        internal static int DecodedUploadBytes(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.EAC_R or UnityEngine.TextureFormat.EAC_R_SIGNED => 2,
            UnityEngine.TextureFormat.EAC_RG or UnityEngine.TextureFormat.EAC_RG_SIGNED => 4,
            UnityEngine.TextureFormat.ETC2_RGBA1 => 4,
            _ when IsPvrtc(format) => 4,
            _ => 0,
        };
        internal static bool IsAstc(UnityEngine.TextureFormat format) =>
            (int)format >= (int)UnityEngine.TextureFormat.ASTC_4x4 && (int)format <= (int)UnityEngine.TextureFormat.ASTC_12x12
            || IsAstcHdr(format);

        internal static bool IsAstcHdr(UnityEngine.TextureFormat format) =>
            (int)format >= (int)UnityEngine.TextureFormat.ASTC_HDR_4x4 && (int)format <= (int)UnityEngine.TextureFormat.ASTC_HDR_12x12;

        internal static int Footprint(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.ASTC_5x5 or UnityEngine.TextureFormat.ASTC_HDR_5x5 => 5,
            UnityEngine.TextureFormat.ASTC_6x6 or UnityEngine.TextureFormat.ASTC_HDR_6x6 => 6,
            UnityEngine.TextureFormat.ASTC_8x8 or UnityEngine.TextureFormat.ASTC_HDR_8x8 => 8,
            UnityEngine.TextureFormat.ASTC_10x10 or UnityEngine.TextureFormat.ASTC_HDR_10x10 => 10,
            UnityEngine.TextureFormat.ASTC_12x12 or UnityEngine.TextureFormat.ASTC_HDR_12x12 => 12,
            _ => 4,
        };

        /// <summary>Bytes of one block, or 0 for formats that are not BC1-BC7, ETC_RGB4, ETC2_RGB
        /// or ETC2_RGBA8, and supported LDR ASTC footprints.</summary>
        internal static int BlockBytes(UnityEngine.TextureFormat format) => Uncrunched(format) switch
        {
            UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.BC4 or UnityEngine.TextureFormat.ETC_RGB4
                or UnityEngine.TextureFormat.ETC2_RGB or UnityEngine.TextureFormat.EAC_R => 8,
            UnityEngine.TextureFormat.DXT5 or UnityEngine.TextureFormat.BC5 or UnityEngine.TextureFormat.BC6H
                or UnityEngine.TextureFormat.BC7 or UnityEngine.TextureFormat.ETC2_RGBA8 or UnityEngine.TextureFormat.EAC_RG
                => 16,
            _ when IsAstc(format) => 16,
            _ => 0,
        };

        /// <summary>The format Texture2D.Compress gives a texture of `format`: RGB24 and RGB565
        /// DXT1, formats with alpha DXT5, R8 BC4, RG16 BC5; with `etc` (the Android target active)
        /// ETC2_RGBA8 for formats with alpha and ETC2_RGB for the others. Null for formats it
        /// does not take (unmeasured).</summary>
        internal static UnityEngine.TextureFormat? CompressTarget(UnityEngine.TextureFormat format, bool etc) => format switch
        {
            UnityEngine.TextureFormat.RGB24 or UnityEngine.TextureFormat.RGB565 =>
                etc ? UnityEngine.TextureFormat.ETC2_RGB : UnityEngine.TextureFormat.DXT1,
            UnityEngine.TextureFormat.RGBA32 or UnityEngine.TextureFormat.ARGB32 or UnityEngine.TextureFormat.BGRA32
                or UnityEngine.TextureFormat.RGBA4444 or UnityEngine.TextureFormat.ARGB4444 =>
                etc ? UnityEngine.TextureFormat.ETC2_RGBA8 : UnityEngine.TextureFormat.DXT5,
            UnityEngine.TextureFormat.R8 => etc ? (Compat.RuntimeTextureCompressionMeasured ? UnityEngine.TextureFormat.EAC_R : UnityEngine.TextureFormat.ETC2_RGB) : UnityEngine.TextureFormat.BC4,
            UnityEngine.TextureFormat.RG16 => etc ? (Compat.RuntimeTextureCompressionMeasured ? UnityEngine.TextureFormat.EAC_RG : UnityEngine.TextureFormat.ETC2_RGB) : UnityEngine.TextureFormat.BC5,
            _ => null,
        };

        /// <summary>Formats whose blocks the native side decodes (the others decode texel by texel
        /// here).</summary>
        internal static bool NativeDecoded(UnityEngine.TextureFormat format) => format is UnityEngine.TextureFormat.BC6H
            or UnityEngine.TextureFormat.BC7 or UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB
            or UnityEngine.TextureFormat.ETC2_RGBA8 or UnityEngine.TextureFormat.ETC2_RGBA1 || IsAstc(format) || IsEac(format)
            || IsPvrtc(format);

        /// <summary>The renderer's packet code of a block format: BC1, BC3 and BC7 colour (sampled
        /// through an sRGB view in linear projects) or linear data; BC4, BC5 and BC6H, ETC and ASTC.
        /// EAC uses normalized channel pixels.</summary>
        internal static int GpuCode(UnityEngine.TextureFormat format, bool linear) => format switch
        {
            UnityEngine.TextureFormat.DXT1 => linear ? 39 : 38,
            UnityEngine.TextureFormat.DXT5 => linear ? 41 : 40,
            UnityEngine.TextureFormat.BC7 => linear ? 8 : 7,
            UnityEngine.TextureFormat.BC4 => 42,
            UnityEngine.TextureFormat.BC5 => 43,
            UnityEngine.TextureFormat.BC6H => 44,
            UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB => linear ? 66 : 65,
            UnityEngine.TextureFormat.ETC2_RGBA8 => linear ? 68 : 67,
            _ when IsAstcHdr(format) => 59 + (int)format - 66,
            _ when IsAstc(format) => (linear ? 53 : 47) + (int)format - 48,
            _ when IsEac(format) || IsSignedEac(format) || IsPvrtc(format) || format == UnityEngine.TextureFormat.ETC2_RGBA1 => -1,
            _ => throw new ArgumentException($"{format} is not a block format"),
        };

        /// <summary>Texel `k` (row-major in the 4x4 block) of a DXT1, DXT5, BC4 or BC5 block, as
        /// the importer's decoding (and the Editor's GetPixels32) reads it: colours interpolate with
        /// truncating thirds (a half in three-colour mode, c0 <= c1, whose fourth entry is
        /// transparent black; DXT5 too, its alpha from the alpha half), alpha, red and green steps
        /// round to nearest.</summary>
        internal static UnityEngine.Color32 DecodeTexel(UnityEngine.TextureFormat format, ReadOnlySpan<byte> block, int k)
        {
            switch (format)
            {
                case UnityEngine.TextureFormat.DXT1:
                    return Colour(block, k);
                case UnityEngine.TextureFormat.DXT5:
                {
                    var c = Colour(block.Slice(8), k);
                    c.a = Step(block, k);
                    return c;
                }
                case UnityEngine.TextureFormat.BC4:
                    return new UnityEngine.Color32(Step(block, k), 0, 0, 255);
                case UnityEngine.TextureFormat.BC5:
                    return new UnityEngine.Color32(Step(block, k), Step(block.Slice(8), k), 0, 255);
                default:
                    throw new ArgumentException($"{format} texels are not decoded here");
            }
        }

        static UnityEngine.Color32 Colour(ReadOnlySpan<byte> block, int k)
        {
            int c0 = block[0] | block[1] << 8, c1 = block[2] | block[3] << 8;
            int index = (int)((uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24) >> (2 * k)) & 3;
            if (index == 3 && c0 <= c1) return new UnityEngine.Color32(0, 0, 0, 0);
            (int wa, int wb, int d) = index switch
            {
                0 => (1, 0, 1),
                1 => (0, 1, 1),
                2 when c0 > c1 => (2, 1, 3),
                2 => (1, 1, 2),
                _ => (1, 2, 3),
            };
            return new UnityEngine.Color32(
                (byte)((Widen(c0, 0) * wa + Widen(c1, 0) * wb) / d),
                (byte)((Widen(c0, 1) * wa + Widen(c1, 1) * wb) / d),
                (byte)((Widen(c0, 2) * wa + Widen(c1, 2) * wb) / d),
                255);
        }

        /// <summary>Channel `p` (red, green, blue) of an RGB565 colour widened to 8 bits.</summary>
        static int Widen(int c, int p) => p switch
        {
            0 => (c >> 11 << 3) | (c >> 13),
            1 => (((c >> 5) & 63) << 2) | (((c >> 5) & 63) >> 4),
            _ => ((c & 31) << 3) | ((c & 31) >> 2),
        };

        /// <summary>An eight-step value (DXT5 alpha, BC4, a BC5 half).</summary>
        static byte Step(ReadOnlySpan<byte> block, int k)
        {
            int a0 = block[0], a1 = block[1];
            ulong bits = 0;
            for (int i = 7; i >= 2; i--) bits = bits << 8 | block[i];
            int index = (int)(bits >> (3 * k)) & 7;
            if (index == 0) return (byte)a0;
            if (index == 1) return (byte)a1;
            if (a0 > a1) { int i = index - 1; return (byte)((a0 * (7 - i) + a1 * i + 3) / 7); }
            if (index == 6) return 0;
            if (index == 7) return 255;
            { int i = index - 1; return (byte)((a0 * (5 - i) + a1 * i + 2) / 5); }
        }

        /// <summary>Texel (x, y) of a `width`-texel-wide level of blocks, rows in the blocks'
        /// order: natively decoded formats give floats (<paramref name="pixel"/>, true), the
        /// others bytes (<paramref name="texel"/>, false).</summary>
        internal static unsafe bool Texel(UnityEngine.TextureFormat format, ReadOnlySpan<byte> level, int width, int x, int y,
            out UnityEngine.Color pixel, out UnityEngine.Color32 texel)
        {
            int size = BlockBytes(format), side = Footprint(format);
            var block = level.Slice(((y / side) * ((width + side - 1) / side) + x / side) * size, size);
            int k = (y % side) * side + x % side;
            pixel = default;
            texel = default;
            if (!NativeDecoded(format))
            {
                texel = DecodeTexel(format, block, k);
                return false;
            }
            UnityEngine.Color decoded = default;
            fixed (byte* input = block)
                if (!Native.DecodeBlockTexel((int)format, input, (uint)k, &decoded))
                    throw new UnityEngine.UnityException("Compressed texel decoding failed.");
            pixel = decoded;
            return true;
        }

        /// <summary>A `width` x `height` level of blocks decoded to RGBA8 as GetPixels32 reads it
        /// (BC6H clamped to 0..1), float RGBA for ASTC HDR with `floats`, normalized 16-bit
        /// channels for EAC with `eac16`; rows in the blocks' order.</summary>
        internal static byte[] DecodeRgba(UnityEngine.TextureFormat format, ReadOnlySpan<byte> blocks, int width, int height, bool floats = false, bool eac16 = false)
        {
            if (IsPvrtc(format))
                Unbound.Runtime.Stubs.Ignored("PVRTC decoding", "approximates the Editor's: at 4 bits a texel blue may differ by a few steps, at 2 bits most texels differ (tests/projects/nativegfx array.decode.PVRTC_*)");
            var json = Native.Call("texture-decode-blocks", JsonSerializer.Serialize(new
            {
                format = (int)format,
                width,
                height,
                data = Convert.ToBase64String(blocks),
                floats,
                eac16,
            }));
            using var doc = JsonDocument.Parse(json);
            return Convert.FromBase64String(doc.RootElement.GetProperty("rgba").GetString());
        }

        /// <summary>What the renderer is given for `images` mip chains of blocks one after the
        /// other (a cube's six faces), and its packet code: the blocks themselves, or for a format
        /// it takes no blocks of the texels they decode to (RGBA8; float RGBA for ASTC HDR;
        /// normalized 16-bit channels for EAC).</summary>
        internal static byte[] GpuData(UnityEngine.TextureFormat format, bool linear, byte[] blocks, int images, int mips, int width, int height, out int code)
        {
            code = GpuCode(format, linear);
            if (code >= 0) return blocks;
            var texels = new System.IO.MemoryStream();
            bool floats = IsAstcHdr(format), eac = IsEac(format) || IsSignedEac(format);
            int at = 0;
            for (int image = 0; image < images; image++)
                for (int mip = 0; mip < mips; mip++)
                {
                    int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip), size = LevelSize(format, w, h);
                    texels.Write(DecodeRgba(format, new ReadOnlySpan<byte>(blocks, at, size), w, h, floats, eac));
                    at += size;
                }
            code = eac ? PixelFormats.GpuNormFormat(format switch
                {
                    UnityEngine.TextureFormat.EAC_R => UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm,
                    UnityEngine.TextureFormat.EAC_RG => UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_UNorm,
                    UnityEngine.TextureFormat.EAC_R_SIGNED => UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SNorm,
                    _ => UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SNorm,
                })
                : floats ? PixelFormats.GpuFloatFormat(UnityEngine.TextureFormat.RGBAFloat) : linear ? 1 : 0;
            return texels.ToArray();
        }

        /// <summary>FNV-1a of some bytes (whether a level of blocks changed).</summary>
        internal static ulong Fnv1a(ReadOnlySpan<byte> data)
        {
            ulong hash = 14695981039346656037UL;
            foreach (var b in data) hash = (hash ^ b) * 1099511628211UL;
            return hash;
        }

        /// <summary>Bytes of a `width` x `height` level: whole blocks; PVRTC's of at least 8x8
        /// texels at 4 bits, 16x8 at 2 (a 4x4 level is 32 bytes either way:
        /// tests/projects/nativegfx array.make.PVRTC_*).</summary>
        internal static int LevelSize(UnityEngine.TextureFormat format, int width, int height)
        {
            if (IsPvrtc(format))
            {
                bool two = format is UnityEngine.TextureFormat.PVRTC_RGB2 or UnityEngine.TextureFormat.PVRTC_RGBA2;
                return Math.Max(width, two ? 16 : 8) * Math.Max(height, 8) * (two ? 2 : 4) / 8;
            }
            return ((width + Footprint(format) - 1) / Footprint(format))
                * ((height + Footprint(format) - 1) / Footprint(format)) * LayeredBlockBytes(format);
        }
    }
}
