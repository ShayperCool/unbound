# Particle emission and playback controls

This probe records emission, playing, paused, stopped, self/child alive, module
enabled, count and time together. The matrix covers disabled/enabled emission,
zero rate, time rate, stationary distance rate, bursts, empty/live particles,
start delay, looping/nonlooping duration, repeated module and rate setters,
Play/Pause/Stop/Clear/Simulate, manual Emit and hierarchy propagation. Opaque
PlaybackState snapshots are tested by restoring them between different public
playback states and observing the public getters, rather than comparing bytes
from platform-specific structs.

Choose `work` under the Unbound cache and a distinct label for each recording:

```sh
python3 tests/projects/particleemission/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/particleemission/run.py native --unity-version 6000.0.71f1 --work "$work" --label native1
```

Repeat with `native2` in a separate prepared project. The runner wraps its own
Editor or engine child in `with-heavy-job`; do not wrap the runner again. For
engine captures, prepare another label, then run `engine` with `--cli` and
`--runtime` pointing to matching private artifacts.

```sh
python3 tests/projects/particleemission/verify.py --unity-version 6000.0.71f1 \
  --native-a "$work/native1.jsonl" --native-b "$work/native2.jsonl" \
  --actual "$work/final.jsonl" --baseline "$work/before.jsonl" \
  --output "$work/comparison.json"
```

The canonical reference has 16,573 records and was published only after exact
agreement between independent Unity 6000.0.71f1 Linux recordings. The verifier
keeps the full union, every nested discrepancy and the lifetime control's
existing absolute/relative tolerance of `2e-4`. Boolean flags and counts compare
exactly. A retained discrepancy still fails verification; it is not waived.

Native reports emitting during start delay and at zero rate while playing with
emission enabled. Disabling emission changes the emitting observation without
changing playback, time or count. Enabling it does not resume a paused system or
undo StopEmitting. Clear ends playback when it removes the last particles after
emission has stopped. Emit(0) does not start a stopped system. PlaybackState
restoration preserves the current public playback flags and module switch.

The expanded controls preserve every original matrix value and add emitted
particle lifetimes and seeds, exact and split lifetime boundaries, restored
playback, fixed stepping, repeated 0.02-second calls and long restarted calls.
At rate 20, the identical unit fixture and public calls produce 19 particles
after 50 steps and 29 after 75 steps: the float playback clock is just below
1 and 1.5 seconds. These are measured expectations, not a changed tolerance.

Constant-acceleration controls cover three maximum step settings and 321 long
call inputs, including adjacent float perturbations around one, two and three
seconds. Their position and velocity fingerprints agree exactly with subtracting
each nominal subdivision from the call's remaining time, clamping a shortened
last step and consuming a positive residual only while it exceeds one microsecond.
This describes equivalent measured arithmetic; the Native internal mechanism is
an inference. Separately, non-fixed calls below ten microseconds do not advance,
while the boundary itself and its next float do. The canonical set keeps the
observations on both sides of those boundaries.

Rate controls cover both one-step calls and the default subdivisions, including
loop crossings at rates 4, 10, 20 and 9.7. All 264 count observations match
accumulating the rounded float `rate * (endTime - startTime)` for each subdivision
and retaining its fractional remainder across calls and loops. An exact whole
birth at an exclusive duration boundary remains pending for the next loop. The
same count controls with and without particle writes separate emission timing
from buffer compaction. Snapshot controls retain the accumulator through the
existing opaque playback-state API.

Stopped-simulation controls (`stoppedSimulate/<mode>/<source>/<delay>`) start from a
fresh system, `Stop` with either behaviour before or after it played (with the
GameObject inactive or active), or a paused one, then make two non-restarting
`Simulate` calls, `Play` and one more call; rate, delayed rate and burst emission
each. `stoppedResume/*` resumes emission after one stopped call of 0.3 to 1.3 s
with 0.1 to 0.7 s calls, crossing the loop end or not. `stoppedLongStep/*`
raises `Time.maximumParticleDeltaTime` above the start delay left, and
`disabledBurst/*` turns the emission module off across a single and a repeating
burst. `mixedRemainder/*` alternates fixed-step and variable-step calls and adds a
fresh delayed playback with a long step; `stoppedLongStep/*/afterPlay{2,3,4}` follow
a resumed system with delay left over longer and frame-length calls; `stoppedChild/*` stops a never-played
Birth sub-emitter with its parent. The Editor runs in batch mode on the GPU (`run.py native` uses
`tests/editor_runner.py`); two captures were identical and the earlier 16573
records are byte-identical.

Only this Editor release is measured. The expanded observations also retain
independent random-seed gaps. The verifier
reports every discrepancy; passing the original lifetime control does not
establish full particle parity.
