using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Unity.IO.LowLevel.Unsafe
{
    public enum FileReadType
    {
        Sync = 0,
        Async = 1,
    }

    public enum Priority
    {
        PriorityLow = 0,
        PriorityHigh = 1,
    }

    public enum ProcessingState
    {
        Unknown = 0,
        InQueue = 1,
        Reading = 2,
        Completed = 3,
        Failed = 4,
        Canceled = 5,
    }

    /// <summary>One read command of a request recorded while metrics were collected.</summary>
    public partial struct AsyncReadManagerRequestMetric
    {
        internal string m_AssetName;
        internal string m_FileName;
        internal ulong m_OffsetBytes;
        internal ulong m_SizeBytes;
        internal ulong m_AssetTypeId;
        internal ulong m_CurrentBytesRead;
        internal uint m_BatchReadCount;
        internal bool m_IsBatchRead;
        internal ProcessingState m_State;
        internal FileReadType m_ReadType;
        internal Priority m_PriorityLevel;
        internal AssetLoadingSubsystem m_Subsystem;
        internal double m_RequestTimeMicroseconds;
        internal double m_TimeInQueueMicroseconds;
        internal double m_TotalTimeMicroseconds;

        public readonly string AssetName => m_AssetName;
        public readonly string FileName => m_FileName;
        public readonly ulong OffsetBytes => m_OffsetBytes;
        public readonly ulong SizeBytes => m_SizeBytes;
        public readonly ulong AssetTypeId => m_AssetTypeId;
        public readonly ulong CurrentBytesRead => m_CurrentBytesRead;
        public readonly uint BatchReadCount => m_BatchReadCount;
        public readonly bool IsBatchRead => m_IsBatchRead;
        public readonly ProcessingState State => m_State;
        public readonly FileReadType ReadType => m_ReadType;
        public readonly Priority PriorityLevel => m_PriorityLevel;
        public readonly AssetLoadingSubsystem Subsystem => m_Subsystem;
        public readonly double RequestTimeMicroseconds => m_RequestTimeMicroseconds;
        public readonly double TimeInQueueMicroseconds => m_TimeInQueueMicroseconds;
        public readonly double TotalTimeMicroseconds => m_TotalTimeMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial class AsyncReadManagerSummaryMetrics
    {
        internal ulong m_TotalBytesRead;
        internal float m_AverageBandwidthMBPerSecond;
        internal float m_AverageReadSizeInBytes;
        internal float m_AverageWaitTimeMicroseconds;
        internal float m_AverageReadTimeMicroseconds;
        internal float m_AverageTotalRequestTimeMicroseconds;
        internal float m_AverageThroughputMBPerSecond;
        internal float m_LongestWaitTimeMicroseconds;
        internal float m_LongestReadTimeMicroseconds;
        internal int m_NumberOfInProgressRequests;
        internal int m_NumberOfCompletedRequests;
        internal int m_NumberOfFailedRequests;
        internal int m_NumberOfWaitingRequests;

        public AsyncReadManagerSummaryMetrics()
        {
        }

        public ulong TotalBytesRead => m_TotalBytesRead;
        public float AverageBandwidthMBPerSecond => m_AverageBandwidthMBPerSecond;
        public float AverageReadSizeInBytes => m_AverageReadSizeInBytes;
        public float AverageWaitTimeMicroseconds => m_AverageWaitTimeMicroseconds;
        public float AverageReadTimeMicroseconds => m_AverageReadTimeMicroseconds;
        public float AverageTotalRequestTimeMicroseconds => m_AverageTotalRequestTimeMicroseconds;
        public float AverageThroughputMBPerSecond => m_AverageThroughputMBPerSecond;
        public float LongestWaitTimeMicroseconds => m_LongestWaitTimeMicroseconds;
        public float LongestReadTimeMicroseconds => m_LongestReadTimeMicroseconds;

        // Unity 6000.0.71f1 (tests/projects/asyncread metrics.*): the longest read's and wait's
        // asset type and subsystem are 0 and Other, and the cached, synchronous, asynchronous and
        // canceled counts 0, whatever the summarized requests are.
        const string Measured = "the 6000.0.71f1 Editor answers this for every summary (tests/projects/asyncread metrics.*)";
        [Unbound.EditorFaithful(Measured)]
        public ulong LongestReadAssetType => 0;
        [Unbound.EditorFaithful(Measured)]
        public ulong LongestWaitAssetType => 0;
        [Unbound.EditorFaithful(Measured)]
        public AssetLoadingSubsystem LongestReadSubsystem => AssetLoadingSubsystem.Other;
        [Unbound.EditorFaithful(Measured)]
        public AssetLoadingSubsystem LongestWaitSubsystem => AssetLoadingSubsystem.Other;
        public int NumberOfInProgressRequests => m_NumberOfInProgressRequests;
        public int NumberOfCompletedRequests => m_NumberOfCompletedRequests;
        public int NumberOfFailedRequests => m_NumberOfFailedRequests;
        public int NumberOfWaitingRequests => m_NumberOfWaitingRequests;
        [Unbound.EditorFaithful(Measured)]
        public int NumberOfCanceledRequests => 0;
        public int TotalNumberOfRequests => m_NumberOfInProgressRequests + m_NumberOfCompletedRequests + m_NumberOfFailedRequests + m_NumberOfWaitingRequests;
        [Unbound.EditorFaithful(Measured)]
        public int NumberOfCachedReads => 0;
        [Unbound.EditorFaithful(Measured)]
        public int NumberOfAsyncReads => 0;
        [Unbound.EditorFaithful(Measured)]
        public int NumberOfSyncReads => 0;
    }

    /// <summary>The categories a metrics query or summary keeps: each set category must hold the
    /// request's value; a null or empty one keeps every value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial class AsyncReadManagerMetricsFilters
    {
        internal ulong[] TypeIDs;
        internal ProcessingState[] States;
        internal FileReadType[] ReadTypes;
        internal Priority[] PriorityLevels;
        internal AssetLoadingSubsystem[] Subsystems;

        public AsyncReadManagerMetricsFilters() => ClearFilters();
        public AsyncReadManagerMetricsFilters(ulong typeID) => SetTypeIDFilter(typeID);
        public AsyncReadManagerMetricsFilters(ProcessingState state) => SetStateFilter(state);
        public AsyncReadManagerMetricsFilters(FileReadType readType) => SetReadTypeFilter(readType);
        public AsyncReadManagerMetricsFilters(Priority priorityLevel) => SetPriorityFilter(priorityLevel);
        public AsyncReadManagerMetricsFilters(AssetLoadingSubsystem subsystem) => SetSubsystemFilter(subsystem);
        public AsyncReadManagerMetricsFilters(ulong[] typeIDs) => SetTypeIDFilter(typeIDs);
        public AsyncReadManagerMetricsFilters(ProcessingState[] states) => SetStateFilter(states);
        public AsyncReadManagerMetricsFilters(FileReadType[] readTypes) => SetReadTypeFilter(readTypes);
        public AsyncReadManagerMetricsFilters(Priority[] priorityLevels) => SetPriorityFilter(priorityLevels);
        public AsyncReadManagerMetricsFilters(AssetLoadingSubsystem[] subsystems) => SetSubsystemFilter(subsystems);

        public AsyncReadManagerMetricsFilters(ulong[] typeIDs, ProcessingState[] states, FileReadType[] readTypes, Priority[] priorityLevels, AssetLoadingSubsystem[] subsystems)
        {
            SetTypeIDFilter(typeIDs);
            SetStateFilter(states);
            SetReadTypeFilter(readTypes);
            SetPriorityFilter(priorityLevels);
            SetSubsystemFilter(subsystems);
        }

        public void SetTypeIDFilter(ulong[] _typeIDs) => TypeIDs = _typeIDs;
        public void SetStateFilter(ProcessingState[] _states) => States = _states;
        public void SetReadTypeFilter(FileReadType[] _readTypes) => ReadTypes = _readTypes;
        public void SetPriorityFilter(Priority[] _priorityLevels) => PriorityLevels = _priorityLevels;
        public void SetSubsystemFilter(AssetLoadingSubsystem[] _subsystems) => Subsystems = _subsystems;
        public void SetTypeIDFilter(ulong _typeID) => TypeIDs = new[] { _typeID };
        public void SetStateFilter(ProcessingState _state) => States = new[] { _state };
        public void SetReadTypeFilter(FileReadType _readType) => ReadTypes = new[] { _readType };
        public void SetPriorityFilter(Priority _priorityLevel) => PriorityLevels = new[] { _priorityLevel };
        public void SetSubsystemFilter(AssetLoadingSubsystem _subsystem) => Subsystems = new[] { _subsystem };
        public void RemoveTypeIDFilter() => TypeIDs = null;
        public void RemoveStateFilter() => States = null;
        public void RemoveReadTypeFilter() => ReadTypes = null;
        public void RemovePriorityFilter() => PriorityLevels = null;
        public void RemoveSubsystemFilter() => Subsystems = null;

        public void ClearFilters()
        {
            RemoveTypeIDFilter();
            RemoveStateFilter();
            RemoveReadTypeFilter();
            RemovePriorityFilter();
            RemoveSubsystemFilter();
        }

        static bool Holds<T>(T[] set, T value) => set == null || set.Length == 0 || Array.IndexOf(set, value) >= 0;

        internal bool Keeps(in AsyncReadManagerRequestMetric metric) =>
            Holds(TypeIDs, metric.m_AssetTypeId) && Holds(States, metric.m_State) && Holds(ReadTypes, metric.m_ReadType)
            && Holds(PriorityLevels, metric.m_PriorityLevel) && Holds(Subsystems, metric.m_Subsystem);
    }

    /// <summary>Records of AsyncReadManager requests, one per read command, kept from
    /// <see cref="StartCollectingMetrics"/> until cleared. As in the Unity 6000.0.71f1 Editor
    /// (tests/projects/asyncread metrics.*): collection is off until started, and
    /// <see cref="IsEnabled"/> answers whether it runs; stopping keeps the records; a read of a
    /// file that does not open spent no time queued; a canceled request is listed but left out of
    /// summaries.</summary>
    public static partial class AsyncReadManagerMetrics
    {
        [Flags]
        public enum Flags
        {
            None = 0,
            ClearOnRead = 1,
        }

        public static bool IsEnabled() => AsyncReadMetrics.Collecting;

        public static void ClearCompletedMetrics() => AsyncReadMetrics.Take(null, true, null);

        public static void StartCollectingMetrics() => AsyncReadMetrics.Collecting = true;

        public static void StopCollectingMetrics() => AsyncReadMetrics.Collecting = false;

        static bool Clear(Flags flags) => (flags & Flags.ClearOnRead) == Flags.ClearOnRead;

        /// <summary>A null filter set is a native null dereference in the release, reported with
        /// this message.</summary>
        static void RequireFilters(AsyncReadManagerMetricsFilters filters)
        {
            if (filters == null) throw new NullReferenceException("(null)");
        }

        public static AsyncReadManagerRequestMetric[] GetMetrics(Flags flags) => AsyncReadMetrics.Snapshot(null, Clear(flags));

        public static AsyncReadManagerRequestMetric[] GetMetrics(AsyncReadManagerMetricsFilters filters, Flags flags)
        {
            RequireFilters(filters);
            return AsyncReadMetrics.Snapshot(filters, Clear(flags));
        }

        /// <summary>Replaces the list's contents with the records, as documented. (The 6000.0.71f1
        /// Editor's own fill throws for new lists and corrupts its heap, so the asyncread probe
        /// records only the null check.)</summary>
        public static void GetMetrics(List<AsyncReadManagerRequestMetric> outMetrics, Flags flags)
        {
            if (outMetrics == null) throw Unbound.Runtime.MonoExceptions.Null("metrics");
            AsyncReadMetrics.Take(null, Clear(flags), outMetrics);
        }

        public static void GetMetrics(List<AsyncReadManagerRequestMetric> outMetrics, AsyncReadManagerMetricsFilters filters, Flags flags)
        {
            if (outMetrics == null) throw Unbound.Runtime.MonoExceptions.Null("metrics");
            RequireFilters(filters);
            AsyncReadMetrics.Take(filters, Clear(flags), outMetrics);
        }

        public static AsyncReadManagerSummaryMetrics GetCurrentSummaryMetrics(Flags flags) =>
            AsyncReadMetrics.Summarize(AsyncReadMetrics.Snapshot(null, Clear(flags)), null);

        public static AsyncReadManagerSummaryMetrics GetCurrentSummaryMetrics(AsyncReadManagerMetricsFilters metricsFilters, Flags flags)
        {
            RequireFilters(metricsFilters);
            return AsyncReadMetrics.Summarize(AsyncReadMetrics.Snapshot(metricsFilters, Clear(flags)), null);
        }

        public static AsyncReadManagerSummaryMetrics GetSummaryOfMetrics(AsyncReadManagerRequestMetric[] metrics) =>
            AsyncReadMetrics.Summarize(metrics, null);

        public static AsyncReadManagerSummaryMetrics GetSummaryOfMetrics(AsyncReadManagerRequestMetric[] metrics, AsyncReadManagerMetricsFilters metricsFilters)
        {
            RequireFilters(metricsFilters);
            return AsyncReadMetrics.Summarize(metrics, metricsFilters);
        }

        /// <summary>The list's records summarized as an array's, as documented. (The 6000.0.71f1
        /// Editor's list summaries did not count the list's records; not compared.)</summary>
        public static AsyncReadManagerSummaryMetrics GetSummaryOfMetrics(List<AsyncReadManagerRequestMetric> metrics) =>
            AsyncReadMetrics.Summarize(metrics, null);

        public static AsyncReadManagerSummaryMetrics GetSummaryOfMetrics(List<AsyncReadManagerRequestMetric> metrics, AsyncReadManagerMetricsFilters metricsFilters)
        {
            RequireFilters(metricsFilters);
            return AsyncReadMetrics.Summarize(metrics, metricsFilters);
        }

        /// <summary>Unbound reads its assets through no other system that Unity counts here; the
        /// Editor answers 0 for game code's reads as well.</summary>
        [Unbound.EditorFaithful("the 6000.0.71f1 Editor answers 0 around game code's reads (tests/projects/asyncread metrics.nonAsrm)")]
        public static ulong GetTotalSizeOfNonASRMReadsBytes(bool emptyAfterRead) => 0;
    }

    /// <summary>The record store behind <see cref="AsyncReadManagerMetrics"/>: a request made
    /// while collecting gets a <see cref="Recording"/>, with one record per command, updated as the
    /// request runs.</summary>
    internal static class AsyncReadMetrics
    {
        static readonly object s_Lock = new object();
        static readonly List<Record> s_Records = new List<Record>();
        static volatile bool s_Collecting;

        internal static bool Collecting
        {
            get => s_Collecting;
            set => s_Collecting = value;
        }

        static double Now() => Stopwatch.GetTimestamp() * (1e6 / Stopwatch.Frequency);

        internal sealed class Record
        {
            internal ulong Offset;
            internal ulong Size;
            internal ulong BytesRead;
            internal uint BatchCount;
            internal bool IsBatch;
            internal ProcessingState State = ProcessingState.InQueue;
            internal double Started = double.NaN;
            internal double Finished = double.NaN;
            internal Recording Request;

            internal bool Done => State == ProcessingState.Completed || State == ProcessingState.Failed || State == ProcessingState.Canceled;

            internal AsyncReadManagerRequestMetric ToMetric(double now)
            {
                double end = double.IsNaN(Finished) ? now : Finished;
                double start = double.IsNaN(Started) ? end : Started;
                return new AsyncReadManagerRequestMetric
                {
                    m_AssetName = Request.AssetName,
                    m_FileName = Request.FileName,
                    m_OffsetBytes = Offset,
                    m_SizeBytes = Size,
                    m_AssetTypeId = Request.TypeId,
                    m_CurrentBytesRead = BytesRead,
                    m_BatchReadCount = BatchCount,
                    m_IsBatchRead = IsBatch,
                    m_State = State,
                    m_ReadType = FileReadType.Async,
                    m_PriorityLevel = Priority.PriorityLow,
                    m_Subsystem = Request.Subsystem,
                    m_RequestTimeMicroseconds = Request.Requested,
                    m_TimeInQueueMicroseconds = start - Request.Requested,
                    m_TotalTimeMicroseconds = end - Request.Requested,
                };
            }
        }

        /// <summary>One request's records. A file handle's reads name no file and no asset
        /// (asyncread metrics.handle.all).</summary>
        internal sealed class Recording
        {
            internal string AssetName;
            internal string FileName;
            internal ulong TypeId;
            internal AssetLoadingSubsystem Subsystem;
            internal double Requested;
            Record[] m_Records = Array.Empty<Record>();

            /// <summary>Records the request's commands, one record each, replacing those it had:
            /// a deferred read is recorded with the commands its array holds when it is made
            /// (asyncread metrics.pending.all) and again with those it takes when it starts.</summary>
            internal unsafe void Commands(ReadCommand* commands, int count)
            {
                count = commands == null ? 0 : Math.Max(count, 0);
                lock (s_Lock)
                {
                    if (count == m_Records.Length)
                    {
                        // The same number of commands: the records take the new ones in place.
                        for (int i = 0; i < count; i++)
                        {
                            m_Records[i].Offset = (ulong)commands[i].Offset;
                            m_Records[i].Size = (ulong)commands[i].Size;
                        }
                        return;
                    }
                    // A request's records sit together in the store, in command order (they are
                    // added together, and none is cleared before the request finishes).
                    int at = m_Records.Length > 0 ? s_Records.IndexOf(m_Records[0]) : -1;
                    if (at >= 0 && at + m_Records.Length <= s_Records.Count && s_Records[at + m_Records.Length - 1] == m_Records[^1])
                        s_Records.RemoveRange(at, m_Records.Length);
                    else
                    {
                        foreach (var record in m_Records) s_Records.Remove(record);
                        at = -1;
                    }
                    var records = new Record[count];
                    for (int i = 0; i < count; i++)
                    {
                        records[i] = new Record
                        {
                            Offset = (ulong)commands[i].Offset,
                            Size = (ulong)commands[i].Size,
                            BatchCount = (uint)count,
                            IsBatch = true,
                            Request = this,
                        };
                    }
                    s_Records.InsertRange(at < 0 ? s_Records.Count : at, records);
                    m_Records = records;
                }
            }

            /// <summary>A GetFileInfo request's one record: no offset, size or batch
            /// (asyncread metrics.fileInfo.all).</summary>
            internal void FileInfo()
            {
                lock (s_Lock)
                {
                    m_Records = new[] { new Record { Request = this } };
                    s_Records.Add(m_Records[0]);
                }
            }

            internal void Start(int index)
            {
                lock (s_Lock)
                {
                    if (index >= m_Records.Length) return;
                    m_Records[index].Started = Now();
                    m_Records[index].State = ProcessingState.Reading;
                }
            }

            /// <summary>A command finished: Failed when it asked for bytes and got none (the
            /// request's own rule), else Completed. <paramref name="opened"/> false: the file
            /// never opened, so the command spent no time queued (asyncread metrics.all).</summary>
            internal void Finish(int index, long size, long read, bool opened)
            {
                lock (s_Lock)
                {
                    if (index >= m_Records.Length) return;
                    var record = m_Records[index];
                    double now = Now();
                    if (double.IsNaN(record.Started)) record.Started = opened ? now : Requested;
                    record.Finished = now;
                    record.BytesRead = (ulong)Math.Max(read, 0);
                    record.State = size > 0 && read == 0 ? ProcessingState.Failed : ProcessingState.Completed;
                }
            }

            /// <summary>The records the request did not finish are canceled.</summary>
            internal void Cancel()
            {
                lock (s_Lock)
                {
                    double now = Now();
                    foreach (var record in m_Records)
                    {
                        if (record.Done) continue;
                        record.State = ProcessingState.Canceled;
                        record.Finished = now;
                    }
                }
            }
        }

        /// <summary>The recording of a request made now, or null when nothing is collected.</summary>
        internal static Recording Begin(string fileName, string assetName, ulong typeId, AssetLoadingSubsystem subsystem) =>
            s_Collecting
                ? new Recording { FileName = fileName ?? "", AssetName = assetName ?? "", TypeId = typeId, Subsystem = subsystem, Requested = Now() }
                : null;

        internal static AsyncReadManagerRequestMetric[] Snapshot(AsyncReadManagerMetricsFilters filters, bool clear)
        {
            var list = new List<AsyncReadManagerRequestMetric>();
            Take(filters, clear, list);
            return list.ToArray();
        }

        /// <summary>Fills <paramref name="into"/> (when given) with the records the filters keep,
        /// in request order; <paramref name="clear"/> then drops every finished record, whatever
        /// the filters keep, as ClearCompletedMetrics does (requests still queued or reading stay:
        /// asyncread metrics.pending.afterClear, metrics.afterFilteredClearOnRead).</summary>
        internal static void Take(AsyncReadManagerMetricsFilters filters, bool clear, List<AsyncReadManagerRequestMetric> into)
        {
            into?.Clear();
            lock (s_Lock)
            {
                double now = Now();
                int kept = 0;
                for (int i = 0; i < s_Records.Count; i++)
                {
                    var record = s_Records[i];
                    var metric = record.ToMetric(now);
                    if (filters == null || filters.Keeps(metric)) into?.Add(metric);
                    // Compacted in one pass: the kept records move down in order.
                    if (clear && record.Done) continue;
                    s_Records[kept++] = record;
                }
                s_Records.RemoveRange(kept, s_Records.Count - kept);
            }
        }

        internal static AsyncReadManagerSummaryMetrics Summarize(IReadOnlyList<AsyncReadManagerRequestMetric> metrics, AsyncReadManagerMetricsFilters filters)
        {
            var summary = new AsyncReadManagerSummaryMetrics();
            if (metrics == null) return summary;
            int counted = 0, finished = 0;
            double wait = 0, total = 0, read = 0, longestWait = 0, longestRead = 0, readBytes = 0, readTime = 0, requestTime = 0;
            ulong sizes = 0;
            for (int i = 0; i < metrics.Count; i++)
            {
                var metric = metrics[i];
                if (filters != null && !filters.Keeps(metric)) continue;
                switch (metric.m_State)
                {
                    case ProcessingState.InQueue: summary.m_NumberOfWaitingRequests++; break;
                    case ProcessingState.Reading: summary.m_NumberOfInProgressRequests++; break;
                    case ProcessingState.Completed: summary.m_NumberOfCompletedRequests++; break;
                    case ProcessingState.Failed: summary.m_NumberOfFailedRequests++; break;
                    // Unity 6000.0.71f1 (asyncread metrics.canceled.summary): a canceled request
                    // and a record of no state count nowhere.
                    default: continue;
                }
                counted++;
                wait += metric.m_TimeInQueueMicroseconds;
                total += metric.m_TotalTimeMicroseconds;
                longestWait = Math.Max(longestWait, metric.m_TimeInQueueMicroseconds);
                if (metric.m_State == ProcessingState.Completed || metric.m_State == ProcessingState.Failed)
                {
                    // The bytes asked for of every finished request, failed ones included
                    // (asyncread metrics.summary: 56 of reads of 32, 16 and 8 bytes).
                    finished++;
                    sizes += metric.m_SizeBytes;
                }
                if (metric.m_State == ProcessingState.Completed)
                {
                    double reading = metric.m_TotalTimeMicroseconds - metric.m_TimeInQueueMicroseconds;
                    read += reading;
                    longestRead = Math.Max(longestRead, reading);
                    readBytes += metric.m_CurrentBytesRead;
                    readTime += reading;
                    requestTime += metric.m_TotalTimeMicroseconds;
                }
            }
            if (counted == 0) return summary;
            int completed = summary.m_NumberOfCompletedRequests;
            summary.m_TotalBytesRead = sizes;
            summary.m_AverageReadSizeInBytes = finished > 0 ? (float)((double)sizes / finished) : 0;
            summary.m_AverageWaitTimeMicroseconds = (float)(wait / counted);
            summary.m_AverageTotalRequestTimeMicroseconds = (float)(total / counted);
            summary.m_AverageReadTimeMicroseconds = completed > 0 ? (float)(read / completed) : 0;
            summary.m_LongestWaitTimeMicroseconds = (float)longestWait;
            summary.m_LongestReadTimeMicroseconds = (float)longestRead;
            // Bytes per microsecond is MB per second (10^6 bytes per 10^6 microseconds).
            summary.m_AverageBandwidthMBPerSecond = readTime > 0 ? (float)(readBytes / readTime) : 0;
            summary.m_AverageThroughputMBPerSecond = requestTime > 0 ? (float)(readBytes / requestTime) : 0;
            return summary;
        }
    }
}
