using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Unbound.Runtime;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// The job safety system's handle to a container (see <see cref="Safety"/>): the Editor's checks
    /// with ENABLE_UNITY_COLLECTIONS_CHECKS, with Unity's inline fast paths and error messages.
    /// </summary>
    public partial struct AtomicSafetyHandle
    {
        internal const int Read = 1;
        internal const int Write = 2;
        internal const int Dispose = 4;
        internal const int TempVersion = 8;
        internal const int VersionIncrement = 16;
        internal const int ReadCheck = -7;
        internal const int WriteCheck = -6;
        internal const int DisposeCheck = -4;
        internal const int ReadWriteDisposeCheck = -8;

        [NativeDisableUnsafePtrRestriction]
        internal IntPtr versionNode;
        internal int version;
        internal int staticSafetyId;

        public static AtomicSafetyHandle Create() => Safety.Create(temp: false);

        /// <summary>A handle valid forever, for slices over memory nobody owns (the stack).</summary>
        public static AtomicSafetyHandle GetTempUnsafePtrSliceHandle() => s_TempUnsafePtrSlice;

        static readonly AtomicSafetyHandle s_TempUnsafePtrSlice = Safety.Create(temp: false);

        /// <summary>A new handle of the current temp memory scope (the frame, or the executing job),
        /// released when the scope ends.</summary>
        public static AtomicSafetyHandle GetTempMemoryHandle() => Safety.CurrentScope.NewHandle();

        public static bool IsTempMemoryHandle(AtomicSafetyHandle handle) => Safety.IsTemp(handle);

        public static void Release(AtomicSafetyHandle handle) => Safety.Release(handle);

        public static bool IsDefaultValue(in AtomicSafetyHandle handle) => handle.versionNode == IntPtr.Zero && handle.version == 0;

        internal static void SetExclusiveWeak(ref AtomicSafetyHandle handle, bool enabled) => Safety.SetFlag(handle, n => n.ExclusiveWeak = enabled);

        internal static bool GetExclusiveWeak(in AtomicSafetyHandle handle) => Safety.GetFlag(handle, n => n.ExclusiveWeak, false);

        public static void PrepareUndisposable(ref AtomicSafetyHandle handle) => Safety.SetFlag(handle, n => n.Undisposable = true);

        /// <summary>Points the handle at the secondary version, which resizing the container bumps
        /// (views such as NativeList.AsArray).</summary>
        public static unsafe void UseSecondaryVersion(ref AtomicSafetyHandle handle)
        {
            var node = (int*)((long)handle.versionNode & ~15L);
            handle.versionNode = (IntPtr)(node + 1);
            handle.version = node[1] & ReadWriteDisposeCheck;
        }

        public static void SetAllowSecondaryVersionWriting(AtomicSafetyHandle handle, bool allowWriting) =>
            Safety.SetFlag(handle, n => n.AllowSecondaryWriting = allowWriting);

        public static void SetBumpSecondaryVersionOnScheduleWrite(AtomicSafetyHandle handle, bool value) =>
            Safety.SetFlag(handle, n => n.BumpSecondaryOnScheduleWrite = value);

        public static void SetAllowReadOrWriteAccess(AtomicSafetyHandle handle, bool allowReadWriteAccess) =>
            Safety.SetFlag(handle, n => n.AllowReadOrWrite = allowReadWriteAccess);

        public static bool GetAllowReadOrWriteAccess(AtomicSafetyHandle handle) => Safety.GetFlag(handle, n => n.AllowReadOrWrite, true);

        public static void SetNestedContainer(AtomicSafetyHandle handle, bool isNestedContainer) =>
            Safety.SetFlag(handle, n => n.Nested = isNestedContainer);

        public static bool GetNestedContainer(AtomicSafetyHandle handle) => Safety.GetFlag(handle, n => n.Nested, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckWriteAndBumpSecondaryVersion(AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (ptr == null || handle.version != (*ptr & WriteCheck))
                CheckWriteAndThrowNoEarlyOut(handle);
            Safety.BumpSecondary(handle);
        }

        /// <summary>Completes the unsynced jobs that use the container.</summary>
        public static EnforceJobResult EnforceAllBufferJobsHaveCompleted(AtomicSafetyHandle handle)
        {
            if (!Safety.IsAlive(handle)) return EnforceJobResult.HandleWasAlreadyDeallocated;
            var jobs = Safety.JobsUsing(handle);
            if (jobs.Count == 0) return EnforceJobResult.AllJobsAlreadySynced;
            foreach (var job in jobs) Unbound.Runtime.Jobs.JobSystem.Sync(job);
            return EnforceJobResult.DidSyncRunningJobs;
        }

        public static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndRelease(AtomicSafetyHandle handle)
        {
            var result = EnforceAllBufferJobsHaveCompleted(handle);
            if (result != EnforceJobResult.HandleWasAlreadyDeallocated) Release(handle);
            return result;
        }

        public static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndDisableReadWrite(AtomicSafetyHandle handle)
        {
            var result = EnforceAllBufferJobsHaveCompleted(handle);
            if (result != EnforceJobResult.HandleWasAlreadyDeallocated) SetAllowReadOrWriteAccess(handle, false);
            return result;
        }

        internal static bool CheckAllBufferJobsAreDependencyOrHaveCompleted(AtomicSafetyHandle handle, JobHandle job)
        {
            foreach (var user in Safety.JobsUsing(handle))
                if (!Unbound.Runtime.Jobs.JobSystem.IsDependency(job, user) && !user.Synced) return false;
            return true;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckReadAndThrowNoEarlyOut(AtomicSafetyHandle handle) => Safety.CheckAndThrow(handle, Read);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckWriteAndThrowNoEarlyOut(AtomicSafetyHandle handle) => Safety.CheckAndThrow(handle, Write);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckDeallocateAndThrow(AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (handle.version != (*ptr & DisposeCheck))
                Safety.CheckAndThrow(handle, Dispose);
        }

        /// <summary>Checks that the container's secondary data (a list's buffer) may be read.</summary>
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckGetSecondaryDataPointerAndThrow(AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (handle.version != (*ptr & ReadCheck))
                Safety.CheckAndThrow(handle, Read);
        }

        /// <summary>The unsynced jobs reading the container, written to <paramref name="output"/> as
        /// JobHandles (at most <paramref name="maxCount"/>); returns how many there are.</summary>
        public static unsafe int GetReaderArray(AtomicSafetyHandle handle, int maxCount, IntPtr output)
        {
            var readers = Safety.ReadersOf(handle);
            var handles = (JobHandle*)output;
            for (int i = 0; i < readers.Count && i < maxCount; i++) handles[i] = readers[i].Handle;
            return readers.Count;
        }

        public static JobHandle GetWriter(AtomicSafetyHandle handle) => Safety.WriterOf(handle)?.Handle ?? default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckReadAndThrow(AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (handle.version != (*ptr & ReadCheck))
                CheckReadAndThrowNoEarlyOut(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckWriteAndThrow(AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (handle.version != (*ptr & WriteCheck))
                CheckWriteAndThrowNoEarlyOut(handle);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void ValidateNonDefaultHandle(in AtomicSafetyHandle handle)
        {
            if (!IsDefaultValue(in handle)) CheckExistsAndThrow(in handle);
        }

        public static bool IsValidNonDefaultHandle(in AtomicSafetyHandle handle) => !IsDefaultValue(in handle) && IsHandleValid(in handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void CheckExistsAndThrow(in AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            if (handle.version != (*ptr & ReadWriteDisposeCheck))
                throw new ObjectDisposedException("The NativeArray has been disposed, it is not allowed to access it");
        }

        public static unsafe bool IsHandleValid(in AtomicSafetyHandle handle)
        {
            int* ptr = (int*)handle.versionNode;
            return handle.version == (*ptr & ReadWriteDisposeCheck);
        }

        public static string GetReaderName(AtomicSafetyHandle handle, int readerIndex)
        {
            var readers = Safety.ReadersOf(handle);
            return readerIndex >= 0 && readerIndex < readers.Count ? readers[readerIndex].Name : null;
        }

        public static string GetWriterName(AtomicSafetyHandle handle) => Safety.WriterOf(handle)?.Name;

        public static unsafe int NewStaticSafetyId(byte* ownerTypeNameBytes, int byteCount) =>
            Safety.NewStaticSafetyId(Encoding.UTF8.GetString(ownerTypeNameBytes, byteCount));

        public static unsafe int NewStaticSafetyId<T>()
        {
            var bytes = Encoding.UTF8.GetBytes(typeof(T).ToString());
            fixed (byte* ownerTypeNameBytes = bytes)
                return NewStaticSafetyId(ownerTypeNameBytes, bytes.Length);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static unsafe void SetCustomErrorMessage(int staticSafetyId, AtomicSafetyErrorType errorType, byte* messageBytes, int byteCount) =>
            Safety.SetCustomErrorMessage(staticSafetyId, errorType, Encoding.UTF8.GetString(messageBytes, byteCount));

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void SetStaticSafetyId(ref AtomicSafetyHandle handle, int staticSafetyId) => handle.staticSafetyId = staticSafetyId;

        internal static void CreateHandle(out AtomicSafetyHandle safety, Allocator allocator)
        {
            safety = allocator == Allocator.Temp ? GetTempMemoryHandle() : Create();
            if (JobsUtility.IsExecutingJob && (uint)(allocator - Allocator.TempJob) <= 1u)
                throw new InvalidOperationException("Jobs can only create Temp memory");
        }

        internal static void DisposeHandle(ref AtomicSafetyHandle safety)
        {
            CheckDeallocateAndThrow(safety);
            // A temp container's handle stays valid until its scope ends (copies keep working).
            if (IsTempMemoryHandle(safety))
            {
                int id = safety.staticSafetyId;
                safety = GetTempMemoryHandle();
                safety.staticSafetyId = id;
            }
            Release(safety);
        }
    }

    public enum AtomicSafetyErrorType
    {
        Deallocated = 0,
        DeallocatedFromJob = 1,
        NotAllocatedFromJob = 2,
    }

    public enum EnforceJobResult
    {
        AllJobsAlreadySynced = 0,
        DidSyncRunningJobs = 1,
        HandleWasAlreadyDeallocated = 2,
    }
}
