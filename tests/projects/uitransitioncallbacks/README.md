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

The complete callback gate passes for the measured Unity release: 718 samples, 3428 callbacks
and 82 cases match the Native reference byte-for-byte. The panel samples each property value
type across its owners before dispatching that type's events. Owner registration order is
independent for each type. Each event batch is frozen before callbacks run, so same-type
retargeting queues events for the next update while retargeting a later type affects this tick.
Layout validation and picking do not advance animations. Detaching installs animation
destinations while preserving the last layout; reparenting preserves already queued events.

The 5053 failing keys from the previous updater are resolved, with zero retained or introduced
failures. The passing complete union contains 78854 observations. The previous comparison
visited 77395 observations because mismatched event arrays represented some missing events
as single failure nodes. The comparator and its tolerances are unchanged. The older transition
gate also passes all 253 samples and 831 callbacks. These are focused transition results,
not a claim of full engine parity.

`diagnose.py --release <release> --candidate <capture> --before <baseline> --output <cache-report>`
retains every failure and changed key, and adds per-scenario counts by phase and field with the
first exact divergence. Missing and extra callbacks and samples remain in the complete union.
Older releases and other discrete/image/font combinations remain unmeasured.
