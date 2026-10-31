# 2D cast and query controls

Canonical Unity data is in `Reference/unity-<version>/`: queries, geometry,
selection, selection-fine, boundary and isolated JSON files. Each is one Native
recording selected after two independent processes agreed. Version selection uses
`UNBOUND_PROBE_UNITY_VERSION`; `UNBOUND_PROBE_UNITY` can override the Editor path.

Run data defaults to `$XDG_CACHE_HOME/unbound/validation/runs/query2dparity/current`
(or `~/.cache/unbound/...`). `QUERY_WORK` can select an explicit private run.
Every capture label uses a separate project, so Native repeats can run in parallel.

```bash
python3 tests/projects/query2dparity/run.py prepare --probe queries --label native1
python3 tests/projects/query2dparity/run.py native --probe queries --label native1
python3 tests/projects/query2dparity/run.py prepare --probe queries --label native2
python3 tests/projects/query2dparity/run.py native --probe queries --label native2
python3 tests/projects/query2dparity/run.py record-reference --probe queries --label native1 --repeat-label native2
python3 tests/projects/query2dparity/report.py --references-only
```

Repeat for each probe. Explicit record-reference replaces that release's oracle
only after byte-identical repeats. Engine mode uses matching `SCENE_SETTINGS_CLI`,
`UNBOUND_MANAGED_DIR` and library paths. It writes captures to the run cache;
`compare.py REFERENCE ACTUAL REPORT` preserves the original union-key and 1e-3
geometry rule. Put REPORT in the run cache. `report.py --work RUN --label LABEL`
checks all six suites and writes complete comparisons outside Git.

Original broad query controls pass. Expanded controls retain initial contact
feature/normal, fine-selection and edge-shape grouping differences. Every missing,
extra and numerically failing value remains visible; no expected-failure masks or
tolerance changes are used. Other releases need their own Editor recordings.
