using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unity.Collections;
using UnityEngine.Audio;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public unsafe class AudioRendererSafetyTests : IDisposable
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object previousApi, previousReady, previousMainThread;
        static int calls, sinkRequests, responseMode;
        static Action duringRender;

        public AudioRendererSafetyTests()
        {
            AudioSystem.EnsureRegistered();
            previousApi = Api.GetValue(null);
            previousReady = Ready.GetValue(null);
            previousMainThread = MainThread.GetValue(null);
            MainThread.SetValue(null, Environment.CurrentManagedThreadId);
            var api = new NativeApi { Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free };
            Native.Initialize(&api);
            calls = sinkRequests = responseMode = 0;
            duringRender = null;
            Assert.True(AudioRenderer.Start());
        }

        public void Dispose()
        {
            AudioSystem.t_InCallback = false;
            AudioRenderer.Stop();
            Api.SetValue(null, previousApi);
            Ready.SetValue(null, previousReady);
            MainThread.SetValue(null, previousMainThread);
            duringRender = null;
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            calls++;
            int status = 0;
            string result = "true";
            try
            {
                if (Encoding.UTF8.GetString(method, methodLength) == "audio-renderer-render")
                {
                    using var input = JsonDocument.Parse(Encoding.UTF8.GetString(request, requestLength));
                    var root = input.RootElement;
                    int length = root.GetProperty("length").GetInt32();
                    var sinks = new List<object>();
                    sinkRequests = root.GetProperty("sinks").GetArrayLength();
                    foreach (var sink in root.GetProperty("sinks").EnumerateArray())
                    {
                        if (sink.GetProperty("length").GetInt32() != length)
                            throw new InvalidOperationException("sink length differs from output length");
                        sinks.Add(new { handle = sink.GetProperty("handle").GetUInt64(), samples = new[] { 0.75f } });
                    }
                    duringRender?.Invoke();
                    result = JsonSerializer.Serialize(new { ok = responseMode != 1,
                        samples = responseMode == 2 ? new float[length + 1] : new[] { 0.25f }, sinks });
                }
            }
            catch (Exception e) { status = 1; result = e.Message; }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        [Fact]
        public void NativeLengthFailureConsumesBorrowedSinkBeforeItIsDisposed()
        {
            var group = new AudioMixerGroup { m_Handle = 9 };
            var sink = new NativeArray<float>(2048, Allocator.Persistent);
            using var output = new NativeArray<float>(1024, Allocator.Persistent);
            Assert.True(AudioRenderer.AddMixerGroupSink(group, sink, false));
            Assert.Throws<InvalidOperationException>(() => AudioRenderer.Render(output));
            sink.Dispose();
            using var next = new NativeArray<float>(2048, Allocator.Persistent);
            Assert.True(AudioRenderer.Render(next));
            Assert.Equal(0, sinkRequests);
            Assert.Equal(0.25f, next[0]);
        }

        [Fact]
        public void DisposedSinkIsRejectedBeforeNativeRenderingAndConsumed()
        {
            var sink = new NativeArray<float>(4, Allocator.Persistent);
            using var output = new NativeArray<float>(4, Allocator.Persistent);
            Assert.True(AudioRenderer.AddMixerGroupSink(new AudioMixerGroup { m_Handle = 9 }, sink, false));
            sink.Dispose();
            int before = calls;
            Assert.Throws<ObjectDisposedException>(() => AudioRenderer.Render(output));
            Assert.Equal(before, calls);
            Assert.True(AudioRenderer.Render(output));
            Assert.Equal(0, sinkRequests);
        }

        [Fact]
        public void InvalidOutputAndFalseRenderBothConsumeRegistrations()
        {
            using var sink = new NativeArray<float>(4, Allocator.Persistent);
            var output = new NativeArray<float>(4, Allocator.Persistent);
            var group = new AudioMixerGroup { m_Handle = 9 };
            AudioRenderer.AddMixerGroupSink(group, sink, false);
            output.Dispose();
            Assert.Throws<ObjectDisposedException>(() => AudioRenderer.Render(output));
            using var next = new NativeArray<float>(4, Allocator.Persistent);
            Assert.True(AudioRenderer.Render(next));
            Assert.Equal(0, sinkRequests);
            AudioRenderer.AddMixerGroupSink(group, sink, false);
            responseMode = 1;
            Assert.False(AudioRenderer.Render(next));
            responseMode = 0;
            Assert.True(AudioRenderer.Render(next));
            Assert.Equal(0, sinkRequests);
        }

        [Fact]
        public void DisposalInCallbackIsCheckedBeforeCopyingAnyOutput()
        {
            var sink = new NativeArray<float>(4, Allocator.Persistent);
            using var output = new NativeArray<float>(new[] { 17f, 0, 0, 0 }, Allocator.Persistent);
            AudioRenderer.AddMixerGroupSink(new AudioMixerGroup { m_Handle = 9 }, sink, false);
            duringRender = () => sink.Dispose();
            Assert.Throws<ObjectDisposedException>(() => AudioRenderer.Render(output));
            Assert.Equal(17, output[0]);
            duringRender = null;
            Assert.True(AudioRenderer.Render(output));
            Assert.Equal(0, sinkRequests);
        }

        [Fact]
        public void OversizedNativeOutputCannotWritePastTheBuffer()
        {
            using var output = new NativeArray<float>(new[] { 17f, 0, 0, 0 }, Allocator.Persistent);
            responseMode = 2;
            Assert.Throws<InvalidOperationException>(() => AudioRenderer.Render(output));
            Assert.Equal(17, output[0]);
        }

        [Fact]
        public void CallbackEntryPointsRejectBeforeRpcOrChangingRegistrations()
        {
            using var sink = new NativeArray<float>(4, Allocator.Persistent);
            using var output = new NativeArray<float>(4, Allocator.Persistent);
            var group = new AudioMixerGroup { m_Handle = 9 };
            AudioRenderer.AddMixerGroupSink(group, sink, false);
            int before = calls;
            AudioSystem.t_InCallback = true;
            try
            {
                Assert.Throws<UnityException>(() => AudioRenderer.Start());
                Assert.Throws<UnityException>(() => AudioRenderer.Stop());
                Assert.Throws<UnityException>(() => AudioRenderer.GetSampleCountForCaptureFrame());
                Assert.Throws<UnityException>(() => AudioRenderer.Render(output));
                Assert.Throws<UnityException>(() => AudioRenderer.AddMixerGroupSink(group, sink, false));
                Assert.Throws<UnityException>(() => AudioRenderer.Internal_AudioRenderer_Render(null, 0));
                Assert.Throws<UnityException>(() => AudioRenderer.Internal_AudioRenderer_AddMixerGroupSink(group, null, 0, false));
                Assert.Equal(before, calls);
            }
            finally { AudioSystem.t_InCallback = false; }
            Assert.True(AudioRenderer.Render(output));
            Assert.Equal(1, sinkRequests);
            Assert.Equal(0.75f, sink[0]);
        }
    }
}
