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

        /// <summary>Bytes of a `width` x `height` level.</summary>
        internal static int LevelSize(UnityEngine.TextureFormat format, int width, int height) =>
            ((width + 3) / 4) * ((height + 3) / 4) * BlockBytes(format);
    }
}
