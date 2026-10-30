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

        Color Read(int i) => Read(0, i);
        Color Read(int mip, int i) => m_Hdr != null ? FloatLevel(mip)[i] : (Color)ByteLevel(mip)[i];
        void Write(int i, Color c) => Write(0, i, c);

        int FloatChannels => m_Format switch
        {
            TextureFormat.RHalf or TextureFormat.RFloat => 1,
            TextureFormat.RGHalf or TextureFormat.RGFloat => 2,
            _ => 4,
        };

        void Write(int mip, int i, Color c)
        {
            if (m_Hdr != null)
            {
                int channels = FloatChannels;
                bool half = m_Format is TextureFormat.RHalf or TextureFormat.RGHalf or TextureFormat.RGBAHalf;
                for (int k = 0; k < 4; k++) c[k] = k >= channels ? 1f : half ? (float)(Half)c[k] : c[k];
                FloatLevel(mip)[i] = c;
            }
            else
            {
                // Texture setters round half upward; Color32's managed conversion uses Round.
                byte Channel(float v) => (byte)Math.Floor(Mathf.Clamp01(v) * 255f + 0.5f);
                var color = new Color32(Channel(c.r), Channel(c.g), Channel(c.b), Channel(c.a));
                if (m_Format == TextureFormat.RGB24) color.a = 255;
                else if (m_Format == TextureFormat.Alpha8) color.r = color.g = color.b = 255;
                else if (m_Format == TextureFormat.R8) { color.g = color.b = 0; color.a = 255; }
                ByteLevel(mip)[i] = color;
            }
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
        public Color GetPixelBilinear(float u, float v, int mipLevel)
        {
            RequireReadable();
            if (!ValidMip(mipLevel)) { Debug.LogError("Texture mip level is out of range."); return Color.white; }
            return Bilinear(u, v, mipLevel);
        }

        public void SetPixel(int x, int y, Color color) => SetPixel(x, y, color, 0);
        public void SetPixel(int x, int y, Color color, int mipLevel)
        {
            RequireReadable();
            if (!ValidMip(mipLevel)) { Debug.LogError("Texture mip level is out of range."); return; }
            int w = MipWidth(mipLevel), h = MipHeight(mipLevel);
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            Write(mipLevel, y * w + x, color);
            m_PixelsDirty = true;
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
            RequireReadable(true);
            CheckMip(miplevel);
            return GetPixels(0, 0, MipWidth(miplevel), MipHeight(miplevel), miplevel);
        }
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight) => GetPixels(x, y, blockWidth, blockHeight, 0);
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight, int miplevel)
        {
            RequireReadable(true);
            CheckBlock(x, y, blockWidth, blockHeight, miplevel);
            var result = new Color[blockWidth * blockHeight];
            int width = MipWidth(miplevel);
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++) result[j * blockWidth + i] = Read(miplevel, (y + j) * width + x + i);
            return result;
        }

        public Color32[] GetPixels32() => GetPixels32(0);
        public Color32[] GetPixels32(int miplevel)
        {
            RequireReadable(true);
            CheckMip(miplevel);
            var result = new Color32[MipWidth(miplevel) * MipHeight(miplevel)];
            for (int i = 0; i < result.Length; i++) result[i] = Read(miplevel, i);
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
            m_PixelsDirty = true;
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
            m_PixelsDirty = true;
        }

        int RawMipSize(int mip) => PixelFormats.Size(m_Format, MipWidth(mip), MipHeight(mip));
        int RawChainSize()
        {
            int bytes = 0;
            for (int mip = 0; mip < m_MipCount; mip++) bytes = checked(bytes + RawMipSize(mip));
            return bytes;
        }

        void DecodeLevel(byte[] data, int mip)
        {
            if (m_Hdr != null)
            {
                var pixels = FloatLevel(mip);
                PixelFormats.DecodeFloat(m_Format, data, pixels);
                for (int i = 0; i < pixels.Length; i++)
                    for (int k = FloatChannels; k < 4; k++) pixels[i][k] = 1f;
            }
            else PixelFormats.Decode(m_Format, data, ByteLevel(mip));
        }

        void DecodeMipChain(byte[] data)
        {
            if (GraphicsFormatUtility.IsHDRFormat(m_Format))
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
                DecodeLevel(new ReadOnlySpan<byte>(data, at, size).ToArray(), mip);
                at += size;
            }
        }

        public void LoadRawTextureData(byte[] data)
        {
            RequireReadable();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < RawChainSize()) throw new UnityException("LoadRawTextureData: not enough data provided.");
            DecodeMipChain(data);
            m_PixelsDirty = true;
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
            DecodeLevel(bytes.Slice(0, size).ToArray(), mipLevel);
            m_PixelsDirty = true;
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
            var data = new byte[RawChainSize()];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                var bytes = m_Hdr != null ? PixelFormats.EncodeFloat(m_Format, FloatLevel(mip)) : PixelFormats.Encode(m_Format, ByteLevel(mip));
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
                            var src = ByteLevel(mip - 1);
                            var a = src[Math.Clamp(iy, 0, sh - 1) * sw + Math.Clamp(ix, 0, sw - 1)];
                            var b = src[Math.Clamp(iy, 0, sh - 1) * sw + Math.Clamp(ix + 1, 0, sw - 1)];
                            var d = src[Math.Clamp(iy + 1, 0, sh - 1) * sw + Math.Clamp(ix, 0, sw - 1)];
                            var e = src[Math.Clamp(iy + 1, 0, sh - 1) * sw + Math.Clamp(ix + 1, 0, sw - 1)];
                            byte Filter(byte av, byte bv, byte dv, byte ev)
                            {
                                int top = (int)Math.Floor(av + (bv - av) * fx);
                                int bottom = (int)Math.Floor(dv + (ev - dv) * fx);
                                return (byte)Math.Clamp((int)Math.Floor(top + (bottom - top) * fy), 0, 255);
                            }
                            ByteLevel(mip)[y * w + x] = new Color32(Filter(a.r, b.r, d.r, e.r), Filter(a.g, b.g, d.g, e.g),
                                Filter(a.b, b.b, d.b, e.b), Filter(a.a, b.a, d.a, e.a));
                        }
                    }
            }
        }

        Color32[] UploadByteChain()
        {
            if (m_MipCount == 1) return m_Pixels;
            int count = 0;
            for (int mip = 0; mip < m_MipCount; mip++) count += ByteLevel(mip).Length;
            var result = new Color32[count];
            int at = 0;
            for (int mip = 0; mip < m_MipCount; mip++)
            {
                var level = ByteLevel(mip);
                level.CopyTo(result, at);
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
                foreach (var pixel in FloatLevel(mip))
                {
                    var c = pixel;
                    // CPU reads fill missing channels with one; the GPU fills RGB with zero.
                    for (int k = FloatChannels; k < 3; k++) c[k] = 0f;
                    result[at++] = c;
                }
            return result;
        }
    }
}
