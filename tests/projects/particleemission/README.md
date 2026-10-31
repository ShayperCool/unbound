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

The canonical reference has 13,604 records and was published only after exact
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

Only this Editor release is measured. The control retains emission timing and
other playback discrepancies independently of the emitting observation; passing
the original lifetime control does not establish full particle parity.
