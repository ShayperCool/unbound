using System;

namespace UnityEngine
{
    /// <summary>A 2D texture: imported from an asset, or created by code.</summary>
    [Unbound.Runtime.ClassId(28)]
    public sealed partial class Texture2D : Texture
    {
        internal TextureFormat m_Format = TextureFormat.RGBA32;
        internal int m_MipCount = 1;
        internal bool m_IsReadable;

        Texture2D() { }

        public Texture2D(int width, int height) : this(width, height, TextureFormat.RGBA32, true, false) { }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) : this(width, height, textureFormat, mipChain, false) { }

        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear)
        {
            m_Width = width;
            m_Height = height;
            m_Format = textureFormat;
            m_MipCount = mipChain ? 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2)) : 1;
            m_IsReadable = true;
            OnCreated(linear);
        }

        partial void OnCreated(bool linear);

        public TextureFormat format => m_Format;
        public override bool isReadable => m_IsReadable;
        public override int mipmapCount => m_MipCount;
    }
}
