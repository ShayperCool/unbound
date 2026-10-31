# Collider skin lifecycle controls

These controls investigate the blocked query candidate. They do not certify it.
The accepted continuation remains `88a53c477dc43e646c21522f6c402c0cec98c6c1`.
The isolated diagnostic merge is `cd10d539`, on
`compat/query2d-skin-diagnostics-20261031`.

Two separate Unity 6000.0.71f1 Linux Editor processes, each using a fresh project
copy, produce byte-identical captures of 46 cases and 644 observations. Neither
process reads collider properties or performs a query between creating/sizing
the box at contact offset 0.01 and the tested global-offset transition.

The unchanged cast response after a global change is consistent with retaining
the geometry's earlier skin for sync, material/density assignments and unchanged
size/offset/scale assignments. Changed geometry, changed Transform pose,
disable/enable, deactivate/activate, trigger changes, body creation, cloning and
initial activation give the response associated with the new offset. This is an
inference from the recorded queries, not a direct read of Unity's internal skin.
The exact source and every value are retained. Other collider types, composites,
serialized loading and older Unity releases are unmeasured.

The frozen accepted runtime matches 473/644 observations. The diagnostic query
candidate matches 588/644, resolving 129 baseline failures, retaining 42 and
introducing 14. Its 56 failures and all rounding differences remain in
`Evidence/query-candidate-comparison.json`. These controls add to the query
delegate's earlier stress suites, which already contained 53 introduced values.
No key, failure or tolerance has been removed to accept either candidate.

The runner uses the existing broad-physics fixture's ProbeRunner and ProbeLog.
All generated projects and artifacts live outside authored repository assets:

```bash
export SKIN_WORK=/mnt/storage-hdd/node-b-caches-20261023/query2d-skin-parent-20261031/lifecycle-skin
python3 tests/projects/colliderskin2d/run.py prepare --label native1
python3 tests/projects/colliderskin2d/run.py native --label native1
python3 tests/projects/colliderskin2d/run.py prepare --label native2
python3 tests/projects/colliderskin2d/run.py native --label native2
```

Labels are write-once; use new labels to reproduce. Every run takes the global
heavy-job lock. Engine captures additionally set `SCENE_SETTINGS_CLI`,
`UNBOUND_MANAGED_DIR` and `LD_LIBRARY_PATH` to the matching private/frozen outputs.
Their paths and hashes are in `Evidence/artifact-manifest.json`; copies of logs
and Native captures are committed. The candidate managed build passed with zero
errors. Its build log remains in the private work directory.

```bash
python3 tests/projects/colliderskin2d/verify.py
```

The verifier checks all captured keys, original query tolerance and archived
file hashes. It confirms the documented rejection, rather than treating these
failures as passing. No production collider or contact-owner files changed here.
