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

Every Editor, build and engine command must use the shared exclusive heavy-job wrapper. On this
machine use `$HOME/.cache/compat/with-heavy-job` before each command below. Use
`timeout 180` after the wrapper. Build with `--disable-build-servers -m:1
-p:UseSharedCompilation=false /nodeReuse:false` so persistent .NET servers do not inherit the lock.

Create the committed scene, only when changing fixture setup:

```sh
Unity -batchmode -nographics -quit -projectPath "$PWD/tests/projects/uitransitions" \
  -executeMethod TransitionRunner.Create -logFile /tmp/ui-transitions-create.log
```

Record the native Editor oracle:

```sh
UITRANSITIONS_OUT="$PWD/tests/projects/uitransitions/Reference" Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/uitransitions" -executeMethod TransitionRunner.Record \
  -logFile /tmp/ui-transitions-oracle.log
```

Build `managed/Unbound.Host/Unbound.Host.csproj` to an isolated output directory and select it
through `UNBOUND_MANAGED_DIR`. Do not use the root CLI's gen-stubs or api-check in a worktree because
their compiled source paths point at another checkout. Run the fixture:

```sh
UITRANSITIONS_OUT=/tmp/ui-transitions-unbound UNBOUND_MANAGED_DIR=/path/to/own/managed \
  unbound run -p tests/projects/uitransitions --headless 100 --fixed-fps 60
python3 tests/projects/uitransitions/compare.py \
  tests/projects/uitransitions/Reference/transitions.jsonl /tmp/ui-transitions-unbound/transitions.jsonl
```

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

The managed engine and host were built into
`$HOME/.cache/unbound/validation/runs/uitransitions/managed`, using independent intermediate output.
Only the UIElements generated stub module was retained after local explicit-path generation.
The root CLI's gen-stubs and api-check were not used.

The existing `UnityEngine.Tests` UIElements filter passed 8/8. Final verification with
`--filter 'FullyQualifiedName~UIElements|FullyQualifiedName~Yoga'` passed 256/256.
The final transition comparison passed all 253 samples and 831 callbacks with zero differences.
The GPU `uitk` fixture preserved all
structure, styles, text and 11 interaction steps. Its record and all three PNGs matched an
independently built pre-change UI implementation exactly. Against native Unity, both builds retain
one 1-pixel final label-width difference and image mean channel differences of 0.04, 0.04 and 0.06.
Logs and independent outputs are under `$HOME/.cache/unbound/validation/runs/uitransitions/`.

The oracle covers a single animated element plus an inheriting child. It does not establish
cross-element ordering when several property types animate simultaneously, event-handler-driven
retargeting, every discrete/image/font property combination, or mixed background-size keywords.
Those cases need additional native measurements before claiming exhaustive transition parity.
The internal `StylePropertyAnimationSystem` API and experimental value-animation API remain stubs;
USS and inline style transitions use the implemented panel updater directly.
