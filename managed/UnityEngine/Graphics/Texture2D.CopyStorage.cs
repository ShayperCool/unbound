using System;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        // The Editor retains CPU storage even when isReadable forbids pixel API access.
        // CopyTexture propagates that storage; subsequent Instantiate/GetRawTextureData see it.
        internal void CopyRetainedPixels(Texture2D destination, int x, int y, int width, int height, int destinationX, int destinationY)
        {
            if (!EnsureRetainedPixels() || !destination.EnsureRetainedPixels()) return;
            CheckBlock(x, y, width, height, 0);
            destination.CheckBlock(destinationX, destinationY, width, height, 0);
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

        bool EnsureRetainedPixels()
        {
            EnsurePixels();
            if (m_Pixels == null && m_Hdr == null)
            {
                if (m_UnreadableRaw != null) DecodeMipChain(m_UnreadableRaw);
                else if (m_AssetGuid != null) FetchImportedPixels();
            }
            return m_Pixels != null || m_Hdr != null;
        }
    }
}
