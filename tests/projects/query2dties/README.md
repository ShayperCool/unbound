# Initial query contact features

Two fresh Unity 6000.0.71f1 Linux processes agree byte for byte on 75,168
observations in 2,592 cases. The source varies four collider shapes, three query
shapes, three rotations, three scales, four body/collider frames, two world
positions and nine displacements across each center tie. Original recordings,
logs and complete union-key comparisons are retained in Evidence.

The frozen accepted runtime matches 72,031 observations. The query continuation
matches 72,439, including 62,425 exact values. It resolves 408 prior failures,
retains 2,729 and introduces zero. Combining captured box skin and the polygon
contact-selection correction leaves all 75,168 values identical to that query
continuation. The remaining failures include frame-dependent normal choices at
and near the segment center, not just repeated casts from one origin.

The pair's SHA256 is
`abadf3d5848e9b1250d1b3270f260784ac99c286fb504961d11d259975bf7858`.
No expected value, missing key or failing observation has been excluded. The
historical query comparator retains its original 1e-3 relative geometry rule;
all nonexact passing values are also archived. Older releases are unmeasured.

```bash
export QUERY_TIES_WORK=/mnt/storage-hdd/node-b-caches-20261023/query2d-ties-parent-20261031
python3 tests/projects/query2dties/run.py prepare --label NEW_NATIVE_LABEL
python3 tests/projects/query2dties/run.py native --label NEW_NATIVE_LABEL
```

Each label creates a separate private project and refuses to replace outputs.
Native runs use the global heavy-job lock. Engine mode additionally requires
matching SCENE_SETTINGS_CLI, UNBOUND_MANAGED_DIR and LD_LIBRARY_PATH. The accepted
capture uses the frozen lifecycle2d-native8 runtime; query-start uses the private
query-continuation managed-final output. Polygon uses the parent's private
managed-polygon output. Exact paths and hashes are recorded in the parent
query2dskinmerge evidence. No authored real-game files were accessed.
