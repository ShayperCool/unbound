using System;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>Pixels of a texture with layers (an array's slices, a volume's depth) and mips, kept
    /// as colors and uploaded on Apply: RGBA bytes, or floats for HDR formats.</summary>
    internal sealed class LayeredPixels
    {
        internal readonly int Width, Height, Layers, Mips;
        internal readonly bool Volume;
        /// <summary>Arrays: [layer][mip], each a level's pixels. Volumes: [0][mip], each a level's
        /// slices one after another (the slices halve with the level too).</summary>
        internal readonly Color[][][] Levels;

        internal LayeredPixels(int width, int height, int layers, int mips, bool volume)
        {
            Width = width;
            Height = height;
            Layers = layers;
            Mips = mips;
            Volume = volume;
            int images = volume ? 1 : layers;
            Levels = new Color[images][][];
            for (int l = 0; l < images; l++)
            {
                Levels[l] = new Color[mips][];
                for (int m = 0; m < mips; m++) Levels[l][m] = new Color[Size(m)];
            }
        }

        internal int W(int mip) => Math.Max(1, Width >> mip);
        internal int H(int mip) => Math.Max(1, Height >> mip);
        internal int D(int mip) => Volume ? Math.Max(1, Layers >> mip) : 1;
        internal int Size(int mip) => W(mip) * H(mip) * D(mip);

        internal static int FullMipCount(int width, int height, int depth) =>
            32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, Math.Max(width, Math.Max(height, depth))));

        /// <summary>Each mip a box filter of the one above (in x, y, and z for a volume).</summary>
        internal void BuildMips()
        {
            foreach (var chain in Levels)
                for (int m = 1; m < Mips; m++)
                {
                    var src = chain[m - 1];
                    var dst = chain[m];
                    int sw = W(m - 1), sh = H(m - 1), sd = D(m - 1);
                    int w = W(m), h = H(m), d = D(m);
                    for (int z = 0; z < d; z++)
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                var sum = new Color(0, 0, 0, 0);
                                int n = 0;
                                for (int dz = 0; dz < (Volume && sd > 1 ? 2 : 1); dz++)
                                    for (int dy = 0; dy < (sh > 1 ? 2 : 1); dy++)
                                        for (int dx = 0; dx < (sw > 1 ? 2 : 1); dx++)
                                        {
                                            int sx = Math.Min(2 * x + dx, sw - 1), sy = Math.Min(2 * y + dy, sh - 1), sz = Math.Min(2 * z + dz, sd - 1);
                                            sum += src[(sz * sh + sy) * sw + sx];
                                            n++;
                                        }
                                dst[(z * h + y) * w + x] = sum / n;
                            }
                }
        }

        internal unsafe void Upload(ulong handle, bool hdr, bool linear, FilterMode filter, TextureWrapMode wrapU, TextureWrapMode wrapV, int aniso)
        {
            int count = 0;
            foreach (var chain in Levels) foreach (var level in chain) count += level.Length;
            var data = new byte[count * (hdr ? 16 : 4)];
            int at = 0;
            foreach (var chain in Levels)
                foreach (var level in chain)
                    foreach (var c in level)
                    {
                        if (hdr)
                        {
                            BitConverter.TryWriteBytes(new Span<byte>(data, at, 4), c.r);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 4, 4), c.g);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 8, 4), c.b);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 12, 4), c.a);
                            at += 16;
                        }
                        else
                        {
                            Color32 b = c;
                            data[at] = b.r; data[at + 1] = b.g; data[at + 2] = b.b; data[at + 3] = b.a;
                            at += 4;
                        }
                    }
            fixed (byte* p = data)
            {
                var packet = new TexturePacket
                {
                    Width = Width,
                    Height = Height,
                    Format = hdr ? 2 : linear ? 1 : 0,
                    MipCount = Mips,
                    Data = p,
                    DataLength = data.Length,
                    FilterMode = (int)filter,
                    WrapU = (int)wrapU,
                    WrapV = (int)wrapV,
                    AnisoLevel = aniso,
                    Layers = Layers,
                    Dimension = (int)(Volume ? TextureDimension.Tex3D : TextureDimension.Tex2DArray),
                };
                Native.UploadTexture(handle, &packet);
            }
        }
    }

    /// <summary>An array of 2D textures of one size and format, sampled by slice.</summary>
    public sealed partial class Texture2DArray : Texture
    {
        TextureFormat m_Format;
        bool m_Linear;
        bool m_Readable = true;
        LayeredPixels m_Pixels;
        int m_Depth;

        public static int allSlices => -1;

        public Texture2DArray(int width, int height, int depth, TextureFormat textureFormat, bool mipChain) : this(width, height, depth, textureFormat, mipChain ? -1 : 1, false) { }
        public Texture2DArray(int width, int height, int depth, TextureFormat textureFormat, bool mipChain, bool linear) : this(width, height, depth, textureFormat, mipChain ? -1 : 1, linear) { }
        public Texture2DArray(int width, int height, int depth, TextureFormat textureFormat, bool mipChain, bool linear, bool createUninitialized) : this(width, height, depth, textureFormat, mipChain ? -1 : 1, linear) { }
        public Texture2DArray(int width, int height, int depth, TextureFormat textureFormat, int mipCount, bool linear, bool createUninitialized) : this(width, height, depth, textureFormat, mipCount, linear) { }

        public Texture2DArray(int width, int height, int depth, TextureFormat textureFormat, int mipCount, bool linear)
        {
            if (width <= 0 || height <= 0 || depth <= 0)
                throw new UnityException($"Failed to create 2D array texture because of invalid parameters. Width {width}, height {height}, depth {depth}.");
            m_Width = width;
            m_Height = height;
            m_Depth = depth;
            m_Format = textureFormat;
            m_Linear = linear;
            int full = LayeredPixels.FullMipCount(width, height, 1);
            m_Pixels = new LayeredPixels(width, height, depth, mipCount < 0 ? full : Math.Max(1, Math.Min(mipCount, full)), false);
        }

        public Texture2DArray(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags) : this(width, height, depth, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public Texture2DArray(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, height, depth, GraphicsFormatUtility.GetTextureFormat(format), mipCount, !GraphicsFormatUtility.IsSRGBFormat(format)) { }

        public Texture2DArray(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags) { }
        public Texture2DArray(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        public int depth => m_Depth;
        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_Pixels?.Mips ?? 1;
        public override TextureDimension dimension { get => TextureDimension.Tex2DArray; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear);

        Color[] Level(int element, int mip)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            if (element < 0 || element >= m_Depth) throw new ArgumentException($"Invalid array element {element}.", nameof(element));
            if (mip < 0 || mip >= m_Pixels.Mips) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels.Levels[element][mip];
        }

        public Color[] GetPixels(int arrayElement, int miplevel) => (Color[])Level(arrayElement, miplevel).Clone();
        public Color[] GetPixels(int arrayElement) => GetPixels(arrayElement, 0);

        public Color32[] GetPixels32(int arrayElement, int miplevel) => Array.ConvertAll(Level(arrayElement, miplevel), c => (Color32)c);
        public Color32[] GetPixels32(int arrayElement) => GetPixels32(arrayElement, 0);

        public void SetPixels(Color[] colors, int arrayElement, int miplevel)
        {
            var level = Level(arrayElement, miplevel);
            if (colors == null || colors.Length != level.Length) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            Array.Copy(colors, level, level.Length);
        }

        public void SetPixels(Color[] colors, int arrayElement) => SetPixels(colors, arrayElement, 0);

        public void SetPixels32(Color32[] colors, int arrayElement, int miplevel) =>
            SetPixels(colors != null ? Array.ConvertAll(colors, c => (Color)c) : null, arrayElement, miplevel);

        public void SetPixels32(Color32[] colors, int arrayElement) => SetPixels32(colors, arrayElement, 0);

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable.");
            if (updateMipmaps) m_Pixels.BuildMips();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            m_Pixels.Upload(m_NativeHandle, GraphicsFormatUtility.IsHDRFormat(m_Format), m_Linear, filterMode, wrapModeU, wrapModeV, anisoLevel);
            updateCount++;
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                m_Pixels = null;
            }
        }

        internal override void OnEngineDestroy()
        {
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }

    /// <summary>A 3D texture: width × height × depth texels, sampled in three coordinates.</summary>
    public sealed partial class Texture3D : Texture
    {
        TextureFormat m_Format;
        bool m_Linear = true;
        bool m_Readable = true;
        LayeredPixels m_Pixels;
        int m_Depth;

        public Texture3D(int width, int height, int depth, TextureFormat textureFormat, bool mipChain) : this(width, height, depth, textureFormat, mipChain ? -1 : 1) { }
        public Texture3D(int width, int height, int depth, TextureFormat textureFormat, bool mipChain, bool createUninitialized) : this(width, height, depth, textureFormat, mipChain ? -1 : 1) { }

        public Texture3D(int width, int height, int depth, TextureFormat textureFormat, int mipCount)
        {
            if (width <= 0 || height <= 0 || depth <= 0)
                throw new UnityException($"Failed to create 3D texture because of invalid parameters. Width {width}, height {height}, depth {depth}.");
            m_Width = width;
            m_Height = height;
            m_Depth = depth;
            m_Format = textureFormat;
            int full = LayeredPixels.FullMipCount(width, height, depth);
            m_Pixels = new LayeredPixels(width, height, depth, mipCount < 0 ? full : Math.Max(1, Math.Min(mipCount, full)), true);
        }

        public Texture3D(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags) : this(width, height, depth, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public Texture3D(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, height, depth, GraphicsFormatUtility.GetTextureFormat(format), mipCount) => m_Linear = !GraphicsFormatUtility.IsSRGBFormat(format);

        public Texture3D(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags) { }
        public Texture3D(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        public int depth => m_Depth;
        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_Pixels?.Mips ?? 1;
        public override TextureDimension dimension { get => TextureDimension.Tex3D; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear);

        Color[] Level(int mip)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            if (mip < 0 || mip >= m_Pixels.Mips) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels.Levels[0][mip];
        }

        public Color[] GetPixels(int miplevel) => (Color[])Level(miplevel).Clone();
        public Color[] GetPixels() => GetPixels(0);
        public Color32[] GetPixels32(int miplevel) => Array.ConvertAll(Level(miplevel), c => (Color32)c);
        public Color32[] GetPixels32() => GetPixels32(0);

        public void SetPixels(Color[] colors, int miplevel)
        {
            var level = Level(miplevel);
            if (colors == null || colors.Length != level.Length) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            Array.Copy(colors, level, level.Length);
        }

        public void SetPixels(Color[] colors) => SetPixels(colors, 0);
        public void SetPixels32(Color32[] colors, int miplevel) => SetPixels(colors != null ? Array.ConvertAll(colors, c => (Color)c) : null, miplevel);
        public void SetPixels32(Color32[] colors) => SetPixels32(colors, 0);

        public void SetPixel(int x, int y, int z, Color color) => SetPixel(x, y, z, color, 0);

        public void SetPixel(int x, int y, int z, Color color, int mipLevel)
        {
            var level = Level(mipLevel);
            int w = m_Pixels.W(mipLevel), h = m_Pixels.H(mipLevel), d = m_Pixels.D(mipLevel);
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) return;
            level[(z * h + y) * w + x] = color;
        }

        public Color GetPixel(int x, int y, int z) => GetPixel(x, y, z, 0);

        public Color GetPixel(int x, int y, int z, int mipLevel)
        {
            var level = Level(mipLevel);
            int w = m_Pixels.W(mipLevel), h = m_Pixels.H(mipLevel), d = m_Pixels.D(mipLevel);
            x = Mathf.Clamp(x, 0, w - 1);
            y = Mathf.Clamp(y, 0, h - 1);
            z = Mathf.Clamp(z, 0, d - 1);
            return level[(z * h + y) * w + x];
        }

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable.");
            if (updateMipmaps) m_Pixels.BuildMips();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            m_Pixels.Upload(m_NativeHandle, GraphicsFormatUtility.IsHDRFormat(m_Format), m_Linear, filterMode, wrapModeU, wrapModeV, anisoLevel);
            updateCount++;
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                m_Pixels = null;
            }
        }

        internal override void OnEngineDestroy()
        {
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }
}
