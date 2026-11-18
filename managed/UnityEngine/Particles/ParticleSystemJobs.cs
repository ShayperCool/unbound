using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

// The particle job API: what the release's managed assemblies carry, over the particle system's job
// data (ParticleSystem.Jobs.cs). A job scheduled on a system runs on its particles as a
// structure of arrays, after the system's update, and may be scheduled only while the system's
// OnParticleUpdateJobScheduled message runs.

namespace UnityEngine.ParticleSystemJobs
{
    [JobProducerType(typeof(ParticleSystemJobStruct<>))]
    public partial interface IJobParticleSystem
    {
        void Execute(ParticleSystemJobData jobData);
    }

    [JobProducerType(typeof(ParticleSystemParallelForJobStruct<>))]
    public partial interface IJobParticleSystemParallelFor
    {
        void Execute(ParticleSystemJobData jobData, int index);
    }

    [JobProducerType(typeof(ParticleSystemParallelForBatchJobStruct<>))]
    public partial interface IJobParticleSystemParallelForBatch
    {
        void Execute(ParticleSystemJobData jobData, int startIndex, int count);
    }

    public static partial class IJobParticleSystemExtensions
    {
        public static void EarlyJobInit<T>() where T : struct, IJobParticleSystem => ParticleSystemJobStruct<T>.Initialize();

        internal static IntPtr GetReflectionData<T>() where T : struct, IJobParticleSystem
        {
            ParticleSystemJobStruct<T>.Initialize();
            var data = ParticleSystemJobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }
    }

    public static partial class IJobParticleSystemParallelForExtensions
    {
        public static void EarlyJobInit<T>() where T : struct, IJobParticleSystemParallelFor => ParticleSystemParallelForJobStruct<T>.Initialize();

        internal static IntPtr GetReflectionData<T>() where T : struct, IJobParticleSystemParallelFor
        {
            ParticleSystemParallelForJobStruct<T>.Initialize();
            var data = ParticleSystemParallelForJobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }
    }

    public static partial class IJobParticleSystemParallelForBatchExtensions
    {
        public static void EarlyJobInit<T>() where T : struct, IJobParticleSystemParallelForBatch => ParticleSystemParallelForBatchJobStruct<T>.Initialize();

        internal static IntPtr GetReflectionData<T>() where T : struct, IJobParticleSystemParallelForBatch
        {
            ParticleSystemParallelForBatchJobStruct<T>.Initialize();
            var data = ParticleSystemParallelForBatchJobStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }
    }

    internal static partial class ParticleSystemJobUtility
    {
        /// <summary>A job on a system runs after the system's jobs scheduled before it.</summary>
        internal static unsafe JobsUtility.JobScheduleParameters CreateScheduleParams<T>(ref T jobData, ParticleSystem ps, JobHandle dependsOn, IntPtr jobReflectionData) where T : struct
        {
            dependsOn = JobHandle.CombineDependencies(ps.GetManagedJobHandle(), dependsOn);
            return new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), jobReflectionData, dependsOn, ScheduleMode.Parallel);
        }
    }

    public static partial class IParticleSystemJobExtensions
    {
        static readonly string k_UserJobScheduledOutsideOfCallbackErrorMsg = "Particle System jobs can only be scheduled in MonoBehaviour.OnParticleUpdateJobScheduled()";

        public static unsafe JobHandle Schedule<T>(this T jobData, ParticleSystem ps, JobHandle dependsOn = default) where T : struct, IJobParticleSystem
        {
            if (!ParticleSystem.UserJobCanBeScheduled()) throw new InvalidOperationException(k_UserJobScheduledOutsideOfCallbackErrorMsg);
            var parameters = ParticleSystemJobUtility.CreateScheduleParams(ref jobData, ps, dependsOn, IJobParticleSystemExtensions.GetReflectionData<T>());
            var handle = ParticleSystem.ScheduleManagedJob(ref parameters, ps.GetManagedJobData());
            ps.SetManagedJobHandle(handle);
            return handle;
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, ParticleSystem ps, int minIndicesPerJobCount, JobHandle dependsOn = default) where T : struct, IJobParticleSystemParallelFor
        {
            if (!ParticleSystem.UserJobCanBeScheduled()) throw new InvalidOperationException(k_UserJobScheduledOutsideOfCallbackErrorMsg);
            var parameters = ParticleSystemJobUtility.CreateScheduleParams(ref jobData, ps, dependsOn, IJobParticleSystemParallelForExtensions.GetReflectionData<T>());
            var handle = JobsUtility.ScheduleParallelForDeferArraySize(ref parameters, minIndicesPerJobCount, ps.GetManagedJobData(), null);
            ps.SetManagedJobHandle(handle);
            return handle;
        }

        public static unsafe JobHandle ScheduleBatch<T>(this T jobData, ParticleSystem ps, int innerLoopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParticleSystemParallelForBatch
        {
            if (!ParticleSystem.UserJobCanBeScheduled()) throw new InvalidOperationException(k_UserJobScheduledOutsideOfCallbackErrorMsg);
            var parameters = ParticleSystemJobUtility.CreateScheduleParams(ref jobData, ps, dependsOn, IJobParticleSystemParallelForBatchExtensions.GetReflectionData<T>());
            var handle = JobsUtility.ScheduleParallelForDeferArraySize(ref parameters, innerLoopBatchCount, ps.GetManagedJobData(), null);
            ps.SetManagedJobHandle(handle);
            return handle;
        }
    }

    /// <summary>The list a particle job's ranges come from: the system's job data and its particle
    /// count (an UnsafeList's layout, so the deferred parallel-for reads the length after it).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct NativeListData
    {
        public void* system;
        public int length;
        public int capacity;
    }

    /// <summary>Pointers to the particles' arrays, one float array per component.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct NativeParticleData
    {
        internal int count;
        internal Array3 positions;
        internal Array3 velocities;
        internal Array3 axisOfRotations;
        internal Array3 rotations;
        internal Array3 rotationalSpeeds;
        internal Array3 sizes;
        internal void* startColors;
        internal void* aliveTimePercent;
        internal void* inverseStartLifetimes;
        internal void* randomSeeds;
        internal Array4 customData1;
        internal Array4 customData2;
        internal void* meshIndices;

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe partial struct Array3
        {
            internal float* x;
            internal float* y;
            internal float* z;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe partial struct Array4
        {
            internal float* x;
            internal float* y;
            internal float* z;
            internal float* w;
        }
    }

    public partial struct ParticleSystemNativeArray3
    {
        public NativeArray<float> x;
        public NativeArray<float> y;
        public NativeArray<float> z;

        public Vector3 this[int index]
        {
            get => new Vector3(x[index], y[index], z[index]);
            set
            {
                x[index] = value.x;
                y[index] = value.y;
                z[index] = value.z;
            }
        }
    }

    public partial struct ParticleSystemNativeArray4
    {
        public NativeArray<float> x;
        public NativeArray<float> y;
        public NativeArray<float> z;
        public NativeArray<float> w;

        public Vector4 this[int index]
        {
            get => new Vector4(x[index], y[index], z[index], w[index]);
            set
            {
                x[index] = value.x;
                y[index] = value.y;
                z[index] = value.z;
                w[index] = value.w;
            }
        }
    }

    /// <summary>A job's view of the particles: arrays over the system's job data, under one
    /// safety handle the job releases when it ends.</summary>
    public unsafe partial struct ParticleSystemJobData
    {
        internal AtomicSafetyHandle m_Safety;

        public int count { get; }
        public ParticleSystemNativeArray3 positions { get; }
        public ParticleSystemNativeArray3 velocities { get; }
        public ParticleSystemNativeArray3 axisOfRotations { get; }
        public ParticleSystemNativeArray3 rotations { get; }
        public ParticleSystemNativeArray3 rotationalSpeeds { get; }
        public ParticleSystemNativeArray3 sizes { get; }
        public NativeArray<Color32> startColors { get; }
        public NativeArray<float> aliveTimePercent { get; }
        public NativeArray<float> inverseStartLifetimes { get; }
        public NativeArray<uint> randomSeeds { get; }
        public ParticleSystemNativeArray4 customData1 { get; }
        public ParticleSystemNativeArray4 customData2 { get; }
        public NativeArray<int> meshIndices { get; }

        internal ParticleSystemJobData(ref NativeParticleData nativeData) : this()
        {
            m_Safety = AtomicSafetyHandle.Create();
            count = nativeData.count;
            positions = CreateNativeArray3(ref nativeData.positions, count);
            velocities = CreateNativeArray3(ref nativeData.velocities, count);
            axisOfRotations = CreateNativeArray3(ref nativeData.axisOfRotations, count);
            rotations = CreateNativeArray3(ref nativeData.rotations, count);
            rotationalSpeeds = CreateNativeArray3(ref nativeData.rotationalSpeeds, count);
            sizes = CreateNativeArray3(ref nativeData.sizes, count);
            startColors = CreateNativeArray<Color32>(nativeData.startColors, count);
            aliveTimePercent = CreateNativeArray<float>(nativeData.aliveTimePercent, count);
            inverseStartLifetimes = CreateNativeArray<float>(nativeData.inverseStartLifetimes, count);
            randomSeeds = CreateNativeArray<uint>(nativeData.randomSeeds, count);
            customData1 = CreateNativeArray4(ref nativeData.customData1, count);
            customData2 = CreateNativeArray4(ref nativeData.customData2, count);
            meshIndices = CreateNativeArray<int>(nativeData.meshIndices, count);
        }

        internal NativeArray<T> CreateNativeArray<T>(void* src, int count) where T : struct
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(src, count, Allocator.Invalid);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            return array;
        }

        internal ParticleSystemNativeArray3 CreateNativeArray3(ref NativeParticleData.Array3 ptrs, int count) => new ParticleSystemNativeArray3
        {
            x = CreateNativeArray<float>(ptrs.x, count),
            y = CreateNativeArray<float>(ptrs.y, count),
            z = CreateNativeArray<float>(ptrs.z, count),
        };

        internal ParticleSystemNativeArray4 CreateNativeArray4(ref NativeParticleData.Array4 ptrs, int count) => new ParticleSystemNativeArray4
        {
            x = CreateNativeArray<float>(ptrs.x, count),
            y = CreateNativeArray<float>(ptrs.y, count),
            z = CreateNativeArray<float>(ptrs.z, count),
            w = CreateNativeArray<float>(ptrs.w, count),
        };
    }

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct ParticleSystemJobStruct<T> where T : struct, IJobParticleSystem
    {
        public delegate void ExecuteJobFunction(ref T data, IntPtr listDataPtr, IntPtr unusedPtr, ref JobRanges ranges, int jobIndex);

        public static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<ParticleSystemJobStruct<T>>();

        [BurstDiscard]
        public static void Initialize()
        {
            if (jobReflectionData.Data == IntPtr.Zero)
                jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
        }

        public static unsafe void Execute(ref T data, IntPtr listDataPtr, IntPtr unusedPtr, ref JobRanges ranges, int jobIndex)
        {
            ParticleSystem.CopyManagedJobData(((NativeListData*)listDataPtr)->system, out var particleData);
            var jobData = new ParticleSystemJobData(ref particleData);
            data.Execute(jobData);
            // The job is over: its arrays may no longer be used.
            AtomicSafetyHandle.CheckDeallocateAndThrow(jobData.m_Safety);
            AtomicSafetyHandle.Release(jobData.m_Safety);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct ParticleSystemParallelForJobStruct<T> where T : struct, IJobParticleSystemParallelFor
    {
        public delegate void ExecuteJobFunction(ref T data, IntPtr listDataPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

        public static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<ParticleSystemParallelForJobStruct<T>>();

        [BurstDiscard]
        public static void Initialize()
        {
            if (jobReflectionData.Data == IntPtr.Zero)
                jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
        }

        public static unsafe void Execute(ref T data, IntPtr listDataPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
        {
            ParticleSystem.CopyManagedJobData(((NativeListData*)listDataPtr)->system, out var particleData);
            var jobData = new ParticleSystemJobData(ref particleData);
            while (JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
            {
                JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref data), begin, end - begin);
                for (int i = begin; i < end; i++) data.Execute(jobData, i);
            }
            // The job is over: its arrays may no longer be used.
            AtomicSafetyHandle.CheckDeallocateAndThrow(jobData.m_Safety);
            AtomicSafetyHandle.Release(jobData.m_Safety);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct ParticleSystemParallelForBatchJobStruct<T> where T : struct, IJobParticleSystemParallelForBatch
    {
        public delegate void ExecuteJobFunction(ref T data, IntPtr listDataPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

        public static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<ParticleSystemParallelForBatchJobStruct<T>>();

        [BurstDiscard]
        public static void Initialize()
        {
            if (jobReflectionData.Data == IntPtr.Zero)
                jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
        }

        public static unsafe void Execute(ref T data, IntPtr listDataPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
        {
            ParticleSystem.CopyManagedJobData(((NativeListData*)listDataPtr)->system, out var particleData);
            var jobData = new ParticleSystemJobData(ref particleData);
            while (JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
            {
                JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref data), begin, end - begin);
                data.Execute(jobData, begin, end - begin);
            }
            // The job is over: its arrays may no longer be used.
            AtomicSafetyHandle.CheckDeallocateAndThrow(jobData.m_Safety);
            AtomicSafetyHandle.Release(jobData.m_Safety);
        }
    }
}
