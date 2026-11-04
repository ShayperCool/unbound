using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The `crc` of AssetBundle.LoadFromFile / LoadFromMemory / LoadFromStream and their
    /// async wrappers, at the managed seam: what each sends to `bundle-open`, and what a native
    /// refusal (the archive check of a nonzero CRC, see the assets crate) gives back. The native side
    /// is a stand-in that accepts 0 or <see cref="Good"/> and refuses any other CRC without
    /// registering anything. Pending async operations are global, hence the frame collection.</summary>
    [Collection("Frames")]
    public unsafe class AssetBundleCrcTests : IDisposable
    {
        const uint Good = 3156288925;
        const uint Bad = Good ^ 1;
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo RunPending = typeof(AsyncOperations).GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly List<string> s_Opens = new List<string>();
        static readonly List<ulong> s_Closes = new List<ulong>();
        static ulong s_NextId;
        readonly object previousApi, previousReady;
        readonly string file;
        readonly byte[] bytes = { 0x55, 0x6e, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 1, 2, 3 };
        readonly List<string> errors = new List<string>();

        public AssetBundleCrcTests()
        {
            previousApi = Api.GetValue(null);
            previousReady = Ready.GetValue(null);
            var api = new NativeApi { Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free };
            Native.Initialize(&api);
            s_Opens.Clear();
            s_Closes.Clear();
            s_NextId = 9000;
            file = Path.Combine(Path.GetTempPath(), $"unbound-crc-{Guid.NewGuid():N}.bundle");
            File.WriteAllBytes(file, bytes);
            Application.logMessageReceivedThreaded += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Capture;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles_Native())
                if (bundle.m_NativeId >= 9000) bundle.Unload(false);
            Api.SetValue(null, previousApi);
            Ready.SetValue(null, previousReady);
            File.Delete(file);
        }

        void Capture(string message, string stack, LogType type)
        {
            if (type == LogType.Error) errors.Add(message);
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
                s_Opens.Add(text);
                using var json = JsonDocument.Parse(text);
                uint crc = json.RootElement.TryGetProperty("crc", out var c) ? c.GetUInt32() : 0;
                if (crc != 0 && crc != Good)
                {
                    status = 1;
                    result = $"CRC mismatch: the AssetBundle's uncompressed content has CRC {Good}, not the expected {crc}";
                }
                else result = JsonSerializer.Serialize(new { id = ++s_NextId, files = Array.Empty<object>(), unityVersion = "6000.0.71f1" });
            }
            else if (operation == "bundle-close")
            {
                using var json = JsonDocument.Parse(text);
                s_Closes.Add(json.RootElement.GetProperty("id").GetUInt64());
            }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(bytes.Length, 1));
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        static JsonElement LastOpen()
        {
            Assert.NotEmpty(s_Opens);
            return JsonDocument.Parse(s_Opens[^1]).RootElement.Clone();
        }

        void AssertMemoryRequest(uint crc)
        {
            var request = LastOpen();
            Assert.Equal(Convert.ToBase64String(bytes), request.GetProperty("data").GetString());
            Assert.Equal(crc, request.GetProperty("crc").GetUInt32());
            Assert.False(request.TryGetProperty("path", out _));
        }

        static void Unload(AssetBundle bundle)
        {
            Assert.NotNull(bundle);
            bundle.Unload(false);
        }

        [Fact]
        public void EveryRouteSendsItsCrc()
        {
            Unload(AssetBundle.LoadFromFile(file));
            var request = LastOpen();
            Assert.Equal(Path.GetFullPath(file), request.GetProperty("path").GetString());
            Assert.Equal(0UL, request.GetProperty("offset").GetUInt64());
            Assert.True(request.TryGetProperty("crc", out var sentCrc), "bundle-open must include its CRC");
            Assert.Equal(0U, sentCrc.GetUInt32());

            Unload(AssetBundle.LoadFromFile(file, Good, 3));
            request = LastOpen();
            Assert.Equal(3UL, request.GetProperty("offset").GetUInt64());
            Assert.Equal(Good, request.GetProperty("crc").GetUInt32());
            Assert.False(request.TryGetProperty("data", out _));

            Unload(AssetBundle.LoadFromMemory(bytes));
            AssertMemoryRequest(0);
            Unload(AssetBundle.LoadFromMemory(bytes, Good));
            AssertMemoryRequest(Good);
            // A stream's bytes go as memory, with its CRC.
            Unload(AssetBundle.LoadFromStream(new MemoryStream(bytes), Good));
            AssertMemoryRequest(Good);
            Unload(AssetBundle.LoadFromStream(new MemoryStream(bytes), Good, 4096));
            AssertMemoryRequest(Good);
            Assert.Empty(errors);
        }

        [Fact]
        public void AMismatchIsReportedAndLoadsNothing()
        {
            var loaded = AssetBundle.GetAllLoadedAssetBundles_Native().Length;
            Assert.Null(AssetBundle.LoadFromFile(file, Bad));
            Assert.Null(AssetBundle.LoadFromFile(file, Bad, 3));
            Assert.Null(AssetBundle.LoadFromMemory(bytes, Bad));
            Assert.Null(AssetBundle.LoadFromStream(new MemoryStream(bytes), Bad));
            Assert.Null(AssetBundle.LoadFromStream(new MemoryStream(bytes), Bad, 4096));
            Assert.Equal(5, s_Opens.Count);
            Assert.Equal(5, errors.Count);
            Assert.All(errors, e => Assert.Contains("CRC mismatch", e));
            Assert.Contains(errors, e => e.Contains(file));
            Assert.Contains(errors, e => e.Contains("<memory>"));
            // Nothing was registered on either side: no bundle to close, none loaded.
            Assert.Empty(s_Closes);
            Assert.Equal(loaded, AssetBundle.GetAllLoadedAssetBundles_Native().Length);
        }

        [Fact]
        public void AsyncWrappersCompleteWithNullOnTheNextFrame()
        {
            var requests = new[]
            {
                AssetBundle.LoadFromFileAsync(file, Bad),
                AssetBundle.LoadFromFileAsync(file, Bad, 3),
                AssetBundle.LoadFromMemoryAsync(bytes, Bad),
                AssetBundle.LoadFromStreamAsync(new MemoryStream(bytes), Bad),
                AssetBundle.LoadFromStreamAsync(new MemoryStream(bytes), Bad, 4096),
            };
            var good = AssetBundle.LoadFromMemoryAsync(bytes, Good);
            int completed = 0;
            foreach (var request in requests)
            {
                Assert.False(request.isDone);
                Assert.Equal(0.9f, request.progress);
                Assert.Null(request.assetBundle);
                request.completed += _ => completed++;
            }
            Assert.False(good.isDone);
            Assert.Equal(5, errors.Count);
            RunPending.Invoke(null, null);
            Assert.Equal(5, completed);
            foreach (var request in requests)
            {
                Assert.True(request.isDone);
                Assert.Null(request.assetBundle);
            }
            Assert.True(good.isDone);
            Unload(good.assetBundle);
            Assert.Equal(5, errors.Count);
        }
    }
}
