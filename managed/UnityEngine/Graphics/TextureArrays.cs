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
    /// <summary>The pixels of a texture with layers (an array's slices, a cube array's faces, a
    /// volume's depth) and mips, stored as Unity stores them: the bytes of the texture's format, an
    /// array's images each with its mip chain in turn, a volume's levels one after another (each
    /// level all its depth slices, which halve with the level). Readers decode, writers encode;
    /// <c>GetPixelData</c> is a view of the bytes.</summary>
    internal sealed class LayeredPixels
    {
        internal readonly int Width, Height, Layers, Mips;
        internal readonly bool Volume;
        internal readonly TextureFormat Format;
        internal readonly byte[] Data;
        readonly int[] m_Offsets;
        GCHandle m_Pinned;
        AtomicSafetyHandle m_Safety;

        internal LayeredPixels(int width, int height, int layers, int mips, bool volume, TextureFormat format, byte[] data = null)
        {
            Width = width;
            Height = height;
            Layers = layers;
            Mips = mips;
            Volume = volume;
            Format = format;
            if (width <= 0 || height <= 0 || layers <= 0 || mips <= 0)
                throw new UnityException($"Unbound: a layered texture needs positive dimensions and mip count, got {width}x{height}x{layers} with {mips} mips.");
            long offsets = (long)Images * mips + 1;
            if (offsets > Array.MaxLength / sizeof(int))
                throw new UnityException($"Unbound: a {width}x{height}x{layers} texture with {mips} mips needs {offsets} pixel offsets, exceeding one managed array.");
            // Check every product and sum before allocating storage or exposing a raw-data view.
            long texel = PixelFormats.Size(format, 1, 1), at = 0, texels = 0, total, upload;
            try
            {
                checked
                {
                    for (int mip = 0; mip < mips; mip++)
                        texels += (long)W(mip) * H(mip) * D(mip) * Images;
                    total = texel * texels;
                    upload = texels * (GraphicsFormatUtility.IsHDRFormat(format) ? 16 : 4);
                }
            }
            catch (OverflowException)
            {
                throw new UnityException($"Unbound: the storage size of a {width}x{height}x{layers} {format} texture with {mips} mips exceeds 64-bit sizing.");
            }
            if (Math.Max(total, upload) > Array.MaxLength)
                throw new UnityException($"Unbound: a {width}x{height}x{layers} {format} texture with {mips} mips needs {total} bytes " +
                    $"({upload} to upload); Unbound keeps a layered texture's pixels in one array of at most {Array.MaxLength} bytes.");
            m_Offsets = new int[(int)offsets];
            for (int image = 0; image < Images; image++)
                for (int mip = 0; mip < mips; mip++)
                {
                    m_Offsets[image * mips + mip] = (int)at;
                    at += LevelBytes(mip);
                }
            m_Offsets[^1] = (int)at;
            if (data != null && data.Length < at)
                throw new ArgumentException($"the texture data has {data.Length} bytes; its {Images} images of {mips} mips need {at}");
            Data = data ?? new byte[at];
        }

        /// <summary>Whether Unbound reads and writes pixels of the format.</summary>
        internal static bool Supported(TextureFormat format) => PixelFormats.Size(format, 1, 1) > 0;

        internal int Images => Volume ? 1 : Layers;
        internal int W(int mip) => Math.Max(1, Width >> mip);
        internal int H(int mip) => Math.Max(1, Height >> mip);
        internal int D(int mip) => Volume ? Math.Max(1, Layers >> mip) : 1;
        internal int Size(int mip) => W(mip) * H(mip) * D(mip);
        internal int LevelBytes(int mip) => PixelFormats.Size(Format, W(mip), H(mip)) * D(mip);
        int Offset(int image, int mip) => m_Offsets[image * Mips + mip];
        bool Hdr => GraphicsFormatUtility.IsHDRFormat(Format);

        internal static int FullMipCount(int width, int height, int depth) =>
            32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, Math.Max(width, Math.Max(height, depth))));

        byte[] Level(int image, int mip) => new ReadOnlySpan<byte>(Data, Offset(image, mip), LevelBytes(mip)).ToArray();

        /// <summary>As <c>GetPixels32</c> reads the stored bytes (R8 reads (r, 1, 1, 1), as the
        /// Unity 6000.0 Editor's array and volume readers do: tests/projects/texturearrays).</summary>
        internal Color32[] GetPixels32(int image, int mip)
        {
            var result = new Color32[Size(mip)];
            if (Hdr)
            {
                var colors = GetPixels(image, mip);
                for (int i = 0; i < result.Length; i++) result[i] = colors[i];
                return result;
            }
            var bytes = Level(image, mip);
            if (Format == TextureFormat.R8)
                for (int i = 0; i < result.Length; i++) result[i] = new Color32(bytes[i], 255, 255, 255);
            else
                PixelFormats.Decode(Format, bytes, result);
            return result;
        }

        /// <summary>As <c>GetPixels</c> reads the stored values: a byte b is b × (1 / 255) in
        /// single precision (not b / 255; measured in tests/projects/texturearrays).</summary>
        internal Color[] GetPixels(int image, int mip)
        {
            var result = new Color[Size(mip)];
            if (Hdr)
            {
                PixelFormats.DecodeFloat(Format, Level(image, mip), result);
                // Channels an R or RG float format does not store read one
                // (tests/projects/resources floatformats.*.cpu).
                for (int i = 0; i < result.Length; i++) result[i] = PixelFormats.QuantizeFloat(Format, result[i]);
                return result;
            }
            const float scale = 1f / 255f;
            var bytes = GetPixels32(image, mip);
            for (int i = 0; i < result.Length; i++)
            {
                var c = bytes[i];
                result[i] = new Color(c.r * scale, c.g * scale, c.b * scale, c.a * scale);
            }
            return result;
        }

        /// <summary>One texel of a level, decoding only its bytes: as Texture3D.GetPixel reads it, a
        /// byte b is b / 255 (not GetPixels' b × (1 / 255); tests/projects/texturepixels).</summary>
        internal Color GetPixel(int image, int mip, int index)
        {
            int size = PixelFormats.Size(Format, 1, 1);
            var bytes = new byte[size];
            Buffer.BlockCopy(Data, Offset(image, mip) + index * size, bytes, 0, size);
            if (Hdr)
            {
                var one = new Color[1];
                PixelFormats.DecodeFloat(Format, bytes, one);
                return PixelFormats.QuantizeFloat(Format, one[0]);
            }
            var c = new Color32[1];
            if (Format == TextureFormat.R8) c[0] = new Color32(bytes[0], 255, 255, 255);
            else PixelFormats.Decode(Format, bytes, c);
            return c[0];
        }

        /// <summary>One texel of a level, encoding only its bytes: Texture3D.SetPixel rounds a byte
        /// half way between two values upward (tests/projects/texturepixels VolumeSetPixel).</summary>
        internal void SetPixel(int image, int mip, int index, Color color)
        {
            static byte Channel(float v) => (byte)Math.Floor(Mathf.Clamp01(v) * 255f + 0.5f);
            var bytes = Hdr ? PixelFormats.EncodeFloat(Format, new[] { color })
                : PixelFormats.Encode(Format, new[] { new Color32(Channel(color.r), Channel(color.g), Channel(color.b), Channel(color.a)) });
            int size = PixelFormats.Size(Format, 1, 1);
            Buffer.BlockCopy(bytes, 0, Data, Offset(image, mip) + index * size, Math.Min(bytes.Length, size));
        }

        internal void SetPixels(int image, int mip, Color[] colors)
        {
            if (colors == null || colors.Length != Size(mip)) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            if (Hdr)
            {
                Store(image, mip, PixelFormats.EncodeFloat(Format, colors));
                return;
            }
            var bytes = new Color32[colors.Length];
            for (int i = 0; i < colors.Length; i++) bytes[i] = colors[i];
            Store(image, mip, PixelFormats.Encode(Format, bytes));
        }

        internal void SetPixels32(int image, int mip, Color32[] colors)
        {
            if (colors == null || colors.Length != Size(mip)) throw new ArgumentException("Array size must match the mip level's size.", nameof(colors));
            if (Hdr)
            {
                SetPixels(image, mip, Array.ConvertAll(colors, c => (Color)c));
                return;
            }
            Store(image, mip, PixelFormats.Encode(Format, colors));
        }

        void Store(int image, int mip, byte[] bytes) =>
            Buffer.BlockCopy(bytes, 0, Data, Offset(image, mip), Math.Min(bytes.Length, LevelBytes(mip)));

        /// <summary>The stored bytes of one level (no copy, as in Unity).</summary>
        internal unsafe NativeArray<T> View<T>(int image, int mip) where T : struct
        {
            int size = UnsafeUtility.SizeOf<T>();
            if (!m_Pinned.IsAllocated) m_Pinned = GCHandle.Alloc(Data, GCHandleType.Pinned);
            if (m_Safety.versionNode == IntPtr.Zero) m_Safety = AtomicSafetyHandle.Create();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (byte*)m_Pinned.AddrOfPinnedObject() + Offset(image, mip), LevelBytes(mip) / size, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            return array;
        }

        internal void CopyIn(ReadOnlySpan<byte> bytes, int image, int mip)
        {
            int size = LevelBytes(mip);
            if (bytes.Length < size) throw new ArgumentException("SetPixelData: not enough data provided.");
            bytes.Slice(0, size).CopyTo(new Span<byte>(Data, Offset(image, mip), size));
        }

        /// <summary>Each mip a box filter of the one above (in x, y, and z for a volume).</summary>
        internal void BuildMips()
        {
            for (int image = 0; image < Images; image++)
                for (int m = 1; m < Mips; m++)
                {
                    var src = GetPixels(image, m - 1);
                    var dst = new Color[Size(m)];
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
                    SetPixels(image, m, dst);
                }
        }

        /// <summary>To the renderer: RGBA bytes (single channels sampled as (r, 0, 0, 1) and
        /// (0, 0, 0, a)), R8 and RG16 in one and two channels as Texture2D's
        /// (<see cref="PixelFormats.GpuChannelFormat"/>), or floats for HDR formats.</summary>
        internal unsafe void Upload(ulong handle, bool linear, FilterMode filter, TextureWrapMode wrapU, TextureWrapMode wrapV, TextureWrapMode wrapW, int aniso,
            TextureDimension? dimension = null)
        {
            bool hdr = Hdr;
            int channels = hdr ? -1 : PixelFormats.GpuChannelFormat(Format, linear);
            int code = hdr ? PixelFormats.GpuFloatFormat(Format) : channels >= 0 ? channels : linear ? 1 : 0;
            int texelBytes = hdr ? 16 : PixelFormats.GpuTexelBytes(code);
            bool blueOne = PixelFormats.SamplesBlueOne(Format, code);
            int texels = 0;
            for (int image = 0; image < Images; image++)
                for (int mip = 0; mip < Mips; mip++) texels += Size(mip);
            var data = new byte[texels * texelBytes];
            int at = 0;
            for (int image = 0; image < Images; image++)
                for (int mip = 0; mip < Mips; mip++)
                {
                    if (hdr)
                    {
                        foreach (var c in GetPixels(image, mip))
                        {
                            BitConverter.TryWriteBytes(new Span<byte>(data, at, 4), c.r);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 4, 4), c.g);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 8, 4), c.b);
                            BitConverter.TryWriteBytes(new Span<byte>(data, at + 12, 4), c.a);
                            at += 16;
                        }
                    }
                    else
                    {
                        var level = new Color32[Size(mip)];
                        PixelFormats.Decode(Format, Level(image, mip), level);
                        if (blueOne)
                            for (int i = 0; i < level.Length; i++) level[i].b = 255;
                        PixelFormats.GpuTexels(code, level, new Span<byte>(data, at, level.Length * texelBytes));
                        at += level.Length * texelBytes;
                    }
                }
            fixed (byte* p = data)
            {
                var packet = new TexturePacket
                {
                    Width = Width,
                    Height = Height,
                    Format = code,
                    MipCount = Mips,
                    Data = p,
                    DataLength = data.Length,
                    FilterMode = (int)filter,
                    WrapU = (int)wrapU,
                    WrapV = (int)wrapV,
                    WrapW = (int)wrapW,
                    AnisoLevel = aniso,
                    Layers = Layers,
                    Dimension = (int)(dimension ?? (Volume ? TextureDimension.Tex3D : TextureDimension.Tex2DArray)),
                };
                Native.UploadTexture(handle, &packet);
            }
        }

        internal void Release()
        {
            TexturePixelStore.Release(ref m_Safety, ref m_Pinned);
        }
    }

    /// <summary>What array, cube array and volume textures share: the stored pixels, their
    /// readability, loading from a serialized asset or an imported image.</summary>
    internal static class LayeredTextures
    {
        /// <summary>The not-readable errors of the Unity 6000.0 Editor (tests/projects/texturearrays).</summary>
        internal static Exception NotReadable(string api, string name) =>
            new ArgumentException($"{api}: texture data is either not readable, corrupted or does not exist. (Texture '{name}')");

        internal static Exception NoMemoryAccess(string name) =>
            new UnityException($"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");

        internal readonly struct Serialized
        {
            internal readonly TextureFormat Format;
            internal readonly bool Srgb;
            internal readonly bool Readable;
            internal readonly int Mips;
            internal readonly byte[] Data;

            internal Serialized(TextureFormat format, bool srgb, bool readable, int mips, byte[] data)
            {
                Format = format;
                Srgb = srgb;
                Readable = readable;
                Mips = mips;
                Data = data;
            }
        }

        /// <summary>The fields a Texture3D, Texture2DArray or CubemapArray asset keeps: its
        /// GraphicsFormat (<c>m_Format</c>), colour space (1 sRGB), mips, readability, sampler and
        /// pixels (the YAML form's <c>_typelessdata</c>, a binary file's <c>image data</c>).</summary>
        internal static Serialized Read(Texture texture, YNode body)
        {
            var graphics = (GraphicsFormat)body.Int("m_Format");
            // The stored bytes follow the GraphicsFormat. B8G8R8A8 bytes are BGRA32's
            // (tests/projects/texturepixels), not ARGB32's, which the table also maps to it.
            var format = graphics switch
            {
                GraphicsFormat.B8G8R8A8_UNorm or GraphicsFormat.B8G8R8A8_SRGB => TextureFormat.BGRA32,
                _ => GraphicsFormatUtility.StoredTextureFormat(graphics),
            };
            bool srgb = body.Int("m_ColorSpace") == 1 || GraphicsFormatUtility.IsSRGBFormat(graphics);
            var settings = body.Get("m_TextureSettings");
            if (settings != null)
                texture.SetSamplerQuietly((FilterMode)settings.Int("m_FilterMode", 1), (TextureWrapMode)settings.Int("m_WrapU"),
                    (TextureWrapMode)settings.Int("m_WrapV"), (TextureWrapMode)settings.Int("m_WrapW"), settings.Int("m_Aniso", 1),
                    settings.Float("m_MipBias"));
            var hex = body.Str("_typelessdata");
            if (hex.Length == 0) hex = body.Str("image data");
            byte[] data = null;
            if (hex.Length >= 2)
                data = Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex.Substring(0, hex.Length - 1));
            else if (body.Get("m_StreamData") is YNode stream && stream.Str("path").Length > 0)
                Debug.LogWarning($"Unbound: {texture.GetType().Name} '{texture.name}' keeps its pixels in a stream file ({stream.Str("path")}), which is not read yet: it has no pixels.");
            if (data != null && !LayeredPixels.Supported(format))
            {
                Debug.LogWarning($"Unbound: {texture.GetType().Name} '{texture.name}' is stored as {graphics}, which Unbound does not read yet: it has no pixels.");
                data = null;
            }
            return new Serialized(format, srgb, body.Bool("m_IsReadable"), Math.Max(1, body.Int("m_MipCount", 1)), data);
        }

        /// <summary>An imported texture's images, mips and mip bias (<c>texture-layers</c>).</summary>
        internal static (int Layers, int Mips, float MipBias) ImportedLayers(string guid)
        {
            var json = Native.Call("texture-layers", JsonSerializer.Serialize(new { guid }));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (root.GetProperty("layers").GetInt32(), root.GetProperty("mipCount").GetInt32(),
                root.GetProperty("mipBias").GetSingle());
        }

        /// <summary>The CPU copy of an imported readable texture, each image's levels from the
        /// importer (RGBA bytes, or floats stored in the texture's half or float format).</summary>
        internal static LayeredPixels FetchImported(string guid, string name, int width, int height, int layers, int mips, bool volume, TextureFormat format)
        {
            try
            {
                var pixels = new LayeredPixels(width, height, layers, mips, volume, format);
                for (int image = 0; image < pixels.Images; image++)
                    for (int mip = 0; mip < mips; mip++)
                    {
                        var json = Native.Call("texture-pixels", JsonSerializer.Serialize(new { guid, mip, layer = image }));
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var bytes = Convert.FromBase64String(root.GetProperty("rgba").GetString());
                        if (root.TryGetProperty("format", out var f) && f.GetInt32() == 2)
                        {
                            var colors = new Color[pixels.Size(mip)];
                            PixelFormats.DecodeFloat(TextureFormat.RGBAFloat, bytes, colors);
                            pixels.SetPixels(image, mip, colors);
                        }
                        else
                        {
                            pixels.CopyIn(bytes, image, mip);
                        }
                    }
                return pixels;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading the pixels of {name}: {e.Message}");
                return null;
            }
        }

        /// <summary>The bytes of a blittable array from <paramref name="start"/>.</summary>
        internal static unsafe ReadOnlySpan<byte> Bytes<T>(T[] data, int start)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (start < 0 || start > data.Length) throw new ArgumentOutOfRangeException("sourceDataStartIndex");
            int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            var bytes = new byte[(long)(data.Length - start) * size];
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                new ReadOnlySpan<byte>((byte*)pin.AddrOfPinnedObject() + (long)start * size, bytes.Length).CopyTo(bytes);
            }
            finally
            {
                pin.Free();
            }
            return bytes;
        }

        internal static unsafe ReadOnlySpan<byte> Bytes<T>(NativeArray<T> data, int start) where T : struct
        {
            int size = UnsafeUtility.SizeOf<T>();
            if (start < 0 || start > data.Length) throw new ArgumentOutOfRangeException("sourceDataStartIndex");
            return new ReadOnlySpan<byte>((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data) + (long)start * size, (data.Length - start) * size);
        }
    }

    /// <summary>An array of 2D textures of one size and format, sampled by slice.</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] [ClassId(187)]
    public sealed partial class Texture2DArray : Texture, ISerializedAsset
    {
        TextureFormat m_Format;
        bool m_Linear;
        bool m_Readable = true;
        LayeredPixels m_Pixels;
        int m_Depth;
        int m_MipCount = 1;
        /// <summary>An imported texture whose CPU copy the importer gives on first access.</summary>
        string m_AssetGuid;

        [Unbound.EditorFaithful("a constant in Unity too")]
        public static int allSlices => -1;

        Texture2DArray() { }

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
            m_MipCount = mipCount < 0 ? full : Math.Max(1, Math.Min(mipCount, full));
            m_Pixels = new LayeredPixels(width, height, depth, m_MipCount, false, PixelStorage(textureFormat));
        }

        public Texture2DArray(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags) : this(width, height, depth, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public Texture2DArray(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, height, depth, GraphicsFormatUtility.StoredTextureFormat(format), mipCount, !GraphicsFormatUtility.IsSRGBFormat(format)) { }

        public Texture2DArray(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags) { }
        public Texture2DArray(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        /// <summary>Formats Unbound cannot store run as RGBA32 (their pixels are kept as colours).</summary>
        internal static TextureFormat PixelStorage(TextureFormat format) => LayeredPixels.Supported(format) ? format : TextureFormat.RGBA32;

        public int depth => m_Depth;
        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_MipCount;
        public override TextureDimension dimension { get => TextureDimension.Tex2DArray; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear && QualitySettings.activeColorSpace == ColorSpace.Linear);

        LayeredPixels Pixels(string api)
        {
            if (m_Readable && m_Pixels == null && m_AssetGuid != null)
                m_Pixels = LayeredTextures.FetchImported(m_AssetGuid, name, m_Width, m_Height, m_Depth, m_MipCount, false, m_Format);
            if (!m_Readable || m_Pixels == null)
                throw api == null ? LayeredTextures.NoMemoryAccess(name) : LayeredTextures.NotReadable(api, name);
            return m_Pixels;
        }

        LayeredPixels Level(string api, int element, int mip)
        {
            var pixels = Pixels(api);
            if (element < 0 || element >= m_Depth) throw new ArgumentException($"Invalid array element {element}.", nameof(element));
            if (mip < 0 || mip >= pixels.Mips) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return pixels;
        }

        public Color[] GetPixels(int arrayElement, int miplevel) => Level("Texture2DArray.GetPixels", arrayElement, miplevel).GetPixels(arrayElement, miplevel);
        public Color[] GetPixels(int arrayElement) => GetPixels(arrayElement, 0);

        public Color32[] GetPixels32(int arrayElement, int miplevel) => Level("Texture2DArray.GetPixels32", arrayElement, miplevel).GetPixels32(arrayElement, miplevel);
        public Color32[] GetPixels32(int arrayElement) => GetPixels32(arrayElement, 0);

        public void SetPixels(Color[] colors, int arrayElement, int miplevel) => Level("Texture2DArray.SetPixels", arrayElement, miplevel).SetPixels(arrayElement, miplevel, colors);
        public void SetPixels(Color[] colors, int arrayElement) => SetPixels(colors, arrayElement, 0);

        public void SetPixels32(Color32[] colors, int arrayElement, int miplevel) => Level("Texture2DArray.SetPixels32", arrayElement, miplevel).SetPixels32(arrayElement, miplevel, colors);
        public void SetPixels32(Color32[] colors, int arrayElement) => SetPixels32(colors, arrayElement, 0);

        public NativeArray<T> GetPixelData<T>(int mipLevel, int element) where T : struct => Level(null, element, mipLevel).View<T>(element, mipLevel);

        public void SetPixelData<T>(T[] data, int mipLevel, int element, int sourceDataStartIndex = 0) =>
            Level(null, element, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), element, mipLevel);

        public void SetPixelData<T>(NativeArray<T> data, int mipLevel, int element, int sourceDataStartIndex = 0) where T : struct =>
            Level(null, element, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), element, mipLevel);

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable) throw new UnityException($"Texture '{name}' is not readable.");
            var pixels = Pixels("Texture2DArray.Apply");
            if (updateMipmaps) pixels.BuildMips();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            IncrementUpdateCount();
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                pixels.Release();
                m_Pixels = null;
            }
        }

        /// <summary>A Texture2DArray asset; as in Unity, an unreadable one keeps no CPU copy.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = body.Int("m_Width");
            m_Height = body.Int("m_Height");
            m_Depth = Math.Max(1, body.Int("m_Depth", 1));
            var read = LayeredTextures.Read(this, body);
            m_Format = read.Format;
            m_Linear = !read.Srgb;
            m_MipCount = read.Mips;
            m_Readable = read.Readable;
            m_Pixels?.Release();
            m_Pixels = null;
            if (read.Data == null) return;
            var pixels = new LayeredPixels(m_Width, m_Height, m_Depth, m_MipCount, false, m_Format, read.Data);
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            m_Pixels = m_Readable ? pixels : null;
        }

        /// <summary>A texture asset of the Texture2DArray shape the renderer imported under
        /// <paramref name="handle"/>; a readable one gets its CPU copy from the importer when read.</summary>
        internal static Texture2DArray FromImport(ulong handle, string guid, string name, in ImportedTextureInfo info)
        {
            Texture2DArray texture;
            using (ObjectRegistry.AsAssets())
                texture = new Texture2DArray { m_Name = name, IsPersistentAsset = true };
            var (layers, mips, mipBias) = LayeredTextures.ImportedLayers(guid);
            texture.m_NativeHandle = handle;
            texture.m_AssetGuid = guid;
            texture.m_Width = info.Width;
            texture.m_Height = info.Height;
            texture.m_Depth = layers;
            texture.m_MipCount = Math.Max(1, mips);
            texture.m_Readable = info.Readable != 0;
            texture.m_Linear = info.Srgb == 0 || info.Format == 2;
            // HDR sources keep half floats, or float32 with an RGBAFloat override, as 2D textures do.
            texture.m_Format = info.Format switch
            {
                2 => TextureFormat.RGBAHalf,
                9 => TextureFormat.RGBAFloat,
                _ => TextureFormat.RGBA32,
            };
            texture.SetSamplerQuietly((FilterMode)info.FilterMode, (TextureWrapMode)info.WrapU, (TextureWrapMode)info.WrapV,
                (TextureWrapMode)info.WrapW, info.AnisoLevel, mipBias);
            texture.ReadSamplerMetadata();
            return texture;
        }

        internal override void OnEngineDestroy()
        {
            m_Pixels?.Release();
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }

    /// <summary>An array of cube maps of one size and format: six faces each (layer element × 6 +
    /// face), sampled by direction and element.</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] [ClassId(188)]
    public sealed partial class CubemapArray : Texture, ISerializedAsset
    {
        TextureFormat m_Format;
        bool m_Linear;
        bool m_Readable = true;
        LayeredPixels m_Pixels;
        int m_Count;
        int m_MipCount = 1;

        CubemapArray() { }

        public CubemapArray(int width, int cubemapCount, TextureFormat textureFormat, bool mipChain) : this(width, cubemapCount, textureFormat, mipChain ? -1 : 1, false) { }
        public CubemapArray(int width, int cubemapCount, TextureFormat textureFormat, bool mipChain, bool linear) : this(width, cubemapCount, textureFormat, mipChain ? -1 : 1, linear) { }
        public CubemapArray(int width, int cubemapCount, TextureFormat textureFormat, bool mipChain, bool linear, bool createUninitialized) : this(width, cubemapCount, textureFormat, mipChain ? -1 : 1, linear) { }
        public CubemapArray(int width, int cubemapCount, TextureFormat textureFormat, int mipCount, bool linear, bool createUninitialized) : this(width, cubemapCount, textureFormat, mipCount, linear) { }

        public CubemapArray(int width, int cubemapCount, TextureFormat textureFormat, int mipCount, bool linear)
        {
            if (width <= 0 || cubemapCount <= 0)
                throw new UnityException($"Failed to create cubemap array texture because of invalid parameters. Width {width}, cubemap count {cubemapCount}.");
            if (cubemapCount > int.MaxValue / 6)
                throw new UnityException($"Unbound: {cubemapCount} cubemaps have too many faces for a layered texture.");
            m_Width = width;
            m_Height = width;
            m_Count = cubemapCount;
            m_Format = textureFormat;
            m_Linear = linear;
            int full = LayeredPixels.FullMipCount(width, width, 1);
            m_MipCount = mipCount < 0 ? full : Math.Max(1, Math.Min(mipCount, full));
            m_Pixels = new LayeredPixels(width, width, cubemapCount * 6, m_MipCount, false, Texture2DArray.PixelStorage(textureFormat));
        }

        public CubemapArray(int width, int cubemapCount, GraphicsFormat format, TextureCreationFlags flags) : this(width, cubemapCount, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public CubemapArray(int width, int cubemapCount, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, cubemapCount, GraphicsFormatUtility.StoredTextureFormat(format), mipCount, !GraphicsFormatUtility.IsSRGBFormat(format)) { }

        public CubemapArray(int width, int cubemapCount, DefaultFormat format, TextureCreationFlags flags) : this(width, cubemapCount, SystemInfo.GetGraphicsFormat(format), flags) { }
        public CubemapArray(int width, int cubemapCount, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, cubemapCount, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        public int cubemapCount => m_Count;
        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_MipCount;
        public override TextureDimension dimension { get => TextureDimension.CubeArray; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear && QualitySettings.activeColorSpace == ColorSpace.Linear);

        LayeredPixels Level(string api, CubemapFace face, int element, int mip)
        {
            if (!m_Readable || m_Pixels == null)
                throw api == null ? LayeredTextures.NoMemoryAccess(name) : LayeredTextures.NotReadable(api, name);
            if (face < CubemapFace.PositiveX || face > CubemapFace.NegativeZ) throw new ArgumentException($"Invalid cubemap face {face}.", nameof(face));
            if (element < 0 || element >= m_Count) throw new ArgumentException($"Invalid array element {element}.", nameof(element));
            if (mip < 0 || mip >= m_Pixels.Mips) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels;
        }

        static int Image(CubemapFace face, int element) => element * 6 + (int)face;

        public Color[] GetPixels(CubemapFace face, int arrayElement, int miplevel) => Level("CubemapArray.GetPixels", face, arrayElement, miplevel).GetPixels(Image(face, arrayElement), miplevel);
        public Color[] GetPixels(CubemapFace face, int arrayElement) => GetPixels(face, arrayElement, 0);
        public Color32[] GetPixels32(CubemapFace face, int arrayElement, int miplevel) => Level("CubemapArray.GetPixels32", face, arrayElement, miplevel).GetPixels32(Image(face, arrayElement), miplevel);
        public Color32[] GetPixels32(CubemapFace face, int arrayElement) => GetPixels32(face, arrayElement, 0);

        public void SetPixels(Color[] colors, CubemapFace face, int arrayElement, int miplevel) =>
            Level("CubemapArray.SetPixels", face, arrayElement, miplevel).SetPixels(Image(face, arrayElement), miplevel, colors);
        public void SetPixels(Color[] colors, CubemapFace face, int arrayElement) => SetPixels(colors, face, arrayElement, 0);
        public void SetPixels32(Color32[] colors, CubemapFace face, int arrayElement, int miplevel) =>
            Level("CubemapArray.SetPixels32", face, arrayElement, miplevel).SetPixels32(Image(face, arrayElement), miplevel, colors);
        public void SetPixels32(Color32[] colors, CubemapFace face, int arrayElement) => SetPixels32(colors, face, arrayElement, 0);

        public NativeArray<T> GetPixelData<T>(int mipLevel, CubemapFace face, int element) where T : struct =>
            Level(null, face, element, mipLevel).View<T>(Image(face, element), mipLevel);

        public void SetPixelData<T>(T[] data, int mipLevel, CubemapFace face, int element, int sourceDataStartIndex = 0) =>
            Level(null, face, element, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), Image(face, element), mipLevel);

        public void SetPixelData<T>(NativeArray<T> data, int mipLevel, CubemapFace face, int element, int sourceDataStartIndex = 0) where T : struct =>
            Level(null, face, element, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), Image(face, element), mipLevel);

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable || m_Pixels == null) throw new UnityException($"Texture '{name}' is not readable.");
            if (updateMipmaps) m_Pixels.BuildMips();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            m_Pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, TextureDimension.CubeArray);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            IncrementUpdateCount();
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                m_Pixels.Release();
                m_Pixels = null;
            }
        }

        /// <summary>A CubemapArray asset: each cube's six faces (+X, -X, +Y, -Y, +Z, -Z), each
        /// face with its mip chain in turn.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = m_Height = body.Int("m_Width");
            m_Count = Math.Max(1, body.Int("m_CubemapCount", 1));
            var read = LayeredTextures.Read(this, body);
            m_Format = read.Format;
            m_Linear = !read.Srgb;
            m_MipCount = read.Mips;
            m_Readable = read.Readable;
            m_Pixels?.Release();
            m_Pixels = null;
            if (read.Data == null) return;
            var pixels = new LayeredPixels(m_Width, m_Width, m_Count * 6, m_MipCount, false, m_Format, read.Data);
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, TextureDimension.CubeArray);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            m_Pixels = m_Readable ? pixels : null;
        }

        internal override void OnEngineDestroy()
        {
            m_Pixels?.Release();
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }

    /// <summary>A 3D texture: width × height × depth texels, sampled in three coordinates.</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] [ClassId(117)]
    public sealed partial class Texture3D : Texture, ISerializedAsset
    {
        TextureFormat m_Format;
        bool m_Linear = true;
        bool m_Readable = true;
        LayeredPixels m_Pixels;
        int m_Depth;
        int m_MipCount = 1;
        /// <summary>An imported texture whose CPU copy the importer gives on first access.</summary>
        string m_AssetGuid;

        Texture3D() { }

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
            m_MipCount = mipCount < 0 ? full : Math.Max(1, Math.Min(mipCount, full));
            m_Pixels = new LayeredPixels(width, height, depth, m_MipCount, true, Texture2DArray.PixelStorage(textureFormat));
        }

        public Texture3D(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags) : this(width, height, depth, format, flags, (flags & TextureCreationFlags.MipChain) != 0 ? -1 : 1) { }

        public Texture3D(int width, int height, int depth, GraphicsFormat format, TextureCreationFlags flags, int mipCount)
            : this(width, height, depth, GraphicsFormatUtility.StoredTextureFormat(format), mipCount) => m_Linear = !GraphicsFormatUtility.IsSRGBFormat(format);

        public Texture3D(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags) { }
        public Texture3D(int width, int height, int depth, DefaultFormat format, TextureCreationFlags flags, int mipCount) : this(width, height, depth, SystemInfo.GetGraphicsFormat(format), flags, mipCount) { }

        public int depth => m_Depth;
        public TextureFormat format => m_Format;
        public override bool isReadable => m_Readable;
        internal override int MipmapCount() => m_MipCount;
        public override TextureDimension dimension { get => TextureDimension.Tex3D; set => throw new NotImplementedException(); }
        internal override GraphicsFormat GraphicsFormatOf() => GraphicsFormatUtility.GetGraphicsFormat(m_Format, !m_Linear && QualitySettings.activeColorSpace == ColorSpace.Linear);

        LayeredPixels Level(string api, int mip)
        {
            if (m_Readable && m_Pixels == null && m_AssetGuid != null)
                m_Pixels = LayeredTextures.FetchImported(m_AssetGuid, name, m_Width, m_Height, m_Depth, m_MipCount, true, m_Format);
            if (!m_Readable || m_Pixels == null)
                throw api == null ? LayeredTextures.NoMemoryAccess(name) : LayeredTextures.NotReadable(api, name);
            if (mip < 0 || mip >= m_Pixels.Mips) throw new ArgumentException($"Invalid mip level {mip}.", nameof(mip));
            return m_Pixels;
        }

        public Color[] GetPixels(int miplevel) => Level("Texture3D.GetPixels", miplevel).GetPixels(0, miplevel);
        public Color[] GetPixels() => GetPixels(0);
        public Color32[] GetPixels32(int miplevel) => Level("Texture3D.GetPixels32", miplevel).GetPixels32(0, miplevel);
        public Color32[] GetPixels32() => GetPixels32(0);

        public void SetPixels(Color[] colors, int miplevel) => Level("Texture3D.SetPixels", miplevel).SetPixels(0, miplevel, colors);
        public void SetPixels(Color[] colors) => SetPixels(colors, 0);
        public void SetPixels32(Color32[] colors, int miplevel) => Level("Texture3D.SetPixels32", miplevel).SetPixels32(0, miplevel, colors);
        public void SetPixels32(Color32[] colors) => SetPixels32(colors, 0);

        public NativeArray<T> GetPixelData<T>(int mipLevel) where T : struct => Level(null, mipLevel).View<T>(0, mipLevel);

        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) =>
            Level(null, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), 0, mipLevel);

        public void SetPixelData<T>(NativeArray<T> data, int mipLevel, int sourceDataStartIndex = 0) where T : struct =>
            Level(null, mipLevel).CopyIn(LayeredTextures.Bytes(data, sourceDataStartIndex), 0, mipLevel);

        public void SetPixel(int x, int y, int z, Color color) => SetPixel(x, y, z, color, 0);

        public void SetPixel(int x, int y, int z, Color color, int mipLevel)
        {
            var pixels = Level("Texture3D.SetPixel", mipLevel);
            int w = pixels.W(mipLevel), h = pixels.H(mipLevel), d = pixels.D(mipLevel);
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) return;
            pixels.SetPixel(0, mipLevel, (z * h + y) * w + x, color);
        }

        public Color GetPixel(int x, int y, int z) => GetPixel(x, y, z, 0);

        public Color GetPixel(int x, int y, int z, int mipLevel)
        {
            var pixels = Level("Texture3D.GetPixel", mipLevel);
            int w = pixels.W(mipLevel), h = pixels.H(mipLevel), d = pixels.D(mipLevel);
            x = Mathf.Clamp(x, 0, w - 1);
            y = Mathf.Clamp(y, 0, h - 1);
            z = Mathf.Clamp(z, 0, d - 1);
            return pixels.GetPixel(0, mipLevel, (z * h + y) * w + x);
        }

        public void Apply() => Apply(true, false);
        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!m_Readable) throw new UnityException($"Texture '{name}' is not readable.");
            var pixels = Level("Texture3D.Apply", 0);
            if (updateMipmaps) pixels.BuildMips();
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            IncrementUpdateCount();
            if (makeNoLongerReadable)
            {
                m_Readable = false;
                pixels.Release();
                m_Pixels = null;
            }
        }

        /// <summary>A Texture3D asset: its levels one after another, each all its depth slices.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Width = body.Int("m_Width");
            m_Height = body.Int("m_Height");
            m_Depth = Math.Max(1, body.Int("m_Depth", 1));
            var read = LayeredTextures.Read(this, body);
            m_Format = read.Format;
            m_Linear = !read.Srgb;
            m_MipCount = read.Mips;
            m_Readable = read.Readable;
            m_Pixels?.Release();
            m_Pixels = null;
            if (read.Data == null) return;
            var pixels = new LayeredPixels(m_Width, m_Height, m_Depth, m_MipCount, true, m_Format, read.Data);
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            pixels.Upload(m_NativeHandle, m_Linear, filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel);
            if (mipMapBias != 0f) EmitSamplerMetadata();
            m_Pixels = m_Readable ? pixels : null;
        }

        /// <summary>A texture asset of the Texture3D shape the renderer imported under
        /// <paramref name="handle"/>; a readable one gets its CPU copy from the importer when read.</summary>
        internal static Texture3D FromImport(ulong handle, string guid, string name, in ImportedTextureInfo info)
        {
            Texture3D texture;
            using (ObjectRegistry.AsAssets())
                texture = new Texture3D { m_Name = name, IsPersistentAsset = true };
            var (layers, mips, mipBias) = LayeredTextures.ImportedLayers(guid);
            texture.m_NativeHandle = handle;
            texture.m_AssetGuid = guid;
            texture.m_Width = info.Width;
            texture.m_Height = info.Height;
            texture.m_Depth = layers;
            texture.m_MipCount = Math.Max(1, mips);
            texture.m_Readable = info.Readable != 0;
            texture.m_Linear = info.Srgb == 0 || info.Format == 2;
            // HDR sources keep half floats, or float32 with an RGBAFloat override, as 2D textures do.
            texture.m_Format = info.Format switch
            {
                2 => TextureFormat.RGBAHalf,
                9 => TextureFormat.RGBAFloat,
                _ => TextureFormat.RGBA32,
            };
            texture.SetSamplerQuietly((FilterMode)info.FilterMode, (TextureWrapMode)info.WrapU, (TextureWrapMode)info.WrapV,
                (TextureWrapMode)info.WrapW, info.AnisoLevel, mipBias);
            texture.ReadSamplerMetadata();
            return texture;
        }

        internal override void OnEngineDestroy()
        {
            m_Pixels?.Release();
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
        }
    }
}
