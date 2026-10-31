# UI Toolkit transitions

This fixture drives a native Unity 6000.0.71f1 runtime panel through reflection. It replaces
`Panel.TimeSinceStartup` with a millisecond clock, applies styles, updates animations, and validates
layout at explicit times. The same script runs in Unbound. No sleeps, frame-time tolerances, or
rendering assumptions enter the numeric comparison.

The fixture records width, opacity, background color, margins, translation, scale and rotation;
TransitionRun/Start/End/Cancel property names, elapsed time, bubbling, trickling, target callback
phase and `AffectsProperty`. Cases include positive/negative delays, zero/negative durations,
multiple properties, `all`, shorthand matching, repeated list values, interruption, reversal,
detachment, display changes, removing the transition property, and all 23 timing functions.

Select the release explicitly and run the portable driver. It copies only authored fixture
inputs into a fresh directory under `$XDG_CACHE_HOME/unbound`, default `~/.cache/unbound`.
The driver wraps each Editor/engine child with the shared heavy-job wrapper; do not wrap the driver.
It never records into the tracked project. Native repeats stay in cache. The canonical oracle is
`Reference/unity-6000.0.71f1.jsonl`, renamed without changing its original bytes.

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/transitions"
python3 tests/projects/uitransitions/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitransitions/run.py native --release 6000.0.71f1 --output "$cache/native-b"
cmp "$cache/native-a/capture/transitions.jsonl" "$cache/native-b/capture/transitions.jsonl"
python3 tests/projects/uitransitions/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/private/unbound --managed /path/to/fresh/managed
python3 tests/projects/uitransitions/compare.py \
  tests/projects/uitransitions/Reference/unity-6000.0.71f1.jsonl "$cache/engine/capture/transitions.jsonl"
```

Build the managed host into a private cache output with matching assembly-local native libraries.
Use the heavy-job wrapper and `--disable-build-servers -m:1 -p:UseSharedCompilation=false
/nodeReuse:false -p:UnboundTarget=<cache>/build/`. Do not run root CLI stub generation in a worktree.
Creating the scene is an explicit setup edit, using `TransitionRunner.Create` in an independent
cache copy before copying the resulting Unity-compatible authored scene back.

## Measured behavior

The Unity reference contains 253 samples across 55 scenarios and 831 callback invocations. Numeric
comparisons use `2e-5` absolute / `2e-6` relative tolerance. Events, property order, callback order,
elapsed time, phases and propagation flags compare exactly. Width and margins compare after
Unity's pixel-grid rounding. Translation supplies unrounded easing samples at quarter points.

- An element's first style does not animate. Subsequent USS and inline changes do.
- A negative delay advances interpolation immediately. A delay at least as negative as the
  duration suppresses the transition and all its events.
- Zero and negative durations can still run with a positive combined delay. At completion they
  emit End without Start. The End payload retains the negative duration when one was supplied.
- Reversal shortens duration and negative delay by the progress already travelled. Event payloads
  retain the original negative delay. Interruption starts from the current presented value.
- Changing transition timing while keeping the target does not restart a running transition.
  An unrelated ancestor restyle also leaves it running.
- Starting and cancelling the same property before an animation update sends no events.
- Removing a transition property snaps to the target and sends Cancel. Detaching drops the
  animation without Cancel, retains its last layout, and reattachment installs the target.
  `display: none` leaves the transition running. Disabled elements receive transition events.
- Transition events bubble. Ancestor trickle callbacks do not run, but target trickle callbacks
  run in TrickleDown followed by target/ancestor BubbleUp callbacks.
- Inherited color follows the parent's presented value. Pixel/percent widths use the parent size;
  translation and transform-origin conversion use the animated element's own size.

## Verification and limits

The accepted comparison has 253 samples and 831 callbacks with zero differences. See the
commands above to replay it with the exact private runtime under review. Comparisons list every
missing, extra or failing observation, including trailing samples and values.

The oracle covers a single animated element plus an inheriting child. It does not establish
cross-element ordering when several property types animate simultaneously, event-handler-driven
retargeting, every discrete/image/font property combination, or mixed background-size keywords.
Those cases need additional native measurements before claiming exhaustive transition parity.
The internal `StylePropertyAnimationSystem` API and experimental value-animation API remain stubs;
USS and inline style transitions use the implemented panel updater directly.
