using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// AsyncReadManager, its handles and FrameTimingManager as game code sees them in Play mode.
/// Every outcome is recorded after its JobHandle completed, so no record depends on timing; a
/// read that must not start yet waits behind a job that holds until the probe releases it.
/// </summary>
public unsafe class AsyncReadProbe : MonoBehaviour
{
    public static bool Finished;
    const string OutVariable = "ASYNC_READ_OUT";
    const int DataLength = 4096;
    const byte Sentinel = 0xEE;
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static void Raw(string key, string json)
    {
        if (!s_Keys.Add(key)) json = Quote("duplicate key");
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(json).Append("}\n");
    }

    static void Record(string key, string value) => Raw(key, Quote(value));
    static void Record(string key, long value) => Raw(key, value.ToString());
    static void Record(string key, bool value) => Raw(key, value ? "true" : "false");

    static void Try(string key, Func<string> action)
    {
        try { Record(key, action()); }
        catch (Exception e) { Record(key, "throws " + e.GetType().Name + ": " + e.Message); }
    }

    static void Try(string key, Action action) => Try(key, () => { action(); return "ok"; });

    static byte Expected(long i) => (byte)((i * 7 + 3) & 255);

    static string Hex(byte* bytes, int length)
    {
        var b = new StringBuilder(length * 2);
        for (int i = 0; i < length; i++) b.Append(bytes[i].ToString("x2"));
        return b.ToString();
    }

    static byte* Buffer(int length)
    {
        var buffer = (byte*)UnsafeUtility.Malloc(length, 16, Allocator.Persistent);
        UnsafeUtility.MemSet(buffer, Sentinel, length);
        return buffer;
    }

    static ReadCommand Command(long offset, long size, byte* buffer) =>
        new ReadCommand { Offset = offset, Size = size, Buffer = buffer };

    /// <summary>Records what a completed read handle reports, disposes it and records its
    /// disposed state.</summary>
    static void Outcome(string key, ReadHandle handle, uint commands)
    {
        Record(key + ".valid", handle.IsValid());
        handle.JobHandle.Complete();
        Record(key + ".status", handle.Status.ToString());
        Record(key + ".readCount", handle.ReadCount);
        Record(key + ".bytesRead", handle.GetBytesRead());
        var perCommand = new StringBuilder();
        ulong* array = handle.GetBytesReadArray();
        for (uint i = 0; i < commands; i++)
        {
            if (i > 0) perCommand.Append(',');
            perCommand.Append(handle.GetBytesRead(i)).Append('/').Append(array == null ? "null" : array[i].ToString());
        }
        Record(key + ".bytesReadPerCommand", perCommand.ToString());
        Try(key + ".cancelAfterCompletion", () => { handle.Cancel(); return handle.Status.ToString(); });
        Try(key + ".dispose", () => handle.Dispose());
        Record(key + ".validAfterDispose", handle.IsValid());
        Try(key + ".statusAfterDispose", () => handle.Status.ToString());
        Try(key + ".disposeTwice", () => handle.Dispose());
    }

    struct HoldJob : IJob
    {
        public void Execute()
        {
            while (Volatile.Read(ref s_Release) == 0) Thread.Sleep(1);
        }
    }

    struct WriteCommandsJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public ReadCommandArray* Array;
        [NativeDisableUnsafePtrRestriction] public ReadCommand* Commands;
        [NativeDisableUnsafePtrRestriction] public byte* Buffer;

        public void Execute()
        {
            Commands[0] = Command(64, 8, Buffer);
            Commands[1] = Command(4000, 4, Buffer + 8);
            Array->ReadCommands = Commands;
            Array->CommandCount = 2;
        }
    }

    static int s_Release;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutVariable) == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("AsyncReadProbe").AddComponent<AsyncReadProbe>().gameObject);
    }

    string m_Absolute;
    string m_Relative;
    string m_Missing;
    string m_Directory;

    void Start()
    {
        try { Run(); }
        catch (Exception e) { Record("error", e.ToString()); }
    }

    static readonly List<string> s_Logs = new List<string>();
    static string s_Root;

    static void OnLog(string message, string stack, LogType type)
    {
        lock (s_Logs) s_Logs.Add(type + ": " + (s_Root != null ? message.Replace(s_Root, "<project>") : message));
    }

    /// <summary>Records the log messages since the previous section, sorted (reads finish on
    /// other threads).</summary>
    static void Logs(string section)
    {
        string[] messages;
        lock (s_Logs)
        {
            messages = s_Logs.ToArray();
            s_Logs.Clear();
        }
        Array.Sort(messages, StringComparer.Ordinal);
        Record("log." + section, string.Join(" | ", messages));
    }

    void Run()
    {
        s_Root = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar);
        Application.logMessageReceivedThreaded += OnLog;
        m_Directory = Path.Combine("Temp", "AsyncReadProbe");
        Directory.CreateDirectory(m_Directory);
        m_Relative = Path.Combine(m_Directory, "data.bin");
        var data = new byte[DataLength];
        for (int i = 0; i < data.Length; i++) data[i] = Expected(i);
        File.WriteAllBytes(m_Relative, data);
        m_Absolute = Path.GetFullPath(m_Relative);
        m_Missing = Path.Combine(Path.GetFullPath(m_Directory), "missing.bin");

        Defaults();
        Logs("defaults");
        FileInfo();
        Logs("info");
        ReadByName();
        Logs("name");
        FileHandles();
        Logs("open");
        Deferred();
        Logs("deferred");
        Cancel();
        Logs("cancel");
        FrameTimingStatic();
        Logs("timing");
        Metrics();
        Logs("metrics");
    }

    static string Positive(double v) => v > 0 ? "positive" : v == 0 ? "0" : v < 0 ? "negative" : v.ToString();

    string DescribeMetric(AsyncReadManagerRequestMetric m) =>
        "asset=" + m.AssetName + " file=" + (m.FileName == null ? "null" : m.FileName.Replace(s_Root, "<project>")) +
        " offset=" + m.OffsetBytes + " size=" + m.SizeBytes + " type=" + m.AssetTypeId + " read=" + m.CurrentBytesRead +
        " batch=" + m.BatchReadCount + " isBatch=" + m.IsBatchRead + " state=" + m.State + " readType=" + m.ReadType +
        " priority=" + m.PriorityLevel + " subsystem=" + m.Subsystem + " request=" + Positive(m.RequestTimeMicroseconds) +
        " queue=" + Positive(m.TimeInQueueMicroseconds) + " total=" + Positive(m.TotalTimeMicroseconds);

    string DescribeAll(AsyncReadManagerRequestMetric[] all)
    {
        if (all == null) return "null";
        var parts = new List<string>();
        foreach (var m in all) parts.Add(DescribeMetric(m));
        return all.Length + ": " + string.Join(" ; ", parts);
    }

    static string DescribeSummary(AsyncReadManagerSummaryMetrics s)
    {
        if (s == null) return "null";
        return "bytes=" + s.TotalBytesRead + " bw=" + Positive(s.AverageBandwidthMBPerSecond) + " avgSize=" + s.AverageReadSizeInBytes +
            " wait=" + Positive(s.AverageWaitTimeMicroseconds) + " readTime=" + Positive(s.AverageReadTimeMicroseconds) +
            " totalTime=" + Positive(s.AverageTotalRequestTimeMicroseconds) + " tp=" + Positive(s.AverageThroughputMBPerSecond) +
            " longestWait=" + Positive(s.LongestWaitTimeMicroseconds) + " longestRead=" + Positive(s.LongestReadTimeMicroseconds) +
            " readAsset=" + s.LongestReadAssetType + " waitAsset=" + s.LongestWaitAssetType + " readSub=" + s.LongestReadSubsystem +
            " waitSub=" + s.LongestWaitSubsystem + " inProgress=" + s.NumberOfInProgressRequests + " completed=" + s.NumberOfCompletedRequests +
            " failed=" + s.NumberOfFailedRequests + " waiting=" + s.NumberOfWaitingRequests + " canceled=" + s.NumberOfCanceledRequests +
            " total=" + s.TotalNumberOfRequests + " cached=" + s.NumberOfCachedReads + " async=" + s.NumberOfAsyncReads + " sync=" + s.NumberOfSyncReads;
    }

    ReadHandle MetricsRead(string key, (long Offset, long Size)[] reads, string path = null, string asset = null,
        ulong type = 0, AssetLoadingSubsystem subsystem = AssetLoadingSubsystem.Scripts, bool complete = true)
    {
        int total = 1;
        foreach (var r in reads) total += (int)Math.Max(r.Size, 0);
        var buffer = Buffer(total);
        var commands = new NativeArray<ReadCommand>(Math.Max(reads.Length, 1), Allocator.Persistent);
        int at = 0;
        for (int i = 0; i < reads.Length; i++) { commands[i] = Command(reads[i].Offset, reads[i].Size, buffer + at); at += (int)reads[i].Size; }
        var handle = asset == null
            ? AsyncReadManager.Read(path ?? m_Absolute, (ReadCommand*)commands.GetUnsafePtr(), (uint)reads.Length)
            : AsyncReadManager.Read(path ?? m_Absolute, (ReadCommand*)commands.GetUnsafePtr(), (uint)reads.Length, asset, type, subsystem);
        handle.JobHandle.Complete();
        Record(key, handle.Status.ToString());
        handle.Dispose();
        commands.Dispose();
        UnsafeUtility.Free(buffer, Allocator.Persistent);
        return handle;
    }

    static AsyncReadManagerRequestMetric[] All() => AsyncReadManagerMetrics.GetMetrics(AsyncReadManagerMetrics.Flags.None);
    static AsyncReadManagerRequestMetric[] Take() => AsyncReadManagerMetrics.GetMetrics(AsyncReadManagerMetrics.Flags.ClearOnRead);

    void Metrics()
    {
        var none = AsyncReadManagerMetrics.Flags.None;
        Try("metrics.enabled", () => AsyncReadManagerMetrics.IsEnabled().ToString());
        Try("metrics.before", () => DescribeAll(All()));
        Try("metrics.nonAsrm.before", () => AsyncReadManagerMetrics.GetTotalSizeOfNonASRMReadsBytes(false).ToString());
        Try("metrics.summary.before", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Try("metrics.start", () => AsyncReadManagerMetrics.StartCollectingMetrics());
        Try("metrics.enabledAfterStart", () => AsyncReadManagerMetrics.IsEnabled().ToString());
        MetricsRead("metrics.read1", new[] { (64L, 32L) });
        MetricsRead("metrics.read2", new[] { (4090L, 16L) });
        MetricsRead("metrics.readMissing", new[] { (0L, 8L) }, m_Missing);
        MetricsRead("metrics.readRelative", new[] { (0L, 4L) }, m_Relative);
        Try("metrics.relative", () => DescribeAll(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(ProcessingState.Completed), AsyncReadManagerMetrics.Flags.None)));
        Try("metrics.all", () => DescribeAll(All()));
        // The list overloads are called only with null (a managed check): the release's native
        // fill of a list throws for new lists and leaves the Editor's heap corrupted (later
        // captures crashed in its garbage collector), so their outcome is not recorded.
        Try("metrics.listNull", () => AsyncReadManagerMetrics.GetMetrics((List<AsyncReadManagerRequestMetric>)null, none));
        Try("metrics.filterCompleted", () => DescribeAll(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(ProcessingState.Completed), none)));
        Try("metrics.filterFailed", () => DescribeAll(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(ProcessingState.Failed), none)));
        Try("metrics.filterSubsystemOther", () => DescribeAll(AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(AssetLoadingSubsystem.Other), none)));
        Try("metrics.filterSubsystemScripts", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(AssetLoadingSubsystem.Scripts), none).Length.ToString());
        Try("metrics.filterAsync", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(FileReadType.Async), none).Length.ToString());
        Try("metrics.filterSync", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(FileReadType.Sync), none).Length.ToString());
        Try("metrics.filterLow", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(Priority.PriorityLow), none).Length.ToString());
        Try("metrics.filterHigh", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(Priority.PriorityHigh), none).Length.ToString());
        Try("metrics.filterType0", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(0UL), none).Length.ToString());
        Try("metrics.filterType28", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(28UL), none).Length.ToString());
        Try("metrics.filterBoth", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(null, new[] { ProcessingState.Completed, ProcessingState.Failed }, null, null, new[] { AssetLoadingSubsystem.Mesh }), none).Length.ToString());
        Try("metrics.filterStates", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(new[] { ProcessingState.Completed, ProcessingState.Failed }), none).Length.ToString());
        Try("metrics.filterEmptyArray", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(new ProcessingState[0]), none).Length.ToString());
        Try("metrics.filterEmpty", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(), none).Length.ToString());
        Try("metrics.filterNull", () => DescribeAll(AsyncReadManagerMetrics.GetMetrics((AsyncReadManagerMetricsFilters)null, none)));
        Try("metrics.summary", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Try("metrics.summaryFiltered", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(new AsyncReadManagerMetricsFilters(ProcessingState.Completed), none)));
        Try("metrics.summaryFilterNull", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics((AsyncReadManagerMetricsFilters)null, none)));
        Try("metrics.summaryOfEmpty", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(new AsyncReadManagerRequestMetric[0])));
        Try("metrics.summaryOfNullArray", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics((AsyncReadManagerRequestMetric[])null)));
        Try("metrics.summaryOfNullList", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics((List<AsyncReadManagerRequestMetric>)null)));
        Try("metrics.summaryOfDefault", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(new AsyncReadManagerRequestMetric[] { default })));
        Try("metrics.summaryOfAll", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(All())));
        Try("metrics.summaryOfFiltered", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(All(), new AsyncReadManagerMetricsFilters(ProcessingState.Failed))));
        Try("metrics.summaryOfFilterNull", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(All(), null)));
        Try("metrics.summaryClearOnRead", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(AsyncReadManagerMetrics.Flags.ClearOnRead)));
        Try("metrics.afterSummaryClear", () => DescribeAll(All()));

        // Several commands, an asset name, type and subsystem, none, size zero, a directory.
        MetricsRead("metrics.multi", new[] { (0L, 8L), (5000L, 4L), (100L, 4L) }, null, "Probe/Asset", 28, AssetLoadingSubsystem.Texture);
        Try("metrics.multi.all", () => DescribeAll(Take()));
        MetricsRead("metrics.noCommands", new (long, long)[0]);
        Try("metrics.noCommands.all", () => DescribeAll(Take()));
        MetricsRead("metrics.zeroSize", new[] { (10L, 0L) });
        Try("metrics.zeroSize.all", () => DescribeAll(Take()));
        MetricsRead("metrics.directory", new[] { (0L, 8L) }, Path.GetFullPath(m_Directory));
        Try("metrics.directory.all", () => DescribeAll(Take()));
        MetricsRead("metrics.nullAsset", new[] { (0L, 8L) }, null, null, 0, AssetLoadingSubsystem.Mesh);
        MetricsRead("metrics.namedMesh", new[] { (0L, 8L) }, null, "M", 43, AssetLoadingSubsystem.Mesh);
        Try("metrics.subsystems.all", () => DescribeAll(Take()));

        // Through a file handle, deferred, pending and canceled.
        var file = AsyncReadManager.OpenFileAsync(m_Absolute);
        file.JobHandle.Complete();
        Try("metrics.open.all", () => DescribeAll(All()));
        var buffer = Buffer(16);
        var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand) * 2, 16, Allocator.Persistent);
        commands[0] = Command(0, 8, buffer);
        commands[1] = Command(8, 8, buffer + 8);
        var handleRead = AsyncReadManager.Read(file, new ReadCommandArray { ReadCommands = commands, CommandCount = 2 });
        handleRead.JobHandle.Complete();
        Record("metrics.handle", handleRead.Status.ToString());
        handleRead.Dispose();
        Try("metrics.handle.all", () => DescribeAll(Take()));

        var array = (ReadCommandArray*)UnsafeUtility.Malloc(sizeof(ReadCommandArray), 16, Allocator.Persistent);
        *array = new ReadCommandArray { ReadCommands = commands, CommandCount = 1 };
        Volatile.Write(ref s_Release, 0);
        var hold = new HoldJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var pending = AsyncReadManager.ReadDeferred(file, array, hold);
        Try("metrics.pending.all", () => DescribeAll(All()));
        Try("metrics.pending.summary", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Try("metrics.pending.clear", () => AsyncReadManagerMetrics.ClearCompletedMetrics());
        Try("metrics.pending.afterClear", () => DescribeAll(All()));
        Volatile.Write(ref s_Release, 1);
        pending.JobHandle.Complete();
        Record("metrics.pending", pending.Status.ToString());
        pending.Dispose();
        Try("metrics.pending.done", () => DescribeAll(Take()));

        Volatile.Write(ref s_Release, 0);
        hold = new HoldJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var canceled = AsyncReadManager.ReadDeferred(file, array, hold);
        canceled.Cancel();
        Volatile.Write(ref s_Release, 1);
        var readJob = canceled.JobHandle;
        file.Close(readJob).Complete();
        hold.Complete();
        Record("metrics.canceled", canceled.Status.ToString());
        canceled.Dispose();
        Try("metrics.canceled.all", () => DescribeAll(All()));
        Try("metrics.canceled.summary", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Take();
        UnsafeUtility.Free(array, Allocator.Persistent);
        UnsafeUtility.Free(commands, Allocator.Persistent);
        UnsafeUtility.Free(buffer, Allocator.Persistent);

        var info = (FileInfoResult*)UnsafeUtility.Malloc(sizeof(FileInfoResult), 16, Allocator.Persistent);
        var infoRead = AsyncReadManager.GetFileInfo(m_Absolute, info);
        infoRead.JobHandle.Complete();
        infoRead.Dispose();
        UnsafeUtility.Free(info, Allocator.Persistent);
        Try("metrics.fileInfo.all", () => DescribeAll(Take()));
        Try("metrics.nonAsrm", () => AsyncReadManagerMetrics.GetTotalSizeOfNonASRMReadsBytes(true).ToString());

        // Clearing, stopping, restarting.
        MetricsRead("metrics.read3", new[] { (0L, 8L) });
        Try("metrics.clear", () => AsyncReadManagerMetrics.ClearCompletedMetrics());
        Try("metrics.afterClear", () => DescribeAll(All()));
        MetricsRead("metrics.read4", new[] { (0L, 4L) });
        Try("metrics.startAgain", () => AsyncReadManagerMetrics.StartCollectingMetrics());
        Try("metrics.afterStartAgain", () => All().Length.ToString());
        Try("metrics.stop", () => AsyncReadManagerMetrics.StopCollectingMetrics());
        Try("metrics.afterStopKept", () => All().Length.ToString());
        MetricsRead("metrics.read5", new[] { (0L, 8L) });
        Try("metrics.afterStop", () => All().Length.ToString());
        Try("metrics.enabledAfterStop", () => AsyncReadManagerMetrics.IsEnabled().ToString());
        Try("metrics.stopTwice", () => AsyncReadManagerMetrics.StopCollectingMetrics());
        Try("metrics.restart", () => AsyncReadManagerMetrics.StartCollectingMetrics());
        Try("metrics.afterRestart", () => All().Length.ToString());
        AsyncReadManagerMetrics.StopCollectingMetrics();
        Take();

        var f = new AsyncReadManagerMetricsFilters(ProcessingState.Failed);
        Try("metrics.filterSetNull", () => f.SetStateFilter((ProcessingState[])null));

        // Lists, summaries of mixed states and of typed reads.
        AsyncReadManagerMetrics.StartCollectingMetrics();
        MetricsRead("metrics.l1", new[] { (0L, 8L) });
        MetricsRead("metrics.l2", new[] { (8L, 4L) }, null, "T", 28, AssetLoadingSubsystem.Texture);
        MetricsRead("metrics.l3", new[] { (0L, 2L) }, m_Missing);
        Try("metrics.filterSetNullUse", () => AsyncReadManagerMetrics.GetMetrics(f, none).Length.ToString());
        var three = All();
        Try("metrics.sumTyped", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(three, new AsyncReadManagerMetricsFilters(28UL))));
        Try("metrics.sumTexture", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(three, new AsyncReadManagerMetricsFilters(AssetLoadingSubsystem.Texture))));

        MetricsMixedStates(none);
        AsyncReadManagerMetrics.StopCollectingMetrics();
        Take();
    }

    /// <summary>Summaries of a request still queued beside finished ones, then of a finished and a
    /// canceled one.</summary>
    void MetricsMixedStates(AsyncReadManagerMetrics.Flags none)
    {
        var file = AsyncReadManager.OpenFileAsync(m_Absolute);
        var buffer = Buffer(8);
        var command = Command(0, 8, buffer);
        var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
        Volatile.Write(ref s_Release, 0);
        var hold = new HoldJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var pending = AsyncReadManager.ReadDeferred(file, &array, hold);
        Try("metrics.mixedPending", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Try("metrics.mixedPendingAll", () => All().Length.ToString());
        Volatile.Write(ref s_Release, 1);
        pending.JobHandle.Complete();
        pending.Dispose();
        Volatile.Write(ref s_Release, 0);
        hold = new HoldJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var canceled = AsyncReadManager.ReadDeferred(file, &array, hold);
        canceled.Cancel();
        Volatile.Write(ref s_Release, 1);
        file.Close(canceled.JobHandle).Complete();
        hold.Complete();
        canceled.Dispose();
        UnsafeUtility.Free(buffer, Allocator.Persistent);
        var five = All();
        Try("metrics.mixedStates", () => { var b = new StringBuilder(); foreach (var m in five) b.Append(m.State).Append(' '); return b.ToString(); });
        Try("metrics.mixedSummary", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(none)));
        Try("metrics.mixedSummaryOf", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(five)));
        Try("metrics.canceledFilter", () => DescribeSummary(AsyncReadManagerMetrics.GetSummaryOfMetrics(five, new AsyncReadManagerMetricsFilters(ProcessingState.Canceled))));
        Try("metrics.canceledFilterGet", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(ProcessingState.Canceled), none).Length.ToString());
        // ClearCompletedMetrics clears failed and canceled requests too (the Scripting API).
        Try("metrics.clearWithCanceled", () => { AsyncReadManagerMetrics.ClearCompletedMetrics(); return DescribeAll(All()); });
        // ClearOnRead with a filter: which finished records go.
        MetricsRead("metrics.f1", new[] { (0L, 8L) });
        MetricsRead("metrics.f2", new[] { (8L, 8L) }, null, "M", 43, AssetLoadingSubsystem.Mesh);
        Try("metrics.filteredClearOnRead", () => AsyncReadManagerMetrics.GetMetrics(new AsyncReadManagerMetricsFilters(AssetLoadingSubsystem.Mesh), AsyncReadManagerMetrics.Flags.ClearOnRead).Length.ToString());
        Try("metrics.afterFilteredClearOnRead", () => DescribeAll(All()));
        MetricsRead("metrics.f3", new[] { (16L, 8L) }, null, "M", 43, AssetLoadingSubsystem.Mesh);
        Try("metrics.filteredSummaryClearOnRead", () => DescribeSummary(AsyncReadManagerMetrics.GetCurrentSummaryMetrics(new AsyncReadManagerMetricsFilters(AssetLoadingSubsystem.Mesh), AsyncReadManagerMetrics.Flags.ClearOnRead)));
        Try("metrics.afterFilteredSummaryClearOnRead", () => DescribeAll(All()));
    }


    static void Defaults()
    {
        ReadHandle read = default;
        Record("default.read.valid", read.IsValid());
        Try("default.read.status", () => read.Status.ToString());
        Try("default.read.readCount", () => read.ReadCount.ToString());
        Try("default.read.jobHandle", () => read.JobHandle.IsCompleted.ToString());
        Try("default.read.cancel", () => read.Cancel());
        Try("default.read.dispose", () => read.Dispose());
        Try("default.read.bytesRead", () => read.GetBytesRead().ToString());
        Try("default.read.bytesReadCommand", () => read.GetBytesRead(0).ToString());
        Try("default.read.bytesReadArray", () => ((IntPtr)read.GetBytesReadArray()).ToString());
        FileHandle file = default;
        Record("default.file.valid", file.IsValid());
        Try("default.file.status", () => file.Status.ToString());
        Try("default.file.jobHandle", () => file.JobHandle.IsCompleted.ToString());
        Try("default.file.close", () => file.Close().Complete());
        var array = new ReadCommandArray();
        var arrayPointer = (ReadCommandArray*)UnsafeUtility.Malloc(sizeof(ReadCommandArray), 16, Allocator.Persistent);
        *arrayPointer = array;
        Try("default.file.read", () => AsyncReadManager.Read(file, array).Dispose());
        Try("default.file.readDeferred", () => AsyncReadManager.ReadDeferred(file, arrayPointer, default).Dispose());
        UnsafeUtility.Free(arrayPointer, Allocator.Persistent);
        var command = new ReadCommand();
        Record("default.command", command.Offset + "/" + command.Size + "/" + (command.Buffer == null));
        Record("default.commandArray", array.CommandCount + "/" + (array.ReadCommands == null));
        Record("default.fileInfo", new FileInfoResult().FileSize + "/" + new FileInfoResult().FileState);
    }

    void FileInfo()
    {
        Try("info.nullResult", () => AsyncReadManager.GetFileInfo(m_Absolute, null).Dispose());
        Info("info.absolute", m_Absolute);
        Info("info.relative", m_Relative);
        Info("info.missing", m_Missing);
        Info("info.directory", Path.GetFullPath(m_Directory));
        Info("info.empty", "");
        Info("info.null", null);
    }

    static void Info(string key, string path)
    {
        try
        {
            var result = new FileInfoResult { FileSize = -7, FileState = (FileState)9 };
            var handle = AsyncReadManager.GetFileInfo(path, &result);
            Record(key + ".valid", handle.IsValid());
            if (!handle.IsValid()) return;
            handle.JobHandle.Complete();
            Record(key + ".status", handle.Status.ToString());
            Record(key + ".readCount", handle.ReadCount);
            Record(key + ".bytesRead", handle.GetBytesRead());
            Record(key + ".size", result.FileSize);
            Record(key + ".state", result.FileState.ToString());
            handle.Dispose();
        }
        catch (Exception e) { Record(key + ".error", e.GetType().Name + ": " + e.Message); }
    }

    void ReadByName()
    {
        ByName("name.two", m_Absolute, new[] { (0L, 16L), (100L, 8L) });
        ByName("name.relative", m_Relative, new[] { (32L, 8L) });
        ByName("name.pastEnd", m_Absolute, new[] { (4090L, 16L) });
        ByName("name.beyondEnd", m_Absolute, new[] { (5000L, 8L) });
        ByName("name.endExactly", m_Absolute, new[] { (4096L, 4L) });
        ByName("name.mixed", m_Absolute, new[] { (8L, 4L), (4094L, 4L), (16L, 4L) });
        ByName("name.zeroSize", m_Absolute, new[] { (10L, 0L), (12L, 2L) });
        ByName("name.missing", m_Missing, new[] { (0L, 8L) });
        ByName("name.missingTwo", m_Missing, new[] { (0L, 8L), (8L, 8L) });
        ByName("name.truncatedThenFailed", m_Absolute, new[] { (4094L, 4L), (5000L, 4L) });
        ByName("name.failedThenTruncated", m_Absolute, new[] { (5000L, 4L), (4094L, 4L) });
        ByName("name.directory", Path.GetFullPath(m_Directory), new[] { (0L, 8L) });
        ByName("name.noCommands", m_Absolute, new (long, long)[0]);
        ByName("name.empty", "", new[] { (0L, 8L) });
        ByName("name.null", null, new[] { (0L, 8L) });

        // Read copies the commands: changing them after the call does not change the read.
        var buffer = Buffer(8);
        var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand), 16, Allocator.Persistent);
        commands[0] = Command(200, 8, buffer);
        var handle = AsyncReadManager.Read(m_Absolute, commands, 1);
        commands[0] = Command(300, 4, buffer);
        UnsafeUtility.Free(commands, Allocator.Persistent);
        handle.JobHandle.Complete();
        Record("name.copied.status", handle.Status.ToString());
        Record("name.copied.data", Hex(buffer, 8));
        handle.Dispose();
        UnsafeUtility.Free(buffer, Allocator.Persistent);
    }

    static void ByName(string key, string path, (long Offset, long Size)[] reads)
    {
        int total = 0;
        foreach (var r in reads) total += (int)Math.Max(r.Size, 0);
        var buffer = Buffer(Math.Max(total, 1));
        var commands = new NativeArray<ReadCommand>(Math.Max(reads.Length, 1), Allocator.Persistent);
        int at = 0;
        for (int i = 0; i < reads.Length; i++)
        {
            commands[i] = Command(reads[i].Offset, reads[i].Size, buffer + at);
            at += (int)reads[i].Size;
        }
        try
        {
            var handle = AsyncReadManager.Read(path, (ReadCommand*)commands.GetUnsafePtr(), (uint)reads.Length);
            Outcome(key, handle, (uint)reads.Length);
            Record(key + ".data", Hex(buffer, total));
        }
        catch (Exception e) { Record(key + ".error", e.GetType().Name + ": " + e.Message); }
        commands.Dispose();
        UnsafeUtility.Free(buffer, Allocator.Persistent);
    }

    void FileHandles()
    {
        Try("open.emptyName", () => AsyncReadManager.OpenFileAsync("").IsValid().ToString());
        Try("open.nullName", () => AsyncReadManager.OpenFileAsync(null).IsValid().ToString());

        var file = AsyncReadManager.OpenFileAsync(m_Absolute);
        Record("open.valid", file.IsValid());
        file.JobHandle.Complete();
        Record("open.status", file.Status.ToString());
        Record("open.jobCompleted", file.JobHandle.IsCompleted);
        Handle("open.read", file, new[] { (0L, 4L), (4094L, 4L) });
        Handle("open.readAgain", file, new[] { (1024L, 8L) });
        var close = file.Close();
        Record("open.validAfterClose", file.IsValid());
        close.Complete();
        Record("open.validAfterCloseComplete", file.IsValid());
        Try("open.statusAfterClose", () => file.Status.ToString());
        Try("open.jobHandleAfterClose", () => file.JobHandle.IsCompleted.ToString());
        Try("open.closeTwice", () => file.Close().Complete());
        Try("open.readAfterClose", () => AsyncReadManager.Read(file, new ReadCommandArray()).Dispose());

        // A read queued before the open completes waits for it; Close may depend on the read.
        var early = AsyncReadManager.OpenFileAsync(m_Relative);
        var buffer = Buffer(8);
        var command = Command(512, 8, buffer);
        var read = AsyncReadManager.Read(early, new ReadCommandArray { ReadCommands = &command, CommandCount = 1 });
        var closeAfter = early.Close(read.JobHandle);
        closeAfter.Complete();
        Record("open.early.status", read.Status.ToString());
        Record("open.early.data", Hex(buffer, 8));
        Record("open.early.validAfterClose", early.IsValid());
        read.Dispose();
        UnsafeUtility.Free(buffer, Allocator.Persistent);

        var missing = AsyncReadManager.OpenFileAsync(m_Missing);
        Record("open.missing.valid", missing.IsValid());
        missing.JobHandle.Complete();
        Record("open.missing.status", missing.Status.ToString());
        Handle("open.missing.read", missing, new[] { (0L, 4L) });
        Try("open.missing.close", () => missing.Close().Complete());
        Record("open.missing.validAfterClose", missing.IsValid());

        var directory = AsyncReadManager.OpenFileAsync(Path.GetFullPath(m_Directory));
        Record("open.directory.valid", directory.IsValid());
        directory.JobHandle.Complete();
        Record("open.directory.status", directory.Status.ToString());
        Handle("open.directory.read", directory, new[] { (0L, 4L) });
        Try("open.directory.close", () => directory.Close().Complete());

        // A read queued before a Close without a dependency.
        var pending = AsyncReadManager.OpenFileAsync(m_Absolute);
        var pendingBuffer = Buffer(16);
        var pendingCommands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand) * 2, 16, Allocator.Persistent);
        pendingCommands[0] = Command(0, 8, pendingBuffer);
        pendingCommands[1] = Command(2048, 8, pendingBuffer + 8);
        var pendingRead = AsyncReadManager.Read(pending, new ReadCommandArray { ReadCommands = pendingCommands, CommandCount = 2 });
        pending.Close().Complete();
        pendingRead.JobHandle.Complete();
        Record("open.closeWithoutDependency.status", pendingRead.Status.ToString());
        Record("open.closeWithoutDependency.data", Hex(pendingBuffer, 16));
        pendingRead.Dispose();
        UnsafeUtility.Free(pendingCommands, Allocator.Persistent);
        UnsafeUtility.Free(pendingBuffer, Allocator.Persistent);

        Try("open.closeCached", () => { AsyncReadManager.CloseCachedFileAsync(m_Absolute).Complete(); return "ok"; });
        Try("open.closeCachedUnknown", () => { AsyncReadManager.CloseCachedFileAsync(m_Missing).Complete(); return "ok"; });
    }

    static void Handle(string key, FileHandle file, (long Offset, long Size)[] reads)
    {
        int total = 0;
        foreach (var r in reads) total += (int)r.Size;
        var buffer = Buffer(total);
        var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand) * reads.Length, 16, Allocator.Persistent);
        int at = 0;
        for (int i = 0; i < reads.Length; i++)
        {
            commands[i] = Command(reads[i].Offset, reads[i].Size, buffer + at);
            at += (int)reads[i].Size;
        }
        try
        {
            var handle = AsyncReadManager.Read(file, new ReadCommandArray { ReadCommands = commands, CommandCount = reads.Length });
            Outcome(key, handle, (uint)reads.Length);
            Record(key + ".data", Hex(buffer, total));
        }
        catch (Exception e) { Record(key + ".error", e.GetType().Name + ": " + e.Message); }
        UnsafeUtility.Free(commands, Allocator.Persistent);
        UnsafeUtility.Free(buffer, Allocator.Persistent);
    }

    void Deferred()
    {
        // The commands are written by the dependency after the call: the read uses them.
        var file = AsyncReadManager.OpenFileAsync(m_Absolute);
        var array = (ReadCommandArray*)UnsafeUtility.Malloc(sizeof(ReadCommandArray), 16, Allocator.Persistent);
        var commands = (ReadCommand*)UnsafeUtility.Malloc(sizeof(ReadCommand) * 2, 16, Allocator.Persistent);
        var buffer = Buffer(12);
        var placeholder = Command(0, 12, buffer);
        *array = new ReadCommandArray { ReadCommands = &placeholder, CommandCount = 1 };
        Volatile.Write(ref s_Release, 0);
        var hold = new HoldJob().Schedule();
        var write = new WriteCommandsJob { Array = array, Commands = commands, Buffer = buffer }.Schedule(hold);
        JobHandle.ScheduleBatchedJobs();
        var read = AsyncReadManager.ReadDeferred(file, array, write);
        Record("deferred.validBeforeDependency", read.IsValid());
        Record("deferred.statusBeforeDependency", read.Status.ToString());
        Volatile.Write(ref s_Release, 1);
        var close = file.Close(read.JobHandle);
        close.Complete();
        Outcome("deferred", read, 2);
        Record("deferred.data", Hex(buffer, 12));
        write.Complete();
        UnsafeUtility.Free(buffer, Allocator.Persistent);
        UnsafeUtility.Free(commands, Allocator.Persistent);
        UnsafeUtility.Free(array, Allocator.Persistent);
    }

    void Cancel()
    {
        // Canceled while its dependency still holds it: the read never runs.
        var file = AsyncReadManager.OpenFileAsync(m_Absolute);
        var buffer = Buffer(8);
        var command = Command(0, 8, buffer);
        var array = new ReadCommandArray { ReadCommands = &command, CommandCount = 1 };
        Volatile.Write(ref s_Release, 0);
        var hold = new HoldJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var read = AsyncReadManager.ReadDeferred(file, &array, hold);
        var readJob = read.JobHandle;
        read.Cancel();
        Try("cancel.cancelTwice", () => read.Cancel());
        Volatile.Write(ref s_Release, 1);
        file.Close(readJob).Complete();
        hold.Complete();
        Try("cancel.status", () => read.Status.ToString());
        Try("cancel.bytesRead", () => read.GetBytesRead().ToString());
        Try("cancel.readCount", () => read.ReadCount.ToString());
        Record("cancel.data", Hex(buffer, 8));
        Try("cancel.dispose", () => read.Dispose());
        UnsafeUtility.Free(buffer, Allocator.Persistent);
    }

    // ------------------------------------------------------------------ FrameTimingManager

    static void FrameTimingStatic()
    {
        Record("timing.enabled", FrameTimingManager.IsFeatureEnabled());
        Record("timing.cpuFrequency", (long)FrameTimingManager.GetCpuTimerFrequency());
        Record("timing.gpuFrequency", (long)FrameTimingManager.GetGpuTimerFrequency());
        var timings = new FrameTiming[4];
        // How many frames exist before the first Update depends on the frames the Editor ran
        // before Play mode; only whether a request is capped by them is recorded.
        Record("timing.beforeCaptureAtMostRequested", FrameTimingManager.GetLatestTimings(4, timings) <= 4);
        Record("timing.vSyncCount", QualitySettings.vSyncCount);
        Try("timing.zeroFrames", () => FrameTimingManager.GetLatestTimings(0, timings).ToString());
        Record("timing.default", Describe(new FrameTiming()));
    }

    /// <summary>What a frame's timing shows that does not depend on how long the frame took:
    /// which durations are positive, whether the timestamps are ordered, scales and interval.</summary>
    static string Describe(FrameTiming t)
    {
        string Sign(double v) => v > 0 ? "+" : v == 0 ? "0" : "-";
        return "cpu" + Sign(t.cpuFrameTime) + " main" + Sign(t.cpuMainThreadFrameTime) +
               " wait" + Sign(t.cpuMainThreadPresentWaitTime) + " render" + Sign(t.cpuRenderThreadFrameTime) +
               " gpu" + Sign(t.gpuFrameTime) +
               " start" + Sign(t.frameStartTimestamp) + " submit" + Sign(t.firstSubmitTimestamp) +
               " present" + Sign(t.cpuTimePresentCalled) + " complete" + Sign(t.cpuTimeFrameComplete) +
               " ordered:" + (t.frameStartTimestamp <= t.cpuTimePresentCalled && t.cpuTimePresentCalled <= t.cpuTimeFrameComplete) +
               " scale:" + t.widthScale + "x" + t.heightScale +
               " sync:" + (t.syncInterval != 0 && t.syncInterval == RefreshRate() ? "refresh" : t.syncInterval.ToString());
    }

    static uint RefreshRate() => (uint)Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);

    int m_Frame;
    readonly FrameTiming[] m_Timings = new FrameTiming[8];
    readonly FrameTiming[] m_Many = new FrameTiming[256];
    const int LongFrameMs = 250;
    int m_LongFrameNewestAt = -1;
    uint m_Previous = uint.MaxValue;
    bool m_GrowsByOne = true;
    bool m_CaptureNoEffect = true;

    void Update()
    {
        if (Finished) return;
        m_Frame++;
        // Frames 1-12 capture; the counts and shapes after twelve captures are recorded.
        if (m_Frame <= 12)
        {
            uint before = FrameTimingManager.GetLatestTimings(256, m_Many);
            FrameTimingManager.CaptureFrameTimings();
            uint after = FrameTimingManager.GetLatestTimings(256, m_Many);
            if (before != after) m_CaptureNoEffect = false;
            // Each frame adds at most one and none is dropped below the capacity (how many exist at
            // first depends on the frames before Play mode, and the first frames are not
            // reported yet).
            if (m_Previous != uint.MaxValue && (after < m_Previous || after > m_Previous + 1)) m_GrowsByOne = false;
            m_Previous = after;
            return;
        }
        if (m_Frame == 13)
        {
            Record("timing.captureNoEffect", m_CaptureNoEffect);
            Record("timing.growsAtMostOnePerFrame", m_GrowsByOne);
            uint count = FrameTimingManager.GetLatestTimings(8, m_Timings);
            Record("timing.countAfter12", count);
            uint few = FrameTimingManager.GetLatestTimings(2, m_Timings);
            Record("timing.countOf2", few);
            if (count > 0) Record("timing.latest", Describe(m_Timings[0]));
            if (count > 0) Record("timing.syncIsRefreshRate", m_Timings[0].syncInterval == RefreshRate());
            if (count > 1) Record("timing.newestFirst", m_Timings[0].frameStartTimestamp > m_Timings[1].frameStartTimestamp);
            var cpuSeconds = (m_Timings[0].cpuTimeFrameComplete - m_Timings[0].frameStartTimestamp) / (double)FrameTimingManager.GetCpuTimerFrequency();
            Record("timing.latestSpanUnderOneSecond", count > 0 && cpuSeconds >= 0 && cpuSeconds < 1);
            // syncInterval under other vSyncCounts: each runs 10 frames, past the frames' delay.
            QualitySettings.vSyncCount = 0;
            return;
        }
        if (m_Frame == 23 || m_Frame == 33)
        {
            uint count = FrameTimingManager.GetLatestTimings(1, m_Timings);
            int vSync = QualitySettings.vSyncCount;
            Record("timing.vSync" + vSync + ".syncZero", count > 0 && m_Timings[0].syncInterval == 0);
            Record("timing.vSync" + vSync + ".syncIsRefreshRate", count > 0 && m_Timings[0].syncInterval == RefreshRate());
            if (count > 0) Record("timing.vSync" + vSync + ".latest", Describe(m_Timings[0]));
            QualitySettings.vSyncCount = vSync == 0 ? 2 : 1;
            if (m_Frame == 23) return;
        }
        if (m_Frame == 33) return;
        // The frames' delay: frame 35 lasts at least LongFrameMs; the frame on which it first
        // reads as the newest timing, counted from it, and where it sits ten frames later.
        if (m_Frame == 35)
        {
            System.Threading.Thread.Sleep(LongFrameMs);
            return;
        }
        if (m_Frame > 35 && m_Frame <= 45)
        {
            if (m_LongFrameNewestAt < 0 && FrameTimingManager.GetLatestTimings(1, m_Timings) > 0 && m_Timings[0].cpuFrameTime >= LongFrameMs)
                m_LongFrameNewestAt = m_Frame - 35;
            if (m_Frame < 45) return;
            uint kept = FrameTimingManager.GetLatestTimings(16, m_Many);
            int index = -1;
            for (int i = 0; i < kept; i++)
                if (m_Many[i].cpuFrameTime >= LongFrameMs) { index = i; break; }
            // After 45 frames the frames kept are at their capacity.
            Record("timing.capacity", FrameTimingManager.GetLatestTimings(256, m_Many));
            Record("timing.longFrameNewestAfter", m_LongFrameNewestAt);
            Record("timing.longFrameIndexAfter10", index);
        }
        if (m_Frame < 45) return;
        Logs("frames");
        Application.logMessageReceivedThreaded -= OnLog;
        Record("complete", true);
        var path = Environment.GetEnvironmentVariable(OutVariable);
        File.WriteAllText(path, s_Output.ToString());
        Finished = true;
    }
}
