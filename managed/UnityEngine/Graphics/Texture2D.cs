using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    /// <summary>A 2D texture: imported from an asset (pixels on the native side), or made by code
    /// (pixels kept here and uploaded on Apply).</summary>
    [ClassId(28)]
    public sealed partial class Texture2D : Texture, ISerializedAsset
    {
        internal TextureFormat m_Format = TextureFormat.RGBA32;
        internal int m_MipCount = 1;
        /// <summary>Mip levels of the renderer's copy (pixel uploads send one; imports their own).</summary>
        int m_GpuMipCount = 1;
        internal bool m_IsReadable;
        internal bool m_Linear;
        /// <summary>CPU pixels of a texture made by code, bottom row first.</summary>
        internal Color32[] m_Pixels;
        /// <summary>The CPU pixels instead of <see cref="m_Pixels"/> for floating point formats
        /// (RGBAHalf, RFloat, ...): their values are not bytes.</summary>
        internal Color[] m_Hdr;
        /// <summary>A serialized texture's mip 0 in its own format (Alpha8: a byte a pixel), kept
        /// instead of <see cref="m_Pixels"/> until a script reads or writes pixels.</summary>
        byte[] m_Raw;
        internal long RawBytes => m_Raw?.LongLength ?? 0;
        internal bool m_PixelsDirty;
        /// <summary>The asset GUID of an imported texture.</summary>
        internal string m_AssetGuid;

        Texture2D() { }

        public Texture2D(int width, int height) : this(width, height, TextureFormat.RGBA32, true, false) { }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) : this(width, height, textureFormat, mipChain, false) { }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear)
        {
            Init(width, height, textureFormat, mipChain ? -1 : 1, linear);
        }

        public Texture2D(int width, int height, TextureFormat textureFormat, int mipCount, bool linear)
        {
            Init(width, height, textureFormat, mipCount, linear);
        }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear, bool createUninitialized)
        {
            Init(width, height, textureFormat, mipChain ? -1 : 1, linear);
        }

        public Texture2D(int width, int height, GraphicsFormat format, TextureCreationFlags flags)
            : this(width, height, format, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1, flags) { }

        public Texture2D(int width, int height, GraphicsFormat format, int mipCount, TextureCreationFlags flags)
        {
            Init(width, height, TextureFormatOf(format), mipCount, !GraphicsFormatUtility.IsSRGBFormat(format));
        }

        public Texture2D(int width, int height, DefaultFormat format, TextureCreationFlags flags)
            : this(width, height, SystemInfo.GetGraphicsFormat(format), flags) { }

        /// <summary>The texture format holding a graphics format's pixels (RGBA32 for formats with
        /// no texture format).</summary>
        static TextureFormat TextureFormatOf(GraphicsFormat format)
        {
            var t = GraphicsFormatUtility.GetTextureFormat(format);
            return t == 0 ? TextureFormat.RGBA32 : t;
        }

        void Init(int width, int height, TextureFormat format, int mipCount, bool linear)
        {
            if (width <= 0 || height <= 0)
                throw new UnityException($"Failed to create texture because of invalid parameters. Width {width}, height {height}.");
            m_Width = width;
            m_Height = height;
            m_Format = format;
            m_MipCount = mipCount < 0 ? 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2)) : Math.Max(1, mipCount);
            m_Linear = linear;
            m_IsReadable = true;
            m_Raw = null;
            // A new texture is grey (205,205,205,205) until written, as in Unity.
            if (GraphicsFormatUtility.IsHDRFormat(format))
            {
                m_Pixels = null;
                m_Hdr = new Color[width * height];
                var grey = new Color(205f / 255f, 205f / 255f, 205f / 255f, 205f / 255f);
                for (int i = 0; i < m_Hdr.Length; i++) m_Hdr[i] = grey;
            }
            else
            {
                m_Hdr = null;
                m_Pixels = new Color32[width * height];
                for (int i = 0; i < m_Pixels.Length; i++) m_Pixels[i] = new Color32(205, 205, 205, 205);
            }
            m_PixelsDirty = true;
        }

        public TextureFormat format => m_SourceFormat ?? m_Format;

        /// <summary>A serialized texture's own format and mip count when Unbound keeps its pixels in
        /// another (an AssetBundle's block-compressed texture, decoded).</summary>
        TextureFormat? m_SourceFormat;
        int m_SourceMips;
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear);
        public override bool isReadable => m_IsReadable;
        internal override int MipmapCount() => m_SourceMips > 0 ? m_SourceMips : m_MipCount;
        public bool alphaIsTransparency { get; set; }
        public bool isPreProcessed => false;
        public bool streamingMipmaps { get { Stubs.Ignored("Texture2D.streamingMipmaps", "is not read from the import settings: texture streaming is not simulated"); return false; } }
        public int streamingMipmapsPriority { get { Stubs.Ignored("Texture2D.streamingMipmapsPriority", "is not read from the import settings: texture streaming is not simulated"); return 0; } }
        public int requestedMipmapLevel { get; set; }
        public int minimumMipmapLevel { get; set; }
        [Unbound.EditorFaithful("virtual texturing is an HDRP-only, opt-in player setting: no texture of a built-in or URP project is VT-only")]
        public bool vtOnly => false;
        public int desiredMipmapLevel { get { Stubs.Ignored("Texture2D.desiredMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }
        public int loadingMipmapLevel { get { Stubs.Ignored("Texture2D.loadingMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }
        public int loadedMipmapLevel { get { Stubs.Ignored("Texture2D.loadedMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }
        public bool ignoreMipmapLimit { get; set; }

        public static Texture2D whiteTexture => DefaultTextures.White;
        public static Texture2D blackTexture => DefaultTextures.Black;
        public static Texture2D redTexture => DefaultTextures.Red;
        public static Texture2D grayTexture => DefaultTextures.Gray;
        public static Texture2D linearGrayTexture => DefaultTextures.Gray;
        public static Texture2D normalTexture => DefaultTextures.Normal;

        /// <summary>The pixels as colors, decoded from the raw data when only that is kept.</summary>
        internal Color32[] EnsurePixels()
        {
            if (m_Pixels == null && m_Raw != null)
            {
                m_Pixels = new Color32[m_Width * m_Height];
                PixelFormats.Decode(m_Format, m_Raw, m_Pixels);
                m_Raw = null;
            }
            return m_Pixels;
        }

        void RequireReadable()
        {
            EnsurePixels();
            if (m_Pixels == null && m_Hdr == null)
            {
                if (m_IsReadable && m_AssetGuid != null) FetchImportedPixels();
                if (m_Pixels == null)
                    throw new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            }
        }

        /// <summary>A texture of a serialized file (a built-in one), uploaded by the renderer from
        /// the file; null when it cannot be read.</summary>
        internal static Texture2D FromSerialized(string guid, long fileId)
        {
            Texture2D texture;
            using (ObjectRegistry.AsAssets())
                texture = new Texture2D(1, 1) { IsPersistentAsset = true };
            texture.m_Pixels = null;
            texture.m_IsReadable = false;
            texture.m_PixelsDirty = false;
            texture.m_NativeHandle = RenderHandles.Next();
            try
            {
                var json = Native.Call("load-texture-object", JsonSerializer.Serialize(new { guid, fileId, handle = texture.m_NativeHandle }));
                var root = JsonDocument.Parse(json).RootElement;
                texture.m_Name = root.GetProperty("name").GetString() ?? "";
                texture.m_Width = root.GetProperty("width").GetInt32();
                texture.m_Height = root.GetProperty("height").GetInt32();
                texture.m_MipCount = Math.Max(1, root.GetProperty("mipCount").GetInt32());
                texture.m_Linear = root.TryGetProperty("linear", out var linear) && linear.GetBoolean();
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: texture {fileId} of {guid} could not be loaded: {e.Message}");
                return null;
            }
        }

        /// <summary>The pixels for the engine's own use (a terrain's detail atlas), readable or not.</summary>
        internal Color32[] EnginePixels32()
        {
            EnsurePixels();
            if (m_Pixels == null && m_Hdr == null && m_AssetGuid != null) FetchImportedPixels();
            if (m_Pixels != null) return (Color32[])m_Pixels.Clone();
            if (m_Hdr != null)
            {
                var result = new Color32[m_Hdr.Length];
                for (int i = 0; i < result.Length; i++) result[i] = m_Hdr[i];
                return result;
            }
            return new Color32[m_Width * m_Height];
        }

        void FetchImportedPixels()
        {
            try
            {
                var json = Native.Call("texture-pixels", JsonSerializer.Serialize(new { guid = m_AssetGuid }));
                var bytes = Convert.FromBase64String(JsonDocument.Parse(json).RootElement.GetProperty("rgba").GetString());
                m_Pixels = new Color32[m_Width * m_Height];
                for (int i = 0; i < m_Pixels.Length && i * 4 + 3 < bytes.Length; i++)
                    m_Pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading the pixels of {name}: {e.Message}");
            }
        }

        public Color GetPixel(int x, int y)
        {
            RequireReadable();
            x = Wrap(x, m_Width, wrapModeU);
            y = Wrap(y, m_Height, wrapModeV);
            return Read(y * m_Width + x);
        }

        Color Read(int i) => m_Hdr != null ? m_Hdr[i] : (Color)m_Pixels[i];

        void Write(int i, Color c)
        {
            if (m_Hdr != null) m_Hdr[i] = c;
            else m_Pixels[i] = c;
        }

        public Color GetPixel(int x, int y, int mipLevel) => GetPixel(x, y);

        static int Wrap(int v, int size, TextureWrapMode mode)
        {
            if (mode == TextureWrapMode.Repeat) return ((v % size) + size) % size;
            return Math.Max(0, Math.Min(size - 1, v));
        }

        public Color GetPixelBilinear(float u, float v)
        {
            RequireReadable();
            float x = u * m_Width - 0.5f, y = v * m_Height - 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            Color c00 = GetPixel(x0, y0), c10 = GetPixel(x0 + 1, y0), c01 = GetPixel(x0, y0 + 1), c11 = GetPixel(x0 + 1, y0 + 1);
            return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
        }

        public Color GetPixelBilinear(float u, float v, int mipLevel) => GetPixelBilinear(u, v);

        public void SetPixel(int x, int y, Color color)
        {
            RequireReadable();
            if (x < 0 || y < 0 || x >= m_Width || y >= m_Height) return;
            Write(y * m_Width + x, color);
            m_PixelsDirty = true;
        }

        public void SetPixel(int x, int y, Color color, int mipLevel) => SetPixel(x, y, color);

        public Color[] GetPixels() => GetPixels(0, 0, m_Width, m_Height);

        public Color[] GetPixels(int miplevel) => GetPixels();

        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight, int miplevel) => GetPixels(x, y, blockWidth, blockHeight);

        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight)
        {
            RequireReadable();
            var result = new Color[blockWidth * blockHeight];
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++)
                    result[j * blockWidth + i] = Read((y + j) * m_Width + x + i);
            return result;
        }

        public Color32[] GetPixels32() => GetPixels32(0);

        public Color32[] GetPixels32(int miplevel)
        {
            RequireReadable();
            if (m_Hdr != null)
            {
                var converted = new Color32[m_Hdr.Length];
                for (int i = 0; i < converted.Length; i++) converted[i] = m_Hdr[i];
                return converted;
            }
            return (Color32[])m_Pixels.Clone();
        }

        public void SetPixels(Color[] colors) => SetPixels(0, 0, m_Width, m_Height, colors);

        public void SetPixels(Color[] colors, int miplevel) => SetPixels(colors);

        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors, int miplevel) => SetPixels(x, y, blockWidth, blockHeight, colors);

        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors)
        {
            RequireReadable();
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++)
                    Write((y + j) * m_Width + x + i, colors[j * blockWidth + i]);
            m_PixelsDirty = true;
        }

        public void SetPixels32(Color32[] colors) => SetPixels32(colors, 0);

        public void SetPixels32(Color32[] colors, int miplevel)
        {
            RequireReadable();
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (m_Hdr != null)
                for (int i = 0; i < Math.Min(colors.Length, m_Hdr.Length); i++) m_Hdr[i] = colors[i];
            else Array.Copy(colors, m_Pixels, Math.Min(colors.Length, m_Pixels.Length));
            m_PixelsDirty = true;
        }

        public void SetPixels32(int x, int y, int blockWidth, int blockHeight, Color32[] colors) => SetPixels32(x, y, blockWidth, blockHeight, colors, 0);

        public void SetPixels32(int x, int y, int blockWidth, int blockHeight, Color32[] colors, int miplevel)
        {
            RequireReadable();
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++)
                    Write((y + j) * m_Width + x + i, colors[j * blockWidth + i]);
            m_PixelsDirty = true;
        }

        public void LoadRawTextureData(byte[] data)
        {
            RequireReadable();
            if (m_Hdr != null) PixelFormats.DecodeFloat(m_Format, data, m_Hdr);
            else PixelFormats.Decode(m_Format, data, m_Pixels);
            m_PixelsDirty = true;
        }

        public void LoadRawTextureData(IntPtr data, int size)
        {
            var bytes = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
            LoadRawTextureData(bytes);
        }

        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) where T : struct
        {
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data, sourceDataStartIndex, data.Length - sourceDataStartIndex)).ToArray();
            LoadRawTextureData(bytes);
        }

        public byte[] GetRawTextureData()
        {
            RequireReadable();
            return m_Hdr != null ? PixelFormats.EncodeFloat(m_Format, m_Hdr) : PixelFormats.Encode(m_Format, m_Pixels);
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (m_Pixels == null && m_Raw == null && m_Hdr == null) return;
            m_PixelsDirty = true;
            Upload();
            if (makeNoLongerReadable)
            {
                m_Pixels = null;
                m_Hdr = null;
                m_IsReadable = false;
            }
        }

        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply() => Apply(true, false);

        public bool Reinitialize(int width, int height) => Reinitialize(width, height, m_Format, m_MipCount > 1);

        public bool Reinitialize(int width, int height, TextureFormat format, bool hasMipMap)
        {
            Init(width, height, format, hasMipMap ? -1 : 1, m_Linear);
            return true;
        }

        public bool Reinitialize(int width, int height, GraphicsFormat format, bool hasMipMap)
        {
            Init(width, height, TextureFormatOf(format), hasMipMap ? -1 : 1, !GraphicsFormatUtility.IsSRGBFormat(format));
            return true;
        }

        [Obsolete("Texture2D.Resize(int, int) has been deprecated because it actually reinitializes the texture. Use Texture2D.Reinitialize(int, int) instead (UnityUpgradable) -> Reinitialize([*] System.Int32, [*] System.Int32)", false)]
        public bool Resize(int width, int height) => Reinitialize(width, height);

        [Obsolete("Texture2D.Resize(int, int, TextureFormat, bool) has been deprecated because it actually reinitializes the texture. Use Texture2D.Reinitialize(int, int, TextureFormat, bool) instead (UnityUpgradable) -> Reinitialize([*] System.Int32, [*] System.Int32, UnityEngine.TextureFormat, [*] System.Boolean)", false)]
        public bool Resize(int width, int height, TextureFormat format, bool hasMipMap) => Reinitialize(width, height, format, hasMipMap);

        public void Compress(bool highQuality) => Stubs.Ignored("Texture2D.Compress", "is not done: the texture keeps its uncompressed format");

        /// <summary>Copies a rectangle of the active render target (the screen when none) into the
        /// texture's CPU pixels at (destX, destY), after everything drawn so far.</summary>
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps)
        {
            RequireReadable();
            var active = RenderTexture.active;
            int sourceWidth = active != null ? active.width : Screen.width;
            int sourceHeight = active != null ? active.height : Screen.height;
            int x0 = Math.Max(0, (int)source.x), y0 = Math.Max(0, (int)source.y);
            int x1 = Math.Min(sourceWidth, (int)(source.x + source.width)), y1 = Math.Min(sourceHeight, (int)(source.y + source.height));
            // The destination clips too.
            x1 = Math.Min(x1, x0 + m_Width - (destX + x0 - (int)source.x));
            y1 = Math.Min(y1, y0 + m_Height - (destY + y0 - (int)source.y));
            if (x1 <= x0 || y1 <= y0 || destX < 0 || destY < 0)
            {
                Debug.LogError("Trying to read pixel out of bounds");
                return;
            }
            int w = x1 - x0, h = y1 - y0;
            bool hdr = m_Hdr != null;
            var bytes = Native.ReadPixels(active != null ? active.EnsureCreated() : 0, x0, y0, w, h, true);
            if (bytes == null)
            {
                Debug.LogWarning("Unbound: Texture2D.ReadPixels has no GPU to read from (headless run); the texture is left unchanged.");
                return;
            }
            // Linear values (the linear-space screen, float targets) go into an sRGB texture encoded.
            bool linearSource = active == null || GraphicsFormatUtility.IsHDRFormat(active.graphicsFormat);
            bool encode = !hdr && !m_Linear && linearSource && QualitySettings.activeColorSpace == ColorSpace.Linear;
            unsafe
            {
                fixed (byte* p = bytes)
                {
                    var f = (float*)p;
                    for (int row = 0; row < h; row++)
                    {
                        int ty = destY + (y0 - (int)source.y) + row;
                        for (int col = 0; col < w; col++)
                        {
                            int tx = destX + (x0 - (int)source.x) + col;
                            var c = new Color(f[0], f[1], f[2], f[3]);
                            f += 4;
                            if (encode) c = c.gamma;
                            Write(ty * m_Width + tx, c);
                        }
                    }
                }
            }
            m_PixelsDirty = true;
        }

        public void ReadPixels(Rect source, int destX, int destY) => ReadPixels(source, destX, destY, true);

        public Rect[] PackTextures(Texture2D[] textures, int padding, int maximumAtlasSize, bool makeNoLongerReadable) =>
            TexturePacker.Pack(this, textures, padding, maximumAtlasSize, makeNoLongerReadable);

        public Rect[] PackTextures(Texture2D[] textures, int padding, int maximumAtlasSize) => PackTextures(textures, padding, maximumAtlasSize, false);

        public Rect[] PackTextures(Texture2D[] textures, int padding) => PackTextures(textures, padding, 2048, false);

        public static Texture2D CreateExternalTexture(int width, int height, TextureFormat format, bool mipChain, bool linear, IntPtr nativeTex) =>
            new Texture2D(width, height, format, mipChain, linear);

        public void UpdateExternalTexture(IntPtr nativeTex) => Stubs.Ignored("Texture2D.UpdateExternalTexture", "is not supported: native textures are not shared with the renderer");

        internal override void OnSamplerChanged() => m_PixelsDirty |= m_Pixels != null || m_Hdr != null || m_NativeHandle != 0;

        /// <summary>The renderer handle, uploading pending pixels first.</summary>
        internal ulong EnsureUploaded()
        {
            if (m_NativeHandle == 0 && (m_Pixels != null || m_Raw != null || m_Hdr != null)) m_PixelsDirty = true;
            if (m_PixelsDirty) Upload();
            return m_NativeHandle;
        }

        unsafe void Upload()
        {
            if (m_Pixels == null && m_Raw != null)
            {
                if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
                m_PixelsDirty = false;
                updateCount++;
                UploadRaw();
                DropIfNotReadable();
                return;
            }
            if (m_Hdr != null)
            {
                if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
                m_PixelsDirty = false;
                updateCount++;
                UploadHdr();
                DropIfNotReadable();
                return;
            }
            if (m_Pixels == null)
            {
                // Pixels only on the GPU (an imported or no longer readable texture): a sampler
                // change goes on its own (the renderer keeps the texels of a same-shaped texture).
                if (m_PixelsDirty && m_NativeHandle != 0) UploadSampler();
                m_PixelsDirty = false;
                return;
            }
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            m_PixelsDirty = false;
            updateCount++;
            UploadPixels();
            DropIfNotReadable();
        }

        /// <summary>Unity keeps no CPU copy of a texture scripts cannot read: once uploaded, its
        /// pixels live on the GPU only (TextMeshPro adds no glyphs to an atlas that is not
        /// readable, as in Unity).</summary>
        void DropIfNotReadable()
        {
            if (m_IsReadable) return;
            m_Pixels = null;
            m_Hdr = null;
            m_Raw = null;
        }

        /// <summary>Floating point pixels as RGBA floats (the renderer keeps half floats).</summary>
        unsafe void UploadHdr()
        {
            m_GpuMipCount = 1;
            fixed (Color* p = m_Hdr)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = 2,
                    MipCount = 1,
                    Data = (byte*)p,
                    DataLength = m_Hdr.Length * 16,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    AnisoLevel = anisoLevel,
                };
                Native.UploadTexture(m_NativeHandle, &packet);
            }
        }

        /// <summary>Uploads the raw data: Alpha8 and RGBA32 as they are, other formats decoded.</summary>
        unsafe void UploadRaw()
        {
            m_GpuMipCount = 1;
            if (m_Format != TextureFormat.Alpha8 && m_Format != TextureFormat.RGBA32)
            {
                EnsurePixels();
                UploadPixels();
                return;
            }
            fixed (byte* data = m_Raw)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = m_Format == TextureFormat.Alpha8 ? 3 : m_Linear ? 1 : 0,
                    MipCount = 1,
                    Data = data,
                    DataLength = Math.Min(m_Raw.Length, m_Width * m_Height * (m_Format == TextureFormat.Alpha8 ? 1 : 4)),
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    AnisoLevel = anisoLevel,
                };
                Native.UploadTexture(m_NativeHandle, &packet);
            }
        }

        unsafe void UploadSampler()
        {
            var packet = new TexturePacket
            {
                Width = m_Width,
                Height = m_Height,
                // The format the texels went up in: a different one would make a new, empty texture.
                Format = m_Format == TextureFormat.Alpha8 ? 3 : GraphicsFormatUtility.IsHDRFormat(m_Format) ? 2 : m_Linear ? 1 : 0,
                MipCount = m_GpuMipCount,
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                AnisoLevel = anisoLevel,
            };
            Native.UploadTexture(m_NativeHandle, &packet);
        }

        unsafe void UploadPixels()
        {
            m_GpuMipCount = 1;
            if (m_Format == TextureFormat.Alpha8)
            {
                // Alpha-only: the renderer samples it as (0, 0, 0, a), as Unity does (font textures).
                var alpha = new byte[m_Pixels.Length];
                for (int i = 0; i < alpha.Length; i++) alpha[i] = m_Pixels[i].a;

                fixed (byte* a = alpha)
                {
                    var packet = new TexturePacket
                    {
                        Width = m_Width,
                        Height = m_Height,
                        Format = 3,
                        MipCount = 1,
                        Data = a,
                        DataLength = alpha.Length,
                        FilterMode = (int)filterMode,
                        WrapU = (int)wrapModeU,
                        WrapV = (int)wrapModeV,
                        AnisoLevel = anisoLevel,
                    };
                    Native.UploadTexture(m_NativeHandle, &packet);
                }
                return;
            }
            fixed (Color32* p = m_Pixels)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = m_Linear ? 1 : 0,
                    MipCount = 1,
                    Data = (byte*)p,
                    DataLength = m_Pixels.Length * 4,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    AnisoLevel = anisoLevel,
                };
                Native.UploadTexture(m_NativeHandle, &packet);
            }
        }

        /// <summary>An asset texture: the native side imports it into a new handle.</summary>
        /// <summary>A texture the renderer imported under <paramref name="handle"/>.</summary>
        internal static Texture2D FromImport(ulong handle, string guid, string name, in ImportedTextureInfo info)
        {
            Texture2D texture;
            using (ObjectRegistry.AsAssets())
                texture = new Texture2D { m_Name = name, IsPersistentAsset = true, m_AssetGuid = guid };
            texture.m_NativeHandle = handle;
            texture.m_Width = info.Width;
            texture.m_Height = info.Height;
            texture.m_MipCount = info.MipCount;
            texture.m_GpuMipCount = Math.Max(1, info.MipCount);
            texture.m_IsReadable = info.Readable != 0;
            texture.m_Linear = info.Srgb == 0 || info.Format == 2;
            // HDR sources (EXR, HDR) import as half floats, the uncompressed format Unity gives them.
            if (info.Format == 2) texture.m_Format = TextureFormat.RGBAHalf;
            texture.filterMode = (FilterMode)info.FilterMode;
            texture.wrapModeU = (TextureWrapMode)info.WrapU;
            texture.wrapModeV = (TextureWrapMode)info.WrapV;
            texture.anisoLevel = info.AnisoLevel;
            return texture;
        }

        /// <summary>A texture serialized in an asset file (TextMeshPro atlases): pixels as hex.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = body.Int("m_Width");
            m_Height = body.Int("m_Height");
            m_Format = (TextureFormat)body.Int("m_TextureFormat", (int)TextureFormat.RGBA32);
            m_MipCount = 1;
            m_SourceFormat = body.Get("m_SourceTextureFormat") is YNode source ? (TextureFormat)source.AsInt() : null;
            m_SourceMips = body.Int("m_SourceMipCount", body.Int("m_MipCount", 1));
            m_IsReadable = body.Bool("m_IsReadable");
            // m_ColorSpace 0 is linear data (TextMeshPro's Alpha8 atlases, terrain splat maps).
            m_Linear = body.Int("m_ColorSpace", 1) == 0;
            var settings = body.Get("m_TextureSettings");
            if (settings != null)
            {
                filterMode = (FilterMode)settings.Int("m_FilterMode", 1);
                anisoLevel = settings.Int("m_Aniso", 1);
                wrapModeU = (TextureWrapMode)settings.Int("m_WrapU");
                wrapModeV = (TextureWrapMode)settings.Int("m_WrapV");
            }
            // The YAML form's pixels, else a binary asset's (`image data`, every mip level).
            var hex = body.Str("_typelessdata");
            if (hex.Length == 0) hex = body.Str("image data");
            m_Pixels = null;
            m_Raw = null;
            int count = Math.Max(0, m_Width * m_Height);
            if (hex.Length == 0 || count == 0) m_Pixels = new Color32[count];
            else
            {
                var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex.Substring(0, hex.Length - 1));
                int levelSize = PixelFormats.Size(m_Format, m_Width, m_Height);
                if (levelSize > 0 && bytes.Length >= levelSize)
                {
                    if (bytes.Length > levelSize) Array.Resize(ref bytes, levelSize);
                    m_Raw = bytes;
                }
                else
                {
                    m_Pixels = new Color32[count];
                    Debug.LogWarning($"Unbound: texture '{m_Name}' has pixel data in {m_Format}, which is not supported.");
                }
            }
            if (body.Get("m_StreamData") is YNode stream && stream.Str("path").Length > 0)
                Debug.LogWarning($"Unbound: texture '{m_Name}' keeps its pixels in a stream file; it is not supported.");
            m_PixelsDirty = true;
            // As in Unity, a texture that is not readable goes to the GPU when it loads and keeps
            // no copy in memory.
            if (!m_IsReadable) EnsureUploaded();
        }

        internal override void OnEngineDestroy()
        {
            Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }
}

namespace Unbound.Runtime
{
    internal static class TextureHandles
    {
        internal static ulong Of(UnityEngine.Texture texture)
        {
            if (texture == null) return 0;
            if (texture is UnityEngine.Texture2D t2d) return t2d.EnsureUploaded();
            if (texture is UnityEngine.RenderTexture rt) return rt.EnsureCreated();
            return texture.m_NativeHandle;
        }
    }

    internal static class DefaultTextures
    {
        static UnityEngine.Texture2D s_White, s_Black, s_Red, s_Gray, s_Normal;

        static UnityEngine.Texture2D Make(string name, UnityEngine.Color32 color)
        {
            var t = new UnityEngine.Texture2D(4, 4, UnityEngine.TextureFormat.RGBA32, false) { name = name };
            var pixels = new UnityEngine.Color32[16];
            for (int i = 0; i < 16; i++) pixels[i] = color;
            t.SetPixels32(pixels);
            t.Apply(false, false);
            return t;
        }

        internal static UnityEngine.Texture2D White => s_White ??= Make("UnityWhite", new UnityEngine.Color32(255, 255, 255, 255));
        internal static UnityEngine.Texture2D Black => s_Black ??= Make("UnityBlack", new UnityEngine.Color32(0, 0, 0, 255));
        internal static UnityEngine.Texture2D Red => s_Red ??= Make("UnityRed", new UnityEngine.Color32(255, 0, 0, 255));
        internal static UnityEngine.Texture2D Gray => s_Gray ??= Make("UnityGrey", new UnityEngine.Color32(128, 128, 128, 255));
        internal static UnityEngine.Texture2D Normal => s_Normal ??= Make("UnityNormalMap", new UnityEngine.Color32(128, 128, 255, 255));
    }

    /// <summary>Raw pixel data of the common texture formats, to and from RGBA32.</summary>
    internal static class PixelFormats
    {
        /// <summary>Bytes of one mip level, or 0 for formats not handled.</summary>
        internal static int Size(UnityEngine.TextureFormat format, int width, int height)
        {
            switch (format)
            {
                case UnityEngine.TextureFormat.Alpha8:
                case UnityEngine.TextureFormat.R8:
                    return width * height;
                case UnityEngine.TextureFormat.RGB24:
                    return width * height * 3;
                case UnityEngine.TextureFormat.RGBA32:
                case UnityEngine.TextureFormat.ARGB32:
                case UnityEngine.TextureFormat.BGRA32:
                    return width * height * 4;
                default:
                    return 0;
            }
        }

        internal static void Decode(UnityEngine.TextureFormat format, byte[] data, UnityEngine.Color32[] into)
        {
            int n = into.Length;
            switch (format)
            {
                case UnityEngine.TextureFormat.RGBA32:
                    for (int i = 0; i < n && i * 4 + 3 < data.Length; i++) into[i] = new UnityEngine.Color32(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
                    break;
                case UnityEngine.TextureFormat.ARGB32:
                    for (int i = 0; i < n && i * 4 + 3 < data.Length; i++) into[i] = new UnityEngine.Color32(data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3], data[i * 4]);
                    break;
                case UnityEngine.TextureFormat.BGRA32:
                    for (int i = 0; i < n && i * 4 + 3 < data.Length; i++) into[i] = new UnityEngine.Color32(data[i * 4 + 2], data[i * 4 + 1], data[i * 4], data[i * 4 + 3]);
                    break;
                case UnityEngine.TextureFormat.RGB24:
                    for (int i = 0; i < n && i * 3 + 2 < data.Length; i++) into[i] = new UnityEngine.Color32(data[i * 3], data[i * 3 + 1], data[i * 3 + 2], 255);
                    break;
                case UnityEngine.TextureFormat.Alpha8:
                    for (int i = 0; i < n && i < data.Length; i++) into[i] = new UnityEngine.Color32(255, 255, 255, data[i]);
                    break;
                case UnityEngine.TextureFormat.R8:
                    for (int i = 0; i < n && i < data.Length; i++) into[i] = new UnityEngine.Color32(data[i], 0, 0, 255);
                    break;
                default:
                    UnityEngine.Debug.LogWarning($"Unbound: raw texture data in {format} is not supported.");
                    break;
            }
        }

        /// <summary>Channels and bytes per channel of the floating point formats.</summary>
        static (int Channels, int Bytes) FloatLayout(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.RHalf => (1, 2),
            UnityEngine.TextureFormat.RGHalf => (2, 2),
            UnityEngine.TextureFormat.RGBAHalf => (4, 2),
            UnityEngine.TextureFormat.RFloat => (1, 4),
            UnityEngine.TextureFormat.RGFloat => (2, 4),
            UnityEngine.TextureFormat.RGBAFloat => (4, 4),
            _ => (0, 0),
        };

        internal static void DecodeFloat(UnityEngine.TextureFormat format, byte[] data, UnityEngine.Color[] into)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0)
            {
                UnityEngine.Debug.LogWarning($"Unbound: raw texture data in {format} is not supported.");
                return;
            }
            for (int i = 0; i < into.Length && (i + 1) * channels * size <= data.Length; i++)
            {
                var c = new UnityEngine.Color(0f, 0f, 0f, 1f);
                for (int k = 0; k < channels; k++)
                {
                    int at = (i * channels + k) * size;
                    float v = size == 2 ? (float)BitConverter.ToHalf(data, at) : BitConverter.ToSingle(data, at);
                    c[k] = v;
                }
                into[i] = c;
            }
        }

        internal static byte[] EncodeFloat(UnityEngine.TextureFormat format, UnityEngine.Color[] pixels)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0) return new byte[0];
            var bytes = new byte[pixels.Length * channels * size];
            for (int i = 0; i < pixels.Length; i++)
                for (int k = 0; k < channels; k++)
                {
                    int at = (i * channels + k) * size;
                    if (size == 2) BitConverter.TryWriteBytes(new Span<byte>(bytes, at, 2), (Half)pixels[i][k]);
                    else BitConverter.TryWriteBytes(new Span<byte>(bytes, at, 4), pixels[i][k]);
                }
            return bytes;
        }

        internal static byte[] Encode(UnityEngine.TextureFormat format, UnityEngine.Color32[] pixels)
        {
            switch (format)
            {
                case UnityEngine.TextureFormat.Alpha8:
                {
                    var a = new byte[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) a[i] = pixels[i].a;
                    return a;
                }
                case UnityEngine.TextureFormat.RGB24:
                {
                    var b = new byte[pixels.Length * 3];
                    for (int i = 0; i < pixels.Length; i++) { b[i * 3] = pixels[i].r; b[i * 3 + 1] = pixels[i].g; b[i * 3 + 2] = pixels[i].b; }
                    return b;
                }
                default:
                {
                    var b = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++) { b[i * 4] = pixels[i].r; b[i * 4 + 1] = pixels[i].g; b[i * 4 + 2] = pixels[i].b; b[i * 4 + 3] = pixels[i].a; }
                    return b;
                }
            }
        }
    }

    /// <summary>Texture2D.PackTextures: a shelf packer.</summary>
    internal static class TexturePacker
    {
        internal static UnityEngine.Rect[] Pack(UnityEngine.Texture2D atlas, UnityEngine.Texture2D[] textures, int padding, int maxSize, bool makeNoLongerReadable)
        {
            int width = 1, height = 1;
            var rects = new UnityEngine.Rect[textures.Length];
            int x = 0, y = 0, rowHeight = 0, size = 64;
            while (true)
            {
                x = y = rowHeight = 0;
                bool fits = true;
                for (int i = 0; i < textures.Length && fits; i++)
                {
                    var t = textures[i];
                    if (x + t.width > size) { x = 0; y += rowHeight + padding; rowHeight = 0; }
                    if (y + t.height > size || t.width > size) { fits = false; break; }
                    rects[i] = new UnityEngine.Rect(x, y, t.width, t.height);
                    x += t.width + padding;
                    rowHeight = Math.Max(rowHeight, t.height);
                }
                if (fits || size >= maxSize) break;
                size *= 2;
            }
            width = height = Math.Min(size, maxSize);
            atlas.Reinitialize(width, height);
            var pixels = new UnityEngine.Color32[width * height];
            for (int i = 0; i < textures.Length; i++)
            {
                var src = textures[i].GetPixels32();
                var r = rects[i];
                for (int j = 0; j < (int)r.height && (int)r.y + j < height; j++)
                    for (int k = 0; k < (int)r.width && (int)r.x + k < width; k++)
                        pixels[((int)r.y + j) * width + (int)r.x + k] = src[j * textures[i].width + k];
                rects[i] = new UnityEngine.Rect(r.x / width, r.y / height, r.width / width, r.height / height);
            }
            atlas.SetPixels32(pixels);
            atlas.Apply(true, makeNoLongerReadable);
            return rects;
        }
    }
}
