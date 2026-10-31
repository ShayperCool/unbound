# Integrated scene settings validation

The parent combines the delegate's `0931158f` candidate with trigger correction
`e407dcdf` and kinematic lifecycle correction `841a2135`. The original candidate
and its evidence remain intact. Main protocol ABI is 5, PhysX shim is 7, shader
cache format remains 13. No PhysX SDK algorithm was changed.

The rebuilt parent CLI, managed output and native library match **5,027/5,055**
observations across all 21 workloads. Of these, 4,775 are exact and 252 have motion
rounding within the original 1e-3 tolerance. No failing observation was introduced
relative to the frozen shim6 baseline. Native pairs agree on all 5,055 values.
All 13 original profiles and all four supplemental contact profiles pass fully.

The retained 28 failures are the prior kd fresh/activation island-history
differences: eight each in PGS default/determinism and six each in TGS/TGS-determinism.
All clone observations now pass. The delegate had 273 failing records, including
eight introduced clone records; the parent resolves 245 of those failures.
Every remaining failure, every passing rounded value and all raw outputs are in
`accepted/summary.json`, `accepted/all-nonexact-values.json` and the sibling JSONL
directories. No keys, tolerances or expected-failure masks changed.

Trigger Stay now follows Enter in the same simulation and persists for sleeping
overlap. Ordered callbacks also match when Enter disables its collider. The
extended Native pair agrees on 1,728 values; the integrated engine matches all
1,152 callback/order records and 1,575/1,728 overall. The 153 retained sleeping-state
differences concern immediate MovePosition activity and Transform+Sync wake.
These remain separate from trigger delivery.

The immediate kinematic setter probe passes 15/15 exactly. Existing bodies perform
both sleep/wake transitions for consecutive true/false assignments; switching to
kinematic clears velocities, and switching back wakes the body. Unrealized bodies
retain their original lazy allocation order. Activation/cloning applies real
zero-displacement kinematic activity and settles without artificial velocity.
The physics unit run passes 41/41; the integrated physics/UIElements/Yoga/Canvas
run passes 302/302. API-check reports zero errors in 2,266 hand-written types.

All six regression outputs are byte-identical to the delegate's independently
measured baseline: project defaults 582/582, controller 23/23, forces 608/608,
rayhits 3,203/3,203, broad physics 21/29, and joints with its documented differences.
The eight broad-physics differences are retained. No real game project was run or
modified.

`accepted/summary.json` records hashes of the rebuilt CLI, CoreModule and shim7.
Raw build, API, test and execution logs remain under
`~/.cache/compat/physics-scene-parent/`. Rust CLI build took 1m09s; managed build
took 30.51s with zero errors. `tests/integrated-final.trx` preserves the 302-test run.
Focused sources, independent Native hashes and reproduction commands are in
`../ParentProbes/README.md` and `oracle-manifest.json`.

The parent preserved matching immutable baseline artifacts for subsequent tasks at
`~/.cache/compat/parity-baseline-native7/`: CLI, full managed output, shim7 and a
SHA256 manifest for integrated source `4df2ae4d`. Set UNBOUND_MANAGED_DIR to its
`managed/` directory and LD_LIBRARY_PATH to the snapshot root. The previous shim6
snapshot remains untouched. Neither snapshot is a mutable build output.

Reproduce from the repository root with matching rebuilt artifacts:

```bash
export SCENE_PARENT_WORK="$HOME/.cache/compat/physics-scene-parent"
export SCENE_WORK="$SCENE_PARENT_WORK/work"
export SCENE_SETTINGS_CLI="$CARGO_TARGET_DIR/release/unbound"
export UNBOUND_MANAGED_DIR="$CARGO_TARGET_DIR/managed"
export LD_LIBRARY_PATH="$UNBOUND_MANAGED_DIR"
fixture="$PWD/tests/projects/physicsscenesettings"
python3 "$fixture/run.py" prepare
python3 "$fixture/run.py" final
python3 "$fixture/contactpairs.py" final
python3 "$fixture/contactpairs.py" final --noise
python3 "$fixture/regressions.py" final
python3 "$fixture/parent_probes.py" prepare --probe kinematic
python3 "$fixture/parent_probes.py" engine --probe kinematic --label integrated
python3 "$fixture/parent_probes.py" prepare --probe triggers
python3 "$fixture/parent_probes.py" engine --probe triggers --label integrated
python3 "$fixture/parent_probes.py" prepare --probe order
python3 "$fixture/parent_probes.py" engine --probe order --label integrated
python3 "$fixture/parent_report.py"
```

Every heavy command uses the shared exclusive lock. `parent_report.py` requires
both agreeing Native recordings, complete 5,055-record coverage, no introduced
failures and all six unchanged regression outputs before archiving acceptance.

Rejected experiments are preserved under `experiments/`. Eager runtime
AddComponent allocation fixed the focused clone probe but introduced 96 failures
in the complete matrix. Eager allocation inside isKinematic passed the scene
matrix but broke broad 3d.kinematic. Neither behavior is accepted. The successful
implementation keeps allocation order and type-change timing separate.

Full engine parity is open. Public Physics.ContactEvent remains a stub; the
supplemental raw-contact probe uses fixture-only reflection on the engine side.
Older releases, unsupported settings and the retained island/sleep cases are not
certified by these passing counts.
