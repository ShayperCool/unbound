# Particle force-field rotation

The probe separates rotation speed and attraction, distance curves, field shapes,
simulation spaces, field order, direction/drag interactions and influence filters.
It uses manually emitted particles and fixed seeds. The canonical reference is one
agreed Unity Editor recording per release; independent repeats remain in cache.

From the repository root, choose a fresh cache directory for `--work`. Run
`run.py prepare` then `run.py native` for two distinct labels, passing
`--unity-version 6000.0.71f1 --work <cache-run> --label <label>` to each command.
The runner wraps its own Editor/engine process in `with-heavy-job`.

For engine capture, prepare another label and run `engine` with the same options
plus `--cli <private-cli> --runtime <matching-private-managed-directory>`.

```sh
python3 tests/projects/particleforcefield/verify.py --unity-version 6000.0.71f1 \
  --native-a "$work/native1.jsonl" --native-b "$work/native2.jsonl" \
  --actual "$work/final.jsonl" --baseline "$work/before.jsonl" \
  --output "$work/comparison.json"
```

Verification requires exact Native agreement and preserves every union key and
nested value. Physical values retain the original `2e-4` tolerances. Reports and
all disposable files belong in the Unbound cache. Randomized rotation curves and
rotation randomness remain unsupported and warn; other releases are unmeasured.
