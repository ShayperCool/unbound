using System;
using System.Text.Json;
using Unbound.Runtime;

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
        byte[] CurrentBlocks() => m_Store ?? SourceRawData();

        /// <summary>While views of a block-compressed texture's blocks exist: per level, the hash
        /// of the blocks its pixel cells show.</summary>
        ulong[] m_BlockCellHashes;

        /// <summary>The pixel cells of a block-compressed texture are what its blocks decode to,
        /// as the Editor's pixel reads are (tests/projects/texturecompression write.*). The
        /// import's cells already are; SetPixelData and LoadRawTextureData decode what they write;
        /// while views of the blocks exist, each read decodes the levels whose blocks changed.</summary>
        void RefreshBlockCells()
        {
            if (!SourceBlocks || m_Store == null) return;
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                var level = new ReadOnlySpan<byte>(m_Store, at, size);
                ulong hash = Fnv1a(level);
                if (m_BlockCellHashes == null || m_BlockCellHashes[mip] != hash) DecodeBlockLevel(level, mip);
                (m_BlockCellHashes ??= new ulong[m_SourceMips])[mip] = hash;
                at += size;
            }
        }

        /// <summary>The level hashes of blocks whose cells are current (views are being made).</summary>
        void HashBlockCells(byte[] blocks)
        {
            m_BlockCellHashes = new ulong[m_SourceMips];
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                m_BlockCellHashes[mip] = Fnv1a(new ReadOnlySpan<byte>(blocks, at, size));
                at += size;
            }
        }

        /// <summary>Texel `i` of level `mip` as GetPixel reads it, decoded from its block in the
        /// pixel data store.</summary>
        unsafe Color BlockTexel(int mip, int i)
        {
            var format = m_SourceFormat.Value;
            int size = BlockFormats.BlockBytes(format), side = BlockFormats.Footprint(format);
            int w = MipWidth(mip), x = i % w, y = i / w;
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            at += ((y / side) * ((w + side - 1) / side) + x / side) * size;
            var block = new ReadOnlySpan<byte>(m_Store, at, size);
            int k = (y % side) * side + x % side;
            if (BlockFormats.IsAstcHdr(format))
            {
                Span<Color> pixels = stackalloc Color[side * side];
                fixed (byte* input = block)
                fixed (Color* output = pixels)
                    if (!Native.DecodeHdrBlock((int)format, input, output))
                        throw new UnityException("ASTC HDR block decoding failed.");
                return pixels[k];
            }
            Color32 texel;
            if (BlockFormats.NativeDecoded(format))
            {
                // One block decoded at its footprint.
                var rgba = DecodeBlockRgba(block, side, side);
                texel = new Color32(rgba[k * 4], rgba[k * 4 + 1], rgba[k * 4 + 2], rgba[k * 4 + 3]);
            }
            else texel = BlockFormats.DecodeTexel(format, block, k);
            if (Texture2DTexels.FloatStored(m_Format)) return new Color(texel.r / 255f, texel.g / 255f, texel.b / 255f, 1f);
            return Texture2DTexels.Pixel(m_Format, texel);
        }

        /// <summary>A `width` x `height` level of blocks decoded to RGBA8 as GetPixels32 reads it
        /// (BC6H clamped to 0..1).</summary>
        byte[] DecodeBlockRgba(ReadOnlySpan<byte> blocks, int width, int height, bool floats = false)
        {
            var json = Native.Call("texture-decode-blocks", JsonSerializer.Serialize(new
            {
                format = (int)m_SourceFormat.Value,
                width,
                height,
                data = Convert.ToBase64String(blocks),
                floats,
            }));
            using var doc = JsonDocument.Parse(json);
            return Convert.FromBase64String(doc.RootElement.GetProperty("rgba").GetString());
        }

        /// <summary>The top level's pixels decoded from the blocks, leaving the texture's cells
        /// alone (engine reads of an unreadable texture); null when there are no blocks.</summary>
        Color32[] DecodeTopBlocks()
        {
            if (!SourceBlocks || BlockFormats.BlockBytes(m_SourceFormat.Value) == 0) return null;
            var blocks = CurrentBlocks();
            if (blocks == null) return null;
            var format = m_SourceFormat.Value;
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
            bool hdr = BlockFormats.IsAstcHdr(m_SourceFormat.Value);
            var rgba = DecodeBlockRgba(blocks, MipWidth(mip), MipHeight(mip), hdr);
            if (hdr)
            {
                m_Hdr ??= new Color[m_Width * m_Height];
                PixelFormats.DecodeFloat(TextureFormat.RGBAFloat, rgba, FloatLevel(mip));
            }
            else if (Texture2DTexels.FloatStored(m_Format))
            {
                Stubs.Ignored("Texture2D BC6H blocks", "written BC6H blocks read back clamped to 0..1 in 8-bit steps: Unbound has no float decoding of them");
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

        static ulong Fnv1a(ReadOnlySpan<byte> data)
        {
            ulong hash = 14695981039346656037UL;
            foreach (var b in data) hash = (hash ^ b) * 1099511628211UL;
            return hash;
        }

        /// <summary>SetPixelData of a block-compressed texture: the level's blocks.</summary>
        void SetBlockLevel(ReadOnlySpan<byte> data, int mip)
        {
            int size = SourceLevelSize(mip);
            if (data.Length < size) throw new ArgumentException("SetPixelData: not enough data provided.");
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
            if (m_BlockCellHashes != null) m_BlockCellHashes[mip] = Fnv1a(new ReadOnlySpan<byte>(blocks, at, size));
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
        }

        /// <summary>SetPixels and SetPixels32 refuse a block-compressed texture (the Editor's
        /// error, tests/projects/texturecompression write.setpixels*).</summary>
        void ThrowIfBlocks(string api)
        {
            if (SourceBlocks) throw new ArgumentException($"{api}: texture uses an unsupported format. (Texture '{name}')");
        }

        /// <summary>Uploads a block-compressed texture's blocks; ETC blocks as the RGBA8 texels
        /// they decode to (Unbound's renderer samples no ETC formats).</summary>
        unsafe void UploadBlocks()
        {
            var blocks = CurrentBlocks();
            m_GpuMipCount = m_SourceMips;
            int format = BlockFormats.GpuCode(m_SourceFormat.Value, m_Linear);
            if (format < 0)
            {
                var texels = new System.IO.MemoryStream();
                bool hdr = BlockFormats.IsAstcHdr(m_SourceFormat.Value);
                int at = 0;
                for (int mip = 0; mip < m_SourceMips; mip++)
                {
                    int size = SourceLevelSize(mip);
                    texels.Write(DecodeBlockRgba(new ReadOnlySpan<byte>(blocks, at, size), MipWidth(mip), MipHeight(mip), hdr));
                    at += size;
                }
                blocks = texels.ToArray();
                format = hdr ? PixelFormats.GpuFloatFormat(TextureFormat.RGBAFloat) : m_Linear ? 1 : 0;
            }
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
                format = (int)m_SourceFormat.Value,
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
            m_SourceRaw = new byte[total];
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
        }

        /// <summary>Texture2D.Compress: RGB24 (and RGB565) to DXT1, formats with alpha to DXT5, R8
        /// to BC4, RG16 to BC5, every level from its pixels (tests/projects/texturecompression
        /// compress.*); with the Android target active, formats with alpha to ETC2_RGBA8 and the
        /// others to ETC2_RGB (the Editor's OpenGL device takes no EAC there: AndroidGamma
        /// compress.*). A compressed, unreadable or not block-sized texture is left alone. The
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
            int size = src.SourceBlocks ? BlockFormats.BlockBytes(src.m_SourceFormat.Value) : 0;
            if (size == 0 || !dst.SourceBlocks || BlockFormats.BlockBytes(dst.m_SourceFormat.Value) != size
                || BlockFormats.Footprint(src.m_SourceFormat.Value) != BlockFormats.Footprint(dst.m_SourceFormat.Value))
            {
                Stubs.Ignored("Graphics.CopyTexture between block sizes", "copies nothing between formats whose blocks differ (as the Editor)");
                return;
            }
            if (!src.ValidMip(srcMip) || !dst.ValidMip(dstMip)) return;
            if (srcWidth >= 0)
            {
                // A region: block-aligned, GPU only (same formats only on Unbound's renderer).
                int side = BlockFormats.Footprint(src.m_SourceFormat.Value);
                bool aligned = srcX % side == 0 && srcY % side == 0 && srcWidth % side == 0 && srcHeight % side == 0 && dstX % side == 0 && dstY % side == 0;
                if (!aligned) return;
                if (src.m_SourceFormat == dst.m_SourceFormat) gpuCopy();
                else Stubs.Ignored("Graphics.CopyTexture region between block formats", "does not copy a region between different block formats on Unbound's renderer (unmeasured)");
                return;
            }
            var from = src.CurrentBlocks();
            var to = dst.CurrentBlocks();
            // Compressed textures keep their blocks on the CPU, readable or not: the copy reaches
            // them from an unreadable source and into an unreadable destination too (the Unity
            // 6000.0.71f1 Editor, tests/projects/texturecompression copy.unreadable,
            // copy.unreadabledst; the documentation's rule is about readable pixel data).
            if (from == null || to == null)
            {
                if (src.m_SourceFormat == dst.m_SourceFormat) gpuCopy();
                return;
            }
            // A whole texture: every level both have.
            int levels = allLevels ? Math.Min(src.m_SourceMips, dst.m_SourceMips) : 1;
            // Pixel cells the destination already has (an import's, a bundle texture's decoded
            // ones) follow the blocks; cells not made yet are decoded when first read.
            // An unreadable destination keeps no cells (engine reads decode its blocks).
            dst.DecodeRaw();
            bool cells = dst.m_Pixels != null || dst.m_Hdr != null || (dst.m_IsReadable && dst.m_AssetGuid != null);
            if (cells) dst.EnsureBlockCells();
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
                if (cells)
                {
                    dst.DecodeBlockLevel(new ReadOnlySpan<byte>(to, toAt, bytes), dm);
                    if (dst.m_BlockCellHashes != null) dst.m_BlockCellHashes[dm] = Fnv1a(new ReadOnlySpan<byte>(to, toAt, bytes));
                }
                copied = true;
            }
            if (!copied) return;
            // The renderer's copy follows the blocks.
            dst.m_PixelsDirty = true;
            dst.Upload();
        }

        /// <summary>Whether a texture of `format` may have these sides: a block format's must be
        /// multiples of 4 for BC/ETC; ASTC accepts odd sides (texturecompression new.ASTC*Odd).</summary>
        static bool BlockSized(TextureFormat format, int width, int height) =>
            BlockFormats.BlockBytes(format) == 0 || BlockFormats.IsAstc(format) || (width % 4 == 0 && height % 4 == 0);

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
        internal static int BlockBytes(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.BC4 or UnityEngine.TextureFormat.ETC_RGB4
                or UnityEngine.TextureFormat.ETC2_RGB => 8,
            UnityEngine.TextureFormat.DXT5 or UnityEngine.TextureFormat.BC5 or UnityEngine.TextureFormat.BC6H
                or UnityEngine.TextureFormat.BC7 or UnityEngine.TextureFormat.ETC2_RGBA8
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
            UnityEngine.TextureFormat.R8 => etc ? UnityEngine.TextureFormat.ETC2_RGB : UnityEngine.TextureFormat.BC4,
            UnityEngine.TextureFormat.RG16 => etc ? UnityEngine.TextureFormat.ETC2_RGB : UnityEngine.TextureFormat.BC5,
            _ => null,
        };

        /// <summary>Formats whose blocks the native side decodes (the others decode texel by texel
        /// here).</summary>
        internal static bool NativeDecoded(UnityEngine.TextureFormat format) => format is UnityEngine.TextureFormat.BC6H
            or UnityEngine.TextureFormat.BC7 or UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB
            or UnityEngine.TextureFormat.ETC2_RGBA8 || IsAstc(format);

        /// <summary>The renderer's packet code of a block format: BC1, BC3 and BC7 colour (sampled
        /// through an sRGB view in linear projects) or linear data; BC4, BC5 and BC6H; -1 for ETC
        /// and ASTC (uploaded decoded).</summary>
        internal static int GpuCode(UnityEngine.TextureFormat format, bool linear) => format switch
        {
            UnityEngine.TextureFormat.DXT1 => linear ? 39 : 38,
            UnityEngine.TextureFormat.DXT5 => linear ? 41 : 40,
            UnityEngine.TextureFormat.BC7 => linear ? 8 : 7,
            UnityEngine.TextureFormat.BC4 => 42,
            UnityEngine.TextureFormat.BC5 => 43,
            UnityEngine.TextureFormat.BC6H => 44,
            UnityEngine.TextureFormat.ETC_RGB4 or UnityEngine.TextureFormat.ETC2_RGB
                or UnityEngine.TextureFormat.ETC2_RGBA8
                => -1,
            _ when IsAstc(format) => -1,
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

        /// <summary>Bytes of a `width` x `height` level.</summary>
        internal static int LevelSize(UnityEngine.TextureFormat format, int width, int height) =>
            ((width + Footprint(format) - 1) / Footprint(format))
                * ((height + Footprint(format) - 1) / Footprint(format)) * BlockBytes(format);
    }
}
