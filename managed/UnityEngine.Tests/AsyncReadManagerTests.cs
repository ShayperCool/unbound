using System;
using System.IO;
using System.Threading;
using Unbound.Runtime;
using Unbound.Runtime.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>AsyncReadManager's requests and FrameTimingManager's frames
    /// (tests/projects/asyncread compares them with the Editor).</summary>
    public unsafe class AsyncReadManagerTests : IDisposable
    {
        const int Length = 4096;
        const byte Sentinel = 0xEE;
        readonly string m_Directory;
        readonly string m_Path;

        public AsyncReadManagerTests()
        {
            m_Directory = Path.Combine(Path.GetTempPath(), "unbound-asyncread-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            m_Path = Path.Combine(m_Directory, "data.bin");
            var data = new byte[Length];
            for (int i = 0; i < Length; i++) data[i] = Expected(i);
            File.WriteAllBytes(m_Path, data);
        }

        public void Dispose() => Directory.Delete(m_Directory, true);

        static byte Expected(long i) => (byte)((i * 7 + 3) & 255);

        sealed class Buffer : IDisposable
        {
            internal readonly byte* Bytes;
            internal readonly int Length;

            internal Buffer(int length)
            {
                Length = length;
                Bytes = (byte*)UnsafeUtility.Malloc(Math.Max(length, 1), 16, Allocator.Persistent);
                UnsafeUtility.MemSet(Bytes, Sentinel, Math.Max(length, 1));
            }

            public void Dispose() => UnsafeUtility.Free(Bytes, Allocator.Persistent);
        }

        /// <summary>Reads (offset, size) commands by name into one buffer and returns the
        /// completed handle.</summary>
        ReadHandle ReadByName(string path, Buffer buffer, params (long Offset, long Size)[] reads)
        {
            var commands = stackalloc ReadCommand[Math.Max(reads.Length, 1)];
            long at = 0;
            for (int i = 0; i < reads.Length; i++)
            {
                commands[i] = new ReadCommand { Offset = reads[i].Offset, Size = reads[i].Size, Buffer = buffer.Bytes + at };
                at += reads[i].Size;
            }
            var handle = AsyncReadManager.Read(path, commands, (uint)reads.Length);
            handle.JobHandle.Complete();
            return handle;
        }

        [Fact]
        public void ReadsEachCommandAndReportsItsBytes()
        {
            using var buffer = new Buffer(24);
            var handle = ReadByName(m_Path, buffer, (0, 16), (100, 8));
            Assert.Equal(ReadStatus.Complete, handle.Status);
            Assert.Equal(2, handle.ReadCount);
            Assert.Equal(24, handle.GetBytesRead());
            Assert.Equal(16, handle.GetBytesRead(0));
            Assert.Equal(8, handle.GetBytesRead(1));
            Assert.Equal(8UL, handle.GetBytesReadArray()[1]);
            for (int i = 0; i < 16; i++) Assert.Equal(Expected(i), buffer.Bytes[i]);
            for (int i = 0; i < 8; i++) Assert.Equal(Expected(100 + i), buffer.Bytes[16 + i]);
            handle.Dispose();
            Assert.False(handle.IsValid());
            Assert.Equal("Cannot use a ReadHandle that has been disposed",
                Assert.Throws<InvalidOperationException>(() => handle.Status).Message);
            Assert.Equal("ReadHandle.Dispose cannot be called twice on the same ReadHandle",
                Assert.Throws<InvalidOperationException>(() => handle.Dispose()).Message);
        }

        [Theory]
        // A short command truncates, an empty one fails, and a failure wins in either order.
        [InlineData(4090L, 16L, -1L, 0L, ReadStatus.Truncated, 6L)]
        [InlineData(4096L, 4L, -1L, 0L, ReadStatus.Failed, 0L)]
        [InlineData(4094L, 4L, 5000L, 4L, ReadStatus.Failed, 2L)]
        [InlineData(5000L, 4L, 4094L, 4L, ReadStatus.Failed, 2L)]
        [InlineData(10L, 0L, 12L, 2L, ReadStatus.Complete, 2L)]
        public void StatusFollowsTheShortestCommand(long offset0, long size0, long offset1, long size1, ReadStatus status, long bytes)
        {
            using var buffer = new Buffer(32);
            var handle = offset1 < 0 ? ReadByName(m_Path, buffer, (offset0, size0)) : ReadByName(m_Path, buffer, (offset0, size0), (offset1, size1));
            Assert.Equal(status, handle.Status);
            Assert.Equal(bytes, handle.GetBytesRead());
            Assert.Equal(offset1 < 0 ? 1 : 2, handle.ReadCount);
            handle.Dispose();
        }

        [Fact]
        public void MissingFilesFailEveryCommandAndCountThem()
        {
            using var buffer = new Buffer(16);
            var handle = ReadByName(Path.Combine(m_Directory, "missing.bin"), buffer, (0, 8), (8, 8));
            Assert.Equal(ReadStatus.Failed, handle.Status);
            Assert.Equal(2, handle.ReadCount);
            Assert.Equal(Sentinel, buffer.Bytes[0]);
            handle.Dispose();

            var none = ReadByName(m_Path, buffer);
            Assert.Equal(ReadStatus.Complete, none.Status);
            Assert.Equal(0, none.ReadCount);
            none.Dispose();
        }

        [Fact]
        public void ReadCopiesItsCommands()
        {
            using var buffer = new Buffer(8);
            var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand), 16, Allocator.Persistent);
            commands[0] = new ReadCommand { Offset = 200, Size = 8, Buffer = buffer.Bytes };
            var handle = AsyncReadManager.Read(m_Path, commands, 1);
            commands[0].Offset = 300;
            UnsafeUtility.Free(commands, Allocator.Persistent);
            handle.JobHandle.Complete();
            Assert.Equal(Expected(200), buffer.Bytes[0]);
            handle.Dispose();
        }

        [Fact]
        public void AReusedSlotDoesNotAnswerForAnOlderHandle()
        {
            using var buffer = new Buffer(8);
            var first = ReadByName(m_Path, buffer, (0, 4));
            first.Dispose();
            var second = ReadByName(m_Path, buffer, (4, 4));
            Assert.Equal(first.ptr, second.ptr);
            Assert.False(first.IsValid());
            Assert.True(second.IsValid());
            Assert.Throws<InvalidOperationException>(() => first.Cancel());
            second.Dispose();
        }

        [Fact]
        public void FileInfoReportsRegularFilesOnly()
        {
            FileInfoResult result = new FileInfoResult { FileSize = -7 };
            var handle = AsyncReadManager.GetFileInfo(m_Path, &result);
            handle.JobHandle.Complete();
            Assert.Equal((Length, FileState.Exists), (result.FileSize, result.FileState));
            Assert.Equal(0, handle.ReadCount);
            handle.Dispose();
            foreach (var path in new[] { m_Directory, Path.Combine(m_Directory, "missing.bin"), "", null })
            {
                result = new FileInfoResult { FileSize = -7, FileState = FileState.Exists };
                handle = AsyncReadManager.GetFileInfo(path, &result);
                handle.JobHandle.Complete();
                Assert.Equal((0L, FileState.Absent), (result.FileSize, result.FileState));
                Assert.Equal(ReadStatus.Complete, handle.Status);
                handle.Dispose();
            }
            Assert.Throws<NullReferenceException>(() => AsyncReadManager.GetFileInfo(m_Path, null));
        }

        static readonly ManualResetEventSlim s_Release = new ManualResetEventSlim(false);

        struct HoldJob : IJob
        {
            public void Execute() => s_Release.Wait();
        }

        struct WriteCommandsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public ReadCommandArray* Array;
            [NativeDisableUnsafePtrRestriction] public ReadCommand* Commands;
            [NativeDisableUnsafePtrRestriction] public byte* Buffer;

            public void Execute()
            {
                Commands[0] = new ReadCommand { Offset = 64, Size = 8, Buffer = Buffer };
                Array->ReadCommands = Commands;
                Array->CommandCount = 1;
            }
        }

        [Fact]
        public void DeferredReadsTakeTheCommandsWhenTheDependencyCompletesAndOutliveAClose()
        {
            using var buffer = new Buffer(8);
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var array = (ReadCommandArray*)UnsafeUtility.Malloc(sizeof(ReadCommandArray), 16, Allocator.Persistent);
            var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand), 16, Allocator.Persistent);
            *array = default;
            s_Release.Reset();
            try
            {
                var hold = new HoldJob().Schedule();
                var write = new WriteCommandsJob { Array = array, Commands = commands, Buffer = buffer.Bytes }.Schedule(hold);
                JobHandle.ScheduleBatchedJobs();
                var read = AsyncReadManager.ReadDeferred(file, array, write);
                Assert.Equal(ReadStatus.InProgress, read.Status);
                // Closed before the read runs: the handle is invalid at once, the read still reads.
                var close = file.Close(read.JobHandle);
                Assert.False(file.IsValid());
                s_Release.Set();
                close.Complete();
                Assert.Equal(ReadStatus.Complete, read.Status);
                for (int i = 0; i < 8; i++) Assert.Equal(Expected(64 + i), buffer.Bytes[i]);
                read.Dispose();
                write.Complete();
            }
            finally
            {
                s_Release.Set();
                UnsafeUtility.Free(commands, Allocator.Persistent);
                UnsafeUtility.Free(array, Allocator.Persistent);
            }
        }

        /// <summary>Red: a close without a dependency did not wait for a read queued before it,
        /// which then failed on the closed file.</summary>
        [Fact]
        public void ACloseWithoutADependencyWaitsForTheQueuedReads()
        {
            using var buffer = new Buffer(8);
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var command = new ReadCommand { Offset = 512, Size = 8, Buffer = buffer.Bytes };
            var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
            s_Release.Reset();
            try
            {
                var hold = new HoldJob().Schedule();
                JobHandle.ScheduleBatchedJobs();
                var read = AsyncReadManager.ReadDeferred(file, &array, hold);
                var close = file.Close();
                Assert.False(close.IsCompleted);
                Assert.Equal(ReadStatus.InProgress, read.Status);
                s_Release.Set();
                close.Complete();
                Assert.Equal(ReadStatus.Complete, read.Status);
                for (int i = 0; i < 8; i++) Assert.Equal(Expected(512 + i), buffer.Bytes[i]);
                read.Dispose();
                hold.Complete();
            }
            finally
            {
                s_Release.Set();
            }
        }

        /// <summary>Makes a request that is disposed at once: each request releases the jobs that
        /// may be released.</summary>
        void Sweep()
        {
            FileInfoResult result;
            var info = AsyncReadManager.GetFileInfo(m_Path, &result);
            info.JobHandle.Complete();
            info.Dispose();
        }

        /// <summary>Red: requests whose handles were never completed kept their job records for
        /// the rest of the session, also those that wait for a job of the caller's.</summary>
        [Fact]
        public void FinishedRequestsAreReleasedAndCallersJobsStayTheCallers()
        {
            using var buffer = new Buffer(8);
            var command = new ReadCommand { Offset = 0, Size = 8, Buffer = buffer.Bytes };
            var read = AsyncReadManager.Read(m_Path, &command, 1);
            var job = read.JobHandle;
            Assert.True(JobSystem.RecordOf(job).Finished.Wait(10_000));
            read.Dispose();
            Assert.Null(JobSystem.RecordOf(job));

            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
            var user = new MarkJob().Schedule();
            JobHandle.ScheduleBatchedJobs();
            var deferred = AsyncReadManager.ReadDeferred(file, &array, user);
            var deferredJob = deferred.JobHandle;
            Assert.True(JobSystem.RecordOf(deferredJob).Finished.Wait(10_000));
            deferred.Dispose();
            Sweep();
            // Syncing the read would sync the caller's job: it waits for the caller.
            Assert.NotNull(JobSystem.RecordOf(deferredJob));
            Assert.NotNull(JobSystem.RecordOf(user));
            user.Complete();
            Sweep();
            Assert.Null(JobSystem.RecordOf(deferredJob));

            // A close that waits for a caller's job, never completed by the caller, through a
            // combined handle whose parts the caller completes one by one.
            var first = new MarkJob().Schedule();
            var second = new MarkJob().Schedule();
            var both = JobHandle.CombineDependencies(first, second);
            JobHandle.ScheduleBatchedJobs();
            var close = file.Close(both);
            Assert.True(JobSystem.RecordOf(close).Finished.Wait(10_000));
            Sweep();
            Assert.NotNull(JobSystem.RecordOf(close));
            first.Complete();
            second.Complete();
            Sweep();
            Assert.Null(JobSystem.RecordOf(close));
        }

        [Fact]
        public void APendingCanceledReadNeverReads()
        {
            using var buffer = new Buffer(8);
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var command = new ReadCommand { Offset = 0, Size = 8, Buffer = buffer.Bytes };
            var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
            s_Release.Reset();
            try
            {
                var hold = new HoldJob().Schedule();
                JobHandle.ScheduleBatchedJobs();
                var read = AsyncReadManager.ReadDeferred(file, &array, hold);
                var job = read.JobHandle;
                read.Cancel();
                Assert.Equal(ReadStatus.Canceled, read.Status);
                s_Release.Set();
                file.Close(job).Complete();
                Assert.Equal(ReadStatus.Canceled, read.Status);
                Assert.Equal(0, read.ReadCount);
                Assert.Equal(Sentinel, buffer.Bytes[0]);
                read.Dispose();
            }
            finally
            {
                s_Release.Set();
            }
        }

        [Fact]
        public void FileHandlesOpenFailOrOpenDirectories()
        {
            var missing = AsyncReadManager.OpenFileAsync(Path.Combine(m_Directory, "missing.bin"));
            Assert.True(missing.IsValid());
            missing.JobHandle.Complete();
            Assert.Equal(FileStatus.OpenFailed, missing.Status);
            missing.Close().Complete();
            Assert.Equal("FileHandle.Close cannot be called twice on the same FileHandle",
                Assert.Throws<InvalidOperationException>(() => missing.Close()).Message);

            using var buffer = new Buffer(4);
            var directory = AsyncReadManager.OpenFileAsync(m_Directory);
            directory.JobHandle.Complete();
            Assert.Equal(FileStatus.Open, directory.Status);
            var command = new ReadCommand { Size = 4, Buffer = buffer.Bytes };
            var read = AsyncReadManager.Read(directory, new ReadCommandArray { ReadCommands = &command, CommandCount = 1 });
            read.JobHandle.Complete();
            Assert.Equal(ReadStatus.Failed, read.Status);
            Assert.Equal(1, read.ReadCount);
            read.Dispose();
            directory.Close().Complete();

            Assert.Equal("FileName is empty", Assert.Throws<InvalidOperationException>(() => AsyncReadManager.OpenFileAsync("")).Message);
            Assert.Throws<NullReferenceException>(() => AsyncReadManager.OpenFileAsync(null));
            Assert.Equal("FileHandle is invalid and may not be read from.",
                Assert.Throws<InvalidOperationException>(() => AsyncReadManager.Read(default(FileHandle), default)).Message);
        }

        [Fact]
        public void FrameTimingsKeepTheLastFramesNewestFirst()
        {
            var ring = new FrameTimingRing();
            var timings = new FrameTiming[FrameTimings.Capacity + 8];
            Assert.Equal(0u, ring.Latest(4, timings));
            ulong t = 1_000;
            for (int i = 0; i < FrameTimings.Capacity + 5; i++)
            {
                ring.BeginFrame(t);
                ring.BeginRendering(t + 100);
                ring.EndRendering(t + 400);
                ring.Present(t + 500, 60);
                t += 1_000_000;
            }
            // The frame in progress is not kept yet.
            Assert.Equal((uint)FrameTimings.Capacity, ring.Latest(1000, timings));
            ring.BeginFrame(t);
            Assert.Equal((uint)FrameTimings.Capacity, ring.Latest(1000, timings));
            // Reported four frames after it ran: the frame that started four frames ago.
            ulong newest = t - FrameTimings.DelayFrames * 1_000_000UL;
            Assert.Equal(newest, timings[0].frameStartTimestamp);
            Assert.Equal(newest - 1_000_000, timings[1].frameStartTimestamp);
            Assert.Equal(newest + 100, timings[0].firstSubmitTimestamp);
            Assert.Equal(newest + 500, timings[0].cpuTimePresentCalled);
            Assert.Equal(timings[0].cpuTimePresentCalled, timings[0].cpuTimeFrameComplete);
            Assert.Equal(1.0, timings[0].cpuFrameTime, 9);
            Assert.Equal(0.0005, timings[0].cpuMainThreadFrameTime, 9);
            Assert.Equal(0.0003, timings[0].cpuRenderThreadFrameTime, 9);
            Assert.Equal(0, timings[0].gpuFrameTime);
            Assert.Equal(60u, timings[0].syncInterval);
            Assert.Equal((1f, 1f), (timings[0].widthScale, timings[0].heightScale));
            // The count does not depend on the array; only what fits is written.
            var two = new FrameTiming[2];
            Assert.Equal(8u, ring.Latest(8, two));
            Assert.Equal(timings[1].frameStartTimestamp, two[1].frameStartTimestamp);
            Assert.Equal(3u, ring.Latest(3, null));
        }

        [Fact]
        public void AFrameWithoutRenderingSubmitsAtItsPresent()
        {
            var ring = new FrameTimingRing();
            ring.BeginFrame(10);
            ring.Present(50, 60);
            var timings = new FrameTiming[1];
            for (int i = 1; i <= FrameTimings.DelayFrames; i++)
            {
                ring.BeginFrame(10 + 100UL * (ulong)i);
                Assert.Equal(i == FrameTimings.DelayFrames ? 1u : 0u, ring.Latest(1, timings));
            }
            Assert.Equal(10UL, timings[0].frameStartTimestamp);
            Assert.Equal(50UL, timings[0].firstSubmitTimestamp);
            Assert.Equal(0, timings[0].cpuRenderThreadFrameTime);
        }

        [Fact]
        public void ALongFrameIsTheNewestTimingFourFramesLater()
        {
            // As the probe measures: frame 35 lasts 250 ms; in frame 39 it is the newest timing,
            // in frame 45 the seventh.
            var ring = new FrameTimingRing();
            var timings = new FrameTiming[16];
            ulong t = 0;
            int newestAt = -1;
            for (int frame = 1; frame <= 45; frame++)
            {
                ring.BeginFrame(t);
                if (newestAt < 0 && ring.Latest(1, timings) > 0 && timings[0].cpuFrameTime >= 250) newestAt = frame;
                ring.Present(t + 1000, 60);
                t += frame == 35 ? 250_000_000UL : 16_000_000UL;
            }
            Assert.Equal(39, newestAt);
            Assert.Equal(16u, ring.Latest(16, timings));
            Assert.Equal(6, Array.FindIndex(timings, timing => timing.cpuFrameTime >= 250));
        }

        static int s_UserJobRan;

        struct MarkJob : IJob
        {
            public void Execute() => Interlocked.Exchange(ref s_UserJobRan, 1);
        }

        [Fact]
        public void ARequestStartsWithoutFlushingTheCallersJobs()
        {
            Interlocked.Exchange(ref s_UserJobRan, 0);
            var user = new MarkJob().Schedule();
            using var buffer = new Buffer(8);
            var command = new ReadCommand { Offset = 0, Size = 8, Buffer = buffer.Bytes };
            var read = AsyncReadManager.Read(m_Path, &command, 1);
            // Polled, never completed: the read runs on its own.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (read.Status == ReadStatus.InProgress && DateTime.UtcNow < deadline) Thread.Sleep(1);
            Assert.Equal(ReadStatus.Complete, read.Status);
            Assert.Equal(0, Volatile.Read(ref s_UserJobRan));
            user.Complete();
            Assert.Equal(1, s_UserJobRan);
            read.Dispose();
        }
    }
}
