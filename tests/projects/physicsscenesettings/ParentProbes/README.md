# Focused 3D controls

`parent_probes.py:SOURCES` maps thirteen probe names to these C# sources.
Preparation copies the project into the private `SCENE_PARENT_WORK` and replaces
its script there. The source files share the original probe's class name and
are not compiled together. Defaults use the per-probe run cache.

Canonical Native is `../Reference/unity-<release>/focused/<probe>.jsonl`.
`UNBOUND_PROBE_UNITY_VERSION` selects the release and Editor. The set includes
long triggers, registration barriers, immediate kinematic setters, contact and
component ordering, SDK flags and ignored pairs, active/inactive sleep, inactive
command sequences and both clone-placement paths. `../verify.py` checks every
source mapping and record count without private build artifacts.

`ContactOrder.cs` retains unsorted contact points, impulses and pre-simulation
state. Native uses Physics.ContactEvent; the engine reader uses fixture-only
reflection of the real PhysX stream. This does not certify the public engine
ContactEvent stub. Component barriers and registration barriers remain distinct
controls even when their Native values agree with another workload.

Run from the repository root, with the matching privately built engine selected
by `SCENE_SETTINGS_CLI` and `UNBOUND_MANAGED_DIR`:

```bash
fixture=tests/projects/physicsscenesettings
python3 "$fixture/parent_probes.py" prepare --probe triggers
python3 "$fixture/parent_probes.py" native --probe triggers --repeat 1
python3 "$fixture/parent_probes.py" native --probe triggers --repeat 2
python3 "$fixture/parent_probes.py" engine --probe triggers --label final
python3 "$fixture/compare.py" "$fixture/Reference/unity-${UNBOUND_PROBE_UNITY_VERSION:-6000.0.71f1}/focused/triggers.jsonl" \
  "${SCENE_PARENT_WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/physicsscenesettings/focused}/triggers-final.jsonl"
```

Every Unity/engine job runs through the shared heavy-job wrapper. All independent
Native repeats, engine outputs and logs remain in the private run directory.
Install a complete agreeing Native focused set explicitly with
`verify.py --record-focused`, as described in the parent README. Historical
rejected experiments are cached run data and are not additional Native oracles.
