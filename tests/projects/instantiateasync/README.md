# InstantiateAsync probe

`Object.InstantiateAsync` (every overload: count, parent, position/rotation and their spans,
`InstantiateParameters`, `CancellationToken`), `Object.Instantiate` with `InstantiateParameters`,
and `AsyncInstantiateOperation<T>`: `Result`, `isDone`, `progress`, `completed`,
`allowSceneActivation`, `IsWaitingForSceneActivation`, `Cancel`, `WaitForCompletion`,
`Get/SetIntegrationTimeMS` and awaiting. The prefab `Resources/Item` (root and one child, each
with `ItemProbe`) and the scene (a scaled and rotated `Parent`, an active `Template`, an inactive
`Hidden`) were made by `ProbeRunner.Create`.

`AsyncProbe` puts a marker before every player-loop system, so each record line carries the frame
(counted from the probe's Start), the phase (the system that runs next), the scenario, the object
and the event. Watched operations are polled at every marker and record each change of `isDone`,
`Result` and `allowSceneActivation`. Each step starts one operation every second frame and then
sleeps 50 ms: Unity copies the objects on a background thread, and operations started together, or
copies still running when the next frame begins, complete in no fixed order or frame. For the same
reason, progress below 0.9 and `IsWaitingForSceneActivation` are recorded only as ranges or after
the step settles, and the 20-object operation under a tiny budget is recorded without its frame.

```
python3 tests/projects/instantiateasync/run.py editor --release 6000.0.71f1 --label <new-label>
python3 tests/projects/instantiateasync/run.py publish --release 6000.0.71f1 --label <new-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/instantiateasync/run.py unbound --release 6000.0.71f1 --label <new-label>
python3 tests/projects/instantiateasync/compare.py tests/projects/instantiateasync/Reference/unity-6000.0.71f1.jsonl \
    ~/.cache/unbound/validation/runs/instantiateasync/<label>/unbound/values.jsonl --report <cache>/compare.json
```

Captures run on fresh copies under `$XDG_CACHE_HOME/unbound/validation/runs/instantiateasync/`.
Publishing requires two byte-identical Editor captures on the NVIDIA renderer. The comparator
aligns the two event sequences and keeps every missing and extra line, with no tolerance.

## Measured behavior in 6000.0.71f1

`Reference/unity-6000.0.71f1.jsonl` has 770 events from two byte-identical GPU captures.

* **When.** The call returns an unfinished operation: `Result` null, nothing in any scene, no
  Awake. The copies are taken at the call: renaming, moving, deactivating or destroying the
  original right after it does not reach them. In the next frame, `EarlyUpdate.UpdateAsyncInstantiate`
  (directly before `UpdateAsyncReadbackManager` and `UpdatePreloading`) places the copies. It
  sends Awake and OnEnable object by object of the original, across all copies of the operation
  (every copy's root, then every copy's child). Then `completed` and await continuations run, with
  `isDone`, `progress` 1 and `Result` set. Start follows in the same frame's
  `ScriptRunDelayedStartupFrame`. An operation integrates whole, even 20 copies under a 0.0001 ms
  budget.
* **progress** is at most 0.9 while the background copy runs, 0.9 once it is ready, and 1 when done.
* **allowSceneActivation = false** holds a ready operation at 0.9 with
  `IsWaitingForSceneActivation()`. Setting it to true integrates at once, inside the setter.
* **Cancel / a cancelled token** completes the operation at its next integration with `Result`
  null and no objects. Cancelling twice, or cancelling a token after completion, changes nothing.
  A cancelled held operation stops waiting but completes, with no result, only when
  `allowSceneActivation` becomes true. A parent destroyed before the integration (Destroy or
  DestroyImmediate) gives the same empty completion.
* **WaitForCompletion** integrates at once (Awake, OnEnable, `completed` in the call). A
  `completed` handler added afterwards runs immediately. An awaiter of an unfinished operation is
  not completed. An already finished one resumes synchronously.
* **Integration budget**: `GetIntegrationTimeMS()` is 2. `SetIntegrationTimeMS` throws
  ArgumentOutOfRangeException for 0 and negative values and ignores NaN.
* **Names**: asynchronous copies are named `<name> (Clone)`; `Instantiate` with
  `InstantiateParameters` keeps `<name>(Clone)`.
* **Placement**: every overload without `InstantiateParameters` uses `worldSpace = true`. A
  default `InstantiateParameters` has `worldSpace = false`. Copy `i` takes `positions[i % n]` and
  `rotations[i % n]`; an empty span keeps the original's pose, and mismatched lengths are not an
  error. Under a parent, worldSpace true keeps the given (or the original's) world position and
  rotation and the local scale. For a synchronous `Instantiate` without coordinates, it keeps the
  world scale too. worldSpace false makes the given coordinates, or the original's local pose,
  local to the parent. The parent's scene wins over `scene`. Without a parent the copy goes to
  `scene`, else to the active scene, before Awake.
* **Errors**: a null original throws ArgumentException, checked before the count. A count of 0 or
  less throws ArgumentException. A ScriptableObject throws ArgumentException for InstantiateAsync,
  but `Instantiate` with parameters clones it. A destroyed original throws
  MissingReferenceException naming parameter `original` (async) or `data` (sync). A texture logs
  "Cannot instantiate multiple objects that are not connected to GameObject, ..." and throws
  NullReferenceException; `Instantiate` with parameters clones it. A parent with a pending
  Destroy is accepted.
