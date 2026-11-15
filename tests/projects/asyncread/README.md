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
  null names; reads through the handle, a read queued before the open completed, `Close` with a
  dependency, the closed handle; `CloseCachedFileAsync`.
* **Deferred reads** (`deferred.*`): `ReadDeferred` whose commands a dependency job writes after
  the call, held by a job until the probe releases it.
* **Cancel** (`cancel.*`): a deferred read canceled while its dependency still holds it.
* **Frame timings** (`timing.*`): `IsFeatureEnabled`, timer frequencies, the frames kept and how
  they grow, newest first, which durations are positive, whether timestamps are ordered, scales
  and `syncInterval` under `vSyncCount` 1, 0 and 2. Durations are not recorded (they are timing).
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
once; `Cancel` of a pending request ends it `Canceled` without reading. The frame timings keep
32 frames, newest first; the timers count nanoseconds; GPU time and present wait are 0 (the
Manual lists GPU time as unsupported on Linux OpenGL); `syncInterval` reads the display's
refresh rate whatever `vSyncCount` is.

Left out because they crash the 6000.0.71f1 Editor or are timing-dependent there:
`GetLatestTimings` with a null array (crash), into an array shorter than the count it returns
(the Editor writes past the array's end and later crashes in its garbage collector),
`GetVSyncsPerSecond` (crash on OpenGLCore), and the status read right after `Cancel` (either
`Canceled` or `InProgress`). How many frames exist before the first `Update` depends on the
Editor frames before Play mode, so only the per-frame growth is recorded. macOS is not measured.

Contracts: [AsyncReadManager.Read](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.AsyncReadManager.Read.html)
(commands are copied), [ReadDeferred](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.AsyncReadManager.ReadDeferred.html)
(the array is read when the dependency completes; the read waits for the open),
[ReadStatus](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.IO.LowLevel.Unsafe.ReadStatus.html),
[Frame timing manager](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html)
(always active in the Editor; GPU time per platform).
