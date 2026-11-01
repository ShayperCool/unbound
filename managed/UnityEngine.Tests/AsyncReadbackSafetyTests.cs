using System;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class AsyncReadbackSafetyTests
    {
        struct Read : IJob
        {
            [ReadOnly] public NativeArray<byte> Data;
            public void Execute() { _ = Data[0]; }
        }

        struct Write : IJob
        {
            public NativeArray<byte> Data;
            public void Execute() { Data[0] = 1; }
        }

        static AsyncReadbacks.Result Data(Action<AsyncGPUReadbackRequest> callback = null) =>
            new AsyncReadbacks.Result { Layers = new[] { new byte[] { 4, 3, 2, 1 } }, Width = 4, Height = 1, Depth = 1, Callback = callback };

        [Theory]
        [InlineData(Allocator.Persistent)]
        [InlineData(Allocator.TempJob)]
        public void PendingDestinationCannotBeAccessedScheduledOrFreed(Allocator allocator)
        {
            var output = new NativeArray<byte>(4, allocator);
            bool callback = false;
            var request = AsyncReadbacks.Into(ref output, Data(_ =>
            {
                Assert.Equal((byte)4, output[0]);
                callback = true;
            }));
            try
            {
                Assert.Throws<InvalidOperationException>(() => output[0]);
                Assert.Throws<InvalidOperationException>(() => output[0] = 5);
                Assert.Throws<InvalidOperationException>(() => output.Dispose());
                Assert.Throws<InvalidOperationException>(() => output.Dispose(default));
                Assert.Throws<InvalidOperationException>(() => new Read { Data = output }.Schedule());
                Assert.Throws<InvalidOperationException>(() => new Write { Data = output }.Schedule());
                request.WaitForCompletion();
                Assert.True(request.done);
                Assert.False(request.hasError);
                Assert.True(callback);
                Assert.Equal(new byte[] { 4, 3, 2, 1 }, output.ToArray());
                var job = new Write { Data = output }.Schedule();
                job.Complete();
                Assert.Equal((byte)1, output[0]);
            }
            finally { request.WaitForCompletion(); output.Dispose(); }
        }

        [Fact]
        public void ExistingWriterMustCompleteBeforeRequestingReadback()
        {
            var output = new NativeArray<byte>(4, Allocator.Persistent);
            var job = new Write { Data = output }.Schedule();
            try
            {
                Assert.Throws<InvalidOperationException>(() => AsyncReadbacks.Into(ref output, Data()));
                job.Complete();
                AsyncReadbacks.Into(ref output, Data()).WaitForCompletion();
                Assert.Equal((byte)4, output[0]);
            }
            finally { job.Complete(); output.Dispose(); }
        }

        [Fact]
        public void FrameLocalAndBorrowedDestinationsFailBeforeLockingStorage()
        {
            var temp = new NativeArray<byte>(4, Allocator.Temp);
            var persistent = new NativeArray<byte>(8, Allocator.Persistent);
            var borrowed = persistent.GetSubArray(0, 4);
            try
            {
                Assert.Throws<ArgumentException>(() => AsyncReadbacks.Into(ref temp, Data()));
                Assert.Throws<ArgumentException>(() => AsyncReadbacks.Into(ref borrowed, Data()));
                temp[0] = 7;
                persistent[0] = 8;
                Assert.Equal((byte)7, temp[0]);
                Assert.Equal((byte)8, borrowed[0]);
            }
            finally { temp.Dispose(); persistent.Dispose(); }
        }
    }
}
