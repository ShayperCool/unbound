# 2D trigger timing and callback order

`Sources/ProbeEntry.cs` covers sensor/contact timing, mixed layouts, sleep,
transforms and teardown. `Sources/OrderEntry.cs` covers callback order. The
canonical Unity 6000.0.71f1 Linux observations are `timing.json` and `order.json`
in `Reference/unity-6000.0.71f1/`. Independent recordings agreed byte for byte.
The obsolete first timing control is archived outside the checkout.

Set `UNBOUND_PROBE_UNITY_VERSION` to select another release's reference directory.
Set `UNBOUND_PROBE_UNITY` to override that release's Editor executable.
`UNITY_EDITOR` is also accepted. New recordings
stay outside Git until explicitly reviewed as replacement Unity references.

Run artifacts default to `${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/trigger2dtiming/current/`.
`TRIGGER_WORK` overrides this directory. Set `UNBOUND_CLI` and
`UNBOUND_MANAGED_DIR` for the engine being measured. `SCENE_SETTINGS_CLI` is also
accepted. Engine and Editor commands use `~/.cache/compat/with-heavy-job`.
Preparation copies the physics project's original inputs to the work directory.

```bash
python3 tests/projects/trigger2dtiming/run.py prepare
python3 tests/projects/trigger2dtiming/run.py native --label native1
python3 tests/projects/trigger2dtiming/run.py native --label native2
python3 tests/projects/trigger2dtiming/run.py engine --label final
python3 tests/projects/trigger2dtiming/run.py prepare --probe order
python3 tests/projects/trigger2dtiming/run.py engine --probe order --label final
python3 tests/projects/trigger2dtiming/verify.py --work "$TRIGGER_WORK"
```

`verify.py --suite timing --candidate /path/to/timing.json` compares a single
capture, including an independent Native repeat. Its relative numeric tolerance
is unchanged at 1e-3. It compares the union of scenario and value keys, reports
missing and surplus keys, and retains all failures and passing rounding records.
Callback identity, phase, step, sleep and touching observations must be exact.
A comparison with failures exits nonzero. An explicit `--baseline` file or
`--baseline-work` directory instead gates on newly failing observations and
reports every retained failure. JSON reports go to the external `reports/`
run directory unless `--output` specifies another cache path.

`report.py --work ... --baseline-work ... --label final` compares timing, order
and broad physics before and after. The historical timing implementation retained
216 timing and 160 order failures; the lifecycle continuation resolved those.
These counts document the old run, not failure exclusions.

`replay_matrix.py` and `matrix.py` replay the same 21 scene profiles using the
scene probe's versioned Unity references. `--work` selects the matrix output
directory. Optional `--accepted /path/to/previous/matrix` additionally requires
all outputs to match that prior run byte for byte. `integration_report.py`
preserves the old full timing integration gates with explicit `--work` and
`--output` directories. It requires actual regression outputs and TRX files,
and records artifact hashes from that supplied run.

Historical logs, rejected patches and repeated captures were moved to
`${XDG_CACHE_HOME:-~/.cache}/unbound/validation/archive-20261031/tests/projects/trigger2dtiming/`.
They are optional migration records. Native oracle reads always use `Reference/`.
These measurements cover the recorded Linux Editor release only.

Running `verify.py` without candidate inputs validates canonical JSON structure
and reports reference counts. It makes no engine parity claim. Supply `--work`
or `--candidate` to compare actual captures. `--references-only` selects this
check even when a work environment variable is set.
