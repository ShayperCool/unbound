using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A render texture's color or depth buffer (or the screen's): what render targets
    /// name when several colors are bound at once.</summary>
    public partial struct RenderBuffer
    {
        internal int m_RenderTextureInstanceID;
        internal IntPtr m_BufferPtr;

        /// <summary>The buffer's kind in <see cref="m_BufferPtr"/>: Unbound has no native pointers.</summary>
        internal const int Color = 1, Depth = 2;

        internal RenderBuffer(RenderTexture rt, int kind)
        {
            m_RenderTextureInstanceID = rt != null ? rt.GetInstanceID() : 0;
            m_BufferPtr = new IntPtr(kind);
        }

        internal RenderTexture Texture => m_RenderTextureInstanceID == 0 ? null : ObjectRegistry.FindById(m_RenderTextureInstanceID) as RenderTexture;

        internal bool IsDepth => m_BufferPtr.ToInt64() == Depth;

        internal RenderBufferLoadAction loadAction { get; set; }

        internal RenderBufferStoreAction storeAction { get; set; }

        internal void SetLoadAction(RenderBufferLoadAction action) => loadAction = action;

        internal void SetStoreAction(RenderBufferStoreAction action) => storeAction = action;

        internal RenderBufferLoadAction GetLoadAction() => loadAction;

        internal RenderBufferStoreAction GetStoreAction() => storeAction;

        public IntPtr GetNativeRenderBufferPtr() => m_BufferPtr;

        /// <summary>The renderer's handle of the texture the buffer belongs to (0: the screen).</summary>
        internal ulong Handle
        {
            get
            {
                var rt = Texture;
                return rt != null ? rt.EnsureCreated() : 0;
            }
        }
    }

    public partial class RenderTexture
    {
        public RenderBuffer colorBuffer => new RenderBuffer(this, RenderBuffer.Color);

        public RenderBuffer depthBuffer => new RenderBuffer(this, RenderBuffer.Depth);
    }

    public partial class Display
    {
        public RenderBuffer colorBuffer => new RenderBuffer(null, RenderBuffer.Color);

        public RenderBuffer depthBuffer => new RenderBuffer(null, RenderBuffer.Depth);
    }

    /// <summary>Colors, a depth buffer and the level, face and slice to render into.</summary>
    public partial struct RenderTargetSetup
    {
        public RenderBuffer[] color;
        public RenderBuffer depth;
        public int mipLevel;
        public CubemapFace cubemapFace;
        public int depthSlice;
        public RenderBufferLoadAction[] colorLoad;
        public RenderBufferStoreAction[] colorStore;
        public RenderBufferLoadAction depthLoad;
        public RenderBufferStoreAction depthStore;

        public RenderTargetSetup(RenderBuffer[] color, RenderBuffer depth, int mip, CubemapFace face, RenderBufferLoadAction[] colorLoad,
            RenderBufferStoreAction[] colorStore, RenderBufferLoadAction depthLoad, RenderBufferStoreAction depthStore)
        {
            this.color = color;
            this.depth = depth;
            mipLevel = mip;
            cubemapFace = face;
            depthSlice = 0;
            this.colorLoad = colorLoad;
            this.colorStore = colorStore;
            this.depthLoad = depthLoad;
            this.depthStore = depthStore;
        }

        internal static RenderBufferLoadAction[] LoadActions(RenderBuffer[] buf)
        {
            var actions = new RenderBufferLoadAction[buf.Length];
            for (int i = 0; i < buf.Length; i++) actions[i] = buf[i].loadAction;
            return actions;
        }

        internal static RenderBufferStoreAction[] StoreActions(RenderBuffer[] buf)
        {
            var actions = new RenderBufferStoreAction[buf.Length];
            for (int i = 0; i < buf.Length; i++) actions[i] = buf[i].storeAction;
            return actions;
        }

        public RenderTargetSetup(RenderBuffer color, RenderBuffer depth) : this(new[] { color }, depth) { }
        public RenderTargetSetup(RenderBuffer color, RenderBuffer depth, int mipLevel) : this(new[] { color }, depth, mipLevel) { }
        public RenderTargetSetup(RenderBuffer color, RenderBuffer depth, int mipLevel, CubemapFace face) : this(new[] { color }, depth, mipLevel, face) { }

        public RenderTargetSetup(RenderBuffer color, RenderBuffer depth, int mipLevel, CubemapFace face, int depthSlice)
            : this(new[] { color }, depth, mipLevel, face)
        {
            this.depthSlice = depthSlice;
        }

        public RenderTargetSetup(RenderBuffer[] color, RenderBuffer depth) : this(color, depth, 0, CubemapFace.Unknown) { }
        public RenderTargetSetup(RenderBuffer[] color, RenderBuffer depth, int mipLevel) : this(color, depth, mipLevel, CubemapFace.Unknown) { }

        public RenderTargetSetup(RenderBuffer[] color, RenderBuffer depth, int mip, CubemapFace face)
            : this(color, depth, mip, face, LoadActions(color), StoreActions(color), depth.loadAction, depth.storeAction) { }
    }

    public partial class Graphics
    {
        public static void SetRenderTarget(RenderBuffer colorBuffer, RenderBuffer depthBuffer) => SetRenderTarget(colorBuffer, depthBuffer, 0, CubemapFace.Unknown, 0);
        public static void SetRenderTarget(RenderBuffer colorBuffer, RenderBuffer depthBuffer, int mipLevel) => SetRenderTarget(colorBuffer, depthBuffer, mipLevel, CubemapFace.Unknown, 0);
        public static void SetRenderTarget(RenderBuffer colorBuffer, RenderBuffer depthBuffer, int mipLevel, CubemapFace face) => SetRenderTarget(colorBuffer, depthBuffer, mipLevel, face, 0);

        public static void SetRenderTarget(RenderBuffer colorBuffer, RenderBuffer depthBuffer, int mipLevel, CubemapFace face, int depthSlice) =>
            SetRenderTargetImpl(new[] { colorBuffer }, depthBuffer, mipLevel, face, depthSlice);

        /// <summary>Several colors at once (the fragment program's outputs in order) and a depth buffer.</summary>
        public static void SetRenderTarget(RenderBuffer[] colorBuffers, RenderBuffer depthBuffer) =>
            SetRenderTargetImpl(colorBuffers, depthBuffer, 0, CubemapFace.Unknown, 0);

        public static void SetRenderTarget(RenderTargetSetup setup) =>
            SetRenderTargetImpl(setup.color, setup.depth, setup.mipLevel, setup.cubemapFace, setup.depthSlice);

        internal static void SetRenderTargetImpl(RenderBuffer[] colorBuffers, RenderBuffer depthBuffer, int mipLevel, CubemapFace face, int depthSlice)
        {
            if (colorBuffers == null || colorBuffers.Length == 0) throw new ArgumentException("colorBuffers must not be empty");
            var first = colorBuffers[0].Texture;
            RenderTexture.SetActiveQuietly(first);
            var colors = new ulong[colorBuffers.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = colorBuffers[i].Handle;
            var depthTexture = depthBuffer.Texture;
            ulong depth = depthTexture == first ? 0 : depthTexture != null ? depthTexture.EnsureCreated() : GfxStream.NoTexture;
            GfxStream.SetTargets(colors, depth, mipLevel, face != CubemapFace.Unknown ? (int)face : depthSlice);
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial struct RenderTargetIdentifier
    {
        /// <summary>The texture a render buffer belongs to (the camera target for the screen's).</summary>
        public RenderTargetIdentifier(RenderBuffer buf, int mipLevel = 0, CubemapFace cubeFace = CubemapFace.Unknown, int depthSlice = 0)
            : this(buf.m_RenderTextureInstanceID == 0 ? BuiltinRenderTextureType.CameraTarget : BuiltinRenderTextureType.RenderTexture)
        {
            m_InstanceID = buf.m_RenderTextureInstanceID;
            m_BufferPointer = buf.m_BufferPtr;
            m_MipLevel = mipLevel;
            m_CubeFace = cubeFace;
            m_DepthSlice = depthSlice;
        }

        public static implicit operator RenderTargetIdentifier(RenderBuffer buf) => new RenderTargetIdentifier(buf);
    }
}
