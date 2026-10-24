using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unbound.Runtime.Jobs;

namespace Unity.Jobs
{
    /// <summary>A scheduled job (or a combination of jobs), as Unity's job system hands it out.</summary>
    public partial struct JobHandle : IEquatable<JobHandle>
    {
        internal ulong jobGroup;
        internal int version;
        internal int debugVersion;
        [NativeDisableUnsafePtrRestriction]
        internal IntPtr debugInfo;

        public void Complete()
        {
            if (jobGroup != 0) JobSystem.Complete(this);
        }

        public bool IsCompleted => JobSystem.IsCompleted(this);

        public static unsafe void CompleteAll(ref JobHandle job0, ref JobHandle job1)
        {
            JobHandle* jobs = stackalloc JobHandle[2];
            jobs[0] = job0;
            jobs[1] = job1;
            JobSystem.CompleteAll(jobs, 2);
            job0 = default;
            job1 = default;
        }

        public static unsafe void CompleteAll(ref JobHandle job0, ref JobHandle job1, ref JobHandle job2)
        {
            JobHandle* jobs = stackalloc JobHandle[3];
            jobs[0] = job0;
            jobs[1] = job1;
            jobs[2] = job2;
            JobSystem.CompleteAll(jobs, 3);
            job0 = default;
            job1 = default;
            job2 = default;
        }

        public static unsafe void CompleteAll(NativeArray<JobHandle> jobs) => JobSystem.CompleteAll((JobHandle*)jobs.GetUnsafeReadOnlyPtr(), jobs.Length);

        /// <summary>Makes the jobs scheduled so far available to the worker threads.</summary>
        public static void ScheduleBatchedJobs() => JobSystem.Flush();

        public static unsafe JobHandle CombineDependencies(JobHandle job0, JobHandle job1)
        {
            JobHandle* jobs = stackalloc JobHandle[2];
            jobs[0] = job0;
            jobs[1] = job1;
            return JobSystem.Combine(jobs, 2);
        }

        public static unsafe JobHandle CombineDependencies(JobHandle job0, JobHandle job1, JobHandle job2)
        {
            JobHandle* jobs = stackalloc JobHandle[3];
            jobs[0] = job0;
            jobs[1] = job1;
            jobs[2] = job2;
            return JobSystem.Combine(jobs, 3);
        }

        public static unsafe JobHandle CombineDependencies(NativeArray<JobHandle> jobs) => CombineDependenciesInternalPtr(jobs.GetUnsafeReadOnlyPtr(), jobs.Length);

        public static unsafe JobHandle CombineDependencies(NativeSlice<JobHandle> jobs) => CombineDependenciesInternalPtr(jobs.GetUnsafeReadOnlyPtr(), jobs.Length);

        internal static unsafe JobHandle CombineDependenciesInternalPtr(void* jobs, int count) => JobSystem.Combine((JobHandle*)jobs, count);

        /// <summary>Whether <paramref name="dependsOn"/> is <paramref name="jobHandle"/> or one of its
        /// dependencies, or already synced.</summary>
        public static bool CheckFenceIsDependencyOrDidSyncFence(JobHandle jobHandle, JobHandle dependsOn)
        {
            if (dependsOn.jobGroup == 0 || dependsOn.jobGroup == jobHandle.jobGroup) return true;
            if (dependsOn.IsCompletedAndSynced()) return true;
            return JobSystem.IsDependency(jobHandle, JobSystem.RecordOf(dependsOn));
        }

        bool IsCompletedAndSynced() => JobSystem.RecordOf(this) == null;

        public bool Equals(JobHandle other) => jobGroup == other.jobGroup;

        public override bool Equals(object obj) => obj is JobHandle other && this == other;

        public static bool operator ==(JobHandle a, JobHandle b) => a.jobGroup == b.jobGroup;

        public static bool operator !=(JobHandle a, JobHandle b) => !(a == b);

        public override int GetHashCode() => jobGroup.GetHashCode();
    }

    [JobProducerType(typeof(IJobExtensions.JobStruct<>))]
    public partial interface IJob
    {
        void Execute();
    }

    [JobProducerType(typeof(IJobForExtensions.ForJobStruct<>))]
    public partial interface IJobFor
    {
        void Execute(int index);
    }

    [JobProducerType(typeof(IJobParallelForExtensions.ParallelForJobStruct<>))]
    public partial interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static partial class IJobExtensions
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        internal partial struct JobStruct<T> where T : struct, IJob
        {
            internal delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            internal static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<JobStruct<T>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
            }

            public static void Execute(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex) => data.Execute();
        }

        public static void EarlyJobInit<T>() where T : struct, IJob => JobStruct<T>.Initialize();

        static IntPtr GetReflectionData<T>() where T : struct, IJob
        {
            JobStruct<T>.Initialize();
            var data = JobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default) where T : struct, IJob
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Single);
            return JobsUtility.Schedule(ref parameters);
        }

        public static unsafe void Run<T>(this T jobData) where T : struct, IJob
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.Schedule(ref parameters);
        }

        public static unsafe JobHandle ScheduleByRef<T>(this ref T jobData, JobHandle dependsOn = default) where T : struct, IJob
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Single);
            return JobsUtility.Schedule(ref parameters);
        }

        public static unsafe void RunByRef<T>(this ref T jobData) where T : struct, IJob
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.Schedule(ref parameters);
        }
    }

    public static partial class IJobForExtensions
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        internal partial struct ForJobStruct<T> where T : struct, IJobFor
        {
            public delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            internal static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<ForJobStruct<T>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
            }

            public static unsafe void Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                while (JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
                {
                    JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), begin, end - begin);
                    for (int i = begin; i < end; i++) jobData.Execute(i);
                }
            }
        }

        public static void EarlyJobInit<T>() where T : struct, IJobFor => ForJobStruct<T>.Initialize();

        static IntPtr GetReflectionData<T>() where T : struct, IJobFor
        {
            ForJobStruct<T>.Initialize();
            var data = ForJobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, int arrayLength, JobHandle dependency) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependency, ScheduleMode.Single);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }

        public static unsafe JobHandle ScheduleParallel<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependency) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependency, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, innerloopBatchCount);
        }

        public static unsafe void Run<T>(this T jobData, int arrayLength) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }

        public static unsafe JobHandle ScheduleByRef<T>(this ref T jobData, int arrayLength, JobHandle dependency) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependency, ScheduleMode.Single);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }

        public static unsafe JobHandle ScheduleParallelByRef<T>(this ref T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependency) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependency, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, innerloopBatchCount);
        }

        public static unsafe void RunByRef<T>(this ref T jobData, int arrayLength) where T : struct, IJobFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }
    }

    public static partial class IJobParallelForExtensions
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        internal partial struct ParallelForJobStruct<T> where T : struct, IJobParallelFor
        {
            public delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            internal static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<ParallelForJobStruct<T>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
            }

            public static unsafe void Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                while (JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
                {
                    JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), begin, end - begin);
                    for (int i = begin; i < end; i++) jobData.Execute(i);
                }
            }
        }

        public static void EarlyJobInit<T>() where T : struct, IJobParallelFor => ParallelForJobStruct<T>.Initialize();

        static IntPtr GetReflectionData<T>() where T : struct, IJobParallelFor
        {
            ParallelForJobStruct<T>.Initialize();
            var data = ParallelForJobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, innerloopBatchCount);
        }

        public static unsafe void Run<T>(this T jobData, int arrayLength) where T : struct, IJobParallelFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }

        public static unsafe JobHandle ScheduleByRef<T>(this ref T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, innerloopBatchCount);
        }

        public static unsafe void RunByRef<T>(this ref T jobData, int arrayLength) where T : struct, IJobParallelFor
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref parameters, arrayLength, arrayLength);
        }
    }

    internal static partial class JobValidationInternal
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckReflectionDataCorrect<T>(IntPtr reflectionData)
        {
            bool burstCompiled = true;
            CheckReflectionDataCorrectInternal<T>(reflectionData, ref burstCompiled);
            if (burstCompiled && reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by an Initialize() call. Support for burst compiled calls to Schedule depends on the Collections package.\n\nFor generic job types, please include [assembly: RegisterGenericJobType(typeof(MyJob<MyJobSpecialization>))] in your source file.");
        }

        [BurstDiscard]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckReflectionDataCorrectInternal<T>(IntPtr reflectionData, ref bool burstCompiled)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException($"Reflection data was not set up by an Initialize() call. Support for burst compiled calls to Schedule depends on the Collections package.\n\nFor generic job types, please include [assembly: RegisterGenericJobType(typeof({typeof(T)}))] in your source file.");
            burstCompiled = false;
        }
    }
}

namespace Unity.Jobs.LowLevel.Unsafe
{
    public partial struct JobRanges
    {
        internal int BatchSize;
        internal int NumJobs;
        public int TotalIterationCount;
        internal IntPtr StartEndIndex;
    }

    public enum ScheduleMode
    {
        Run = 0,
        [Obsolete("Batched is obsolete, use Parallel or Single depending on job type. (UnityUpgradable) -> Parallel", false)]
        Batched = 1,
        Parallel = 1,
        Single = 2,
    }

    [Obsolete("Reflection data is now universal between job types. The parameter can be removed.", false)]
    public enum JobType
    {
        Single = 0,
        ParallelFor = 1,
    }

    /// <summary>Creating, scheduling and introspecting jobs (see <see cref="JobSystem"/>).</summary>
    public static unsafe partial class JobsUtility
    {
        public partial struct JobScheduleParameters
        {
            public JobHandle Dependency;
            public int ScheduleMode;
            public IntPtr ReflectionData;
            public IntPtr JobDataPtr;

            public JobScheduleParameters(void* i_jobData, IntPtr i_reflectionData, JobHandle i_dependency, ScheduleMode i_scheduleMode)
            {
                Dependency = i_dependency;
                JobDataPtr = (IntPtr)i_jobData;
                ReflectionData = i_reflectionData;
                ScheduleMode = (int)i_scheduleMode;
            }
        }

        internal delegate void PanicFunction_();

        public const int MaxJobThreadCount = 128;
        public const int CacheLineSize = 64;

        internal static PanicFunction_ PanicFunction;

        public static bool IsExecutingJob => JobSystem.IsExecutingJob;

        public static bool JobDebuggerEnabled { get; set; } = true;

        public static bool JobCompilerEnabled { get; set; } = true;

        public static int JobWorkerMaximumCount => JobSystem.MaximumWorkers;

        public static int JobWorkerCount
        {
            get => JobSystem.ActiveWorkers;
            set
            {
                if (value < 0 || value > JobWorkerMaximumCount)
                    throw new ArgumentOutOfRangeException(nameof(JobWorkerCount), $"Invalid JobWorkerCount {value} must be in the range 0 -> {JobWorkerMaximumCount}");
                JobSystem.ActiveWorkers = value;
            }
        }

        public static void ResetJobWorkerCount() => JobSystem.ActiveWorkers = JobSystem.MaximumWorkers;

        public static int ThreadIndex => JobSystem.ThreadIndex;

        public static int ThreadIndexCount => JobSystem.ThreadIndexCount;

        internal static bool JobBatchingEnabled => true;

        public static void GetJobRange(ref JobRanges ranges, int jobIndex, out int beginIndex, out int endIndex)
        {
            int* startEnd = (int*)ranges.StartEndIndex;
            beginIndex = startEnd[jobIndex * 2];
            endIndex = startEnd[jobIndex * 2 + 1];
        }

        public static bool GetWorkStealingRange(ref JobRanges ranges, int jobIndex, out int beginIndex, out int endIndex) =>
            JobSystem.GetWorkStealingRange(ref ranges, jobIndex, out beginIndex, out endIndex);

        public static JobHandle Schedule(ref JobScheduleParameters parameters) => JobSystem.Schedule(ref parameters, JobSystem.Kind.Single, 1, 1);

        public static JobHandle ScheduleParallelFor(ref JobScheduleParameters parameters, int arrayLength, int innerloopBatchCount) =>
            JobSystem.Schedule(ref parameters, JobSystem.Kind.ParallelFor, arrayLength, innerloopBatchCount);

        /// <summary>A parallel-for over a list whose length is read when the job starts (a job
        /// before it may fill the list): <paramref name="listData"/> is the list's UnsafeList.</summary>
        public static JobHandle ScheduleParallelForDeferArraySize(ref JobScheduleParameters parameters, int innerloopBatchCount, void* listData, void* listDataAtomicSafetyHandle) =>
            JobSystem.ScheduleDeferred(ref parameters, innerloopBatchCount, listData, listDataAtomicSafetyHandle);

        public static JobHandle ScheduleParallelForTransform(ref JobScheduleParameters parameters, IntPtr transfromAccesssArray) =>
            JobSystem.Schedule(ref parameters, JobSystem.Kind.Transform, 0, 1, transfromAccesssArray);

        public static JobHandle ScheduleParallelForTransformReadOnly(ref JobScheduleParameters parameters, IntPtr transfromAccesssArray, int innerloopBatchCount) =>
            JobSystem.Schedule(ref parameters, JobSystem.Kind.TransformReadOnly, 0, innerloopBatchCount, transfromAccesssArray);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void PatchBufferMinMaxRanges(IntPtr bufferRangePatchData, void* jobdata, int startIndex, int rangeSize) =>
            JobSystem.PatchBufferMinMaxRanges(bufferRangePatchData, jobdata, startIndex, rangeSize);

        [Obsolete("JobType is obsolete. The parameter should be removed. (UnityUpgradable) -> !1")]
        public static IntPtr CreateJobReflectionData(Type type, JobType jobType, object managedJobFunction0, object managedJobFunction1 = null, object managedJobFunction2 = null) =>
            CreateJobReflectionData(type, type, managedJobFunction0, managedJobFunction1, managedJobFunction2);

        public static IntPtr CreateJobReflectionData(Type type, object managedJobFunction0, object managedJobFunction1 = null, object managedJobFunction2 = null) =>
            CreateJobReflectionData(type, type, managedJobFunction0, managedJobFunction1, managedJobFunction2);

        [Obsolete("JobType is obsolete. The parameter should be removed. (UnityUpgradable) -> !2")]
        public static IntPtr CreateJobReflectionData(Type wrapperJobType, Type userJobType, JobType jobType, object managedJobFunction0) =>
            CreateJobReflectionData(wrapperJobType, userJobType, managedJobFunction0, null, null);

        public static IntPtr CreateJobReflectionData(Type wrapperJobType, Type userJobType, object managedJobFunction0) =>
            CreateJobReflectionData(wrapperJobType, userJobType, managedJobFunction0, null, null);

        static IntPtr CreateJobReflectionData(Type wrapperJobType, Type userJobType, object managedJobFunction0, object managedJobFunction1, object managedJobFunction2)
        {
            if (managedJobFunction1 != null || managedJobFunction2 != null)
                throw new NotSupportedException("Unbound: job reflection data with more than one managed function is not supported");
            return JobSystem.CreateReflectionData(wrapperJobType, userJobType, managedJobFunction0);
        }

        static readonly IntPtr s_SystemIdCell = (IntPtr)NativeMemory.AllocZeroed(sizeof(int));

        /// <summary>The cell the job debugger reads the current system id from (Entities).</summary>
        internal static IntPtr GetSystemIdCellPtr() => s_SystemIdCell;

        internal static void ClearSystemIds() => *(int*)s_SystemIdCell = 0;

        internal static int GetSystemIdMappings(JobHandle* handles, int* systemIds, int maxCount) => 0;
    }

    public static partial class JobHandleUnsafeUtility
    {
        public static unsafe JobHandle CombineDependencies(JobHandle* jobs, int count) => JobHandle.CombineDependenciesInternalPtr(jobs, count);
    }

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    public partial struct BatchQueryJobStruct<T> where T : struct
    {
        internal static IntPtr jobReflectionData;

        public static IntPtr Initialize()
        {
            if (jobReflectionData == IntPtr.Zero) jobReflectionData = JobsUtility.CreateJobReflectionData(typeof(T), null);
            return jobReflectionData;
        }
    }

    public partial struct BatchQueryJob<CommandT, ResultT> where CommandT : struct where ResultT : struct
    {
        [ReadOnly]
        internal NativeArray<CommandT> commands;
        internal NativeArray<ResultT> results;

        public BatchQueryJob(NativeArray<CommandT> commands, NativeArray<ResultT> results)
        {
            this.commands = commands;
            this.results = results;
        }
    }
}
