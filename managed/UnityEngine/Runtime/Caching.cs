using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The AssetBundle caches of <see cref="Caching"/>: an ordered list of directories, each keeping
    /// a downloaded AssetBundle version as <c>&lt;cache&gt;/&lt;name&gt;/&lt;hash&gt;/__data</c> (the
    /// archive, see <c>unbound_assets::bundle_cache</c>) with an <c>__info</c> file, as the Unity
    /// 6000.0.71f1 Editor lays them out (tests/projects/networkcaching). Lookups search every cache in
    /// order; downloads write to the current cache. What a cache holds is indexed from its directory
    /// when it is added and kept in memory afterwards; all of it runs on the main thread.
    ///
    /// Measured rules: a version's name is the given name, else the URL's last path component
    /// without its query and extension; a cache occupies its versions' <c>__data</c> and
    /// <c>__info</c> bytes; its free space is the limit minus that, never below 0; an archive is
    /// kept when the room it needs (<c>bundle-cache-form</c>) is free, after removing the least
    /// recently used versions that are not in use as far as needed, else it is not kept (the
    /// bundle still loads); a version loaded from the cache is in use (a <c>__lock</c> file) until
    /// its bundle unloads, and clearing it fails with a warning, clearing its whole cache fails
    /// silently; operations over all caches stop at the first cache that fails;
    /// <c>ClearCache(expiration)</c> removes the versions unused for longer than
    /// <c>expiration</c> seconds; the expiration delay is kept per cache (at most 150 days) and
    /// written to the versions' <c>__info</c>, but removes nothing within a session, and a cache
    /// added again starts with the default delay and limit.
    ///
    /// Unbound's choices: the default cache is the project's
    /// <c>Library/Unbound/Caching/&lt;company&gt;_&lt;product&gt;</c> (the Editor's is in the
    /// user's cache directory and is shared with the Editor, which Unbound does not write to); a
    /// cache is ready as soon as it is added (Unity reads its index on a thread); versions read
    /// from disk count as last used when their <c>__info</c> was last written.
    /// </summary>
    internal static class BundleCaches
    {
        /// <summary>The longest expiration delay a cache takes: 150 days.</summary>
        internal const int MaximumExpirationDelay = 12960000;
        const string DataFile = "__data";
        const string InfoFile = "__info";
        const string LockFile = "__lock";
        /// <summary>A download written next to a cache while it is checked, and its cache form.</summary>
        internal const string DownloadPrefix = ".download-";
        internal const string FormSuffix = ".cache";

        internal static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        internal sealed class Version
        {
            internal string Name;
            internal Hash128 Hash;
            internal string Directory;
            internal long Size;
            internal long LastUsed;
            internal int Users;
            internal string DataPath => Path.Combine(Directory, DataFile);
        }

        internal sealed class Store
        {
            internal readonly int Handle;
            internal readonly string Path;
            internal readonly bool ReadOnly;
            internal int ExpirationDelay = MaximumExpirationDelay;
            internal long MaximumSpace = long.MaxValue;
            internal long Occupied;
            readonly Dictionary<string, SortedDictionary<Hash128, Version>> m_Versions = new(StringComparer.Ordinal);

            internal Store(int handle, string path, bool readOnly)
            {
                Handle = handle;
                Path = path;
                ReadOnly = readOnly;
            }

            internal long Free => Math.Max(0, MaximumSpace - Occupied);

            internal Version Find(string name, Hash128 hash) =>
                m_Versions.TryGetValue(name, out var versions) && versions.TryGetValue(hash, out var version) ? version : null;

            internal IEnumerable<Version> VersionsOf(string name) =>
                m_Versions.TryGetValue(name, out var versions) ? versions.Values : Array.Empty<Version>();

            IEnumerable<Version> All()
            {
                foreach (var versions in m_Versions.Values)
                    foreach (var version in versions.Values) yield return version;
            }

            /// <summary>Indexes what the directory holds: every <c>name/hash/__data</c>. What cannot be
            /// read is left out, with a warning.</summary>
            internal void Index()
            {
                try { IndexDirectory(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"Unbound: the AssetBundle cache '{Path}' could not be read completely: {e.Message}");
                }
            }

            void IndexDirectory()
            {
                if (!System.IO.Directory.Exists(Path)) return;
                // Downloads a run left behind while checking them are not versions.
                foreach (var stale in System.IO.Directory.GetFiles(Path, DownloadPrefix + "*")) TryDelete(stale);
                foreach (var nameDirectory in System.IO.Directory.GetDirectories(Path))
                {
                    string name = System.IO.Path.GetFileName(nameDirectory);
                    foreach (var directory in System.IO.Directory.GetDirectories(nameDirectory))
                    {
                        var hashName = System.IO.Path.GetFileName(directory);
                        if (hashName.Length != 32 || !File.Exists(System.IO.Path.Combine(directory, DataFile))) continue;
                        var hash = Hash128.Parse(hashName);
                        var info = System.IO.Path.Combine(directory, InfoFile);
                        Add(new Version
                        {
                            Name = name,
                            Hash = hash,
                            Directory = directory,
                            Size = SizeOf(directory),
                            LastUsed = File.Exists(info) ? new DateTimeOffset(File.GetLastWriteTimeUtc(info)).ToUnixTimeSeconds() : Now(),
                        });
                    }
                }
            }

            static long SizeOf(string directory)
            {
                long size = 0;
                foreach (var file in System.IO.Directory.GetFiles(directory)) size += new FileInfo(file).Length;
                return size;
            }

            void Add(Version version)
            {
                if (!m_Versions.TryGetValue(version.Name, out var versions))
                    m_Versions[version.Name] = versions = new SortedDictionary<Hash128, Version>();
                versions[version.Hash] = version;
                Occupied += version.Size;
            }

            void Forget(Version version, bool keepCounted)
            {
                if (!m_Versions.TryGetValue(version.Name, out var versions) || !versions.Remove(version.Hash)) return;
                if (versions.Count == 0) m_Versions.Remove(version.Name);
                if (!keepCounted) Occupied -= version.Size;
            }

            /// <summary>Removes a version that is not in use, files and all. With
            /// <paramref name="keepCounted"/> its size stays in the occupied space, as the Editor
            /// leaves it for a cached version that failed its CRC.</summary>
            internal void Delete(Version version, bool keepCounted = false)
            {
                Forget(version, keepCounted);
                try { System.IO.Directory.Delete(version.Directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                var parent = System.IO.Path.GetDirectoryName(version.Directory);
                try
                {
                    if (System.IO.Directory.Exists(parent) && System.IO.Directory.GetFileSystemEntries(parent).Length == 0)
                        System.IO.Directory.Delete(parent);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            /// <summary>A version's use: its time and <c>__info</c>.</summary>
            internal void Touch(Version version)
            {
                version.LastUsed = Now();
                try
                {
                    WriteInfo(version);
                    long size = SizeOf(version.Directory);
                    Occupied += size - version.Size;
                    version.Size = size;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }

            void WriteInfo(Version version) =>
                File.WriteAllText(System.IO.Path.Combine(version.Directory, InfoFile),
                    "-1\n" + (version.LastUsed + ExpirationDelay).ToString(CultureInfo.InvariantCulture) + "\n1\n" + DataFile + "\n");

            /// <summary>The cache's own <c>__info</c>: the time its expiration delay runs out (the
            /// largest int until a delay is set), then two fields the Editor writes as 1 and 0.</summary>
            internal void WriteCacheInfo(bool delaySet)
            {
                if (!System.IO.Directory.Exists(Path)) return;
                long until = delaySet ? Now() + ExpirationDelay : int.MaxValue;
                try { File.WriteAllText(System.IO.Path.Combine(Path, InfoFile), until.ToString(CultureInfo.InvariantCulture) + "\n1\n0\n"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            /// <summary>Makes room for <paramref name="required"/> bytes, removing the least
            /// recently used versions not in use while it is short; false when it stays short.</summary>
            internal bool Reserve(long required)
            {
                if (required <= Free) return true;
                var candidates = new List<Version>();
                foreach (var version in All()) if (version.Users == 0) candidates.Add(version);
                candidates.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed));
                foreach (var version in candidates)
                {
                    if (required <= Free) break;
                    Delete(version);
                }
                return required <= Free;
            }

            /// <summary>Takes the archive at <paramref name="archive"/> as the version, moving it.</summary>
            internal Version Keep(string name, Hash128 hash, string archive)
            {
                var directory = System.IO.Path.Combine(Path, name, hash.ToString());
                if (Find(name, hash) is { } previous)
                {
                    if (previous.Users > 0) return null;
                    Delete(previous);
                }
                System.IO.Directory.CreateDirectory(directory);
                var data = System.IO.Path.Combine(directory, DataFile);
                File.Move(archive, data, true);
                var version = new Version { Name = name, Hash = hash, Directory = directory, LastUsed = Now() };
                WriteInfo(version);
                version.Size = SizeOf(directory);
                Add(version);
                return version;
            }

            /// <summary>Clears one version; false, with a warning, when it is in use.</summary>
            internal bool ClearVersion(string name, Hash128 hash)
            {
                var version = Find(name, hash);
                if (version == null) return true;
                if (version.Users > 0)
                {
                    WarnInUse(version);
                    return false;
                }
                Delete(version);
                return true;
            }

            /// <summary>Clears every version of a name, but <paramref name="keep"/> when
            /// <paramref name="keepOne"/>; the versions in use stay, each with a warning.</summary>
            internal bool ClearVersions(string name, Hash128 keep, bool keepOne)
            {
                bool cleared = true;
                foreach (var version in new List<Version>(VersionsOf(name)))
                {
                    if (keepOne && version.Hash == keep) continue;
                    if (version.Users > 0)
                    {
                        WarnInUse(version);
                        cleared = false;
                        continue;
                    }
                    Delete(version);
                }
                return cleared;
            }

            /// <summary>Clears the whole cache: its directory is made again, empty. Nothing is
            /// cleared while a version is in use.</summary>
            internal bool Clear()
            {
                foreach (var version in All()) if (version.Users > 0) return false;
                m_Versions.Clear();
                Occupied = 0;
                try
                {
                    if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, true);
                    System.IO.Directory.CreateDirectory(Path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return true;
            }

            /// <summary>Removes the versions not used for longer than <paramref name="expiration"/>
            /// seconds and not in use.</summary>
            internal bool ClearOlderThan(int expiration)
            {
                long limit = Now() - expiration;
                foreach (var version in new List<Version>(All()))
                    if (version.LastUsed < limit && version.Users == 0) Delete(version);
                return true;
            }

            static void WarnInUse(Version version) =>
                Debug.LogWarning($"AssetBundle '{version.Name}' with hash '{version.Hash}' is still in use. ");
        }

        static readonly List<Store> s_Stores = new List<Store>();
        static int s_NextHandle;
        static Store s_Default, s_Current;
        internal static bool CompressionEnabled = true;

        internal static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static void EnsureDefault()
        {
            if (s_Default != null) return;
            string folder = ProjectInfo.CompanyName + "_" + ProjectInfo.ProductName;
            var path = Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "Caching", folder).Replace('\\', '/');
            s_Default = new Store(++s_NextHandle, path, false);
            s_Stores.Insert(0, s_Default);
            s_Current = s_Default;
            s_Default.Index();
        }

        internal static int Count
        {
            get { EnsureDefault(); return s_Stores.Count; }
        }

        internal static Store Default
        {
            get { EnsureDefault(); return s_Default; }
        }

        internal static Store Current
        {
            get { EnsureDefault(); return s_Current; }
            set { EnsureDefault(); s_Current = value; }
        }

        internal static Store ByHandle(int handle)
        {
            EnsureDefault();
            foreach (var store in s_Stores) if (store.Handle == handle) return store;
            return null;
        }

        internal static int IndexOf(int handle)
        {
            EnsureDefault();
            for (int i = 0; i < s_Stores.Count; i++) if (s_Stores[i].Handle == handle) return i;
            return -1;
        }

        internal static Store At(int index)
        {
            EnsureDefault();
            if (index < 0 || index >= s_Stores.Count)
                throw new IndexOutOfRangeException("Invalid input cache index " + index.ToString(CultureInfo.InvariantCulture));
            return s_Stores[index];
        }

        internal static Store ByPath(string path)
        {
            EnsureDefault();
            foreach (var store in s_Stores) if (store.Path == path) return store;
            return null;
        }

        internal static Store Add(string path, bool readOnly)
        {
            EnsureDefault();
            var store = new Store(++s_NextHandle, path, readOnly);
            store.Index();
            store.WriteCacheInfo(false);
            s_Stores.Add(store);
            return store;
        }

        /// <summary>Removes a cache from the list (not its files); the default cache stays.</summary>
        internal static bool Remove(Store store)
        {
            if (store == s_Default)
            {
                Debug.LogWarning("Cannot remove the default cache.");
                return false;
            }
            s_Stores.Remove(store);
            if (s_Current == store) s_Current = s_Default;
            return true;
        }

        internal static void Move(Store source, Store destination, bool after)
        {
            if (source == destination) return;
            s_Stores.Remove(source);
            int at = s_Stores.IndexOf(destination);
            s_Stores.Insert(after ? at + 1 : at, source);
        }

        /// <summary>The name a version is kept under: the given one, else the URL's last path
        /// component without its query and extension.</summary>
        internal static string NameOf(string url, string name)
        {
            if (!string.IsNullOrEmpty(name)) return name;
            var path = (url ?? "").Replace('\\', '/');
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);
            path = path.Substring(path.LastIndexOf('/') + 1);
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path.Substring(0, dot) : path;
        }

        /// <summary>The version a request names, in the first cache that has it.</summary>
        internal static (Store Store, Version Version) Find(string url, string name, Hash128 hash)
        {
            EnsureDefault();
            name = NameOf(url, name);
            foreach (var store in s_Stores)
                if (store.Find(name, hash) is { } version) return (store, version);
            return (null, null);
        }

        internal static Hash128[] VersionsOf(string name)
        {
            EnsureDefault();
            var hashes = new SortedSet<Hash128>();
            foreach (var store in s_Stores)
                foreach (var version in store.VersionsOf(name)) hashes.Add(version.Hash);
            var result = new Hash128[hashes.Count];
            hashes.CopyTo(result);
            return result;
        }

        /// <summary>Runs <paramref name="clear"/> on each cache in order, stopping at the first
        /// that fails.</summary>
        internal static bool ClearEach(Func<Store, bool> clear)
        {
            EnsureDefault();
            foreach (var store in s_Stores.ToArray())
                if (!clear(store)) return false;
            return true;
        }

        /// <summary>A bundle loaded from a version uses it until it unloads.</summary>
        internal static void Use(Version version)
        {
            if (version.Users++ == 0)
            {
                try { File.WriteAllBytes(Path.Combine(version.Directory, LockFile), Array.Empty<byte>()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static void Release(Version version)
        {
            if (version.Users == 0 || --version.Users > 0) return;
            try { File.Delete(Path.Combine(version.Directory, LockFile)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Forgets every cache (tests).</summary>
        internal static void Reset()
        {
            s_Stores.Clear();
            s_Default = s_Current = null;
            CompressionEnabled = true;
        }
    }
}

namespace UnityEngine
{
    using Unbound.Runtime;

    /// <summary>A cache of downloaded AssetBundles, by handle (<see cref="BundleCaches"/>).</summary>
    public partial struct Cache : IEquatable<Cache>
    {
        int m_Handle;

        internal Cache(int handle) { m_Handle = handle; }

        internal BundleCaches.Store Store => BundleCaches.ByHandle(m_Handle) ?? throw new InvalidOperationException("Cache is invalid.");

        public static bool operator ==(Cache lhs, Cache rhs) => lhs.m_Handle == rhs.m_Handle;

        public static bool operator !=(Cache lhs, Cache rhs) => lhs.m_Handle != rhs.m_Handle;

        public override int GetHashCode() => m_Handle;

        public override bool Equals(object other) => other is Cache cache && Equals(cache);

        public bool Equals(Cache other) => m_Handle == other.m_Handle;

        public bool valid => BundleCaches.ByHandle(m_Handle) != null;

        /// <summary>Unbound indexes a cache when it is added, so a valid cache is ready at once.</summary>
        public bool ready => Store != null;

        public bool readOnly => Store.ReadOnly;

        public string path => Store.Path;

        public int index => BundleCaches.IndexOf(m_Handle);

        public long spaceFree => Store.Free;

        public long maximumAvailableStorageSpace
        {
            get => Store.MaximumSpace;
            set => Store.MaximumSpace = value;
        }

        public long spaceOccupied => Store.Occupied;

        public int expirationDelay
        {
            get => Store.ExpirationDelay;
            set
            {
                var store = Store;
                store.ExpirationDelay = value;
                if (value > BundleCaches.MaximumExpirationDelay)
                {
                    Debug.LogError("Cache expiration may not be higher then " + BundleCaches.MaximumExpirationDelay.ToString(CultureInfo.InvariantCulture));
                    store.ExpirationDelay = BundleCaches.MaximumExpirationDelay;
                }
                store.WriteCacheInfo(true);
            }
        }

        public bool ClearCache() => Store.Clear();

        public bool ClearCache(int expiration) => Store.ClearOlderThan(expiration);
    }

    public partial struct CachedAssetBundle
    {
        string m_Name;
        Hash128 m_Hash;

        public CachedAssetBundle(string name, Hash128 hash)
        {
            m_Name = name;
            m_Hash = hash;
        }

        public string name
        {
            get => m_Name;
            set => m_Name = value;
        }

        public Hash128 hash
        {
            get => m_Hash;
            set => m_Hash = value;
        }
    }

    /// <summary>The AssetBundle caches (<see cref="BundleCaches"/>).</summary>
    public sealed partial class Caching
    {
        [Unbound.EditorFaithful("the Editor's cache is ready before any script runs")]
        public static bool ready => true;

        public static bool compressionEnabled
        {
            get => BundleCaches.CompressionEnabled;
            set => BundleCaches.CompressionEnabled = value;
        }

        public static bool ClearCache() => BundleCaches.ClearEach(store => store.Clear());

        public static bool ClearCache(int expiration) => BundleCaches.ClearEach(store => store.ClearOlderThan(expiration));

        /// <summary>Unity's ArgumentNullException(paramName), with the message its runtime gives it.</summary>
        static ArgumentNullException NullArgument(string paramName) =>
            new ArgumentNullException(null, "Value cannot be null.\nParameter name: " + paramName);

        static void CheckName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Input AssetBundle name cannot be null or empty.");
        }

        static void CheckUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentException("Input AssetBundle url cannot be null or empty.");
        }

        public static bool ClearCachedVersion(string assetBundleName, Hash128 hash)
        {
            CheckName(assetBundleName);
            return BundleCaches.ClearEach(store => store.ClearVersion(assetBundleName, hash));
        }

        public static bool ClearOtherCachedVersions(string assetBundleName, Hash128 hash)
        {
            CheckName(assetBundleName);
            return BundleCaches.ClearEach(store => store.ClearVersions(assetBundleName, hash, true));
        }

        public static bool ClearAllCachedVersions(string assetBundleName)
        {
            CheckName(assetBundleName);
            return BundleCaches.ClearEach(store => store.ClearVersions(assetBundleName, default, false));
        }

        /// <summary>Adds the versions of every cache, sorted, to the list (which is not cleared).</summary>
        public static void GetCachedVersions(string assetBundleName, List<Hash128> outCachedVersions)
        {
            CheckName(assetBundleName);
            if (outCachedVersions == null) throw NullArgument("Input outCachedVersions cannot be null.");
            outCachedVersions.AddRange(BundleCaches.VersionsOf(assetBundleName));
        }

        [Obsolete("Please use IsVersionCached with Hash128 instead.")]
        public static bool IsVersionCached(string url, int version) => IsVersionCached(url, new Hash128(0, 0, 0, (uint)version));

        public static bool IsVersionCached(string url, Hash128 hash)
        {
            CheckUrl(url);
            return BundleCaches.Find(url, "", hash).Version != null;
        }

        public static bool IsVersionCached(CachedAssetBundle cachedBundle)
        {
            CheckName(cachedBundle.name);
            return BundleCaches.Find("", cachedBundle.name, cachedBundle.hash).Version != null;
        }

        [Obsolete("Please use MarkAsUsed with Hash128 instead.")]
        public static bool MarkAsUsed(string url, int version) => MarkAsUsed(url, new Hash128(0, 0, 0, (uint)version));

        public static bool MarkAsUsed(string url, Hash128 hash)
        {
            CheckUrl(url);
            return MarkAsUsed(url, "", hash);
        }

        public static bool MarkAsUsed(CachedAssetBundle cachedBundle)
        {
            CheckName(cachedBundle.name);
            return MarkAsUsed("", cachedBundle.name, cachedBundle.hash);
        }

        internal static bool MarkAsUsed(string url, string name, Hash128 hash)
        {
            var (store, version) = BundleCaches.Find(url, name, hash);
            if (version == null) return false;
            store.Touch(version);
            return true;
        }

        // The backup flag is an iOS file attribute; the Editor has no such attribute to set.
        [Obsolete("Please use SetNoBackupFlag with Hash128 instead.")]
        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void SetNoBackupFlag(string url, int version) { }

        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void SetNoBackupFlag(string url, Hash128 hash) { }

        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void SetNoBackupFlag(CachedAssetBundle cachedBundle) { }

        [Obsolete("Please use ResetNoBackupFlag with Hash128 instead.")]
        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void ResetNoBackupFlag(string url, int version) { }

        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void ResetNoBackupFlag(string url, Hash128 hash) { }

        [Unbound.EditorFaithful("the no-backup flag only applies to iOS players")]
        public static void ResetNoBackupFlag(CachedAssetBundle cachedBundle) { }

        [Obsolete("This function is obsolete and will always return -1. Use IsVersionCached instead.")]
        [Unbound.EditorFaithful("documented to always return -1")]
        public static int GetVersionFromCache(string url) => -1;

        public static Cache AddCache(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath))
                throw NullArgument("Cache path cannot be null or empty.");
            bool readOnly = false;
            if (cachePath.Replace('\\', '/').StartsWith(Application.streamingAssetsPath, StringComparison.Ordinal))
                readOnly = true;
            else
            {
                if (!Directory.Exists(cachePath)) throw new ArgumentException("Cache path '" + cachePath + "' doesn't exist.");
                if ((File.GetAttributes(cachePath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) readOnly = true;
            }
            if (GetCacheByPath(cachePath).valid) throw new InvalidOperationException("Cache with path '" + cachePath + "' has already been added.");
            return new Cache(BundleCaches.Add(cachePath, readOnly).Handle);
        }

        public static Cache GetCacheAt(int cacheIndex) => new Cache(BundleCaches.At(cacheIndex).Handle);

        public static Cache GetCacheByPath(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) throw new ArgumentException("Invalid empty input cache path.");
            return BundleCaches.ByPath(cachePath) is { } store ? new Cache(store.Handle) : default;
        }

        public static void GetAllCachePaths(List<string> cachePaths)
        {
            cachePaths.Clear();
            for (int i = 0; i < cacheCount; i++) cachePaths.Add(GetCacheAt(i).path);
        }

        public static bool RemoveCache(Cache cache) =>
            BundleCaches.Remove(BundleCaches.ByHandle(cache.GetHashCode()) ?? throw new InvalidOperationException("Input cache is invalid."));

        public static void MoveCacheBefore(Cache src, Cache dst) => Move(src, dst, false);

        public static void MoveCacheAfter(Cache src, Cache dst) => Move(src, dst, true);

        static void Move(Cache src, Cache dst, bool after)
        {
            var source = BundleCaches.ByHandle(src.GetHashCode()) ?? throw new ArgumentException("Cache src is invalid.");
            var destination = BundleCaches.ByHandle(dst.GetHashCode()) ?? throw new ArgumentException("Cache dst is invalid.");
            BundleCaches.Move(source, destination, after);
        }

        public static int cacheCount => BundleCaches.Count;

        public static Cache defaultCache => new Cache(BundleCaches.Default.Handle);

        public static Cache currentCacheForWriting
        {
            get => new Cache(BundleCaches.Current.Handle);
            set
            {
                var store = BundleCaches.ByHandle(value.GetHashCode()) ?? throw new InvalidOperationException("The input cache is invalid.");
                if (store.ReadOnly) throw new InvalidOperationException("The input cache '" + store.Path + "' is readonly.");
                BundleCaches.Current = store;
            }
        }

        [Obsolete("This function is obsolete. Please use ClearCache.  (UnityUpgradable) -> ClearCache()")]
        public static bool CleanCache() => ClearCache();

        [Obsolete("Please use use Cache.spaceOccupied to get used bytes per cache.")]
        public static int spaceUsed => (int)spaceOccupied;

        [Obsolete("This property is only used for the current cache, use Cache.spaceOccupied to get used bytes per cache.")]
        public static long spaceOccupied => currentCacheForWriting.spaceOccupied;

        [Obsolete("Please use use Cache.spaceOccupied to get used bytes per cache.")]
        public static int spaceAvailable => (int)spaceFree;

        [Obsolete("This property is only used for the current cache, use Cache.spaceFree to get unused bytes per cache.")]
        public static long spaceFree => currentCacheForWriting.spaceFree;

        [Obsolete("This property is only used for the current cache, use Cache.maximumAvailableStorageSpace to access the maximum available storage space per cache.")]
        public static long maximumAvailableDiskSpace
        {
            get => currentCacheForWriting.maximumAvailableStorageSpace;
            set
            {
                var cache = currentCacheForWriting;
                cache.maximumAvailableStorageSpace = value;
            }
        }

        [Obsolete("This property is only used for the current cache, use Cache.expirationDelay to access the expiration delay per cache.")]
        public static int expirationDelay
        {
            get => currentCacheForWriting.expirationDelay;
            set
            {
                var cache = currentCacheForWriting;
                cache.expirationDelay = value;
            }
        }
    }
}
