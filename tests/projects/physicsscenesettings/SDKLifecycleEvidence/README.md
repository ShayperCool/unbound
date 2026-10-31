# SDK lifecycle continuation

Accepted runtime `9491a8bc` closes the full **5,055/5,055** scene-settings matrix
on rebuilt parent artifacts. There are 4,803 exact observations and 252 passing
motion-rounding records at the unchanged tolerance. All 28 retained island cases
are resolved, with no new failing observations. The original references and all
prior acceptance/rejection evidence remain intact.

The SDK preserves the existing broadphase element during a trigger flag change,
updates its type metadata and refilters interactions. Trigger/trigger pairs use
the normal trigger path. New trigger overlaps evaluate filter callbacks so an
ignored pair remains suppressed after re-enabling its collider. Box geometry,
material and flags update in place; registered activation and cloning realize
physics before script Awake. This also creates a short-lived clone's actor even
when no simulation or property read intervenes. Contact and trigger event states
remain distinct and dispatch in Native's measured order. Ignored pairs survive
Collider/GameObject deactivation and are forgotten on destruction.

The integrated build passes 318 managed tests, API-check with zero errors across
2,266 hand-written types, and all 80 graphics observations. Defaults 582/582,
controller 23/23, rayhits 3,203/3,203 and joints retain byte-identical outputs.
Forces pass 608/608 with changed passing rounding. Broad physics improves from
21/29 to 22/29 scenarios: all 85 previously failing stack values now pass, and all
3D scenarios pass. The remaining seven scenarios and 133 failing values are 2D
baseline differences; no previously passing value regresses.

All callbacks, their global order, raw contact counts, poses and IgnoreCollision
states match the added Native controls. Overall scores are 1,149/1,152,
1,437/1,440 and 1,845/1,848; each retains the same three pre-simulation sleeping
records. Immediate setters pass 15/15; the original contact-order control passes
276/276. The longer sleeping-trigger control remains 1,575/1,728 with its original
153 sleep-state differences. These failures are retained explicitly in
`accepted/summary.json`; no keys, tolerances or masks changed.

Main ABI remains 5, PhysX shim is 8 and shader cache remains 13. SDK archives now
use a versioned BLAKE3 key over source/header contents and compiler inputs rather
than timestamps. The complete SDK/CLI build passed in 2m53s; a second build reused
the SDK archive and completed in 32.47s. Final managed, API, test and graphics logs
and artifact hashes are under `accepted/`. Full engine parity remains open;
older releases, other collider geometry lifecycle cases and public ContactEvent
are not certified by these counts.

Reproduction uses matching rebuilt artifacts, the same shared heavy-job lock,
SCENE_WORK=$SDK_WORK/work and SCENE_PARENT_WORK=$SDK_WORK/probes. Run the three
original drivers, all focused probes with `--label integrated-sdk8`, the existing
regression driver, graphics probe and `sdk_report.py`. The latter requires all
5,055 values to pass, unchanged independent Native references, no introduced
focused/broad failures, and every trigger/contact callback observation to match.
Build and raw execution files remain under
`/mnt/storage-hdd/node-b-caches-20261023/sdk-physx-parity-20261031/`.

The sections below describe Native-first controls captured before runtime edits.

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

TriggerPairLifecycle.cs adds trigger/trigger separation, reentry and conversion
back to simulation, with 1,440 byte-identical observations in each Native run.
IgnoredPairLifecycle.cs adds GetIgnoreCollision and GameObject deactivation, with
1,848 byte-identical observations per Native run. Native preserves ignore state
through both Collider.enabled and GameObject.SetActive changes. Their complete
baseline outputs, exact source hashes and both Native hashes are preserved.

`experiments/sdk8-born/` retains the first SDK candidate's complete 21 workloads.
It fixed all original 28 differences but introduced 66 fresh-pair failures in
other profiles, so 4,989/5,055 passed. The runtime patch was the earlier born8
candidate plus the measured ScShapeSim flag correction. The engine still omitted
one imported clone's actor until a native-backed read; realizing activation before
Awake removed the allocation-history discrepancy. No rejected candidate was
integrated into the continuation branch.

`trace/` retains a noiseless diagnostic run matching all 278 Native observations,
with input/artifact hashes and the actual actor/element log. The first private ID
trace exited 139 before producing an oracle. The helper now skips contact-header
actors marked removed; the completed rerun exits zero. The initial log remains
named and hashed in the diagnostic summary.
