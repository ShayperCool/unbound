# Initial-overlap query controls

The canonical Native recording is `Reference/unity-<version>.json`. The current
release has 6,256 observations covering initial TOI thresholds, shallow overlap,
zero/short range, both start-in-collider flags and cast direction. Two independent
Editor processes agreed before implementation. The accepted runtime matches this
suite with the unchanged query comparator; broader query gaps remain separate.

`UNBOUND_PROBE_UNITY_VERSION` selects both reference release and installed Editor;
`UNBOUND_PROBE_UNITY` overrides the executable. All generated projects, captures,
build logs and comparisons belong in the cache, never the source tree.

```bash
python3 tests/projects/query2dcontinuation/run.py prepare --label native1
python3 tests/projects/query2dcontinuation/run.py native --label native1
python3 tests/projects/query2dcontinuation/run.py prepare --label native2
python3 tests/projects/query2dcontinuation/run.py native --label native2
python3 tests/projects/query2dcontinuation/run.py record-reference --label native1 --repeat-label native2
python3 tests/projects/query2dcontinuation/verify.py
python3 tests/projects/query2dcontinuation/verify.py --actual /path/to/cache/engine.json
```

A default verify checks the reference only and says that no engine capture was
compared. Supplying an actual capture performs the original union-key comparison
and returns nonzero for retained failures. `capture_existing.py --label LABEL`
replays all query controls into QUERY_WORK, which defaults under
`$XDG_CACHE_HOME/unbound/validation/runs/query2dcontinuation/current`.
`report.py --work RUN --label LABEL` checks the inherited suites plus these initial
controls. Optional --baseline-work/--baseline-label reports complete deltas;
--no-regressions requires a baseline and still reports all retained failures.
Reports are cache-only. The private CLI adapter keeps fixture imports/caches out
of authored test projects. Configure `UNBOUND_PROBE_CLI` with a matching engine.
