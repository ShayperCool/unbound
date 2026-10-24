# Jobs probe

The job system and what runs on it, run identically in the Unity Editor and in Unbound; each
scenario writes its values (results, and the exact exception or logged error of every misuse) to a
JSON file and `compare.py` lists the differences:

* `JobsProbe`: IJob, IJobFor (single, parallel, run), IJobParallelFor, IJobParallelForTransform,
  dependencies and CombineDependencies, when scheduled jobs start (the batch waits for
  ScheduleBatchedJobs, IsCompleted or Complete) and on which thread, thread indices, worker counts,
  copies of job data, exceptions in jobs, generic jobs registered by the Collections IL
  post-processor.
* `SafetyProbe`: the safety system with ENABLE_UNITY_COLLECTIONS_CHECKS (the Editor's default):
  main-thread access while jobs read or write, disposal, allocation arguments, temp memory scopes
  (per handle, per frame), AtomicSafetyHandle's API (secondary versions, read/write disallowed,
  undisposable, static safety ids and custom messages), schedule-time checks (missing dependencies,
  aliasing, reference and pointer fields, deallocated, temp and default containers), [ReadOnly] /
  [WriteOnly] / deallocation inside jobs, parallel-for index restrictions, DeallocateOnJobCompletion.
* `CollectionsProbe`: com.unity.collections containers in jobs (lists, deferred jobs, batches,
  filters, parallel writers, streams, hash maps, allocators, fixed strings).
* `BurstProbe`: com.unity.burst with compilation disabled (managed execution): function pointers,
  shared statics, direct calls, intrinsics.
* `ProfilingProbe`: categories (ids, names, colors), markers, counters (com.unity.profiling.core),
  ProfilerRecorder frame by frame, Recorder/CustomSampler, engine stats.
* `SplinesProbe`: com.unity.splines evaluating a spline in parallel jobs (SplineJobs, NativeSpline).
* `BaselibProbe`, `TransportProbe`, `LoggingProbe`: Baselib through com.unity.transport (network
  addresses; UDP, WebSocket over TCP and IPC drivers exchanging messages over loopback) and
  com.unity.logging (a file sink; the Editor console sink, whose entries skip Application's log
  callbacks).

Unbound has no Burst compiler, so it runs Burst code as an Editor started with
`UNITY_BURST_DISABLE_COMPILATION=1` does; the reference is recorded the same way. Record it
(`Reference/` keeps the recording of the release in its file name):

    UNITY_BURST_DISABLE_COMPILATION=1 JOBS_PROBE_OUT=/tmp/unity.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -nographics \
        -projectPath tests/projects/jobs -executeMethod ProbeRunner.Record -logFile /tmp/unity.log

Run Unbound (which sets the Burst variable itself) and compare:

    JOBS_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/jobs --headless 2000
    python3 tests/projects/jobs/compare.py tests/projects/jobs/Reference/unity-6000.0.71f1.json /tmp/unbound.json

`compare.py` reports apart the values that differ because of the .NET runtime rather than the
engine (`RUNTIME_DIFFERENCES`: Enum.ToString of shared values, GetFunctionPointerForDelegate,
AssemblyQualifiedName of BCL types) and normalizes how CoreCLR formats ArgumentException and
NullReferenceException messages.

The IL post-processors' output can be checked against the Editor's: after a Unity run,
`Library/ScriptAssemblies/JobsProbe.dll` is the post-processed assembly (with
`__JobReflectionRegistrationOutput__*` registering every job type); Unbound's is
`Library/Unbound/ScriptAssemblies/JobsProbe/<key>.<hash>.pp.dll`. Decompiled (`ilspycmd`), the
generated registration code is identical; the rest differs only as Release (the Editor) and Debug
(Unbound, for Edit and Continue) compiles do.
