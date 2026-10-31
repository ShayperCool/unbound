# 3D scene settings and lifecycle probes

The canonical Unity recordings live in `Reference/unity-6000.0.71f1/`.
`UNBOUND_PROBE_UNITY_VERSION` selects a different release directory and matching
Editor installation. `UNBOUND_PROBE_UNITY` can select an explicit Editor path. A missing release fails instead of falling back to another
oracle. The measurements currently cover Unity 6000.0.71f1 on Linux.

The JSONL schema is one `{"key": ..., "value": [...]}` record per observation.
Keys are unique. The comparator includes the union of both key sets, rejects
missing/extra/duplicate records and non-finite numeric values, and retains the
original relative tolerances: 1e-3 for `/motion`, 1e-6 for other fields.
No failures are excluded.

## Source and oracle correspondence

| Source and runner | Canonical files | Records |
| --- | --- | ---: |
| `Sources/original/`, `run.py`, 13 `PROFILES` | `<profile>.jsonl` | 13 x 227 |
| `Sources/supplemental/`, `contactpairs.py` | `contacts/<profile>.jsonl` | 4 x 299 |
| Same supplemental source, `contactpairs.py --noise` | `islands/<profile>.jsonl` | 4 x 227 |
| `ParentProbes/`, `parent_probes.py` | `focused/<probe>.jsonl` | 33,552 |
| `ContinuationProbes/`, `continuation.py` | `continuation/<control>.jsonl` | 1,383 |

`parent_probes.py:SOURCES` lists all thirteen focused source mappings.
`verify.py` checks every file and its source/schema record count. The focused set
retains active and inactive sleep, command sequences, both clone-placement
controls, kinematic setters, long triggers, flags, trigger pairs, ignored-pair
lifecycle, contact order, component barriers and registration barriers. The
continuation set retains original, body-first, collider-first, disable-first and
Rigidbody-before-Collider creation controls. Diagnostic debugger traces and
repeated captures are disposable outputs, not additional oracles.

`Profiles/` preserves the original DynamicsManager inputs. Preparation writes
new profile snapshots into the private work directory. The original and
supplemental C# snapshots stay separate because their workloads differ.
`m_EnablePCM` is absent from this Editor's serialized settings; the injected
legacy field does not establish a measured PCM-off mode for this release.

## Run and compare

All default outputs go below
`${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/physicsscenesettings/`.
Explicit `SCENE_WORK`, `SCENE_PARENT_WORK`, `KD_WORK`, `SDK_WORK`, `SLEEP_WORK`
and `INACTIVE_WORK` select private replay directories. Logs, Native repeats,
engine recordings, summaries, hashes, builds and experiment patches stay there.
Heavy Unity/engine jobs use `~/.cache/compat/with-heavy-job`.

These checks work from a fresh checkout without Unity, private binaries or old
run archives:

```bash
python3 tests/projects/physicsscenesettings/verify.py
python3 tests/projects/physicsscenesettings/test_compare.py
```

To record the original matrix and its supplements, choose a private work path
and run from the repository root:

```bash
export SCENE_WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/physicsscenesettings/matrix"
fixture=tests/projects/physicsscenesettings
python3 "$fixture/run.py" prepare
python3 "$fixture/run.py" native
python3 "$fixture/contactpairs.py" native
python3 "$fixture/contactpairs.py" native --noise
# Explicitly install the two agreeing Native captures as this release's matrix.
python3 "$fixture/report.py" --record-reference
# SCENE_SETTINGS_CLI and UNBOUND_MANAGED_DIR select the matching engine build.
python3 "$fixture/run.py" final
python3 "$fixture/contactpairs.py" final
python3 "$fixture/contactpairs.py" final --noise
python3 "$fixture/verify.py" --matrix-output "$SCENE_WORK"
```

For focused and continuation probes, prepare and record each source twice with
`parent_probes.py` or `continuation.py`. Install the agreeing captures explicitly
with `verify.py --record-focused <focused-run> --record-continuation <controls-run>`.
Use `--focused-output <run>` or `--continuation-output <run>` and `--label` to
compare selected engine captures. Use an empty `--label` for cached files named
`<probe>.jsonl` without a stage suffix. These comparisons return nonzero for every
retained numerical failure. For a new release, set the version before preparing,
recording and installing all three groups.

`cache.py` reuses a private Library while changing only the friction setting and
restoring it in `finally`. `regressions.py` runs the existing physics fixtures.
`parent_report.py`, `continuation_report.py`, `sdk_report.py`, `sleep_report.py`
and `inactive_report.py` preserve the historical stage gates and write reports
only to the run cache. Their historical engine baselines come from the read-only
migration archive through `tests/probe_paths.py`; canonical Native reads always
come from `Reference/`. The archive is optional for ordinary comparison and is
not committed. Historical full build audits require the matching private build
artifacts.

## Retained numerical failures

The original scene-settings final capture passes 4,782/5,055 records, with 4,530
exact and 252 within tolerance. Its 273 failures remain failures, including eight
introduced PGS island clone/contact records. Its baseline passes 4,111/5,055.
The later parent capture passes 5,027/5,055 and retains 28 failures. The later SDK
and inactive acceptance captures pass 5,055/5,055 with 4,803 exact records.
These are different engine stages against the same unchanged Native matrix.
Replay an explicitly selected cached stage to inspect its complete failures;
no earlier failure is reclassified as an expected pass.

The older sleep stage retains 732/1,050 inactive failures; the inactive stage
resolves them. Broad 2D fixtures retain their own numerical failures. Actor
creation history, long simulations, parallel-worker determinism, non-unit scale
and other Unity releases remain outside this fixture's measured guarantees.
