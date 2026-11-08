using System;
using System.IO;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine.Networking
{
    /// <summary>
    /// Downloads an AssetBundle and opens it as the request completes; with a version, a hash or a
    /// <see cref="CachedAssetBundle"/> it goes through the AssetBundle cache
    /// (<see cref="BundleCaches"/>). As the Unity 6000.0.71f1 Editor does
    /// (tests/projects/networkcaching):
    ///
    /// The archive is checked when the download completes, whether or not it loads then: a
    /// nonzero CRC that does not match the uncompressed content, or data that is no archive,
    /// completes the request as a DataProcessingError ("Data Processing Error, see Download
    /// Handler error") with the reason as the handler's error ("CRC Mismatch. Provided …,
    /// calculated … from data. Will not load AssetBundle '&lt;url&gt;'", "Failed to decompress data
    /// for the AssetBundle '&lt;url&gt;'."), nothing logged, nothing cached. A cached request
    /// whose version is in a cache completes without a transfer (response code 0, no bytes);
    /// with a nonzero CRC the cached version is checked first and, when it does not match, removed
    /// and downloaded again, a mismatch of that download then completing as a ConnectionError
    /// ("Unable to write data") with no bytes downloaded. Otherwise a cached request keeps the
    /// download in the current cache when there is room (bundle_cache) and the bundle loads from
    /// the cache, which keeps that version in use until the bundle unloads.
    ///
    /// The AssetBundle object is made on the first access to assetBundle (or GetContent), or with
    /// the request when autoLoadAssetBundle is set (it is false by default, documented); when
    /// another bundle of the same files is loaded it is null, the loader's error is logged and
    /// then again as "Error while getting Asset Bundle: …", and the request stays Success.
    /// isDownloadComplete is true once the request has finished, whatever its result. data and
    /// text throw NotSupportedException.
    ///
    /// Unbound's choices (unmeasured): with autoLoadAssetBundle, a bundle that does not load
    /// completes the request as a DataProcessingError with the loader's error as the handler's;
    /// a cached version that does not open is removed and downloaded again; the cached version is
    /// opened on the main thread as the request is sent.
    /// </summary>
    public sealed partial class DownloadHandlerAssetBundle : DownloadHandler
    {
        readonly MemoryStream m_Stream = new MemoryStream();
        readonly string m_Url;
        readonly uint m_Crc;
        readonly bool m_Cached;
        readonly string m_Name;
        readonly Hash128 m_Hash;
        bool m_AutoLoad;
        bool m_DownloadComplete;
        /// <summary>A cached version failed its CRC and is being downloaded again.</summary>
        bool m_Redownload;
        UnityWebRequest.Result? m_Failure;
        /// <summary>The archive opened natively (its `bundle-open` answer), not yet made into an
        /// AssetBundle object.</summary>
        JsonElement? m_Opened;
        BundleCaches.Version m_Version;
        bool m_Made;
        AssetBundle m_AssetBundle;

        public DownloadHandlerAssetBundle(string url, uint crc)
        {
            m_Url = url;
            m_Crc = crc;
        }

        public DownloadHandlerAssetBundle(string url, uint version, uint crc) : this(url, "", new Hash128(0, 0, 0, version), crc) { }

        public DownloadHandlerAssetBundle(string url, Hash128 hash, uint crc) : this(url, "", hash, crc) { }

        public DownloadHandlerAssetBundle(string url, string name, Hash128 hash, uint crc) : this(url, crc)
        {
            m_Cached = true;
            m_Name = name ?? "";
            m_Hash = hash;
        }

        public DownloadHandlerAssetBundle(string url, CachedAssetBundle cachedBundle, uint crc) : this(url, cachedBundle.name, cachedBundle.hash, crc) { }

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
                if (m_DownloadComplete && !m_Made && Make() is { } error)
                    Debug.LogError("Error while getting Asset Bundle: " + error);
                return m_AssetBundle;
            }
        }

        internal override bool Accept(byte[] data, int length)
        {
            m_Stream.Write(data, 0, length);
            return true;
        }

        internal override bool StartRequest()
        {
            if (!m_Cached) return false;
            var (store, version) = BundleCaches.Find(m_Url, m_Name, m_Hash);
            if (version == null) return false;
            var opened = OpenNative(new { path = version.DataPath, crc = m_Crc, reportCrc = true }, out var mismatch);
            if (opened == null || mismatch)
            {
                // The cached copy is not the bundle asked for: it goes, and the bundle downloads,
                // its own result deciding the request's. As measured, the room the copy took
                // stays counted in the cache's occupied space.
                if (version.Users == 0) store.Delete(version, keepCounted: true);
                m_Redownload = mismatch;
                m_Failure = null;
                m_Error = "";
                return false;
            }
            store.Touch(version);
            m_Opened = opened;
            m_Version = version;
            return true;
        }

        internal override void End(bool received)
        {
            m_DownloadComplete = true;
            if (received && m_Opened == null) Receive();
            if (received && m_Failure == null && m_AutoLoad && Make() is { } error)
            {
                m_Error = error;
                m_Failure = UnityWebRequest.Result.DataProcessingError;
            }
            base.End(received);
        }

        /// <summary>Checks and opens the download and, for a cached request, keeps it.</summary>
        void Receive()
        {
            string download = m_Cached ? WriteDownload() : null;
            object request = download != null
                ? new { path = download, crc = m_Crc, reportCrc = true }
                : new { data = Convert.ToBase64String(m_Stream.GetBuffer(), 0, (int)m_Stream.Length), crc = m_Crc, reportCrc = true };
            // The archive opens (or is refused) from here on: the download is not needed any more.
            m_Stream.SetLength(0);
            m_Stream.Capacity = 0;
            try
            {
                m_Opened = OpenNative(request, out _);
                if (m_Opened != null && download != null) m_Version = Keep(download);
            }
            finally
            {
                if (download != null)
                {
                    try { File.Delete(download); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>The download as a file next to the current cache, so keeping it is a move on one
        /// volume; null, with a warning, when it cannot be written (the bundle then loads from
        /// memory, not kept).</summary>
        string WriteDownload()
        {
            var store = BundleCaches.Current;
            var path = Path.Combine(store.Path, ".download-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(store.Path);
                using (var file = File.Create(path)) m_Stream.WriteTo(file);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Unbound: the AssetBundle '{m_Url}' cannot be kept in the cache '{store.Path}': {e.Message}");
                if (File.Exists(path)) File.Delete(path);
                return null;
            }
        }

        /// <summary>Opens an archive natively; null, with the handler's error and failure set,
        /// when it does not.</summary>
        JsonElement? OpenNative(object request, out bool crcMismatch)
        {
            crcMismatch = false;
            JsonElement info;
            try
            {
                info = JsonDocument.Parse(Native.Call("bundle-open", JsonSerializer.Serialize(request))).RootElement;
            }
            catch (Exception e)
            {
                // The reason goes to the log file only: the Editor logs nothing here.
                ConsoleLog.Append($"Unbound: the AssetBundle '{m_Url}' does not open: {e.Message}");
                m_Error = $"Failed to decompress data for the AssetBundle '{m_Url}'.";
                m_Failure = UnityWebRequest.Result.DataProcessingError;
                return null;
            }
            if (!info.TryGetProperty("crcMismatch", out var mismatch)) return info;
            crcMismatch = true;
            m_Error = $"CRC Mismatch. Provided {mismatch.GetProperty("expected").GetUInt32():x}, calculated {mismatch.GetProperty("actual").GetUInt32():x} from data. Will not load AssetBundle '{m_Url}'";
            m_Failure = m_Redownload ? UnityWebRequest.Result.ConnectionError : UnityWebRequest.Result.DataProcessingError;
            return null;
        }

        /// <summary>Keeps the download in the current cache when it has room for it.</summary>
        BundleCaches.Version Keep(string download)
        {
            var store = BundleCaches.Current;
            var name = BundleCaches.NameOf(m_Url, m_Name);
            var form = download + ".cache";
            try
            {
                var answer = JsonDocument.Parse(Native.Call("bundle-cache-form", JsonSerializer.Serialize(new
                {
                    source = download,
                    dest = form,
                    compress = BundleCaches.CompressionEnabled,
                }))).RootElement;
                if (!store.Reserve(answer.GetProperty("required").GetInt64()))
                {
                    ConsoleLog.Append($"Not enough space in cache to write file '{Path.Combine(store.Path, name, m_Hash.ToString())}'.");
                    return null;
                }
                // A version in use stays as it is; the download then loads without being kept.
                return store.Keep(name, m_Hash, form);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                Debug.LogWarning($"Unbound: the AssetBundle '{m_Url}' could not be kept in the cache '{store.Path}': {e.Message}");
                return null;
            }
            finally
            {
                if (File.Exists(form)) File.Delete(form);
            }
        }

        /// <summary>Makes the AssetBundle object once; the loader's error when it is not made.</summary>
        string Make()
        {
            m_Made = true;
            if (m_Opened is not { } opened) return null;
            m_Opened = null;
            m_AssetBundle = AssetBundle.FromOpened(opened, m_Url, out var error);
            if (m_AssetBundle != null && m_Version != null)
            {
                m_AssetBundle.m_CacheVersion = m_Version;
                BundleCaches.Use(m_Version);
            }
            return m_AssetBundle == null ? error : null;
        }

        internal override UnityWebRequest.Result? CompletionFailure => m_Failure;

        protected override byte[] GetData() => throw new NotSupportedException("Raw data access is not supported for asset bundles");

        protected override string GetText() => throw new NotSupportedException("String access is not supported for asset bundles");

        public override void Dispose()
        {
            base.Dispose();
            m_Stream.Dispose();
            // An archive opened for a bundle nobody asked for is closed.
            if (m_Opened is { } opened)
            {
                m_Opened = null;
                try { Native.Call("bundle-close", JsonSerializer.Serialize(new { id = opened.GetProperty("id").GetUInt64() })); }
                catch (Exception) { }
            }
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
