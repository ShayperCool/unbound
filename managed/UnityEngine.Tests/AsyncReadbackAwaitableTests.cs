using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public unsafe class AsyncReadbackAwaitableTests : IDisposable
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo Update = typeof(AsyncReadbacks).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly byte[] BufferBytes = Convert.FromHexString("040302014433221188776655ddccbbaa");
        static readonly byte[] TextureBytes = Convert.FromHexString("0102030405060708");
        readonly object previousApi = Api.GetValue(null), previousReady = Ready.GetValue(null);

        public AsyncReadbackAwaitableTests()
        {
            GfxStream.Flush();
            var api = new NativeApi { Version = NativeApi.CurrentVersion, ReadBuffer = &ReadBuffer,
                ReadTexture = &ReadTexture, FreeBuffer = &Free, SubmitCommands = &Submit };
            Native.Initialize(&api);
        }

        public void Dispose()
        {
            AsyncGPUReadback.WaitAllRequests();
            NextFrame();
            NextFrame();
            GfxStream.Flush();
            Api.SetValue(null, previousApi);
            Ready.SetValue(null, previousReady);
        }

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        static int Copy(ReadOnlySpan<byte> bytes, NativeBuffer* result)
        {
            result->Length = bytes.Length;
            result->Data = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
            bytes.CopyTo(new Span<byte>(result->Data, bytes.Length));
            return 1;
        }

        [UnmanagedCallersOnly]
        static int ReadBuffer(ulong handle, long offset, long size, NativeBuffer* result) =>
            Copy(BufferBytes.AsSpan((int)offset, (int)size), result);

        [UnmanagedCallersOnly]
        static int ReadTexture(ulong handle, int mip, int layer, int x, int y, int width, int height, int asFloat, NativeBuffer* result) =>
            Copy(TextureBytes.AsSpan(x * 4, width * height * 4), result);

        static void NextFrame()
        {
            Time.s_FrameCount++;
            Update.Invoke(null, null);
        }

        static Awaitable<AsyncGPUReadbackRequest> BufferAsync(ComputeBuffer compute, GraphicsBuffer graphics, int overload) => overload switch
        {
            0 => AsyncGPUReadback.RequestAsync(compute),
            1 => AsyncGPUReadback.RequestAsync(compute, 8, 4),
            2 => AsyncGPUReadback.RequestAsync(graphics),
            _ => AsyncGPUReadback.RequestAsync(graphics, 8, 4),
        };

        static Awaitable<AsyncGPUReadbackRequest> TextureAsync(Texture texture, int overload, int mip = 0) => overload switch
        {
            4 => AsyncGPUReadback.RequestAsync(texture, mip),
            5 => AsyncGPUReadback.RequestAsync(texture, mip, TextureFormat.RGBA32),
            6 => AsyncGPUReadback.RequestAsync(texture, mip, GraphicsFormat.R8G8B8A8_UNorm),
            7 => AsyncGPUReadback.RequestAsync(texture, mip, 1, 1, 0, 1, 0, 1),
            8 => AsyncGPUReadback.RequestAsync(texture, mip, 1, 1, 0, 1, 0, 1, TextureFormat.RGBA32),
            _ => AsyncGPUReadback.RequestAsync(texture, mip, 1, 1, 0, 1, 0, 1, GraphicsFormat.R8G8B8A8_UNorm),
        };

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        public void SuccessfulOverloadsCompleteOnReadbackUpdateAndKeepRequestLifetime(int overload)
        {
            using var compute = new ComputeBuffer(4, 4);
            using var graphics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false)
                { m_NativeHandle = 1, m_PixelsDirty = false };
            var events = new List<string>();
            int thread = Environment.CurrentManagedThreadId, frame = Time.frameCount;
            // A preceding callback request keeps the same completion order as the probe.
            AsyncGPUReadback.Request(compute, _ => events.Add("callback"));
            var awaited = overload < 4 ? BufferAsync(compute, graphics, overload) : TextureAsync(texture, overload);
            Assert.False(awaited.GetAwaiter().IsCompleted);
            AsyncGPUReadbackRequest request = default;
            bool resumed = false;
            int continuationThread = -1;
            awaited.GetAwaiter().OnCompleted(() =>
            {
                continuationThread = Environment.CurrentManagedThreadId;
                events.Add("await");
                request = awaited.GetAwaiter().GetResult();
                resumed = true;
            });
            Update.Invoke(null, null);
            Assert.False(resumed);
            NextFrame();
            Assert.True(resumed);
            Assert.True(awaited.GetAwaiter().IsCompleted);
            Assert.Equal(frame + 1, Time.frameCount);
            Assert.Equal(thread, continuationThread);
            Assert.Equal(new[] { "callback", "await" }, events);
            Assert.True(request.done);
            Assert.False(request.hasError);
            byte[] expected = overload < 4 ? (overload % 2 == 0 ? BufferBytes : BufferBytes[4..12])
                : overload < 7 ? TextureBytes : TextureBytes[4..];
            Assert.Equal(expected, request.GetData<byte>().ToArray());
            Assert.Equal(expected, request.GetData<byte>().ToArray());
            AssertConsumed(awaited);
            NextFrame();
            NextFrame();
            Assert.True(request.done);
            Assert.True(request.hasError);
            Assert.Equal("Cannot access the data as it is not available",
                Assert.Throws<InvalidOperationException>(() => request.GetData<byte>()).Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void NullAndReleasedBuffersThrowBeforeReturningAnAwaitable(int overload)
        {
            Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => BufferAsync(null, null, overload)).ParamName);
            using var compute = new ComputeBuffer(4, 4);
            using var graphics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            compute.Release();
            graphics.Release();
            Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => BufferAsync(compute, graphics, overload)).ParamName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidBufferRangeReturnsAnAlreadyCompletedErrorRequest(bool graphicsBuffer)
        {
            using var compute = new ComputeBuffer(4, 4);
            using var graphics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            var awaited = graphicsBuffer ? AsyncGPUReadback.RequestAsync(graphics, 8, 12)
                : AsyncGPUReadback.RequestAsync(compute, 8, 12);
            AssertImmediateError(awaited);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        public void InvalidMipReturnsAnAlreadyCompletedErrorRequest(int overload)
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            AssertImmediateError(TextureAsync(texture, overload, 8));
        }

        static void AssertImmediateError(Awaitable<AsyncGPUReadbackRequest> awaited)
        {
            Assert.True(awaited.GetAwaiter().IsCompleted);
            var request = awaited.GetAwaiter().GetResult();
            Assert.True(request.done);
            Assert.True(request.hasError);
            Assert.Throws<InvalidOperationException>(() => request.GetData<byte>());
            AssertConsumed(awaited);
        }

        static void AssertConsumed(Awaitable<AsyncGPUReadbackRequest> awaited) =>
            Assert.Equal("Object reference not set to an instance of an object",
                Assert.Throws<NullReferenceException>(() => awaited.GetAwaiter().GetResult()).Message);
    }
}
