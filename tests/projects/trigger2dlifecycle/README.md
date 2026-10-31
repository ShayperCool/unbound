# 2D contact lifetime and receiver order

`Sources/ProbeEntry.cs` measures callback flags, solid/sensor contacts,
awake/sleeping bodies, collider/body/object lifetimes, callback mutations and
moving static colliders. `Sources/ReceiverOrder.cs` measures receiver order,
including late Rigidbody2D attachment. Canonical Unity 6000.0.71f1 Linux
observations are `lifecycle.json` and `receivers.json` under
`Reference/unity-6000.0.71f1/`. Independent Native recordings agreed byte for byte
on 25,356 lifecycle and 1,152 receiver observations.

Select another release with `UNBOUND_PROBE_UNITY_VERSION`; use `UNBOUND_PROBE_UNITY`
to override its Editor executable. `UNITY_EDITOR` is also accepted. Outputs default to
`${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/trigger2dlifecycle/current/`.
`TRIGGER_WORK` or `LIFECYCLE_WORK` selects a work directory. Set `UNBOUND_CLI`
and `UNBOUND_MANAGED_DIR` for the engine under test. Editor, engine, build and
test commands use `~/.cache/compat/with-heavy-job`. Probe preparation copies
existing physics inputs into the work directory.

```bash
python3 tests/projects/trigger2dlifecycle/run.py prepare
python3 tests/projects/trigger2dlifecycle/run.py native --label native1
python3 tests/projects/trigger2dlifecycle/run.py native --label native2
python3 tests/projects/trigger2dlifecycle/run.py engine --label final
python3 tests/projects/trigger2dlifecycle/run.py prepare --probe receivers
python3 tests/projects/trigger2dlifecycle/run.py engine --probe receivers --label final
python3 tests/projects/trigger2dlifecycle/verify.py --work "$TRIGGER_WORK"
```

`verify.py --suite lifecycle --candidate /path/to/capture.json` verifies a
single candidate or Native repeat. Both owned suites require exact observations.
The shared comparator still uses union keys and its original 1e-3 numeric
comparison, reports all rounding and failures, and rejects callback identity,
phase, step, sleep or touching rounding. Failures produce a nonzero exit.
Reports go to the external `reports/` run directory; `--output` overrides it.

`report.py final --work ... --baseline-work ... --accept` checks all lifecycle,
receiver, timing, order and broad physics captures. It preserves the original
acceptance gates and every retained broad failure. The historical final run
passed the two owned suites exactly, timing 18,491/18,491 and order 5,720/5,720.
Broad physics retained 121 failures with zero new failures. Those failures are
not excluded from comparisons.

`validate.py` retains the focused, broad, matrix, visual and managed-test replay
groups. `MATRIX_ACCEPTED` supplies optional prior matrix outputs for byte checks.
`integration_verify.py --work ... --baseline /path/to/previous-physics.json`
retains the full integration verifier, writing to an external directory selected
by `--output`. It requires real regression captures, TRX files and the environment
paths for those captures. Artifact hashes describe the supplied run and do not
require historical DLL identities.

Historical run evidence is optional and outside Git at
`${XDG_CACHE_HOME:-~/.cache}/unbound/validation/archive-20261031/tests/projects/trigger2dlifecycle/`.
No Native oracle reads depend on that archive. Older releases remain unmeasured.

Running `verify.py` without candidate inputs validates canonical JSON structure
and reports reference counts. It makes no engine parity claim. Supply `--work`
or `--candidate` to compare actual captures. `--references-only` selects this
check even when a work environment variable is set.
