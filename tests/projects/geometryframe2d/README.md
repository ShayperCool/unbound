# Collider body frames

This probe records local and world shape data, composite paths and cast contacts
for static, body, child and rotated-child frames. It varies small displacements,
collider offsets, reflected/nonuniform scale, large positions and nonplanar
parents. The canonical Unity 6000.0.71f1 reference contains 320 scenarios and
43,068 observations from two byte-identical independent Editor runs.

Run from the repository root. Each label gets a fresh project in the external
cache; the runner wraps each Editor or engine process in `with-heavy-job`.

```sh
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/geometry-frames"
python3 tests/projects/geometryframe2d/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/geometryframe2d/run.py native --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/geometryframe2d/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native2
python3 tests/projects/geometryframe2d/run.py native --unity-version 6000.0.71f1 --work "$work" --label native2
```

`record-reference --label native1 --repeat-label native2` publishes an absent
release reference only after checking the release, authored inputs and complete
capture bytes. It refuses to replace an existing reference.

For an engine recording, prepare another label, select matching private managed
and native libraries through `UNBOUND_MANAGED_DIR` and `LD_LIBRARY_PATH`, then run
`engine --label <label> --cli <private-cli>` with the same release and work options.

```sh
python3 tests/projects/geometryframe2d/verify.py --unity-version 6000.0.71f1 \
  --actual "$work/final.json" --baseline "$work/before.json" \
  --report "$work/comparison.json"
```

Verification preserves the full union of keys and the existing query tolerance,
plus every exact difference. With `--baseline`, success requires zero introduced
failing keys; it still reports all retained failures. Without a baseline, any
failure returns a nonzero exit status. The probe includes unresolved behavior;
its reference and passing displacement tests do not establish geometry parity.
Run artifacts and rejected experiments stay in the cache. Other Unity releases
remain unmeasured.
