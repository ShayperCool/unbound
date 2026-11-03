using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        // Level zero remains in m_Pixels/m_Hdr for font atlases and other engine users.
        Color32[][] m_MipPixels;
        Color[][] m_MipHdr;
        byte[] m_UnreadableRaw;

        int MipWidth(int mip) => Math.Max(1, m_Width >> mip);
        int MipHeight(int mip) => Math.Max(1, m_Height >> mip);
        bool ValidMip(int mip) => mip >= 0 && mip < m_MipCount;

        void CheckMip(int mip)
        {
            if (!ValidMip(mip)) throw new ArgumentException("Texture mip level is out of range.");
        }

        Color32[] ByteLevel(int mip)
        {
            if (mip == 0) return m_Pixels;
            m_MipPixels ??= new Color32[m_MipCount][];
            if (m_MipPixels[mip] == null)
            {
                var data = m_MipPixels[mip] = new Color32[MipWidth(mip) * MipHeight(mip)];
                for (int i = 0; i < data.Length; i++) data[i] = new Color32(205, 205, 205, 205);
            }
            return m_MipPixels[mip];
        }

        Color[] FloatLevel(int mip)
        {
            if (mip == 0) return m_Hdr;
            m_MipHdr ??= new Color[m_MipCount][];
            if (m_MipHdr[mip] == null)
            {
                var data = m_MipHdr[mip] = new Color[MipWidth(mip) * MipHeight(mip)];
                for (int i = 0; i < data.Length; i++) data[i] = Color.white * (205f / 255f);
            }
            return m_MipHdr[mip];
        }

        /// <summary>A texel's cell (byte formats), from the store while a pixel data view exists.</summary>
        Color32 Cell(int mip, int i)
        {
            CheckStoreRead();
            return m_Store != null ? Texture2DTexels.DecodeCell(m_Format, StoreTexel(mip, i)) : ByteLevel(mip)[i];
        }
        /// <summary>A texel's cell (R16 and float formats).</summary>
        Color FloatCell(int mip, int i)
        {
            CheckStoreRead();
            return m_Store != null ? Texture2DTexels.DecodeFloatCell(m_Format, StoreTexel(mip, i)) : FloatLevel(mip)[i];
        }

        void SetCell(int mip, int i, Color32 cell)
        {
            CheckStoreWrite();
            if (m_Store != null) Texture2DTexels.EncodeCell(m_Format, cell, StoreTexel(mip, i));
            else ByteLevel(mip)[i] = cell;
        }

        void SetFloatCell(int mip, int i, Color cell)
        {
            CheckStoreWrite();
            if (m_Store != null) Texture2DTexels.EncodeFloatCell(m_Format, cell, StoreTexel(mip, i));
            else FloatLevel(mip)[i] = cell;
        }

        Color Read(int i) => Read(0, i);
        /// <summary>A texel as GetPixel reads it.</summary>
        Color Read(int mip, int i) => m_Hdr != null ? FloatCell(mip, i) : Texture2DTexels.Pixel(m_Format, Cell(mip, i));
        void Write(int i, Color c) => Write(0, i, c);

        int FloatChannels => m_Format switch
        {
            TextureFormat.RHalf or TextureFormat.RFloat or TextureFormat.R16 => 1,
            TextureFormat.RGHalf or TextureFormat.RGFloat => 2,
            _ => 4,
        };

        void Write(int mip, int i, Color c)
        {
            if (m_Hdr != null)
            {
                if (m_Format == TextureFormat.R16)
                {
                    SetFloatCell(mip, i, Texture2DTexels.QuantizeFloat(m_Format, c));
                    return;
                }
                int channels = FloatChannels;
                bool half = m_Format is TextureFormat.RHalf or TextureFormat.RGHalf or TextureFormat.RGBAHalf;
                for (int k = 0; k < 4; k++) c[k] = k >= channels ? 1f : half ? (float)(Half)c[k] : c[k];
                SetFloatCell(mip, i, c);
            }
            // Texture setters round half upward; Color32's managed conversion uses Round.
            else SetCell(mip, i, Texture2DTexels.Quantize(m_Format, c));
        }

        public Color GetPixel(int x, int y) => GetPixel(x, y, 0);
        public Color GetPixel(int x, int y, int mipLevel)
        {
            RequireReadable();
            if (!ValidMip(mipLevel)) { Debug.LogError("Texture mip level is out of range."); return Color.white; }
            int w = MipWidth(mipLevel), h = MipHeight(mipLevel);
            return Read(mipLevel, Wrap(y, h, wrapModeV) * w + Wrap(x, w, wrapModeU));
        }

        static int Wrap(int v, int size, TextureWrapMode mode)
        {
            if (mode == TextureWrapMode.Repeat) return ((v % size) + size) % size;
            if (mode == TextureWrapMode.Mirror)
            {
                int i = ((v % (size * 2)) + size * 2) % (size * 2);
                return i >= size ? size * 2 - 1 - i : i;
            }
            if (mode == TextureWrapMode.MirrorOnce && v < 0) v = -1 - v;
            return Math.Max(0, Math.Min(size - 1, v));
        }

        Color Bilinear(float u, float v, int mip, bool texelCenters = false)
        {
            int w = MipWidth(mip), h = MipHeight(mip);
            float offset = texelCenters ? 0.5f : 0f;
            float x = u * w - offset, y = v * h - offset;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            Color c00 = Read(mip, Wrap(y0, h, wrapModeV) * w + Wrap(x0, w, wrapModeU));
            Color c10 = Read(mip, Wrap(y0, h, wrapModeV) * w + Wrap(x0 + 1, w, wrapModeU));
            Color c01 = Read(mip, Wrap(y0 + 1, h, wrapModeV) * w + Wrap(x0, w, wrapModeU));
            Color c11 = Read(mip, Wrap(y0 + 1, h, wrapModeV) * w + Wrap(x0 + 1, w, wrapModeU));
            return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
        }

        public Color GetPixelBilinear(float u, float v) => GetPixelBilinear(u, v, 0);
        /// <summary>The four GetPixel values around (u w, v h) (no half-texel offset: a texel
        /// centre mixes its right and upper neighbours in), each lerp a (1 - t) + b t in single
        /// precision, along x first (tests/projects/texturepixels).</summary>
        public Color GetPixelBilinear(float u, float v, int mipLevel)
        {
            RequireReadable();
            if (!ValidMip(mipLevel)) { Debug.LogError("Texture mip level is out of range."); return Color.white; }
            int w = MipWidth(mipLevel), h = MipHeight(mipLevel);
            float x = u * w, y = v * h;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            int r0 = Wrap(y0, h, wrapModeV) * w, r1 = Wrap(y0 + 1, h, wrapModeV) * w;
            int c0 = Wrap(x0, w, wrapModeU), c1 = Wrap(x0 + 1, w, wrapModeU);
            Color a = Read(mipLevel, r0 + c0), b = Read(mipLevel, r0 + c1), c = Read(mipLevel, r1 + c0), d = Read(mipLevel, r1 + c1);
            static float Lerp(float p, float q, float t) => p * (1f - t) + q * t;
            var result = new Color();
            for (int k = 0; k < 4; k++) result[k] = Lerp(Lerp(a[k], b[k], fx), Lerp(c[k], d[k], fx), fy);
            return result;
        }

        public void SetPixel(int x, int y, Color color) => SetPixel(x, y, color, 0);
        public void SetPixel(int x, int y, Color color, int mipLevel)
        {
            RequireReadable();
            if (!ValidMip(mipLevel)) { Debug.LogError("Texture mip level is out of range."); return; }
            int w = MipWidth(mipLevel), h = MipHeight(mipLevel);
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            Write(mipLevel, y * w + x, color);
            CpuTexelsChanged();
        }

        void CheckBlock(int x, int y, int w, int h, int mip)
        {
            CheckMip(mip);
            if (x < 0 || y < 0 || w < 0 || h < 0 || (long)x + w > MipWidth(mip) || (long)y + h > MipHeight(mip))
                throw new ArgumentException("Texture pixel block is out of bounds.");
        }

        public Color[] GetPixels() => GetPixels(0);
        public Color[] GetPixels(int miplevel)
        {
            if (m_Width == 0) throw LayeredTextures.NotReadable("Texture2D.GetPixels", name);
            RequireReadable(true);
            CheckMip(miplevel);
            return GetPixels(0, 0, MipWidth(miplevel), MipHeight(miplevel), miplevel);
        }
        /// <summary>Level zero with each stored byte as b / 255 (the Color32 conversion), for engine
        /// paths that are not GetPixels.</summary>
        internal Color[] ColorsOfLevelZero()
        {
            RequireReadable(true);
            var result = new Color[m_Width * m_Height];
            for (int i = 0; i < result.Length; i++) result[i] = Read(0, i);
            return result;
        }

        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight) => GetPixels(x, y, blockWidth, blockHeight, 0);
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight, int miplevel)
        {
            RequireReadable(true);
            CheckBlock(x, y, blockWidth, blockHeight, miplevel);
            var result = new Color[blockWidth * blockHeight];
            int width = MipWidth(miplevel);
            // GetPixels reads a stored byte b as b * (1 / 255) in single precision, which differs
            // from b / 255 (GetPixel, Alpha8's GetPixels) in the last bit for 126 of the 256 values.
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++)
                {
                    int at = (y + j) * width + x + i;
                    result[j * blockWidth + i] = m_Hdr != null ? Texture2DTexels.Pixels(m_Format, FloatCell(miplevel, at)) : Texture2DTexels.Pixels(m_Format, Cell(miplevel, at));
                }
            return result;
        }

        public Color32[] GetPixels32() => GetPixels32(0);
        public Color32[] GetPixels32(int miplevel)
        {
            RequireReadable(true);
            CheckMip(miplevel);
            var result = new Color32[MipWidth(miplevel) * MipHeight(miplevel)];
            for (int i = 0; i < result.Length; i++)
                result[i] = m_Hdr != null ? Texture2DTexels.Pixel32(m_Format, FloatCell(miplevel, i)) : Texture2DTexels.Pixel32(m_Format, Cell(miplevel, i));
            return result;
        }

        public void SetPixels(Color[] colors) => SetPixels(colors, 0);
        public void SetPixels(Color[] colors, int miplevel)
        {
            RequireReadable();
            CheckMip(miplevel);
            SetPixels(0, 0, MipWidth(miplevel), MipHeight(miplevel), colors, miplevel);
        }
        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors) => SetPixels(x, y, blockWidth, blockHeight, colors, 0);
        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors, int miplevel)
        {
            RequireReadable();
            CheckBlock(x, y, blockWidth, blockHeight, miplevel);
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (colors.Length < (long)blockWidth * blockHeight) throw new ArgumentException("Not enough pixel data.");
            int width = MipWidth(miplevel);
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++) Write(miplevel, (y + j) * width + x + i, colors[j * blockWidth + i]);
            CpuTexelsChanged();
        }

        public void SetPixels32(Color32[] colors) => SetPixels32(colors, 0);
        public void SetPixels32(Color32[] colors, int miplevel)
        {
            RequireReadable(true);
            CheckMip(miplevel);
            SetPixels32(0, 0, MipWidth(miplevel), MipHeight(miplevel), colors, miplevel);
        }
        public void SetPixels32(int x, int y, int blockWidth, int blockHeight, Color32[] colors) => SetPixels32(x, y, blockWidth, blockHeight, colors, 0);
        public void SetPixels32(int x, int y, int blockWidth, int blockHeight, Color32[] colors, int miplevel)
        {
            RequireReadable(true);
            CheckBlock(x, y, blockWidth, blockHeight, miplevel);
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (colors.Length < (long)blockWidth * blockHeight) throw new ArgumentException("Not enough pixel data.");
            int width = MipWidth(miplevel);
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++) Write(miplevel, (y + j) * width + x + i, colors[j * blockWidth + i]);
            CpuTexelsChanged();
        }

        int RawMipSize(int mip) => Texture2DTexels.Size(m_Format, MipWidth(mip), MipHeight(mip));
        int RawChainSize()
        {
            int bytes = 0;
            for (int mip = 0; mip < m_MipCount; mip++) bytes = checked(bytes + RawMipSize(mip));
            return bytes;
        }

        /// <summary>A level from bytes in the texture's format (into the store while it exists).</summary>
        void DecodeLevel(ReadOnlySpan<byte> data, int mip)
        {
            int size = RawMipSize(mip);
            if (m_Store != null)
            {
                CheckStoreWrite();
                data.Slice(0, size).CopyTo(new Span<byte>(m_Store, m_StoreOffsets[mip], size));
                return;
            }
            int n = TexelBytes;
            if (m_Hdr != null)
            {
                var pixels = FloatLevel(mip);
                if (m_Format == TextureFormat.R16)
                {
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = Texture2DTexels.DecodeFloatCell(m_Format, data.Slice(i * n, n));
                    return;
                }
                PixelFormats.DecodeFloat(m_Format, data.ToArray(), pixels);
                for (int i = 0; i < pixels.Length; i++)
                    for (int k = FloatChannels; k < 4; k++) pixels[i][k] = 1f;
            }
            else if (Texture2DTexels.Coded(m_Format))
            {
                var level = ByteLevel(mip);
                for (int i = 0; i < level.Length; i++) level[i] = Texture2DTexels.DecodeCell(m_Format, data.Slice(i * n, n));
            }
            else PixelFormats.Decode(m_Format, data.ToArray(), ByteLevel(mip));
        }

        /// <summary>A level as bytes in the texture's format.</summary>
        byte[] EncodeLevel(int mip)
        {
            CheckStoreRead();
            int size = RawMipSize(mip);
            if (m_Store != null) return new ReadOnlySpan<byte>(m_Store, m_StoreOffsets[mip], size).ToArray();
            int n = TexelBytes, texels = MipWidth(mip) * MipHeight(mip);
            if (m_Hdr != null && m_Format != TextureFormat.R16) return PixelFormats.EncodeFloat(m_Format, FloatLevel(mip));
            if (m_Hdr == null && !Texture2DTexels.Coded(m_Format)) return PixelFormats.Encode(m_Format, ByteLevel(mip));
            var bytes = new byte[size];
            for (int i = 0; i < texels; i++)
            {
                var texel = new Span<byte>(bytes, i * n, n);
                if (m_Hdr != null) Texture2DTexels.EncodeFloatCell(m_Format, FloatLevel(mip)[i], texel);
                else Texture2DTexels.EncodeCell(m_Format, ByteLevel(mip)[i], texel);
            }
            return bytes;
        }

        void DecodeMipChain(byte[] data)
        {
            if (Texture2DTexels.FloatStored(m_Format))
            {
                m_Hdr ??= new Color[m_Width * m_Height];
                m_Pixels = null;
            }
            else m_Pixels ??= new Color32[m_Width * m_Height];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                int size = RawMipSize(mip);
                if (size == 0 || at + size > data.Length)
                    throw new UnityException($"Texture '{name}' has incomplete pixel data for mip {mip}.");
                DecodeLevel(new ReadOnlySpan<byte>(data, at, size), mip);
                at += size;
            }
        }

        public void LoadRawTextureData(byte[] data)
        {
            RequireReadable();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < RawChainSize()) throw new UnityException("LoadRawTextureData: not enough data provided.");
            DecodeMipChain(data);
            CpuTexelsChanged();
        }
        public void LoadRawTextureData(IntPtr data, int size)
        {
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            LoadRawTextureData(bytes);
        }
        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) where T : struct
        {
            RequireReadable();
            CheckMip(mipLevel);
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (sourceDataStartIndex < 0 || sourceDataStartIndex > data.Length) throw new ArgumentOutOfRangeException(nameof(sourceDataStartIndex));
            var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data, sourceDataStartIndex, data.Length - sourceDataStartIndex));
            int size = RawMipSize(mipLevel);
            if (bytes.Length < size) throw new ArgumentException("SetPixelData: not enough data provided.");
            DecodeLevel(bytes.Slice(0, size), mipLevel);
            CpuTexelsChanged();
        }
        public byte[] GetRawTextureData()
        {
            if (m_SourceRaw != null) return (byte[])m_SourceRaw.Clone();
            if (!m_IsReadable)
            {
                if (m_UnreadableRaw != null) return (byte[])m_UnreadableRaw.Clone();
                if (m_AssetGuid != null)
                {
                    FetchImportedPixels();
                    if (m_Pixels != null || m_Hdr != null)
                    {
                        m_UnreadableRaw = EncodeMipChain();
                        DropIfNotReadable();
                        return (byte[])m_UnreadableRaw.Clone();
                    }
                }
            }
            RequireReadable();
            return EncodeMipChain();
        }

        byte[] EncodeMipChain()
        {
            if (m_Store != null) return (byte[])m_Store.Clone();
            var data = new byte[RawChainSize()];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                var bytes = EncodeLevel(mip);
                bytes.CopyTo(data, at);
                at += bytes.Length;
            }
            return data;
        }

        /// <summary>Unity generates each level by bilinearly sampling the previous one at the new
        /// texel centers. Byte results truncate after each axis, including NPOT dimensions
        /// (resources mips probe).</summary>
        void GenerateMipChain()
        {
            for (int mip = 1; mip < m_MipCount; mip++)
            {
                int w = MipWidth(mip), h = MipHeight(mip);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = Bilinear((x + 0.5f) / w, (y + 0.5f) / h, mip - 1, true);
                        if (m_Hdr != null) Write(mip, y * w + x, c);
                        else
                        {
                            int sw = MipWidth(mip - 1), sh = MipHeight(mip - 1);
                            float sx = (x + 0.5f) * sw / w - 0.5f, sy = (y + 0.5f) * sh / h - 0.5f;
                            int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                            float fx = sx - ix, fy = sy - iy;
                            var a = Cell(mip - 1, Math.Clamp(iy, 0, sh - 1) * sw + Math.Clamp(ix, 0, sw - 1));
                            var b = Cell(mip - 1, Math.Clamp(iy, 0, sh - 1) * sw + Math.Clamp(ix + 1, 0, sw - 1));
                            var d = Cell(mip - 1, Math.Clamp(iy + 1, 0, sh - 1) * sw + Math.Clamp(ix, 0, sw - 1));
                            var e = Cell(mip - 1, Math.Clamp(iy + 1, 0, sh - 1) * sw + Math.Clamp(ix + 1, 0, sw - 1));
                            byte Filter(byte av, byte bv, byte dv, byte ev)
                            {
                                int top = (int)Math.Floor(av + (bv - av) * fx);
                                int bottom = (int)Math.Floor(dv + (ev - dv) * fx);
                                return (byte)Math.Clamp((int)Math.Floor(top + (bottom - top) * fy), 0, 255);
                            }
                            SetCell(mip, y * w + x, Texture2DTexels.Canonical(m_Format, new Color32(Filter(a.r, b.r, d.r, e.r), Filter(a.g, b.g, d.g, e.g),
                                Filter(a.b, b.b, d.b, e.b), Filter(a.a, b.a, d.a, e.a))));
                        }
                    }
            }
        }

        Color32[] UploadByteChain()
        {
            if (m_MipCount == 1 && m_Store == null) return m_Pixels;
            int count = 0;
            for (int mip = 0; mip < m_MipCount; mip++) count += ByteLevel(mip).Length;
            var result = new Color32[count];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                var level = ByteLevel(mip);
                if (m_Store != null)
                    for (int i = 0; i < level.Length; i++) result[at + i] = Cell(mip, i);
                else level.CopyTo(result, at);
                at += level.Length;
            }
            return result;
        }

        Color[] UploadHdrChain()
        {
            int count = 0;
            for (int mip = 0; mip < m_MipCount; mip++) count += FloatLevel(mip).Length;
            var result = new Color[count];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
                for (int i = 0, n = MipWidth(mip) * MipHeight(mip); i < n; i++)
                {
                    var c = FloatCell(mip, i);
                    // CPU reads fill missing channels with one; the GPU fills RGB with zero.
                    for (int k = FloatChannels; k < 3; k++) c[k] = 0f;
                    result[at++] = c;
                }
            return result;
        }
    }
}
