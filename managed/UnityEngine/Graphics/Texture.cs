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
        /// <summary>The renderer's id of the texture (0: none yet).</summary>
        internal ulong m_NativeHandle;
        internal int m_Width;
        internal int m_Height;
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

        public FilterMode filterMode { get => m_FilterMode; set { m_FilterMode = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapMode { get => m_WrapModeU; set { m_WrapModeU = m_WrapModeV = m_WrapModeW = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapModeU { get => m_WrapModeU; set { m_WrapModeU = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapModeV { get => m_WrapModeV; set { m_WrapModeV = value; OnSamplerChanged(); } }
        public TextureWrapMode wrapModeW { get => m_WrapModeW; set { m_WrapModeW = value; OnSamplerChanged(); } }
        public int anisoLevel { get => m_AnisoLevel; set { m_AnisoLevel = value; OnSamplerChanged(); } }
        public float mipMapBias { get => m_MipMapBias; set { m_MipMapBias = value; EmitSamplerMetadata(); } }
        public Vector2 texelSize => new Vector2(m_Width > 0 ? 1f / m_Width : 0f, m_Height > 0 ? 1f / m_Height : 0f);
        [Unbound.EditorFaithful("the base answer, as Unity's: render and webcam textures are not CPU-readable; the readable texture classes override it")]
        public virtual bool isReadable => false;
        public virtual GraphicsFormat graphicsFormat => GraphicsFormatOf();
        internal virtual GraphicsFormat GraphicsFormatOf() => GraphicsFormat.R8G8B8A8_SRGB;

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

        internal void EmitSamplingProperties()
        {
            if (m_NativeHandle == 0 || !Unbound.Runtime.Native.IsReady) return;
            Unbound.Runtime.GfxStream.Flush();
            Unbound.Runtime.Native.Call("texture-sampling-properties", System.Text.Json.JsonSerializer.Serialize(
                new { handle = m_NativeHandle, filterMode = (int)m_FilterMode,
                    wrapU = (int)m_WrapModeU, wrapV = (int)m_WrapModeV, wrapW = (int)m_WrapModeW, anisoLevel = m_AnisoLevel }));
        }

        // The mip bias uses the JSON control channel, outside the texture packet. Initial quiet
        // sampler state must be sent when its first handle is uploaded.
        internal unsafe void UploadTexture(TexturePacket* packet)
        {
            Unbound.Runtime.Native.UploadTexture(m_NativeHandle, packet);
            if (m_MipMapBias != 0f) EmitSamplerMetadata();
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

        [global::System.@ObsoleteAttribute("masterTextureLimit has been deprecated. Use globalMipmapLimit instead (UnityUpgradable) -> globalMipmapLimit", false)] public static int masterTextureLimit { get; set; }
        [global::System.@ObsoleteAttribute("globalMipmapLimit is not supported. Use QualitySettings.globalTextureMipmapLimit or Mipmap Limit Groups instead.", false)] public static int globalMipmapLimit { get; set; }
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
