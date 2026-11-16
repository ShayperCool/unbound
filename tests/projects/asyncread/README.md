# Asynchronous file reads and frame timings

`AsyncReadManager`, `ReadHandle`, `FileHandle`, `ReadCommand(Array)`, `FileInfoResult` and
`FrameTimingManager` as game code sees them in the Editor's Play mode on Unity 6000.0.71f1 (Linux,
OpenGLCore). `Assets/Scripts/AsyncReadProbe.cs` writes a 4096-byte file under the project's
`Temp/` (byte `i` is `i * 7 + 3`) and records, after each request's `JobHandle` completed:

* **Defaults** (`default.*`): default handles, their validity and the exceptions of every member.
* **File info** (`info.*`): `GetFileInfo` of the file by absolute and project-relative path, a
  missing file, a directory, an empty and a null name, and a null result.
* **Reads by name** (`name.*`): status, `ReadCount`, `GetBytesRead` (total, per command and the
  array), the bytes read (unread bytes keep the sentinel `ee`), `Cancel` after completion,
  `Dispose` and the disposed handle, for commands inside the file, past its end, at its end,
  mixed, of size 0, none, a missing file, a directory, an empty and a null name; a command
  array changed right after `Read` (the commands are copied).
* **File handles** (`open.*`): `OpenFileAsync` of the file, a missing file, a directory, empty and
  null names; reads through the handle, a read queued before the open completed, `Close` with and
  without a dependency on a queued read, the closed handle; `CloseCachedFileAsync`.
* **Deferred reads** (`deferred.*`): `ReadDeferred` whose commands a dependency job writes after
  the call, held by a job until the probe releases it.
* **Cancel** (`cancel.*`): a deferred read canceled while its dependency still holds it.
* **Frame timings** (`timing.*`): `IsFeatureEnabled`, timer frequencies, the frames kept and how
  they grow, newest first, which durations are positive, whether timestamps are ordered, scales
  and `syncInterval` under `vSyncCount` 1, 0 and 2, and the delay: frame 35 sleeps 250 ms, and
  the frame on which it is first the newest timing and its index ten frames later are recorded.
  Durations are not recorded (they are timing).
* **Read metrics** (`metrics.*`): `AsyncReadManagerMetrics` before and after
  `StartCollectingMetrics`: `IsEnabled`, the records of reads by name (one per command, with
  asset name, type and subsystem, none, size zero, a missing file, a directory, a relative path),
  through a file handle, deferred while their dependency holds them and canceled, and of
  `GetFileInfo`; filters of each category alone and together, empty and null; summaries of the
  current records, of arrays and with filters, with requests finished, failed, queued and
  canceled; `ClearOnRead` (also with a filter), `ClearCompletedMetrics` (with a canceled record), stopping and
  starting again. Durations are recorded as zero or positive only.
* **Logs** (`log.*`): the messages game code receives per section (none: the Editor writes its
  file errors to its own log only).

```bash
python3 tests/projects/asyncread/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/asyncread/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/asyncread/run.py unbound --release 6000.0.71f1
```

`editor` runs two independent windowed Editors on the GPU (existing display) and compares them;
`publish` writes `Reference/unity-6000.0.71f1.jsonl` when they are identical. The comparator is
modelroot's (exact strings, numbers within 2e-4).

Measured (two identical captures, NVIDIA driver): a command of nonzero size that reads nothing
fails the request and one cut short by the end of the file truncates it, every command still
running; `ReadCount` counts the commands even when the file does not open; a directory opens
and its reads fail; a missing file opens as `OpenFailed`; `Close` invalidates the handle at
once and, without a dependency, still lets a read queued before it complete; `Cancel` of a pending request ends it `Canceled` without reading. The frame timings keep
32 frames, newest first, a frame becoming the newest four frames after it ran (as the Manual
says); the timers count nanoseconds; GPU time and present wait are 0 (the
Manual lists GPU time as unsupported on Linux OpenGL); `syncInterval` reads the display's
refresh rate whatever `vSyncCount` is.

Left out because they crash the 6000.0.71f1 Editor or are timing-dependent there:
`GetLatestTimings` with a null array (crash), into an array shorter than the count it returns
(the Editor writes past the array's end and later crashes in its garbage collector),
`GetVSyncsPerSecond` (crash on OpenGLCore), the status read right after `Cancel` (either
`Canceled` or `InProgress`), and `AsyncReadManagerMetrics.GetMetrics` into a list (the Editor
throws a NullReferenceException for new lists, and captures that called it crashed later in its
garbage collector; summaries of lists did not count the list's records; only the null list's
check is recorded). How many frames exist before the first `Update` depends on the
Editor frames before Play mode (28 in both captures, which an engine run starts without), so
only the per-frame growth (at most one, never fewer) is recorded. macOS is not measured.
`timing.longFrameNewestAfter` assumes no frame before the slept one lasts 250 ms: on a heavily
loaded machine an engine run can see an earlier frame that long (one run of four at a load
average of 68).

Measured read metrics (two identical captures, the Editor started without
`-enable-file-read-metrics`): collection is off until started and `IsEnabled` answers whether it
records; each command is a record (its batch is the request's command count), failed when it
asked for bytes and got none; a file that does not open spends no time queued, a directory does;
file handle reads name no file; `GetFileInfo` is a record of subsystem FileInfo without offset,
size or batch; a deferred read is recorded `InQueue` with its array's commands while its
dependency holds it, and `ClearCompletedMetrics` keeps it; `ClearOnRead`, with a filter too,
clears every finished record as `ClearCompletedMetrics` does; stopping keeps the records and
starting again does not clear them. Summaries count finished, failed, queued and reading
requests, not canceled ones: `TotalBytesRead` is the sizes asked for by finished and failed
requests, `AverageReadSizeInBytes` that over their count; the longest read's and wait's asset
type and subsystem, and the canceled, cached, synchronous and asynchronous counts, are 0 (Other).
A null filter set throws NullReferenceException `(null)`; empty or null categories keep every
value.

Contracts: [AsyncReadManager.Read](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.AsyncReadManager.Read.html)
(commands are copied), [ReadDeferred](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.AsyncReadManager.ReadDeferred.html)
(the array is read when the dependency completes; the read waits for the open),
[ReadStatus](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.ReadStatus.html),
[Frame timing manager](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html)
(always active in the Editor; GPU time per platform),
[AsyncReadManagerMetrics](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics.html)
(available in the Editor; IsEnabled reports whether it is recording; ClearCompletedMetrics clears
failed and canceled requests too; it also collects reads of internal Unity systems, of which the
probe's captures show none: Unbound records game code's requests only).
