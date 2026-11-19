using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A texture whose tiles are made resident one by one (UpdateTile, UpdateTileRaw) and released
    /// (UnloadTile). Unbound keeps every tile of an ordinary texture of the same size, format and
    /// mips: a tile that is not loaded holds zeros, which is what the Editor samples there
    /// (Unity 6000.0.71f1 Editor on OpenGL Core and Vulkan, tests/projects/graphicsgaps sparse.*).
    /// A tile covers 64 KiB of the texture (the minimum size the API checks) in the device's
    /// standard sparse block shape: 256x256 texels of 1 byte, 256x128 of 2, 128x128 of 4
    /// (measured: RGBA32), 128x64 of 8 and 64x64 of 16 bytes (Vulkan's standard sparse image
    /// block shapes). Block-compressed formats are not supported (unmeasured): creating one
    /// fails with an error.
    /// </summary>
    public sealed partial class SparseTexture : Texture
    {
        /// <summary>The bytes a tile covers.</summary>
        const int TileBytes = 65536;

        Texture2D m_Tiles;
        /// <summary>Tiles changed since the texture was last sent: it is sent once, when a draw
        /// binds it or as the next frame renders (a game streams many tiles a frame, and
        /// materials and globals keep the texture they were given), under the same handle.</summary>
        bool m_TilesChanged;
        bool m_Listed;
        static readonly List<SparseTexture> s_Changed = new List<SparseTexture>();
        GraphicsFormat m_GraphicsFormat;
        int m_TileWidth, m_TileHeight;

        public int tileWidth => m_TileWidth;
        public int tileHeight => m_TileHeight;
        public bool isCreated => m_Tiles != null;
        public override GraphicsFormat graphicsFormat => m_GraphicsFormat;
        internal override GraphicsFormat GraphicsFormatOf() => m_GraphicsFormat;
        internal override int MipmapCount() => m_Tiles != null ? m_Tiles.mipmapCount : 1;

        public SparseTexture(int width, int height, DefaultFormat format, int mipCount)
            : this(width, height, SystemInfo.GetGraphicsFormat(format), mipCount)
        {
        }

        public SparseTexture(int width, int height, GraphicsFormat format, int mipCount)
        {
            if (ValidateFormat(format, width, height) && ValidateSize(width, height, format))
                Create(width, height, format, mipCount);
        }

        public SparseTexture(int width, int height, TextureFormat textureFormat, int mipCount)
            : this(width, height, textureFormat, mipCount, false)
        {
        }

        public SparseTexture(int width, int height, TextureFormat textureFormat, int mipCount, [Internal.DefaultValue("false")] bool linear)
        {
            if (GraphicsFormatUtility.IsCrunchFormat(textureFormat)) throw new ArgumentException("Crunched SparseTexture is not supported.");
            if (!ValidateFormat(textureFormat, width, height)) return;
            // sRGB only in a linear-space project, as Texture2D's (a gamma project's RGBA32 is
            // R8G8B8A8_UNorm: tests/projects/graphicsgaps sparse.graphicsFormat).
            var format = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, !linear && QualitySettings.activeColorSpace == ColorSpace.Linear);
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sparse))
                Debug.LogError($"Creation of a SparseTexture with '{textureFormat}' is not supported on this platform.");
            else if (ValidateSize(width, height, format))
                Create(width, height, format, mipCount);
        }

        internal bool ValidateFormat(TextureFormat format, int width, int height)
        {
            if (GraphicsFormatUtility.IsCompressedFormat(format))
            {
                Debug.LogError($"Unbound: a SparseTexture of the block-compressed format {format} is not supported (unmeasured).");
                return false;
            }
            return true;
        }

        internal bool ValidateFormat(GraphicsFormat format, int width, int height)
        {
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sparse))
            {
                Debug.LogError($"Texture creation failed. '{format}' is not supported for Sparse usage on this platform.");
                return false;
            }
            if (GraphicsFormatUtility.IsCompressedFormat(format))
            {
                Debug.LogError($"Unbound: a SparseTexture of the block-compressed format {format} is not supported (unmeasured).");
                return false;
            }
            return true;
        }

        internal bool ValidateSize(int width, int height, GraphicsFormat format)
        {
            long bytes = (long)GraphicsFormatUtility.GetBlockSize(format) * (width / GraphicsFormatUtility.GetBlockWidth(format)) * (height / GraphicsFormatUtility.GetBlockHeight(format));
            if (bytes < TileBytes)
            {
                Debug.LogError("SparseTexture creation failed. The minimum size in bytes of a SparseTexture is 64KB.", this);
                return false;
            }
            return true;
        }

        void Create(int width, int height, GraphicsFormat format, int mipCount)
        {
            var textureFormat = GraphicsFormatUtility.GetTextureFormat(format);
            int texel = (int)GraphicsFormatUtility.GetBlockSize(format);
            (m_TileWidth, m_TileHeight) = texel switch
            {
                1 => (256, 256),
                2 => (256, 128),
                4 => (128, 128),
                8 => (128, 64),
                16 => (64, 64),
                _ => (0, 0),
            };
            if (m_TileWidth == 0 || (int)textureFormat <= 0)
            {
                Debug.LogError($"Unbound: a SparseTexture of the format {format} is not supported (unmeasured).");
                return;
            }
            m_GraphicsFormat = format;
            m_Width = width;
            m_Height = height;
            m_Tiles = new Texture2D(width, height, format, mipCount, mipCount != 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None)
            {
                name = "SparseTexture tiles",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filterMode,
                wrapModeU = wrapModeU,
                wrapModeV = wrapModeV,
                anisoLevel = anisoLevel,
            };
            // Every tile starts unloaded: zero.
            for (int mip = 0; mip < m_Tiles.mipmapCount; mip++)
            {
                var pixels = m_Tiles.GetPixelData<byte>(mip);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = 0;
            }
            Changed();
        }

        /// <summary>The texture holding the tiles (null when creation failed), sent with the tiles
        /// changed since the last bind: what draws bind.</summary>
        internal Texture2D Tiles
        {
            get
            {
                Send();
                return m_Tiles;
            }
        }

        void Send()
        {
            if (!m_TilesChanged || m_Tiles == null) return;
            m_TilesChanged = false;
            m_Tiles.Apply(false, false);
        }

        void Changed()
        {
            m_TilesChanged = true;
            if (m_Listed) return;
            m_Listed = true;
            s_Changed.Add(this);
        }

        /// <summary>Sends every sparse texture whose tiles changed since it was last sent (once a
        /// frame, before its cameras render).</summary>
        internal static void SendChanged()
        {
            if (s_Changed.Count == 0) return;
            foreach (var texture in s_Changed)
            {
                if (texture is null) continue;
                texture.m_Listed = false;
                if (texture != null) texture.Send();
            }
            s_Changed.Clear();
        }

        internal override void OnSamplerChanged()
        {
            base.OnSamplerChanged();
            if (m_Tiles == null) return;
            m_Tiles.filterMode = filterMode;
            m_Tiles.wrapModeU = wrapModeU;
            m_Tiles.wrapModeV = wrapModeV;
            m_Tiles.anisoLevel = anisoLevel;
        }

        /// <summary>The texel rectangle of a tile at a mip, clipped to the mip; false when the tile
        /// is outside it.</summary>
        bool TileRect(int tileX, int tileY, int miplevel, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            if (m_Tiles == null || miplevel < 0 || miplevel >= m_Tiles.mipmapCount || tileX < 0 || tileY < 0) return false;
            int mipWidth = Math.Max(1, m_Width >> miplevel), mipHeight = Math.Max(1, m_Height >> miplevel);
            x = tileX * m_TileWidth;
            y = tileY * m_TileHeight;
            if (x >= mipWidth || y >= mipHeight) return false;
            w = Math.Min(m_TileWidth, mipWidth - x);
            h = Math.Min(m_TileHeight, mipHeight - y);
            return true;
        }

        public void UpdateTile(int tileX, int tileY, int miplevel, Color32[] data)
        {
            if (!TileRect(tileX, tileY, miplevel, out int x, out int y, out int w, out int h)) return;
            if (data == null || data.Length < w * h) throw new ArgumentException("SparseTexture.UpdateTile: the data is smaller than the tile.");
            var colors = data.Length == w * h ? data : data.AsSpan(0, w * h).ToArray();
            m_Tiles.SetPixels32(x, y, w, h, colors, miplevel);
            Changed();
            IncrementUpdateCount();
        }

        public void UpdateTileRaw(int tileX, int tileY, int miplevel, byte[] data)
        {
            if (!TileRect(tileX, tileY, miplevel, out int x, out int y, out int w, out int h)) return;
            int texel = (int)GraphicsFormatUtility.GetBlockSize(m_GraphicsFormat);
            int mipWidth = Math.Max(1, m_Width >> miplevel);
            var pixels = m_Tiles.GetPixelData<byte>(miplevel);
            // No data unloads the tile: its texels are zero again.
            if (data != null && data.Length < w * h * texel) throw new ArgumentException("SparseTexture.UpdateTileRaw: the data is smaller than the tile.");
            for (int row = 0; row < h; row++)
            {
                int target = ((y + row) * mipWidth + x) * texel;
                for (int i = 0; i < w * texel; i++) pixels[target + i] = data != null ? data[row * w * texel + i] : (byte)0;
            }
            Changed();
            IncrementUpdateCount();
        }

        public void UnloadTile(int tileX, int tileY, int miplevel) => UpdateTileRaw(tileX, tileY, miplevel, null);

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            if (m_Tiles != null) DestroyImmediate(m_Tiles);
            m_Tiles = null;
        }
    }
}
