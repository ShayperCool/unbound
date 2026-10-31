# Euler factory precision

This probe records both `Quaternion.Euler` overloads without renaming or
normalizing their outputs. It retains each component, the normalized quaternion,
the squared norm and rotation of four vectors. Cases include tilted and mixed
angles, negative angles, neighbourhoods of zero, 90, 180 and 360 degrees,
arbitrary values and large numbers of complete turns.

The release-named canonical reference is published after two independent Unity
Editor recordings agree exactly. The comparator preserves the full missing,
extra and failing-field union, including quaternion signs and indexed vectors.
All captures, projects, input hashes and reports stay in cache.

```sh
WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/euler-local"
python3 tests/projects/quaternioneuler/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/quaternioneuler/run.py native --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/quaternioneuler/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/quaternioneuler/run.py native --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/quaternioneuler/run.py compare --release 6000.0.71f1 --work "$WORK" --label agreement --reference "$WORK/native1.json" --candidate "$WORK/native2.json" --output "$WORK/agreement.json"
```

The driver wraps each Editor or engine process with the shared job pool. Do not
wrap the driver again. Engine mode selects a prepared `--project` and a private
`--runtime`. Compare the replay using `--baseline` to retain every resolved,
retained and introduced value with exact numeric comparison.
