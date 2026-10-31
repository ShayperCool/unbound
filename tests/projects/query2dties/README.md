# Initial query contact features

The single canonical oracle is `Reference/unity-<version>.json`. The current set
contains 75,168 observations in 2,592 cases, confirmed by two independent Editor
processes. It varies four collider shapes, three query shapes, three rotations,
three scales, four collider/body frames, two positions and nine center offsets.

The accepted runtime matches 72,439 values and retains 2,729 point/normal failures.
The unchanged union-key comparator reports all missing/extra keys and numeric
rounding values; no case is excluded. These controls distinguish tie choices near
segment centers from one repeated exact-center input. Other releases are unmeasured.

```bash
python3 tests/projects/query2dties/run.py prepare --label native1
python3 tests/projects/query2dties/run.py native --label native1
python3 tests/projects/query2dties/run.py prepare --label native2
python3 tests/projects/query2dties/run.py native --label native2
python3 tests/projects/query2dties/run.py record-reference --label native1 --repeat-label native2
python3 tests/projects/query2dties/verify.py --actual /path/to/cache/engine.json
```

Select the release with UNBOUND_PROBE_UNITY_VERSION and optionally its executable
with UNBOUND_PROBE_UNITY. QUERY_TIES_WORK defaults to the XDG unbound validation
cache. Each label creates an independent project and refuses output replacement.
Default verify checks only the reference; an explicit actual capture returns
nonzero for retained failures. Engine mode requires matching CLI/managed/native
artifacts. Reports and process logs stay outside Git.
