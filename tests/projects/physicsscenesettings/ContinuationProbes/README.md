# Continuation controls

`LifecycleOrder.cs` preserves ContactOrder's complete workload, with an optional
explicit teardown before DestroyImmediate(GameObject). `KD_CLEANUP` selects
`original`, `body-first`, `collider-first`, or `disable-first`. The runner installs
the source in a private project under `$KD_WORK/controls/`. It never rewrites the
canonical sources or references.

Two independent Native runs agree on all keys and values for each control.
Original, collider-first and disable-first have 276 identical Native records.
Body-first has 277 records and changes 45 values/keys against the original. Its
first kd manifold uses x=.5 for fresh/activate and x=.3 for clone, reversing the
original Native faces. Removing Rigidbody explicitly while retaining Collider is
therefore observable in subsequent pairs. Engine matching counts and all raw
records are in `../ContinuationEvidence/controls-summary.json`.

`trace_native.py` compiles a private diagnostic shim from a copy of the tracked
shim and matching, unchanged SDK archives. It adds no exports or ABI changes.
`KD_TRACE=1` logs rigid IDs, element IDs, kinematic flags and real callback actor
order. No SDK source or algorithm changes. Use a private copy of the frozen
managed output containing this shim, because the assembly-local native library
wins over LD_LIBRARY_PATH. The diagnostic run preserves all engine probe values.
The short kd trace and build hashes are archived; the full log path and hash are
in the summary. IDs and addresses are engine measurements, not Native ID claims.

For the failing fresh case, the engine has K rigid2/element7, D rigid4/element1.
Activated K has rigid1/element6, D rigid5/element2. The final callback actor order
is D,K, corresponding to the point on the kinematic face at x=.5. The clone also
has D,K and passes. These IDs follow shape attachment and the real dynamic to
kinematic transition, which reinserts the broadphase element.

Reproduction, from the repository root with private environment variables:

```bash
python3 tests/projects/physicsscenesettings/continuation.py prepare --cleanup original
python3 tests/projects/physicsscenesettings/continuation.py native --cleanup original --repeat 1
python3 tests/projects/physicsscenesettings/continuation.py native --cleanup original --repeat 2
python3 tests/projects/physicsscenesettings/continuation.py engine --cleanup original --label frozen7
```

Repeat for each cleanup selection. The runner wraps each Unity/engine execution
in `~/.cache/compat/with-heavy-job`. Wrap the trace compiler in the same lock.
The frozen complete 21-workload rerun matches accepted evidence exactly at
5,027/5,055, 4,775 exact, 252 passing motion rounding, and 28 retained failures.
The two original agreeing Native sets and accepted/rejected evidence are intact.
