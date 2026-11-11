# Manual Destroy contract

This probe records manual `Object.Destroy` requests, synchronous callbacks,
returned object state and later delivery inside brackets around the default
PlayerLoop leaves. The reference is Unity 6000.0.71f1, LinuxEditor, windowed GPU,
OpenGLCore on NVIDIA, with the built-in pipeline.

`Reference/unity-6000.0.71f1.jsonl` contains 1115 records. Two independent captures
passed the strict exit, renderer, input, compiler-graph, schema, marker and log
guards and were byte-identical. Both raw captures and diagnostics remain outside
Git under the validation cache. This reference admission makes no claim that the
engine implementation matches it.

## Observations

- Before a zero-delay Destroy call returns, the measured enabled and active
  MonoBehaviour has received OnDisable and reports enabled=false. The object and
  component remain alive and listed; Find answers, the GameObject's active state,
  parent and child membership remain unchanged. Component-only destruction keeps
  its GameObject alive.
- Already-disabled or inactive subjects receive no duplicate OnDisable. These
  subjects had previously awakened; never-active objects are not covered.
- In the single-child hierarchy, both OnDisable and OnDestroy visit the parent
  before the child. This manual-Destroy observation must be kept separate from
  DestroyImmediate or scene-unload callback order.
- Destroying the same GameObject again from its OnDisable returns without a
  second OnDisable or OnDestroy. Broader reentrancy is unmeasured.
- OnDestroy is deferred. The observed delivery brackets are:

| Request point | OnDestroy bracket | Relative frame |
|---|---|---|
| Update | `Update.ScriptRunDelayedDynamicFrameRate` | Same frame |
| LateUpdate | `PostLateUpdate.ScriptRunDelayedDynamicFrameRate` | Same frame |
| Marker after `PostLateUpdate.TriggerEndOfFrameCallbacks` | `FixedUpdate.ScriptRunDelayedFixedFrameRate` | Following frame |

The last row is a loop-marker request, not a coroutine EOF request. Its delivery
precedes the next driver Update, so the probe step still has its previous value.
Brackets establish observed intervals, not Unity's private implementation or the
order relative to every callback within a leaf.

The precommitted overall delivery fit is **NONE**. Update and LateUpdate each fit
their next-delayed candidate separately. The disabled-case mechanism remains
MULTIPLE. These results were retained without changing the hypotheses.

## Coroutine EOF and other limits

Both windowed repeats resume WaitForEndOfFrame 23 times, at steps 1-23, after
`PostLateUpdate.ObjectDispatcherPostLateUpdate`. The `go.wfeof` request is issued at
step 21, relative frame 22; OnDisable occurs before it returns and OnDestroy is
recorded in `FixedUpdate.ScriptRunDelayedFixedFrameRate` of relative frame 23.
These are observed brackets in this fixture, not a general scheduling guarantee.

The strict engine comparison is 887/1115 failing keys, previously 891/1115 against
the batch reference. Of these, 886 differ only in `cb.wfeof`, which is one higher
in Unbound. The `points` key contains one extra engine EOF resume at step 0,
relative frame 1; removing that point makes the sequences equal. First Game-view
rendering and coroutine startup need further controls; no engine behavior changes
or tolerance changes are included here.

Positive delays, DestroyImmediate, never-active objects, multiple siblings,
deeper hierarchies, other handler types/execution orders, non-script components,
custom loops and other releases/backends are outside this reference's coverage.

## Tools and evidence

`run.py` provides `editor`, `check`, `publish` and `unbound` modes with an explicit
release and write-once run label. `guards.py` checks completeness and run validity;
it does not gate the candidate mechanism or callback order. `compare.py` retains
the full ordered/raw comparison. `analyze.py` evaluates the precommitted models
separately from publication. Real-time values and raw IDs remain in hashed
diagnostics outside the compared record.

## Windowed reference replacement

The Python runner now uses the existing GPU display, without `-batchmode`, and
forces OpenGLCore. It creates no X server. The C# recorder is unchanged; its sources
still match the recorded compile preflight. Two independent Editor processes pass
the strict guards, exit normally on NVIDIA and produce byte-identical records.

Exactly 855 of 1115 reference keys change from the batch recording:

- 839 state keys change only `cb.wfeof`: 819 `snap.*` states and 20 `case.*` states
  whose newly resumed coroutine increments the counter.
- Nine states `snap.{22,23,24}.{update,late,teofc}.go.wfeof` now describe the
  destruction that the batch capture never requested.
- `case.go.wfeof.issued`, `case.go.wfeof.before.go.wfeof`,
  `case.go.wfeof.after.go.wfeof`, `trace.go.wfeof`, `order` and `points` now include
  that request or the resumed EOF phases.
- `context.isBatchMode` changes from true to false.

The other 260 keys and all comparator rules are byte-identical. `PREDICTIONS.json`
retains the precommitted hypotheses; only its existing core API README digest is
refreshed to the current file. `Controls/controls.py` passes all 270 controls.
