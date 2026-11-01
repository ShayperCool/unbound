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
