using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>com.unity.collections' containers and job types running their real code paths.</summary>
public static class CollectionsProbe
{
    public struct FillList : IJob
    {
        public NativeList<int> List;
        public int Count;

        public void Execute()
        {
            for (int i = 0; i < Count; i++) List.Add(i * 2);
        }
    }

    public struct DoubleDeferred : IJobParallelForDefer
    {
        public NativeArray<int> Values;

        public void Execute(int index) => Values[index] *= 2;
    }

    public struct BatchJob : IJobParallelForBatch
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Starts;

        public void Execute(int startIndex, int count)
        {
            for (int i = startIndex; i < startIndex + count; i++) Starts[i] = startIndex * 1000 + count;
        }
    }

    public struct QueueWriter : IJobParallelFor
    {
        public NativeQueue<int>.ParallelWriter Queue;

        public void Execute(int index) => Queue.Enqueue(index);
    }

    public struct MapWriter : IJobParallelFor
    {
        public NativeParallelHashMap<int, int>.ParallelWriter Map;

        public void Execute(int index) => Map.TryAdd(index, index * index);
    }

    public struct ListWriter : IJobParallelFor
    {
        public NativeList<int>.ParallelWriter List;

        public void Execute(int index) => List.AddNoResize(index);
    }

    public struct StreamWriterJob : IJobParallelFor
    {
        public NativeStream.Writer Writer;

        public void Execute(int index)
        {
            Writer.BeginForEachIndex(index);
            for (int i = 0; i <= index; i++) Writer.Write(i);
            Writer.EndForEachIndex();
        }
    }

    public struct ReferenceJob : IJob
    {
        public NativeReference<int> Value;

        public void Execute() => Value.Value += 10;
    }

    public struct FilterJob : IJobFilter
    {
        public bool Execute(int index) => index % 3 == 0;
    }

    public struct FixedStringJob : IJob
    {
        public NativeArray<FixedString64Bytes> Out;

        public void Execute()
        {
            var s = new FixedString64Bytes("job ");
            s.Append(42);
            s.Append(' ');
            s.Append(1.5f);
            Out[0] = s;
        }
    }

    public struct MathJob : IJobParallelFor
    {
        public NativeArray<float3> Points;

        public void Execute(int index)
        {
            var p = new float3(index, index * 0.5f, 1f);
            Points[index] = math.normalize(math.cross(p, new float3(0, 1, 0)) + 1f);
        }
    }

    public static void RunAll()
    {
        Lists();
        Deferred();
        Batches();
        Parallel();
        Misc();
        Allocators();
        Strings();
    }

    static void Lists()
    {
        ProbeLog.Begin("collections.list");
        var list = new NativeList<int>(2, Allocator.TempJob);
        new FillList { List = list, Count = 100 }.Schedule().Complete();
        ProbeLog.Value("length", list.Length);
        ProbeLog.Value("capacityAtLeast", list.Capacity >= 100);
        ProbeLog.Value("[99]", list[99]);
        var array = list.AsArray();
        ProbeLog.Value("asArray.length", array.Length);
        list.Add(5);
        ProbeLog.Try("asArray.readAfterAdd", () => { var _ = array[0]; });
        ProbeLog.Try("asArray.lengthAfterAdd", () => { var _ = array.Length; });
        var handle = new FillList { List = list, Count = 1 }.Schedule();
        ProbeLog.Try("readWhileScheduled", () => { var _ = list[0]; });
        ProbeLog.Try("lengthWhileScheduled", () => { var _ = list.Length; });
        handle.Complete();
        list.RemoveAtSwapBack(0);
        ProbeLog.Value("removeAtSwapBack[0]", list[0]);
        list.Sort();
        ProbeLog.Value("sorted[0]", list[0]);
        ProbeLog.Value("sorted[last]", list[list.Length - 1]);
        ProbeLog.Try("indexOutOfRange", () => { var _ = list[1000]; });
        list.Dispose();
        ProbeLog.Try("readAfterDispose", () => { var _ = list.Length; });
    }

    static void Deferred()
    {
        ProbeLog.Begin("collections.deferred");
        var list = new NativeList<int>(Allocator.TempJob);
        var fill = new FillList { List = list, Count = 37 }.Schedule();
        var doubled = new DoubleDeferred { Values = list.AsDeferredJobArray() }.Schedule(list, 4, fill);
        doubled.Complete();
        ProbeLog.Value("length", list.Length);
        ProbeLog.Value("[36]", list[36]);
        list.Dispose();
    }

    static void Batches()
    {
        ProbeLog.Begin("collections.batch");
        var starts = new NativeArray<int>(50, Allocator.TempJob);
        new BatchJob { Starts = starts }.ScheduleBatch(starts.Length, 8).Complete();
        var seen = new SortedSet<int>();
        for (int i = 0; i < starts.Length; i++) seen.Add(starts[i]);
        ProbeLog.Value("batches", string.Join(",", seen));
        new BatchJob { Starts = starts }.RunBatch(starts.Length);
        ProbeLog.Value("run[49]", starts[49]);
        starts.Dispose();

        ProbeLog.Begin("collections.filter");
        var indices = new NativeList<int>(Allocator.TempJob);
        new FilterJob().ScheduleAppend(indices, 20).Complete();
        ProbeLog.Value("append", string.Join(",", indices.AsArray().ToArray()));
        new FilterJob().ScheduleFilter(indices).Complete();
        ProbeLog.Value("filter", string.Join(",", indices.AsArray().ToArray()));
        indices.Dispose();
    }

    static void Parallel()
    {
        ProbeLog.Begin("collections.parallel");
        var queue = new NativeQueue<int>(Allocator.TempJob);
        new QueueWriter { Queue = queue.AsParallelWriter() }.Schedule(1000, 16).Complete();
        ProbeLog.Value("queue.count", queue.Count);
        long sum = 0;
        while (queue.TryDequeue(out var v)) sum += v;
        ProbeLog.Value("queue.sum", sum);
        queue.Dispose();

        var map = new NativeParallelHashMap<int, int>(500, Allocator.TempJob);
        new MapWriter { Map = map.AsParallelWriter() }.Schedule(500, 32).Complete();
        ProbeLog.Value("map.count", map.Count());
        ProbeLog.Value("map[21]", map[21]);
        ProbeLog.Try("map.missing", () => { var _ = map[1000]; });
        map.Dispose();

        var list = new NativeList<int>(300, Allocator.TempJob);
        new ListWriter { List = list.AsParallelWriter() }.Schedule(300, 10).Complete();
        list.Sort();
        ProbeLog.Value("list.length", list.Length);
        ProbeLog.Value("list[299]", list[299]);
        list.Dispose();

        var stream = new NativeStream(20, Allocator.TempJob);
        new StreamWriterJob { Writer = stream.AsWriter() }.Schedule(20, 3).Complete();
        ProbeLog.Value("stream.count", stream.Count());
        var reader = stream.AsReader();
        reader.BeginForEachIndex(19);
        ProbeLog.Value("stream.remaining19", reader.RemainingItemCount);
        int streamSum = 0;
        while (reader.RemainingItemCount > 0) streamSum += reader.Read<int>();
        reader.EndForEachIndex();
        ProbeLog.Value("stream.sum19", streamSum);
        stream.Dispose();

        var points = new NativeArray<float3>(64, Allocator.TempJob);
        new MathJob { Points = points }.Schedule(points.Length, 4).Complete();
        ProbeLog.Value("math[63].x", points[63].x);
        ProbeLog.Value("math[63].z", points[63].z);
        points.Dispose();
    }

    static void Misc()
    {
        ProbeLog.Begin("collections.misc");
        var reference = new NativeReference<int>(5, Allocator.TempJob);
        new ReferenceJob { Value = reference }.Schedule().Complete();
        ProbeLog.Value("reference", reference.Value);
        reference.Dispose();

        var map = new NativeHashMap<FixedString32Bytes, int>(4, Allocator.Temp);
        map.Add("one", 1);
        map.Add("two", 2);
        ProbeLog.Value("hashMap.two", map["two"]);
        ProbeLog.Try("hashMap.duplicate", () => map.Add("one", 3));
        map.Dispose();

        var set = new NativeHashSet<int>(8, Allocator.Temp);
        for (int i = 0; i < 20; i++) set.Add(i % 7);
        ProbeLog.Value("hashSet.count", set.Count);
        set.Dispose();

        var bits = new NativeBitArray(100, Allocator.Temp);
        bits.Set(3, true);
        bits.Set(64, true);
        ProbeLog.Value("bits.countBits", bits.CountBits(0, 100));
        bits.Dispose();

        var unsafeList = new UnsafeList<int>(4, Allocator.Persistent);
        for (int i = 0; i < 10; i++) unsafeList.Add(i);
        ProbeLog.Value("unsafeList.length", unsafeList.Length);
        unsafeList.Dispose();

        var ring = new NativeRingQueue<int>(3, Allocator.Temp);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ProbeLog.Value("ring.dequeue", ring.Dequeue());
        ring.Dispose();

        var sortArray = new NativeArray<int>(new[] { 5, 3, 9, 1, 7 }, Allocator.TempJob);
        sortArray.SortJob().Schedule().Complete();
        ProbeLog.Value("sortJob", string.Join(",", sortArray.ToArray()));
        ProbeLog.Value("binarySearch", sortArray.BinarySearch(7));
        sortArray.Dispose();
    }

    static void Allocators()
    {
        ProbeLog.Begin("collections.allocators");
        var allocator = new AllocatorHelper<RewindableAllocator>(Allocator.Persistent);
        allocator.Allocator.Initialize(16 * 1024);
        var list = new NativeList<int>(10, allocator.Allocator.Handle);
        for (int i = 0; i < 1000; i++) list.Add(i);
        ProbeLog.Value("rewindable.length", list.Length);
        allocator.Allocator.Rewind();
        ProbeLog.Try("rewindable.afterRewind", () => { var _ = list.Length; });
        allocator.Allocator.Dispose();
        allocator.Dispose();

        var custom = CollectionHelper.CreateNativeArray<int>(4, Allocator.Persistent);
        ProbeLog.Value("collectionHelper.length", custom.Length);
        CollectionHelper.Dispose(custom);

        var temp = new NativeArray<int>(4, Allocator.Temp);
        ProbeLog.Value("tempHandle.isTemp", AtomicSafetyHandle.IsTempMemoryHandle(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(temp)));
        temp.Dispose();
    }

    static void Strings()
    {
        ProbeLog.Begin("collections.strings");
        var output = new NativeArray<FixedString64Bytes>(1, Allocator.TempJob);
        new FixedStringJob { Out = output }.Schedule().Complete();
        ProbeLog.Value("fixedString", output[0].ToString());
        output.Dispose();
        FixedString32Bytes small = "abc";
        ProbeLog.Value("fixed32.length", small.Length);
        ProbeLog.Try("fixed32.overflow", () => { FixedString32Bytes s = new string('x', 40); });
        var text = new NativeText("hello", Allocator.Temp);
        text.Append(" world");
        ProbeLog.Value("nativeText", text.ToString());
        text.Dispose();
        ProbeLog.Value("format", FixedString.Format("{0}-{1}", 3, 4.25f).ToString());
    }
}
