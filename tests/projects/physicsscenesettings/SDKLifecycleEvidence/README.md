# SDK lifecycle continuation

The parent's separate SDK experiment follows direct Native traces in
`../ContinuationEvidence/native-flags/`. The previous delegated brief limited
changes to the shim and managed engine; that limit did not prohibit parent SDK
work under the user's parity request.

Before runtime changes, TriggerFlagLifecycle.cs recorded 1,152 observations in
each of two independent Unity 6000.0.71f1 processes. Both files are byte-identical.
The private fixture enables all kinematic contact pairs and freezes dynamic
translation/rotation. Cases cover awake/sleeping D/D, K/D, K/K, K/static and ignored
pairs. Sixteen phases measure simulation/trigger conversion in both directions,
two triggers, same-frame round trips and role swaps, disable/re-enable, and
filter suppression/restoration. Counts, global callback order, raw contacts,
sleep state and positions are retained.

The unchanged frozen shim7 matches 992/1,152 exactly. Native contact-to-trigger
conversion delivers TriggerEnter/Stay followed by CollisionExit. The reverse
delivers TriggerExit before CollisionEnter. Overlapping trigger/trigger pairs
continue receiving Stay, including the role-swap controls. Existing raw-contact
and notification differences remain in the baseline recording. No expected
failure masks, exclusions or tolerance changes were introduced.

The new probe uses Native Physics.ContactEvent for raw contact counts and the
existing fixture-only reflection of real engine contacts. It does not claim to
implement the public ContactEvent stub. `manifest.json` records source, settings
and both independent output hashes. Raw logs and projects remain in
`/mnt/storage-hdd/node-b-caches-20261023/sdk-physx-parity-20261031/`.

Reproduce with `SCENE_PARENT_WORK` pointing to a private probes directory:

```bash
python3 tests/projects/physicsscenesettings/parent_probes.py prepare --probe flags
python3 tests/projects/physicsscenesettings/parent_probes.py native --probe flags --repeat 1
python3 tests/projects/physicsscenesettings/parent_probes.py native --probe flags --repeat 2
python3 tests/projects/physicsscenesettings/parent_probes.py engine --probe flags --label baseline7
```

The runner uses the shared heavy-job lock. For the baseline, select the immutable
parity-baseline-native7 CLI, its managed directory and its native library path.
