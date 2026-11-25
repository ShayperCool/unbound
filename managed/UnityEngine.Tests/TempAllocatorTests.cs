using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Allocator.Temp memory comes from a stack, as Unity's temp allocator's does: each allocation
    /// after the previous one in the scope's block, the block free again when the scope ends, the
    /// heap for what the block cannot hold.
    /// </summary>
    public unsafe class TempAllocatorTests
    {
        [Fact]
        public void AScopesTempMemoryIsAStack()
        {
            var scope = new Safety.TempScope(blockSize: 1024);
            byte* a = (byte*)scope.Stack(10, 8);
            byte* b = (byte*)scope.Stack(4, 16);
            Assert.True(a != null && b != null);
            // The next allocation follows the previous one, at its alignment.
            Assert.Equal((long)(((ulong)(a + 10) + 15) & ~15UL), (long)b);
            // Bytes past the newest allocation are the block's free part.
            Assert.True(scope.InBlock(b, 4 + 64));
            // What the block cannot hold is not taken from it.
            Assert.True(scope.Stack(2000, 8) == null);
            Assert.True(scope.Stack((nuint)(1024 - (b + 4 - a)), 1) != null);
            Assert.True(scope.Stack(1, 1) == null);
            // The end of the scope frees the whole block.
            scope.End();
            Assert.True(scope.Stack(10, 8) == a);
        }

        [Fact]
        public void AThreadsOwnScopeKeepsNoStack()
        {
            var scope = new Safety.TempScope(ends: false, blockSize: 1024);
            Assert.True(scope.Stack(10, 8) == null);
        }

        /// <summary>A Temp buffer whose clear reaches sizeof(WriterHandle) bytes past it, as the
        /// development build of com.unity.multiplayer.tools' FastBufferWriter.Grow clears it: the
        /// cleared bytes past it stay in the job's stack block (in the heap they overwrote the
        /// next chunk's header and the process aborted at the next allocation).</summary>
        struct GrowLikeWriter : IJob
        {
            public NativeArray<long> Result;

            public void Execute()
            {
                const int size = 64, past = 40;
                byte* first = (byte*)UnsafeUtility.Malloc(size, 1, Allocator.Temp);
                UnsafeUtility.MemSet(first, 0, size + past);
                byte* second = (byte*)UnsafeUtility.Malloc(8, 8, Allocator.Temp);
                UnsafeUtility.Free(first, Allocator.Temp);
                Result[0] = (long)first;
                Result[1] = (long)second;
                Result[2] = Unbound.Runtime.Jobs.JobSystem.CurrentTempScope.InBlock(first, size + past) ? 1 : 0;
            }
        }

        [Fact]
        public void AJobsTempMemoryComesFromItsStackBlock()
        {
            var result = new NativeArray<long>(3, Allocator.Persistent);
            try
            {
                new GrowLikeWriter { Result = result }.Schedule().Complete();
                Assert.Equal(1, result[2]);
                // The second allocation follows the first in the block.
                Assert.Equal(((result[0] + 64) + 7) & ~7L, result[1]);
            }
            finally
            {
                result.Dispose();
            }
        }
    }
}
