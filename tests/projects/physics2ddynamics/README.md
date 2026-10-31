# 2D contact observation phases

This probe retains the complete original `Probe2D.RunAll()` and `Probe3D.RunAll()`
sequence before 24 contact controls. The controls vary circle/box geometry,
rotation, approach speed and discrete/continuous collision detection. Each records
body state and `GetContacts` before and after each simulation, plus the collision
payload and a separate `GetContacts` read inside callbacks. Real world history is
preserved; the probe does not add dummy bodies or alter allocation state.

The Unity 6000.0.71f1 Linux reference has 24,082 values across 53 scenarios.
Two independent project copies produced byte-identical Native captures, SHA-256
`8350bd98896ccf6fbb7514aa033d15ef995712c8d63c463d6c3d4417fd7a9227`.
Only the agreed capture is retained in `Reference/unity-6000.0.71f1.json`.

For example, `phase/False/0/-12/False` enters on step 2. Both its collision
payload and callback query report point `(0.02, -0.066962)`, separation
`-0.153923988` and relative velocity `(-1.12539244, 12.1962)`.
The body has already been solved when its callback runs. Public contact data
therefore cannot all be reconstructed from that body's current pose and velocity.
The rotating-box and continuous controls remain separate observations; agreement
in this circle example does not establish a solver ordering rule.

```bash
export DYNAMICS_WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/contact-phases"
python3 tests/projects/physics2ddynamics/run.py prepare --label native1
python3 tests/projects/physics2ddynamics/run.py prepare --label native2
python3 tests/projects/physics2ddynamics/run.py native --label native1
python3 tests/projects/physics2ddynamics/run.py native --label native2
cmp "$DYNAMICS_WORK/native1.json" "$DYNAMICS_WORK/native2.json"
python3 tests/projects/physics2ddynamics/verify.py --suite dynamics --candidate "$DYNAMICS_WORK/native2.json"

# Select a private managed runtime and matching CLI explicitly.
export UNBOUND_CLI=/path/to/private/unbound
export UNBOUND_MANAGED_DIR=/path/to/private/managed
python3 tests/projects/physics2ddynamics/run.py engine --label baseline
python3 tests/projects/physics2ddynamics/run.py engine --label final
python3 tests/projects/physics2ddynamics/verify.py --suite dynamics \
    --candidate "$DYNAMICS_WORK/final.json" --baseline "$DYNAMICS_WORK/baseline.json"
```

`UNBOUND_PROBE_UNITY_VERSION` selects the release. `UNBOUND_PROBE_UNITY` or
`UNITY_EDITOR` selects the Editor executable. Every capture uses the shared
heavy-job launcher. Independent Native captures can run in parallel because
preparation creates separate project copies. Engine jobs are exclusive.

The verifier uses the existing union-key comparator and unchanged 1e-3 relative
numeric tolerance. All missing, extra and failing observations remain in external
reports. Without a candidate it checks reference structure only. This is a
measurement probe, not a claim that current dynamics match Unity. Older releases
remain unmeasured. Runtime experiments, Native repeats, traces and comparisons
belong in the validation cache, never this source directory.
