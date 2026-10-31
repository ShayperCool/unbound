# Solver body lifetime controls

The probe measures deletion/recreation, static/dynamic/kinematic transitions,
simulation toggles, activation and scene movement. Public observations use one
canonical Unity reference per release. Diagnostic traces and Native repeats stay
in cache. The current production solver still has unresolved differences.

Use `run.py prepare --release 6000.0.71f1 --work <fresh-cache-run> --label <label>`
for each independent project. `native` records public observations directly from
the selected Editor through the shared heavy-job wrapper. Independent Native
repeats must agree before publishing a canonical reference.

`engine --label final --project <prepared-label> --runtime <private-runtime-root>`
selects a matching private executable at `unbound` and assemblies/native libraries
in `managed/`. All modes also require `--release` and `--work`.

`verify.py --release <version> --candidate <capture> --baseline <prior-capture>
--output <cache-report>` preserves complete union comparisons and the original
tolerance. Baseline mode rejects introduced failures while reporting every
retained failure. `verify_order.py` can compare separately supplied diagnostic
island/body/constraint records. Independent repeats must first agree under
bijective pointer renaming only. Diagnostic capture tools and outputs stay
outside Git and are not required to record or compare the public probe.

Passing the lifetime unit tests does not certify the complete solver oracle.
