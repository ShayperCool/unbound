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
    /// <summary>UnityWebRequestAssetBundle and DownloadHandlerAssetBundle over file:// URLs: the
    /// download goes through UnityWebRequest, the load through AssetBundle.LoadFromMemory and its
    /// `bundle-open`. The native side is a stand-in that opens data starting with the UnityFS
    /// signature when the CRC is 0 or <see cref="Good"/>, and refuses anything else, as the archive
    /// reader does. Native and the main-thread id are global, hence the frame collection.
    ///
    /// Documented (6000.0 Scripting API): a mismatching nonzero CRC logs an error and loads nothing;
    /// autoLoadAssetBundle loads with the request, or on the first assetBundle access when false,
    /// and is false by default ("Default: false."); an error decoding the bundle is logged and
    /// assetBundle returns null; GetContent is assetBundle; data and text throw
    /// NotSupportedException. Implementation checks of Unbound's unmeasured choices: by default
    /// invalid data leaves the request Success and the first access logs once; with
    /// autoLoadAssetBundle it completes as DataProcessingError; the cached overloads' warning.</summary>
    [Collection("Frames")]
    public unsafe class WebRequestAssetBundleTests : IDisposable
    {
        const uint Good = 3156288925;
        const uint Bad = Good ^ 1;
        const string CacheWarning = "DownloadHandlerAssetBundle caching";
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly List<string> s_Opens = new List<string>();
        static ulong s_NextId;
        static readonly byte[] Signature = Encoding.ASCII.GetBytes("UnityFS\0");
        readonly object previousApi, previousReady, previousMainThread;
        readonly string directory;
        readonly byte[] bundleBytes = { 0x55, 0x6e, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 7, 8, 9 };
        readonly List<(LogType Type, string Message)> logs = new List<(LogType, string)>();

        public WebRequestAssetBundleTests()
        {
            previousApi = Api.GetValue(null);
            previousReady = Ready.GetValue(null);
            previousMainThread = MainThread.GetValue(null);
            // Main-thread deliveries run inline on the request's worker, as in the other request tests.
            MainThread.SetValue(null, -1);
            var api = new NativeApi { Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free };
            Native.Initialize(&api);
            lock (s_Opens) s_Opens.Clear();
            s_NextId = 9500;
            directory = Path.Combine(Path.GetTempPath(), $"unbound-uwrab-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles_Native())
                if (bundle.m_NativeId > 9500) bundle.Unload(false);
            Api.SetValue(null, previousApi);
            Ready.SetValue(null, previousReady);
            MainThread.SetValue(null, previousMainThread);
            Directory.Delete(directory, true);
        }

        void Capture(string message, string stack, LogType type)
        {
            lock (logs) logs.Add((type, message));
        }

        List<string> Logged(LogType type)
        {
            lock (logs) return logs.Where(l => l.Type == type).Select(l => l.Message).ToList();
        }

        /// <summary>The errors, exceptions and assertions logged since the last call.</summary>
        List<(LogType Type, string Message)> TakeProblems()
        {
            lock (logs)
            {
                var problems = logs.Where(l => l.Type is LogType.Error or LogType.Exception or LogType.Assert).ToList();
                logs.Clear();
                return problems;
            }
        }

        static int Opens
        {
            get { lock (s_Opens) return s_Opens.Count; }
        }

        static JsonElement LastOpen()
        {
            lock (s_Opens)
            {
                Assert.NotEmpty(s_Opens);
                return JsonDocument.Parse(s_Opens[^1]).RootElement.Clone();
            }
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            int status = 0;
            string result = "{}";
            string operation = Encoding.UTF8.GetString(method, methodLength);
            string text = Encoding.UTF8.GetString(request, requestLength);
            if (operation == "bundle-open")
            {
                lock (s_Opens) s_Opens.Add(text);
                using var json = JsonDocument.Parse(text);
                uint crc = json.RootElement.TryGetProperty("crc", out var c) ? c.GetUInt32() : 0;
                var data = json.RootElement.TryGetProperty("data", out var d) ? Convert.FromBase64String(d.GetString()) : Array.Empty<byte>();
                if (data.Length < Signature.Length || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                {
                    status = 1;
                    result = "not an AssetBundle archive";
                }
                else if (crc != 0 && crc != Good)
                {
                    status = 1;
                    result = $"CRC mismatch: the AssetBundle's uncompressed content has CRC {Good}, not the expected {crc}";
                }
                else result = JsonSerializer.Serialize(new { id = Interlocked.Increment(ref s_NextId), files = Array.Empty<object>(), unityVersion = "6000.0.71f1" });
            }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(bytes.Length, 1));
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        string Write(string name, byte[] contents)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, contents);
            return "file://" + path;
        }

        static void Wait(UnityWebRequest request)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!request.isDone && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.True(request.isDone, "the request did not complete");
        }

        static UnityWebRequest Send(UnityWebRequest request)
        {
            Assert.NotNull(request);
            request.SendWebRequest();
            Wait(request);
            return request;
        }

        void AssertOpened(uint crc)
        {
            var open = LastOpen();
            Assert.Equal(Convert.ToBase64String(bundleBytes), open.GetProperty("data").GetString());
            Assert.Equal(crc, open.GetProperty("crc").GetUInt32());
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        [Fact]
        public void GetAssetBundleDownloadsAndLoadsTheBundle()
        {
            var url = Write("good.bundle", bundleBytes);
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.Equal(UnityWebRequest.kHttpVerbGET, request.method);
                var handler = Assert.IsType<DownloadHandlerAssetBundle>(request.downloadHandler);
                // Documented default: autoLoadAssetBundle is false, so the request only downloads
                // and the bundle is loaded on the first access of assetBundle.
                Assert.False(handler.autoLoadAssetBundle);
                Assert.True(handler.isDownloadComplete);
                Assert.True(handler.isDone);
                Assert.Equal(0, Opens);
                var bundle = handler.assetBundle;
                Assert.NotNull(bundle);
                Assert.Equal(1, Opens);
                AssertOpened(0);
                Assert.Same(bundle, DownloadHandlerAssetBundle.GetContent(request));
                Assert.Equal(1, Opens);
                bundle.Unload(false);
            }
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(new Uri(url), Good)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.Equal(1, Opens);
                // GetContent is assetBundle: the first access loads.
                var bundle = DownloadHandlerAssetBundle.GetContent(request);
                Assert.NotNull(bundle);
                Assert.Equal(2, Opens);
                AssertOpened(Good);
                bundle.Unload(false);
            }
            // autoLoadAssetBundle = true: loaded as part of the request, before it reported done.
            var eager = new DownloadHandlerAssetBundle(url, Good) { autoLoadAssetBundle = true };
            Assert.True(eager.autoLoadAssetBundle);
            using (var request = Send(new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, eager, null)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                Assert.True(eager.isDownloadComplete);
                Assert.Equal(3, Opens);
                AssertOpened(Good);
                var bundle = eager.assetBundle;
                Assert.NotNull(bundle);
                Assert.Same(bundle, DownloadHandlerAssetBundle.GetContent(request));
                Assert.Equal(3, Opens);
                bundle.Unload(false);
            }
            Assert.Empty(Logged(LogType.Error));
            Assert.Empty(Logged(LogType.Exception));
        }

        [Fact]
        public void InvalidDataOrCrcFailsLoudlyAndLoadsNothing()
        {
            var loaded = AssetBundle.GetAllLoadedAssetBundles_Native().Length;
            var garbage = Write("garbage.bundle", Encoding.ASCII.GetBytes("<html>not a bundle</html>"));
            var url = Write("good.bundle", bundleBytes);

            // Default (load on access). The request itself succeeds: Unbound's choice (unmeasured),
            // the docs do not say. The first access logs the loader's error once and returns null
            // (documented); later accesses return null without loading or logging again.
            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(garbage)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
                Assert.True(handler.isDownloadComplete);
                Assert.Equal(0, Opens);
                Assert.Empty(TakeProblems());
                Assert.Null(handler.assetBundle);
                Assert.Equal(1, Opens);
                Assert.Contains(garbage, handler.error);
                Assert.Null(handler.assetBundle);
                Assert.Null(DownloadHandlerAssetBundle.GetContent(request));
                Assert.Equal(1, Opens);
            }
            var logged = Assert.Single(TakeProblems());
            Assert.Equal(LogType.Error, logged.Type);
            Assert.Contains("not an AssetBundle archive", logged.Message);

            using (var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(url, Bad)))
            {
                Assert.Equal(UnityWebRequest.Result.Success, request.result);
                var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
                Assert.Equal(1, Opens);
                Assert.Null(handler.assetBundle);
                Assert.Equal(2, Opens);
                AssertOpened(Bad);
                Assert.Null(DownloadHandlerAssetBundle.GetContent(request));
                Assert.Equal(2, Opens);
            }
            logged = Assert.Single(TakeProblems());
            Assert.Equal(LogType.Error, logged.Type);
            Assert.Contains("CRC mismatch", logged.Message);

            // autoLoadAssetBundle = true: the load fails as part of the request, which completes as
            // DataProcessingError (Unbound's choice, unmeasured).
            int opens = 2;
            foreach (var (source, crc, reason) in new[] { (garbage, 0U, "not an AssetBundle archive"), (url, Bad, "CRC mismatch") })
            {
                var handler = new DownloadHandlerAssetBundle(source, crc) { autoLoadAssetBundle = true };
                using (var request = Send(new UnityWebRequest(source, UnityWebRequest.kHttpVerbGET, handler, null)))
                {
                    Assert.Equal(UnityWebRequest.Result.DataProcessingError, request.result);
                    Assert.Equal("Data Processing Error, see Download Handler error", request.error);
                    Assert.True(handler.isDownloadComplete);
                    Assert.Equal(++opens, Opens);
                    Assert.Contains(source, handler.error);
                    Assert.Null(handler.assetBundle);
                    Assert.Null(DownloadHandlerAssetBundle.GetContent(request));
                    Assert.Equal(opens, Opens);
                }
                var problems = TakeProblems();
                Assert.Equal(2, problems.Count);
                Assert.Contains(problems, l => l.Type == LogType.Error && l.Message.Contains(reason));
                // Known difference, unresolved, pending a Root decision: the request also logs the
                // handler's exception naming the URL, where Unity documents one logged error.
                Assert.Contains(problems, l => l.Type == LogType.Exception && l.Message.Contains(source));
            }
            Assert.Equal(loaded, AssetBundle.GetAllLoadedAssetBundles_Native().Length);
        }

        [Fact]
        public void ANetworkFailureIsAConnectionErrorWithoutALoad()
        {
            var missing = "file://" + Path.Combine(directory, "missing.bundle");
            using var request = Send(UnityWebRequestAssetBundle.GetAssetBundle(missing));
            Assert.Equal(UnityWebRequest.Result.ConnectionError, request.result);
            var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
            Assert.False(handler.isDownloadComplete);
            Assert.Null(handler.assetBundle);
            Assert.Null(DownloadHandlerAssetBundle.GetContent(request));
            Assert.Equal(0, Opens);
        }

        [Fact]
        public void CachedOverloadsWarnAndStillDownloadAndLoad()
        {
            var url = Write("cached.bundle", bundleBytes);
            int before = Hits(CacheWarning);
            var requests = new[]
            {
                UnityWebRequestAssetBundle.GetAssetBundle(url, 3U, 0U),
                UnityWebRequestAssetBundle.GetAssetBundle(new Uri(url), 3U, Good),
                UnityWebRequestAssetBundle.GetAssetBundle(url, new Hash128(1, 2, 3, 4)),
                UnityWebRequestAssetBundle.GetAssetBundle(new Uri(url), new Hash128(1, 2, 3, 4), Good),
                UnityWebRequestAssetBundle.GetAssetBundle(url, default(CachedAssetBundle)),
                UnityWebRequestAssetBundle.GetAssetBundle(new Uri(url), default(CachedAssetBundle), Good),
                new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAssetBundle(url, "named", new Hash128(5, 6, 7, 8), 0U), null),
            };
            // Each cached request says the cache is not there (the warning itself is logged once).
            Assert.Equal(before + requests.Length, Hits(CacheWarning));
            Assert.Contains(Stubs.HitReport(), h => h.Key == CacheWarning);
            int opens = 0;
            foreach (var request in requests)
            {
                using (request)
                {
                    Send(request);
                    Assert.Equal(UnityWebRequest.Result.Success, request.result);
                    // Loaded on access (the documented default), never from a cache: every request
                    // downloads and loads again.
                    Assert.Equal(opens, Opens);
                    var bundle = DownloadHandlerAssetBundle.GetContent(request);
                    Assert.NotNull(bundle);
                    Assert.Equal(++opens, Opens);
                    bundle.Unload(false);
                }
            }
            Assert.Empty(Logged(LogType.Error));
        }

        [Fact]
        public void GetContentChecksTheRequestAndRawAccessIsUnsupported()
        {
            Assert.Throws<NullReferenceException>(() => DownloadHandlerAssetBundle.GetContent(null));
            var url = Write("good.bundle", bundleBytes);
            using var request = UnityWebRequestAssetBundle.GetAssetBundle(url);
            Assert.Throws<InvalidOperationException>(() => DownloadHandlerAssetBundle.GetContent(request));
            Send(request);
            var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
            Assert.Throws<NotSupportedException>(() => handler.data);
            Assert.Throws<NotSupportedException>(() => handler.text);
            var bundle = DownloadHandlerAssetBundle.GetContent(request);
            Assert.NotNull(bundle);
            Assert.Same(handler.assetBundle, bundle);
            bundle.Unload(false);
        }

        [Fact]
        public void WithoutAutoLoadTheBundleLoadsOnFirstAccess()
        {
            var url = Write("lazy.bundle", bundleBytes);
            var handler = new DownloadHandlerAssetBundle(url, Good) { autoLoadAssetBundle = false };
            Assert.False(handler.autoLoadAssetBundle);
            using var request = Send(new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, handler, null));
            Assert.Equal(UnityWebRequest.Result.Success, request.result);
            Assert.True(handler.isDownloadComplete);
            Assert.Equal(0, Opens);
            var bundle = handler.assetBundle;
            Assert.NotNull(bundle);
            Assert.Equal(1, Opens);
            AssertOpened(Good);
            Assert.Same(bundle, handler.assetBundle);
            Assert.Same(bundle, DownloadHandlerAssetBundle.GetContent(request));
            Assert.Equal(1, Opens);
            bundle.Unload(false);
        }
    }
}
