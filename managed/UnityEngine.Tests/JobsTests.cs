using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The job system and its safety checks (tests/projects/jobs compares them with the Editor).</summary>
    public class JobsTests
    {
        struct WriteJob : IJob
        {
            public NativeArray<int> Data;

            public void Execute()
            {
                for (int i = 0; i < Data.Length; i++) Data[i] = i;
            }
        }

        struct SquareParallel : IJobParallelFor
        {
            public NativeArray<int> Data;

            public void Execute(int index) => Data[index] = index * index;
        }

        struct AddJob : IJob
        {
            public NativeArray<int> Data;
            public int Amount;

            public void Execute()
            {
                for (int i = 0; i < Data.Length; i++) Data[i] += Amount;
            }
        }

        [Fact]
        public void ScheduledJobsRunWhenFlushedAndLockTheirContainersUntilCompleted()
        {
            var data = new NativeArray<int>(16, Allocator.TempJob);
            var handle = new WriteJob { Data = data }.Schedule();
            var read = Assert.Throws<InvalidOperationException>(() => data[0]);
            Assert.Contains("You must call JobHandle.Complete()", read.Message);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!handle.IsCompleted && DateTime.UtcNow < deadline) Thread.Sleep(1);
            Assert.True(handle.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => data[0]);
            handle.Complete();
            Assert.Equal(15, data[15]);
            data.Dispose();
        }

        [Fact]
        public void ParallelForCoversEveryIndexAndChainsRunInOrder()
        {
            var data = new NativeArray<int>(1000, Allocator.TempJob);
            new SquareParallel { Data = data }.Schedule(data.Length, 7).Complete();
            Assert.Equal(999 * 999, data[999]);
            var a = new AddJob { Data = data, Amount = 1 }.Schedule();
            var b = new AddJob { Data = data, Amount = 2 }.Schedule(a);
            b.Complete();
            Assert.Equal(3, data[0]);
            Assert.True(a.IsCompleted);
            data.Dispose();
        }

        [Fact]
        public void ConflictingSchedulesAndDisposedContainersThrow()
        {
            var data = new NativeArray<int>(4, Allocator.TempJob);
            var first = new WriteJob { Data = data }.Schedule();
            var conflict = Assert.Throws<InvalidOperationException>(() => new WriteJob { Data = data }.Schedule());
            Assert.Contains("you must include", conflict.Message);
            new WriteJob { Data = data }.Schedule(first).Complete();
            data.Dispose();
            var disposed = Assert.Throws<InvalidOperationException>(() => new WriteJob { Data = data }.Schedule());
            Assert.Contains("has been deallocated. All containers must be valid when scheduling a job.", disposed.Message);
            Assert.Throws<ObjectDisposedException>(() => data[0]);
        }
    }
}
