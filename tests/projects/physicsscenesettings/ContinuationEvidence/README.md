# KD continuation result

No runtime candidate was accepted. The remaining 28 observations are unresolved.
Runtime source equals parent bc3dda31315fd6302f000024f0b7005725145fba. Main ABI5,
production PhysX shim7 and shader cache13 remain unchanged. Both immutable
baseline snapshots, original Sources/Reference, and all prior evidence are intact.

The complete frozen7 rerun matches every parent accepted value. Of 5,055 records,
5,027 pass: 4,775 exact and 252 rounding within the original motion tolerance.
All thirteen original profiles and four supplemental contact profiles pass.
The island workloads retain 8/8/6/6 failures for PGS/PGS-determinism/TGS/TGS-det.
No passing baseline observation now fails; every failed key and value remains
in frozen7/ and controls-summary.json. No tolerances or exclusions changed.

Final validation, using unchanged matching frozen artifacts and private projects:

- Kinematic setter: 15/15 exact.
- Extended triggers: 1,575/1,728; all 1,152 callback/order records exact, the same
  153 pre-existing sleeping-state differences. Raw output equals accepted bytes.
- Physics tests: 41/41; integrated physics/UIElements/Yoga/Canvas: 302/302.
- All six regression raw files equal parent accepted bytes: defaults 582/582,
  controller 23/23, forces 608/608, rayhits 3,203/3,203, broad physics 21/29 with
  the eight retained scenarios, joints 26/27 with one known settling difference.
- Comparator tests: 6/6. Production runtime files have no diff from the parent.

`final/summary.json` contains source and immutable artifact hashes. Final full
matrix data are the frozen rerun, because no runtime edits survived. Matching
ABI5 CLI, full managed output and shim7 came from parity-baseline-native7.
Builds, caches, private Unity projects and raw logs are under
`/mnt/storage-hdd/node-b-caches-20261023/kd-parity-20261031/`. Environment setup is
`env.sh` there. Every Unity, native compiler, dotnet and engine job used the shared
with-heavy-job lock. Managed builds used disabled build servers, one MSBuild node,
UseSharedCompilation=false, disabled node reuse and a private UnboundTarget.
No Rust/Bevy build was needed. No game project, generated API or shared output
was modified. No PR, merge, delegates or parent/backup branch edits.

## New Native controls

`controls/` retains two independent runs per cleanup/construction control.
Original, collider-first and disable-first all agree on 276 identical values.
Explicit Rigidbody-first destruction changes 45 keys/values and has 277 records.
Adding Rigidbody before Collider instead produces 278 agreeing Native values,
and the frozen engine passes all 278, with 266 exact and 12 motion rounding.
Thus the collider's temporary static owner is observable in subsequent pairs.
Sources and reproduction commands are in ../ContinuationProbes/README.md.

`native-trace/` contains two direct Unity debugger traces. Both retain every
original 276-record Native value. All 369 ID events from step48 onward agree
once addresses are removed, including all final pair orientations. Twenty-four
earlier ID events differ due to imported FindObjectsByType(None) order. Both
full 1,215-event traces retain those differences. Native creates activated and
cloned bodies already kinematic, whereas a fresh runtime component starts dynamic
and transitions after its shapes attach. Initial activation activity must still
be preserved by any future correction.

`native-flags/` independently repeats the Native trace with flag-change entry
and exit breakpoints. Both 276-record outputs preserve the original oracle.
All 477 pair-workload events agree after removing addresses. Eight observed
simulation-to-trigger changes, flags11 to14, keep the same element ID. At step57,
K rigid47 has element38 both before and after the flag setter. This is significant
because these trigger cases precede the failing non-trigger kd workload.

The local disassembly in native-flags/onFlagChange-disassembly.txt shows Unity
updating broadphase trigger metadata and marking interactions dirty for this
case. The vendored SDK's ScShapeSim::onFlagChange instead calls
reinsertBroadPhase when trigger status changes, consuming another element ID.
The private engine trace of born8 changes the corresponding element37 to57.
This is a measured SDK-path difference, not proof that modifying that algorithm
alone would make the engine conform. No SDK algorithm change was attempted.
The user excluded such changes; a safe general lifecycle solution was not found.

## Rejected runtime experiments

Every candidate patch and focused output is preserved in experiments/. Full
21-workload outputs, all nonexact values, and introduced/fixed keys are retained
for local7 and update8. Each uses the unchanged comparator. Other candidates
were rejected at the focused gate, so they did not receive a complete matrix.

- flush8 adds stock PxScene::flushSimulation(false) before SyncTransforms.
  Focused 224/278, 54 failures, compared with baseline 248/278 and 30 failures.
  It keeps the opposite face and introduces other motion failures. Removed.
- perbody7 applies pending type conversion after the body's last shape.
  Focused 248/278, 30 failures. Rigid ordering remains wrong. Removed.
- local7 realizes only a collider's static owner and its conversion to Rigidbody,
  avoiding the parent's rejected global AddComponent flush. Focused 276/276,
  but complete matrix 4,923/5,055: all original 28 fixed, 132 introduced failures.
  Unit run 301/302, with mass/impulse regression velocity6 instead of3. Broad
  3d.kinematic also regresses. All raw outputs and the failed TRX are retained.
- update8 adds in-place box geometry/material/flag updates and applies pending
  mass before force operations. Focused 262/277; full matrix 4,975/5,055,
  14 original failures fixed and 66 introduced, leaving 80 failures. Rejected.
- born8 additionally creates serialized kinematic bodies already kinematic,
  retaining the accepted real zero-displacement target/activity period.
  Focused 262/277, with the remaining wrong pair moving from fresh to activation.
  Rejected without expanding validation. This experiment uses the same private
  update8 native artifact and matching private managed output.

Private ABI8 exports and binaries were used only by experiments and were removed
from tracked runtime sources. Trace7 adds no exports; its output was checked to
preserve every baseline probe value. The initial debugger attempt stopped on
Mono SIG37 before any oracle was written. Corrected scripts pass real-time
signals; all four successful Native debugger runs exited normally. The initial
failure log remains in the private work directory and is named in the summary.

Full engine parity remains open. Public ContactEvent stays a stub; engine raw
contacts still come from fixture-only reflection of real PhysX contacts. The 153
sleep observations and integrated public API checking remain outside this task.
