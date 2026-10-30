using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A texture cameras, blits and command buffers render into.</summary>
    [ClassId(84)]
    public partial class RenderTexture : Texture, ISerializedAsset
    {
        RenderTextureDescriptor m_Descriptor;
        bool m_Created;
        /// <summary>In the temporary pool and released (<see cref="ReleaseTemporary"/>).</summary>
        internal bool m_PooledFree;
        internal int m_LastUsedFrame;

        static RenderTexture s_Active;

        protected internal RenderTexture()
        {
            m_Descriptor = new RenderTextureDescriptor(256, 256);
            ClampByDefault();
        }

        /// <summary>A render texture clamps (measured: one made from a descriptor reports and saves
        /// Clamp).</summary>
        void ClampByDefault() => SetSamplerQuietly(FilterMode.Bilinear, TextureWrapMode.Clamp, TextureWrapMode.Clamp, TextureWrapMode.Clamp, NoAniso ? 0 : 1, 0f);

        /// <summary>Cube and depth-only targets have no anisotropic filtering (measured: they report
        /// 0 whatever was saved).</summary>
        bool NoAniso => m_Descriptor.dimension == TextureDimension.Cube || m_Descriptor.graphicsFormat == GraphicsFormat.None;

        /// <summary>A `.renderTexture` asset: its size, formats, samples, mips, dimension and
        /// sampler as saved (measured: tests/projects/builtinrp rt.*). Older files name the formats
        /// by RenderTextureFormat and depth bits.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            int w = Math.Max(1, body.Int("m_Width", 256)), h = Math.Max(1, body.Int("m_Height", 256));
            bool sRGB = body.Bool("m_SRGB");
            GraphicsFormat color, depth;
            if (body.Get("m_DepthStencilFormat") != null)
            {
                color = (GraphicsFormat)body.Int("m_ColorFormat", (int)GraphicsFormat.R8G8B8A8_UNorm);
                depth = (GraphicsFormat)body.Int("m_DepthStencilFormat");
            }
            else
            {
                color = GraphicsFormatUtility.GetGraphicsFormat((RenderTextureFormat)body.Int("m_ColorFormat"), sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                depth = GraphicsFormatUtility.DepthStencilFormatForBits(body.Int("m_DepthFormat") switch { 0 => 0, 1 => 16, _ => 24 });
            }
            var d = new RenderTextureDescriptor(w, h, color, depth, body.Int("m_MipCount", -1))
            {
                msaaSamples = Math.Max(1, body.Int("m_AntiAliasing", 1)),
                useMipMap = body.Bool("m_MipMap"),
                autoGenerateMips = body.Bool("m_GenerateMips", true),
                useDynamicScale = body.Bool("m_UseDynamicScale"),
                useDynamicScaleExplicit = body.Bool("m_UseDynamicScaleExplicit"),
                bindMS = body.Bool("m_BindMS"),
                enableRandomWrite = body.Bool("m_EnableRandomWrite"),
                dimension = (TextureDimension)body.Int("m_Dimension", (int)TextureDimension.Tex2D),
                volumeDepth = Math.Max(1, body.Int("m_VolumeDepth", 1)),
                shadowSamplingMode = (ShadowSamplingMode)body.Int("m_ShadowSamplingMode", (int)ShadowSamplingMode.None),
            };
            m_Descriptor = d;
            m_Width = w;
            m_Height = h;
            var settings = body.Get("m_TextureSettings");
            if (settings != null)
                SetSamplerQuietly((FilterMode)settings.Int("m_FilterMode", 1), (TextureWrapMode)settings.Int("m_WrapU", 1), (TextureWrapMode)settings.Int("m_WrapV", 1),
                    (TextureWrapMode)settings.Int("m_WrapW", 1), NoAniso ? 0 : settings.Int("m_Aniso", 1), settings.Float("m_MipBias"));
        }

        /// <summary>The mips: the full chain (or the descriptor's count) when it has mipmaps.</summary>
        internal override int MipmapCount()
        {
            if (!m_Descriptor.useMipMap) return 1;
            int full = 1 + (int)Math.Floor(Math.Log(Math.Max(1, Math.Max(m_Descriptor.width, m_Descriptor.height)), 2));
            return m_Descriptor.mipCount > 0 ? Math.Min(m_Descriptor.mipCount, full) : full;
        }

        public RenderTexture(int width, int height, int depth) : this(width, height, depth, RenderTextureFormat.Default) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
            : this(new RenderTextureDescriptor(width, height, format, depth, -1)) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
            : this(Descriptor(width, height, format, depth, readWrite)) { }

        public RenderTexture(int width, int height, int depth, GraphicsFormat format)
            : this(new RenderTextureDescriptor(width, height, format, depth, -1)) { }

        public RenderTexture(int width, int height, int depth, DefaultFormat format)
            : this(new RenderTextureDescriptor(width, height, format == DefaultFormat.HDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, depth, -1)) { }

        public RenderTexture(int width, int height, int depth, GraphicsFormat format, int mipCount)
            : this(new RenderTextureDescriptor(width, height, format, depth, mipCount)) { }

        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat, int mipCount)
            : this(new RenderTextureDescriptor(width, height, colorFormat, depthStencilFormat, mipCount)) { }

        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat)
            : this(new RenderTextureDescriptor(width, height, colorFormat, depthStencilFormat, -1)) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, int mipCount)
            : this(new RenderTextureDescriptor(width, height, format, depth, mipCount)) { }

        public RenderTexture(RenderTextureDescriptor desc)
        {
            if (desc.width <= 0 || desc.height <= 0)
                throw new ArgumentException("RenderTextureDesc width and height must be greater than zero.");
            m_Descriptor = desc;
            m_Width = desc.width;
            m_Height = desc.height;
            ClampByDefault();
        }

        public RenderTexture(RenderTexture textureToCopy) : this(textureToCopy.m_Descriptor) { }

        static RenderTextureDescriptor Descriptor(int width, int height, RenderTextureFormat format, int depth, RenderTextureReadWrite readWrite)
        {
            var d = new RenderTextureDescriptor(width, height, format, depth, -1);
            if (readWrite == RenderTextureReadWrite.Linear) d.sRGB = false;
            else if (readWrite == RenderTextureReadWrite.sRGB) d.sRGB = true;
            return d;
        }

        void Change()
        {
            if (m_Created)
                Debug.LogError("Setting a property of a RenderTexture that is already created is not supported; release it first.");
        }

        public override int width { get => m_Descriptor.width; set { Change(); m_Descriptor.width = value; m_Width = value; } }
        public override int height { get => m_Descriptor.height; set { Change(); m_Descriptor.height = value; m_Height = value; } }
        public int depth { get => m_Descriptor.depthBufferBits; set { Change(); m_Descriptor.depthBufferBits = value; } }
        /// <summary>The color format; a depth-only target is Depth, or Shadowmap when it compares
        /// (measured).</summary>
        public RenderTextureFormat format { get => m_Descriptor.colorFormat; set { Change(); m_Descriptor.colorFormat = value; } }
        public new GraphicsFormat graphicsFormat { get => m_Descriptor.graphicsFormat; set { Change(); m_Descriptor.graphicsFormat = value; } }
        internal override GraphicsFormat GraphicsFormatOf() => m_Descriptor.graphicsFormat;
        public GraphicsFormat depthStencilFormat { get => m_Descriptor.depthStencilFormat; set { Change(); m_Descriptor.depthStencilFormat = value; } }
        public GraphicsFormat stencilFormat { get => m_Descriptor.stencilFormat; set { Change(); m_Descriptor.stencilFormat = value; } }
        public bool useMipMap { get => m_Descriptor.useMipMap; set { Change(); m_Descriptor.useMipMap = value; } }
        public bool autoGenerateMips { get => m_Descriptor.autoGenerateMips; set { Change(); m_Descriptor.autoGenerateMips = value; } }
        public int antiAliasing { get => m_Descriptor.msaaSamples; set { Change(); m_Descriptor.msaaSamples = value; } }
        public bool bindTextureMS { get => m_Descriptor.bindMS; set { Change(); m_Descriptor.bindMS = value; } }
        public bool enableRandomWrite { get => m_Descriptor.enableRandomWrite; set { Change(); m_Descriptor.enableRandomWrite = value; } }
        public bool useDynamicScale { get => m_Descriptor.useDynamicScale; set { Change(); m_Descriptor.useDynamicScale = value; } }
        public bool useDynamicScaleExplicit { get => m_Descriptor.useDynamicScaleExplicit; set { Change(); m_Descriptor.useDynamicScaleExplicit = value; } }
        [Unbound.EditorFaithful("Unity's own setter does nothing: the answer follows the size")]
        public bool isPowerOfTwo { get => Mathf.IsPowerOfTwo(width) && Mathf.IsPowerOfTwo(height); set { } }
        public bool sRGB => m_Descriptor.sRGB;
        public RenderTextureMemoryless memorylessMode { get => m_Descriptor.memoryless; set { Change(); m_Descriptor.memoryless = value; } }
        public VRTextureUsage vrUsage { get => m_Descriptor.vrUsage; set { Change(); m_Descriptor.vrUsage = value; } }
        public override TextureDimension dimension { get => m_Descriptor.dimension; set { Change(); m_Descriptor.dimension = value; } }
        public int volumeDepth { get => m_Descriptor.volumeDepth; set { Change(); m_Descriptor.volumeDepth = value; } }

        public RenderTextureDescriptor descriptor
        {
            get => m_Descriptor;
            set
            {
                Change();
                m_Descriptor = value;
                m_Width = value.width;
                m_Height = value.height;
            }
        }

        /// <summary>The render target of GL drawing and <c>ReadPixels</c>: setting it makes it the
        /// target of what draws next.</summary>
        public static RenderTexture active
        {
            get => s_Active;
            set
            {
                s_Active = value;
                GfxStream.SetTarget(value != null ? value.EnsureCreated() : 0);
            }
        }

        /// <summary>Sets <see cref="active"/> for a caller that sets the stream's target itself.</summary>
        internal static void SetActiveQuietly(RenderTexture value) => s_Active = value;

        public bool Create()
        {
            EnsureCreated();
            return true;
        }

        public void Release()
        {
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
            m_Created = false;
        }

        public bool IsCreated() => m_Created;

        [Unbound.EditorFaithful("no-op is equivalent: after a discard the contents are undefined, so keeping them is allowed")]
        public void DiscardContents() { }

        [Unbound.EditorFaithful("no-op is equivalent: after a discard the contents are undefined, so keeping them is allowed")]
        public void DiscardContents(bool discardColor, bool discardDepth) { }

        [Unbound.EditorFaithful("only silences a tile-GPU restore warning: nothing a game observes")]
        public void MarkRestoreExpected() { }

        public void GenerateMips()
        {
            if (!useMipMap) { Debug.LogError("RenderTexture.GenerateMips: the render texture has no mip maps."); return; }
            GfxStream.GenerateMips(EnsureCreated());
        }

        // Native passes resolve their MSAA attachment before later work can read it.
        public void ResolveAntiAliasedSurface() => GfxStream.Flush();

        public void ResolveAntiAliasedSurface(RenderTexture target)
        {
            if (target == null || target == this) { ResolveAntiAliasedSurface(); return; }
            // The Editor leaves an uncreated destination alone (resources rtmips.resolve).
            if (!target.IsCreated()) return;
            Graphics.CopyTexture(this, 0, 0, target, 0, 0);
        }

        [Unbound.EditorFaithful("the Editor has no dynamic resolution: the scale stays 1 (as ScalableBufferManager)")]
        public void ApplyDynamicScale() { }

        public void SetGlobalShaderProperty(string propertyName) => Shader.SetGlobalTexture(propertyName, this);

        public bool IsCreatedOrPending() => m_Created;

        /// <summary>The native target format: packed 11/11/10 floats as themselves, half floats for
        /// other HDR formats, RGBA8 otherwise (an sRGB view for sRGB ones).</summary>
        int PacketFormat()
        {
            var f = m_Descriptor.graphicsFormat;
            if (f == GraphicsFormat.None && m_Descriptor.depthStencilFormat != GraphicsFormat.None) return 5;
            if (f == GraphicsFormat.B10G11R11_UFloatPack32) return 4;
            if (f == GraphicsFormat.R32_SFloat) return 6;
            if (f == GraphicsFormat.R32G32B32A32_SFloat) return 9;
            if (f == GraphicsFormat.R32G32_SFloat) return 10;
            if (f == GraphicsFormat.R16_SFloat) return 11;
            if (f == GraphicsFormat.R16G16_SFloat) return 12;
            if (GraphicsFormatUtility.IsHDRFormat(f) || GraphicsFormatUtility.IsIEEE754Format(f)) return 2;
            return GraphicsFormatUtility.IsSRGBFormat(f) ? 0 : 1;
        }

        internal unsafe ulong EnsureCreated()
        {
            m_LastUsedFrame = Time.frameCount;
            if (m_Created) return m_NativeHandle;
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            var dimension = m_Descriptor.dimension;
            if (dimension == TextureDimension.Tex3D)
                Debug.LogWarning($"Unbound: RenderTexture '{name}' is a {dimension}; 3D render textures are not drawn into yet (a 2D one stands in).");
            int layers = dimension switch
            {
                TextureDimension.Cube => 6,
                TextureDimension.Tex2DArray => Math.Max(1, m_Descriptor.volumeDepth),
                _ => 1,
            };
            int depthBits = GraphicsFormatUtility.GetDepthBits(m_Descriptor.depthStencilFormat);
            var packet = new TexturePacket
            {
                Width = Math.Max(1, m_Descriptor.width),
                Height = Math.Max(1, m_Descriptor.height),
                Format = PacketFormat(),
                MipCount = MipmapCount(),
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                AnisoLevel = anisoLevel,
                // 1: drawn into; 2: written by kernels (a storage binding).
                RenderTarget = 1 | (m_Descriptor.enableRandomWrite ? 2 : 0) | (m_Descriptor.useMipMap && m_Descriptor.autoGenerateMips ? 4 : 0) | (Math.Clamp(m_Descriptor.msaaSamples, 1, 8) << 8),
                DepthBits = depthBits,
                Layers = layers,
                Dimension = layers > 1 ? (int)dimension : 0,
            };
            Native.UploadTexture(m_NativeHandle, &packet);
            m_Created = true;
            return m_NativeHandle;
        }

        internal override void OnSamplerChanged()
        {
            // The sampler is part of the texture on the native side: a created target is remade
            // with it (its contents are kept by the renderer for an unchanged shape).
            if (m_Created)
            {
                m_Created = false;
                EnsureCreated();
            }
        }

        // ---------------------------------------------------------------- temporaries

        static readonly List<RenderTexture> s_Temporaries = new List<RenderTexture>();
        /// <summary>Frames a released temporary stays pooled before it is destroyed.</summary>
        const int TemporaryLifetime = 15;

        public static RenderTexture GetTemporary(RenderTextureDescriptor desc)
        {
            foreach (var t in s_Temporaries)
            {
                if (t.m_PooledFree && SameShape(t.m_Descriptor, desc))
                {
                    t.m_PooledFree = false;
                    t.m_LastUsedFrame = Time.frameCount;
                    return t;
                }
            }
            RenderTexture created;
            using (ObjectRegistry.AsAssets())
                created = new RenderTexture(desc) { name = "TempBuffer " + s_Temporaries.Count, hideFlags = HideFlags.HideAndDontSave };
            created.m_LastUsedFrame = Time.frameCount;
            s_Temporaries.Add(created);
            return created;
        }

        static bool SameShape(in RenderTextureDescriptor a, in RenderTextureDescriptor b) =>
            a.width == b.width && a.height == b.height && a.graphicsFormat == b.graphicsFormat
            && a.depthStencilFormat == b.depthStencilFormat && a.msaaSamples == b.msaaSamples
            && a.dimension == b.dimension && a.volumeDepth == b.volumeDepth && a.useMipMap == b.useMipMap
            && a.enableRandomWrite == b.enableRandomWrite && a.memoryless == b.memoryless;

        static RenderTextureDescriptor TemporaryDescriptor(int width, int height, int depthBuffer, RenderTextureFormat format,
            RenderTextureReadWrite readWrite, int antiAliasing, RenderTextureMemoryless memorylessMode = RenderTextureMemoryless.None,
            VRTextureUsage vrUsage = VRTextureUsage.None, bool useDynamicScale = false)
        {
            var d = Descriptor(width, height, format, depthBuffer, readWrite);
            d.msaaSamples = Math.Max(1, antiAliasing);
            d.memoryless = memorylessMode;
            d.vrUsage = vrUsage;
            d.useDynamicScale = useDynamicScale;
            return d;
        }

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite,
            int antiAliasing, RenderTextureMemoryless memorylessMode, VRTextureUsage vrUsage, bool useDynamicScale) =>
            GetTemporary(TemporaryDescriptor(width, height, depthBuffer, format, readWrite, antiAliasing, memorylessMode, vrUsage, useDynamicScale));

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite,
            int antiAliasing, RenderTextureMemoryless memorylessMode, VRTextureUsage vrUsage) =>
            GetTemporary(TemporaryDescriptor(width, height, depthBuffer, format, readWrite, antiAliasing, memorylessMode, vrUsage));

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite,
            int antiAliasing, RenderTextureMemoryless memorylessMode) =>
            GetTemporary(TemporaryDescriptor(width, height, depthBuffer, format, readWrite, antiAliasing, memorylessMode));

        public static RenderTexture GetTemporary(int width, int height, [Internal.DefaultValue("0")] int depthBuffer,
            [Internal.DefaultValue("RenderTextureFormat.Default")] RenderTextureFormat format,
            [Internal.DefaultValue("RenderTextureReadWrite.Default")] RenderTextureReadWrite readWrite,
            [Internal.DefaultValue("1")] int antiAliasing) =>
            GetTemporary(TemporaryDescriptor(width, height, depthBuffer, format, readWrite, antiAliasing));

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite) =>
            GetTemporary(width, height, depthBuffer, format, readWrite, 1);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format) =>
            GetTemporary(width, height, depthBuffer, format, RenderTextureReadWrite.Default, 1);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer) =>
            GetTemporary(width, height, depthBuffer, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);
        public static RenderTexture GetTemporary(int width, int height) =>
            GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, GraphicsFormat format) =>
            GetTemporary(new RenderTextureDescriptor(width, height, format, depthBuffer));
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, GraphicsFormat format, int antiAliasing) =>
            GetTemporary(new RenderTextureDescriptor(width, height, format, depthBuffer) { msaaSamples = Math.Max(1, antiAliasing) });

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, GraphicsFormat format, int antiAliasing, RenderTextureMemoryless memorylessMode) =>
            GetTemporary(new RenderTextureDescriptor(width, height, format, depthBuffer) { msaaSamples = Math.Max(1, antiAliasing), memoryless = memorylessMode });
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, GraphicsFormat format, int antiAliasing, RenderTextureMemoryless memorylessMode, VRTextureUsage vrUsage) =>
            GetTemporary(new RenderTextureDescriptor(width, height, format, depthBuffer) { msaaSamples = Math.Max(1, antiAliasing), memoryless = memorylessMode, vrUsage = vrUsage });
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, GraphicsFormat format, int antiAliasing, RenderTextureMemoryless memorylessMode, VRTextureUsage vrUsage, bool useDynamicScale) =>
            GetTemporary(new RenderTextureDescriptor(width, height, format, depthBuffer) { msaaSamples = Math.Max(1, antiAliasing), memoryless = memorylessMode, vrUsage = vrUsage, useDynamicScale = useDynamicScale });

        public static void ReleaseTemporary(RenderTexture temp)
        {
            if (temp == null) return;
            if (!s_Temporaries.Contains(temp))
            {
                Debug.LogError("Attempting to release RenderTexture that was not gotten as a temp buffer");
                return;
            }
            temp.m_PooledFree = true;
            temp.m_LastUsedFrame = Time.frameCount;
        }

        /// <summary>Destroys temporaries released and unused for a while (each frame's end).</summary>
        internal static void CollectTemporaries()
        {
            for (int i = s_Temporaries.Count - 1; i >= 0; i--)
            {
                var t = s_Temporaries[i];
                if (t == null) { s_Temporaries.RemoveAt(i); continue; }
                if (t.m_PooledFree && Time.frameCount - t.m_LastUsedFrame > TemporaryLifetime)
                {
                    s_Temporaries.RemoveAt(i);
                    DestroyImmediate(t);
                }
            }
        }

        public static bool SupportsStencil(RenderTexture rt) =>
            rt == null || GraphicsFormatUtility.IsStencilFormat(rt.m_Descriptor.depthStencilFormat);

        public static GraphicsFormat GetCompatibleFormat(RenderTextureFormat renderTextureFormat, RenderTextureReadWrite readWrite) =>
            GraphicsFormatUtility.GetGraphicsFormat(renderTextureFormat, readWrite);

        internal override void OnEngineDestroy() => Release();
    }

    public partial struct RenderTextureDescriptor
    {
        GraphicsFormat m_GraphicsFormat;
        GraphicsFormat m_DepthStencilFormat;
        RenderTextureCreationFlags m_Flags;

        public RenderTextureDescriptor(int width, int height) : this(width, height, RenderTextureFormat.Default, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat) : this(width, height, colorFormat, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits)
            : this(width, height, colorFormat, depthBufferBits, 1) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits, int mipCount)
            : this(width, height, colorFormat, depthBufferBits, mipCount, RenderTextureReadWrite.Default) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits, int mipCount, RenderTextureReadWrite readWrite)
            : this(width, height, GraphicsFormatUtility.GetGraphicsFormat(colorFormat, readWrite), GraphicsFormatUtility.DepthStencilFormatForBits(depthBufferBits), mipCount)
        {
            if (colorFormat == RenderTextureFormat.Shadowmap) shadowSamplingMode = ShadowSamplingMode.CompareDepths;
        }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, int depthBufferBits)
            : this(width, height, colorFormat, depthBufferBits, 1) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, int depthBufferBits, int mipCount)
            : this(width, height, colorFormat, GraphicsFormatUtility.DepthStencilFormatForBits(depthBufferBits), mipCount) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat)
            : this(width, height, colorFormat, depthStencilFormat, 1) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat, int mipCount)
        {
            this = default;
            this.width = width;
            this.height = height;
            msaaSamples = 1;
            volumeDepth = 1;
            this.mipCount = mipCount;
            dimension = TextureDimension.Tex2D;
            m_GraphicsFormat = colorFormat;
            m_DepthStencilFormat = depthStencilFormat;
            m_Flags = RenderTextureCreationFlags.AutoGenerateMips | RenderTextureCreationFlags.AllowVerticalFlip;
            // Measured: a new descriptor samples without comparison.
            shadowSamplingMode = ShadowSamplingMode.None;
        }

        public int width { get; set; }
        public int height { get; set; }
        public int msaaSamples { get; set; }
        public int volumeDepth { get; set; }
        public int mipCount { get; set; }
        public TextureDimension dimension { get; set; }
        public ShadowSamplingMode shadowSamplingMode { get; set; }
        public VRTextureUsage vrUsage { get; set; }
        public RenderTextureMemoryless memoryless { get; set; }
        public RenderTextureCreationFlags flags => m_Flags;
        public GraphicsFormat stencilFormat { get; set; }

        public GraphicsFormat graphicsFormat { get => m_GraphicsFormat; set => m_GraphicsFormat = value; }
        public GraphicsFormat depthStencilFormat { get => m_DepthStencilFormat; set => m_DepthStencilFormat = value; }

        /// <summary>The color format; a depth-only target is Depth, or Shadowmap when it compares
        /// (measured: tests/projects/builtinrp rt.ProbeShadow).</summary>
        public RenderTextureFormat colorFormat
        {
            get => m_GraphicsFormat == GraphicsFormat.None && m_DepthStencilFormat != GraphicsFormat.None
                ? (shadowSamplingMode == ShadowSamplingMode.CompareDepths ? RenderTextureFormat.Shadowmap : RenderTextureFormat.Depth)
                : GraphicsFormatUtility.GetRenderTextureFormat(m_GraphicsFormat);
            set
            {
                m_GraphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(value, sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Default);
                if (value == RenderTextureFormat.Shadowmap) shadowSamplingMode = ShadowSamplingMode.CompareDepths;
                else if (value == RenderTextureFormat.Depth) shadowSamplingMode = ShadowSamplingMode.None;
            }
        }

        public bool sRGB
        {
            get => GraphicsFormatUtility.IsSRGBFormat(m_GraphicsFormat);
            set => m_GraphicsFormat = value ? GraphicsFormatUtility.GetSRGBFormat(m_GraphicsFormat) : GraphicsFormatUtility.GetLinearFormat(m_GraphicsFormat);
        }

        public int depthBufferBits
        {
            get => GraphicsFormatUtility.GetDepthBits(m_DepthStencilFormat);
            set => m_DepthStencilFormat = GraphicsFormatUtility.DepthStencilFormatForBits(value);
        }

        void SetFlag(RenderTextureCreationFlags flag, bool on)
        {
            if (on) m_Flags |= flag; else m_Flags &= ~flag;
        }

        public bool useMipMap { get => (m_Flags & RenderTextureCreationFlags.MipMap) != 0; set => SetFlag(RenderTextureCreationFlags.MipMap, value); }
        public bool autoGenerateMips { get => (m_Flags & RenderTextureCreationFlags.AutoGenerateMips) != 0; set => SetFlag(RenderTextureCreationFlags.AutoGenerateMips, value); }
        public bool enableRandomWrite { get => (m_Flags & RenderTextureCreationFlags.EnableRandomWrite) != 0; set => SetFlag(RenderTextureCreationFlags.EnableRandomWrite, value); }
        public bool bindMS { get => (m_Flags & RenderTextureCreationFlags.BindMS) != 0; set => SetFlag(RenderTextureCreationFlags.BindMS, value); }
        public bool useDynamicScale { get => (m_Flags & RenderTextureCreationFlags.DynamicallyScalable) != 0; set => SetFlag(RenderTextureCreationFlags.DynamicallyScalable, value); }
        public bool useDynamicScaleExplicit { get => (m_Flags & RenderTextureCreationFlags.DynamicallyScalableExplicit) != 0; set => SetFlag(RenderTextureCreationFlags.DynamicallyScalableExplicit, value); }
    }
}

namespace UnityEngine.Rendering
{
    public enum RenderTextureSubElement
    {
        Color = 0,
        Depth = 1,
        Stencil = 2,
        Default = 3,
    }
}
