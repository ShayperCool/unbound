using System;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        // The Editor retains CPU storage even when isReadable forbids pixel API access.
        // CopyTexture propagates that storage; subsequent Instantiate/GetRawTextureData see it.
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
