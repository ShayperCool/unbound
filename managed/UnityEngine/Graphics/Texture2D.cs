using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        /// <summary>Mip levels of the renderer's copy, kept for sampler-only updates.</summary>
        int m_GpuMipCount = 1;
        internal bool m_IsReadable;
        internal bool m_Linear;
        /// <summary>CPU pixels of a texture made by code, bottom row first.</summary>
        internal Color32[] m_Pixels;
        /// <summary>The CPU pixels instead of <see cref="m_Pixels"/> for floating point formats
        /// (RGBAHalf, RFloat, ...): their values are not bytes.</summary>
        internal Color[] m_Hdr;
        /// <summary>A serialized texture's mip chain in its own format (Alpha8: a byte a pixel), kept
        /// instead of <see cref="m_Pixels"/> until a script reads or writes pixels.</summary>
        byte[] m_Raw;
        byte[] m_SourceRaw;
        internal long RawBytes => (m_Raw?.LongLength ?? 0) + (m_UnreadableRaw?.LongLength ?? 0) + (m_SourceRaw?.LongLength ?? 0) + (m_Store?.LongLength ?? 0);
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
            // A format the device cannot sample is refused: the texture is left destroyed (== null),
            // as the Editor's (measured for A2B10G10R10_UNormPack32 and integer formats on its
            // OpenGLCore device, tests/projects/textureformats .new).
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
            {
                Debug.LogError($"Texture creation failed. '{format}' is not supported for Sample usage on this platform. Use 'SystemInfo.IsFormatSupported' C# API to check format support.");
                MarkDestroyed();
                return;
            }
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
            CheckMainThreadCreation("SupportsTextureFormatNative");
            if (width <= 0 || height <= 0)
                throw new UnityException($"Failed to create texture because of invalid parameters. Width {width}, height {height}.");
            // Block formats need block-sized sides (checked before anything changes).
            if (!BlockSized(format, width, height))
                throw new UnityException("Failed to create texture because of invalid parameters.");
            ReleaseStore();
            m_Width = width;
            m_Height = height;
            m_Format = format;
            int fullMips = LayeredPixels.FullMipCount(width, height, 1);
            if (mipCount > fullMips) throw new UnityException("Failed to create texture because its mip count exceeds its dimensions.");
            m_MipCount = mipCount <= 0 ? fullMips : mipCount;
            m_Linear = linear;
            m_IsReadable = true;
            m_Raw = null;
            m_MipPixels = null;
            m_MipHdr = null;
            m_UnreadableRaw = null;
            m_SourceFormat = null;
            m_SourceMips = 0;
            m_SourceRaw = null;
            m_BlockCellHashes = null;
            m_AssetGuid = null;
            if (BlockFormats.BlockBytes(format) > 0)
            {
                InitBlocks(format);
                return;
            }
            // A new texture is grey (205,205,205,205) until written, as in Unity.
            if (Texture2DTexels.FloatStored(format))
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

        /// <summary>The texture <c>ScreenCapture.CaptureScreenshotAsTexture</c> returns for a size
        /// below one: ARGB32, linear, readable, 0x0 and without pixels (tests/projects/screencapture).</summary>
        internal static Texture2D EmptyCapture()
        {
            var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false, true);
            texture.m_Width = 0;
            texture.m_Height = 0;
            texture.m_Pixels = null;
            texture.m_PixelsDirty = false;
            return texture;
        }

        public TextureFormat format => m_SourceFormat ?? m_Format;

        /// <summary>A serialized texture's own format and mip count when Unbound keeps its pixels in
        /// another (an AssetBundle's block-compressed texture, decoded).</summary>
        TextureFormat? m_SourceFormat;
        int m_SourceMips;
        internal override GraphicsFormat GraphicsFormatOf() =>
            GraphicsFormatUtility.Texture2DGraphicsFormat(format, !m_Linear && QualitySettings.activeColorSpace == ColorSpace.Linear);
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

        /// <summary>The mips the quality level's limit drops from the top (none for a texture that
        /// ignores it or has no mips). Unbound still samples the full texture: it warns once.</summary>
        public int activeMipmapLimit
        {
            get
            {
                if (ignoreMipmapLimit || MipmapCount() <= 1) return 0;
                int limit = Math.Min(QualitySettings.globalTextureMipmapLimit, MipmapCount() - 1);
                if (limit > 0) Unbound.Runtime.Stubs.Ignored("QualitySettings.globalTextureMipmapLimit", "textures are sampled from their first mip whatever the limit");
                return limit;
            }
        }

        public static Texture2D whiteTexture => DefaultTextures.White;
        public static Texture2D blackTexture => DefaultTextures.Black;
        public static Texture2D redTexture => DefaultTextures.Red;
        public static Texture2D grayTexture => DefaultTextures.Gray;
        public static Texture2D linearGrayTexture => DefaultTextures.Gray;
        public static Texture2D normalTexture => DefaultTextures.Normal;

        /// <summary>The cells for an engine path that reads or writes <see cref="m_Pixels"/> itself:
        /// decoded from the raw data when only that is kept, taken back from a pixel data store.</summary>
        internal Color32[] EnsurePixels()
        {
            DecodeRaw();
            RefreshBlockCells();
            LeaveStore();
            return m_Pixels;
        }

        void DecodeRaw()
        {
            if (m_Raw != null)
            {
                var raw = m_Raw;
                m_Raw = null;
                DecodeMipChain(raw);
            }
        }

        /// <summary>Throws unless the texture is readable; makes its pixel cells available unless
        /// `pixels` is false (raw-data calls of a compressed texture made by code need its blocks
        /// alone).</summary>
        void RequireReadable(bool pixelArray = false, bool pixels = true)
        {
            if (!m_IsReadable)
            {
                string message = $"Texture '{name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.";
                if (pixelArray) throw new ArgumentException(message);
                throw new UnityException(message);
            }
            DecodeRaw();
            // Whole-level reads see blocks written through views (single texels read them from
            // the blocks directly, Read).
            if (pixelArray) RefreshBlockCells();
            if (m_Pixels == null && m_Hdr == null)
            {
                // A compressed texture made by code decodes its blocks when its pixels are read.
                if (SourceBlocks && m_AssetGuid == null)
                {
                    if (pixels) DecodeAllBlocks();
                    return;
                }
                if (m_IsReadable && m_AssetGuid != null) FetchImportedPixels();
                if (m_Pixels == null && m_Hdr == null)
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
                texture.m_GpuMipCount = texture.m_MipCount;
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
            DecodeRaw();
            SyncFromStore();
            RefreshBlockCells();
            if (m_Pixels == null && m_Hdr == null && m_UnreadableRaw != null && m_Store == null)
            {
                // Made non-readable by Apply: decode the kept data for the engine, leaving the
                // texture unreadable to scripts.
                DecodeMipChain(m_UnreadableRaw);
                var decoded = m_Pixels;
                var decodedHdr = m_Hdr;
                m_Pixels = null;
                m_Hdr = null;
                m_MipPixels = null;
                m_MipHdr = null;
                if (decoded != null) return decoded;
                if (decodedHdr != null)
                {
                    var converted = new Color32[decodedHdr.Length];
                    for (int i = 0; i < converted.Length; i++) converted[i] = decodedHdr[i];
                    return converted;
                }
            }
            if (m_Pixels == null && m_Hdr == null && DecodeTopBlocks() is { } blockPixels) return blockPixels;
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
                for (int mip = 0; mip < m_MipCount; mip++)
                {
                    var json = Native.Call("texture-pixels", JsonSerializer.Serialize(new { guid = m_AssetGuid, mip }));
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var bytes = Convert.FromBase64String(root.GetProperty("rgba").GetString());
                    if (root.TryGetProperty("format", out var format) && format.GetInt32() == 2)
                    {
                        m_Hdr ??= new Color[m_Width * m_Height];
                        m_Pixels = null;
                        PixelFormats.DecodeFloat(TextureFormat.RGBAFloat, bytes, FloatLevel(mip));
                    }
                    else
                    {
                        m_Pixels ??= new Color32[m_Width * m_Height];
                        PixelFormats.Decode(TextureFormat.RGBA32, bytes, ByteLevel(mip));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading the pixels of {name}: {e.Message}");
            }
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            // A block-compressed texture uploads its blocks: its pixels need not be decoded.
            RequireReadable(pixels: !SourceBlocks);
            // A block-compressed texture's levels are its blocks: nothing regenerates them.
            if (updateMipmaps && !SourceBlocks) GenerateMipChain();
            var rawCopy = makeNoLongerReadable && !SourceBlocks ? EncodeMipChain() : null;
            m_PixelsDirty = true;
            Upload();
            if (makeNoLongerReadable)
            {
                ReleaseStore();
                m_Pixels = null;
                m_Hdr = null;
                m_Raw = null;
                m_MipPixels = null;
                m_MipHdr = null;
                m_UnreadableRaw = rawCopy;
                m_IsReadable = false;
            }
        }

        public void Apply(bool updateMipmaps) => Apply(updateMipmaps, false);

        public void Apply() => Apply(true, false);

        public bool Reinitialize(int width, int height) => Reinitialize(width, height, format, MipmapCount() > 1);

        public bool Reinitialize(int width, int height, TextureFormat format, bool hasMipMap)
        {
            MainThreadCheck.Check("Reinitialize");
            // A block format refuses sides that are not block-sized, leaving the texture as it was
            // (tests/projects/texturecompression reinit.odd).
            if (!BlockSized(format, width, height)) return false;
            Init(width, height, format, hasMipMap ? -1 : 1, m_Linear);
            return true;
        }

        public bool Reinitialize(int width, int height, GraphicsFormat format, bool hasMipMap)
        {
            MainThreadCheck.Check("Reinitialize");
            if (!BlockSized(TextureFormatOf(format), width, height)) return false;
            Init(width, height, TextureFormatOf(format), hasMipMap ? -1 : 1, !GraphicsFormatUtility.IsSRGBFormat(format));
            return true;
        }

        [Obsolete("Texture2D.Resize(int, int) has been deprecated because it actually reinitializes the texture. Use Texture2D.Reinitialize(int, int) instead (UnityUpgradable) -> Reinitialize([*] System.Int32, [*] System.Int32)", false)]
        public bool Resize(int width, int height) => Reinitialize(width, height);

        [Obsolete("Texture2D.Resize(int, int, TextureFormat, bool) has been deprecated because it actually reinitializes the texture. Use Texture2D.Reinitialize(int, int, TextureFormat, bool) instead (UnityUpgradable) -> Reinitialize([*] System.Int32, [*] System.Int32, UnityEngine.TextureFormat, [*] System.Boolean)", false)]
        public bool Resize(int width, int height, TextureFormat format, bool hasMipMap) => Reinitialize(width, height, format, hasMipMap);

        public void Compress(bool highQuality) => CompressToBlocks(highQuality);

        /// <summary>Copies a rectangle of the active render target (the screen when none) into the
        /// texture's CPU pixels at (destX, destY), after everything drawn so far.</summary>
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps)
        {
            RequireReadable();
            // The active target as drawn into: a screen camera's stand-in while it renders (what
            // the screen shows once it is presented).
            var active = RenderTexture.ActiveTarget;
            // The level and layer Graphics.SetRenderTarget bound (a cube face, an array slice).
            ulong activeHandle = active != null ? active.EnsureCreated() : 0;
            var bound = GfxStream.Current;
            bool level = active != null && bound.Color == activeHandle && (bound.Mip > 0 || bound.Slice > 0);
            int mip = level ? bound.Mip : 0, layer = level ? Math.Max(0, bound.Slice) : 0;
            int sourceWidth = active != null ? Math.Max(1, active.width >> mip) : Screen.width;
            int sourceHeight = active != null ? Math.Max(1, active.height >> mip) : Screen.height;
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
            // Outside the frame's drawing the Editor reads its window's framebuffer (measured in
            // Update: tests/projects/screencapture read.update), with an error.
            if (active == null && Camera.current == null && !FrameBuilder.InDrawingFrame)
                Debug.LogError("ReadPixels was called to read pixels from system frame buffer, while not inside drawing frame.");
            // The Editor's OpenGLCore device cannot read an integer target (a GL error; the texels
            // stay as they were: tests/projects/textureformats).
            if (active != null && RenderTexture.IntegerPacketFormat(active.graphicsFormat) >= 0)
            {
                Debug.LogError($"Unbound: ReadPixels cannot read the integer render texture '{active.name}' ({active.graphicsFormat}); the texture is left unchanged.");
                return;
            }
            var bytes = level ? Native.ReadTexture(activeHandle, mip, layer, x0, y0, w, h, true, currentAttachment: true)
                : Native.ReadPixels(activeHandle, x0, y0, w, h, true);
            if (bytes == null)
            {
                Debug.LogWarning("Unbound: Texture2D.ReadPixels has nothing to read (a headless run, or no screen of this size yet); the texture is left unchanged.");
                return;
            }
            // ReadPixels copies attachment values, including float-to-byte reads, regardless
            // of the destination's linear flag; the screen's are its 8-bit (in Linear,
            // sRGB-encoded) bytes.
            bool eightBit = active != null && ReadsAsBytes(active.graphicsFormat);
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
                            if (eightBit)
                                for (int k = 0; k < 4; k++) c[k] = (int)(Mathf.Clamp01(c[k]) * 255f + 0.5f) * (1f / 255f);
                            Write(ty * m_Width + tx, c);
                        }
                    }
                }
            }
            if (recalculateMipMaps) GenerateMipChain();
            CpuTexelsChanged();
        }

        public void ReadPixels(Rect source, int destX, int destY) => ReadPixels(source, destX, destY, true);

        /// <summary>Whether ReadPixels from a target of <paramref name="source"/> reads 8-bit
        /// channels (b × (1 / 255) in a float texture): every target that is neither a float format
        /// nor the texture's own format. Measured on the Editor's OpenGLCore device
        /// (tests/projects/textureformats .read, .read64): R16, RG32, ARGB64 and ARGB2101010 targets
        /// read into RGBAFloat at 8 bits, ARGB64 into RGBA64 at 16, float targets at full precision.</summary>
        bool ReadsAsBytes(GraphicsFormat source) =>
            !GraphicsFormatUtility.IsHDRFormat(source) && !GraphicsFormatUtility.IsIEEE754Format(source)
            && source != GraphicsFormatOf();

        public Rect[] PackTextures(Texture2D[] textures, int padding, int maximumAtlasSize, bool makeNoLongerReadable) =>
            TexturePacker.Pack(this, textures, padding, maximumAtlasSize, makeNoLongerReadable);

        public Rect[] PackTextures(Texture2D[] textures, int padding, int maximumAtlasSize) => PackTextures(textures, padding, maximumAtlasSize, false);

        public Rect[] PackTextures(Texture2D[] textures, int padding) => PackTextures(textures, padding, 2048, false);

        /// <summary>The native texture an external texture shows (CreateExternalTexture,
        /// UpdateExternalTexture); zero for other textures. Its texels are the native texture's:
        /// nothing is uploaded from here.</summary>
        IntPtr m_ExternalTexture;

        /// <summary>A texture showing a native texture: one a plugin made, or another texture's
        /// <see cref="Texture.GetNativeTexturePtr"/> (the same texels). The format and color
        /// space given here describe this texture (<c>format</c>, <c>graphicsFormat</c>); the
        /// native texture is sampled in its own. Matches the Unity 6000.0.71f1 Editor
        /// (tests/projects/nativegfx external.*, ext.*).</summary>
        public static Texture2D CreateExternalTexture(int width, int height, TextureFormat format, bool mipChain, bool linear, IntPtr nativeTex)
        {
            if (nativeTex == IntPtr.Zero) throw new ArgumentException("nativeTex can not be null");
            var texture = new Texture2D(width, height, format, mipChain, linear);
            texture.UpdateExternalTexture(nativeTex);
            return texture;
        }

        public unsafe void UpdateExternalTexture(IntPtr nativeTex)
        {
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            m_ExternalTexture = nativeTex;
            m_PixelsDirty = false;
            if (!Native.IsReady) return;
            GfxStream.Flush();
            // The native texture decides the size, the format and whether its texels are sRGB;
            // this texture, through how many of its levels it is sampled.
            var error = new NativeBuffer();
            if (Native.Api.ExternalTexture(m_NativeHandle, (ulong)nativeTex, mipmapCount, &error) != 0)
            {
                var reason = error.Length == 0 ? "unknown error" : System.Text.Encoding.UTF8.GetString(error.Data, (int)error.Length);
                Native.Free(error);
                Debug.LogError($"Unbound: the external texture {nativeTex} cannot be shown: {reason}");
                return;
            }
            EmitSamplingProperties();
        }

        internal override IntPtr NativePointer() =>
            m_ExternalTexture != IntPtr.Zero ? m_ExternalTexture : NativePlugins.TexturePointer(EnsureUploaded(), false);

        internal override void OnSamplerChanged() => EmitSamplingProperties();

        /// <summary>Records a CPU texel write (SetPixel(s), SetPixels32, SetPixelData,
        /// LoadRawTextureData, ReadPixels). It changes only the retained CPU storage: once the
        /// texture has GPU storage its texels change through Apply or GPU copies, not at a later
        /// use. Measured on Unity 6000.0.71f1 for SetPixel (tests/projects/assetclones
        /// texture.dirty.source.gpu, sampled through Blit) and SetPixels (tests/projects/samplerbias
        /// cache/pixels/unapplied, sampled through a material). A texture without GPU storage
        /// still uploads its current CPU texels at first use (<see cref="EnsureUploaded"/>).</summary>
        void CpuTexelsChanged()
        {
            if (m_NativeHandle == 0) m_PixelsDirty = true;
        }

        /// <summary>The renderer handle, uploading pending pixels first.</summary>
        internal ulong EnsureUploaded()
        {
            if (m_NativeHandle == 0 && (m_Pixels != null || m_Raw != null || m_Hdr != null || SourceBlocks)) m_PixelsDirty = true;
            if (m_PixelsDirty) Upload();
            return m_NativeHandle;
        }

        unsafe void Upload()
        {
            if (m_ExternalTexture != IntPtr.Zero)
            {
                // The native texture holds the texels.
                m_PixelsDirty = false;
                return;
            }
            if (SourceBlocks && CurrentBlocks() != null)
            {
                // A block-compressed texture uploads its blocks (also those written by scripts).
                if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
                m_PixelsDirty = false;
                updateCount++;
                UploadBlocks();
                DropIfNotReadable();
                return;
            }
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

        /// <summary>Drop decoded arrays for non-readable textures. The Editor's byte-array
        /// GetRawTextureData still returns their data, unlike the pixel APIs (resources probe).</summary>
        void DropIfNotReadable()
        {
            if (m_IsReadable) return;
            // A block-compressed texture keeps its blocks (GetRawTextureData answers them).
            if (!SourceBlocks) m_UnreadableRaw ??= m_Raw != null ? (byte[])m_Raw.Clone() : EncodeMipChain();
            ReleaseStore();
            m_Pixels = null;
            m_Hdr = null;
            m_Raw = null;
            m_MipPixels = null;
            m_MipHdr = null;
            m_GpuTexels = null;
        }

        /// <summary>RGBA float wire data; the renderer stores the declared component format.</summary>
        unsafe void UploadHdr()
        {
            m_GpuMipCount = m_MipCount;
            var pixels = UploadHdrChain();
            fixed (Color* p = pixels)
            {
                var packet = new TexturePacket
                {
                    Width = m_Width,
                    Height = m_Height,
                    Format = PixelFormats.GpuFloatFormat(m_Format),
                    MipCount = m_MipCount,
                    Data = (byte*)p,
                    DataLength = pixels.Length * 16,
                    FilterMode = (int)filterMode,
                    WrapU = (int)wrapModeU,
                    WrapV = (int)wrapModeV,
                    WrapW = (int)wrapModeW,
                    AnisoLevel = anisoLevel,
                };
                UploadTexture(&packet);
            }
        }

        /// <summary>Uploads the raw data: formats the renderer keeps as stored (RGBA32, Alpha8,
        /// and R8 and RG16 in their channels, <see cref="GpuByteFormat"/>) as they are, other
        /// formats decoded.</summary>
        unsafe void UploadRaw()
        {
            m_GpuMipCount = m_MipCount;
            int format = GpuByteFormat();
            if (m_Format != TextureFormat.RGBA32 && PixelFormats.GpuTexelBytes(format) == 4)
            {
                DecodeRaw();
                if (m_Hdr != null) UploadHdr();
                else UploadPixels();
                return;
            }
            fixed (byte* data = m_Raw) UploadByteTexels(format, data, m_Raw.Length);
        }

        unsafe void UploadSampler()
        {
            var packet = new TexturePacket
            {
                Width = m_Width,
                Height = m_Height,
                // The format the texels went up in: a different one would make a new, empty texture.
                Format = Texture2DTexels.FloatStored(m_Format) ? PixelFormats.GpuFloatFormat(m_Format) : GpuByteFormat(),
                MipCount = m_GpuMipCount,
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                WrapW = (int)wrapModeW,
                AnisoLevel = anisoLevel,
            };
            UploadTexture(&packet);
        }

        /// <summary>The renderer's format code for 8-bit texels: Alpha8 alone, R8 and RG16 in one
        /// and two channels unless sampled through an sRGB view
        /// (<see cref="PixelFormats.GpuChannelFormat"/>), other formats as RGBA8.</summary>
        internal int GpuByteFormat()
        {
            if (m_Format == TextureFormat.Alpha8) return PixelFormats.GpuAlpha8;
            int channels = PixelFormats.GpuChannelFormat(m_Format, m_Linear);
            return channels >= 0 ? channels : m_Linear ? 1 : 0;
        }

        /// <summary>The one- and two-channel upload texels of the last <see cref="UploadPixels"/>.</summary>
        byte[] m_GpuTexels;

        unsafe void UploadPixels()
        {
            m_GpuMipCount = m_MipCount;
            var pixels = UploadByteChain();
            int format = GpuByteFormat();
            int bytes = PixelFormats.GpuTexelBytes(format);
            if (bytes < 4)
            {
                // Reused across uploads (textures applied every frame).
                if (m_GpuTexels?.Length != pixels.Length * bytes) m_GpuTexels = new byte[pixels.Length * bytes];
                PixelFormats.GpuTexels(format, pixels, m_GpuTexels);
                fixed (byte* t = m_GpuTexels) UploadByteTexels(format, t, m_GpuTexels.Length);
                return;
            }
            if (PixelFormats.SamplesBlueOne(m_Format, format))
            {
                if (ReferenceEquals(pixels, m_Pixels)) pixels = (Color32[])pixels.Clone();
                for (int i = 0; i < pixels.Length; i++) pixels[i].b = 255;
            }
            fixed (Color32* p = pixels) UploadByteTexels(format, (byte*)p, pixels.Length * 4);
        }

        unsafe void UploadByteTexels(int format, byte* data, int length)
        {
            var packet = new TexturePacket
            {
                Width = m_Width,
                Height = m_Height,
                Format = format,
                MipCount = m_MipCount,
                Data = data,
                DataLength = length,
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                WrapW = (int)wrapModeW,
                AnisoLevel = anisoLevel,
            };
            UploadTexture(&packet);
        }

        /// <summary>An asset texture: the native side imports it into a new handle.</summary>
        /// <summary>A texture the renderer imported under <paramref name="handle"/>.</summary>
        internal static Texture2D FromImport(ulong handle, string guid, string name, in ImportedTextureInfo info)
        {
            Texture2D texture;
            using (ObjectRegistry.AsAssets())
                texture = new Texture2D { m_Name = name, IsPersistentAsset = true, m_AssetGuid = guid, m_ImportedFrom = guid };
            texture.m_NativeHandle = handle;
            texture.ApplyImport(info);
            return texture;
        }

        /// <summary>The texture file this texture was imported from. Unlike <see cref="m_AssetGuid"/>
        /// it stays when a script reinitializes the texture: unloading reads the file again.</summary>
        string m_ImportedFrom;

        /// <summary>Imports a texture file again under a renderer handle (tests replace it).</summary>
        internal static Func<ulong, string, (bool Ok, ImportedTextureInfo Info)> Reimport =
            (handle, guid) => (Native.ImportTexture(handle, guid, out var info), info);

        /// <summary>The shape, readability and sampler an import gives.</summary>
        void ApplyImport(in ImportedTextureInfo info)
        {
            m_Width = info.Width;
            m_Height = info.Height;
            m_MipCount = info.MipCount;
            m_GpuMipCount = Math.Max(1, info.MipCount);
            m_IsReadable = info.Readable != 0;
            m_Linear = info.Srgb == 0 || info.Format == 2;
            m_Format = PixelFormats.ImportedFormat(info);
            // A block-compressed import reports its format and keeps its blocks; its pixels are
            // what the blocks decode to.
            if (BlockFormats.BlockBytes((TextureFormat)info.TextureFormat) > 0)
            {
                m_SourceFormat = (TextureFormat)info.TextureFormat;
                m_SourceMips = info.MipCount;
            }
            m_WrapModeW = (TextureWrapMode)info.WrapW;
            filterMode = (FilterMode)info.FilterMode;
            wrapModeU = (TextureWrapMode)info.WrapU;
            wrapModeV = (TextureWrapMode)info.WrapV;
            anisoLevel = info.AnisoLevel;
            ReadSamplerMetadata();
        }

        /// <summary>
        /// Resources.UnloadAsset of an imported texture (UnloadUnusedAssets keeps textures: it
        /// cannot load one back lazily): the file is imported again into the same renderer handle,
        /// so materials and sprites that bind the texture draw the saved texels. What scripts did
        /// to it (pixels, size, format, readability, sampler settings) is gone; the CPU pixels are
        /// read from the import when a script next asks for them, as after loading. The object
        /// stays the same one (as measured for materials, tests/projects/droppedparams
        /// unload.material; textures themselves are unmeasured). False, with nothing changed, for a
        /// texture no texture file made or a file that cannot be imported again. False too, with a
        /// warning, when the file now imports as a cubemap, an array or a 3D texture (its import
        /// settings changed while running): the renderer's data under the handle is already that
        /// new import, but this Texture2D keeps its own size, format and pixels.
        /// </summary>
        internal bool ReloadImported()
        {
            if (m_ImportedFrom == null) return false;
            ulong handle = m_NativeHandle != 0 ? m_NativeHandle : RenderHandles.Next();
            var (ok, info) = Reimport(handle, m_ImportedFrom);
            if (!ok) return false;
            if (info.Dimension is 3 or 4 or 5)
            {
                Debug.LogWarning($"Unbound: unloading texture '{m_Name}': its file now imports as a {(UnityEngine.Rendering.TextureDimension)info.Dimension} texture, not a Texture2D; its renderer data was replaced, the Texture2D was not.");
                return false;
            }
            ReleaseStore();
            m_Pixels = null;
            m_Hdr = null;
            m_Raw = null;
            m_SourceRaw = null;
            m_UnreadableRaw = null;
            m_MipPixels = null;
            m_MipHdr = null;
            m_SourceFormat = null;
            m_SourceMips = 0;
            m_BlockCellHashes = null;
            m_PixelsDirty = false;
            m_AssetGuid = m_ImportedFrom;
            m_NativeHandle = handle;
            // The mip bias is the import's again (read back from the renderer, 0 without one).
            SetSamplerQuietly(filterMode, wrapModeU, wrapModeV, m_WrapModeW, anisoLevel, 0f);
            ApplyImport(info);
            return true;
        }

        /// <summary>A texture serialized in an asset file (TextMeshPro atlases): pixels as hex.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReleaseStore();
            m_Width = body.Int("m_Width");
            m_Height = body.Int("m_Height");
            m_Format = (TextureFormat)body.Int("m_TextureFormat", (int)TextureFormat.RGBA32);
            m_MipCount = Math.Max(1, body.Int("m_MipCount", 1));
            m_MipPixels = null;
            m_MipHdr = null;
            m_SourceFormat = body.Get("m_SourceTextureFormat") is YNode source ? (TextureFormat)source.AsInt() : null;
            m_SourceMips = body.Int("m_SourceMipCount", body.Int("m_MipCount", 1));
            string encoded = body.Str("m_SourceTextureData");
            m_SourceRaw = encoded.Length > 0 ? Convert.FromHexString(encoded) : null;
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
                int levelSize = Texture2DTexels.Size(m_Format, m_Width, m_Height);
                if (levelSize > 0 && bytes.Length >= levelSize)
                {
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
            ReleaseStore();
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
        /// <summary>The format an imported texture reports: the one the import names (Alpha8, R8,
        /// RGB24: what the Editor reports for automatic uncompressed formats), else its data's
        /// (the target override may keep HDR working pixels as float32 instead of half).</summary>
        internal static UnityEngine.TextureFormat ImportedFormat(in ImportedTextureInfo info) => info.TextureFormat switch
        {
            (int)UnityEngine.TextureFormat.Alpha8 => UnityEngine.TextureFormat.Alpha8,
            (int)UnityEngine.TextureFormat.R8 => UnityEngine.TextureFormat.R8,
            (int)UnityEngine.TextureFormat.RG16 => UnityEngine.TextureFormat.RG16,
            (int)UnityEngine.TextureFormat.RGBA32 => UnityEngine.TextureFormat.RGBA32,
            (int)UnityEngine.TextureFormat.RGB24 => UnityEngine.TextureFormat.RGB24,
            _ => info.Format switch
            {
                2 => UnityEngine.TextureFormat.RGBAHalf,
                9 => UnityEngine.TextureFormat.RGBAFloat,
                _ => UnityEngine.TextureFormat.RGBA32,
            },
        };

        internal static int GpuFloatFormat(UnityEngine.TextureFormat format) => format switch
        {
            UnityEngine.TextureFormat.RFloat => 6,
            UnityEngine.TextureFormat.R16 => GpuR16UNorm,
            UnityEngine.TextureFormat.RG32 => GpuRG16UNorm,
            UnityEngine.TextureFormat.RGBA64 => GpuRGBA16UNorm,
            UnityEngine.TextureFormat.RGBAFloat => 9,
            UnityEngine.TextureFormat.RGFloat => 10,
            UnityEngine.TextureFormat.RHalf => 11,
            UnityEngine.TextureFormat.RGHalf => 12,
            _ => 2,
        };

        /// <summary>Packet formats the renderer keeps at their own precision where the device can
        /// (else as linear RGBA8, with a warning): normalized 16-bit R, RG and RGBA, packed
        /// 10-10-10-2 and signed normalized 8-bit R, RG and RGBA. Their upload data is four floats
        /// per texel.</summary>
        internal const int GpuRG16UNorm = 13, GpuRGBA16UNorm = 14, GpuRGB10A2UNorm = 15, GpuR16UNorm = 16,
            GpuR8SNorm = 35, GpuRG8SNorm = 36, GpuRGBA8SNorm = 37;

        /// <summary>Packet formats of 8-bit texels with fewer than four channels: Alpha8 (one
        /// byte, sampled (0, 0, 0, a); the renderer keeps it as RGBA8), R8_UNorm and R8G8_UNorm
        /// (one and two bytes, sampled (r, 0, 0, 1) and (r, g, 0, 1); kept in their channels where
        /// the device can, else as linear RGBA8 with a warning).</summary>
        internal const int GpuAlpha8 = 3, GpuR8UNorm = 45, GpuRG8UNorm = 46;

        /// <summary>The packet format of R8 and RG16 texels sampled without sRGB decoding (linear
        /// ones, and sRGB ones in a gamma project: R8_UNorm and R8G8_UNorm there), or -1. R8_SRGB
        /// and R8G8_SRGB (sRGB in a linear project) stay RGBA8 sampled through an sRGB view: the
        /// device has no one- or two-channel sRGB formats (the Unity 6000.0.71f1 Editor,
        /// tests/projects/texturecompression channels.*).</summary>
        internal static int GpuChannelFormat(UnityEngine.TextureFormat format, bool linear)
        {
            if (!linear && UnityEngine.QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear) return -1;
            return format switch
            {
                UnityEngine.TextureFormat.R8 => GpuR8UNorm,
                UnityEngine.TextureFormat.RG16 => GpuRG8UNorm,
                _ => -1,
            };
        }

        /// <summary>Whether texels of `format` uploaded as `packet` (RGBA8) sample blue one: RG16
        /// sampled through an sRGB view, as R8G8_SRGB samples (the Unity 6000.0.71f1 Editor,
        /// tests/projects/texturecompression channels.RG16.srgb).</summary>
        internal static bool SamplesBlueOne(UnityEngine.TextureFormat format, int packet) =>
            format == UnityEngine.TextureFormat.RG16 && packet == 0;

        /// <summary>Bytes per texel of an 8-bit packet format's upload data.</summary>
        internal static int GpuTexelBytes(int format) => format switch
        {
            GpuAlpha8 or GpuR8UNorm => 1,
            GpuRG8UNorm => 2,
            _ => 4,
        };

        /// <summary>The upload texels of an 8-bit packet format from cells that hold what the GPU
        /// samples ((0, 0, 0, a), (r, 0, 0, 1), (r, g, 0, 1)): alpha, red, red and green.</summary>
        internal static void GpuTexels(int format, ReadOnlySpan<UnityEngine.Color32> cells, Span<byte> into)
        {
            switch (format)
            {
                case GpuAlpha8:
                    for (int i = 0; i < cells.Length; i++) into[i] = cells[i].a;
                    break;
                case GpuR8UNorm:
                    for (int i = 0; i < cells.Length; i++) into[i] = cells[i].r;
                    break;
                case GpuRG8UNorm:
                    for (int i = 0; i < cells.Length; i++)
                    {
                        into[i * 2] = cells[i].r;
                        into[i * 2 + 1] = cells[i].g;
                    }
                    break;
                default:
                    MemoryMarshal.AsBytes(cells).CopyTo(into);
                    break;
            }
        }

        /// <summary>The packet format of a normalized 16-bit, 10-10-10-2, signed normalized 8-bit or
        /// one- or two-channel normalized 8-bit graphics format, or -1. Textures and render textures of one format share it (copies
        /// between them need it).</summary>
        internal static int GpuNormFormat(UnityEngine.Experimental.Rendering.GraphicsFormat format) => format switch
        {
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm => GpuR16UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_UNorm => GpuRG16UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_UNorm => GpuRGBA16UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.A2B10G10R10_UNormPack32 => GpuRGB10A2UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8_SNorm => GpuR8SNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_SNorm => GpuRG8SNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SNorm => GpuRGBA8SNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm => GpuR8UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_UNorm => GpuRG8UNorm,
            _ => -1,
        };

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
                case UnityEngine.TextureFormat.RHalf: return width * height * 2;
                case UnityEngine.TextureFormat.RGHalf:
                case UnityEngine.TextureFormat.RFloat: return width * height * 4;
                case UnityEngine.TextureFormat.RGBAHalf:
                case UnityEngine.TextureFormat.RGFloat: return width * height * 8;
                case UnityEngine.TextureFormat.RGBAFloat: return width * height * 16;
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

        /// <summary>Float setters store the declared precision; missing CPU channels read one.</summary>
        internal static UnityEngine.Color QuantizeFloat(UnityEngine.TextureFormat format, UnityEngine.Color color)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0) return color;
            for (int k = 0; k < 4; k++) color[k] = k >= channels ? 1f : size == 2 ? (float)(Half)color[k] : color[k];
            return color;
        }

        internal static void DecodeFloat(UnityEngine.TextureFormat format, byte[] data, UnityEngine.Color[] into)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0)
            {
                UnityEngine.Debug.LogWarning($"Unbound: raw texture data in {format} is not supported.");
                return;
            }
            for (int i = 0; i < into.Length && (i + 1) * channels * size <= data.Length; i++)
                into[i] = DecodeFloatTexel(format, data.AsSpan(i * channels * size, channels * size));
        }

        internal static UnityEngine.Color DecodeFloatTexel(UnityEngine.TextureFormat format, ReadOnlySpan<byte> data)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0) throw new NotSupportedException($"Unbound: floating texels in {format} are not supported.");
            var color = new UnityEngine.Color(0f, 0f, 0f, 1f);
            for (int k = 0; k < channels; k++)
                color[k] = size == 2 ? (float)BitConverter.ToHalf(data.Slice(k * size, size)) : BitConverter.ToSingle(data.Slice(k * size, size));
            return color;
        }

        internal static void EncodeFloatTexel(UnityEngine.TextureFormat format, UnityEngine.Color pixel, Span<byte> data)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0) throw new NotSupportedException($"Unbound: floating texels in {format} are not supported.");
            for (int k = 0; k < channels; k++)
            {
                if (size == 2) BitConverter.TryWriteBytes(data.Slice(k * size, size), (Half)pixel[k]);
                else BitConverter.TryWriteBytes(data.Slice(k * size, size), pixel[k]);
            }
        }

        internal static byte[] EncodeFloat(UnityEngine.TextureFormat format, UnityEngine.Color[] pixels)
        {
            var (channels, size) = FloatLayout(format);
            if (channels == 0) return new byte[0];
            var bytes = new byte[pixels.Length * channels * size];
            for (int i = 0; i < pixels.Length; i++)
                EncodeFloatTexel(format, pixels[i], bytes.AsSpan(i * channels * size, channels * size));
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
                case UnityEngine.TextureFormat.R8:
                {
                    var b = new byte[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) b[i] = pixels[i].r;
                    return b;
                }
                case UnityEngine.TextureFormat.ARGB32:
                case UnityEngine.TextureFormat.BGRA32:
                {
                    var b = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var c = pixels[i];
                        if (format == UnityEngine.TextureFormat.ARGB32) { b[i * 4] = c.a; b[i * 4 + 1] = c.r; b[i * 4 + 2] = c.g; b[i * 4 + 3] = c.b; }
                        else { b[i * 4] = c.b; b[i * 4 + 1] = c.g; b[i * 4 + 2] = c.r; b[i * 4 + 3] = c.a; }
                    }
                    return b;
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
