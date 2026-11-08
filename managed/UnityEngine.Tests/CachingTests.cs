using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Caching and Cache API over directories, as measured in the Unity 6000.0.71f1
    /// Editor (tests/projects/networkcaching): names taken from URLs, the cache list and its order,
    /// the errors, the limits, and clearing versions in use.</summary>
    [Collection("Frames")]
    public class CachingTests : IDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), $"unbound-caching-{Guid.NewGuid():N}");
        readonly string previousProject = ProjectInfo.ProjectPath;
        readonly List<(LogType Type, string Message)> logs = new List<(LogType, string)>();

        public CachingTests()
        {
            Directory.CreateDirectory(directory);
            ProjectInfo.ProjectPath = directory;
            BundleCaches.Reset();
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            BundleCaches.Reset();
            ProjectInfo.ProjectPath = previousProject;
            Directory.Delete(directory, true);
        }

        void Capture(string message, string stack, LogType type)
        {
            lock (logs) logs.Add((type, message));
        }

        string Dir(string name)
        {
            var path = Path.Combine(directory, name);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>A version kept in a cache as a download would be.</summary>
        static void Keep(Cache cache, string name, Hash128 hash, int size = 100)
        {
            var archive = Path.GetTempFileName();
            File.WriteAllBytes(archive, new byte[size]);
            cache.Store.Keep(name, hash, archive);
        }

        [Theory]
        [InlineData("http://host/dir/alpha?v=1", "alpha")]
        [InlineData("http://host/dir/beta.bundle", "beta")]
        [InlineData("http://host/dir/gamma.tar.gz?x=1.2", "gamma.tar")]
        [InlineData("alpha", "alpha")]
        [InlineData(@"C:\bundles\delta.unity3d", "delta")]
        public void AVersionsNameIsTheUrlsLastComponentWithoutQueryAndExtension(string url, string name)
        {
            Assert.Equal(name, BundleCaches.NameOf(url, ""));
            Assert.Equal("given", BundleCaches.NameOf(url, "given"));
        }

        [Fact]
        public void TheDefaultCacheComesFirstAndIsReady()
        {
            Assert.True(Caching.ready);
            Assert.Equal(1, Caching.cacheCount);
            var cache = Caching.defaultCache;
            Assert.True(cache.valid);
            Assert.True(cache.ready);
            Assert.Equal(0, cache.index);
            Assert.Equal(cache, Caching.currentCacheForWriting);
            Assert.Equal(12960000, cache.expirationDelay);
            Assert.Equal(long.MaxValue, cache.maximumAvailableStorageSpace);
            Assert.Equal(long.MaxValue, cache.spaceFree);
            Assert.Equal(Path.Combine(directory, "Library", "Unbound", "Caching", ProjectInfo.CompanyName + "_" + ProjectInfo.ProductName).Replace('\\', '/'), cache.path);
            Assert.False(Directory.Exists(cache.path), "made when it first keeps something");
            Assert.True(Caching.compressionEnabled);
            Assert.False(default(Cache).valid);
            Assert.Equal(-1, default(Cache).index);
            Assert.Equal("Cache is invalid.", Assert.Throws<InvalidOperationException>(() => default(Cache).path).Message);
        }

        [Fact]
        public void AddingCachesChecksThePathAndKeepsTheOrderAsMoved()
        {
            var missing = Path.Combine(directory, "missing");
            Assert.Equal($"Cache path '{missing}' doesn't exist.", Assert.Throws<ArgumentException>(() => Caching.AddCache(missing)).Message);
            Assert.Equal("Value cannot be null.\nParameter name: Cache path cannot be null or empty.", Assert.Throws<ArgumentNullException>(() => Caching.AddCache("")).Message);
            var a = Caching.AddCache(Dir("a"));
            Assert.Equal($"Cache with path '{Dir("a")}' has already been added.", Assert.Throws<InvalidOperationException>(() => Caching.AddCache(Dir("a"))).Message);
            var aSlash = Caching.AddCache(Dir("a") + "/");
            Assert.NotEqual(a, aSlash);
            Assert.True(File.Exists(Path.Combine(Dir("a"), "__info")));
            var b = Caching.AddCache(Dir("b"));
            Caching.MoveCacheBefore(b, a);
            Assert.Equal((0, 1, 2, 3), (Caching.defaultCache.index, b.index, a.index, aSlash.index));
            Caching.MoveCacheAfter(b, aSlash);
            Assert.Equal((1, 2, 3), (a.index, aSlash.index, b.index));
            Assert.Equal("Invalid input cache index 4", Assert.Throws<IndexOutOfRangeException>(() => Caching.GetCacheAt(4)).Message);
            Assert.Equal(b, Caching.GetCacheByPath(Dir("b")));
            Assert.False(Caching.GetCacheByPath(Path.Combine(directory, "nowhere")).valid);
            Assert.Equal("Invalid empty input cache path.", Assert.Throws<ArgumentException>(() => Caching.GetCacheByPath("")).Message);
            Assert.Equal("Cache src is invalid.", Assert.Throws<ArgumentException>(() => Caching.MoveCacheBefore(default, a)).Message);

            Caching.currentCacheForWriting = b;
            Assert.True(Caching.RemoveCache(b));
            Assert.False(b.valid);
            Assert.Equal(Caching.defaultCache, Caching.currentCacheForWriting);
            Assert.True(Directory.Exists(Dir("b")), "removing a cache keeps its files");
            Assert.False(Caching.RemoveCache(Caching.defaultCache));
            Assert.Contains((LogType.Warning, "Cannot remove the default cache."), logs);
            Assert.Equal("Input cache is invalid.", Assert.Throws<InvalidOperationException>(() => Caching.RemoveCache(b)).Message);
            Assert.Equal("The input cache is invalid.", Assert.Throws<InvalidOperationException>(() => Caching.currentCacheForWriting = default).Message);
        }

        [Fact]
        public void AReadOnlyDirectoryMakesAReadOnlyCache()
        {
            if (OperatingSystem.IsWindows()) return;
            var info = new DirectoryInfo(Dir("ro"));
            info.Attributes |= FileAttributes.ReadOnly;
            try
            {
                var cache = Caching.AddCache(info.FullName);
                Assert.True(cache.readOnly);
                Assert.Equal($"The input cache '{info.FullName}' is readonly.",
                    Assert.Throws<InvalidOperationException>(() => Caching.currentCacheForWriting = cache).Message);
            }
            finally
            {
                info.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        [Fact]
        public void LimitsAndDelaysAreKeptPerCache()
        {
            var cache = Caching.AddCache(Dir("c"));
            cache.expirationDelay = 100;
            Assert.Equal(100, cache.expirationDelay);
            cache.expirationDelay = 13000000;
            Assert.Equal(12960000, cache.expirationDelay);
            Assert.Contains((LogType.Error, "Cache expiration may not be higher then 12960000"), logs);
            cache.expirationDelay = -5;
            Assert.Equal(-5, cache.expirationDelay);
            cache.maximumAvailableStorageSpace = 150;
            Keep(cache, "x", new Hash128(1, 0, 0, 0));
            Assert.Equal(100 + new FileInfo(Path.Combine(Dir("c"), "x", new Hash128(1, 0, 0, 0).ToString(), "__info")).Length, cache.spaceOccupied);
            Assert.Equal(150 - cache.spaceOccupied, cache.spaceFree);
            cache.maximumAvailableStorageSpace = -1;
            Assert.Equal(0, cache.spaceFree);
            Assert.Equal("Cache is invalid.", Assert.Throws<InvalidOperationException>(() => { var c = default(Cache); c.expirationDelay = 1; }).Message);
        }

        [Fact]
        public void LookupsSearchEveryCacheAndVersionsComeSortedFromAll()
        {
            var a = Caching.AddCache(Dir("a"));
            var b = Caching.AddCache(Dir("b"));
            Keep(a, "alpha", Hash128.Parse("0123456789abcdef0123456789abcdef"));
            Keep(b, "alpha", Hash128.Parse("fedcba9876543210fedcba9876543210"));
            Keep(b, "alpha", Hash128.Parse("0123456789abcdef0123456789abcdef"));
            Assert.True(Caching.IsVersionCached("http://host/alpha.bundle", Hash128.Parse("fedcba9876543210fedcba9876543210")));
            Assert.False(Caching.IsVersionCached("http://host/beta.bundle", Hash128.Parse("fedcba9876543210fedcba9876543210")));
            var versions = new List<Hash128> { default };
            Caching.GetCachedVersions("alpha", versions);
            // Appended, not replacing; sorted by the hash's 64-bit halves.
            Assert.Equal(new[] { default, Hash128.Parse("fedcba9876543210fedcba9876543210"), Hash128.Parse("0123456789abcdef0123456789abcdef") }, versions);
            Assert.True(Caching.MarkAsUsed("http://host/alpha", Hash128.Parse("fedcba9876543210fedcba9876543210")));
            Assert.False(Caching.MarkAsUsed("http://host/alpha", new Hash128(9, 9, 9, 9)));
            Assert.Equal("Input AssetBundle url cannot be null or empty.", Assert.Throws<ArgumentException>(() => Caching.IsVersionCached("", default(Hash128))).Message);
            Assert.Equal("Input AssetBundle name cannot be null or empty.", Assert.Throws<ArgumentException>(() => Caching.ClearAllCachedVersions(null)).Message);
            Assert.Equal("Value cannot be null.\nParameter name: Input outCachedVersions cannot be null.", Assert.Throws<ArgumentNullException>(() => Caching.GetCachedVersions("alpha", null)).Message);

            // A cache added again reads what its directory holds.
            Assert.True(Caching.RemoveCache(b));
            var again = Caching.AddCache(Dir("b"));
            Assert.Equal(2, new List<BundleCaches.Version>(again.Store.VersionsOf("alpha")).Count);
            Assert.True(again.spaceOccupied > 200);
        }

        [Fact]
        public void ClearingStopsAtTheFirstCacheWithAVersionInUse()
        {
            var a = Caching.AddCache(Dir("a"));
            var b = Caching.AddCache(Dir("b"));
            var inUse = new Hash128(1, 0, 0, 0);
            Keep(a, "alpha", inUse);
            Keep(a, "alpha", new Hash128(2, 0, 0, 0));
            Keep(b, "alpha", new Hash128(3, 0, 0, 0));
            BundleCaches.Use(a.Store.Find("alpha", inUse));
            Assert.True(File.Exists(Path.Combine(Dir("a"), "alpha", inUse.ToString(), "__lock")));
            Assert.False(Caching.ClearAllCachedVersions("alpha"));
            Assert.Contains((LogType.Warning, $"AssetBundle 'alpha' with hash '{inUse}' is still in use. "), logs);
            var versions = new List<Hash128>();
            Caching.GetCachedVersions("alpha", versions);
            // The cache with the version in use cleared the rest; the next one was not reached.
            Assert.Equal(new[] { inUse, new Hash128(3, 0, 0, 0) }, versions);
            logs.Clear();
            Assert.False(Caching.ClearCache());
            Assert.Empty(logs);
            Assert.True(Caching.ClearCachedVersion("alpha", new Hash128(7, 7, 7, 7)), "nothing to clear is no failure");
            BundleCaches.Release(a.Store.Find("alpha", inUse));
            Assert.True(Caching.ClearCache());
            Assert.Empty(Directory.GetFileSystemEntries(Dir("a")));
            Assert.Equal(0, a.spaceOccupied);
        }
    }
}
