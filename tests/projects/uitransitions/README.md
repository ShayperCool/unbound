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
machine use `/home/shaypercool/.cache/compat/with-heavy-job` before each command below. Use
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
