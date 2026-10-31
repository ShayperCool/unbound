# Query contact feature controls

The canonical Unity 6000.0.71f1 recording contains 181,440 observations in 4,320
scenarios. Two independent Editor processes in separate project copies produced
byte-identical captures before this reference was published. Other releases are
unmeasured.

The source varies circle, box and both capsule targets, five angles, four body
frames, three centers and nine offsets along each target axis. It records circle,
box and capsule casts together with the collider's exported shape vertices,
radii and world vertices. These distinguish a contact selection discrepancy from
a difference in the geometry or the frame supplied to collision detection.

```bash
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
export QUERY_CONTACT_WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/contact-controls"
python3 tests/projects/querycontactfeatures/run.py prepare --label native1
python3 tests/projects/querycontactfeatures/run.py native --label native1
python3 tests/projects/querycontactfeatures/run.py prepare --label native2
python3 tests/projects/querycontactfeatures/run.py native --label native2
python3 tests/projects/querycontactfeatures/run.py record-reference --label native1 --repeat-label native2
python3 tests/projects/querycontactfeatures/verify.py --actual "$QUERY_CONTACT_WORK/engine.json" --report "$QUERY_CONTACT_WORK/comparison.json"
```

`UNBOUND_PROBE_UNITY` optionally selects the Editor executable. Engine mode uses
`SCENE_SETTINGS_CLI` and its matching managed/native runtime. Captures and logs
are write-once and stay in the validation cache. Verification compares the union
of all keys using the existing query comparator without exclusions or changed
tolerances. A reference-only verification does not claim engine parity.
