# 2D motion continuation

The main objective is incomplete. Bounce9, stack27 and kinematic9 remain failing.
No motion candidate met the no-new-failing-values requirement, so none was kept
in production. Every candidate, raw capture and complete union-key comparison
is retained. This branch contains one measured runtime fix for the root's
BoxCollider2D skin dependency and 46 new Native-backed transition tests.

The base is 88a53c477dc43e646c21522f6c402c0cec98c6c1. The prepared branch is
`t3/physics2d-motion-parity`. No PR, merge, delegate, parent checkout edit or
backup edit was made. Nothing in ~/unity-projects/game was read, run or modified.

## Native measurements

All new Editor recordings use Unity 6000.0.71f1 Linux in CPU batch/nographics
mode, independently imported private projects and separate processes. Both
processes agree byte for byte in each pair, before the corresponding behavior
experiment. Older releases remain unmeasured.

| Pair | Values per recording | SHA256 of each uncompressed recording |
| --- | ---: | --- |
| Motion v1, three isolated original scenarios plus controls | 3353875 | 61873a0b67b9a79501dcba180f62dfc00ba920f5db81b5c18e7d516b9b3befca |
| Motion v2, full original broad sequence plus controls | 3364863 | d73de9014edb3fc1c099a69a220b830e92aeb8a294f5d713f22674dc81b12c6c |
| MoveRotation controls | 36000 | e8d7ac9f7cdadb5fc9471b411cc201a4a0fdebc0244d2d3461db00ecde850a14 |
| Root-supplied skin lifecycle | 644 | ac3955120dd650216f983a642d825d28eb16e40696053b175e6d249adb12d8ba |

The v2 pair reproduces every original broad reference scenario exactly. Isolated
stack execution has a different Native result, so the v1 stack was retained and
v2 added the full preceding execution context. Original physics authored files,
reference, scripts and comparators remain unchanged. Sources/original keeps
byte-identical copies of its scripts and 2D settings. Input manifests identify
every private project file.

The controls retain pre/post poses, velocities, sleeping state, callback phase,
step, receiver, both contact points, separation, normal, relative velocity,
solver impulses, material values, gravity, dt and iteration counts. They vary
material combinations, rotation, creation order, sleeping, dt and velocity/position
iterations. Both sides of callbacks and teardown remain in the records.

The first contact records prove that point, separation and point velocity belong
to contact evaluation, while callback body state and normal orientation can
already reflect later solver motion. OWNER-PROPOSAL.md gives the exact step49
values and the remaining investigation and query integration points.

## Rejected candidates

Evidence/candidate-summary.json summarizes the complete compressed reports.
Every report retains reference and candidate keys, including missing and extra
keys, all failures and every passing numeric rounding value. No observation,
scenario, timestamp or failure mask was removed. Tolerance stays 1e-3.

Candidate1 caches the complete manifold and changes accepted rotating normals.
Candidate2 retains a live normal and evaluated point/separation/point velocity.
It resolves 17 original enter-geometry values but introduces 15066 expanded
observations. Candidate3 adds real contact-creation ordering for constraints.
It passes original bounce and stack and resolves 44 of the assigned 45 values,
with no original broad regressions, but introduces 51488 expanded observations.
Candidate4 also sorts real proxy IDs and introduces 495804 expanded observations;
it retains the 27 original stack failures again. All are rejected.

The rotation experiment improves exact matches from 12417 to 27384 in the
36000-value direct control suite, with no comparator failures before or after.
It resolves no broad failing key and is unshipped. Its full motion replay has
no newly failing keys and retains the same 121 original failures. Proposed
contact tests are preserved as source text rather than installed failing tests.
The first skin draft missed six transform transitions; the failed test result,
source patch and binaries are preserved. The corrected patch passes all 46.

## Retained production change and final gates

The production fix captures BoxCollider2D skin on the root's measured geometry
and registration transitions, including pending transform edits before Rebuild.
The unchanged-value, sync, material and density controls retain the earlier skin.
Enable/activation and clones use real registration, and adding a body refreshes
skin through attachment. The solver, contact construction, callback lifecycle,
queries and global tuning remain as in the parent. Circles, capsules and
polygon/edge/chain/composite skin transitions are not generalized here.

The final broad comparator remains 11505/11626 values in 23/29 scenarios, with
7895 exact and 3610 original passing rounding values. Resolved 0, introduced 0,
retained 121. The assigned 45 are still bounce9, stack27 and kinematic9; the other
76 are unchanged query failures. Evidence/validation/summary.json contains every
remaining broad failure. Evidence/final/comparison.json.gz contains the complete
new motion comparison, including its 1424057 retained baseline failures and zero
newly failing keys over the 3399409-key union. These large controls are not claimed
to pass Native parity.

Final checks pass all original 384 integrated cases and 46 new skin cases,
430/430 with no skips. Contact lifecycle25356 and receiver1152 are exact;
timing18491 passes with 18445 exact and its original 46 rounding values;
order5720 passes with 5716 exact and its original four rounding values. All
callback/order/step/sleep/touching records are exact. Trigger enter44/exit56 and
trigger-before-solid remain unchanged in the raw broad/lifecycle captures.

Loading passes 4656/4656 exactly in each YAML order. Focused 3D checks retain
inactive1050, sequences4668, active20700, clones160+Transform12, triggers1728,
flags1152, trigger-pairs1440, ignore-lifecycle1848 and immediate15 exactly.
Order276, component276 and barrier227 pass their original comparator.
The matrix passes 5055/5055, 4803 exact and 252 original rounding records;
all 21 raw files are byte-identical to SDK8 acceptance. Defaults582,
controller23, forces608 and rayhits3203 pass. Joint2D retains 1382 direct
matches plus 19 existing solver-rounding records; effectors retain 2531 direct
matches plus 28 measured alternatives. The existing joints comparator passes
with its previously reported limit-force difference. Nothing is newly masked.
Comparator unit tests pass scene6, Joint2D11 and effectors8. Graphics80,
Canvas8671 and messages316 pass, and lifecycle is Native-byte-identical.

The old standalone query helper retains the same 171 failures in root's
644-value skin diagnostic. Its helper math is outside ownership and the rejected
query branch was not merged. All 46 actual fixture transitions are verified by
the new tests. PhysicsShapes' global-skin subtraction and other query-owned
integration points are called out in OWNER-PROPOSAL.md. Root still owns the
combined query replay and final integrated API verification.

## Artifacts and reproduction

All large projects, native logs, baseline/candidate managed copies, build output,
scene caches and test binaries are private to:

`/mnt/storage-hdd/node-b-caches-20261023/physics2dmotion-t3-319e74d7/`

Evidence/env.sh replaces obsolete variables and provides every private path.
Copies/reflinks are used, never hardlinked build outputs. Each Unity, engine,
dotnet and GPU invocation uses the shared exclusive with-heavy-job flock and
this agent's memory.max is 32212254720. Dotnet uses disabled build servers,
-m:1, UseSharedCompilation=false and /nodeReuse:false, plus all three required
environment settings. No Rust/native/Bevy build was used.

Evidence/validation/summary.json hashes every final managed output, frozen CLI,
actual assembly-local and test-local native library, source, regression artifact
and all eight frozen snapshot manifests. Native6/7/8, sleep3d, trigger2d,
inactive3d, loading3d and lifecycle2d snapshots verify unchanged. ABI5, shim8 and
shadercache13 are unchanged. Final core DLL SHA256 is
b48ab02e8895168188228ed7e9d1ea335d96b00ab31f78b4a4c2efefa212896e.
Both actual native libraries have SHA256
4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208.
The frozen CLI has SHA256
0a55f7d5fb6314deda371ee9c9334ec129652047925272d803d5cb86bb704b56.
Candidate artifact manifests and Evidence/extra-artifacts.json identify all
rejected binary snapshots. Raw JSON is losslessly gzip-compressed in git.

commands.sh records the final reproduction sequence and native/control stages.
Use fresh private paths and labels. run.py refuses to overwrite captures/logs;
report.py and verify.py refuse to replace an existing report. Final regression
writers target private directories, never historical accepted/rejected archives.
The one premature rotation report attempt is retained with its FileNotFoundError;
the complete report uses a separate archive name. A redundant copy of the
already copied, read-only private shim initially returned Permission denied;
only the private writable output was chmod'd. The frozen source was untouched.
Git transport returned errors after the requested ref had advanced;
subsequent remote-ref reads verified the intended commit before proceeding.
Production source passes git diff --check. The full evidence diff reports
trailing spaces in raw graphics output and patch context; those bytes were
retained rather than rewriting historical observations.

ccc initialization succeeded. The first 50-second refresh and final 25-second
index request timed out with exit124; no shared daemon was stopped. Source
review used exact rg searches and direct reads. No full-engine parity claim.
