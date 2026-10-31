# Particle lifetime boundary

The probe disables emission and shape, then writes manually constructed particles
with unique seeds. It records input getters, immediate output, buffer order, both
custom-data streams, playback flags, array and NativeArray overloads, slices and
roundtrips. It also records the exact public setup used by the stopped Emit unit test.

For each independent capture, choose a distinct label and run from the repo root:

```sh
python3 tests/projects/particlelifetime/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/particlelifetime/run.py native --unity-version 6000.0.71f1 --work "$work" --label native1
```

`work` must be under the Unbound cache. The runner wraps its own Editor or engine
child in `with-heavy-job`. Prepare another label for engine capture, then use
`engine` with `--cli <private-cli> --runtime <matching-private-managed-directory>`.

```sh
python3 tests/projects/particlelifetime/verify.py --unity-version 6000.0.71f1 \
  --native-a "$work/native1.jsonl" --native-b "$work/native2.jsonl" \
  --actual "$work/final.jsonl" --baseline "$work/before.jsonl" \
  --output "$work/comparison.json"
```

Only publish a canonical reference after exact independent Native agreement.
The verifier retains every union key and nested difference. Input getters, seeds,
counts and custom data compare exactly as float32 values; exported physical values
use absolute and relative tolerances of `2e-4`. Replays can omit both Native arguments
to use the release-selected canonical reference.

Unity 6000.0.71f1 removes nonpositive input lifetimes before age encoding. Tiny
positive inputs survive even if they export as zero; writing that zero back removes
them. Exhaustive deletion masks for lengths 1 through 9 and offsets 0 through 3
match four-slot groups aligned to the full buffer. Each pass removes marked slots
from high to low with swap-back, then revisits the group. Custom data moves with
the slot. This describes measured output order; an internal SIMD mechanism is
only an inference. Offset writes retain the prefix and truncate at
the written slice's end. Lifetimes exceeding start lifetime export at birth.
Other releases and nonfinite lifetimes are unmeasured.
