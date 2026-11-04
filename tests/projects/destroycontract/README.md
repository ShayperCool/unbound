# Manual Destroy contract

This probe records manual `Object.Destroy` requests, synchronous callbacks,
returned object state and later delivery inside brackets around the default
PlayerLoop leaves. The reference is Unity 6000.0.71f1, LinuxEditor, batch GPU,
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

Neither batch repeat resumed WaitForEndOfFrame. `go.wfeof` is therefore
`NOT_ISSUED`, and coroutine-phase destruction is unmeasured. These captures also
do not establish whether a coroutine stops at a manual Destroy request.

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

The admitted pair used label `destroycontract-native-20261103a`. Its two launches,
publication and fit are spent. Its existing Roslyn preflight and 270 mock controls
were reused, not rerun. Any further capture requires a separate release.

Evidence lives under `validation/runs/destroy-native-witness-20261104/` and Root
`validation/runs/destroy-native-root-admission-20261104/` in the Unbound cache.
The accepted engine baseline remains manifest `d4078b07`, Core `fb2a7180`, suite
1661/1661 and API 0/2319. Those are earlier matching results, not runs of this probe.
