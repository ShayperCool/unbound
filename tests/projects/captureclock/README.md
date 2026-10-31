# Capture duration at startup

The probe assigns `Time.captureDeltaTime` in `Awake` and records every public float as IEEE-754
bits in the first three `Update` calls. Each canonical profile has two independently agreeing
Unity 6000.0.71f1 Editor captures. The requested durations are `1f/60`, `1024f/48000`, and `3f`.
The last profile has `captureFramerate == 0` while capture stepping remains active.

Choose a fresh cache label for each run. For example:

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/capture-clock"
python3 tests/projects/captureclock/run.py native --release 6000.0.71f1 \
  --work "$work" --label slow-native1 --duration 3
python3 tests/projects/captureclock/run.py native --release 6000.0.71f1 \
  --work "$work" --label slow-native2 --duration 3
cmp "$work/slow-native1/capture.jsonl" "$work/slow-native2/capture.jsonl"
python3 tests/projects/captureclock/run.py engine --release 6000.0.71f1 \
  --work "$work" --label slow-engine --duration 3 --cli /path/to/unbound --runtime /path/to/managed
python3 tests/projects/captureclock/verify.py --release 6000.0.71f1 --profile slow \
  --candidate "$work/slow-engine/capture.jsonl" --report "$work/slow-comparison.json"
```

For the other profiles use durations `0.01666666753590107` and `0.02133333310484886`.
The verifier compares every recorded field exactly and preserves the complete union. Optional
`--before` records resolved, retained and introduced differences. This probe does not certify
wall-clock timing, arbitrary capture durations, or other Editor releases.
