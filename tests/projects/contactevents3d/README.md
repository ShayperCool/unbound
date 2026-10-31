# Public 3D contact reports

Use `run.py native|engine --unity-version 6000.0.71f1 --suite contacts|controls|geometry|capsule --label <unique-label> --work <cache-run>`.
Each capture copies the authored project into a fresh cache directory. The runner wraps each Editor or engine process in `with-heavy-job`; do not wrap the runner again. Select a private ABI5 CLI with `--cli` and its matching managed runtime with `UNBOUND_MANAGED_DIR` for engine runs.

`Reference/unity-6000.0.71f1` contains one canonical Native set. Four contact profiles have independently byte-identical Native repeats of 6658, 7399, 7415 and 8156 rows. Geometry and layout repeats also agree exactly. Controls differ only in two process-dependent hash rows. Both raw hash rows remain in the canonical capture and comparisons.

`compare.py <reference> <candidate> --output <cache-report>` compares the complete union of keys, resolving raw object IDs only through registered identities. Without `--numeric`, comparisons are exact. `--numeric` applies the original absolute 1e-5 bound to physical data only. All missing and extra values remain failures. It never drops or sorts SDK contact points.

The probe covers the default physics scene, borrowed report arrays, synchronous jobs, identities, mesh face mapping, callback ordering, mutation, exceptions and recursive simulation. Negative `GetContactPointFaceIndex` indices dereference unchecked Native memory and crashed the original experiment. They are undefined and are deliberately not dereferenced again. Local scenes, outstanding asynchronous jobs and stale copied pointer dereferences remain unmeasured.

All outputs, Editor logs, private shims, managed builds, TRX files, comparisons and manifests belong under `$XDG_CACHE_HOME/unbound/validation/runs`. Native repeats stay there; only one agreed reference per release is tracked.
