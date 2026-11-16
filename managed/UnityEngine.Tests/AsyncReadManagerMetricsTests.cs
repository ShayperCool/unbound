using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>AsyncReadManagerMetrics' records and summaries (tests/projects/asyncread
    /// metrics.* compares them with the Editor).</summary>
    public unsafe class AsyncReadManagerMetricsTests : IDisposable
    {
        const int Length = 4096;
        readonly string m_Directory;
        readonly string m_Path;
        readonly byte* m_Buffer;

        public AsyncReadManagerMetricsTests()
        {
            m_Directory = Path.Combine(Path.GetTempPath(), "unbound-readmetrics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            m_Path = Path.Combine(m_Directory, "data.bin");
            File.WriteAllBytes(m_Path, new byte[Length]);
            m_Buffer = (byte*)UnsafeUtility.Malloc(Length, 16, Allocator.Persistent);
            AsyncReadManagerMetrics.StopCollectingMetrics();
            AsyncReadManagerMetrics.GetMetrics(AsyncReadManagerMetrics.Flags.ClearOnRead);
        }

        public void Dispose()
        {
            s_Release.Set();
            AsyncReadManagerMetrics.StopCollectingMetrics();
            AsyncReadManagerMetrics.GetMetrics(AsyncReadManagerMetrics.Flags.ClearOnRead);
            UnsafeUtility.Free(m_Buffer, Allocator.Persistent);
            Directory.Delete(m_Directory, true);
        }

        static readonly ManualResetEventSlim s_Release = new ManualResetEventSlim(true);

        struct HoldJob : IJob
        {
            public void Execute() => s_Release.Wait();
        }

        struct WriteCommandJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public ReadCommandArray* Array;
            [NativeDisableUnsafePtrRestriction] public ReadCommand* Commands;
            [NativeDisableUnsafePtrRestriction] public byte* Buffer;

            public void Execute()
            {
                Commands[0] = new ReadCommand { Offset = 64, Size = 8, Buffer = Buffer };
                Commands[1] = new ReadCommand { Offset = 128, Size = 4, Buffer = Buffer + 8 };
                Array->ReadCommands = Commands;
                Array->CommandCount = 2;
            }
        }

        ReadStatus Read(string path, (long Offset, long Size)[] reads, string asset = "", ulong type = 0, AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts)
        {
            var commands = stackalloc ReadCommand[Math.Max(reads.Length, 1)];
            long at = 0;
            for (int i = 0; i < reads.Length; i++)
            {
                commands[i] = new ReadCommand { Offset = reads[i].Offset, Size = reads[i].Size, Buffer = m_Buffer + at };
                at += Math.Max(reads[i].Size, 0);
            }
            var handle = AsyncReadManager.Read(path, commands, (uint)reads.Length, asset, type, subsystem);
            handle.JobHandle.Complete();
            var status = handle.Status;
            handle.Dispose();
            return status;
        }

        static AsyncReadManagerRequestMetric[] All() => AsyncReadManagerMetrics.GetMetrics(AsyncReadManagerMetrics.Flags.None);

        [Fact]
        public void NothingIsRecordedUntilCollectionStartsAndStoppingKeepsTheRecords()
        {
            Assert.False(AsyncReadManagerMetrics.IsEnabled());
            Read(m_Path, new[] { (0L, 8L) });
            Assert.Empty(All());
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Assert.True(AsyncReadManagerMetrics.IsEnabled());
            Read(m_Path, new[] { (0L, 8L) });
            AsyncReadManagerMetrics.StopCollectingMetrics();
            Assert.False(AsyncReadManagerMetrics.IsEnabled());
            Read(m_Path, new[] { (0L, 8L) });
            Assert.Single(All());
            // Starting again keeps what was recorded.
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Assert.Single(All());
        }

        [Fact]
        public void EachCommandIsARecordWithItsOwnOutcome()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Assert.Equal(ReadStatus.Failed, Read(m_Path, new[] { (0L, 8L), (5000L, 4L), (Length - 2L, 4L) }, "Asset", 28, AssetLoadingSubsystem.Texture));
            var all = All();
            Assert.Equal(3, all.Length);
            Assert.Equal(new[] { ProcessingState.Completed, ProcessingState.Failed, ProcessingState.Completed }, Array.ConvertAll(all, m => m.State));
            Assert.Equal(new[] { 8UL, 0UL, 2UL }, Array.ConvertAll(all, m => m.CurrentBytesRead));
            Assert.Equal(new[] { 0UL, 5000UL, Length - 2UL }, Array.ConvertAll(all, m => m.OffsetBytes));
            foreach (var metric in all)
            {
                Assert.Equal("Asset", metric.AssetName);
                Assert.Equal(m_Path, metric.FileName);
                Assert.Equal(28UL, metric.AssetTypeId);
                Assert.Equal(3U, metric.BatchReadCount);
                Assert.True(metric.IsBatchRead);
                Assert.Equal(FileReadType.Async, metric.ReadType);
                Assert.Equal(Priority.PriorityLow, metric.PriorityLevel);
                Assert.Equal(AssetLoadingSubsystem.Texture, metric.Subsystem);
                Assert.True(metric.RequestTimeMicroseconds > 0);
                Assert.True(metric.TimeInQueueMicroseconds > 0);
                Assert.True(metric.TotalTimeMicroseconds >= metric.TimeInQueueMicroseconds);
            }
            // No commands, no records.
            Read(m_Path, new (long, long)[0]);
            Assert.Equal(3, All().Length);
        }

        [Fact]
        public void AFileThatDoesNotOpenSpendsNoTimeQueuedWhileADirectoryDoes()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Read(Path.Combine(m_Directory, "missing.bin"), new[] { (0L, 8L) });
            Read(m_Directory, new[] { (0L, 8L) });
            var all = All();
            Assert.Equal(ProcessingState.Failed, all[0].State);
            Assert.Equal(0.0, all[0].TimeInQueueMicroseconds);
            Assert.True(all[0].TotalTimeMicroseconds > 0);
            Assert.Equal(ProcessingState.Failed, all[1].State);
            Assert.True(all[1].TimeInQueueMicroseconds > 0);
        }

        [Fact]
        public void HandleReadsNameNoFileAndFileInfoIsItsOwnRecord()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var command = new ReadCommand { Offset = 4, Size = 8, Buffer = m_Buffer };
            var read = AsyncReadManager.Read(file, new ReadCommandArray { ReadCommands = &command, CommandCount = 1 });
            file.Close(read.JobHandle).Complete();
            read.Dispose();
            FileInfoResult info;
            var infoRead = AsyncReadManager.GetFileInfo(m_Path, &info);
            infoRead.JobHandle.Complete();
            infoRead.Dispose();
            var all = All();
            Assert.Equal(2, all.Length);
            Assert.Equal("", all[0].FileName);
            Assert.Equal("", all[0].AssetName);
            Assert.Equal(AssetLoadingSubsystem.Scripts, all[0].Subsystem);
            Assert.Equal(ProcessingState.Completed, all[0].State);
            Assert.Equal(m_Path, all[1].FileName);
            Assert.Equal(AssetLoadingSubsystem.FileInfo, all[1].Subsystem);
            Assert.Equal(0U, all[1].BatchReadCount);
            Assert.False(all[1].IsBatchRead);
            Assert.Equal(ProcessingState.Completed, all[1].State);
        }

        [Fact]
        public void AQueuedReadStaysThroughClearsAndACanceledOneIsListedButNotSummarized()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Read(m_Path, new[] { (0L, 32L) });
            Read(Path.Combine(m_Directory, "missing.bin"), new[] { (0L, 8L) });
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var command = new ReadCommand { Offset = 0, Size = 16, Buffer = m_Buffer };
            var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
            s_Release.Reset();
            var hold = new HoldJob().Schedule();
            JobHandle.ScheduleBatchedJobs();
            var pending = AsyncReadManager.ReadDeferred(file, &array, hold);
            var all = All();
            Assert.Equal(ProcessingState.InQueue, all[2].State);
            var summary = AsyncReadManagerMetrics.GetCurrentSummaryMetrics(AsyncReadManagerMetrics.Flags.None);
            // The bytes asked for by finished requests, the failed one included; the average over
            // the finished ones; the queued one counted as waiting.
            Assert.Equal(40UL, summary.TotalBytesRead);
            Assert.Equal(20f, summary.AverageReadSizeInBytes);
            Assert.Equal(1, summary.NumberOfCompletedRequests);
            Assert.Equal(1, summary.NumberOfFailedRequests);
            Assert.Equal(1, summary.NumberOfWaitingRequests);
            Assert.Equal(3, summary.TotalNumberOfRequests);
            AsyncReadManagerMetrics.ClearCompletedMetrics();
            Assert.Equal(ProcessingState.InQueue, Assert.Single(All()).State);

            var canceled = AsyncReadManager.ReadDeferred(file, &array, hold);
            canceled.Cancel();
            s_Release.Set();
            file.Close(JobHandle.CombineDependencies(pending.JobHandle, canceled.JobHandle)).Complete();
            hold.Complete();
            pending.Dispose();
            canceled.Dispose();
            all = All();
            Assert.Equal(new[] { ProcessingState.Completed, ProcessingState.Canceled }, Array.ConvertAll(all, m => m.State));
            summary = AsyncReadManagerMetrics.GetSummaryOfMetrics(all);
            Assert.Equal(1, summary.TotalNumberOfRequests);
            Assert.Equal(0, summary.NumberOfCanceledRequests);
            Assert.Equal(16UL, summary.TotalBytesRead);
            Assert.Equal(0, AsyncReadManagerMetrics.GetSummaryOfMetrics(all, new AsyncReadManagerMetricsFilters(ProcessingState.Canceled)).TotalNumberOfRequests);
            // ClearCompletedMetrics drops the finished records, failed and canceled ones included
            // (the Scripting API), as ClearOnRead does.
            AsyncReadManagerMetrics.ClearCompletedMetrics();
            Assert.Empty(All());
        }

        [Fact]
        public void ADeferredReadIsRecordedWithTheCommandsItTakes()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            var file = AsyncReadManager.OpenFileAsync(m_Path);
            var array = (ReadCommandArray*)UnsafeUtility.Malloc(sizeof(ReadCommandArray), 16, Allocator.Persistent);
            var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand) * 2, 16, Allocator.Persistent);
            *array = default;
            s_Release.Reset();
            try
            {
                var hold = new HoldJob().Schedule();
                var write = new WriteCommandJob { Array = array, Commands = commands, Buffer = m_Buffer }.Schedule(hold);
                JobHandle.ScheduleBatchedJobs();
                var read = AsyncReadManager.ReadDeferred(file, array, write);
                // Nothing to record yet: the array holds no commands.
                Assert.Empty(All());
                s_Release.Set();
                file.Close(read.JobHandle).Complete();
                Assert.Equal(ReadStatus.Complete, read.Status);
                Assert.Equal(12, read.GetBytesRead());
                read.Dispose();
                write.Complete();
                var all = All();
                Assert.Equal(new[] { 64UL, 128UL }, Array.ConvertAll(all, m => m.OffsetBytes));
                Assert.Equal(new[] { 8UL, 4UL }, Array.ConvertAll(all, m => m.CurrentBytesRead));
                Assert.All(all, m => Assert.Equal(2U, m.BatchReadCount));
            }
            finally
            {
                s_Release.Set();
                UnsafeUtility.Free(commands, Allocator.Persistent);
                UnsafeUtility.Free(array, Allocator.Persistent);
            }
        }

        [Fact]
        public void FiltersKeepWhatEverySetCategoryHolds()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Read(m_Path, new[] { (0L, 8L) });
            Read(m_Path, new[] { (0L, 4L) }, "M", 43, AssetLoadingSubsystem.Mesh);
            Read(Path.Combine(m_Directory, "missing.bin"), new[] { (0L, 2L) }, "M", 43, AssetLoadingSubsystem.Mesh);
            var none = AsyncReadManagerMetrics.Flags.None;
            Assert.Equal(2, AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(43UL), none).Length);
            Assert.Single(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(null, new[] { ProcessingState.Completed }, null, null, new[] { AssetLoadingSubsystem.Mesh }), none));
            Assert.Equal(3, AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(new ProcessingState[0]), none).Length);
            Assert.Empty(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(Priority.PriorityHigh), none));
            var filters = new AsyncReadManagerMetricsFilters(FileReadType.Sync);
            Assert.Empty(AsyncReadManagerMetrics.GetMetrics(filters, none));
            filters.ClearFilters();
            Assert.Equal(3, AsyncReadManagerMetrics.GetMetrics(filters, none).Length);
            var e = Assert.Throws<NullReferenceException>(() => AsyncReadManagerMetrics.GetMetrics((AsyncReadManagerMetricsFilters)null, none));
            Assert.Equal("(null)", e.Message);
            Assert.Throws<NullReferenceException>(() => AsyncReadManagerMetrics.GetCurrentSummaryMetrics(null, none));
        }

        [Fact]
        public void ListsAreFilledAndSummarizedAsArraysAre()
        {
            AsyncReadManagerMetrics.StartCollectingMetrics();
            Read(m_Path, new[] { (0L, 8L) });
            Read(m_Path, new[] { (8L, 8L) });
            var list = new List<AsyncReadManagerRequestMetric> { default, default, default };
            AsyncReadManagerMetrics.GetMetrics(list, AsyncReadManagerMetrics.Flags.None);
            Assert.Equal(2, list.Count);
            Assert.Equal(16UL, AsyncReadManagerMetrics.GetSummaryOfMetrics(list).TotalBytesRead);
            Assert.Equal(0UL, AsyncReadManagerMetrics.GetSummaryOfMetrics((List<AsyncReadManagerRequestMetric>)null).TotalBytesRead);
            var e = Assert.Throws<ArgumentNullException>(() => AsyncReadManagerMetrics.GetMetrics((List<AsyncReadManagerRequestMetric>)null, AsyncReadManagerMetrics.Flags.None));
            Assert.Equal("Value cannot be null.\nParameter name: metrics", e.Message);
        }
    }
}
