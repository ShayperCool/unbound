using System;

namespace UnityEngine.Networking
{
    /// <summary>
    /// Keeps the downloaded bytes and loads them as an AssetBundle through AssetBundle.LoadFromMemory,
    /// with the handler's CRC. Unity's documentation (6000.0, Scripting API and the installed module
    /// documentation) guarantees: a nonzero CRC that does not match logs an error and loads nothing;
    /// with autoLoadAssetBundle the bundle is loaded as part of the request, otherwise on the first
    /// access of assetBundle; autoLoadAssetBundle is false by default ("Default: false.",
    /// https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/networking/downloadhandlerassetbundle/autoloadassetbundle);
    /// an error decoding the bundle is logged and assetBundle returns null; assetBundle and GetContent
    /// give the bundle or null; data and text throw NotSupportedException; isDownloadComplete is true
    /// once the download part is complete.
    ///
    /// Unbound's choices (unmeasured): by default (load on access) data that does not load leaves
    /// the request's result Success; the first access logs the loader's error once, sets the
    /// handler's error and returns null, and later accesses return null without loading again.
    /// With autoLoadAssetBundle the bundle is loaded when the request completes, before its isDone,
    /// its completed event and waiting coroutines see it; data that does not load completes the
    /// request as DataProcessingError (Unity documents that result for data "corrupted or not in
    /// the correct format"), with the loader's error logged and an exception naming the URL. The
    /// wording of the handler's messages is Unbound's own.
    ///
    /// The versioned, hashed and CachedAssetBundle constructors do not use a cache: Unbound has no
    /// AssetBundle cache yet. They warn once, then download and load the bundle every time, as the
    /// plain constructor does; nothing reports a cache hit.
    /// </summary>
    public sealed partial class DownloadHandlerAssetBundle : DownloadHandler
    {
        readonly System.IO.MemoryStream m_Stream = new System.IO.MemoryStream();
        readonly string m_Url;
        readonly uint m_Crc;
        bool m_AutoLoad;
        bool m_DownloadComplete;
        bool m_LoadAttempted;
        AssetBundle m_AssetBundle;

        public DownloadHandlerAssetBundle(string url, uint crc)
        {
            m_Url = url;
            m_Crc = crc;
        }

        public DownloadHandlerAssetBundle(string url, uint version, uint crc) : this(url, crc) => WarnUncached();

        public DownloadHandlerAssetBundle(string url, Hash128 hash, uint crc) : this(url, crc) => WarnUncached();

        public DownloadHandlerAssetBundle(string url, string name, Hash128 hash, uint crc) : this(url, crc) => WarnUncached();

        public DownloadHandlerAssetBundle(string url, CachedAssetBundle cachedBundle, uint crc) : this(url, crc) => WarnUncached();

        /// <summary>One warning for every cached request: the cache is not there, so each downloads.</summary>
        static void WarnUncached() =>
            Unbound.Runtime.Stubs.Ignored("DownloadHandlerAssetBundle caching",
                "is not supported yet: Unbound does not cache AssetBundles, so a versioned, hashed or CachedAssetBundle request downloads and loads the bundle every time");

        public bool autoLoadAssetBundle
        {
            get => m_AutoLoad;
            set => m_AutoLoad = value;
        }

        public bool isDownloadComplete => m_DownloadComplete;

        public AssetBundle assetBundle
        {
            get
            {
                if (m_DownloadComplete && !m_LoadAttempted) Load();
                return m_AssetBundle;
            }
        }

        internal override bool Accept(byte[] data, int length)
        {
            m_Stream.Write(data, 0, length);
            return true;
        }

        /// <summary>Loads once; a failure keeps the handler's error, the loader has logged why.</summary>
        bool Load()
        {
            m_LoadAttempted = true;
            m_AssetBundle = AssetBundle.LoadFromMemory(m_Stream.ToArray(), m_Crc);
            if (m_AssetBundle != null) return true;
            m_Error = $"Unbound: the data downloaded from '{m_Url}' could not be loaded as an AssetBundle";
            return false;
        }

        internal override void End(bool received)
        {
            m_DownloadComplete = received;
            bool loaded = !received || !m_AutoLoad || Load();
            base.End(received);
            // The request reports a data processing error for the exception (UnityWebRequest.Finish).
            // Known difference, unresolved, pending a Root decision: Finish also logs this exception,
            // so a failed automatic load gives two console entries (the loader's error plus a
            // LogType.Exception naming the URL) where Unity documents one logged error.
            if (!loaded) throw new InvalidOperationException(m_Error);
        }

        protected override byte[] GetData() => throw new NotSupportedException("Unbound: DownloadHandlerAssetBundle does not expose the downloaded bytes; read the bundle from assetBundle or DownloadHandlerAssetBundle.GetContent");

        protected override string GetText() => throw new NotSupportedException("Unbound: DownloadHandlerAssetBundle has no text form of the download; read the bundle from assetBundle or DownloadHandlerAssetBundle.GetContent");

        public override void Dispose()
        {
            base.Dispose();
            m_Stream.Dispose();
        }

        public static AssetBundle GetContent(UnityWebRequest www) => GetCheckedDownloader<DownloadHandlerAssetBundle>(www).assetBundle;
    }

    public static partial class UnityWebRequestAssetBundle
    {
        static string UrlOf(Uri uri) => uri == null ? null : uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;

        static UnityWebRequest Get(string uri, DownloadHandlerAssetBundle handler) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, handler, null);

        static UnityWebRequest Get(Uri uri, DownloadHandlerAssetBundle handler) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, handler, null);

        public static UnityWebRequest GetAssetBundle(string uri) => GetAssetBundle(uri, 0U);

        public static UnityWebRequest GetAssetBundle(Uri uri) => GetAssetBundle(uri, 0U);

        public static UnityWebRequest GetAssetBundle(string uri, uint crc) => Get(uri, new DownloadHandlerAssetBundle(uri, crc));

        public static UnityWebRequest GetAssetBundle(Uri uri, uint crc) => Get(uri, new DownloadHandlerAssetBundle(UrlOf(uri), crc));

        public static UnityWebRequest GetAssetBundle(string uri, uint version, uint crc) => Get(uri, new DownloadHandlerAssetBundle(uri, version, crc));

        public static UnityWebRequest GetAssetBundle(Uri uri, uint version, uint crc) => Get(uri, new DownloadHandlerAssetBundle(UrlOf(uri), version, crc));

        public static UnityWebRequest GetAssetBundle(string uri, Hash128 hash, uint crc = 0U) => Get(uri, new DownloadHandlerAssetBundle(uri, hash, crc));

        public static UnityWebRequest GetAssetBundle(Uri uri, Hash128 hash, uint crc = 0U) => Get(uri, new DownloadHandlerAssetBundle(UrlOf(uri), hash, crc));

        public static UnityWebRequest GetAssetBundle(string uri, CachedAssetBundle cachedAssetBundle, uint crc = 0U) =>
            Get(uri, new DownloadHandlerAssetBundle(uri, cachedAssetBundle, crc));

        public static UnityWebRequest GetAssetBundle(Uri uri, CachedAssetBundle cachedAssetBundle, uint crc = 0U) =>
            Get(uri, new DownloadHandlerAssetBundle(UrlOf(uri), cachedAssetBundle, crc));
    }
}
