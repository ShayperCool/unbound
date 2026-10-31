# 2D motion controls

`Sources/ProbeEntry.cs` runs the complete original broad sequence before the
expanded motion controls. Its canonical Unity 6000.0.71f1 Linux oracle is
`Reference/unity-6000.0.71f1/motion.json.gz`, containing 3,364,863 observations.
The obsolete isolated v1 sequence is archived outside Git. `MoveRotation.cs`
has the separate 36,000-observation `rotation.json.gz` oracle; `SkinEntry.cs`
uses the 644-observation oracle at
`../colliderskin2d/Reference/unity-6000.0.71f1.json`. Each retained oracle came from an
independently agreeing Native pair. Only the two large Unity oracles are gzip
compressed. Candidate captures and comparison reports are ordinary JSON.

`UNBOUND_PROBE_UNITY_VERSION` selects the reference release. `UNBOUND_PROBE_UNITY`
overrides its Editor executable. `UNITY_EDITOR` is also accepted. Run artifacts default to
`${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/physics2dmotion/current/`;
`MOTION_WORK` overrides it. Set `UNBOUND_CLI` and `UNBOUND_MANAGED_DIR` to the
engine under test. All heavy processes use `~/.cache/compat/with-heavy-job`.
Preparation copies the unchanged physics inputs into this external work directory.

```bash
python3 tests/projects/physics2dmotion/run.py prepare --label native1
python3 tests/projects/physics2dmotion/run.py native --label native1
python3 tests/projects/physics2dmotion/run.py prepare --label native2
python3 tests/projects/physics2dmotion/run.py native --label native2
python3 tests/projects/physics2dmotion/run.py engine --label final
python3 tests/projects/physics2dmotion/verify.py --suite motion --candidate "$MOTION_WORK/final.json"
```

Set `MOTION_SOURCE=MoveRotation.cs` when preparing the rotation project.
`skin.py` replays the skin controls. `commands.sh` shows the current capture and
comparison sequence without depending on archived shell environments or DLLs.

The comparator retains the 1e-3 relative numeric tolerance, compares the union
of all scenario and value keys, and reports every failure and passing numeric
difference. Verification exits nonzero on failures. Optional `--baseline`
specifies an actual previous candidate file and requires zero newly failing
observations while retaining all existing failures. Reports default to the
external `reports/` run directory; `--output` overrides it.

`report.py final --baseline baseline` compares the current motion capture against
a prior run and the canonical oracle. `--suite rotation` or `--suite skin`
selects the other controls. `--baseline-work` supplies a separate previous work
directory. `validate.py` keeps loading and managed-test checks.
`integration_verify.py` retains the full cross-probe integration gates with
explicit `--work`, `--baseline` and `--motion-report` paths. It records current
artifact hashes and requires real regression outputs and tests. It writes only
to its external `--output` directory.

The historical 45-value broad motion objective remained unresolved, with nine
bounce, 27 stack and nine kinematic failures. Contact/constraint/proxy ordering
and rotation candidates were rejected. Captured BoxCollider2D skin was accepted;
its old query diagnostic retained 171/644 failures. These are recorded limits,
not failure masks. No full engine parity claim follows from these controls.

Historical captures, rejected patches, source snapshots and reports are at
`${XDG_CACHE_HOME:-~/.cache}/unbound/validation/archive-20261031/tests/projects/physics2dmotion/`.
The archive is optional; Native oracle reads always use `Reference/`. Older
releases and non-Box skin transitions remain unmeasured.

Running `verify.py` without candidate inputs validates canonical JSON structure
and reports reference counts. It makes no engine parity claim. Supply `--work`
or `--candidate` to compare actual captures. `--references-only` selects this
check even when a work environment variable is set.
