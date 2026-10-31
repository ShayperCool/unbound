# Public 3D contact reports

Use `run.py native|engine --unity-version 6000.0.71f1 --suite contacts|controls|geometry|capsule|sweeps --label <unique-label> --work <cache-run>`.
Each capture copies the authored project into a fresh cache directory. The runner wraps each Editor or engine process in `with-heavy-job`; do not wrap the runner again. Select a private ABI5 CLI with `--cli` and its matching managed runtime with `UNBOUND_MANAGED_DIR` for engine runs.

`Reference/unity-6000.0.71f1` contains one canonical Native set. Four contact profiles have independently byte-identical Native repeats of 6658, 7399, 7415 and 8156 rows. Geometry and layout repeats also agree exactly. Controls differ only in two process-dependent hash rows. Both raw hash rows remain in the canonical capture and comparisons.

`compare.py <reference> <candidate> --output <cache-report>` compares the complete union of keys, resolving raw object IDs only through registered identities. Without `--numeric`, comparisons are exact. `--numeric` applies the original absolute 1e-5 bound to physical data only. All missing and extra values remain failures. It never drops or sorts SDK contact points.

The probe covers the default physics scene, borrowed report arrays, synchronous jobs, identities, mesh face mapping, callback ordering, mutation, exceptions and recursive simulation. Negative `GetContactPointFaceIndex` indices dereference unchecked Native memory and crashed the original experiment. They are undefined and are deliberately not dereferenced again. Local scenes, outstanding asynchronous jobs and stale copied pointer dereferences remain unmeasured.

All outputs, Editor logs, private shims, managed builds, TRX files, comparisons and manifests belong under `$XDG_CACHE_HOME/unbound/validation/runs`. Native repeats stay there; only one agreed reference per release is tracked.

The capsule controls measure both creation paths and cylinder initialization. The component default is height 1, while capsule and cylinder primitives explicitly set height 2. All three axes retain a minimum half-segment of 5e-6, visible independently in bounds and axial raycasts. Ordered contacts distinguish the collider's local frame. The proposed local frame is an inference from those observations; Unity's internal implementation was not inspected.

`CapsuleSweeps.cs` measures 5610 values against capsule, sphere and box targets, with all capsule axes, heights 1/2/3, both endpoint orders and five offsets. Two independent Native captures agree byte for byte. These controls distinguish query quaternion rounding from collider geometry. `report.py` preserves exact comparisons as well as the original fixed-bound physical comparisons, with resolved/retained/introduced keys when `--baseline-label` is supplied. Sweep distances retain strict exact comparisons.

`verify_api.py --unity-version 6000.0.71f1 --runtime <hand-written-build> --work <new-cache-directory>` runs the API checker against a build made with `UnboundNoStubs=true`, then regenerates declarations in cache and compares the PhysicsModule file. It never generates into the repository.

The reusable broad capture driver is `tests/projects/query2dskinmerge/validate.py --work <cache-gates> --managed <candidate-managed> --matrix-baseline <previous-matrix> --skip-tests`. Set `UNBOUND_PROBE_CLI` to the matching private ABI5 executable. It wraps each child itself. Run the 497 original managed cases plus `PhysicsContactEventTests` separately, then pass that TRX, the API log and an explicit prior capture root to `query2dskinmerge/check_gates.py`. The full original union and comparator tolerances remain unchanged.

Only Unity 6000.0.71f1 Linux was measured. Other releases, non-unit capsule transforms, local physics scenes and asynchronous report lifetimes need separate measurements. This probe does not establish full engine parity.
