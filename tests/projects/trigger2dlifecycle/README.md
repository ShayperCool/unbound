# 2D contact lifetime continuation

This stage starts at 7c0bde630f630c55181a31ac7563954323e774da. Historical
trigger2dtiming inputs and evidence stay unchanged. New controls cover both
callback flags, solid and sensor pairs, awake and sleeping dynamic/kinematic
bodies, static/dynamic/kinematic partners, collider disable, simulated disable,
deactivation and destruction of colliders, bodies and GameObjects. Additional
controls exercise changes from enter callbacks and moving static colliders.

Two independent Unity 6000.0.71f1 Linux CPU Editor processes agree byte for byte
on 25,356 observations in 324 cases before runtime implementation. Every callback
has its real phase, step and receiver. Teardown remains in the records.
Evidence/native-manifest.json identifies sources, captures and private paths.

Native collider disable, deactivation and collider/GameObject destruction send
immediate exits when callbacksOnDisable is true. simulated=false exits on the
next Simulate, including when callbacksOnDisable is false. Removing a Rigidbody2D
silently removes its old contacts and converts remaining colliders to static
fixtures. DestroyImmediate of physics components or objects during a physics
callback is rejected by Native; collider disable and deactivation are permitted,
with exit callbacks following the current pair's enter delivery.

These measurements cover this release only. Older releases remain unmeasured.
This is not a full-engine parity claim.

## Accepted result

The final managed change resolves all 376 retained timing/order observations and
preserves every accepted parent key with the original comparisons and tolerances.
Timing passes 18,491/18,491, with 18,445 exact and the original 46 passing numeric
rounding observations. Mixed order passes 5,720/5,720, with 5,716 exact and the
original four passing numeric rounding observations. All callback, order, step,
sleep and touching records are exact. Teardown remains in every comparison.

The new lifetime controls pass 25,356/25,356 exactly. The additional 96 receiver
controls pass 1,152/1,152 exactly against both independent Native recordings.
Collider creation determines receiver order, including when a Rigidbody2D is
added to an older collider. The counter records creation order directly; it does
not depend on allocator layout or add padding. Body/fixture registration happens
before returning. Moving static bodies wakes existing neighbours at transform
sync and newly discovered neighbours during contact creation. Kinematic sensor
pairs retain their measured sleep state. Simulated=false uses Box2D's enabled
state, removing real contacts/proxies while preserving the body and sleep state.
Removal callbacks drain only after the Box2D mutation completes, after both sides
of an in-progress callback have been delivered. The destruction hook applies
only to 2D physics components and hierarchies during 2D callbacks. The existing
3D realization and clone construction code stays intact.

The historical broad physics comparator now passes 11,505/11,626 values, with
7,895 exact and 3,610 passing numeric rounding observations. Ten kinematic exit
values are resolved and 121 failures remain across the same six 2D scenarios.
Every retained failure is in Evidence/final/summary.json. There are no introduced
failures. Broad trigger enter44/exit56 and trigger-before-solid order remain
correct. This result does not claim parity for the retained broad failures.

Fresh final regression gates pass matrix5055, inactive1050, sequences4668,
active20700, clone-placement160, ordinaryTransform12, triggers1728, flags1152,
trigger-pairs1440, ignore-lifecycle1848 and immediate15. Order276, component276
and barrier227 pass the unchanged comparator. All 21 matrix raw files are
byte-identical to SDK8 acceptance, with 4803 exact and 252 original rounding.
Joint2D, effectors, forces, defaults, joints, controller and rayhits pass their
unchanged comparators. Joint2D's 19 solver-rounding observations and effectors'
28 measured alternatives remain reported. Comparator unit tests pass 11 and 8.
The full requested managed filter passes 366/366, including 13 added Native-backed
cases. Graphics80, Canvas8671 and messages316 pass; lifecycle is byte-identical
to Native. The parent owns the final API check and sibling integration.

## Preserved candidates and artifacts

Evidence/candidate1 retains every capture and its source patch. It resolves 170
historical timing observations and all 160 mixed-order observations but retains
46 timing observations. Its new lifetime comparison introduces 288 keys and
retains 5638, so it is rejected. Candidate2 resolves all 376 historical differences
but introduces 112 new lifetime keys and 32 receiver keys. Candidate3 introduces
none but retains 96 new receiver identity observations after late body attachment.
Its complete capture and patch remain available. The final candidate resolves
all these new controls without exclusions or expected-failure masks.

The first attempt to archive complete private projects followed recursive
ProjectView symlinks and raised Errno 40. All raw outputs were subsequently
preserved by explicit filename. The final broad replay initially refused to
overwrite the earlier private capture; that rejection log is retained, the old
capture was archived, and the fresh replay completed. Neither failure changed
historical evidence. ccc initialization succeeded, but both bounded semantic
refresh attempts timed out with exit 124. Exact rg searches and direct source
reads supplied the code review; no shared daemon was stopped.

Private caches, original projects, logs, candidate2/candidate3 managed copies,
final managed output and test binaries are under:

`/mnt/storage-hdd/node-b-caches-20261023/trigger2dlifecycle-bd5ab077/`

Evidence/env.sh records every private output/cache path. Evidence/validation-summary.json
hashes final assemblies, the frozen CLI, test-local and assembly-local shim8,
source files and all archived evidence, and verifies every native6/7/8, sleep3d,
trigger2d and inactive3d frozen manifest. The final core DLL SHA256 is
`21365a3259060727425cbc7fb56e91d5ee2047fe1555522f7e0acacf46f1b101`.
Both actual copied/test shims have SHA256
`4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208`.
MainABI5, shim8 and shadercache13 are unchanged. No Rust/native rebuild was used.

## Reproduction

Use a fresh private HDD work directory and copies of the full frozen
parity-baseline-inactive3d-native8 managed output, never hardlinks. Keep a private
managed-baseline copy and writable managed-final copy with the matching shim.
Adapt Evidence/env.sh to that work directory before sourcing it. For new probes:

```bash
python3 tests/projects/trigger2dlifecycle/run.py prepare
python3 tests/projects/trigger2dlifecycle/run.py native --label native1
python3 tests/projects/trigger2dlifecycle/run.py native --label native2
python3 tests/projects/trigger2dlifecycle/run.py engine --label baseline
```

Use --probe receivers for the second Native pair. Obtain and compare both Native
recordings before changing runtime behavior. Build the candidate with:

```bash
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false
```

Prepare and run engine probes lifecycle, receivers, timing and order with --label
final, then run validate.py for tests, focused, broad, matrix and visual. Every
engine/Editor/build/test invocation takes the global exclusive heavy-job lock.
The visual runner uses Canvas --frames24 --size640x480 and messages --headless445
--size960x540 with the unchanged comparator's arguments. It never requests
software graphics. report.py final --accept preserves complete before/after
comparisons; verify.py validates and archives the remaining gates. Runners refuse
to replace existing named JSON captures. Use new labels or archive prior private
outputs first. Historical Sources, Reference and accepted/rejected folders are
never writer destinations for these scripts.

No authored files in ~/unity-projects/game were read, run or modified. Further
multi-pair reentrancy, hierarchy/scale combinations and older releases remain
outside these measurements. This is not a full-engine parity claim.

## Parent integration

The parent merged this work with the accepted serialized Rigidbody loading fix
on a separate integration branch and rebuilt the managed engine. The combined
filter passes 384/384 tests and API-check reports zero errors in 2266 hand-written
types. The API command uses the complete physicsscenesettings project; this
fixture's directory contains source probes rather than a complete Unity project.

The parent independently replays the new lifecycle and receiver controls, both
old timing/order suites, both serialized document orders and all existing 3D,
matrix and visual gates. Fresh captures, comparisons, artifact/source hashes and
exact verification scope are in `Evidence/ParentIntegration/summary.json`.
The parent's verifier writes only that new archive and leaves delegate evidence
and historical references unchanged.

The accepted combined parent runtime is frozen at
`~/.cache/compat/parity-baseline-lifecycle2d-native8/`, including the ABI5 CLI,
complete managed output and matching shim8. Every file hash and the source
commit are recorded in `Evidence/ParentIntegration/baseline-manifest.json`.
Use copies for subsequent work; keep this and all earlier baselines unchanged.
