using System;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A texture cameras can render into.</summary>
    [ClassId(84)]
    public partial class RenderTexture : Texture
    {
        int m_Depth;
        RenderTextureFormat m_Format = RenderTextureFormat.ARGB32;
        bool m_Created;

        internal RenderTexture() { }

        public RenderTexture(int width, int height, int depth) : this(width, height, depth, RenderTextureFormat.Default) { }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
        {
            m_Width = width;
            m_Height = height;
            m_Depth = depth;
            m_Format = format;
        }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) : this(width, height, depth, format) { }

        public RenderTexture(int width, int height, int depth, GraphicsFormat format) : this(width, height, depth) { }

        public RenderTexture(RenderTextureDescriptor desc) : this(desc.width, desc.height, desc.depthBufferBits) { }

        public RenderTexture(RenderTexture textureToCopy) : this(textureToCopy.width, textureToCopy.height, textureToCopy.depth, textureToCopy.format) { }

        public override int width { get => m_Width; set { m_Width = value; m_Created = false; } }
        public override int height { get => m_Height; set { m_Height = value; m_Created = false; } }
        public int depth { get => m_Depth; set => m_Depth = value; }
        public RenderTextureFormat format { get => m_Format; set => m_Format = value; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; } = true;
        public int antiAliasing { get; set; } = 1;
        public bool isPowerOfTwo { get; set; }
        public bool enableRandomWrite { get; set; }
        public bool sRGB => true;
        public TextureDimension dimension { get; set; } = TextureDimension.Tex2D;
        public int volumeDepth { get; set; } = 1;
        public RenderTextureDescriptor descriptor => new RenderTextureDescriptor(m_Width, m_Height, m_Format, m_Depth);

        public static RenderTexture active { get; set; }

        public bool Create()
        {
            EnsureCreated();
            return true;
        }

        public void Release()
        {
            Native.ReleaseTexture(m_NativeHandle);
            m_NativeHandle = 0;
            m_Created = false;
        }

        public bool IsCreated() => m_Created;

        public void DiscardContents() { }

        public void MarkRestoreExpected() { }

        public void GenerateMips() { }

        internal unsafe ulong EnsureCreated()
        {
            if (m_Created) return m_NativeHandle;
            if (m_NativeHandle == 0) m_NativeHandle = RenderHandles.Next();
            var packet = new TexturePacket
            {
                Width = Math.Max(1, m_Width),
                Height = Math.Max(1, m_Height),
                Format = 0,
                MipCount = 1,
                FilterMode = (int)filterMode,
                WrapU = (int)wrapModeU,
                WrapV = (int)wrapModeV,
                AnisoLevel = anisoLevel,
                RenderTarget = 1,
                DepthBits = m_Depth,
            };
            Native.UploadTexture(m_NativeHandle, &packet);
            m_Created = true;
            return m_NativeHandle;
        }

        public static RenderTexture GetTemporary(int width, int height, [Internal.DefaultValue("0")] int depthBuffer,
            [Internal.DefaultValue("RenderTextureFormat.Default")] RenderTextureFormat format,
            [Internal.DefaultValue("RenderTextureReadWrite.Default")] RenderTextureReadWrite readWrite,
            [Internal.DefaultValue("1")] int antiAliasing) => new RenderTexture(width, height, depthBuffer, format);

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite) => new RenderTexture(width, height, depthBuffer, format);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format) => new RenderTexture(width, height, depthBuffer, format);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer) => new RenderTexture(width, height, depthBuffer);
        public static RenderTexture GetTemporary(int width, int height) => new RenderTexture(width, height, 0);
        public static RenderTexture GetTemporary(RenderTextureDescriptor desc) => new RenderTexture(desc);

        public static void ReleaseTemporary(RenderTexture temp)
        {
            if (temp != null) temp.Release();
        }

        public static bool SupportsStencil(RenderTexture rt) => rt == null || rt.depth >= 24;

        internal override void OnEngineDestroy() => Release();
    }

    public partial struct RenderTextureDescriptor
    {
        public RenderTextureDescriptor(int width, int height) : this(width, height, RenderTextureFormat.Default, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat) : this(width, height, colorFormat, 0) { }

        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits)
        {
            this.width = width;
            this.height = height;
            this.colorFormat = colorFormat;
            this.depthBufferBits = depthBufferBits;
            msaaSamples = 1;
            volumeDepth = 1;
            mipCount = 1;
            dimension = TextureDimension.Tex2D;
            useMipMap = false;
            autoGenerateMips = true;
            sRGB = true;
        }

        public int width { get; set; }
        public int height { get; set; }
        public int msaaSamples { get; set; }
        public int volumeDepth { get; set; }
        public int mipCount { get; set; }
        public RenderTextureFormat colorFormat { get; set; }
        public int depthBufferBits { get; set; }
        public TextureDimension dimension { get; set; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; }
        public bool sRGB { get; set; }
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
