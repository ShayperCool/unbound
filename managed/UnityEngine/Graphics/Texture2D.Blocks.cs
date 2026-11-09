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
        Color BlockTexel(int mip, int i)
        {
            var format = m_SourceFormat.Value;
            int size = BlockFormats.BlockBytes(format);
            int w = MipWidth(mip), x = i % w, y = i / w;
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            at += ((y / 4) * ((w + 3) / 4) + x / 4) * size;
            var block = new ReadOnlySpan<byte>(m_Store, at, size);
            int k = (y % 4) * 4 + x % 4;
            Color32 texel;
            if (format is TextureFormat.BC6H or TextureFormat.BC7)
            {
                // One block decoded as a 4x4 level.
                var json = Native.Call("texture-decode-blocks", JsonSerializer.Serialize(new
                {
                    format = (int)format,
                    width = 4,
                    height = 4,
                    data = Convert.ToBase64String(block),
                }));
                using var doc = JsonDocument.Parse(json);
                var rgba = Convert.FromBase64String(doc.RootElement.GetProperty("rgba").GetString());
                texel = new Color32(rgba[k * 4], rgba[k * 4 + 1], rgba[k * 4 + 2], rgba[k * 4 + 3]);
            }
            else texel = BlockFormats.DecodeTexel(format, block, k);
            if (Texture2DTexels.FloatStored(m_Format)) return new Color(texel.r / 255f, texel.g / 255f, texel.b / 255f, 1f);
            return Texture2DTexels.Pixel(m_Format, texel);
        }

        /// <summary>Decodes one level of blocks into the pixel cells.</summary>
        void DecodeBlockLevel(ReadOnlySpan<byte> blocks, int mip)
        {
            int w = MipWidth(mip), h = MipHeight(mip);
            var json = Native.Call("texture-decode-blocks", JsonSerializer.Serialize(new
            {
                format = (int)m_SourceFormat.Value,
                width = w,
                height = h,
                data = Convert.ToBase64String(blocks),
            }));
            using var doc = JsonDocument.Parse(json);
            var rgba = Convert.FromBase64String(doc.RootElement.GetProperty("rgba").GetString());
            if (Texture2DTexels.FloatStored(m_Format))
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
            var blocks = CurrentBlocks();
            int at = 0;
            for (int m = 0; m < mip; m++) at += SourceLevelSize(m);
            CheckStoreWrite();
            data.Slice(0, size).CopyTo(new Span<byte>(blocks, at, size));
            EnsureBlockCells();
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
            var blocks = CurrentBlocks();
            CheckStoreWrite();
            data.Slice(0, total).CopyTo(blocks);
            EnsureBlockCells();
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

        /// <summary>Uploads a block-compressed texture's blocks.</summary>
        unsafe void UploadBlocks()
        {
            var blocks = CurrentBlocks();
            m_GpuMipCount = m_SourceMips;
            fixed (byte* b = blocks)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = BlockFormats.GpuCode(m_SourceFormat.Value, m_Linear),
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

        /// <summary>A whole level's pixels as GetPixels and GetPixels32 answer them: a compressed
        /// texture's level smaller than a block is the whole decoded block, the level repeated as
        /// tiles (the Unity 6000.0.71f1 Editor, tests/projects/texturecompression pixels5 and
        /// pixels6 of 2x2 and 1x1 levels).</summary>
        T[] BlockTiled<T>(T[] level, int mip)
        {
            int w = MipWidth(mip), h = MipHeight(mip);
            if (!SourceBlocks || (w >= 4 && h >= 4)) return level;
            int bw = (w + 3) / 4 * 4, bh = (h + 3) / 4 * 4;
            var tiled = new T[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    tiled[y * bw + x] = level[(y % h) * w + x % w];
            return tiled;
        }

        /// <summary>Bytes of a source level of a block-compressed texture.</summary>
        int SourceLevelSize(int mip) =>
            BlockFormats.LevelSize(m_SourceFormat.Value, Math.Max(1, m_Width >> mip), Math.Max(1, m_Height >> mip));
    }
}

namespace Unbound.Runtime
{
    /// <summary>Block-compressed texture formats: 4x4 texel blocks, a level at least one block.</summary>
    internal static class BlockFormats
    {
        /// <summary>Bytes of one block, or 0 for formats that are not BC1-BC7.</summary>
        internal static int BlockBytes(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.DXT1 or UnityEngine.TextureFormat.BC4 => 8,
            UnityEngine.TextureFormat.DXT5 or UnityEngine.TextureFormat.BC5 or UnityEngine.TextureFormat.BC6H
                or UnityEngine.TextureFormat.BC7 => 16,
            _ => 0,
        };

        /// <summary>The renderer's packet code of a block format: BC1, BC3 and BC7 colour (sampled
        /// through an sRGB view in linear projects) or linear data; BC4, BC5 and BC6H.</summary>
        internal static int GpuCode(UnityEngine.TextureFormat format, bool linear) => format switch
        {
            UnityEngine.TextureFormat.DXT1 => linear ? 39 : 38,
            UnityEngine.TextureFormat.DXT5 => linear ? 41 : 40,
            UnityEngine.TextureFormat.BC7 => linear ? 8 : 7,
            UnityEngine.TextureFormat.BC4 => 42,
            UnityEngine.TextureFormat.BC5 => 43,
            UnityEngine.TextureFormat.BC6H => 44,
            _ => throw new ArgumentException($"{format} is not a block format"),
        };

        /// <summary>Texel `k` (row-major in the 4x4 block) of a DXT1, DXT5, BC4 or BC5 block, as
        /// the importer's decoding (and the Editor's GetPixels32) reads it: colours interpolate with
        /// truncating thirds (a half in DXT1's three-colour mode, whose fourth entry is
        /// transparent black), alpha, red and green steps round to nearest.</summary>
        internal static UnityEngine.Color32 DecodeTexel(UnityEngine.TextureFormat format, ReadOnlySpan<byte> block, int k)
        {
            switch (format)
            {
                case UnityEngine.TextureFormat.DXT1:
                    return Colour(block, k, false);
                case UnityEngine.TextureFormat.DXT5:
                {
                    var c = Colour(block.Slice(8), k, true);
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

        static UnityEngine.Color32 Colour(ReadOnlySpan<byte> block, int k, bool four)
        {
            int c0 = block[0] | block[1] << 8, c1 = block[2] | block[3] << 8;
            int index = (int)((uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24) >> (2 * k)) & 3;
            if (index == 3 && !four && c0 <= c1) return new UnityEngine.Color32(0, 0, 0, 0);
            (int wa, int wb, int d) = index switch
            {
                0 => (1, 0, 1),
                1 => (0, 1, 1),
                2 when four || c0 > c1 => (2, 1, 3),
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
            ((width + 3) / 4) * ((height + 3) / 4) * BlockBytes(format);
    }
}
