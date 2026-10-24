using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Jobs;

/// <summary>Job scheduling: the job interfaces, dependencies, worker threads and job data.</summary>
public static class JobsProbe
{
    public struct SumJob : IJob
    {
        [ReadOnly] public NativeArray<int> Input;
        public NativeArray<long> Output;

        public void Execute()
        {
            long sum = 0;
            for (int i = 0; i < Input.Length; i++) sum += Input[i];
            Output[0] = sum;
        }
    }

    public struct SquareFor : IJobFor
    {
        public NativeArray<int> Data;

        public void Execute(int index) => Data[index] = index * index;
    }

    public struct OrderFor : IJobFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Order;
        [NativeDisableParallelForRestriction] public NativeArray<int> Next;

        public void Execute(int index)
        {
            Order[Next[0]] = index;
            Next[0]++;
        }
    }

    public struct SquareParallel : IJobParallelFor
    {
        public NativeArray<int> Data;

        public void Execute(int index) => Data[index] = index * index + 1;
    }

    public struct AddJob : IJob
    {
        public NativeArray<int> Data;
        public int Amount;

        public void Execute()
        {
            for (int i = 0; i < Data.Length; i++) Data[i] += Amount;
        }
    }

    public struct MultiplyJob : IJob
    {
        public NativeArray<int> Data;
        public int Factor;

        public void Execute()
        {
            for (int i = 0; i < Data.Length; i++) Data[i] *= Factor;
        }
    }

    public struct CopyJob : IJob
    {
        [ReadOnly] public NativeArray<int> From;
        public NativeArray<int> To;

        public void Execute() => To.CopyFrom(From);
    }

    public struct ContextJob : IJob
    {
        public NativeArray<int> Out;

        public void Execute()
        {
            Out[0] = JobsUtility.IsExecutingJob ? 1 : 0;
            Out[1] = JobsUtility.ThreadIndex;
            Out[2] = JobsUtility.ThreadIndexCount;
            Out[3] = Thread.CurrentThread.ManagedThreadId;
        }
    }

    public static int s_Ran;
    public static int s_RanOnThread;

    public struct FlagJob : IJob
    {
        public void Execute()
        {
            s_RanOnThread = Thread.CurrentThread.ManagedThreadId;
            Interlocked.Exchange(ref s_Ran, 1);
        }
    }

    public struct ThreadIndexParallel : IJobParallelFor
    {
        [NativeSetThreadIndex] public int ThreadIndex;
        public NativeArray<int> Indices;

        public void Execute(int index) => Indices[index] = ThreadIndex;
    }

    public unsafe struct CoverageParallel : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Hits;

        public void Execute(int index) => Interlocked.Increment(ref UnsafeUtility.ArrayElementAsRef<int>(Hits.GetUnsafePtr(), index));
    }

    public struct MutatingJob : IJob
    {
        public int Counter;
        public NativeArray<int> Out;

        public void Execute()
        {
            Counter += 5;
            Out[0] = Counter;
        }
    }

    public struct ThrowingJob : IJob
    {
        public void Execute() => throw new InvalidOperationException("thrown by a job");
    }

    public struct TransformJob : IJobParallelForTransform
    {
        public float Offset;

        public void Execute(int index, TransformAccess transform)
        {
            transform.position += new Vector3(index + Offset, 0, 0);
            transform.localRotation = Quaternion.Euler(0, index * 10, 0);
        }
    }

    public struct TransformReadJob : IJobParallelForTransform
    {
        public NativeArray<Vector3> Positions;

        public void Execute(int index, TransformAccess transform) => Positions[index] = transform.position;
    }

    public struct GenericJob<T> : IJob where T : unmanaged
    {
        public NativeArray<T> Data;
        public T Value;

        public void Execute()
        {
            for (int i = 0; i < Data.Length; i++) Data[i] = Value;
        }
    }

    public static void RunAll()
    {
        Simple();
        For();
        Parallel();
        Dependencies();
        Context();
        JobData();
        Exceptions();
        Transforms();
        Generic();
    }

    static void Simple()
    {
        ProbeLog.Begin("jobs.ijob");
        var input = new NativeArray<int>(1000, Allocator.TempJob);
        for (int i = 0; i < input.Length; i++) input[i] = i * 3 - 7;
        var output = new NativeArray<long>(1, Allocator.TempJob);
        var handle = new SumJob { Input = input, Output = output }.Schedule();
        ProbeLog.Value("handleIsDefault", handle.Equals(default(JobHandle)));
        handle.Complete();
        ProbeLog.Value("sum", output[0]);
        ProbeLog.Value("isCompleted", handle.IsCompleted);
        handle.Complete();
        output[0] = 0;
        new SumJob { Input = input, Output = output }.Run();
        ProbeLog.Value("runSum", output[0]);
        input.Dispose();
        output.Dispose();

        ProbeLog.Begin("jobs.handle");
        JobHandle none = default;
        ProbeLog.Value("defaultIsCompleted", none.IsCompleted);
        none.Complete();
        var combined = JobHandle.CombineDependencies(default, default);
        ProbeLog.Value("combineDefaults.isDefault", combined.Equals(default(JobHandle)));
        ProbeLog.Value("combineDefaults.isCompleted", combined.IsCompleted);
        JobHandle.ScheduleBatchedJobs();
    }

    static void For()
    {
        ProbeLog.Begin("jobs.ijobfor");
        var data = new NativeArray<int>(257, Allocator.TempJob);
        new SquareFor { Data = data }.Schedule(data.Length, default).Complete();
        ProbeLog.Value("single[256]", data[256]);
        ProbeLog.Value("single.sum", Sum(data));
        for (int i = 0; i < data.Length; i++) data[i] = 0;
        new SquareFor { Data = data }.ScheduleParallel(data.Length, 16, default).Complete();
        ProbeLog.Value("parallel.sum", Sum(data));
        for (int i = 0; i < data.Length; i++) data[i] = 0;
        new SquareFor { Data = data }.Run(data.Length);
        ProbeLog.Value("run.sum", Sum(data));
        data.Dispose();

        var order = new NativeArray<int>(64, Allocator.TempJob);
        var next = new NativeArray<int>(1, Allocator.TempJob);
        new OrderFor { Order = order, Next = next }.Schedule(64, default).Complete();
        bool inOrder = next[0] == 64;
        for (int i = 0; i < 64; i++) inOrder &= order[i] == i;
        ProbeLog.Value("single.inOrder", inOrder);
        order.Dispose();
        next.Dispose();
    }

    static void Parallel()
    {
        ProbeLog.Begin("jobs.parallelfor");
        var data = new NativeArray<int>(1000, Allocator.TempJob);
        new SquareParallel { Data = data }.Schedule(data.Length, 7).Complete();
        ProbeLog.Value("sum", Sum(data));
        ProbeLog.Value("[999]", data[999]);
        new SquareParallel { Data = data }.Schedule(data.Length, 0).Complete();
        ProbeLog.Value("batch0.sum", Sum(data));
        new SquareParallel { Data = data }.Run(10);
        ProbeLog.Value("run10.[9]", data[9]);
        var empty = new SquareParallel { Data = data }.Schedule(0, 1);
        empty.Complete();
        ProbeLog.Value("emptyCompleted", empty.IsCompleted);
        data.Dispose();

        var hits = new NativeArray<int>(5000, Allocator.TempJob);
        new CoverageParallel { Hits = hits }.Schedule(hits.Length, 3).Complete();
        int once = 0;
        for (int i = 0; i < hits.Length; i++) if (hits[i] == 1) once++;
        ProbeLog.Value("coverage.exactlyOnce", once);
        hits.Dispose();
    }

    static void Dependencies()
    {
        ProbeLog.Begin("jobs.dependencies");
        var data = new NativeArray<int>(100, Allocator.TempJob);
        var a = new AddJob { Data = data, Amount = 3 }.Schedule();
        var b = new MultiplyJob { Data = data, Factor = 5 }.Schedule(a);
        var c = new AddJob { Data = data, Amount = -1 }.Schedule(b);
        c.Complete();
        ProbeLog.Value("chain", data[0]);
        ProbeLog.Value("aCompleted", a.IsCompleted);

        var other = new NativeArray<int>(100, Allocator.TempJob);
        var x = new AddJob { Data = data, Amount = 1 }.Schedule();
        var y = new AddJob { Data = other, Amount = 2 }.Schedule();
        var both = JobHandle.CombineDependencies(x, y);
        var target = new NativeArray<int>(100, Allocator.TempJob);
        var copy = new CopyJob { From = data, To = target }.Schedule(both);
        copy.Complete();
        ProbeLog.Value("combined", target[0]);
        ProbeLog.Value("otherCompletedBySync", y.IsCompleted);
        other.Dispose();

        var handles = new NativeArray<JobHandle>(3, Allocator.Temp);
        var d0 = new AddJob { Data = data, Amount = 1 }.Schedule();
        handles[0] = d0;
        handles[1] = default;
        handles[2] = d0;
        var all = JobHandle.CombineDependencies(handles);
        JobHandle.CompleteAll(handles);
        all.Complete();
        ProbeLog.Value("array", data[0]);
        handles.Dispose();
        target.Dispose();
        data.Dispose();
    }

    static void Context()
    {
        ProbeLog.Begin("jobs.context");
        ProbeLog.Value("main.isExecutingJob", JobsUtility.IsExecutingJob);
        ProbeLog.Value("main.threadIndex", JobsUtility.ThreadIndex);
        ProbeLog.Value("workerCountPositive", JobsUtility.JobWorkerCount > 0);
        ProbeLog.Value("maximumAtLeastCount", JobsUtility.JobWorkerMaximumCount >= JobsUtility.JobWorkerCount);
        ProbeLog.Value("indexCountIsWorkersPlusOne", JobsUtility.ThreadIndexCount == JobsUtility.JobWorkerCount + 1);
        ProbeLog.Value("indexCountIsMaximumPlusOne", JobsUtility.ThreadIndexCount == JobsUtility.JobWorkerMaximumCount + 1);
        ProbeLog.Value("maximumIsProcessorsMinusOne", JobsUtility.JobWorkerMaximumCount == SystemInfo.processorCount - 1);
        ProbeLog.Value("countIsProcessorsMinusOne", JobsUtility.JobWorkerCount == SystemInfo.processorCount - 1);
        ProbeLog.Value("indexCountMinusMaximum", JobsUtility.ThreadIndexCount - JobsUtility.JobWorkerMaximumCount);
        ProbeLog.Value("maxJobThreadCount", JobsUtility.MaxJobThreadCount);
        ProbeLog.Value("cacheLineSize", JobsUtility.CacheLineSize);
        var output = new NativeArray<int>(4, Allocator.TempJob);
        new ContextJob { Out = output }.Schedule().Complete();
        ProbeLog.Value("scheduled.isExecutingJob", output[0]);
        ProbeLog.Value("scheduled.threadIndexInRange", output[1] >= 0 && output[1] < output[2]);
        ProbeLog.Value("scheduled.indexCountMatches", output[2] == JobsUtility.ThreadIndexCount);
        ProbeLog.Value("scheduled.onOtherThread", output[3] != Thread.CurrentThread.ManagedThreadId);
        s_Ran = 0;
        var unflushed = new FlagJob().Schedule();
        Thread.Sleep(100);
        ProbeLog.Value("unflushed.ranBeforeFlush", s_Ran);
        JobHandle.ScheduleBatchedJobs();
        for (int i = 0; i < 200 && s_Ran == 0; i++) Thread.Sleep(5);
        ProbeLog.Value("unflushed.ranAfterFlush", s_Ran);
        ProbeLog.Value("unflushed.onOtherThread", s_RanOnThread != Thread.CurrentThread.ManagedThreadId);
        unflushed.Complete();
        s_Ran = 0;
        var polled = new FlagJob().Schedule();
        bool completedAtFirstPoll = polled.IsCompleted;
        for (int i = 0; i < 200 && s_Ran == 0; i++) Thread.Sleep(5);
        ProbeLog.Value("polled.ranAfterIsCompleted", s_Ran);
        polled.Complete();
        new ContextJob { Out = output }.Run();
        ProbeLog.Value("run.isExecutingJob", output[0]);
        ProbeLog.Value("run.threadIndex", output[1]);
        ProbeLog.Value("run.onMainThread", output[3] == Thread.CurrentThread.ManagedThreadId);
        output.Dispose();

        var indices = new NativeArray<int>(512, Allocator.TempJob);
        new ThreadIndexParallel { Indices = indices }.Schedule(indices.Length, 8).Complete();
        bool inRange = true;
        for (int i = 0; i < indices.Length; i++) inRange &= indices[i] >= 0 && indices[i] < JobsUtility.ThreadIndexCount;
        ProbeLog.Value("setThreadIndexInRange", inRange);
        indices.Dispose();

        int workers = JobsUtility.JobWorkerCount;
        JobsUtility.JobWorkerCount = 1;
        ProbeLog.Value("setWorkerCount", JobsUtility.JobWorkerCount);
        ProbeLog.Try("setWorkerCountNegative", () => JobsUtility.JobWorkerCount = -1);
        JobsUtility.JobWorkerCount = workers;
        JobsUtility.ResetJobWorkerCount();
        ProbeLog.Value("resetWorkerCountRestores", JobsUtility.JobWorkerCount == workers);
    }

    static void JobData()
    {
        ProbeLog.Begin("jobs.data");
        var output = new NativeArray<int>(1, Allocator.TempJob);
        var job = new MutatingJob { Counter = 1, Out = output };
        job.Schedule().Complete();
        ProbeLog.Value("schedule.out", output[0]);
        ProbeLog.Value("schedule.callerCounter", job.Counter);
        job.Run();
        ProbeLog.Value("run.out", output[0]);
        ProbeLog.Value("run.callerCounter", job.Counter);
        job.ScheduleByRef().Complete();
        ProbeLog.Value("scheduleByRef.callerCounter", job.Counter);
        job.RunByRef();
        ProbeLog.Value("runByRef.callerCounter", job.Counter);
        output.Dispose();
    }

    static void Exceptions()
    {
        ProbeLog.Begin("jobs.exceptions");
        using (var capture = new LogCapture())
        {
            var handle = new ThrowingJob().Schedule();
            ProbeLog.Try("complete", () => handle.Complete());
            capture.Record("scheduled");
        }
        using (var capture = new LogCapture())
        {
            ProbeLog.Try("run", () => new ThrowingJob().Run());
            capture.Record("run");
        }
    }

    static void Transforms()
    {
        ProbeLog.Begin("jobs.transforms");
        var objects = new Transform[5];
        for (int i = 0; i < objects.Length; i++)
        {
            objects[i] = new GameObject("t" + i).transform;
            objects[i].position = new Vector3(0, i, 0);
        }
        var array = new TransformAccessArray(objects);
        ProbeLog.Value("length", array.length);
        ProbeLog.Value("isCreated", array.isCreated);
        new TransformJob { Offset = 0.5f }.Schedule(array).Complete();
        for (int i = 0; i < objects.Length; i++)
        {
            ProbeLog.Value("position" + i, objects[i].position);
            ProbeLog.Value("rotation" + i, objects[i].rotation.eulerAngles.y);
        }
        var positions = new NativeArray<Vector3>(objects.Length, Allocator.TempJob);
        new TransformReadJob { Positions = positions }.ScheduleReadOnly(array, 2).Complete();
        ProbeLog.Value("read4", positions[4]);
        new TransformReadJob { Positions = positions }.RunReadOnly(array);
        ProbeLog.Value("runRead3", positions[3]);
        positions.Dispose();

        array.RemoveAtSwapBack(1);
        ProbeLog.Value("afterRemove.length", array.length);
        ProbeLog.Value("afterRemove[1]", array[1].name);
        array.Add(new GameObject("added").transform);
        ProbeLog.Value("afterAdd[4]", array[4].name);
        array[0] = objects[2];
        ProbeLog.Value("set[0]", array[0].name);
        array.capacity = 16;
        ProbeLog.Value("capacity", array.capacity);
        UnityEngine.Object.DestroyImmediate(objects[3].gameObject);
        ProbeLog.Value("destroyedIsNull", array[3] == null);
        array.Dispose();
        ProbeLog.Value("disposed.isCreated", array.isCreated);
        ProbeLog.Try("disposed.length", () => { var _ = array.length; });

        var allocated = new TransformAccessArray(4, 2);
        ProbeLog.Value("allocated.capacity", allocated.capacity);
        ProbeLog.Value("allocated.length", allocated.length);
        allocated.Dispose();
        for (int i = 0; i < objects.Length; i++)
            if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i].gameObject);
    }

    static void Generic()
    {
        ProbeLog.Begin("jobs.generic");
        var data = new NativeArray<float>(4, Allocator.TempJob);
        ProbeLog.Try("schedule", () => new GenericJob<float> { Data = data, Value = 2.5f }.Schedule().Complete());
        ProbeLog.Value("value", data[3]);
        data.Dispose();
    }

    static long Sum(NativeArray<int> data)
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return sum;
    }
}
