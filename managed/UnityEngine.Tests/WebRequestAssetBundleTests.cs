using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Unbound.Runtime;
using UnityEngine.Networking;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>UnityWebRequestAssetBundle, DownloadHandlerAssetBundle and the AssetBundle cache
    /// over file:// URLs, as measured in the Unity 6000.0.71f1 Editor (tests/projects/networkcaching):
    /// the archive opens (`bundle-open`, CRC reported) when the download completes; a CRC mismatch
    /// or data that is no archive completes as a DataProcessingError with the handler's error and
    /// nothing logged; the AssetBundle object is made on first access (or with the request when
    /// autoLoadAssetBundle); cached requests keep the download (`bundle-cache-form`) and complete
    /// later ones without a transfer. The native side is a stand-in: archives are data starting
    /// with the UnityFS signature, their content CRC <see cref="Good"/>; the cache form is the
    /// archive itself, needing its own size. Native and the main-thread id are global, hence the
    /// frame collection.</summary>
    [Collection("Frames")]
    public unsafe class WebRequestAssetBundleTests : IDisposable
    {
        const uint Good = 3156288925;
        const uint Bad = Good ^ 1;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);
        readonly WebRequestTestLoop loop = new WebRequestTestLoop();
        static readonly List<string> s_Calls = new List<string>();
        static ulong s_NextId;
        static readonly byte[] Signature = Encoding.ASCII.GetBytes("UnityFS\0");
        readonly object previousApi, previousReady;
        readonly string previousProject;
        readonly string directory;
        readonly byte[] bundleBytes = { 0x55, 0x6e, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 7, 8, 9 };
        readonly List<(LogType Type, string Message)> logs = new List<(LogType, string)>();

        public WebRequestAssetBundleTests()
        {
            previousApi = Api.GetValue(null);
            previousReady = Ready.GetValue(null);
            var api = new NativeApi { Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free };
            Native.Initialize(&api);
            lock (s_Calls) s_Calls.Clear();
            s_NextId = 9500;
            directory = Path.Combine(Path.GetTempPath(), $"unbound-uwrab-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            previousProject = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = directory;
            BundleCaches.Reset();
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            loop.Dispose();
            Application.logMessageReceivedThreaded -= Capture;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles_Native())
                if (bundle.m_NativeId > 9500) bundle.Unload(false);
            BundleCaches.Reset();
            ProjectInfo.ProjectPath = previousProject;
            Api.SetValue(null, previousApi);
            Ready.SetValue(null, previousReady);
            foreach (var dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
                new DirectoryInfo(dir).Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(directory, true);
        }

        void Capture(string message, string stack, LogType type)
        {
            lock (logs) logs.Add((type, message));
        }

        List<(LogType Type, string Message)> TakeLogs()
        {
            lock (logs)
            {
                var taken = logs.ToList();
                logs.Clear();
                return taken;
            }
        }

        static List<string> Calls(string method)
        {
            lock (s_Calls) return s_Calls.Where(c => c.StartsWith(method + " ", StringComparison.Ordinal)).Select(c => c.Substring(method.Length + 1)).ToList();
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            int status = 0;
            string result = "{}";
            string operation = Encoding.UTF8.GetString(method, methodLength);
            string text = Encoding.UTF8.GetString(request, requestLength);
            lock (s_Calls) s_Calls.Add(operation + " " + text);
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (operation == "bundle-open")
            {
                uint crc = root.TryGetProperty("crc", out var c) ? c.GetUInt32() : 0;
                var data = root.TryGetProperty("data", out var d) ? Convert.FromBase64String(d.GetString())
                    : File.ReadAllBytes(root.GetProperty("path").GetString());
                if (data.Length < Signature.Length || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                {
                    status = 1;
                    result = "not a UnityFS AssetBundle";
                }
                else if (crc != 0 && crc != ContentCrc(data))
                {
                    if (root.TryGetProperty("reportCrc", out var report) && report.GetBoolean())
                        result = JsonSerializer.Serialize(new { crcMismatch = new { expected = crc, actual = ContentCrc(data) } });
                    else
                    {
                        status = 1;
                        result = "CRC mismatch";
                    }
                }
                else result = JsonSerializer.Serialize(new { id = Interlocked.Increment(ref s_NextId), files = Array.Empty<object>(), unityVersion = "6000.0.71f1" });
            }
            else if (operation == "bundle-cache-form")
            {
                var source = root.GetProperty("source").GetString();
                File.Copy(source, root.GetProperty("dest").GetString(), true);
                result = JsonSerializer.Serialize(new { required = new FileInfo(source).Length, rewritten = false });
            }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(bytes.Length, 1));
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        /// <summary>The stand-in's content CRC: <see cref="Good"/> for <see cref="bundleBytes"/>
        /// (last byte 9), another value for other archives.</summary>
        static uint ContentCrc(byte[] data) => data[^1] == 9 ? Good : Good ^ 7;

        string Write(string name, byte[] contents)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, contents);
            return "file://" + path;
        }

        static UnityWebRequest Send(UnityWebRequest request)
        {
            Assert.NotNull(request);
            request.SendWebRequest();
            WebRequestTestLoop.Until(() => request.isDone);
            return request;
        }

        Cache FreshCache(string name)
        {
            var path = Path.Combine(directory, name);
            Directory.CreateDirectory(path);
            var cache = Caching.AddCache(path);
            Caching.currentCacheForWriting = cache;
            return cache;
        }

        [Fact]
        public void TheArchiveOpensWithTheRequestAndTheBundleOnFirstAccess()
        {
            var url = Write("good.bundle", bundleBytes);
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, Good)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                var handler = Assert.IsType<DownloadHandlerAssetBundle>(request.downloadHandler);
                Assert.False(handler.autoLoadAssetBundle);
                Assert.True(handler.isDownloadComplete);
                var open = JsonDocument.Parse(Assert.Single(Calls("bundle-open"))).RootElement;
                Assert.Equal(Convert.ToBase64String(bundleBytes), open.GetProperty("data").GetString());
                Assert.Equal(Good, open.GetProperty("crc").GetUInt32());
                Assert.Equal(0, AssetBundle.GetAllLoadedAssetBundles_Native().Count(b => b.m_NativeId > 9500));
                var bundle = handler.assetBundle;
                Assert.NotNull(bundle);
                Assert.Same(bundle, DownloadHandlerAssetBundle.GetContent(request));
                Assert.Single(Calls("bundle-open"));
                Assert.Equal("Raw data access is not supported for asset bundles", Assert.Throws<NotSupportedException>(() => handler.data).Message);
                Assert.Equal("String access is not supported for asset bundles", Assert.Throws<NotSupportedException>(() => handler.text).Message);
                bundle.Unload(false);
            }
            var eager = new DownloadHandlerAssetBundle(url, 0) { autoLoadAssetBundle = true };
            using (var request = Send(new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, eager, null)))
            {
                Assert.Equal(1, AssetBundle.GetAllLoadedAssetBundles_Native().Count(b => b.m_NativeId > 9500));
                eager.assetBundle.Unload(false);
            }
            Assert.Empty(TakeLogs());
        }

        [Fact]
        public void ACrcMismatchOrDataThatIsNoArchiveFailsTheRequestQuietly()
        {
            var url = Write("good.bundle", bundleBytes);
            var garbage = Write("garbage.bundle", Encoding.ASCII.GetBytes("<html>not a bundle</html>"));
            foreach (var request in new[]
            {
                UnityWebRequestAssetBundle.GetAssetBundle(url, Bad),
                UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(1, 2, 3, 4), Bad),
            })
            {
                using (Send(request))
                {
                    var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
                    Assert.Equal(UnityWebRequest.Result.DataProcessingError, request.result);
                    Assert.Equal("Data Processing Error, see Download Handler error", request.error);
                    Assert.Equal($"CRC Mismatch. Provided {Bad:x}, calculated {Good:x} from data. Will not load AssetBundle '{url}'", handler.error);
                    Assert.True(handler.isDownloadComplete);
                    Assert.Null(handler.assetBundle);
                }
            }
            Assert.False(Caching.IsVersionCached(url, new Hash128(1, 2, 3, 4)));
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(garbage)))
            {
                Assert.Equal(UnityWebRequest.Result.DataProcessingError, request.result);
                Assert.Equal($"Failed to decompress data for the AssetBundle '{garbage}'.", request.downloadHandler.error);
                Assert.Null(DownloadHandlerAssetBundle.GetContent(request));
            }
            Assert.Empty(TakeLogs());
        }

        [Fact]
        public void ANetworkFailureOpensNothing()
        {
            using var request = Send(UnityWebRequestAssetBundle.GetAssetBundle("file://" + Path.Combine(directory, "missing.bundle"), new Hash128(1, 0, 0, 0)));
            Assert.Equal(UnityWebRequest.Result.ConnectionError, request.result);
            Assert.True(((DownloadHandlerAssetBundle)request.downloadHandler).isDownloadComplete);
            Assert.Empty(Calls("bundle-open"));
            Assert.Equal(0, Caching.currentCacheForWriting.spaceOccupied);
        }

        [Fact]
        public void CachedRequestsKeepTheDownloadAndCompleteLaterOnesWithoutATransfer()
        {
            var cache = FreshCache("cache");
            var url = Write("alpha.bundle", bundleBytes);
            var hash = Hash128.Parse("0123456789abcdef0123456789abcdef");
            AssetBundle first;
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url + "?v=1", hash, 0)))
            {
                Assert.Equal((ulong)bundleBytes.Length, request.downloadedBytes);
                first = DownloadHandlerAssetBundle.GetContent(request);
                Assert.NotNull(first);
            }
            var entry = Path.Combine(cache.path, "alpha", hash.ToString());
            Assert.Equal(bundleBytes, File.ReadAllBytes(Path.Combine(entry, "__data")));
            var info = File.ReadAllText(Path.Combine(entry, "__info"));
            Assert.Matches(@"^-1\n\d+\n1\n__data\n$", info);
            Assert.True(File.Exists(Path.Combine(entry, "__lock")), "in use while its bundle is loaded");
            Assert.Equal(bundleBytes.Length + info.Length, cache.spaceOccupied);
            Assert.True(Caching.IsVersionCached("https://elsewhere/dir/alpha.unity3d?x", hash));
            Assert.True(Caching.IsVersionCached(new CachedAssetBundle("alpha", hash)));
            Assert.False(Caching.ClearCachedVersion("alpha", hash));
            Assert.Contains((LogType.Warning, $"AssetBundle 'alpha' with hash '{hash}' is still in use. "), TakeLogs());
            Assert.False(Caching.ClearCache());
            Assert.Empty(TakeLogs());
            first.Unload(false);
            Assert.False(File.Exists(Path.Combine(entry, "__lock")));

            // The source goes: a hit reads the cache, without a transfer.
            File.Delete(new Uri(url).LocalPath);
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url + "?other", hash, Good)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.Equal(0, request.responseCode);
                Assert.Equal(0UL, request.downloadedBytes);
                Assert.Equal(1f, request.downloadProgress);
                var bundle = DownloadHandlerAssetBundle.GetContent(request);
                Assert.NotNull(bundle);
                bundle.Unload(false);
            }
            Assert.EndsWith("__data", JsonDocument.Parse(Calls("bundle-open").Last()).RootElement.GetProperty("path").GetString());
            Assert.True(Caching.ClearCachedVersion("alpha", hash));
            Assert.Equal(0, cache.spaceOccupied);
            Assert.Empty(TakeLogs());
        }

        [Fact]
        public void ACachedVersionFailingItsCrcIsDownloadedAgain()
        {
            var cache = FreshCache("cache");
            var url = Write("beta.bundle", bundleBytes);
            var hash = new Hash128(0, 0, 0, 7);
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 7, 0)))
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
            long occupied = cache.spaceOccupied;
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 7, Bad)))
            {
                // The download fails the CRC too: refused as it is written, with nothing counted.
                Assert.Equal(UnityWebRequest.Result.ConnectionError, request.result);
                Assert.Equal("Unable to write data", request.error);
                Assert.Equal(0UL, request.downloadedBytes);
                Assert.StartsWith($"CRC Mismatch. Provided {Bad:x}", request.downloadHandler.error);
            }
            Assert.False(Caching.IsVersionCached(url, hash));
            // As measured, the removed copy's room stays counted.
            Assert.Equal(occupied, cache.spaceOccupied);
            Assert.True(Caching.ClearCache());
            Assert.Equal(0, cache.spaceOccupied);
        }

        [Fact]
        public void ACachedCopyFailingItsCrcIsReplacedByAGoodDownload()
        {
            var cache = FreshCache("cache");
            var hash = new Hash128(0, 0, 0, 3);
            var other = (byte[])bundleBytes.Clone();
            other[^1] = 10;
            var url = Write("epsilon.bundle", other);
            using (Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 3, 0))) { }
            Assert.True(Caching.IsVersionCached(url, hash));
            // The server now has the bundle the CRC names; the cached copy is another.
            Write("epsilon.bundle", bundleBytes);
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 3, Good)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.Equal("", request.downloadHandler.error);
                Assert.Equal((ulong)bundleBytes.Length, request.downloadedBytes);
                var bundle = DownloadHandlerAssetBundle.GetContent(request);
                Assert.NotNull(bundle);
                bundle.Unload(false);
            }
            Assert.Equal(bundleBytes, File.ReadAllBytes(Path.Combine(cache.path, "epsilon", hash.ToString(), "__data")));
            // A cached copy that is no archive at all is downloaded again as well.
            File.WriteAllBytes(Path.Combine(cache.path, "epsilon", hash.ToString(), "__data"), new byte[] { 1, 2, 3 });
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 3, 0)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.NotEqual(0UL, request.downloadedBytes);
            }
            Assert.Empty(TakeLogs());
        }

        [Fact]
        public void ACacheHitCompletesAfterSendWebRequestReturns()
        {
            FreshCache("cache");
            var url = Write("zeta.bundle", bundleBytes);
            using (Send(UnityWebRequestAssetBundle.GetAssetBundle(url, 9, 0))) { }
            using var request = UnityWebRequestAssetBundle.GetAssetBundle(url, 9, 0);
            var operation = request.SendWebRequest();
            bool doneInside = request.isDone, completed = false;
            operation.completed += _ => completed = true;
            Assert.False(doneInside);
            Assert.False(completed, "an operation already done calls a new completed handler at once");
            WebRequestTestLoop.Until(() => request.isDone);
            Assert.True(completed);
            Assert.Equal(0, request.responseCode);
            Assert.Equal(0UL, request.downloadedBytes);
        }

        [Fact]
        public void ADownloadWithoutRoomLoadsWithoutBeingKept()
        {
            var cache = FreshCache("cache");
            var url = Write("gamma.bundle", bundleBytes);
            cache.maximumAvailableStorageSpace = bundleBytes.Length - 1;
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(5, 0, 0, 0), 0)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                var bundle = DownloadHandlerAssetBundle.GetContent(request);
                Assert.NotNull(bundle);
                bundle.Unload(false);
            }
            Assert.False(Caching.IsVersionCached(url, new Hash128(5, 0, 0, 0)));
            Assert.Equal(0, cache.spaceOccupied);
            Assert.Empty(TakeLogs());
            cache.maximumAvailableStorageSpace = bundleBytes.Length;
            using (Send(UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(5, 0, 0, 0), 0))) { }
            Assert.True(Caching.IsVersionCached(url, new Hash128(5, 0, 0, 0)));
            Assert.Equal(0, cache.spaceFree);
        }

        [Fact]
        public void TheLeastRecentlyUsedVersionsMakeRoom()
        {
            var cache = FreshCache("cache");
            var url = Write("delta.bundle", bundleBytes);
            for (uint i = 1; i <= 3; i++)
                using (Send(UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(i, 0, 0, 0), 0))) { }
            var store = cache.Store;
            long now = BundleCaches.Now();
            store.Find("delta", new Hash128(1, 0, 0, 0)).LastUsed = now - 30;
            store.Find("delta", new Hash128(2, 0, 0, 0)).LastUsed = now - 50;
            store.Find("delta", new Hash128(3, 0, 0, 0)).LastUsed = now - 10;
            cache.maximumAvailableStorageSpace = cache.spaceOccupied;
            using (Send(UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(4, 0, 0, 0), 0))) { }
            var versions = new List<Hash128>();
            Caching.GetCachedVersions("delta", versions);
            Assert.Equal(new[] { new Hash128(1, 0, 0, 0), new Hash128(3, 0, 0, 0), new Hash128(4, 0, 0, 0) }, versions);
            // ClearCache(expiration): what was unused for longer than the expiration goes.
            store.Find("delta", new Hash128(1, 0, 0, 0)).LastUsed = now - 100;
            Assert.True(cache.ClearCache(60));
            versions.Clear();
            Caching.GetCachedVersions("delta", versions);
            Assert.Equal(new[] { new Hash128(3, 0, 0, 0), new Hash128(4, 0, 0, 0) }, versions);
        }
    }
}
