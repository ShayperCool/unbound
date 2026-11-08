using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A cube texture: six square faces, each with its mip chain. Script-made cubemaps
    /// keep their pixels (RGBA, floats for HDR formats) and upload on Apply; built-in ones load
    /// straight to the renderer.</summary>
    [ClassId(89)]
    public sealed partial class Cubemap : Texture, ISerializedAsset
    {
        TextureFormat m_Format = TextureFormat.RGBA32;
        int m_MipCount = 1;
        bool m_Readable = true;
        internal bool m_Linear;
        /// <summary>Per face, per mip level: the pixels (readable cubemaps).</summary>
        Color[][][] m_Pixels;
        /// <summary>The data is RGBM encoded (a reflection cubemap).</summary>
        internal bool m_Rgbm;
        /// <summary>An imported cubemap whose faces the importer gives on first access (an unreadable
        /// one's on its first clone).</summary>
        string m_AssetGuid;

        public Cubemap(int width, TextureFormat textureFormat, bool mipChain) : this(width, textureFormat, mipChain ? -1 : 1, false) { }
        public Cubemap(int width, TextureFormat textureFormat, bool mipChain, bool createUninitialized) : this(width, textureFormat, mipChain ? -1 : 1, createUninitialized) { }
        public Cubemap(int width, TextureFormat format, int mipCount) : this(width, format, mipCount, false) { }

        public Cubemap(int width, TextureFormat format, int mipCount, bool createUninitialized)
        {
            if (width <= 0) throw new ArgumentException("Cubemap width must be positive.", nameof(width));
            m_Width = m_Height = width;
            m_Format = format;
            m_MipCount = mipCount < 0 ? FullMipCount(width) : Math.Max(1, Math.Min(mipCount, FullMipCount(width)));
            // A cubemap made from a TextureFormat holds linear data (Unity creates it so).
            m_Linear = true;
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
                    // A new cubemap starts gray (205 in every channel, measured).
                    var pixels = m_Pixels[f][m] = new Color[s * s];
                    Array.Fill(pixels, new Color(205f / 255f, 205f / 255f, 205f / 255f, 205f / 255f));
                }
            }
        }

        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_MipCount;
        public override TextureDimension dimension { get => TextureDimension.Cube; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear && QualitySettings.activeColorSpace == ColorSpace.Linear);
        internal bool isPreProcessed => false;
        public bool streamingMipmaps { get { Stubs.Ignored("Cubemap.streamingMipmaps", "is not read from the import settings: texture streaming is not simulated"); return false; } }
        public int streamingMipmapsPriority { get { Stubs.Ignored("Cubemap.streamingMipmapsPriority", "is not read from the import settings: texture streaming is not simulated"); return 0; } }
        public int requestedMipmapLevel { get; set; }
        internal bool loadAllMips { get; set; }
        public int desiredMipmapLevel { get { Stubs.Ignored("Cubemap.desiredMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }
        public int loadingMipmapLevel { get { Stubs.Ignored("Cubemap.loadingMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }
        public int loadedMipmapLevel { get { Stubs.Ignored("Cubemap.loadedMipmapLevel", "is not simulated: every mip of every texture is loaded"); return 0; } }

        public void ClearRequestedMipmapLevel() => requestedMipmapLevel = 0;
        public bool IsRequestedMipmapLevelLoaded()
        {
            Stubs.Ignored("Cubemap.IsRequestedMipmapLevelLoaded", "is not simulated: every mip of every texture is loaded");
            return true;
        }

        /// <summary>The faces of an imported readable cubemap from the importer: bytes read as
        /// b × (1 / 255), as array and volume textures, and each face's rows top row first (the
        /// importer keeps them bottom-up for the renderer); tests/projects/texturearrays.</summary>
        void FetchImported()
        {
            var pixels = new Color[6][][];
            try
            {
                for (int f = 0; f < 6; f++)
                {
                    pixels[f] = new Color[m_MipCount][];
                    for (int mip = 0; mip < m_MipCount; mip++)
                    {
                        var json = Native.Call("texture-pixels", JsonSerializer.Serialize(new { guid = m_AssetGuid, mip, layer = f }));
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var bytes = Convert.FromBase64String(root.GetProperty("rgba").GetString());
                        int s = Math.Max(1, m_Width >> mip);
                        var level = pixels[f][mip] = new Color[s * s];
                        int stride = bytes.Length / s;
                        var flipped = new byte[bytes.Length];
                        for (int y = 0; y < s; y++) Buffer.BlockCopy(bytes, (s - 1 - y) * stride, flipped, y * stride, stride);
                        bytes = flipped;
                        if (root.TryGetProperty("format", out var format) && format.GetInt32() == 2)
                        {
                            PixelFormats.DecodeFloat(TextureFormat.RGBAFloat, bytes, level);
                            for (int i = 0; i < level.Length; i++) level[i] = PixelFormats.QuantizeFloat(m_Format, level[i]);
                        }
                        else
                        {
                            const float scale = 1f / 255f;
                            for (int i = 0; i < level.Length; i++)
                                level[i] = new Color(bytes[i * 4] * scale, bytes[i * 4 + 1] * scale, bytes[i * 4 + 2] * scale, bytes[i * 4 + 3] * scale);
                        }
                    }
                }
                m_Pixels = pixels;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading the pixels of {name}: {e.Message}");
            }
            m_AssetGuid = null;
        }

        /// <summary>A face's level, current with the pixel data store while one exists.</summary>
        Color[] Level(CubemapFace face, int mip)
        {
            var level = CheckedLevel(face, mip);
            if (m_Store != null) DecodeStored((int)face, mip, level);
            return level;
        }

        Color[] CheckedLevel(CubemapFace face, int mip)
        {
            CheckStoreRead();
            if (m_Readable && m_Pixels == null && m_AssetGuid != null) FetchImported();
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            if (face < CubemapFace.PositiveX || face > CubemapFace.NegativeZ) throw new ArgumentException("Invalid cubemap face.", nameof(face));
            if (mip < 0 || mip >= m_MipCount) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels[(int)face][mip];
        }

        public void SetPixel(CubemapFace face, int x, int y, Color color) => SetPixel(face, x, y, color, 0);

        public void SetPixel(CubemapFace face, int x, int y, Color color, int mip)
        {
            CheckStoreWrite();
            var level = CheckedLevel(face, mip);
            int s = Math.Max(1, m_Width >> mip);
            if (x < 0 || y < 0 || x >= s || y >= s) return;
            var c = level[y * s + x] = PixelFormats.QuantizeFloat(m_Format, color);
            if (m_Store != null) EncodeStored(c, StoreTexel((int)face, mip, y * s + x));
        }

        public Color GetPixel(CubemapFace face, int x, int y) => GetPixel(face, x, y, 0);

        public Color GetPixel(CubemapFace face, int x, int y, int mip)
        {
            var level = CheckedLevel(face, mip);
            int s = Math.Max(1, m_Width >> mip);
            x = Mathf.Clamp(x, 0, s - 1);
            y = Mathf.Clamp(y, 0, s - 1);
            return m_Store != null ? DecodeStored(StoreTexel((int)face, mip, y * s + x)) : level[y * s + x];
        }

        public Color[] GetPixels(CubemapFace face) => GetPixels(face, 0);
        public Color[] GetPixels(CubemapFace face, int miplevel)
        {
            if (!m_Readable && Compat.AssetCloneResourcesMeasured)
                throw new ArgumentException($"Cubemap.GetPixels: texture data is either not readable, corrupted or does not exist. (Texture '{name}')");
            return (Color[])Level(face, miplevel).Clone();
        }

        public void SetPixels(Color[] colors, CubemapFace face) => SetPixels(colors, face, 0);

        public void SetPixels(Color[] colors, CubemapFace face, int miplevel)
        {
            CheckStoreWrite();
            var level = CheckedLevel(face, miplevel);
            if (colors == null || colors.Length != level.Length) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            for (int i = 0; i < level.Length; i++) level[i] = PixelFormats.QuantizeFloat(m_Format, colors[i]);
            if (m_Store != null)
                for (int i = 0; i < level.Length; i++) EncodeStored(level[i], StoreTexel((int)face, miplevel, i));
        }

        /// <summary>Once a script asks for <see cref="GetPixelData{T}"/>: the six faces in the
        /// cubemap's format, each with its mip chain in turn, rows as <c>GetPixels</c> returns them
        /// (tests/projects/texturearrays CubeStrip*.raw). It is then the cubemap's pixels: readers and
        /// writers go through it, until the cubemap loses its CPU copy.</summary>
        byte[] m_Store;
        int[] m_StoreOffsets;
        GCHandle m_StorePin;
        AtomicSafetyHandle m_StoreSafety;

        void CheckStoreRead() { if (m_Store != null) AtomicSafetyHandle.CheckReadAndThrow(m_StoreSafety); }
        void CheckStoreWrite() { if (m_Store != null) AtomicSafetyHandle.CheckWriteAndThrow(m_StoreSafety); }

        int TexelBytes => PixelFormats.Size(m_Format, 1, 1);
        int LevelBytes(int mip) => PixelFormats.Size(m_Format, Math.Max(1, m_Width >> mip), Math.Max(1, m_Width >> mip));
        Span<byte> StoreTexel(int face, int mip, int i) => new Span<byte>(m_Store, m_StoreOffsets[face * m_MipCount + mip] + i * TexelBytes, TexelBytes);

        /// <summary>The stored bytes of a face's mip level (no copy, as in Unity).</summary>
        public unsafe NativeArray<T> GetPixelData<T>(int mipLevel, CubemapFace face) where T : struct
        {
            CheckedLevel(face, mipLevel);
            if (m_Store == null)
            {
                if (TexelBytes == 0) throw new UnityException($"Unbound: Cubemap.GetPixelData of a {m_Format} cubemap is not supported.");
                var offsets = new int[6 * m_MipCount + 1];
                for (int i = 0; i < 6 * m_MipCount; i++) offsets[i + 1] = checked(offsets[i] + LevelBytes(i % m_MipCount));
                m_Store = new byte[offsets[^1]];
                m_StoreOffsets = offsets;
                for (int f = 0; f < 6; f++)
                    for (int m = 0; m < m_MipCount; m++)
                    {
                        var level = m_Pixels[f][m];
                        for (int i = 0; i < level.Length; i++) EncodeStored(level[i], StoreTexel(f, m, i));
                    }
                m_StorePin = GCHandle.Alloc(m_Store, GCHandleType.Pinned);
                m_StoreSafety = AtomicSafetyHandle.Create();
            }
            int size = UnsafeUtility.SizeOf<T>();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (byte*)m_StorePin.AddrOfPinnedObject() + m_StoreOffsets[(int)face * m_MipCount + mipLevel], LevelBytes(mipLevel) / size, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_StoreSafety);
            return array;
        }

        /// <summary>A stored texel: a byte b reads b × (1 / 255), as the cubemap's faces do.</summary>
        Color DecodeStored(ReadOnlySpan<byte> texel)
        {
            if (IsHdr)
                return PixelFormats.QuantizeFloat(m_Format, PixelFormats.DecodeFloatTexel(m_Format, texel));
            var c = Texture2DTexels.DecodeCell(m_Format, texel);
            const float scale = 1f / 255f;
            return new Color(c.r * scale, c.g * scale, c.b * scale, c.a * scale);
        }

        void DecodeStored(int face, int mip, Color[] level)
        {
            for (int i = 0; i < level.Length; i++) level[i] = DecodeStored(StoreTexel(face, mip, i));
        }

        /// <summary>A texel into the store; a byte half way between two values rounds upward.</summary>
        void EncodeStored(Color c, Span<byte> texel)
        {
            static byte Channel(float v) => (byte)Math.Floor(Mathf.Clamp01(v) * 255f + 0.5f);
            if (IsHdr) PixelFormats.EncodeFloatTexel(m_Format, c, texel);
            else Texture2DTexels.EncodeCell(m_Format, new Color32(Channel(c.r), Channel(c.g), Channel(c.b), Channel(c.a)), texel);
        }

        void ReleaseStore()
        {
            TexturePixelStore.Release(ref m_StoreSafety, ref m_StorePin);
            m_Store = null;
            m_StoreOffsets = null;
        }

        public void SmoothEdges() => Stubs.Ignored("Cubemap.SmoothEdges", "is not done: the faces' seams are left as they are");
        public void SmoothEdges(int smoothRegionWidthInPixels) => Stubs.Ignored("Cubemap.SmoothEdges", "is not done: the faces' seams are left as they are");

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (updateMipmaps) CheckStoreWrite(); else CheckStoreRead();
            if (!m_Readable) throw new UnityException($"Texture '{name}' is not readable.");
            if (m_Pixels == null && m_AssetGuid != null) FetchImported();
            if (m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable.");
            if (m_Store != null)
                for (int f = 0; f < 6; f++)
                    for (int m = 0; m < m_MipCount; m++) DecodeStored(f, m, m_Pixels[f][m]);
            if (updateMipmaps)
                for (int f = 0; f < 6; f++)
                    for (int m = 1; m < m_MipCount; m++)
                    {
                        Downsample(m_Pixels[f][m - 1], Math.Max(1, m_Width >> (m - 1)), m_Pixels[f][m]);
                        if (m_Store != null)
                            for (int i = 0; i < m_Pixels[f][m].Length; i++) EncodeStored(m_Pixels[f][m][i], StoreTexel(f, m, i));
                    }
            Upload();
            if (makeNoLongerReadable)
            {
                ReleaseStore();
                m_Readable = false;
                // Kept for clones only, as Texture2D keeps its retained storage.
                m_UnreadablePixels = m_Pixels;
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
            bool hdr = IsHdr;
            int pixels = 0;
            for (int m = 0; m < m_MipCount; m++) { int s = Math.Max(1, m_Width >> m); pixels += s * s; }
            var data = new byte[pixels * 6 * (hdr ? 16 : 4)];
            int at = 0;
            for (int f = 0; f < 6; f++)
                for (int m = 0; m < m_MipCount; m++)
                {
                    var level = m_Pixels[f][m];
                    for (int i = 0; i < level.Length; i++)
                    {
                        // A face's rows go up top row first, as GetPixels returns them: the GPU
                        // addresses cube faces top-down (tests/projects/texturepixels CubeStrip*.applied).
                        var c = level[i];
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
                }
            UploadStorage(data);
        }

        unsafe void UploadStorage(byte[] data)
        {
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            fixed (byte* p = data)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = IsHdr ? PixelFormats.GpuFloatFormat(m_Format) : m_Linear ? 1 : 0,
                    MipCount = m_MipCount,
                    Data = p,
                    DataLength = data?.Length ?? 0,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    WrapW = (int)wrapModeW,
                    AnisoLevel = anisoLevel,
                    Layers = 6,
                    Dimension = (int)TextureDimension.Cube,
                };
                UploadTexture(&packet);
            }
        }

        /// <summary>A cubemap in Unity's serialization (an AssetBundle's skybox reflection): six
        /// images one after the other, each with its mip chain; RGBA32 (RGBM encoded when its
        /// lightmap format is 5) goes straight to the renderer.</summary>
        unsafe void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = body.Int("m_Width");
            m_Height = body.Int("m_Height");
            m_Format = (TextureFormat)body.Int("m_TextureFormat", (int)TextureFormat.RGBA32);
            m_MipCount = Math.Max(1, body.Int("m_MipCount", 1));
            m_Readable = false;
            m_Pixels = null;
            m_Rgbm = body.Int("m_LightmapFormat") == 5;
            m_Linear = m_Rgbm || body.Int("m_ColorSpace", 1) == 0;
            var settings = body.Get("m_TextureSettings");
            if (settings != null)
            {
                filterMode = (FilterMode)settings.Int("m_FilterMode", 1);
                anisoLevel = settings.Int("m_Aniso", 1);
                wrapModeU = (TextureWrapMode)settings.Int("m_WrapU");
                wrapModeV = (TextureWrapMode)settings.Int("m_WrapV");
            }
            if (m_Format != TextureFormat.RGBA32)
            {
                Debug.LogWarning($"Unbound: cubemap '{m_Name}' is stored as {m_Format}, which Unbound does not read from serialized data yet.");
                return;
            }
            // The YAML form keeps its pixels in _typelessdata (`image data` is their size there); a binary
            // asset, in `image data`.
            var hex = body.Str("_typelessdata");
            if (hex.Length == 0) hex = body.Str("image data");
            var data = hex.Length >= 2 ? Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex.Substring(0, hex.Length - 1)) : Array.Empty<byte>();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            fixed (byte* p = data)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = m_Linear ? 1 : 0,
                    MipCount = m_MipCount,
                    Data = p,
                    DataLength = data.Length,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    WrapW = (int)wrapModeW,
                    AnisoLevel = anisoLevel,
                    Layers = 6,
                    Dimension = (int)TextureDimension.Cube,
                };
                UploadTexture(&packet);
            }
        }

        /// <summary>A cubemap the renderer imported from an image under <paramref name="handle"/>
        /// (a texture asset of the Cube shape): its pixels stay on the GPU.</summary>
        internal static Cubemap FromImport(ulong handle, string guid, string name, in ImportedTextureInfo info)
        {
            Cubemap cube;
            using (ObjectRegistry.AsAssets())
                cube = new Cubemap { IsPersistentAsset = true, m_Readable = info.Readable != 0 };
            // An unreadable cubemap reads its faces from the importer for its clones only.
            cube.m_AssetGuid = guid;
            cube.m_NativeHandle = handle;
            cube.m_Name = name;
            cube.m_Width = cube.m_Height = info.Width;
            cube.m_MipCount = Math.Max(1, info.MipCount);
            cube.m_Linear = info.Srgb == 0 || info.Format == 2;
            cube.m_Format = PixelFormats.ImportedFormat(info);
            cube.m_WrapModeW = (TextureWrapMode)info.WrapW;
            cube.filterMode = (FilterMode)info.FilterMode;
            cube.wrapModeU = (TextureWrapMode)info.WrapU;
            cube.wrapModeV = (TextureWrapMode)info.WrapV;
            cube.anisoLevel = info.AnisoLevel;
            cube.ReadSamplerMetadata();
            return cube;
        }

        /// <summary>A cubemap of a serialized file (the built-in default reflection), uploaded by
        /// the renderer from the file; null when it cannot be read.</summary>
        internal static Cubemap FromSerialized(string guid, long fileId)
        {
            Cubemap cube;
            using (ObjectRegistry.AsAssets())
                cube = new Cubemap { IsPersistentAsset = true, m_Readable = false, m_Linear = true };
            if (Compat.AssetCloneResourcesMeasured) cube.hideFlags = HideFlags.NotEditable;
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
            ReleaseStore();
            Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }
}
