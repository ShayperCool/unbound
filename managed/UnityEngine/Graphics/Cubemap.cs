using System;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A cube texture: six square faces, each with its mip chain. Script-made cubemaps
    /// keep their pixels (RGBA, floats for HDR formats) and upload on Apply; built-in ones load
    /// straight to the renderer.</summary>
    public sealed partial class Cubemap : Texture
    {
        TextureFormat m_Format = TextureFormat.RGBA32;
        int m_MipCount = 1;
        bool m_Readable = true;
        bool m_Linear;
        /// <summary>Per face, per mip level: the pixels (readable cubemaps).</summary>
        Color[][][] m_Pixels;
        /// <summary>The data is RGBM encoded (a reflection cubemap).</summary>
        internal bool m_Rgbm;

        public Cubemap(int width, TextureFormat textureFormat, bool mipChain) : this(width, textureFormat, mipChain ? -1 : 1, false) { }
        public Cubemap(int width, TextureFormat textureFormat, bool mipChain, bool createUninitialized) : this(width, textureFormat, mipChain ? -1 : 1, createUninitialized) { }
        public Cubemap(int width, TextureFormat format, int mipCount) : this(width, format, mipCount, false) { }

        public Cubemap(int width, TextureFormat format, int mipCount, bool createUninitialized)
        {
            if (width <= 0) throw new ArgumentException("Cubemap width must be positive.", nameof(width));
            m_Width = m_Height = width;
            m_Format = format;
            m_MipCount = mipCount < 0 ? FullMipCount(width) : Math.Max(1, Math.Min(mipCount, FullMipCount(width)));
            Allocate();
        }

        public Cubemap(int width, GraphicsFormat format, TextureCreationFlags flags) : this(width, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public Cubemap(int width, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, GraphicsFormatUtility.GetTextureFormat(format), mipCount, false) =>
            m_Linear = !GraphicsFormatUtility.IsSRGBFormat(format);

        public Cubemap(int width, DefaultFormat format, TextureCreationFlags flags) : this(width, SystemInfo.GetGraphicsFormat(format), flags) { }
        public Cubemap(int width, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        Cubemap() { }

        static int FullMipCount(int width) => 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, width));

        void Allocate()
        {
            m_Pixels = new Color[6][][];
            for (int f = 0; f < 6; f++)
            {
                m_Pixels[f] = new Color[m_MipCount][];
                for (int m = 0; m < m_MipCount; m++)
                {
                    int s = Math.Max(1, m_Width >> m);
                    m_Pixels[f][m] = new Color[s * s];
                }
            }
        }

        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        public override int mipmapCount => m_MipCount;
        public override TextureDimension dimension { get => TextureDimension.Cube; set => throw new NotImplementedException(); }
        internal bool isPreProcessed => false;
        public bool streamingMipmaps => false;
        public int streamingMipmapsPriority => 0;
        public int requestedMipmapLevel { get; set; }
        internal bool loadAllMips { get; set; }
        public int desiredMipmapLevel => 0;
        public int loadingMipmapLevel => 0;
        public int loadedMipmapLevel => 0;

        public void ClearRequestedMipmapLevel() => requestedMipmapLevel = 0;
        public bool IsRequestedMipmapLevelLoaded() => true;

        Color[] Level(CubemapFace face, int mip)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            if (face < CubemapFace.PositiveX || face > CubemapFace.NegativeZ) throw new ArgumentException("Invalid cubemap face.", nameof(face));
            if (mip < 0 || mip >= m_MipCount) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels[(int)face][mip];
        }

        public void SetPixel(CubemapFace face, int x, int y, Color color) => SetPixel(face, x, y, color, 0);

        public void SetPixel(CubemapFace face, int x, int y, Color color, int mip)
        {
            var level = Level(face, mip);
            int s = Math.Max(1, m_Width >> mip);
            if (x < 0 || y < 0 || x >= s || y >= s) return;
            level[y * s + x] = color;
        }

        public Color GetPixel(CubemapFace face, int x, int y) => GetPixel(face, x, y, 0);

        public Color GetPixel(CubemapFace face, int x, int y, int mip)
        {
            var level = Level(face, mip);
            int s = Math.Max(1, m_Width >> mip);
            x = Mathf.Clamp(x, 0, s - 1);
            y = Mathf.Clamp(y, 0, s - 1);
            return level[y * s + x];
        }

        public Color[] GetPixels(CubemapFace face) => GetPixels(face, 0);
        public Color[] GetPixels(CubemapFace face, int miplevel) => (Color[])Level(face, miplevel).Clone();

        public void SetPixels(Color[] colors, CubemapFace face) => SetPixels(colors, face, 0);

        public void SetPixels(Color[] colors, CubemapFace face, int miplevel)
        {
            var level = Level(face, miplevel);
            if (colors == null || colors.Length != level.Length) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            Array.Copy(colors, level, level.Length);
        }

        public void SmoothEdges() { }
        public void SmoothEdges(int smoothRegionWidthInPixels) { }

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable) throw new UnityException($"Texture '{name}' is not readable.");
            if (updateMipmaps)
                for (int f = 0; f < 6; f++)
                    for (int m = 1; m < m_MipCount; m++)
                        Downsample(m_Pixels[f][m - 1], Math.Max(1, m_Width >> (m - 1)), m_Pixels[f][m]);
            Upload();
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                m_Pixels = null;
            }
        }

        static void Downsample(Color[] source, int size, Color[] into)
        {
            int half = Math.Max(1, size / 2);
            for (int y = 0; y < half; y++)
                for (int x = 0; x < half; x++)
                {
                    int x0 = Math.Min(2 * x, size - 1), y0 = Math.Min(2 * y, size - 1);
                    int x1 = Math.Min(x0 + 1, size - 1), y1 = Math.Min(y0 + 1, size - 1);
                    into[y * half + x] = (source[y0 * size + x0] + source[y0 * size + x1] + source[y1 * size + x0] + source[y1 * size + x1]) * 0.25f;
                }
        }

        bool IsHdr => m_Format is TextureFormat.RGBAHalf or TextureFormat.RGBAFloat or TextureFormat.RGB9e5Float or TextureFormat.BC6H
            or TextureFormat.RHalf or TextureFormat.RGHalf or TextureFormat.RFloat or TextureFormat.RGFloat;

        unsafe void Upload()
        {
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            bool hdr = IsHdr;
            int pixels = 0;
            for (int m = 0; m < m_MipCount; m++) { int s = Math.Max(1, m_Width >> m); pixels += s * s; }
            var data = new byte[pixels * 6 * (hdr ? 16 : 4)];
            int at = 0;
            for (int f = 0; f < 6; f++)
                for (int m = 0; m < m_MipCount; m++)
                    foreach (var c in m_Pixels[f][m])
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
                    Width = m_Width,
                    Height = m_Height,
                    Format = hdr ? 2 : m_Linear ? 1 : 0,
                    MipCount = m_MipCount,
                    Data = p,
                    DataLength = data.Length,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    AnisoLevel = anisoLevel,
                    Layers = 6,
                    Dimension = (int)TextureDimension.Cube,
                };
                Native.UploadTexture(m_NativeHandle, &packet);
            }
        }

        /// <summary>A cubemap the renderer imported from an image under <paramref name="handle"/>
        /// (a texture asset of the Cube shape): its pixels stay on the GPU.</summary>
        internal static Cubemap FromImport(ulong handle, string name, in ImportedTextureInfo info)
        {
            Cubemap cube;
            using (ObjectRegistry.AsAssets())
                cube = new Cubemap { IsPersistentAsset = true, m_Readable = false };
            cube.m_NativeHandle = handle;
            cube.m_Name = name;
            cube.m_Width = cube.m_Height = info.Width;
            cube.m_MipCount = Math.Max(1, info.MipCount);
            cube.m_Linear = info.Srgb == 0 || info.Format == 2;
            cube.m_Format = info.Format == 2 ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
            cube.filterMode = (FilterMode)info.FilterMode;
            cube.wrapModeU = (TextureWrapMode)info.WrapU;
            cube.wrapModeV = (TextureWrapMode)info.WrapV;
            cube.anisoLevel = info.AnisoLevel;
            return cube;
        }

        /// <summary>A cubemap of a serialized file (the built-in default reflection), uploaded by
        /// the renderer from the file; null when it cannot be read.</summary>
        internal static Cubemap FromSerialized(string guid, long fileId)
        {
            Cubemap cube;
            using (ObjectRegistry.AsAssets())
                cube = new Cubemap { IsPersistentAsset = true, m_Readable = false, m_Linear = true };
            cube.m_NativeHandle = RenderHandles.Next();
            try
            {
                var json = Native.Call("load-texture-object", JsonSerializer.Serialize(new { guid, fileId, handle = cube.m_NativeHandle }));
                var root = JsonDocument.Parse(json).RootElement;
                cube.m_Name = root.GetProperty("name").GetString() ?? "";
                cube.m_Width = root.GetProperty("width").GetInt32();
                cube.m_Height = root.GetProperty("height").GetInt32();
                cube.m_MipCount = root.GetProperty("mipCount").GetInt32();
                cube.m_Rgbm = root.GetProperty("rgbm").GetBoolean();
                return cube;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: cubemap {fileId} of {guid} could not be loaded: {e.Message}");
                return null;
            }
        }

        internal override void OnEngineDestroy()
        {
            Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }
}
