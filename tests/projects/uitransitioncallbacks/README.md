# Transition callback controls

Unity 6000.0.71f1. The probe replaces the runtime Panel millisecond clock through reflection,
just like `uitransitions`. Each sample explicitly applies styles, updates animations, then validates
layout. Callback records include presented values before and immediately after mutation, event
property names, elapsed time, target, observer, propagation phase and flags. Repeated ticks at the
same timestamp are intentional. No sleeps or frame timing tolerances are used.

78 cases cover sibling and parent/child ordering, reversed creation and style-change order,
same and different value types, staggered per-type starts, simultaneous run/start/end/cancel, and mutation in each event kind.
Mutations retarget self/another element, reverse a class change, remove a transition property,
detach, reparent, clear the root, register/unregister callbacks, or read `worldBound` after mutation
to request layout through the public API. This does not call the experimental animation API.
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
