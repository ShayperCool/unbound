using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Unbound.Runtime.Jobs
{
    /// <summary>The native job system's table (crates/unbound-jobs/src/ffi.rs, the same layout).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct JobsApi
    {
        internal const uint CurrentVersion = 1;

        internal uint Version;
        /// <summary>(prepare, execute, finish): the callbacks every job's user value goes to.</summary>
        internal delegate* unmanaged<delegate* unmanaged<nint, uint>, delegate* unmanaged<nint, int, int, void>, delegate* unmanaged<nint, void>, void> SetCallbacks;
        /// <summary>(user, dependency handle or 0) -> handle: a job into the batch.</summary>
        internal delegate* unmanaged<nint, ulong, ulong> Schedule;
        /// <summary>(user, handles, count) -> handle: a combination of dependencies.</summary>
        internal delegate* unmanaged<nint, ulong*, int, ulong> Combine;
        /// <summary>(handle, held) -> whether the job is still batched.</summary>
        internal delegate* unmanaged<ulong, int, int> SetHeld;
        internal delegate* unmanaged<void> Flush;
        internal delegate* unmanaged<ulong, void> Start;
        /// <summary>(handle) -> whether the job was not done yet: runs or waits for it.</summary>
        internal delegate* unmanaged<ulong, int> Wait;
        internal delegate* unmanaged<ulong, int> IsDone;
        internal delegate* unmanaged<ulong, void> Release;
        internal delegate* unmanaged<int> ProcessorCount;
        internal delegate* unmanaged<int> MaxWorkers;
        internal delegate* unmanaged<int> WorkerCount;
        internal delegate* unmanaged<int, void> SetWorkerCount;
        internal delegate* unmanaged<int> ThreadIndex;
    }

    internal static unsafe partial class JobSystem
    {
        static JobsApi* s_Api;

        static JobsApi* Api => s_Api != null ? s_Api : throw new InvalidOperationException("Unbound: the job system is not installed (no native JobsApi)");

        /// <summary>Uses the native job system's table (static native data): the engine's, or in the
        /// managed tests the one of the job system's shared library.</summary>
        internal static void Install(void* table)
        {
            var api = (JobsApi*)table;
            if (api->Version != JobsApi.CurrentVersion)
                throw new InvalidOperationException($"native job system version {api->Version}, managed expects {JobsApi.CurrentVersion}");
            api->SetCallbacks(&OnPrepare, &OnExecute, &OnFinish);
            s_Api = api;
        }

        static JobRecord RecordOfUser(nint user) => (JobRecord)GCHandle.FromIntPtr(user).Target;

        /// <summary>Before the job's first participant runs: its BeforeStart work when a Complete
        /// runs it, its deferred data; returns its participant count.</summary>
        [UnmanagedCallersOnly]
        static uint OnPrepare(nint user)
        {
            JobRecord record = null;
            try
            {
                record = RecordOfUser(user);
                RunBeforeStart(record);
                PrepareToRun(record);
                return (uint)Math.Max(1, record.Participants);
            }
            catch (Exception e)
            {
                if (record != null) record.PrepareFailed = true;
                ReportCallbackException(e);
                return 1;
            }
        }

        [UnmanagedCallersOnly]
        static void OnExecute(nint user, int jobIndex, int threadIndex)
        {
            try
            {
                Execute(RecordOfUser(user), jobIndex, threadIndex);
            }
            catch (Exception e)
            {
                ReportCallbackException(e);
            }
        }

        [UnmanagedCallersOnly]
        static void OnFinish(nint user)
        {
            try
            {
                Finish(RecordOfUser(user));
            }
            catch (Exception e)
            {
                ReportCallbackException(e);
            }
        }

        /// <summary>An exception must not leave a native callback (it would end the process): it is
        /// logged as an exception in a job is.</summary>
        static void ReportCallbackException(Exception e)
        {
            try { Debug.LogException(e); }
            catch (Exception) { Console.Error.WriteLine(e); }
        }
    }
}
