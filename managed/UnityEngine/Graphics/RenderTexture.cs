using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A texture cameras, blits and command buffers render into.</summary>
    [ClassId(84)]
    public partial class RenderTexture : Texture
    {
        RenderTextureDescriptor m_Descriptor;
        bool m_Created;
        int? m_AllocatedMipCount;
        /// <summary>Released at least once: the profiler counts a smaller header from then on.</summary>
        internal bool m_EverReleased;
        /// <summary>In the temporary pool and released (<see cref="ReleaseTemporary"/>).</summary>
        internal bool m_PooledFree;
        internal long m_PoolLease;
        internal int m_LastUsedFrame;
        /// <summary>The size the texture's images were made at.</summary>
        int m_AllocatedWidth, m_AllocatedHeight;
        /// <summary>The factors <see cref="ApplyDynamicScale"/> last applied to a texture marked
        /// useDynamicScaleExplicit.</summary>
        float m_ExplicitWidthScale = 1f, m_ExplicitHeightScale = 1f;

        static RenderTexture s_Active;
        /// <summary>Whether a script made the texture (<see cref="descriptor"/>).</summary>
        bool m_CreatedFromScript;

        /// <summary>A clone's mark: its source's.</summary>
        internal void MarkCreatedFromScript(bool made) => m_CreatedFromScript = made;

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

        /// <summary>A 2D or 2D array color target with a depth buffer samples without anisotropic
        /// filtering whatever its anisoLevel and the global setting, which still raise the level of
        /// one without depth (matches the Unity 6000.0.71f1 Editor on OpenGL Core and Vulkan,
        /// NVIDIA: tests/projects/samplerbias `--target-control`); so does a depth-only target.</summary>
        internal override int SamplerAnisoLevel =>
            m_Descriptor.graphicsFormat == GraphicsFormat.None
            || m_Descriptor.depthStencilFormat != GraphicsFormat.None
                && (m_Descriptor.dimension == TextureDimension.Tex2D || m_Descriptor.dimension == TextureDimension.Tex2DArray)
                ? 0 : anisoLevel;

        /// <summary>A `.renderTexture` asset: its size, formats, samples, mips, dimension and
        /// sampler as saved (measured: tests/projects/builtinrp rt.*). Older files name the formats
        /// by RenderTextureFormat and depth bits.</summary>
        internal virtual void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadRenderTexture(body);

        /// <summary>The render texture fields of a serialized render texture (custom render
        /// textures read theirs after these).</summary>
        internal void ReadRenderTexture(YNode body)
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
            // An asset falls back to the compatible format unless it says not to (Scripting API
            // RenderTexture.depthStencilFormat, 6000.0: m_EnableCompatibleFormat).
            if (body.Bool("m_EnableCompatibleFormat", true))
                m_Descriptor.depthStencilFormat = RenderableDepthFormat(d.depthStencilFormat);
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
            if (Compat.RenderingMeasured && m_AllocatedMipCount.HasValue) return m_AllocatedMipCount.Value;
            return m_Descriptor.useMipMap ? MipLevels(m_Descriptor) : 1;
        }

        public RenderTexture(int width, int height, int depth) : this(width, height, depth, RenderTextureFormat.Default) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
            : this(Descriptor(width, height, format, depth, RenderTextureReadWrite.Default)) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
            : this(Descriptor(width, height, format, depth, readWrite)) { }

        public RenderTexture(int width, int height, int depth, GraphicsFormat format)
            : this(new RenderTextureDescriptor(width, height, format, depth, -1)) { }

        public RenderTexture(int width, int height, int depth, DefaultFormat format)
            : this(new RenderTextureDescriptor(width, height, format == DefaultFormat.HDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, depth, Texture.GenerateAllMips, RenderTextureReadWrite.Default)) { }

        public RenderTexture(int width, int height, int depth, GraphicsFormat format, int mipCount)
            : this(new RenderTextureDescriptor(width, height, format, depth, mipCount)) { }

        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat, int mipCount)
            : this(new RenderTextureDescriptor(width, height, colorFormat, depthStencilFormat, mipCount)) { }

        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat)
            : this(new RenderTextureDescriptor(width, height, colorFormat, depthStencilFormat, -1)) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, int mipCount)
            : this(Descriptor(width, height, format, depth, RenderTextureReadWrite.Default, mipCount)) { }

        public RenderTexture(RenderTextureDescriptor desc)
        {
            if (desc.width <= 0 || desc.height <= 0)
                throw new ArgumentException("RenderTextureDesc width and height must be greater than zero.");
            if (Compat.RenderingMeasured && desc.graphicsFormat != GraphicsFormat.None
                && !SystemInfo.IsFormatSupported(desc.graphicsFormat, GraphicsFormatUsage.Render))
                throw new ArgumentException($"RenderTextureDesc graphicsFormat {desc.graphicsFormat} is not supported on this platform.", "desc.graphicsFormat");
            m_Descriptor = desc;
            m_Descriptor.depthStencilFormat = RenderableDepthFormat(desc.depthStencilFormat);
            m_CreatedFromScript = true;
            m_Width = desc.width;
            m_Height = desc.height;
            ClampByDefault();
        }

        public RenderTexture(RenderTexture textureToCopy) : this(textureToCopy.m_Descriptor) { }

        static RenderTextureDescriptor Descriptor(int width, int height, RenderTextureFormat format, int depth, RenderTextureReadWrite readWrite) =>
            Descriptor(width, height, format, depth, readWrite, Texture.GenerateAllMips);

        static RenderTextureDescriptor Descriptor(int width, int height, RenderTextureFormat format, int depth, RenderTextureReadWrite readWrite, int mipCount)
        {
            var d = new RenderTextureDescriptor(width, height, format, depth, mipCount, RenderTextureReadWrite.Default);
            if (readWrite == RenderTextureReadWrite.Linear) d.sRGB = false;
            // Native ignores this constructor's sRGB request in a Gamma project.
            // An explicitly supplied GraphicsFormat/descriptor is a separate API.
            else if (readWrite == RenderTextureReadWrite.sRGB) d.sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear;
            d.graphicsFormat = SupportedOrFallback(d.graphicsFormat);
            return d;
        }

        /// <summary>A RenderTextureFormat the device cannot render becomes its compatible format,
        /// with the Editor's warning, where a descriptor's GraphicsFormat throws: the Vulkan Editor
        /// makes `new RenderTexture(2, 1, 0, RenderTextureFormat.ARGB4444)` and an ARGB4444
        /// RenderTextureDescriptor R8G8B8A8_UNorm targets and logs this once for each
        /// (tests/projects/samplerstates Packed.ARGB4444 on Vulkan).</summary>
        internal static GraphicsFormat SupportedOrFallback(GraphicsFormat requested)
        {
            if (!Compat.RenderingMeasured || requested == GraphicsFormat.None || SystemInfo.IsFormatSupported(requested, GraphicsFormatUsage.Render))
                return requested;
            var compatible = SystemInfo.GetCompatibleFormat(requested, GraphicsFormatUsage.Render);
            if (compatible == GraphicsFormat.None || compatible == requested) return requested;
            Debug.LogWarning($"'{requested}' is not supported. RenderTexture::GetTemporary fallbacks to {compatible} format on this platform. Use 'SystemInfo.IsFormatSupported' C# API to check format support.");
            return compatible;
        }

        void Change()
        {
            if (m_Created)
                Debug.LogError("Setting a property of a RenderTexture that is already created is not supported; release it first.");
        }

        public override int width { get => m_Descriptor.width; set { Change(); m_Descriptor.width = value; m_Width = value; if (!m_Created) m_AllocatedMipCount = null; } }
        public override int height { get => m_Descriptor.height; set { Change(); m_Descriptor.height = value; m_Height = value; if (!m_Created) m_AllocatedMipCount = null; } }
        public int depth { get => m_Descriptor.depthBufferBits; set { Change(); m_Descriptor.depthBufferBits = value; } }
        /// <summary>The color format; a depth-only target is Depth, or Shadowmap when it compares
        /// (measured). Set, it is taken in the texture's own encoding as it is (no device
        /// fallback, unlike the descriptor's), a Shadowmap turns comparing on and nothing turns it
        /// off, and a depth-only format without a depth buffer gets 16 bits, with Unity's warning
        /// (Unity 6000.0.71f1's RenderTexture.format).</summary>
        public RenderTextureFormat format
        {
            get => m_Descriptor.colorFormat;
            set
            {
                Change();
                if (value == RenderTextureFormat.Depth || value == RenderTextureFormat.Shadowmap)
                {
                    if (m_Descriptor.depthStencilFormat == GraphicsFormat.None)
                    {
                        Debug.LogWarning($"{value} RenderTexture requested without a depth buffer. Changing to a 16 bit depth buffer. To resolve this warning, please specify the desired number of depth bits when creating the render texture.");
                        m_Descriptor.depthStencilFormat = RenderableDepthFormat(GraphicsFormat.D16_UNorm);
                    }
                    if (value == RenderTextureFormat.Shadowmap) m_Descriptor.shadowSamplingMode = ShadowSamplingMode.CompareDepths;
                }
                m_Descriptor.graphicsFormat = GraphicsFormatUtility.GetGraphicsFormat(value, m_Descriptor.sRGB);
            }
        }
        public new GraphicsFormat graphicsFormat { get => m_Descriptor.graphicsFormat; set { Change(); m_Descriptor.graphicsFormat = value; } }
        internal override GraphicsFormat GraphicsFormatOf() => m_Descriptor.graphicsFormat;
        public GraphicsFormat depthStencilFormat { get => m_Descriptor.depthStencilFormat; set { Change(); m_Descriptor.depthStencilFormat = RenderableDepthFormat(value); } }
        public GraphicsFormat stencilFormat { get => m_Descriptor.stencilFormat; set { Change(); m_Descriptor.stencilFormat = value; } }
        public bool useMipMap { get => m_Descriptor.useMipMap; set { Change(); m_Descriptor.useMipMap = value; if (!m_Created) m_AllocatedMipCount = null; } }
        public bool autoGenerateMips { get => m_Descriptor.autoGenerateMips; set { Change(); m_Descriptor.autoGenerateMips = value; } }
        public int antiAliasing { get => m_Descriptor.msaaSamples; set { Change(); m_Descriptor.msaaSamples = value; } }
        public bool bindTextureMS
        {
            get => m_Descriptor.bindMS;
            set
            {
                if (Compat.RenderingMeasured)
                {
                    if (value && dimension != TextureDimension.Tex2D && dimension != TextureDimension.Tex2DArray)
                    {
                        Debug.LogError("Invalid operation for this texture type");
                        return;
                    }
                    if (value == m_Descriptor.bindMS) return;
                    if (m_Created)
                    {
                        Debug.LogError("Setting bind as Texture2DMS of already created render texture is not supported!");
                        return;
                    }
                }
                Change();
                m_Descriptor.bindMS = value;
            }
        }
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

        /// <summary>
        /// The texture as a descriptor. Its creation flags name an sRGB color format and a
        /// texture a script made (<see cref="RenderTextureCreationFlags.CreatedFromScript"/>).
        /// Measured (Unity 6000.0.71f1, tests/projects/particletrailwitness `target.flags`):
        /// AutoGenerateMips, SRGB, CreatedFromScript, AllowVerticalFlip for a default
        /// `new RenderTexture` in a Linear project. Not measured: Unity's own managed class
        /// marks the descriptor of <see cref="GetTemporary(RenderTextureDescriptor)"/> (so the
        /// engine's own temporaries are marked here too); Unbound's reading of the rest is that
        /// every constructor and a descriptor given to a texture mark it, a texture loaded from
        /// an asset is not marked, and a clone is marked as its source is.
        /// </summary>
        public RenderTextureDescriptor descriptor
        {
            get
            {
                var d = m_Descriptor;
                d.createdFromScript = m_CreatedFromScript;
                return d;
            }
            set
            {
                Change();
                m_CreatedFromScript = true;
                m_Descriptor = value;
                m_Descriptor.depthStencilFormat = RenderableDepthFormat(value.depthStencilFormat);
                m_Width = value.width;
                m_Height = value.height;
                if (!m_Created) m_AllocatedMipCount = null;
            }
        }

        /// <summary>The render target of GL drawing and <c>ReadPixels</c>: setting it makes it the
        /// target of what draws next.</summary>
        public static RenderTexture active
        {
            get => s_Active != null && s_Active == ScreenStandIn ? null : s_Active;
            set
            {
                s_Active = value ?? ScreenStandIn;
                GfxStream.SetTarget(s_Active != null ? s_Active.EnsureCreated() : 0);
            }
        }

        /// <summary>The texture that stands for the screen while a screen camera draws
        /// (FrameBuilder): <see cref="active"/> reads null for it, and null selects it. It is
        /// stored as the screen is (top-down where the device stores the screen so: a raw copy of
        /// it or a default Blit from it holds the image as the screen's does, and a wireframe
        /// rasterizes into it as onto the screen; measured: tests/projects/rtdepth vulkan).</summary>
        internal static RenderTexture ScreenStandIn
        {
            get => s_ScreenStandIn;
            set
            {
                if (value == s_ScreenStandIn) return;
                s_ScreenStandIn = value;
                GfxStream.ScreenStandIn(value != null ? value.EnsureCreated() : 0);
            }
        }
        static RenderTexture s_ScreenStandIn;

        /// <summary>The target drawing goes into, the screen's stand-in included.</summary>
        internal static RenderTexture ActiveTarget => s_Active;

        /// <summary>Sets <see cref="active"/> for a caller that sets the stream's target itself.</summary>
        internal static void SetActiveQuietly(RenderTexture value) => s_Active = value;

        public bool Create()
        {
            EnsureCreated();
            return m_Created;
        }

        public void Release() => ReleaseWithWarning(true);

        void ReleaseWithWarning(bool warn)
        {
            // Destruction already marks the wrapper as destroyed, so use object identity here.
            if (warn && ReferenceEquals(s_Active, this) && !ReferenceEquals(this, s_ScreenStandIn))
                Debug.LogWarning("Releasing render texture that is set to be RenderTexture.active!");
            ReleaseQuietly();
        }

        /// <summary>Frees the GPU texture for internal callers without an active-target warning.</summary>
        internal void ReleaseQuietly()
        {
            if (ReferenceEquals(s_Active, this)) active = null;
            if (m_NativeHandle != 0) Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
            m_Created = false;
            m_EverReleased = true;
        }

        public bool IsCreated() => m_Created;

        /// <summary>Zero until the texture is created (the call does not create it), as in the
        /// Unity 6000.0.71f1 Editor (tests/projects/nativegfx ptr.rt.*).</summary>
        internal override IntPtr NativePointer()
        {
            if (!m_Created) return NativePlugins.NoPointer();
            // Its images at the current scale (tests/projects/nativegfx dynres.*).
            EnsureCreated();
            return NativePlugins.TexturePointer(m_NativeHandle, false);
        }

        /// <summary>The depth buffer's OpenGL texture; a texture without depth gives its color
        /// texture (a multisampled one its samples), as in the Editor (tests/projects/nativegfx
        /// ptr.rt.nodepth.*, shape.rt.msaa.color.depth): the renderer knows which.</summary>
        public IntPtr GetNativeDepthBufferPtr()
        {
            if (!m_Created) return NativePlugins.NoPointer();
            EnsureCreated();
            return NativePlugins.TexturePointer(m_NativeHandle, true, depth > 0);
        }

        [Unbound.EditorFaithful("no-op is equivalent: after a discard the contents are undefined, so keeping them is allowed")]
        public void DiscardContents() { }

        [Unbound.EditorFaithful("no-op is equivalent: after a discard the contents are undefined, so keeping them is allowed")]
        public void DiscardContents(bool discardColor, bool discardDepth) { }

        [global::System.@ObsoleteAttribute("This function has no effect.", false)] [Unbound.EditorFaithful("only silences a tile-GPU restore warning: nothing a game observes")]
        public void MarkRestoreExpected() { }

        public void GenerateMips()
        {
            if (!useMipMap) { Debug.LogError("RenderTexture.GenerateMips: the render texture has no mip maps."); return; }
            if (Compat.RenderingMeasured && autoGenerateMips)
            {
                Debug.LogError("RenderTexture.GenerateMips failed:  mipmaps for this render texture are generated automatically (set autoGenerateMips to false to disable that behavior).");
                return;
            }
            GfxStream.GenerateMips(EnsureCreated());
        }

        public void ResolveAntiAliasedSurface()
        {
            if (Compat.RenderingMeasured && antiAliasing > 1 && IsCreated())
                GfxStream.ResolveTexture(EnsureCreated(), EnsureCreated());
            GfxStream.Flush();
        }

        /// <summary>The Editor's assertion when the texture resolved into another is not the
        /// render target being drawn into (its OpenGL Core device, Unity 6000.0.71f1).</summary>
        const string ResolveOfInactiveSource = "Assertion failed on expression: 'srcAttach < m_CurrentFramebuffer.colorCount && \"We should always resolve only current RT\"'";

        /// <summary>
        /// Resolves the multisampled texture into <paramref name="target"/>. The Editor's
        /// OpenGL Core device resolves from the render target being drawn into. Measured
        /// (Unity 6000.0.71f1, tests/projects/cameramsaa `*.resolve` and
        /// `boundary.*.resolveSentinelInactive`; NVIDIA driver): with no render target active,
        /// a created multisampled source logs an assertion (text, type and one per call as in
        /// the capture's Editor log) and the destination receives the pixels of the Editor
        /// window's own framebuffer. Unbound logs the assertion in that state and resolves the
        /// source: it has no Editor window to copy, as for the other reads of that framebuffer.
        /// Not measured, and silent here: another render target active, a source without
        /// multisampling or not created yet, the Vulkan device, and the resolve without a
        /// destination.
        /// </summary>
        public void ResolveAntiAliasedSurface(RenderTexture target)
        {
            if (target == null || target == this) { ResolveAntiAliasedSurface(); return; }
            if (WriteResolve(target)) GfxStream.Flush();
        }

        /// <summary>Writes the resolve into <paramref name="target"/> to the command stream;
        /// false when there is nothing to send.</summary>
        internal bool WriteResolve(RenderTexture target)
        {
            // The Editor leaves an uncreated destination alone (resources rtmips.resolve).
            if (!target.IsCreated()) return false;
            if (!Compat.RenderingMeasured)
            {
                Graphics.CopyElement(this, 0, 0, target, 0, 0);
                return false;
            }
            if (s_Active == null && antiAliasing > 1 && IsCreated() && SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
                Debug.LogAssertion(ResolveOfInactiveSource);
            GfxStream.ResolveTexture(EnsureCreated(), target.EnsureCreated());
            return true;
        }

        /// <summary>Applies ScalableBufferManager's current factors to a texture marked both
        /// useDynamicScale and useDynamicScaleExplicit, which scales only then (as documented; the
        /// macOS Editor's Metal device: tests/projects/nativegfx dynres.explicit.*); other textures
        /// ignore it. The Linux Editor's devices keep the factors 1.</summary>
        public void ApplyDynamicScale()
        {
            if (!m_Descriptor.useDynamicScale || !m_Descriptor.useDynamicScaleExplicit) return;
            m_ExplicitWidthScale = ScalableBufferManager.widthScaleFactor;
            m_ExplicitHeightScale = ScalableBufferManager.heightScaleFactor;
        }

        public void SetGlobalShaderProperty(string propertyName) => Shader.SetGlobalTexture(propertyName, this);

        public bool IsCreatedOrPending() => m_Created;

        /// <summary>The native target format: packed 11/11/10 floats as themselves, half floats for
        /// other HDR formats and R10G10B10_XR, normalized 16-bit R/RG/RGBA, packed 10-10-10-2 and signed normalized
        /// 8-bit R/RG/RGBA (BGRA as RGBA) as themselves (as linear RGBA8, with a warning, where the device cannot
        /// keep them), 8/16/32-bit integer formats as themselves, RGBA8 otherwise (an sRGB view for
        /// sRGB ones; other signed normalized formats with a warning: they lose their sign).</summary>
        internal int PacketFormat()
        {
            var f = m_Descriptor.graphicsFormat;
            if (f == GraphicsFormat.None && m_Descriptor.depthStencilFormat != GraphicsFormat.None) return 5;
            if (f == GraphicsFormat.B10G11R11_UFloatPack32) return 4;
            // Extended-range 10-bit codes step by 1/510: half floats keep every value they encode.
            if (f == GraphicsFormat.R10G10B10_XRUNormPack32) return 2;
            if (f == GraphicsFormat.R32_SFloat) return 6;
            if (f == GraphicsFormat.R32G32B32A32_SFloat) return 9;
            if (f == GraphicsFormat.R32G32_SFloat) return 10;
            if (f == GraphicsFormat.R16_SFloat) return 11;
            if (f == GraphicsFormat.R16G16_SFloat) return 12;
            int norm = PixelFormats.GpuNormFormat(f);
            if (norm >= 0) return norm;
            // A blue-first signed target keeps signed 8-bit channels: its channel order shows only in
            // raw reads, which the device refuses (tests/projects/textureformats metal.jsonl
            // readback.B8G8R8A8_SNorm.*, the Unity 6000.0.71f1 macOS Editor on Metal).
            if (f == GraphicsFormat.B8G8R8A8_SNorm) return PixelFormats.GpuRGBA8SNorm;
            if (GraphicsFormatUtility.IsHDRFormat(f) || GraphicsFormatUtility.IsIEEE754Format(f)) return 2;
            int integer = IntegerPacketFormat(f);
            if (integer >= 0) return integer;
            if (GraphicsFormatUtility.IsIntegerFormat(f) && s_WarnedIntegerFormats.Add(f))
                Debug.LogWarning($"Unbound: render texture '{name}' has the packed integer format {f}, which Unbound does not store yet: it is kept as 8-bit RGBA (values beyond 8 bits per channel are lost).");
            else if (GraphicsFormatUtility.IsSNormFormat(f) && s_WarnedIntegerFormats.Add(f))
                Debug.LogWarning($"Unbound: render texture '{name}' has the signed normalized format {f}, which Unbound does not store yet: it is kept as 8-bit RGBA (negative values read as 0).");
            return GraphicsFormatUtility.IsSRGBFormat(f) ? 0 : 1;
        }

        /// <summary>The render protocol's code (17..=34) of an R, RG, RGB or RGBA integer format of
        /// 8-, 16- or 32-bit channels (RGB as RGBA): 17 + (channel index × 3 + bit index) × 2 +
        /// signed; -1 for any other format.</summary>
        internal static int IntegerPacketFormat(GraphicsFormat f)
        {
            if (f == GraphicsFormat.A2B10G10R10_UIntPack32) return PixelFormats.GpuRGB10A2UInt;
            if (!GraphicsFormatUtility.IsIntegerFormat(f) || GraphicsFormatUtility.IsPackedFormat(f)) return -1;
            uint n = GraphicsFormatUtility.GetComponentCount(f), size = GraphicsFormatUtility.GetBlockSize(f);
            if (n == 0 || n > 4 || size % n != 0) return -1;
            uint bits = size / n * 8;
            if (bits != 8 && bits != 16 && bits != 32) return -1;
            int c = n == 1 ? 0 : n == 2 ? 1 : 2, b = bits == 8 ? 0 : bits == 16 ? 1 : 2;
            return 17 + (c * 3 + b) * 2 + (GraphicsFormatUtility.IsSIntFormat(f) ? 1 : 0);
        }

        /// <summary>Integer and signed normalized formats warned about (once each).</summary>
        static readonly HashSet<GraphicsFormat> s_WarnedIntegerFormats = new HashSet<GraphicsFormat>();

        static readonly int[] s_Bits4444 = { 4, 4, 4, 4 }, s_Bits565 = { 5, 6, 5, 0 };

        /// <summary>The bits a target of a packed 4-bit or 5-6-5 format keeps per channel (red, green,
        /// blue, alpha; 0 where it has none). Unbound stores such a target as 8-bit RGBA; null for
        /// every other format.</summary>
        internal static int[] StoredChannelBits(GraphicsFormat format) => format switch
        {
            GraphicsFormat.R4G4B4A4_UNormPack16 or GraphicsFormat.B4G4R4A4_UNormPack16 => s_Bits4444,
            GraphicsFormat.R5G6B5_UNormPack16 or GraphicsFormat.B5G6R5_UNormPack16 => s_Bits565,
            _ => null,
        };

        /// <summary>R10G10B10_XR's 10-bit code of a value: round(value * 510 + 384), ties to even,
        /// within 0..1023 (the extended range -0.75 to 1.25). The macOS Editor's Metal device stores
        /// 0.25, 0.5, 0.75 as 512, 639, 766 (Unity 6000.0.71f1, tests/projects/textureformats
        /// metal.jsonl readback.R10G10B10_XRUNormPack32.async).</summary>
        internal static uint ExtendedRangeCode(float value) =>
            float.IsNaN(value) ? 0u : (uint)Math.Clamp(Math.Round(value * 510.0 + 384.0, MidpointRounding.ToEven), 0.0, 1023.0);

        /// <summary>Float texels read from a render texture of <paramref name="format"/> as a device
        /// that draws into that format reads them, where that is measured
        /// (TargetReadbackRule.ReadsStoredChannelBits; elsewhere the stored values are read). A packed 4-bit or 5-6-5 target keeps its channel
        /// bits (StoredChannelBits): its values round to k / (2^bits - 1) from Unbound's 8-bit store.
        /// An R10G10B10_XR target reads each color channel's code over 1023, at 8-bit precision, not
        /// the value it encodes. The macOS Editor's Metal device reads targets cleared to
        /// (0.25, 0.5, 0.75, 0.6) as 4/15, 8/15, 11/15, 9/15 (4-bit), 8/31, 32/63, 23/31, 1 (5-6-5)
        /// and 128/255, 159/255, 191/255, 1 (XR) (Unity 6000.0.71f1, tests/projects/textureformats
        /// metal.jsonl readback.*.read.*, .asyncfloat). No 8-bit value lies halfway between two
        /// levels of 4, 5 or 6 bits, nor a code over 1023 halfway between two bytes. Shaders
        /// sampling one read what the device keeps (GraphicsDeviceProfile.PackedTargetsKeepChannelBits).</summary>
        internal static unsafe void QuantizeReadTexels(GraphicsFormat format, byte[] floatTexels)
        {
            var bits = StoredChannelBits(format);
            bool extended = format == GraphicsFormat.R10G10B10_XRUNormPack32;
            if ((bits == null && !extended) || floatTexels == null
                || GraphicsDeviceProfile.Current.TargetReadback?.ReadsStoredChannelBits != true
                || !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)) return;
            fixed (byte* p = floatTexels)
            {
                var values = (float*)p;
                int count = floatTexels.Length / 4;
                for (int i = 0; i < count; i++)
                {
                    if (extended)
                    {
                        if ((i & 3) != 3) values[i] = MathF.Round(ExtendedRangeCode(values[i]) * (255f / 1023f)) / 255f;
                        continue;
                    }
                    int b = bits[i & 3];
                    if (b == 0) continue;
                    float levels = (1 << b) - 1;
                    values[i] = MathF.Round(Math.Clamp(values[i], 0f, 1f) * levels, MidpointRounding.AwayFromZero) / levels;
                }
            }
        }

        /// <summary>The renderer's image count, or depth for a volume.</summary>
        internal int GpuLayers => m_Descriptor.dimension switch
        {
            TextureDimension.Cube => 6,
            TextureDimension.Tex2DArray or TextureDimension.Tex3D => Math.Max(1, m_Descriptor.volumeDepth),
            _ => 1,
        };

        // Texture packet flag: process automatic mip updates at attachment transitions.
        const int DeferredMipUpdates = 8, RetainedMsaaResolve = 16, BindMultisampled = 32, CompareDepth = 64, DepthSamplerState = 128,
            ScreenOrder = TexturePacket.ScreenOrder, Packed565 = 1 << 17, Packed5551 = 2 << 17, Packed4444 = 3 << 17;

        /// <summary>The packet bits of a 5-6-5, 1-5-5-5 or (GraphicsDeviceProfile.Packs4444Targets)
        /// 4-4-4-4 target whose drawn values the device keeps at its channels' bits
        /// (GraphicsDeviceProfile.PackedTargetsKeepChannelBits), else 0.</summary>
        internal int PackedChannelBits()
        {
            var device = GraphicsDeviceProfile.Current;
            if (!device.PackedTargetsKeepChannelBits) return 0;
            return m_Descriptor.graphicsFormat switch
            {
                GraphicsFormat.R5G6B5_UNormPack16 or GraphicsFormat.B5G6R5_UNormPack16 => Packed565,
                GraphicsFormat.A1R5G5B5_UNormPack16 or GraphicsFormat.B5G5R5A1_UNormPack16 or GraphicsFormat.R5G5B5A1_UNormPack16 => Packed5551,
                GraphicsFormat.R4G4B4A4_UNormPack16 or GraphicsFormat.B4G4R4A4_UNormPack16 when device.Packs4444Targets => Packed4444,
                _ => 0,
            };
        }

        /// <summary>Stored as the screen is (a screen camera's stand-in): the renderer keeps it in
        /// the screen's channel order and shows it to native plugins as the screen.</summary>
        internal bool m_ScreenOrder;

        /// <summary>The size of the texture's images: a dynamically scaled one's is scaled
        /// (<see cref="ScalableBufferManager"/>), its width and height staying as they were.</summary>
        internal int ScaledWidth => ScalableBufferManager.Scaled(m_Descriptor.width, WidthScale);

        internal int ScaledHeight => ScalableBufferManager.Scaled(m_Descriptor.height, HeightScale);

        /// <summary>The factors the texture is scaled by: ScalableBufferManager's for one marked
        /// useDynamicScale, those last applied by ApplyDynamicScale for one marked
        /// useDynamicScaleExplicit too (tests/projects/nativegfx dynres.explicit.*), else 1.</summary>
        float WidthScale => !m_Descriptor.useDynamicScale ? 1f
            : m_Descriptor.useDynamicScaleExplicit ? m_ExplicitWidthScale : ScalableBufferManager.widthScaleFactor;

        float HeightScale => !m_Descriptor.useDynamicScale ? 1f
            : m_Descriptor.useDynamicScaleExplicit ? m_ExplicitHeightScale : ScalableBufferManager.heightScaleFactor;

        /// <summary>A texture scaled by a factor other than 1.</summary>
        internal bool DynamicallyScaled => WidthScale != 1f || HeightScale != 1f;

        internal unsafe ulong EnsureCreated()
        {
            m_LastUsedFrame = Time.frameCount;
            // Created, unless it scales and its scaled size changed since: then it is remade at
            // its new size, as the Editor's Metal device resizes existing textures. The renderer
            // keeps the full-size image meanwhile and has it again back at full size; a scaled
            // image starts cleared (the Editor's scaled images read texels no rule found here
            // explains: tests/projects/nativegfx dynres.content.*).
            if (m_Created && (!m_Descriptor.useDynamicScale || (Math.Max(1, ScaledWidth) == m_AllocatedWidth && Math.Max(1, ScaledHeight) == m_AllocatedHeight)))
                return m_NativeHandle;
            if (m_Descriptor.dimension == TextureDimension.Tex3D && depth > 0)
            {
                Debug.LogError("RenderTexture.Create failed: 3D textures with depth are not supported.");
                return 0;
            }
            if (Compat.RenderingMeasured && m_Descriptor.bindMS && m_Descriptor.msaaSamples <= 1) return 0;
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            var dimension = m_Descriptor.dimension;
            int layers = GpuLayers;
            int depthBits = StoredDepthBits(m_Descriptor.depthStencilFormat);
            int mipCount = !m_Descriptor.useMipMap ? 1 : MipLevels(m_Descriptor);
            if (Compat.RenderingMeasured && dimension != TextureDimension.Tex3D && m_Descriptor.msaaSamples > 1)
                mipCount = 1;
            bool scaled = DynamicallyScaled;
            var packet = new TexturePacket
            {
                Width = Math.Max(1, ScaledWidth),
                Height = Math.Max(1, ScaledHeight),
                LogicalWidth = scaled ? Math.Max(1, m_Descriptor.width) : 0,
                LogicalHeight = scaled ? Math.Max(1, m_Descriptor.height) : 0,
                Format = PacketFormat(),
                MipCount = mipCount,
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                WrapW = (int)wrapModeW,
                AnisoLevel = SamplerAnisoLevel,
                // 1: drawn into; 2: written by kernels (a storage binding).
                RenderTarget = 1 | (Compat.RenderingMeasured ? DeferredMipUpdates | RetainedMsaaResolve | DepthSamplerState | (m_Descriptor.bindMS ? BindMultisampled : 0) | (m_Descriptor.shadowSamplingMode == ShadowSamplingMode.CompareDepths ? CompareDepth : 0) : 0) | (m_Descriptor.enableRandomWrite ? 2 : 0) | (m_Descriptor.useMipMap && m_Descriptor.autoGenerateMips ? 4 : 0) | (Math.Clamp(m_Descriptor.msaaSamples, 1, 8) << 8) | (m_ScreenOrder ? ScreenOrder : 0) | PackedChannelBits(),
                DepthBits = depthBits,
                Layers = layers,
                Dimension = dimension != TextureDimension.Tex2D || layers > 1 ? (int)dimension : 0,
            };
            UploadTexture(&packet);
            m_AllocatedMipCount = Compat.RenderingMeasured ? mipCount : null;
            m_Created = true;
            (m_AllocatedWidth, m_AllocatedHeight) = (packet.Width, packet.Height);
            return m_NativeHandle;
        }

        internal override void OnSamplerChanged()
        {
            if (m_Created) EmitSamplingProperties();
        }

        // ---------------------------------------------------------------- temporaries

        static readonly List<RenderTexture> s_Temporaries = new List<RenderTexture>();
        static long s_NextPoolLease;
        /// <summary>Frames a released temporary stays pooled before it is destroyed.</summary>
        const int TemporaryLifetime = 15;

        public static RenderTexture GetTemporary(RenderTextureDescriptor desc) => GetTemporary(desc, false);

        /// <summary>A temporary, stored as the screen is when <paramref name="screenOrder"/> (kept
        /// apart from the others in the pool).</summary>
        internal static RenderTexture GetTemporary(RenderTextureDescriptor desc, bool screenOrder)
        {
            // As the texture keeps it, so that a pooled one matches again.
            desc.depthStencilFormat = RenderableDepthFormat(desc.depthStencilFormat);
            foreach (var t in s_Temporaries)
            {
                if (t.m_PooledFree && t.m_ScreenOrder == screenOrder && SameShape(t.m_Descriptor, desc))
                {
                    // Taken again it starts unscaled, as a new explicitly scaled one does
                    // (dynres.explicit.created; a reused one is unmeasured).
                    (t.m_ExplicitWidthScale, t.m_ExplicitHeightScale) = (1f, 1f);
                    t.m_PooledFree = false;
                    t.m_PoolLease = ++s_NextPoolLease;
                    t.m_LastUsedFrame = Time.frameCount;
                    return t;
                }
            }
            RenderTexture created;
            using (ObjectRegistry.AsAssets())
                created = new RenderTexture(desc) { name = "TempBuffer " + s_Temporaries.Count, hideFlags = HideFlags.HideAndDontSave, m_ScreenOrder = screenOrder };
            created.m_LastUsedFrame = Time.frameCount;
            created.m_PoolLease = ++s_NextPoolLease;
            s_Temporaries.Add(created);
            return created;
        }

        static bool SameShape(in RenderTextureDescriptor a, in RenderTextureDescriptor b) =>
            a.width == b.width && a.height == b.height && a.graphicsFormat == b.graphicsFormat
            && a.depthStencilFormat == b.depthStencilFormat && a.msaaSamples == b.msaaSamples
            && a.dimension == b.dimension && a.volumeDepth == b.volumeDepth && a.useMipMap == b.useMipMap
            && (!a.useMipMap || (MipLevels(a) == MipLevels(b) && a.autoGenerateMips == b.autoGenerateMips))
            && a.enableRandomWrite == b.enableRandomWrite && a.memoryless == b.memoryless
            && a.shadowSamplingMode == b.shadowSamplingMode && a.bindMS == b.bindMS
            && a.useDynamicScale == b.useDynamicScale && a.useDynamicScaleExplicit == b.useDynamicScaleExplicit;

        /// <summary>The mip levels a descriptor with mips gets (its mip count, the full chain when
        /// that is 0 or less or more than the chain).</summary>
        static int MipLevels(in RenderTextureDescriptor d)
        {
            int full = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, Math.Max(d.width, d.height)));
            return d.mipCount > 0 ? Math.Min(d.mipCount, full) : full;
        }

        internal static RenderTextureDescriptor TemporaryDescriptor(int width, int height, int depthBuffer, RenderTextureFormat format,
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

        /// <summary>The depth-stencil format a render texture keeps: the device's compatible render
        /// format of one it cannot render (D16_UNorm_S8_UInt reads back as D24_UNorm_S8_UInt on
        /// both measured devices, tests/projects/samplerstates Stencil.d16s8.format; measured after
        /// Create, Unbound converts when the format is set), the format itself otherwise or when
        /// there is no compatible one.</summary>
        internal static GraphicsFormat RenderableDepthFormat(GraphicsFormat format)
        {
            if (format == GraphicsFormat.None || SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)) return format;
            var compatible = SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);
            return compatible == GraphicsFormat.None ? format : compatible;
        }

        /// <summary>The depth bits of the buffer the renderer stores for a depth-stencil format;
        /// a 16-bit format with stencil gets 24 bits. A 32-bit stencil format uses the packet's
        /// dedicated tag, keeping 32-bit depth without stencil distinct. The measured profile
        /// also preserves 24-bit depth without stencil.</summary>
        internal static int StoredDepthBits(GraphicsFormat format)
        {
            int bits = GraphicsFormatUtility.GetDepthBits(format);
            bool stencil = GraphicsFormatUtility.IsStencilFormat(format);
            if (bits == 24 && !stencil && Compat.RenderingMeasured) return TexturePacket.Depth24Bits;
            if (bits == 32 && stencil) return TexturePacket.Depth32StencilBits;
            return bits == 16 && stencil ? 24 : bits;
        }

        internal static GraphicsFormat GetCompatibleFormat(RenderTextureFormat renderTextureFormat, RenderTextureReadWrite readWrite) =>
            GraphicsFormatUtility.GetGraphicsFormat(renderTextureFormat, readWrite);

        internal override void OnEngineDestroy() => ReleaseWithWarning(Compat.RenderingMeasured);
    }

    public partial struct RenderTextureDescriptor
    {
        GraphicsFormat m_GraphicsFormat;
        GraphicsFormat m_DepthStencilFormat;
        RenderTextureCreationFlags m_Flags;

        public RenderTextureDescriptor(int width, int height) : this(width, height, RenderTextureFormat.Default, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat) : this(width, height, colorFormat, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits)
            : this(width, height, colorFormat, depthBufferBits, Texture.GenerateAllMips) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits, int mipCount)
            : this(width, height, colorFormat, depthBufferBits, mipCount, RenderTextureReadWrite.Linear) { }

        /// <summary>A RenderTextureFormat the device cannot render becomes its compatible format,
        /// with the Editor's warning, as RenderTexture's constructors of one do (the Vulkan
        /// Editor's ARGB4444 descriptor makes an R8G8B8A8_UNorm target: tests/projects/samplerstates
        /// Packed.ARGB4444.mip.*); a descriptor of a GraphicsFormat keeps it.</summary>
        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits, int mipCount, RenderTextureReadWrite readWrite)
            : this(width, height, RenderTexture.SupportedOrFallback(GraphicsFormatUtility.GetGraphicsFormat(colorFormat, readWrite)),
                colorFormat == RenderTextureFormat.Shadowmap ? GraphicsFormatUtility.ShadowmapDepthFormatForBits(depthBufferBits) : GraphicsFormatUtility.DepthStencilFormatForBits(depthBufferBits), mipCount)
        {
            if (colorFormat == RenderTextureFormat.Shadowmap) shadowSamplingMode = ShadowSamplingMode.CompareDepths;
        }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, int depthBufferBits)
            : this(width, height, colorFormat, depthBufferBits, Texture.GenerateAllMips) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, int depthBufferBits, int mipCount)
            : this(width, height, colorFormat, GraphicsFormatUtility.DepthStencilFormatForBits(depthBufferBits), mipCount) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat)
            : this(width, height, colorFormat, depthStencilFormat, Texture.GenerateAllMips) { }

        public RenderTextureDescriptor(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat, int mipCount)
        {
            this = default;
            this.width = width;
            this.height = height;
            msaaSamples = 1;
            volumeDepth = 1;
            this.mipCount = mipCount;
            dimension = TextureDimension.Tex2D;
            // Before the format, whose setter adds its own flag.
            m_Flags = RenderTextureCreationFlags.AutoGenerateMips | RenderTextureCreationFlags.AllowVerticalFlip;
            graphicsFormat = colorFormat;
            m_DepthStencilFormat = depthStencilFormat;
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

        /// <summary>The color format; the <see cref="RenderTextureCreationFlags.SRGB"/> flag
        /// follows it, as in Unity's own descriptor.</summary>
        public GraphicsFormat graphicsFormat
        {
            get => m_GraphicsFormat;
            set
            {
                m_GraphicsFormat = value;
                SetFlag(RenderTextureCreationFlags.SRGB, GraphicsFormatUtility.IsSRGBFormat(value));
            }
        }

        /// <summary>Whether a script made the render texture the descriptor describes
        /// (<see cref="RenderTexture.descriptor"/> sets it; a new descriptor has it clear).</summary>
        internal bool createdFromScript
        {
            get => (m_Flags & RenderTextureCreationFlags.CreatedFromScript) != 0;
            set => SetFlag(RenderTextureCreationFlags.CreatedFromScript, value);
        }
        public GraphicsFormat depthStencilFormat { readonly get => m_DepthStencilFormat; set => m_DepthStencilFormat = value; }

        /// <summary>The color format; a depth-only target is Depth, or Shadowmap when it compares
        /// (measured: tests/projects/builtinrp rt.ProbeShadow).</summary>
        public RenderTextureFormat colorFormat
        {
            get => m_GraphicsFormat == GraphicsFormat.None && m_DepthStencilFormat != GraphicsFormat.None
                ? (shadowSamplingMode == ShadowSamplingMode.CompareDepths ? RenderTextureFormat.Shadowmap : RenderTextureFormat.Depth)
                : GraphicsFormatUtility.GetRenderTextureFormat(m_GraphicsFormat);
            // The format in the descriptor's own encoding, as the device can render it; only a
            // Shadowmap compares (Unity 6000.0.71f1's RenderTextureDescriptor.colorFormat).
            set
            {
                shadowSamplingMode = value == RenderTextureFormat.Shadowmap ? ShadowSamplingMode.CompareDepths : ShadowSamplingMode.None;
                var format = GraphicsFormatUtility.GetGraphicsFormat(value, sRGB);
                graphicsFormat = SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);
            }
        }

        public bool sRGB
        {
            get => GraphicsFormatUtility.IsSRGBFormat(m_GraphicsFormat);
            // As Unity's own descriptor: an sRGB format only in a Linear project.
            set => graphicsFormat = value && QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormatUtility.GetSRGBFormat(m_GraphicsFormat)
                : GraphicsFormatUtility.GetLinearFormat(m_GraphicsFormat);
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
