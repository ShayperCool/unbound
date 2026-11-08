# Scene loading and activation controls

This companion to `editorcallbacks` measures Unity 6000.0.71f1 Play Mode in the
built-in pipeline on the Linux GPU Editor. The fixture uses private copies of
Editor-authored scenes and a prefab. Generated captures, logs and repeats live
under `$XDG_CACHE_HOME/unbound/q192/`.

`First` and `Second` place a prefab root at different positions among plain roots.
The driver persists across single loads. The default capture covers first and
later additive/single loads, held activation, two queued loads and an unload,
synchronous loading, root sibling changes, re-enabling a handler and runtime
AddComponent. `--single-first` isolates a cold single load. `--force-only`
checks synchronous loads behind held async work, both immediately and after the
held load has prepared. Lifecycle events, scene notifications, operation state
and render callbacks carry relative frames and public player-loop leaf brackets.

Create the authored inputs once, then record and publish each profile:

```
python3 tests/projects/editorcallbacks/scene_loop.py create --release 6000.0.71f1 --label setup
python3 tests/projects/editorcallbacks/scene_loop.py editor --release 6000.0.71f1 --label pair --repeat native1
python3 tests/projects/editorcallbacks/scene_loop.py editor --release 6000.0.71f1 --label pair --repeat native2
python3 tests/projects/editorcallbacks/scene_loop.py publish --release 6000.0.71f1 --label pair
```

Use distinct labels and add `--single-first` or `--force-only` to all three commands
for the other profiles. Publication requires byte-identical canonical records,
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

Other releases retain the previous one-pass scene-load timing until measured.
Large-content preparation durations, cancellation and other rendering pipelines
are not established by this fixture. A duplicate additive load immediately after a
single load completed at frame 2 or 3 across earlier full cold-start repeats. Both
raw repeats remain in the validation cache; this is variation in an unspecified
loading duration, not a separate exact-frame rule. The scene input format correction changes
runtime hierarchy queries and does not change imported/cache output.
