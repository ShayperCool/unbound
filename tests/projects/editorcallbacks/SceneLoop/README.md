# Scene loading and activation controls

This companion to `editorcallbacks` measures Unity 6000.0.71f1 Play Mode in the
built-in pipeline on the Linux GPU Editor. The fixture uses private copies of
Editor-authored scenes and a prefab. Generated captures, logs and repeats live
under `$XDG_CACHE_HOME/unbound/validation/runs/editorcallbacks/`.

`First` and `Second` place a prefab root at different positions among plain roots.
The driver persists across single loads. The default capture covers first and
later additive/single loads, held activation, two queued loads and an unload,
synchronous loading, root sibling changes, re-enabling a handler and runtime
AddComponent. `--single-first` isolates a cold single load. `--force-only`
checks synchronous loads behind held async work, both immediately and after the
held load has prepared. `--starts-only` compares Start-only scripts created from
a coroutine and LateUpdate with a script that also has LateUpdate. `--queue-only`
checks two unheld additive requests issued together. It waits 500 ms per frame for
background readers; the other profiles wait 50 ms. Lifecycle events, scene notifications, operation state
and render callbacks carry relative frames and public player-loop leaf brackets.

Create the authored inputs once, then record and publish each profile:

```
python3 tests/projects/editorcallbacks/scene_loop.py create --release 6000.0.71f1 --label setup
python3 tests/projects/editorcallbacks/scene_loop.py editor --release 6000.0.71f1 --label pair --repeat native1
python3 tests/projects/editorcallbacks/scene_loop.py editor --release 6000.0.71f1 --label pair --repeat native2
python3 tests/projects/editorcallbacks/scene_loop.py publish --release 6000.0.71f1 --label pair
```

Use distinct labels and add `--single-first` or `--force-only` to all three commands
for the other profiles. Use `--starts-only` for the Start control, `--queue-only` for the unheld queue,
`--queue-three` for three queued loads and `--queue-budget` for the integration time limit. Publication requires byte-identical canonical records,
normal Editor exits, completion markers and a verified GPU renderer. It never
overwrites a reference. Request-time progress is checked against `[0,0.9]`, because
its exact read races background preparation. Settled frame reads remain exact.
Every progress read and the variable unload completion frame are retained in
`values.txt.raw.txt` beside each capture. The unload control checks that it stays
pending behind held work, completes in preloading after both loads, and is done at
the settled read. Its elapsed completion time is not fixed by the reference.

The engine-only commands, also run by `tools/agents/suite.py`, are:

```
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --single-first
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --force-only
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --starts-only
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --queue-only
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --queue-three
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/editorcallbacks/scene_loop.py check --release 6000.0.71f1 --queue-budget
```

`scene_loop_compare.py` prints the complete ordered raw delta, then compares every
receiver's full sequence and the driver's full sequence. Frames, phases, state,
counts, event order within each receiver and ordered root-query answers are
strict. Relative callbacks across different instances of the same script remain
an observation. The comparator neither discards events nor changes an existing
probe's oracle or tolerances.

## Contracts, observations and limits

| Source for Unity 6000.0 | Documented guarantee | Observation or implementation choice |
|---|---|---|
| [LoadSceneAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html) | Loads in the background and returns an operation; single loading unloads the current scenes. | These small first/later single and additive loads prepare at 0.9 by frame 1 and integrate in frame 2. This is a measured context, not a promised loading duration for arbitrary content. |
| [allowSceneActivation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html), [progress](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-progress.html) | Holding activation stops at 0.9 with isDone false and stalls queued operations. | Prepared held loads integrate at the preloading pass after release. A second load can prepare while held, then integrates in the following frame. A queued unload follows the loads; its exact completion frame varies. |
| [LoadScene](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadScene.html) | Completes during the next frame and forces previous async operations to complete, including held ones. | Both forced-load controls integrate the held prefix and the synchronous request at the next preloading pass, preserving the held operation's allowSceneActivation value. |
| [Execution order](https://docs.unity3d.com/6000.0/Documentation/Manual/execution-order.html) | sceneLoaded follows OnEnable and precedes Start. Configurable script execution order applies; order between instances of one script is not guaranteed. | Awake/OnEnable, scene notifications and completion run in preloading; Start follows in the startup leaf. No cross-object wake sort is inferred from these scenes. |
| [GetRootGameObjects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.Scene.GetRootGameObjects.html) | Returns the scene's root objects. The page specifies no ordering. | The authored SceneRoots list, including entries naming prefab instances, determines the measured root sequence and sibling indices. Public hierarchy order and script activation order are kept separate. |
| [OnRenderObject](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderObject.html) | Called after camera rendering; it applies to scripts beyond the camera's object. No order between receivers is specified. | Root sibling changes do not reorder the handler list. The fixture's first handler order differs from hierarchy order. Initial cross-object differences remain raw and Deferred; no renderer sort or universal wake order is introduced. |

The Start control records Start-only scripts created by a coroutine and by
LateUpdate at PostLateUpdate.ScriptRunDelayedDynamicFrameRate. A script with a
LateUpdate callback starts immediately before that callback instead. Early pending
Starts and Update-created Starts retain their startup and Update scripting points.
`Compat.PendingStartsAfterLateUpdate` selects the measured final drain; other
releases retain the previous LateUpdate-end drain until measured.

Other releases retain the previous one-pass scene-load timing until measured.
Large-content preparation durations, cancellation and other rendering pipelines
are not established by this fixture. A duplicate additive load immediately after a
single load completed at frame 2 or 3 across earlier full cold-start repeats. Both
raw repeats remain in the validation cache; this is variation in an unspecified
loading duration, not a separate exact-frame rule. At the shorter frame pause the
unheld queue's second load also completed in frame 3 or 4; both repeats still
integrated the two loads in distinct preloading passes. The scene input format correction changes
runtime hierarchy queries and does not change imported/cache output.

## Integration time limit (`--queue-budget`)

Scene loads, scene unloads and unused-asset unloads share one queue. Its timing
follows the per-frame main-thread integration limit of
[Application.backgroundLoadingPriority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-backgroundLoadingPriority.html).
The profile runs about 30 cases with a 100 ms frame pause. A busy wait in each
loaded scene's `PlainA.Awake` (`d2`, `d25`, `d70`: milliseconds) makes an
integration take a known minimum time.

| Source for Unity 6000.0 | Documented guarantee | Measured in the Linux GPU Editor (this profile) |
|---|---|---|
| [backgroundLoadingPriority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-backgroundLoadingPriority.html) | Main-thread integration of asynchronous operations is limited per frame: Low 2 ms, BelowNormal 4 ms, Normal 10 ms, High 50 ms. The default is BelowNormal. The page says the setting has no effect in the Editor. | The initial value is BelowNormal. Normal and High read back as Low, and their limit is Low's: a 2 ms Awake lets the next load arm in the same pass under BelowNormal and not under Low, Normal or High. |
| [allowSceneActivation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html) | A held load stops at 0.9 and stalls the operations queued behind it. | A load that reaches the head prepared waits one pass, then integrates in a later pass. A pass goes on to the next request while its elapsed time, rounded to milliseconds, is below the limit: queued loads complete 2/3/4 frames after the request below the limit and 2/4/6 beyond it (`d70`). A held first load released at frame 4 completes at 5; the second completes at 6 or 7 by the same rule. |
| [UnloadSceneAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html) | Unloads in the background and returns an operation. | An unload behind a load integrates as soon as it heads the queue: in the load's pass below the limit, in the next pass beyond it. |
| [AsyncOperation.priority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-priority.html) | Of queued operations, the highest priority executes first; a change after an operation has started has no effect. | With an unused-asset unload requested first, loads requested in the same frame complete in priority order. Priorities set one frame later have no effect. Without that unload, whether the loader takes the first load before its priority is set varies between Editor runs (`priority.request`, sidecar only). |
| [EditorSettings.forceAssetUnloadAndGCOnSceneLoad](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorSettings-forceAssetUnloadAndGCOnSceneLoad.html), [Resources.UnloadUnusedAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html) | After a Single load the Editor unloads unused assets and collects the managed heap. UnloadUnusedAssets returns an operation. | The unload after a Single load is a queued operation. Loads queued behind it do not start before it completes, so a load queued behind a Single load (at once or after it completed) completes three frames after it. With the setting off, the queued load completes two frames after it. `Resources.UnloadUnusedAssets` alone completes at the next preloading pass and holds back later loads in the same way. |

Below the limit, frames depend on the integration's own duration, which machine load
changes. Those cases keep their frames in the sidecar. Each case's canonical
`limit=` line instead checks every gap between consecutive completions against the
pass time the probe measured around `EarlyUpdate.UpdatePreloading`. A gap is skipped
when the next request was not prepared or was held, or when the pass time is within
0.6 ms of the rounding boundary. Cases beyond the limit (`d25`, `d70`) and the held
and Single cases also record absolute frames. A request's progress read at a
coroutine point races the loader for a request it has just started. The record keeps
the last pending progress before completion; the sidecar keeps every read and pass
time.

Unbound applies the same rule with its own pass time. With built-in lighting data read
once per session, a warm integration of these scenes takes well under a millisecond, so
the main profile's queue control matches. The first integration of a session costs
several milliseconds more (about 7 ms measured, mostly first-use compilation), beyond
the 4 ms limit: `--queue-only`, the session's first queue, therefore matches the
Editor's frames 2/4. A faster first integration would turn that profile's second load
into frame 3, a raw failure under the same rule. Machine load can still move a
below-limit gap, as in the Editor. Unmeasured: the macOS priority read-back, the
read-back of a Linux user allowed to raise thread priorities (one machine and user
measured), other asynchronous operations (Resources and AssetBundle loads) sharing the
limit, and other releases. Those releases keep the previous one-pass timing.

The main, cold-single, forced-load, Start, unheld-queue and three-load profiles pass
their receiver/driver checks. The suite retains every strict result. Runtime frame
limits leave space beyond each fixture's final marker.
