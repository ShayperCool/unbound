# Transition callback controls

Unity 6000.0.71f1. The probe replaces the runtime Panel millisecond clock through reflection,
just like `uitransitions`. Each sample explicitly applies styles, updates animations, then validates
layout. Callback records include presented values before and immediately after mutation, event
property names, elapsed time, target, observer, propagation phase and flags. Repeated ticks at the
same timestamp are intentional. No sleeps or frame timing tolerances are used.

82 cases cover sibling and parent/child ordering, reversed creation and style-change order,
same and different value types, staggered per-type starts, simultaneous run/start/end/cancel, and mutation in each event kind.
Mutations retarget self/another element, reverse a class change, remove a transition property,
detach, reparent, clear the root, register/unregister callbacks, read `worldBound` after mutation, or call public `IPanel.Pick` to exercise nested panel validation. This does not call the experimental animation API.
Two independent Native project runs must agree byte-for-byte before publishing the single release
oracle. Original repeats and all engine candidates stay in cache.

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/transitioncallbacks"
python3 tests/projects/uitransitioncallbacks/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitransitioncallbacks/run.py native --release 6000.0.71f1 --output "$cache/native-b"
cmp "$cache/native-a/capture/callbacks.jsonl" "$cache/native-b/capture/callbacks.jsonl"
python3 tests/projects/uitransitioncallbacks/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/private/unbound --managed /path/to/fresh/managed
python3 tests/projects/uitransitioncallbacks/verify.py --release 6000.0.71f1 \
  --candidate "$cache/engine/capture/callbacks.jsonl" --output "$cache/comparison.json"
```

Drivers wrap their Editor and engine children with `~/.cache/compat/with-heavy-job`; do not wrap
the drivers themselves. They run fresh cache copies, leaving authored projects untouched.
`compare.py` walks the complete ordered union, including extra keys, rows, values and callbacks.
Physical value arrays keep `2e-5` absolute / `2e-6` relative tolerances. All event metadata and
elapsed times are exact. `verify.py --before <capture>` also reports every resolved, retained and
introduced failure key. A partial match is an unaccepted control, never a release parity claim.
Older releases and other discrete/image/font combinations remain unmeasured.

## Acceptance status

The full callback gate is unaccepted. The canonical reference has 718 explicit-tick samples and
3428 callbacks across 82 cases. The unchanged runtime retains 5053 complete-union failures in
77395 observations, with zero resolved or introduced failure keys versus its independent baseline. Native measurements establish panel-wide property-value-type
phases, with a separate element start order for each type. Callbacks see updated values of the
current type on all owners, while later types and layout can still expose earlier values.
Retargeting from an earlier type can affect a later type on the same explicit tick. Reparenting,
property-list mutations and cancellation during dispatch require additional queue semantics.

The attempted grouped updater was rejected because it introduced failing observations. Its source,
runtime artifacts and full comparisons stay in cache. No runtime transition change is included here.
The dedicated managed tests retain only Native-derived behaviors the baseline already satisfies:
Run/Start retargeting of another element's later type, callback registration changes, and detachment
suppressing a pending Start. Passing these tests does not certify the full callback oracle.
