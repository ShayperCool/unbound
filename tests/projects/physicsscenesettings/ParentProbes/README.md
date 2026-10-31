# Parent integration probes

These probes run outside authored projects. `parent_probes.py prepare` copies this
fixture into `SCENE_PARENT_WORK` and installs one alternative source. The source
files deliberately share the original probe's class name and are not Unity assets.

`TriggerLifecycle.cs` measures kk, ks, kd and static/static pairs across fresh,
activation and cloning lifetimes. Each case runs twelve manual simulations. It
records ordered callbacks and sleeping states, with stationary overlap, explicit
sleep/wake, a kinematic move, separation/reentry, and collider removal inside Enter.
Two independent Unity 6000.0.71f1 runs agree byte-for-byte on all 1,728 records.
Unity delivers Enter followed by Stay in the first simulation, and Stay throughout
sleeping overlap. Disabling the collider inside Enter still delivers the recorded
Stay of that simulation, then no further callbacks.

`RegistrationBarrier.cs` is the supplemental probe with an optional SyncTransforms
between Instantiate and disabling the source. With island history enabled, both
Native runs match the original 227-record `Reference/islands/default.jsonl`
byte-for-byte. The engine's barrier run still has the newly failing clone records.
Thus flushing the cloned object before source deactivation is insufficient to
explain or repair those differences.

`KinematicTransitions.cs` measures immediate setter effects on fresh, activated and
cloned bodies. Both Native runs agree on 15 records. Switching to kinematic clears
both velocities and puts the body to sleep immediately. Switching back wakes it,
with zero velocities. Consecutive true/false assignments retain both transitions
without requiring a simulation or SyncTransforms. The pre-fix engine matches only
the three initial records and preserves stale velocities in all twelve remaining
records, even after SyncTransforms.

The pre-fix engine matches 1,189/1,728 extended trigger records. Its 539 differences
include 193 callback records, 193 order records and 153 sleeping-state records.
The latter are a separate, retained discrepancy; fixing callback delivery does not
certify sleep state. The barrier run matches 204/227 records, with unchanged motion
tolerance. Original and focused comparisons retain every failing observation.

After correcting trigger delivery, all 1,152 callback and order records match
Unity exactly. The full recording matches 1,575/1,728; all 153 sleeping-state
differences remain. The focused 3D and 2D physics test run passes 36/36, including
new tests for sleeping overlap, reentry and collider removal during Enter.

Reproduction from the repository root:

```bash
export SCENE_PARENT_WORK="$HOME/.cache/compat/physics-scene-parent"
fixture="$PWD/tests/projects/physicsscenesettings"
python3 "$fixture/parent_probes.py" prepare --probe triggers
python3 "$fixture/parent_probes.py" native --probe triggers --repeat 1
python3 "$fixture/parent_probes.py" native --probe triggers --repeat 2
python3 "$fixture/parent_probes.py" prepare --probe barrier
python3 "$fixture/parent_probes.py" native --probe barrier --repeat 1
python3 "$fixture/parent_probes.py" native --probe barrier --repeat 2
# Use matching privately built managed output and shim7; main CLI ABI remains5.
export SCENE_SETTINGS_CLI="$CARGO_TARGET_DIR/release/unbound"
export UNBOUND_MANAGED_DIR="$SCENE_PARENT_WORK/managed"
export LD_LIBRARY_PATH="$SCENE_PARENT_WORK/native7"
python3 "$fixture/parent_probes.py" engine --probe triggers --label before
python3 "$fixture/parent_probes.py" engine --probe barrier --label before
python3 "$fixture/compare.py" "$fixture/ParentEvidence/Reference/triggers.jsonl" \
  "$SCENE_PARENT_WORK/triggers-before.jsonl"
```

All jobs use the shared heavy-job lock. Raw logs remain in SCENE_PARENT_WORK.
An initial trigger draft used a C# type test on Unity's missing-component sentinel,
threw MissingComponentException, and timed out before producing any recording.
Its log `triggers-native1.log` was superseded by the corrected successful run; the
initial exception remains in the execution transcript. This failure did not produce
reference data. The checked-in source uses Unity's null comparison instead.

`ParentEvidence/oracle-manifest.json` records hashes of both independent runs and
their exact sources. References and pre-fix outputs are preserved alongside it.
