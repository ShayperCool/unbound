using System;

namespace UnityEngine.Networking
{
    [Flags]
    public enum DownloadedTextureFlags : uint
    {
        None = 0U,
        Readable = 1U,
        MipmapChain = 2U,
        LinearColorSpace = 4U,
    }

    public partial struct DownloadedTextureParams
    {
        public DownloadedTextureFlags flags;
        public int mipmapCount;

        public static DownloadedTextureParams Default => new DownloadedTextureParams
        {
            flags = DownloadedTextureFlags.Readable | DownloadedTextureFlags.MipmapChain,
            mipmapCount = -1,
        };

        public bool readable
        {
            get => (flags & DownloadedTextureFlags.Readable) != 0;
            set => flags = value ? flags | DownloadedTextureFlags.Readable : flags & ~DownloadedTextureFlags.Readable;
        }

        public bool mipmapChain
        {
            get => (flags & DownloadedTextureFlags.MipmapChain) != 0;
            set => flags = value ? flags | DownloadedTextureFlags.MipmapChain : flags & ~DownloadedTextureFlags.MipmapChain;
        }

        public bool linearColorSpace
        {
            get => (flags & DownloadedTextureFlags.LinearColorSpace) != 0;
            set => flags = value ? flags | DownloadedTextureFlags.LinearColorSpace : flags & ~DownloadedTextureFlags.LinearColorSpace;
        }
    }

    /// <summary>Keeps the body and decodes it into a texture (PNG, JPG, ...) on first access.</summary>
    public sealed partial class DownloadHandlerTexture : DownloadHandler
    {
        readonly System.IO.MemoryStream m_Stream = new System.IO.MemoryStream();
        readonly DownloadedTextureParams m_Params;
        Texture2D m_Texture;
        bool m_Decoded;

        public DownloadHandlerTexture() : this(true) { }

        public DownloadHandlerTexture(bool readable)
        {
            m_Params = DownloadedTextureParams.Default;
            m_Params.readable = readable;
        }

        public DownloadHandlerTexture(DownloadedTextureParams parameters)
        {
            m_Params = parameters;
        }

        internal override bool Accept(byte[] data, int length)
        {
            m_Stream.Write(data, 0, length);
            return true;
        }

        internal override byte[] Bytes() => m_Stream.ToArray();

        protected override Unity.Collections.NativeArray<byte> GetNativeData() => base.GetNativeData();

        public Texture2D texture
        {
            get
            {
                if (!m_Decoded && m_IsDone)
                {
                    m_Decoded = true;
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, m_Params.mipmapChain, m_Params.linearColorSpace);
                    if (!texture.LoadImage(Bytes(), !m_Params.readable))
                    {
                        Object.DestroyImmediate(texture);
                        texture = null;
                    }
                    m_Texture = texture;
                }
                return m_Texture;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            m_Stream.Dispose();
        }

        public static Texture2D GetContent(UnityWebRequest www) => GetCheckedDownloader<DownloadHandlerTexture>(www).texture;
    }

    public static partial class UnityWebRequestTexture
    {
        public static UnityWebRequest GetTexture(string uri) => GetTexture(uri, false);

        public static UnityWebRequest GetTexture(Uri uri) => GetTexture(uri, false);

        public static UnityWebRequest GetTexture(string uri, bool nonReadable) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(!nonReadable), null);

        public static UnityWebRequest GetTexture(Uri uri, bool nonReadable) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(!nonReadable), null);

        public static UnityWebRequest GetTexture(string uri, DownloadedTextureParams parameters) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(parameters), null);

        public static UnityWebRequest GetTexture(Uri uri, DownloadedTextureParams parameters) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(parameters), null);
    }
}
