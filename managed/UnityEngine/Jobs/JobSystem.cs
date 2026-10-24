using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unbound.Runtime.Jobs
{
    /// <summary>What CreateJobReflectionData returns a pointer to: the job type and its function.</summary>
    internal sealed class JobReflection
    {
        public Type Wrapper;
        public Type User;
        public Delegate Execute;
        public JobSystem.Invoker Invoke;
        public JobLayout Layout;
        /// <summary>The name in Unity's messages: <c>Namespace.Outer:Inner</c>.</summary>
        public string Name;
        public int Size;
    }

    /// <summary>A scheduled job, a combination of dependencies, until it is synced.</summary>
    internal sealed unsafe class JobRecord
    {
        public ulong Id;
        public string Name;
        public JobReflection Reflection;
        public byte* Data;
        public IntPtr AdditionalData;
        public IntPtr BufferRangePatchData;
        public JobRecord[] Dependencies = Array.Empty<JobRecord>();
        public readonly List<JobRecord> Dependents = new List<JobRecord>();
        public int PendingDependencies;
        /// <summary>The job's ranges (parallel jobs): a JobRanges, then per job index its element
        /// range, then per job index the next batch it will take.</summary>
        public JobRanges* Ranges;
        public int Participants = 1;
        /// <summary>Per participant: whether a thread took it.</summary>
        public int[] Claimed = new int[1];
        public int Remaining;
        public bool Flushed;
        public bool Parallel;
        public bool Transform;
        public volatile bool Done;
        public bool Synced;
        public readonly ManualResetEventSlim Finished = new ManualResetEventSlim(false);
        public readonly List<Safety.Node> Nodes = new List<Safety.Node>();
        public readonly List<Safety.Shadow> Shadows = new List<Safety.Shadow>();
        /// <summary>Containers to deallocate after the job ([DeallocateOnJobCompletion]).</summary>
        public readonly List<(IntPtr Buffer, Allocator Allocator, AtomicSafetyHandle Safety)> Deallocations = new List<(IntPtr, Allocator, AtomicSafetyHandle)>();
        public Action OnFinished;
        /// <summary>Set once the job's data and ranges are final (deferred arrays and lengths read).</summary>
        public int Prepared;
        /// <summary>The list whose length a deferred parallel-for job iterates (its UnsafeList).</summary>
        public void* DeferredList;
        public int DeferredBatchSize;

        public JobHandle Handle => new JobHandle { jobGroup = Id, version = 1 };
    }

    /// <summary>
    /// Unity's job system: jobs scheduled from the main thread wait in a batch until
    /// ScheduleBatchedJobs (or Complete/IsCompleted) flushes it, then run on worker threads in
    /// dependency order; Complete runs what it waits for on the calling thread when no worker has
    /// started it. Parallel-for jobs split into batches the participants take and steal. The safety
    /// system's state of a job is released when it is synced (Complete).
    /// </summary>
    internal static unsafe class JobSystem
    {
        internal delegate void Invoker(Delegate function, IntPtr data, IntPtr additional, IntPtr bufferRangePatchData, IntPtr ranges, int jobIndex);

        static readonly object s_Lock = new object();
        static readonly ConcurrentDictionary<ulong, JobRecord> s_Records = new ConcurrentDictionary<ulong, JobRecord>();
        static readonly List<JobRecord> s_Batch = new List<JobRecord>();
        static readonly ConcurrentQueue<(JobRecord Job, int Index)> s_Ready = new ConcurrentQueue<(JobRecord, int)>();
        static readonly SemaphoreSlim s_Work = new SemaphoreSlim(0);
        static long s_NextId;
        static Thread[] s_Workers;
        static int s_ActiveWorkers;
        static int s_PendingTransformJobs;

        /// <summary>Unity's worker count: one per core but the main thread's.</summary>
        internal static readonly int MaximumWorkers = Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>Thread indices: the main thread (0), the workers, and Unity's four background workers.</summary>
        internal static int ThreadIndexCount => MaximumWorkers + 5;

        internal static int ActiveWorkers
        {
            get => s_ActiveWorkers;
            set
            {
                EnsureWorkers();
                s_ActiveWorkers = value;
            }
        }

        [ThreadStatic] static JobRecord t_Executing;
        [ThreadStatic] static int t_ThreadIndex;
        [ThreadStatic] static Safety.TempScope t_JobScope;

        internal static bool IsExecutingJob => t_Executing != null;
        internal static int ThreadIndex => t_Executing != null ? t_ThreadIndex : 0;

        /// <summary>The temp memory scope of the job executing on this thread.</summary>
        internal static Safety.TempScope CurrentTempScope => t_Executing != null ? t_JobScope : null;

        static void EnsureWorkers()
        {
            if (s_Workers != null) return;
            lock (s_Lock)
            {
                if (s_Workers != null) return;
                s_ActiveWorkers = MaximumWorkers;
                var workers = new Thread[MaximumWorkers];
                for (int i = 0; i < workers.Length; i++)
                {
                    int index = i + 1;
                    workers[i] = new Thread(() => WorkerLoop(index)) { IsBackground = true, Name = "Job.Worker " + i };
                    workers[i].Start();
                }
                s_Workers = workers;
            }
        }

        static void WorkerLoop(int index)
        {
            t_ThreadIndex = index;
            t_JobScope = new Safety.TempScope();
            while (true)
            {
                // Workers above JobWorkerCount stay idle; the work goes to the others (or to Complete).
                while (index > Volatile.Read(ref s_ActiveWorkers)) Thread.Sleep(10);
                s_Work.Wait();
                // Every item left (some were taken by Complete on another thread meanwhile).
                while (index <= Volatile.Read(ref s_ActiveWorkers) && s_Ready.TryDequeue(out var item)) Execute(item.Job, item.Index);
            }
        }

        // ------------------------------------------------------------------ reflection data

        internal static IntPtr CreateReflectionData(Type wrapperJobType, Type userJobType, object managedJobFunction0)
        {
            if (wrapperJobType == null) throw new ArgumentNullException("wrapperJobType");
            if (userJobType == null) throw new ArgumentNullException("userJobType");
            var reflection = new JobReflection
            {
                Wrapper = wrapperJobType,
                User = userJobType,
                Name = JobName(userJobType),
                Size = UnsafeUtility.SizeOf(wrapperJobType),
                Layout = JobLayout.For(wrapperJobType),
            };
            if (managedJobFunction0 != null)
            {
                if (managedJobFunction0 is not Delegate function)
                    throw new ArgumentException("managedJobFunction0 must be a delegate", nameof(managedJobFunction0));
                reflection.Execute = function;
                reflection.Invoke = InvokerFor(function.GetType());
            }
            return GCHandle.ToIntPtr(GCHandle.Alloc(reflection));
        }

        static JobReflection Reflection(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero) throw new InvalidOperationException("Reflection data was not set up by an Initialize() call.");
            return (JobReflection)GCHandle.FromIntPtr(reflectionData).Target;
        }

        /// <summary>Unity's name for a job type in messages: its declaring types and its name
        /// separated by ':', without namespace or generic arity.</summary>
        static string JobName(Type type)
        {
            var name = type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            for (var outer = type.DeclaringType; outer != null; outer = outer.DeclaringType)
            {
                var outerName = outer.Name;
                tick = outerName.IndexOf('`');
                if (tick >= 0) outerName = outerName.Substring(0, tick);
                name = outerName + ":" + name;
            }
            return name;
        }

        static readonly ConcurrentDictionary<Type, Invoker> s_Invokers = new ConcurrentDictionary<Type, Invoker>();

        /// <summary>Calls a job function delegate <c>(ref T data, IntPtr, IntPtr, ref JobRanges, int)</c>
        /// with pointers, as the native side does.</summary>
        static Invoker InvokerFor(Type delegateType) => s_Invokers.GetOrAdd(delegateType, type =>
        {
            var invoke = type.GetMethod("Invoke");
            var parameters = invoke.GetParameters();
            if (parameters.Length != 5 || !parameters[0].ParameterType.IsByRef || parameters[1].ParameterType != typeof(IntPtr) ||
                parameters[2].ParameterType != typeof(IntPtr) || parameters[3].ParameterType != typeof(JobRanges).MakeByRefType() ||
                parameters[4].ParameterType != typeof(int))
                throw new InvalidOperationException($"Unbound: the job function {type} does not have the signature (ref T, IntPtr, IntPtr, ref JobRanges, int) the job system calls");
            var method = new DynamicMethod("InvokeJob", typeof(void),
                new[] { typeof(Delegate), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(int) }, typeof(JobSystem).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Ldarg_S, (byte)5);
            il.Emit(OpCodes.Callvirt, invoke);
            il.Emit(OpCodes.Ret);
            return (Invoker)method.CreateDelegate(typeof(Invoker));
        });

        // ------------------------------------------------------------------ handles

        static JobRecord Find(in JobHandle handle) =>
            handle.jobGroup != 0 && s_Records.TryGetValue(handle.jobGroup, out var record) ? record : null;

        /// <summary>The unsynced job of a handle, or null.</summary>
        internal static JobRecord RecordOf(JobHandle handle) => Find(handle);

        /// <summary>Whether <paramref name="job"/> is <paramref name="handle"/>'s job or one of its
        /// (transitive) dependencies.</summary>
        internal static bool IsDependency(JobHandle handle, JobRecord job) => IsDependency(Find(handle), job);

        static bool IsDependency(JobRecord of, JobRecord job)
        {
            if (of == null) return false;
            if (of == job) return true;
            foreach (var dependency in of.Dependencies)
                if (IsDependency(dependency, job)) return true;
            return false;
        }

        internal static JobHandle Combine(JobHandle* handles, int count)
        {
            var dependencies = new List<JobRecord>();
            for (int i = 0; i < count; i++)
            {
                var record = Find(handles[i]);
                if (record != null && !record.Synced && !dependencies.Contains(record)) dependencies.Add(record);
            }
            if (dependencies.Count == 0) return default;
            if (dependencies.Count == 1) return dependencies[0].Handle;
            var combined = NewRecord("CombineDependencies", null, dependencies.ToArray());
            combined.Remaining = 0;
            lock (s_Lock)
            {
                combined.Flushed = true;
                if (combined.PendingDependencies == 0) FinishLocked(combined);
            }
            return combined.Handle;
        }

        static JobRecord NewRecord(string name, JobReflection reflection, JobRecord[] dependencies)
        {
            var record = new JobRecord
            {
                Id = (ulong)Interlocked.Increment(ref s_NextId),
                Name = name,
                Reflection = reflection,
                Dependencies = dependencies,
            };
            lock (s_Lock)
            {
                foreach (var dependency in dependencies)
                {
                    if (dependency.Done) continue;
                    record.PendingDependencies++;
                    dependency.Dependents.Add(record);
                }
            }
            s_Records[record.Id] = record;
            return record;
        }

        // ------------------------------------------------------------------ scheduling

        internal enum Kind
        {
            Single,
            ParallelFor,
            Transform,
            TransformReadOnly,
        }

        /// <summary>A parallel-for over the length a list has when the job starts.</summary>
        internal static JobHandle ScheduleDeferred(ref JobsUtility.JobScheduleParameters parameters, int innerloopBatchCount, void* listData, void* listSafety)
        {
            // The job reads the list's length when it starts: it is one of the list's readers.
            var safety = listSafety != null ? *(AtomicSafetyHandle*)listSafety : default;
            return Schedule(ref parameters, Kind.ParallelFor, 0, innerloopBatchCount, default, listData, safety);
        }

        internal static JobHandle Schedule(ref JobsUtility.JobScheduleParameters parameters, Kind kind, int arrayLength, int innerloopBatchCount, IntPtr transforms = default, void* deferredList = null, AtomicSafetyHandle deferredListSafety = default)
        {
            var reflection = Reflection(parameters.ReflectionData);
            if (reflection.Invoke == null)
                throw new InvalidOperationException($"Unbound: {reflection.Name} is a job Unity runs in native engine code, which Unbound does not implement");
            var mode = (ScheduleMode)parameters.ScheduleMode;
            var layout = reflection.Layout;
            if (layout.Error != null) throw new InvalidOperationException(layout.Error);
            var dependency = Find(parameters.Dependency);
            var dependencies = dependency != null && !dependency.Synced ? new[] { dependency } : Array.Empty<JobRecord>();
            bool parallel = mode != ScheduleMode.Single && kind != Kind.Single && mode != ScheduleMode.Run;
            var source = (byte*)parameters.JobDataPtr;

            // The containers: valid, not temp memory, not aliased, not used by a job this one does not depend on.
            var handles = new List<(JobLayout.Container Container, int Offset, AtomicSafetyHandle Handle)>();
            foreach (var container in layout.Containers)
            {
                foreach (int offset in container.Handles)
                {
                    var handle = *(AtomicSafetyHandle*)(source + offset);
                    var type = Safety.TypeName(handle.staticSafetyId);
                    if (handle.versionNode == IntPtr.Zero)
                        throw new InvalidOperationException($"The {type} {container.Path} has not been assigned or constructed. All containers must be valid when scheduling a job.");
                    if (!Safety.IsAlive(handle))
                        throw new InvalidOperationException($"The {type} {container.Path} has been deallocated. All containers must be valid when scheduling a job.");
                    if (Safety.IsTemp(handle))
                        throw new InvalidOperationException($"The {type} {container.Path} is allocated with Temp memory. Temp memory containers cannot be used when scheduling a job, use TempJob instead.");
                    if (Safety.IsNested(handle))
                        throw new InvalidOperationException($"The {type} {container.Path} contains nested native containers. Nested native containers are illegal in jobs.");
                    if (parallel && (container.Access & ContainerAccess.Write) != 0 && !container.DisableParallelForRestriction &&
                        !container.AtomicWriteOnly && (!container.SupportsMinMax || container.MinOffset < 0))
                        throw new InvalidOperationException($"{container.Path} is not declared [ReadOnly] in a IJobParallelFor job. The container does not support parallel writing. Please use a more suitable container type.");
                    foreach (var other in handles)
                    {
                        if (other.Handle.versionNode != handle.versionNode) continue;
                        if ((other.Container.Access & ContainerAccess.Write) == 0 && (container.Access & ContainerAccess.Write) == 0) continue;
                        var (writer, second) = (other.Container.Access & ContainerAccess.Write) != 0 ? (other.Container, container) : (container, other.Container);
                        throw new InvalidOperationException($"The writeable {type} {writer.Path} is the same {type} as {second.Path}, two containers may not be the same (aliasing).");
                    }
                    handles.Add((container, offset, handle));
                }
            }
            if (deferredListSafety.versionNode != IntPtr.Zero)
            {
                if (!Safety.IsAlive(deferredListSafety))
                    throw new InvalidOperationException($"The {Safety.TypeName(deferredListSafety.staticSafetyId)} the job's length is deferred to has been deallocated. All containers must be valid when scheduling a job.");
                handles.Add((new JobLayout.Container { Path = reflection.User.Name + ".<deferred list>", Access = ContainerAccess.Read }, -1, deferredListSafety));
            }
            var record = NewRecord(reflection.Name, reflection, dependencies);
            foreach (var (container, _, handle) in handles)
            {
                var error = Safety.CheckSchedule(handle, record, (container.Access & ContainerAccess.Write) != 0, reflection.Name,
                    Safety.TypeName(handle.staticSafetyId), container.Path, previous => IsDependency(dependency, previous));
                if (error != null)
                {
                    Discard(record);
                    throw new InvalidOperationException(error);
                }
            }

            // The job's own copy of its data, its containers patched for it.
            int size = Math.Max(reflection.Size, 1);
            record.Data = (byte*)NativeMemory.AlignedAlloc((nuint)size, 16);
            Buffer.MemoryCopy(source, record.Data, size, reflection.Size);
            foreach (int offset in layout.NullOnSchedule) *(IntPtr*)(record.Data + offset) = IntPtr.Zero;
            foreach (var container in layout.Unchecked)
            {
                foreach (int offset in container.Handles)
                {
                    ref var handle = ref *(AtomicSafetyHandle*)(record.Data + offset);
                    if (handle.versionNode != IntPtr.Zero && Safety.IsAlive(handle)) Safety.PatchForJob(ref handle, record, ContainerAccess.ReadWrite, 0);
                }
            }
            foreach (var (container, offset, handle) in handles)
            {
                bool writes = (container.Access & ContainerAccess.Write) != 0;
                if (offset < 0)
                {
                    Safety.AddJobUse(handle, record, writes, container.Path);
                    continue;
                }
                int deny = AtomicSafetyHandle.Dispose | (writes ? 0 : AtomicSafetyHandle.Write) | ((container.Access & ContainerAccess.Read) != 0 ? 0 : AtomicSafetyHandle.Read);
                Safety.AddJobUse(handle, record, writes, container.Path);
                Safety.PatchForJob(ref *(AtomicSafetyHandle*)(record.Data + offset), record, container.Access, deny);
                if (container.DeallocateOnCompletion && container.BufferOffset >= 0 && container.AllocatorOffset >= 0)
                    record.Deallocations.Add((*(IntPtr*)(source + container.BufferOffset), *(Allocator*)(source + container.AllocatorOffset), handle));
            }
            record.BufferRangePatchData = RangePatchData(layout, parallel || kind == Kind.Transform || kind == Kind.TransformReadOnly);
            if (deferredList != null)
            {
                // The length is the list's when the job starts (PrepareToRun).
                record.DeferredList = deferredList;
                record.DeferredBatchSize = innerloopBatchCount;
                record.Parallel = true;
            }
            else
            {
                PrepareRanges(record, kind, mode, arrayLength, innerloopBatchCount, transforms);
            }

            if (mode == ScheduleMode.Run)
            {
                // Run executes now on this thread (after its dependencies) and is synced at once.
                RunNow(record);
                Sync(record);
                return default;
            }
            lock (s_Lock)
            {
                s_Batch.Add(record);
                if (record.Transform) s_PendingTransformJobs++;
            }
            Trace.Jobs($"scheduled {record.Name} #{record.Id} ({record.Participants} participants, {record.PendingDependencies} pending dependencies)");
            return record.Handle;
        }

        /// <summary>Undoes a record that failed its checks.</summary>
        static void Discard(JobRecord record)
        {
            lock (s_Lock)
                foreach (var dependency in record.Dependencies)
                    dependency.Dependents.Remove(record);
            s_Records.TryRemove(record.Id, out _);
        }

        /// <summary>What PatchBufferMinMaxRanges reads: the count of containers to restrict, then per
        /// container its m_MinIndex and m_MaxIndex offsets (null when there are none).</summary>
        static IntPtr RangePatchData(JobLayout layout, bool parallel)
        {
            if (!parallel) return IntPtr.Zero;
            var offsets = new List<(int Min, int Max)>();
            foreach (var container in layout.Containers)
                if (container.SupportsMinMax && container.MinOffset >= 0 && container.MaxOffset >= 0 && !container.DisableParallelForRestriction &&
                    (container.Access & ContainerAccess.Write) != 0)
                    offsets.Add((container.MinOffset, container.MaxOffset));
            if (offsets.Count == 0) return IntPtr.Zero;
            var data = (int*)NativeMemory.Alloc((nuint)(4 * (1 + 2 * offsets.Count)));
            data[0] = offsets.Count;
            for (int i = 0; i < offsets.Count; i++)
            {
                data[1 + 2 * i] = offsets[i].Min;
                data[2 + 2 * i] = offsets[i].Max;
            }
            return (IntPtr)data;
        }

        internal static void PatchBufferMinMaxRanges(IntPtr bufferRangePatchData, void* jobdata, int startIndex, int rangeSize)
        {
            if (bufferRangePatchData == IntPtr.Zero) return;
            var data = (int*)bufferRangePatchData;
            for (int i = 0; i < data[0]; i++)
            {
                *(int*)((byte*)jobdata + data[1 + 2 * i]) = startIndex;
                *(int*)((byte*)jobdata + data[2 + 2 * i]) = startIndex + rangeSize - 1;
            }
        }

        static void PrepareRanges(JobRecord record, Kind kind, ScheduleMode mode, int arrayLength, int batchSize, IntPtr transforms)
        {
            record.Transform = kind == Kind.Transform || kind == Kind.TransformReadOnly;
            List<(int Begin, int End)> chunks;
            int total;
            if (kind == Kind.Single)
            {
                total = 1;
                batchSize = 1;
                chunks = new List<(int, int)> { (0, 1) };
            }
            else if (kind == Kind.Transform)
            {
                // Transforms of one hierarchy run on one thread: a range per root.
                var array = TransformAccessArrayData.From(transforms);
                record.AdditionalData = array.PrepareForJob(readOnly: false);
                total = array.Count;
                chunks = array.HierarchyRanges(mode == ScheduleMode.Run ? 1 : MaximumWorkers + 1);
                batchSize = Math.Max(1, total);
            }
            else
            {
                if (kind == Kind.TransformReadOnly)
                {
                    var array = TransformAccessArrayData.From(transforms);
                    record.AdditionalData = array.PrepareForJob(readOnly: true);
                    arrayLength = array.Count;
                }
                total = Math.Max(0, arrayLength);
                batchSize = Math.Max(1, batchSize);
                int batches = (total + batchSize - 1) / batchSize;
                int participants = mode == ScheduleMode.Parallel ? Math.Max(1, Math.Min(batches, MaximumWorkers + 1)) : 1;
                chunks = new List<(int, int)>();
                // Contiguous batches per participant; a finished participant steals from the others.
                for (int j = 0; j < participants; j++)
                {
                    int firstBatch = (int)((long)batches * j / participants);
                    int endBatch = (int)((long)batches * (j + 1) / participants);
                    chunks.Add((Math.Min(total, firstBatch * batchSize), Math.Min(total, endBatch * batchSize)));
                }
            }
            int n = chunks.Count;
            record.Parallel = kind != Kind.Single;
            record.Participants = n;
            record.Claimed = new int[n];
            record.Remaining = n;
            var ranges = (JobRanges*)NativeMemory.AllocZeroed((nuint)(sizeof(JobRanges) + 4 * 3 * n));
            int* startEnd = (int*)(ranges + 1);
            int* cursors = startEnd + 2 * n;
            for (int j = 0; j < n; j++)
            {
                startEnd[2 * j] = chunks[j].Begin;
                startEnd[2 * j + 1] = chunks[j].End;
                cursors[j] = chunks[j].Begin;
            }
            ranges->BatchSize = batchSize;
            ranges->NumJobs = n;
            ranges->TotalIterationCount = total;
            ranges->StartEndIndex = (IntPtr)startEnd;
            record.Ranges = ranges;
        }

        /// <summary>The next batch for <paramref name="jobIndex"/>: from its own range, else stolen from
        /// another participant's.</summary>
        internal static bool GetWorkStealingRange(ref JobRanges ranges, int jobIndex, out int beginIndex, out int endIndex)
        {
            int n = ranges.NumJobs;
            int* startEnd = (int*)ranges.StartEndIndex;
            int* cursors = startEnd + 2 * n;
            int batch = Math.Max(1, ranges.BatchSize);
            for (int k = 0; k < n; k++)
            {
                int j = (jobIndex + k) % n;
                int end = startEnd[2 * j + 1];
                if (Volatile.Read(ref cursors[j]) >= end) continue;
                int begin = Interlocked.Add(ref cursors[j], batch) - batch;
                if (begin >= end) continue;
                beginIndex = begin;
                endIndex = Math.Min(begin + batch, end);
                return true;
            }
            beginIndex = endIndex = 0;
            return false;
        }

        // ------------------------------------------------------------------ execution

        /// <summary>Makes the batched jobs available to the workers.</summary>
        internal static void Flush()
        {
            List<JobRecord> batch;
            lock (s_Lock)
            {
                if (s_Batch.Count == 0) return;
                batch = new List<JobRecord>(s_Batch);
                s_Batch.Clear();
                Trace.Jobs($"flush {batch.Count} jobs");
                foreach (var record in batch)
                {
                    record.Flushed = true;
                    if (record.PendingDependencies == 0) EnqueueLocked(record);
                }
            }
        }

        /// <summary>Before the job's first participant runs (its dependencies done): deferred arrays
        /// take their lists' buffers and lengths, and a deferred job its iteration count.</summary>
        static void PrepareToRun(JobRecord record)
        {
            if (record.Reflection == null || Interlocked.Exchange(ref record.Prepared, 1) != 0) return;
            foreach (var container in record.Reflection.Layout.Containers)
            {
                if (!container.DeferredConvert || container.BufferOffset < 0 || container.LengthOffset < 0) continue;
                var buffer = *(IntPtr*)(record.Data + container.BufferOffset);
                if (((long)buffer & 1) == 0) continue;
                // An UnsafeList: its element pointer, then its length.
                var list = (byte*)buffer - 1;
                int length = *(int*)(list + IntPtr.Size);
                *(IntPtr*)(record.Data + container.BufferOffset) = *(IntPtr*)list;
                *(int*)(record.Data + container.LengthOffset) = length;
                if (container.MinOffset >= 0) *(int*)(record.Data + container.MinOffset) = 0;
                if (container.MaxOffset >= 0) *(int*)(record.Data + container.MaxOffset) = length - 1;
            }
            if (record.DeferredList != null)
            {
                int length = *(int*)((byte*)record.DeferredList + IntPtr.Size);
                PrepareRanges(record, Kind.ParallelFor, ScheduleMode.Parallel, length, record.DeferredBatchSize, IntPtr.Zero);
            }
        }

        static void EnqueueLocked(JobRecord record)
        {
            PrepareToRun(record);
            EnsureWorkers();
            for (int j = 0; j < record.Participants; j++) s_Ready.Enqueue((record, j));
            s_Work.Release(Math.Min(record.Participants, MaximumWorkers));
        }

        /// <summary>Runs participant <paramref name="jobIndex"/> of a job unless it was taken.</summary>
        static void Execute(JobRecord record, int jobIndex)
        {
            if (Interlocked.Exchange(ref record.Claimed[jobIndex], 1) != 0) return;
            Trace.Jobs($"run {record.Name} #{record.Id} participant {jobIndex} on thread {Environment.CurrentManagedThreadId}");
            var previous = t_Executing;
            var previousScope = t_JobScope;
            int previousIndex = t_ThreadIndex;
            t_Executing = record;
            t_JobScope = new Safety.TempScope();
            if (!PlayerLoopDriver.IsMainThread && t_ThreadIndex == 0) t_ThreadIndex = 1;
            byte* data = record.Data;
            byte* copy = null;
            try
            {
                if (record.Participants > 1 || record.Reflection.Layout.ThreadIndexOffsets.Count > 0)
                {
                    // Each participant of a parallel job works on its own copy of the job data.
                    int size = Math.Max(record.Reflection.Size, 1);
                    copy = (byte*)NativeMemory.AlignedAlloc((nuint)size, 16);
                    Buffer.MemoryCopy(record.Data, copy, size, size);
                    data = copy;
                }
                foreach (int offset in record.Reflection.Layout.ThreadIndexOffsets) *(int*)(data + offset) = ThreadIndex;
                record.Reflection.Invoke(record.Reflection.Execute, (IntPtr)data, record.AdditionalData, record.BufferRangePatchData, (IntPtr)record.Ranges, jobIndex);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                t_JobScope.End();
                if (copy != null) NativeMemory.AlignedFree(copy);
                t_Executing = previous;
                t_JobScope = previousScope;
                t_ThreadIndex = previousIndex;
            }
            if (Interlocked.Decrement(ref record.Remaining) == 0) Finish(record);
        }

        static void Finish(JobRecord record)
        {
            Trace.Jobs($"finished {record.Name} #{record.Id}");
            foreach (var (buffer, allocator, safety) in record.Deallocations)
            {
                UnsafeUtility.FreeTracked((void*)buffer, allocator);
                AtomicSafetyHandle.Release(safety);
            }
            record.OnFinished?.Invoke();
            lock (s_Lock) FinishLocked(record);
        }

        static void FinishLocked(JobRecord record)
        {
            record.Done = true;
            record.Finished.Set();
            foreach (var dependent in record.Dependents)
            {
                if (--dependent.PendingDependencies != 0) continue;
                if (dependent.Reflection == null)
                {
                    // A combination: done when its dependencies are.
                    FinishLocked(dependent);
                }
                else if (dependent.Flushed)
                {
                    EnqueueLocked(dependent);
                }
            }
        }

        /// <summary>Runs a job and its unstarted dependencies on this thread; waits for what others run.</summary>
        static void RunNow(JobRecord record)
        {
            foreach (var dependency in record.Dependencies) RunNow(dependency);
            if (record.Reflection != null)
            {
                foreach (var dependency in record.Dependencies) dependency.Finished.Wait();
                lock (s_Lock)
                {
                    PrepareToRun(record);
                    if (record.Participants > 1 && !record.Flushed)
                    {
                        // The workers help through the queue while this thread takes participants too.
                        record.Flushed = true;
                        s_Batch.Remove(record);
                        EnqueueLocked(record);
                    }
                }
                for (int j = 0; j < record.Participants; j++) Execute(record, j);
            }
            record.Finished.Wait();
        }

        // ------------------------------------------------------------------ completion

        internal static void Complete(JobHandle handle)
        {
            var record = Find(handle);
            if (record == null) return;
            if (!record.Done)
            {
                lock (s_Lock) s_Batch.Remove(record);
                RunNow(record);
                Flush();
            }
            Sync(record);
        }

        internal static bool IsCompleted(JobHandle handle)
        {
            Flush();
            var record = Find(handle);
            return record == null || record.Done;
        }

        /// <summary>The job (finished) is synced: it and its dependencies release their containers.</summary>
        internal static void Sync(JobRecord record)
        {
            if (!record.Done) Complete(record.Handle);
            if (record.Synced) return;
            record.Synced = true;
            Trace.Jobs($"synced {record.Name} #{record.Id}");
            foreach (var dependency in record.Dependencies) Sync(dependency);
            Safety.JobSynced(record);
            if (record.Transform)
            {
                lock (s_Lock) s_PendingTransformJobs--;
            }
            s_Records.TryRemove(record.Id, out _);
            if (record.Data != null) NativeMemory.AlignedFree(record.Data);
            record.Data = null;
            if (record.Ranges != null) NativeMemory.Free(record.Ranges);
            record.Ranges = null;
            if (record.BufferRangePatchData != IntPtr.Zero) NativeMemory.Free((void*)record.BufferRangePatchData);
            record.BufferRangePatchData = IntPtr.Zero;
            if (record.AdditionalData != IntPtr.Zero)
            {
                if (record.Transform) TransformAccessArrayData.ReleaseJobData(record.AdditionalData);
                NativeMemory.Free((void*)record.AdditionalData);
            }
            record.AdditionalData = IntPtr.Zero;
        }

        internal static void CompleteAll(JobHandle* jobs, int count)
        {
            for (int i = 0; i < count; i++) Complete(jobs[i]);
        }

        /// <summary>Main thread access to transforms waits for the transform jobs, as in Unity.</summary>
        internal static void SyncTransformJobs()
        {
            if (Volatile.Read(ref s_PendingTransformJobs) == 0 || t_Executing != null || !PlayerLoopDriver.IsMainThread) return;
            List<JobRecord> pending = new List<JobRecord>();
            foreach (var record in s_Records.Values)
                if (record.Transform && !record.Synced) pending.Add(record);
            foreach (var record in pending) Complete(record.Handle);
        }

        internal static bool TransformJobsPending => Volatile.Read(ref s_PendingTransformJobs) != 0;
    }
}
