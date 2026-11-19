using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        // The Editor retains CPU storage even when isReadable forbids pixel API access.
        // CopyTexture propagates that storage; subsequent Instantiate/GetRawTextureData see it.
        /// <summary>The retained CPU pixels a Graphics.CopyTexture of first levels copies besides the
        /// GPU copy (a width of -1: the whole level, its bytes as they are, so that a BGRA32 level
        /// copied into an RGBA32 one trades red and blue, as the Unity 6000.0.71f1 macOS Editor's
        /// CommandBuffer.CopyTexture does, tests/projects/cmdbuffers metal.jsonl
        /// `metal.copy.bgra32.rgba32.cpu`; a region its texels' bytes alike, between formats of one
        /// texel size); other levels keep theirs, and a region that leaves either level copies
        /// nothing here (the GPU copy reports it).</summary>
        internal static void CopyRetained(Texture2D source, int sourceMip, int x, int y, int width, int height,
            Texture2D destination, int destinationMip, int destinationX, int destinationY)
        {
            if (sourceMip != 0 || destinationMip != 0) return;
            static bool Fits(Texture2D t, int left, int bottom, int w, int h) =>
                left >= 0 && bottom >= 0 && h >= 0 && (long)left + w <= t.width && (long)bottom + h <= t.height;
            if (width >= 0 && (!Fits(source, x, y, width, height) || !Fits(destination, destinationX, destinationY, width, height))) return;
            if (width < 0) source.CopyRetainedLevel(destination);
            else source.CopyRetainedBytes(destination, x, y, width, height, destinationX, destinationY);
        }

        /// <summary>A fitted region of the first level into <paramref name="destination"/>'s, its
        /// texels' bytes as they are, texel by texel (no level-sized arrays: command buffers repeat
        /// it every execution), between formats of one texel size whose cells Unbound encodes
        /// (Texture2DTexels.Coded); other pairs copy nothing on the CPU (unmeasured).</summary>
        void CopyRetainedBytes(Texture2D destination, int x, int y, int width, int height, int destinationX, int destinationY)
        {
            int texel = TexelBytes;
            if (texel == 0 || texel > MaxTexelBytes || texel != destination.TexelBytes
                || !Texture2DTexels.Coded(m_Format) || !Texture2DTexels.Coded(destination.m_Format)) return;
            if (!EnsureRetainedPixels() || !destination.EnsureRetainedPixels()) return;
            Span<byte> bytes = stackalloc byte[MaxTexelBytes];
            var cell = bytes.Slice(0, texel);
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    ReadTexelBytes((y + row) * m_Width + x + column, cell);
                    destination.WriteTexelBytes((destinationY + row) * destination.m_Width + destinationX + column, cell);
                }
            destination.m_UnreadableRaw = null;
            destination.DropIfNotReadable();
            DropIfNotReadable();
        }

        /// <summary>The largest texel (RGBAFloat).</summary>
        const int MaxTexelBytes = 16;

        /// <summary>A first-level texel's bytes in the texture's format.</summary>
        void ReadTexelBytes(int i, Span<byte> cell)
        {
            if (CellStore) { CheckStoreRead(); StoreTexel(0, i).CopyTo(cell); }
            else if (m_Hdr != null) Texture2DTexels.EncodeFloatCell(m_Format, FloatCell(0, i), cell);
            else Texture2DTexels.EncodeCell(m_Format, Cell(0, i), cell);
        }

        /// <summary>Sets a first-level texel from its bytes in the texture's format.</summary>
        void WriteTexelBytes(int i, ReadOnlySpan<byte> cell)
        {
            if (CellStore) { CheckStoreWrite(); cell.CopyTo(StoreTexel(0, i)); }
            else if (m_Hdr != null) SetFloatCell(0, i, Texture2DTexels.DecodeFloatCell(m_Format, cell));
            else SetCell(0, i, Texture2DTexels.DecodeCell(m_Format, cell));
        }

        internal void CopyRetainedPixels(Texture2D destination, int x, int y, int width, int height, int destinationX, int destinationY)
        {
            // Graphics.CopyTexture has fitted the regions to both textures.
            if (!EnsureRetainedPixels() || !destination.EnsureRetainedPixels()) return;
            var pixels = new Color[width * height];
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                    pixels[row * width + column] = Read(0, (y + row) * m_Width + x + column);
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                    destination.Write(0, (destinationY + row) * destination.m_Width + destinationX + column, pixels[row * width + column]);
            destination.m_UnreadableRaw = null;
            destination.DropIfNotReadable();
            DropIfNotReadable();
            // The existing GPU copy follows this bookkeeping. Do not upload CPU pixels here:
            // the GPU's source may contain changes which have no CPU representation.
        }

        /// <summary>A whole first level into <paramref name="destination"/>'s: its bytes in order
        /// where both levels hold as many (levels of other sizes: the Unity 6000.0.71f1 Editor,
        /// tests/projects/apierrors `texture.copy_case.element_same_bytes_other_size`; of other
        /// formats inferred from `element_same_bytes_other_format`, unmeasured on its own);
        /// otherwise the pixels both levels hold (unmeasured). The GPU copy that follows takes the
        /// region both levels share (the Editor's GPU side of this case is unmeasured).</summary>
        internal void CopyRetainedLevel(Texture2D destination)
        {
            if (!EnsureRetainedPixels() || !destination.EnsureRetainedPixels()) return;
            if (RawMipSize(0) != destination.RawMipSize(0))
            {
                CopyRetainedPixels(destination, 0, 0, Math.Min(m_Width, destination.m_Width), Math.Min(m_Height, destination.m_Height), 0, 0);
                return;
            }
            destination.DecodeLevel(EncodeLevel(0), 0);
            destination.m_UnreadableRaw = null;
            destination.DropIfNotReadable();
            DropIfNotReadable();
        }

        bool EnsureRetainedPixels()
        {
            DecodeRaw();
            if (m_Pixels == null && m_Hdr == null)
            {
                if (m_UnreadableRaw != null) DecodeMipChain(m_UnreadableRaw);
                else if (m_AssetGuid != null) FetchImportedPixels();
            }
            return m_Pixels != null || m_Hdr != null;
        }
    }
}
