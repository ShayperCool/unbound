using System;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>Base of every texture. The pixels live on the native side under a handle.</summary>
    public partial class Texture : Object
    {
        public static readonly int GenerateAllMips = -1;

        /// <summary>Counts textures destroyed: materials holding one send it again
        /// (Material.Sync).</summary>
        internal static int s_DestroyedEpoch;

        /// <summary>Sets the mipmap streaming debug view's material properties: Unbound streams no
        /// mipmaps, so there is nothing to show (measured to return: tests/projects/runtimeapis
        /// texture.streaming_debug_properties).</summary>
        [Unbound.EditorFaithful("no mipmap streaming: no streaming debug data; measured: tests/projects/runtimeapis texture.streaming_debug_properties")]
        public static void SetStreamingTextureMaterialDebugProperties() { }

        [Unbound.EditorFaithful("no mipmap streaming: no streaming debug data; measured: tests/projects/runtimeapis texture.streaming_debug_properties")]
        public static void SetStreamingTextureMaterialDebugProperties(int materialTextureSlot) { }
        /// <summary>The renderer's id of the texture (0: none yet).</summary>
        internal ulong m_NativeHandle;
        internal int m_Width;
        internal int m_Height;
        /// <summary>CPU blocks of an uninitialized ASTC texture before the measured fallback upload.</summary>
        internal bool m_AstcMemoryPendingUpload;
        /// <summary>Software ASTC setup bytes attributed to this texture by the profiler.</summary>
        internal int m_AstcFallbackBytes;
        FilterMode m_FilterMode = FilterMode.Bilinear;
        TextureWrapMode m_WrapModeU = TextureWrapMode.Repeat;
        TextureWrapMode m_WrapModeV = TextureWrapMode.Repeat;
        internal TextureWrapMode m_WrapModeW = TextureWrapMode.Repeat;
        int m_AnisoLevel = 1;
        float m_MipMapBias;

        protected Texture() { }

        public virtual int width
        {
            get => m_Width;
            set => throw new NotImplementedException("Setting width is not supported for this texture type.");
        }

        public virtual int height
        {
            get => m_Height;
            set => throw new NotImplementedException("Setting height is not supported for this texture type.");
        }

        public FilterMode filterMode { get => m_FilterMode; set { SamplerEdited(m_FilterMode != value); m_FilterMode = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapMode
        {
            get => m_WrapModeU;
            set { SamplerEdited(m_WrapModeU != value || m_WrapModeV != value || m_WrapModeW != value); m_WrapModeU = m_WrapModeV = m_WrapModeW = value; OnSamplerChanged(); }
        }
        public TextureWrapMode wrapModeU { get => m_WrapModeU; set { SamplerEdited(m_WrapModeU != value); m_WrapModeU = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapModeV { get => m_WrapModeV; set { SamplerEdited(m_WrapModeV != value); m_WrapModeV = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapModeW { get => m_WrapModeW; set { SamplerEdited(m_WrapModeW != value); m_WrapModeW = value; OnSamplerChanged(); } }
        public int anisoLevel { get => m_AnisoLevel; set { SamplerEdited(m_AnisoLevel != value); m_AnisoLevel = value; OnSamplerChanged(); } }
        public float mipMapBias { get => m_MipMapBias; set { SamplerEdited(m_MipMapBias != value); m_MipMapBias = value; EmitSamplerMetadata(); } }

        /// <summary>The engine's own change of the mip bias (a supersampled screen capture's), no
        /// edit of an asset.</summary>
        internal void SetMipMapBiasQuietly(float bias)
        {
            m_MipMapBias = bias;
            EmitSamplerMetadata();
        }

        /// <summary>A sampler setting changed by script dirties a texture asset (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/tmpatlas op.Filter, op.Wrap, op.Aniso, op.MipBias; a
        /// setting set to the value it has is unmeasured: left clean).</summary>
        void SamplerEdited(bool changed)
        {
            if (changed && Compat.AssetEditsMeasured && IsNativeAsset) MarkAssetEdited();
        }
        public Vector2 texelSize => new Vector2(m_Width > 0 ? 1f / m_Width : 0f, m_Height > 0 ? 1f / m_Height : 0f);
        [Unbound.EditorFaithful("the base answer, as Unity's: render and webcam textures are not CPU-readable; the readable texture classes override it")]
        public virtual bool isReadable => false;
        public virtual GraphicsFormat graphicsFormat => GraphicsFormatOf();
        internal virtual GraphicsFormat GraphicsFormatOf() => GraphicsFormat.R8G8B8A8_SRGB;

        internal void RecordInitialAstcMemory(TextureFormat format, bool createUninitialized)
        {
            if (createUninitialized)
                m_AstcMemoryPendingUpload = MemorySizes.MeasuresAstcFallback(this, format);
            else
                MemorySizes.RecordAstcFallback(this, format);
        }

        internal void RecordUploadedAstcMemory(TextureFormat format)
        {
            MemorySizes.RecordAstcFallback(this, format);
            m_AstcMemoryPendingUpload = false;
        }

        public virtual TextureDimension dimension
        {
            get => TextureDimension.Tex2D;
            set => throw new NotImplementedException("Setting dimension is not supported for this texture type.");
        }
        public int mipmapCount => MipmapCount();

        /// <summary>The mip count of the kind of texture (mipmapCount is not virtual in Unity: game
        /// code may call it without virtual dispatch).</summary>
        internal virtual int MipmapCount() => 1;
        uint m_UpdateCount;
        public uint updateCount => m_UpdateCount;
        public Hash128 imageContentsHash { get; set; }

        internal virtual void OnSamplerChanged() => EmitSamplingProperties();

        /// <summary>The anisotropic level the device samples the texture at before the global
        /// setting (QualitySettings.anisotropicFiltering and its limits) applies.</summary>
        internal virtual int SamplerAnisoLevel => m_AnisoLevel;

        internal void EmitSamplingProperties()
        {
            if (m_NativeHandle == 0 || !Unbound.Runtime.Native.IsReady) return;
            Unbound.Runtime.GfxStream.Flush();
            Unbound.Runtime.Native.Call("texture-sampling-properties", System.Text.Json.JsonSerializer.Serialize(
                new { handle = m_NativeHandle, filterMode = (int)m_FilterMode,
                    wrapU = (int)m_WrapModeU, wrapV = (int)m_WrapModeV, wrapW = (int)m_WrapModeW, anisoLevel = SamplerAnisoLevel }));
        }

        // The mip bias uses the JSON control channel, outside the texture packet. Initial quiet
        // sampler state must be sent when its first handle is uploaded.
        internal unsafe void UploadTexture(TexturePacket* packet)
        {
            // Kept red first, a B8G8R8A8 color tells the renderer its order, which a copy into or
            // out of a red-first texture moves on a device storing it blue first (Metal).
            if ((packet->RenderTarget & TexturePacket.ScreenOrder) == 0 && TexturePacket.IsBlueFirst(GraphicsFormatOf()))
                packet->RenderTarget |= TexturePacket.BlueFirst;
            Unbound.Runtime.Native.UploadTexture(m_NativeHandle, packet);
            if (m_MipMapBias != 0f) EmitSamplerMetadata();
            SyncMipmapLimit();
        }

        /// <summary>The smallest side a texture keeps under the quality level's mipmap limit.</summary>
        const int MipmapLimitMinimumSide = 8;

        /// <summary>How many top mips the quality level's texture mipmap limit
        /// (QualitySettings.globalTextureMipmapLimit) may drop from a texture of this size and mip
        /// count: never its last mip, nor so many that its smaller side falls below 8 texels
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/mipmaplimit: 64x64 drops at most
        /// 3, 16x16 1, 8x8, 64x8 and 8x64 none, 64x64 with 3 mips 2; sides that are not powers of
        /// two are not measured).</summary>
        internal static int MaxMipmapLimit(int width, int height, int mipCount)
        {
            int side = Math.Min(width, height), drop = 0;
            while (drop < mipCount - 1 && (side >> (drop + 1)) >= MipmapLimitMinimumSide) drop++;
            return drop;
        }

        /// <summary>The most top mips the quality level's mipmap limit drops from this texture
        /// (0 for the kinds it leaves whole and for a texture that ignores it).</summary>
        internal virtual int MipmapLimitMaxDrop => 0;

        /// <summary>The top mips a texture whose CPU copy Apply discarded dropped in that upload
        /// (-1: it follows the limit). Such a texture is not uploaded again when the limit changes
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/mipmaplimit scriptLocked.*; the
        /// Scripting API's Texture2D.Apply and Texture2DArray.Apply say so for both); it follows the
        /// limit again once it has pixels of its own again (Init, a reload).</summary>
        internal int m_FrozenMipmapLimit = -1;

        internal int FrozenMipmapLimit => m_FrozenMipmapLimit;

        /// <summary>Whether the renderer samples fewer mips of this texture than it holds.</summary>
        internal bool UnderMipmapLimit => FrozenMipmapLimit > 0 || (FrozenMipmapLimit < 0 && Math.Min(s_ActiveMipmapLimit, MipmapLimitMaxDrop) > 0);

        /// <summary>A GPU copy or readback of a texture under the mipmap limit reads or writes its
        /// whole chain; the Editor's mip 0 is the dropped size there (Scripting API 6000.0,
        /// Texture2D.activeMipmapLimit). Said once.</summary>
        internal static void WarnCopyUnderMipmapLimit(string api, Texture texture)
        {
            if (texture == null || !texture.UnderMipmapLimit) return;
            // Said once per API, without building its message again.
            lock (s_CopyWarnings)
                if (!s_CopyWarnings.Add(api)) return;
            Unbound.Runtime.Stubs.Ignored(api + " of a texture under the texture mipmap limit", "addresses the texture's whole mip chain, not the mips the limit keeps");
        }

        static readonly System.Collections.Generic.HashSet<string> s_CopyWarnings = new System.Collections.Generic.HashSet<string>();

        /// <summary>What the renderer holds for <see cref="m_SentMipmapLimitHandle"/>: the most mips
        /// it drops and whether that is a fixed count.</summary>
        int m_SentMipmapLimit;
        bool m_SentMipmapLimitFixed;
        ulong m_SentMipmapLimitHandle;

        /// <summary>Tells the renderer the most top mips this texture drops under the quality
        /// level's limit (or the fixed count of a frozen texture), when that changed.</summary>
        internal void SyncMipmapLimit()
        {
            if (m_NativeHandle == 0 || !Unbound.Runtime.Native.IsReady) return;
            int frozen = FrozenMipmapLimit;
            bool isFixed = frozen >= 0;
            int drop = isFixed ? frozen : MipmapLimitMaxDrop;
            bool known = m_SentMipmapLimitHandle == m_NativeHandle;
            int sent = known ? m_SentMipmapLimit : 0;
            bool sentFixed = known && m_SentMipmapLimitFixed;
            m_SentMipmapLimitHandle = m_NativeHandle;
            m_SentMipmapLimit = drop;
            m_SentMipmapLimitFixed = isFixed;
            if (drop == sent && (isFixed == sentFixed || drop == 0)) return;
            Unbound.Runtime.GfxStream.Flush();
            Unbound.Runtime.Native.Call("texture-mipmap-limit", System.Text.Json.JsonSerializer.Serialize(
                new { handle = m_NativeHandle, maxDrop = drop, @fixed = isFixed }));
        }

        internal void EmitSamplerMetadata()
        {
            if (m_NativeHandle == 0 || !Unbound.Runtime.Native.IsReady) return;
            Unbound.Runtime.GfxStream.Flush();
            Unbound.Runtime.Native.Call("texture-sampler-state", System.Text.Json.JsonSerializer.Serialize(
                new { handle = m_NativeHandle, mipBias = m_MipMapBias }));
        }

        internal void ReadSamplerMetadata()
        {
            if (!Unbound.Runtime.Native.IsReady) return;
            m_MipMapBias = System.Text.Json.JsonSerializer.Deserialize<float>(Unbound.Runtime.Native.Call(
                "texture-sampler-state", System.Text.Json.JsonSerializer.Serialize(new { handle = m_NativeHandle })));
        }

        /// <summary>Sets the sampler without telling the renderer (before the texture exists).</summary>
        internal void SetSamplerQuietly(FilterMode filter, TextureWrapMode u, TextureWrapMode v, TextureWrapMode w, int aniso, float mipBias)
        {
            m_FilterMode = filter;
            m_WrapModeU = u;
            m_WrapModeV = v;
            m_WrapModeW = w;
            m_AnisoLevel = aniso;
            m_MipMapBias = mipBias;
        }

        /// <summary>The device's native texture pointer, sharing the renderer's storage
        /// (<see cref="NativePlugins.TexturePointer"/>).</summary>
        public IntPtr GetNativeTexturePtr() => NativePointer();

        internal virtual IntPtr NativePointer() => IsExternalTexture ? ExternalNativePointer() : NativePlugins.TexturePointer(TextureHandles.Of(this), false);

        public void IncrementUpdateCount() => m_UpdateCount++;

        /// <summary>The update count a texture taking a new import in place carries on with.</summary>
        internal void SetUpdateCount(uint count) => m_UpdateCount = count;

        /// <summary>The texture mipmap limit textures are sampled under: what the quality level's
        /// limit sets, and these two set without changing the level's (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/mipmaplimit textureLimits.*, afterMaster3).</summary>
        static int s_ActiveMipmapLimit;

        internal static int ActiveMipmapLimit => s_ActiveMipmapLimit;

        /// <summary>Makes <paramref name="limit"/> (at least 0; a negative limit set here is not
        /// measured, the quality level's reads 0) the limit the renderer samples textures under.</summary>
        internal static void SetActiveMipmapLimit(int limit)
        {
            s_ActiveMipmapLimit = Math.Max(0, limit);
            if (s_ActiveMipmapLimit > 0 && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal)
                Unbound.Runtime.Stubs.Ignored("the texture mipmap limit on Metal", "keeps the 8-texel floor measured on OpenGL Core and Vulkan (tests/projects/mipmaplimit); the Metal device's is not measured");
            Unbound.Runtime.GfxStream.MipmapLimit(s_ActiveMipmapLimit);
        }

        [global::System.@ObsoleteAttribute("masterTextureLimit has been deprecated. Use globalMipmapLimit instead (UnityUpgradable) -> globalMipmapLimit", false)] public static int masterTextureLimit { get => s_ActiveMipmapLimit; set => SetActiveMipmapLimit(value); }
        [global::System.@ObsoleteAttribute("globalMipmapLimit is not supported. Use QualitySettings.globalTextureMipmapLimit or Mipmap Limit Groups instead.", false)] public static int globalMipmapLimit { get => s_ActiveMipmapLimit; set => SetActiveMipmapLimit(value); }
        static AnisotropicFiltering s_Anisotropic = AnisotropicFiltering.Enable;
        static int s_ForcedMinAniso = 9, s_GlobalMaxAniso = 16;

        /// <summary>Disable: no texture filters anisotropically; Enable: as each texture says;
        /// ForceEnable: every texture at least at the forced minimum level.</summary>
        public static AnisotropicFiltering anisotropicFiltering
        {
            get => s_Anisotropic;
            set
            {
                s_Anisotropic = value;
                EmitAnisotropicFiltering();
            }
        }
        public static bool allowThreadedTextureCreation { get; set; } = true;
        public static bool streamingTextureForceLoadAll { get; set; }
        public static bool streamingTextureDiscardUnusedMips { get; set; }
        public static ulong totalTextureMemory { get { Unbound.Runtime.Stubs.Ignored("Texture.totalTextureMemory", "is not tracked yet: it answers 0"); return 0; } }
        public static ulong currentTextureMemory { get { Unbound.Runtime.Stubs.Ignored("Texture.currentTextureMemory", "is not tracked yet: it answers 0"); return 0; } }

        /// <summary>The forced minimum (ForceEnable) and the maximum anisotropic level; -1 keeps
        /// the default (9, 16).</summary>
        public static void SetGlobalAnisotropicFilteringLimits(int forcedMin, int globalMax)
        {
            s_ForcedMinAniso = forcedMin < 0 ? 9 : forcedMin;
            s_GlobalMaxAniso = globalMax < 0 ? 16 : globalMax;
            EmitAnisotropicFiltering();
        }

        internal static void EmitAnisotropicFiltering() =>
            Unbound.Runtime.GfxStream.Anisotropy((int)s_Anisotropic, s_ForcedMinAniso, s_GlobalMaxAniso);
    }
}
