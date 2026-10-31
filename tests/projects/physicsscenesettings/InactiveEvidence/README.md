# Inactive 3D Rigidbody continuation

The starting source is d5d42fb25dca89d9d8541f553cb3697417d030e9. The existing
1,050 inactive records and 20,700 active records each have two byte-identical
independent Unity 6000.0.71f1 Linux captures. Their hashes were verified before
runtime changes. The inactive baseline passes 318 records and retains 732.

InactiveCommandSequences.cs adds 4,668 observations per independent Native
process. Both captures are byte-identical. Cases use nonidentity initial rotation
and translated poses, Transform changes before and after adding Rigidbody,
awake and sleeping deactivation, initially inactive and deactivated clones,
ordered Move and pose setter sequences, inactive sync and PublishTransform,
Sleep/WakeUp, velocity writes, activation and twenty simulation steps.

Native retains the pose from component attachment or deactivation while inactive.
Transform edits and inactive sync leave that pose unchanged. Rigidbody setters
replace their physics pose component without publishing Transform. Move commands,
PublishTransform and Sleep/WakeUp leave the inactive pose and sleep unchanged.
Activation takes the current Transform pose. Dynamic velocity survives
deactivation and inactive velocity writes; kinematic velocity writes are ignored.
Clones begin with zero velocities. These statements describe the measured cases;
older releases remain unmeasured and this is not full engine parity.

native-manifest.json hashes the exact sources, settings and independent captures.
Raw projects and execution logs are in
/mnt/storage-hdd/node-b-caches-20261023/inactive3d-t3-3a3af6f7-20261031/.

## Accepted result

The final managed-only change matches all 1,050 original inactive records exactly
against both Native recordings. It resolves all 732 retained records and preserves
all 318 accepted records, with zero introduced or retained inactive failures.
The new sequence control matches 4,668/4,668 exactly, resolving 3,194 baseline
records and preserving 1,474 already accepted records.

The active control remains 20,700/20,700 exact. Long triggers pass 1,728/1,728,
SDK flags 1,152/1,152, trigger pairs 1,440/1,440, ignore lifecycle 1,848/1,848 and
immediate setters 15/15 exactly. Order and component remain 276/276 and barrier
227/227 with the unchanged motion comparator. All 5,055 matrix records pass,
including 4,803 exact and the original 252 passing motion-rounding records.
The full integrated filter passes 337/337, consisting of 327 existing tests and
ten added cases. Graphics passes 80/80. Main ABI5, shim8 and shader cache13 remain
unchanged. No Runtime/Compat, native, SDK, Rust or 2D sources were changed.

Five broad outputs are byte-identical to the accepted parent. Forces passes all
608 records with changed passing rounding. Broad physics remains 22/29 scenarios,
with all 3D scenarios passing and exactly the same 133 failing values and keys
across seven pre-existing 2D scenarios. The report retains every such value.

The first build failed because private copies inherited the frozen snapshot's
read-only permissions. Only private copies were made writable. An initial runtime
candidate then lost imported scene bodies because its attachment hook assumed a
Transform already existed. The entire rejected regression run is preserved in
final/rejected-import-order. The fix handles component document allocation order
inside Rigidbody and has a dedicated deserialization regression test. A Vulkan
RequestDeviceError with Device(Lost) stopped one graphics run. Its complete log is
preserved; the fresh final run passes all 80 cases. No failed candidate is used by
the final gate.

final/summary.json contains precise counts, complete baseline failures, explicit
pre-existing broad differences and hashes of every archived file.
final/artifact-manifest.json hashes all private managed outputs and the CLI,
native and test artifacts. All four immutable baseline manifests were verified.
Assembly-local and test-local libunbound_physx.so both match the frozen shim8
SHA256 4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208.

## Reproduction

From this checkout, source the private environment:

```bash
source /mnt/storage-hdd/node-b-caches-20261023/inactive3d-t3-3a3af6f7-20261031/env.sh
```

It discards obsolete WT and explicitly sets UNBOUND_MANAGED_DIR, UnboundTarget,
UnboundNativeDir, SCENE_WORK, SCENE_PARENT_WORK, SCENE_SETTINGS_CLI and
CARGO_TARGET_DIR. Build with:

```bash
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false
```

Use the same flags for dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj,
with private UnboundTarget="$INACTIVE_WORK/test-build/", the full requested filter
covering PhysicsTests, Physics2DTests, UIElements, YogaLayout, CanvasOrderTests and
CoreTests, and --logger 'trx;LogFileName=integrated.trx' --results-directory
"$INACTIVE_WORK/tests". Keep all three MSBuild-server environment settings from
env.sh. final/validation/validate.sh contains the exact focused, matrix, broad and
graphics commands. Each runner takes the shared exclusive heavy-job lock.

Run the new strict gate with:

```bash
python3 tests/projects/physicsscenesettings/InactiveEvidence/report.py
```

For new Native captures, prepare the inactive-sequences fixture with
parent_probes.py, then use native --probe inactive-sequences --repeat 1 and
--repeat 2. Preserve both recordings before any further runtime changes. For a
baseline engine replay, explicitly select the frozen sleep3d snapshot's managed
directory and native path, not the candidate outputs. Copies must not be hardlinks.

No authored files in ~/unity-projects/game were read, run or modified. Historical
sources, references, accepted evidence and sleep_report.py remain unchanged.
Serialized inactive objects edited before their first physics read, parented clone
pose initialization and older releases are outside the added Native measurements.
The parent owns the final integrated API check and shared documentation. These
results certify the measured cases, not full engine parity.

## Parent integration

The parent integrated this change with the separate 2D timing fix, then added
Native-first clone-placement controls before accepting the combined runtime.
They exposed ten newly failing keys in the inactive-only candidate. The parent
fixed initial body-pose capture after placement and the existing no-parent clone
placement behavior. A further Canvas regression caught overwritten copied
RectTransform anchors; the final correction restores all 8671 observations.
See `../ClonePlacementEvidence/README.md` for the complete rejected and accepted
controls. Historical evidence above remains unchanged.

Final fresh parent verification passes 1050 inactive, 4668 sequence and 172 added
clone records exactly; active20700 and every long/SDK control also remain exact.
All 21 matrix files pass 5055/5055 and remain byte-identical to SDK8 acceptance.
Tests pass 353/353 and API-check reports zero errors in 2266 types. Graphics80,
Canvas8671 and messages316 pass; lifecycle is Native-byte-identical. Broad physics
retains the current 131 2D differences after the separate timing fix, with the
same 376 timing/order differences. Existing regression gates pass unchanged.

Parent captures, logs, artifact/source hashes and full retained failures are in
`ParentIntegration/summary.json`. Set INACTIVE_PARENT_WORK to a fresh parent
capture directory and run `ParentIntegration/verify.py` to validate and archive
those results. The exact engine driver is archived under
`ParentIntegration/accepted/validation/validate-final.py`; its environment uses
the same explicit output paths and heavy-job wrapper as the delegate.

The accepted combined runtime also has an immutable local snapshot at
`~/.cache/compat/parity-baseline-inactive3d-native8/`, with the ABI5 CLI, complete
managed output, shim8 and all file hashes. Its manifest is preserved in
`ParentIntegration/baseline-manifest.json`. Use copies of this snapshot for the
next workstreams; leave it and every earlier baseline unchanged.
