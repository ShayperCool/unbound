using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unbound.Runtime.Jobs;

namespace UnityEngine.Jobs
{
    [JobProducerType(typeof(IJobParallelForTransformExtensions.TransformParallelForLoopStruct<>))]
    public partial interface IJobParallelForTransform
    {
        void Execute(int index, TransformAccess transform);
    }

    public static partial class IJobParallelForTransformExtensions
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        internal partial struct TransformParallelForLoopStruct<T> where T : struct, IJobParallelForTransform
        {
            struct TransformJobData
            {
                public IntPtr TransformAccessArray;
                public int IsReadOnly;
            }

            public delegate void ExecuteJobFunction(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            internal static readonly BurstLike.SharedStatic<IntPtr> jobReflectionData = BurstLike.SharedStatic<IntPtr>.GetOrCreate<TransformParallelForLoopStruct<T>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), new ExecuteJobFunction(Execute));
            }

            public static unsafe void Execute(ref T jobData, IntPtr jobData2, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                UnsafeUtility.CopyPtrToStructure<TransformJobData>((void*)jobData2, out var data);
                int* sortedToUser = (int*)TransformAccessArray.GetSortedToUserIndex(data.TransformAccessArray);
                var access = (TransformAccess*)TransformAccessArray.GetSortedTransformAccess(data.TransformAccessArray);
                if (data.IsReadOnly == 1)
                {
                    while (JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
                    {
                        for (int i = begin; i < end; i++)
                        {
                            int userIndex = sortedToUser[i];
                            JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), userIndex, 1);
                            var transform = access[i];
                            transform.MarkReadOnly();
                            jobData.Execute(userIndex, transform);
                        }
                    }
                }
                else
                {
                    JobsUtility.GetJobRange(ref ranges, jobIndex, out int begin, out int end);
                    for (int i = begin; i < end; i++)
                    {
                        int userIndex = sortedToUser[i];
                        JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), userIndex, 1);
                        var transform = access[i];
                        transform.MarkReadWrite();
                        jobData.Execute(userIndex, transform);
                    }
                }
            }
        }

        public static void EarlyJobInit<T>() where T : struct, IJobParallelForTransform => TransformParallelForLoopStruct<T>.Initialize();

        static IntPtr GetReflectionData<T>() where T : struct, IJobParallelForTransform
        {
            TransformParallelForLoopStruct<T>.Initialize();
            var data = TransformParallelForLoopStruct<T>.jobReflectionData.Data;
            JobValidationInternal.CheckReflectionDataCorrect<T>(data);
            return data;
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, TransformAccessArray transforms, JobHandle dependsOn = default) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelForTransform(ref parameters, transforms.GetTransformAccessArrayForSchedule());
        }

        public static unsafe JobHandle ScheduleReadOnly<T>(this T jobData, TransformAccessArray transforms, int batchSize, JobHandle dependsOn = default) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelForTransformReadOnly(ref parameters, transforms.GetTransformAccessArrayForSchedule(), batchSize);
        }

        public static unsafe void RunReadOnly<T>(this T jobData, TransformAccessArray transforms) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelForTransformReadOnly(ref parameters, transforms.GetTransformAccessArrayForSchedule(), transforms.length);
        }

        public static unsafe JobHandle ScheduleByRef<T>(this ref T jobData, TransformAccessArray transforms, JobHandle dependsOn = default) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelForTransform(ref parameters, transforms.GetTransformAccessArrayForSchedule());
        }

        public static unsafe JobHandle ScheduleReadOnlyByRef<T>(this ref T jobData, TransformAccessArray transforms, int batchSize, JobHandle dependsOn = default) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), dependsOn, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelForTransformReadOnly(ref parameters, transforms.GetTransformAccessArrayForSchedule(), batchSize);
        }

        public static unsafe void RunReadOnlyByRef<T>(this ref T jobData, TransformAccessArray transforms) where T : struct, IJobParallelForTransform
        {
            var parameters = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), GetReflectionData<T>(), default, ScheduleMode.Run);
            JobsUtility.ScheduleParallelForTransformReadOnly(ref parameters, transforms.GetTransformAccessArrayForSchedule(), transforms.length);
        }
    }

    /// <summary>A transform as a transform job sees it: the job's snapshot of the array and an index.</summary>
    public partial struct TransformAccess
    {
        IntPtr hierarchy;
        int index;
        bool isReadOnly;

        internal TransformAccess(IntPtr snapshot, int index)
        {
            hierarchy = snapshot;
            this.index = index;
            isReadOnly = false;
        }

        Transform Target => ((Transform[])GCHandle.FromIntPtr(hierarchy).Target)[index];

        public Vector3 position
        {
            get
            {
                CheckHierarchyValid();
                return Target.position;
            }
            set
            {
                CheckHierarchyValid();
                CheckWriteAccess();
                Target.position = value;
            }
        }

        public Quaternion rotation
        {
            get
            {
                CheckHierarchyValid();
                return Target.rotation;
            }
            set
            {
                CheckHierarchyValid();
                CheckWriteAccess();
                Target.rotation = value;
            }
        }

        public Vector3 localPosition
        {
            get
            {
                CheckHierarchyValid();
                return Target.localPosition;
            }
            set
            {
                CheckHierarchyValid();
                CheckWriteAccess();
                Target.localPosition = value;
            }
        }

        public Quaternion localRotation
        {
            get
            {
                CheckHierarchyValid();
                return Target.localRotation;
            }
            set
            {
                CheckHierarchyValid();
                CheckWriteAccess();
                Target.localRotation = value;
            }
        }

        public Vector3 localScale
        {
            get
            {
                CheckHierarchyValid();
                return Target.localScale;
            }
            set
            {
                CheckHierarchyValid();
                CheckWriteAccess();
                Target.localScale = value;
            }
        }

        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                CheckHierarchyValid();
                return Target.localToWorldMatrix;
            }
        }

        public Matrix4x4 worldToLocalMatrix
        {
            get
            {
                CheckHierarchyValid();
                return Target.worldToLocalMatrix;
            }
        }

        public bool isValid => hierarchy != IntPtr.Zero && (object)Target != null && !Target.m_Destroyed;

        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            CheckHierarchyValid();
            Target.SetPositionAndRotation(position, rotation);
        }

        public void SetLocalPositionAndRotation(Vector3 localPosition, Quaternion localRotation)
        {
            CheckHierarchyValid();
            Target.SetLocalPositionAndRotation(localPosition, localRotation);
        }

        public void GetPositionAndRotation(out Vector3 position, out Quaternion rotation)
        {
            CheckHierarchyValid();
            Target.GetPositionAndRotation(out position, out rotation);
        }

        public void GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation)
        {
            CheckHierarchyValid();
            Target.GetLocalPositionAndRotation(out localPosition, out localRotation);
        }

        internal void CheckHierarchyValid()
        {
            if (!isValid) throw new NullReferenceException("The TransformAccess is not valid and points to an invalid hierarchy");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void MarkReadWrite() => isReadOnly = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void MarkReadOnly() => isReadOnly = true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWriteAccess()
        {
            if (isReadOnly) throw new InvalidOperationException("Cannot write to TransformAccess since the transform job was scheduled as read-only");
        }
    }

    /// <summary>The transforms a transform job runs over.</summary>
    public partial struct TransformAccessArray : IDisposable
    {
        IntPtr m_TransformArray;
        AtomicSafetyHandle m_Safety;

        public TransformAccessArray(Transform[] transforms, int desiredJobCount = -1)
        {
            Allocate(transforms.Length, desiredJobCount, out this);
            TransformAccessArrayData.From(m_TransformArray).SetTransforms(transforms);
        }

        public TransformAccessArray(int capacity, int desiredJobCount = -1)
        {
            Allocate(capacity, desiredJobCount, out this);
        }

        public static void Allocate(int capacity, int desiredJobCount, out TransformAccessArray array)
        {
            array.m_TransformArray = TransformAccessArrayData.Create(capacity, desiredJobCount);
            UnsafeUtility.LeakRecord(array.m_TransformArray, LeakCategory.TransformAccessArray, 0);
            AtomicSafetyHandle.CreateHandle(out array.m_Safety, Allocator.Persistent);
        }

        public bool isCreated => m_TransformArray != IntPtr.Zero;

        public void Dispose()
        {
            UnsafeUtility.LeakErase(m_TransformArray, LeakCategory.TransformAccessArray);
            AtomicSafetyHandle.DisposeHandle(ref m_Safety);
            TransformAccessArrayData.Destroy(m_TransformArray);
            m_TransformArray = IntPtr.Zero;
        }

        internal IntPtr GetTransformAccessArrayForSchedule()
        {
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            return m_TransformArray;
        }

        public Transform this[int index]
        {
            get
            {
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                return GetTransform(m_TransformArray, index);
            }
            set
            {
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                SetTransform(m_TransformArray, index, value);
            }
        }

        public int capacity
        {
            get
            {
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                return GetCapacity(m_TransformArray);
            }
            set
            {
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                SetCapacity(m_TransformArray, value);
            }
        }

        public int length
        {
            get
            {
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                return GetLength(m_TransformArray);
            }
        }

        public void Add(Transform transform)
        {
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            TransformAccessArrayData.From(m_TransformArray).Add(transform);
        }

        public void Add(int instanceId)
        {
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            TransformAccessArrayData.From(m_TransformArray).Add(Unbound.Runtime.ObjectRegistry.FindById(instanceId) as Transform);
        }

        public void RemoveAtSwapBack(int index)
        {
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            TransformAccessArrayData.From(m_TransformArray).RemoveAtSwapBack(index);
        }

        public void SetTransforms(Transform[] transforms)
        {
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            TransformAccessArrayData.From(m_TransformArray).SetTransforms(transforms);
        }

        internal static unsafe IntPtr GetSortedTransformAccess(IntPtr transformArrayIntPtr) => TransformAccessArrayData.SnapshotAccess(transformArrayIntPtr);

        internal static unsafe IntPtr GetSortedToUserIndex(IntPtr transformArrayIntPtr) => TransformAccessArrayData.SnapshotSortedToUser(transformArrayIntPtr);

        internal static int GetLength(IntPtr transformArrayIntPtr) => TransformAccessArrayData.From(transformArrayIntPtr).Count;

        internal static int GetCapacity(IntPtr transformArrayIntPtr) => TransformAccessArrayData.From(transformArrayIntPtr).Capacity;

        internal static void SetCapacity(IntPtr transformArrayIntPtr, int capacity) => TransformAccessArrayData.From(transformArrayIntPtr).Capacity = capacity;

        internal static Transform GetTransform(IntPtr transformArrayIntPtr, int index) => TransformAccessArrayData.From(transformArrayIntPtr).Get(index);

        internal static void SetTransform(IntPtr transformArrayIntPtr, int index, Transform transform) => TransformAccessArrayData.From(transformArrayIntPtr).Set(index, transform);
    }
}

namespace Unbound.Runtime.Jobs
{
    /// <summary>
    /// The native side of a TransformAccessArray: its transforms, and the snapshot a transform job
    /// runs over (transforms sorted by hierarchy, so each hierarchy's transforms are written by one
    /// thread).
    /// </summary>
    internal sealed unsafe class TransformAccessArrayData
    {
        readonly List<UnityEngine.Transform> m_Transforms = new List<UnityEngine.Transform>();
        int m_Capacity;
        GCHandle m_Self;

        internal static IntPtr Create(int capacity, int desiredJobCount)
        {
            var data = new TransformAccessArrayData { m_Capacity = Math.Max(0, capacity) };
            data.m_Self = GCHandle.Alloc(data);
            return GCHandle.ToIntPtr(data.m_Self);
        }

        internal static TransformAccessArrayData From(IntPtr array)
        {
            if (array == IntPtr.Zero) throw new NullReferenceException("The TransformAccessArray is not created");
            return (TransformAccessArrayData)GCHandle.FromIntPtr(array).Target;
        }

        internal static void Destroy(IntPtr array)
        {
            if (array == IntPtr.Zero) return;
            From(array).m_Self.Free();
        }

        internal int Count => m_Transforms.Count;

        internal int Capacity
        {
            get => Math.Max(m_Capacity, m_Transforms.Count);
            set
            {
                if (value < m_Transforms.Count) throw new ArgumentException("Capacity can not be smaller than the length of the TransformAccessArray");
                m_Capacity = value;
            }
        }

        internal void SetTransforms(UnityEngine.Transform[] transforms)
        {
            m_Transforms.Clear();
            m_Transforms.AddRange(transforms);
            m_Capacity = Math.Max(m_Capacity, transforms.Length);
        }

        internal void Add(UnityEngine.Transform transform)
        {
            m_Transforms.Add(transform);
            if (m_Transforms.Count > m_Capacity) m_Capacity = Math.Max(m_Capacity * 2, m_Transforms.Count);
        }

        internal void RemoveAtSwapBack(int index)
        {
            if ((uint)index >= (uint)m_Transforms.Count) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Transforms.Count}' Length.");
            int last = m_Transforms.Count - 1;
            m_Transforms[index] = m_Transforms[last];
            m_Transforms.RemoveAt(last);
        }

        internal UnityEngine.Transform Get(int index)
        {
            if ((uint)index >= (uint)m_Transforms.Count) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Transforms.Count}' Length.");
            var transform = m_Transforms[index];
            return transform == null ? null : transform;
        }

        internal void Set(int index, UnityEngine.Transform transform)
        {
            if ((uint)index >= (uint)m_Transforms.Count) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Transforms.Count}' Length.");
            m_Transforms[index] = transform;
        }

        // A snapshot: [TransformJobData: snapshot pointer, read-only flag][header: magic, count,
        // transforms GCHandle][sorted-to-user indices][TransformAccess per sorted index].
        const int Magic = 0x7a5f0b5;
        const int JobDataSize = 16;
        const int HeaderSize = 16;

        int[] m_LastOrder = Array.Empty<int>();
        List<(int Begin, int End)> m_LastGroups = new List<(int, int)>();

        /// <summary>The job data a transform job reads (freed with the job, and its snapshot with it).</summary>
        internal IntPtr PrepareForJob(bool readOnly)
        {
            int count = m_Transforms.Count;
            var snapshot = m_Transforms.ToArray();
            // Sorted by hierarchy root, user order within a hierarchy.
            var roots = new Dictionary<UnityEngine.Transform, int>();
            var keys = new int[count];
            for (int i = 0; i < count; i++)
            {
                var t = snapshot[i];
                if (t == null)
                {
                    keys[i] = int.MaxValue;
                    continue;
                }
                var root = t;
                while (root.m_Parent != null) root = root.m_Parent;
                if (!roots.TryGetValue(root, out int key)) roots[root] = key = roots.Count;
                keys[i] = key;
            }
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));
            m_LastGroups = new List<(int, int)>();
            for (int i = 0; i < count;)
            {
                int j = i;
                while (j < count && keys[order[j]] == keys[order[i]]) j++;
                m_LastGroups.Add((i, j));
                i = j;
            }
            m_LastOrder = order;

            int accessOffset = JobDataSize + HeaderSize + ((count * 4 + 15) & ~15);
            var memory = (byte*)NativeMemory.AllocZeroed((nuint)(accessOffset + count * sizeof(UnityEngine.Jobs.TransformAccess) + 16));
            var header = (int*)(memory + JobDataSize);
            var handle = GCHandle.Alloc(snapshot);
            header[0] = Magic;
            header[1] = count;
            *(IntPtr*)(header + 2) = GCHandle.ToIntPtr(handle);
            int* sortedToUser = (int*)(memory + JobDataSize + HeaderSize);
            var access = (UnityEngine.Jobs.TransformAccess*)(memory + accessOffset);
            for (int i = 0; i < count; i++)
            {
                sortedToUser[i] = order[i];
                access[i] = new UnityEngine.Jobs.TransformAccess(snapshot[order[i]] == null ? IntPtr.Zero : GCHandle.ToIntPtr(handle), order[i]);
            }
            *(IntPtr*)memory = (IntPtr)header;
            *(int*)(memory + IntPtr.Size) = readOnly ? 1 : 0;
            return (IntPtr)memory;
        }

        /// <summary>Frees the snapshot of a transform job's data (when the job is synced).</summary>
        internal static void ReleaseJobData(IntPtr jobData)
        {
            var header = *(int**)jobData;
            if (header == null || header[0] != Magic) return;
            GCHandle.FromIntPtr(*(IntPtr*)(header + 2)).Free();
            header[0] = 0;
        }

        /// <summary>Ranges of sorted indices, whole hierarchies each, at most <paramref name="maxChunks"/>.</summary>
        internal List<(int Begin, int End)> HierarchyRanges(int maxChunks)
        {
            var groups = m_LastGroups;
            var chunks = new List<(int, int)>();
            if (groups.Count == 0)
            {
                chunks.Add((0, 0));
                return chunks;
            }
            int n = Math.Max(1, Math.Min(maxChunks, groups.Count));
            for (int c = 0; c < n; c++)
            {
                int first = groups.Count * c / n, last = groups.Count * (c + 1) / n;
                chunks.Add((groups[first].Begin, groups[last - 1].End));
            }
            return chunks;
        }

        internal static IntPtr SnapshotSortedToUser(IntPtr snapshot)
        {
            var header = (int*)snapshot;
            if (header[0] != Magic) throw new InvalidOperationException("Not a transform job's TransformAccessArray");
            return (IntPtr)((byte*)header + HeaderSize);
        }

        internal static IntPtr SnapshotAccess(IntPtr snapshot)
        {
            var header = (int*)snapshot;
            if (header[0] != Magic) throw new InvalidOperationException("Not a transform job's TransformAccessArray");
            int count = header[1];
            return (IntPtr)((byte*)header - JobDataSize + JobDataSize + HeaderSize + ((count * 4 + 15) & ~15));
        }
    }
}
