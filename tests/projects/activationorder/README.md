# Activation callback order of an already-awake hierarchy

Unity 6000.0.71f1, built-in pipeline (`Assets/Scripts/ActivationOrderProbe.cs`). The 208-record
reference is admitted from two byte-identical strict windowed GPU captures. No production
activation correction or Unbound parity is established by that admission.

Earlier Native batch references show child-first OnDisable/OnEnable for an already-awake parent
with a single child and level. Those records could not choose between hierarchy post-order and
reverse pre-order, or between plain `SetActive` and the batch calls. This fixture separates
the seven predeclared orders for each operation, within its limits.

## Fixture

`Packages/`, `ProjectSettings/`, the empty probe scene, the folder metas and the runner's `.meta`
are byte copies of the shader pass count fixture. Its Packages and ProjectSettings are byte
copies of the Editor-serialized shader keyword fixture (commit 983a7fd6). No setup Editor ran.
The probe script's `.meta` is new and has no importer settings, so the script runs at the
default execution order. The only compile inputs are the two scripts.

The probe builds every node itself, at the start of Play mode, from authored tables:

| node | parent | authored activeSelf |
|---|---|---|
| P | root | true |
| C1 | P (sibling 0) | true |
| G | C1 | true |
| C2 | P (sibling 1) | true |
| U | root (untouched control) | true |
| W, W1 | root, W | false, true |
| V, V1 | root, V | false, true |

GameObjects are created in the order G, C2, P, C1, U, W1, W, V1, V. They are parented in table
order and the inactive ones are deactivated. Then one recorder is added to each node in the order
C2, C1, P, G, U, W1, W, V1, V. For P's subtree, neither creation order nor recorder order, nor
either reversed, equals pre-order, post-order or reverse pre-order. A creation, instance or
component order therefore cannot pass for a traversal.

One class is both the recorder and the driver. Every instance records Awake, OnEnable, Start and
OnDisable in one raw trace, under its GameObject's authored label. Each entry also has the
instance's own activeSelf, activeInHierarchy, enabled and isActiveAndEnabled at that moment. The
driver sits on its own `driver` root, which is never toggled. It has no other Unity message
(no OnDestroy and no Update). Nothing is destroyed. The trace closes before the record is
written, so Play-mode teardown callbacks are not recorded.

## Operations (one Play mode run, one frame, in order)

After the set-up, the probe waits 2 frames so that Awake, OnEnable and Start complete. Then:

1. `op.plainOff` / `op.plainOn`: `P.SetActive(false)`, then `P.SetActive(true)`.
2. `op.spanOff` / `op.spanOn`: `GameObject.SetGameObjectsActive(ReadOnlySpan<int> [P], false/true)`.
3. `op.nativeOff` / `op.nativeOn`: the `NativeArray<int>` overload with `[P]` (Persistent, disposed).
   This is the compact preservation control for the other batch overload. It needs no extra
   module, because `NativeArray` is in the core module.
4. First-wake controls, kept apart: `wake.plainOn` (`W.SetActive(true)`) and `wake.spanOn`
   (`SetGameObjectsActive([V], true)`).

## Record

`values.jsonl` has one `{"k": key, "v": value}` per line, in the probe's fixed order. A read
records its value or `key.exception`. A step records only `step.exception`. Every block ends
with `block.log`.

* `context.*`: Unity version, batchmode, platform and graphics device.
* `boot.calls`: the driver's own callbacks.
* `setup.<node>.step[create|parent|deactivate|recorder]`, then `setup.calls` (Awake/OnEnable
  at AddComponent), then `setup.tree`. The tree is read pre-order through `Transform.GetChild`
  from each authored root and gives label, parent, sibling index, depth and child count.
* `settle.calls` (the Starts), `settle.relativeFrame`, `settle.state.<node>`.
* For each operation: `request` (the authored call/target/value), `relativeFrame`, then
  `before.<node>` for all nine nodes, the NativeArray step, `returned` (`"void"` or the
  exception), `calls` (the synchronous callbacks in raw order), the Dispose step and
  `after.<node>`. A node state is activeSelf, activeInHierarchy, the recorder's enabled and
  isActiveAndEnabled, parent label, sibling index, and the recorded counts of each callback.
* `capture.strayCalls`: any trace entry outside the lists above, with where it occurred.
  `capture.unscopedLogs` follows, then the single marker `{"k":"complete","v":true}`. The file
  is written once, through a temporary name.

The canonical record holds only deterministic context, authored labels, relative frame markers,
states and raw callback order. Instance IDs, absolute frames, real time and device names go to
`diagnostics.jsonl`, which is hashed and never compared. Nothing is sorted.

## Hypotheses (precommitted, `PREDICTIONS.json` from `analyze.py predict`)

The hypotheses are derived only from the committed probe source's tables, separately for each
operation:

* already-awake (OnDisable for off, OnEnable for on, over P's subtree): PRE `P,C1,G,C2`,
  POST `G,C1,C2,P` and REVPRE `C2,G,C1,P`. The confounds CREATE/REVCREATE and ADD/REVADD each
  predict a different list.
* first wake: PARENT_INTERLEAVED, PARENT_PHASED, CHILD_INTERLEAVED and CHILD_PHASED. With one
  child, child-first coincides with these pairs' creation and recorder order. These controls
  preserve existing first-wake behaviour; they are not hierarchy-order evidence.
* relations per direction: SAME_PATH (plain, span and NativeArray lists equal) or
  SEPARATE_PATH. The wake relation compares the plain and batch patterns under a label map.

`analyze.py fit` gives ONE, MULTIPLE or NONE per operation; every outcome is valid. If the tree
or the before-state is not what an operation assumes, that operation is NOT_EVALUABLE and is
never refitted. Nothing in `run.py` or `guards.py` reads the analyzer.

## Admitted observations

Both windowed captures exit 0 on NVIDIA/OpenGLCore, log shutdown, keep authored
files unchanged and match the recorded compile graph. They agree byte for byte.

- `plainOff`, `spanOff` and `nativeOff`: G,C1,C2,P. The stored fit is uniquely POST among
  the seven predeclared orders in this tree.
- `plainOn`, `spanOn` and `nativeOn`: C2,C1,P,G. The stored fit is uniquely ADD among those
  candidates. It is not a hierarchy traversal; component-add order remains confounded with
  other registration histories, so no general add-order activation mechanism is adopted.
- First-wake plain/span controls: child Awake/OnEnable, then parent Awake/OnEnable. This is
  CHILD_INTERLEAVED in the stored fit, confounded with creation/recorder order and preservation
  only. Earlier parent-first controls remain separate; no universal first-wake direction follows.
- The stored SAME_PATH relations describe equal callback/state lists for one valid ID in
  this sequential matrix. They do not prove identical native implementations, cold-start
  paths, multi-ID ordering or atomicity.

## Capture protocol

```
python3 tests/projects/activationorder/preflight.py --rsp-dir <shaderpasscount native1 copy>/Library/Bee/artifacts/<hash>.dag --launch <its launch dir> --out <cache>/preflight [--identity-only]
python3 tests/projects/activationorder/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native1 --preflight <cache>/preflight/preflight.json [--dry-run]
python3 tests/projects/activationorder/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/activationorder/run.py publish --release 6000.0.71f1 --label <fresh>
python3 tests/projects/activationorder/analyze.py fit <cache>/<label>/native1/capture/values.jsonl --report <cache>/fit.json
```

The capture uses a windowed GPU Editor on the existing display through
`tests/editor_runner.py` with `EditorJob(batch=False, graphics=True, private_display=False)` and
`vglrun -d egl Unity -force-glcore -projectPath P -executeMethod ProbeRunner.Record`, without
`-quit` or `-nographics`. The runner is that runner with the probe names changed and the shader
compilation setting removed; it changes no Editor setting. Record opens the scene and enters Play
mode from a delayCall. Only the update handler leaves Play mode, after the finished code; it
then waits 30 Edit-mode updates, removes both handlers, closes its event output and quits. The
hard deadline is 1200 s. The external no-progress deadline is 300 s and is reset only by
`runner-events.jsonl` and the completed `values.jsonl`. Do not wrap `run.py` in with-heavy-job.

A repeat directory is write-once. `native2` starts only after `native1` passes `check`, which
also compares the actual compile graph with the preflight. `publish` requires both repeats to
pass and agree byte for byte, and never overwrites.

`guards.py` refuses a capture for any of these:

* a forced stop of any kind, an exit other than 0, a non-NVIDIA renderer, no shutdown line, or a
  launcher failure;
* a command that is not the planned one, or prelaunch inputs (including the scene) that differ
  from the prepared fixture;
* runner events other than record/enter/finished 0/leave/exit 0, or the Editor-side watchdog;
* a partial record, compile errors or native faults in the Editor log, or a changed fixture asset;
* a record that does not follow the schema in order, or an operation request that is not the
  authored one;
* a set-up that could not build its nodes, a context that is not the release/windowed/OpenGLCore,
  unscoped error logs, or missing diagnostics.

No callback order, count, state, stray call or returned value is a guard.

`preflight.py` compiles both scripts with the Editor's own Roslyn and the response files of the
shader pass count batch GPU capture (`shader-pass-count-native-20261102a/native1`, admitted by
Root). It proves from that launch's receipt that its Packages/ProjectSettings are these bytes,
and that the copy changed only that runner's `m_AsyncShaderCompilation` afterwards. It also
proves that neither project has another compile input under Assets, and that each assembly
folder has one script. Only the source paths are rebound. `Controls/controls.py` compiles the
real scripts at C# 9 against fake Unity types and drives them through the runner protocol. The
fake answers are not Unity's.

## Limits

* One shape and the default execution order only. It covers no execution-order or type effect,
  scene-file wake order, component enable/disable, reentrancy, destruction, or multi-ID batch.
* Windowed Linux Editor with OpenGLCore only. The hypotheses are per operation: one answer
  says nothing about other lifecycle paths.
* The first-wake controls cannot separate child-first wake from creation or recorder order.

## Reference mode correction

The re-record changes exactly `context.isBatchMode` from true to false. All other
207 keys, including callback sequences, are byte-identical to the earlier reference.
The strict engine count decreases from 9/208 to 8/208 failing keys; the eight raw
cross-object callback-order differences remain. Controls pass 233/233. No callback
order or comparator rule changes. The Python runner creates no X server.
