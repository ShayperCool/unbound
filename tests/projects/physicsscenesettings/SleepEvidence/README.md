# Kinematic sleep continuation

The managed change sends each kinematic target when MovePosition, MoveRotation or
Move is called. PhysX wakes immediately and retains the current pose until the
simulation consumes that target. Commands merge position and rotation until that
simulation. Rigidbody pose setters replace their target component; a changed
Transform followed by SyncTransforms replaces the entire target. A teleport sends
a target at the new pose, producing Native's measured activity period with zero
velocity. IsSleeping continues reading the actual PhysX state.

The fix uses the frozen ABI5 CLI and copied shim8 library without native or SDK
changes. Dynamic Move commands apply their pose immediately. Sleeping dynamic
bodies retain an unpublished Transform after Move followed by Sleep, matching the
control. Actor destruction clears pending command merging state.

Before implementation, KinematicSleep.cs produced two byte-identical independent
Unity 6000.0.71f1 Linux recordings of 20,700 records each. The SHA256 of both is
`56f6e64820a14cef251c13d33594641c25d0ea3f9eeb0133a5e9091caafae57a`.
Twenty-five command sequences cover fresh, initially inactive then activated,
reactivated and cloned kinematic bodies, awake dynamic controls and sleeping
dynamic controls. Each has twenty settling steps before and after its command.
Position, rotation, Transform pose, velocities and sleep are recorded. The frozen
baseline passes 18,688/20,700; the final output is byte-identical to Native.

The original TriggerLifecycle oracle improves from 1,575/1,728 to 1,728/1,728
exact. Its nine pre-simulation MovePosition records and 144 Transform sync sleep
records are resolved. The flag, trigger-pair and ignore-lifecycle controls pass
1,152/1,152, 1,440/1,440 and 1,848/1,848 exactly, resolving their three initial sleep
records each. Immediate setters remain 15/15. Contact order remains 276/276;
registration barrier and component controls pass 227/227 and 276/276. Their existing
motion rounding uses the unchanged comparator.

All 5,055 primary scene records pass, including 4,803 exact records and 252 motion
rounding records at the original tolerance. All six broad regression outputs are
byte-identical to accepted SDK8. Broad physics remains 22/29 scenarios with all 3D
scenarios passing and the same 133 failing values across seven 2D scenarios.
The integrated filter passes the existing 318 tests plus nine new cases, 327/327.
Final counts, full outputs, artifact hashes and logs are in `final/summary.json`.
The parent owns the final integrated API check and shared documentation.

The parent independently rebuilt the integrated managed sources and passed
327/327 tests and API-check with zero errors in 2266 hand-written types. Its six
fresh focused captures reproduce every exact active-body result and the unchanged
inactive boundary. It also checked all 5055 archived primary values against both
original Native recordings. Parent logs, hashes and precise verification scope
are in `ParentIntegration/summary.json`. The parent corrected `final/summary.json`
to count the 80 graphics cases rather than the two JSON wrapper fields; the raw
graphics capture, passing comparator output and all other observations are intact.

## Inactive boundary and limits

InactiveKinematicSleep.cs separately measures commands issued while an actor is
outside the scene, including initially inactive, deactivated and inactive cloned
bodies. Two independent Native processes agree on all 1,050 records. The frozen
baseline and candidate each pass 318/1,050, and their complete outputs are
byte-identical. The 732 retained differing records cover inactive pose/setter
behavior and reactivation state. They are preserved explicitly in the final
summary with both complete outputs. This diagnostic control introduces no mask,
exclusion or tolerance change and does not weaken any acceptance gate.

This certifies the measured active-body sleep behavior on 6000.0.71f1 Linux.
Inactive body parity, older releases and full engine parity remain open.

## Reproduction

Private artifacts and raw logs are under
`/mnt/storage-hdd/node-b-caches-20261023/sleep3d-1e422835-20261031/`.
Its `env.sh` sources the shared PATH setup, discards WT and sets every build and
output directory explicitly. Large outputs live on the HDD. The CLI and copied
native library hashes are checked against the unchanged immutable baseline8
manifest by `sleep_report.py`.

Use `parent_probes.py prepare/native/engine --probe sleep` and the corresponding
`sleep-inactive` probe with SCENE_PARENT_WORK set to a private directory. Native
runs require `--repeat 1` and `--repeat 2`; the runner takes the shared heavy-job
lock. The second boundary's baseline run must use frozen baseline8 managed output,
while its final run uses the private managed output. No real game is accessed.

Managed builds and tests also run through `~/.cache/compat/with-heavy-job`, with
private UnboundTarget and UnboundNativeDir, `--disable-build-servers -m:1
-p:UseSharedCompilation=false /nodeReuse:false`, and UseSharedCompilation=false,
MSBUILDDISABLENODEREUSE=1, DOTNET_CLI_USE_MSBUILD_SERVER=0. Use the requested full
integrated filter covering PhysicsTests, Physics2DTests, UIElements, YogaLayout,
CanvasOrderTests and CoreTests. Run the original matrix drivers, all focused
probes, existing regressions and graphics. Then run `sleep_report.py` to archive
only into SleepEvidence, leaving historical references and acceptances intact.
