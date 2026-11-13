# Unbound patches to PhysX

This is the complete source-patch inventory for the vendored PhysX 4.1.2 tree.
[UNBOUND.md](UNBOUND.md) records its upstream revision,
`a2c0428acab643e60618c681b501e86f7fd558cc`, and the directories retained at import.
The local import is `9a4c8ad7`. Relative to that import, twenty-eight SDK source/header
files have changes and one header is added, listed below. The import's directory exclusions and the
Unbound build/shim code outside this directory are separate from these patches.

The behavioral references cited here were recorded with the Unity 6000.0.71f1
Linux Editor. They establish the named observations, not full engine parity or
the behavior of other Unity releases. A public callback trace does not by itself
prove which internal algorithm Unity uses. Internal-mechanism observations are
identified separately where relevant.

## Inventory

| Patch | File and function | Introduced in |
| --- | --- | --- |
| Free-spinning twist motor | [PxD6Joint.h](include/extensions/PxD6Joint.h), drive flags; [ExtConstraintHelper.h](source/physxextensions/src/ExtConstraintHelper.h), angular drive helper; [ExtD6Joint.cpp](source/physxextensions/src/ExtD6Joint.cpp), twist drive preparation | Q06 |
| Refresh Discrete bodies moved by CCD | [ScScene.cpp](source/simulationcontroller/src/ScScene.cpp), `Sc::Scene::postCCDPass` | Q145 |
| Retain breaking force and torque | [ScConstraintCore.h](source/simulationcontroller/include/ScConstraintCore.h), `Sc::ConstraintCore::setBreakingForce`; [ScConstraintCore.cpp](source/simulationcontroller/src/ScConstraintCore.cpp), `Sc::ConstraintCore::getForce`; [ScConstraintSim.cpp](source/simulationcontroller/src/ScConstraintSim.cpp), `Sc::ConstraintSim::checkMaxForceExceeded` | `1b53e844` |
| Cancel the controller retry's artificial step | [CctCharacterController.cpp](source/physxcharacterkinematic/src/CctCharacterController.cpp), `physx::Cct::SweepTest::moveCharacter` | `0207b818` |
| Keep NEON lane constants valid for Clang | [PsUnixNeonInlineAoS.h](source/foundation/include/unix/neon/PsUnixNeonInlineAoS.h), `BSplatElement`, `V4U32SplatElement`, `V4SplatElement` | `ee55f99c` |
| Allow trigger/trigger filtering | [ScNPhaseCore.cpp](source/simulationcontroller/src/ScNPhaseCore.cpp), general `filterRbCollisionPair` overload | `9491a8bc` |
| Run pair callbacks during interaction creation | [ScNPhaseCore.cpp](source/simulationcontroller/src/ScNPhaseCore.cpp), `Sc::NPhaseCore::createRbElementInteraction` overload taking shapes | `9491a8bc` |
| Change trigger role without broadphase reinsertion | [ScShapeSim.cpp](source/simulationcontroller/src/ScShapeSim.cpp), `physx::Sc::ShapeSim::onFlagChange` | `9491a8bc` |
| Refit objects moved late in a query-tree rebuild | [SqAABBPruner.cpp](source/scenequery/src/SqAABBPruner.cpp), `Sq::AABBPruner::updateObjectsAfterManualBoundsUpdates`, `Sq::AABBPruner::updateObjectsAndInflateBounds` | Q276 |
| Run the BV4 midphase on NEON | [GuBV4_Simd.h](source/geomutils/src/mesh/GuBV4_Simd.h) (added); the BV4 guards of [GuBV4.h](source/geomutils/src/mesh/GuBV4.h), [GuBV4_Common.h](source/geomutils/src/mesh/GuBV4_Common.h), [GuBV4_Internal.h](source/geomutils/src/mesh/GuBV4_Internal.h), [GuBV4_AABBAABBSweepTest.h](source/geomutils/src/mesh/GuBV4_AABBAABBSweepTest.h), [GuBV4_BoxBoxOverlapTest.h](source/geomutils/src/mesh/GuBV4_BoxBoxOverlapTest.h), [GuMidphaseInterface.h](source/geomutils/src/mesh/GuMidphaseInterface.h), [GuMidphaseBV4.cpp](source/geomutils/src/mesh/GuMidphaseBV4.cpp) and the `GuBV4_*` query sources (`AABBSweep`, `BoxOverlap`, `CapsuleSweep`, `CapsuleSweepAA`, `OBBSweep`, `Raycast`, `SphereOverlap`, `SphereSweep`) | Q249 |
| CCD sweep frame in the SSE2 order | [PsUnixNeonInlineAoS.h](source/foundation/include/unix/neon/PsUnixNeonInlineAoS.h), `QuatGetMat33VSse2Order` (added); [GuCCDSweepPrimitives.cpp](source/geomutils/src/ccd/GuCCDSweepPrimitives.cpp), `CCDSweep` | Q431 |

The comparison against `4724b839e749590151e862695e1e31ad94447151` contains only
the trigger/trigger filtering, interaction-creation callback and trigger-role
patches, in `ScNPhaseCore.cpp` and `ScShapeSim.cpp`. The older
patches remain part of the current SDK and are included here.

## Refresh Discrete bodies moved by CCD

`Sc::Scene::postCCDPass` refreshes the cached shape transforms and bounds of
dynamic bodies moved by CCD even when their own sweep flag is disabled. It uses
the existing list of updated bodies, skipping sweep-enabled bodies and
articulation links whose caches the existing passes already update. It adds no
storage, allocations or pairwise scans.

Measured requirement: Unity 6000.0.71f1 keeps two touching bodies at their actual
post-impact separation on subsequent steps after a ContinuousDynamic body hits
a Discrete dynamic target. The confirming reference is
`tests/projects/physicsccdhinge/Reference/unity-6000.0.71f1/geometry-controls.tsv`,
`control/dynamic/{0,1}/{1,1000}/-1/2`. It crosses sphere/convex targets and equal
or unequal masses. Their poses, contact separations, velocities and callbacks
are recorded on every step.

The implementation cause is a stale shape transform: the original pass only
refreshes the sweep-enabled bodies and articulations, although CCD can move
both dynamic participants. The next narrow phase then sees the Discrete
target's earlier pose. Updating its cache explains the measured correction;
this is an implementation inference, not a claim about Unity's implementation.

## Free-spinning twist motor

The twist drive accepts an optional `eFREE_SPIN` flag. Its constraint row permits
impulse only in the direction of a nonzero velocity target. A zero target keeps
both impulse directions. The angular drive helper returns its existing row so
the twist drive can adjust those bounds without adding storage or allocations.
Other drives and joints keep their existing bounds. The flag uses the existing
drive flags field; no constraint data layout changes.

Measured requirement: Unity 6000.0.71f1 lets a free-spinning hinge coast faster
than a nonzero motor target in the same direction, accelerates it toward a target
in the opposite direction, and brakes it toward a zero target. The confirming
probe is `tests/projects/physicsccdhinge`, `hinge/motor` and `hinge/motor-toggle`,
with positive and negative targets, initial velocities and a connected-body
control. The one-sided impulse bounds are our implementation of those
observations, not a claim about Unity's implementation.

## Retain the force and torque that broke a constraint

Upstream `ConstraintCore::getForce` returns zero when `mSim` is null. Breaking a
constraint removes its simulation object before Unbound reads the break result.
That discarded the force needed for Unity's `OnJointBreak` argument.

The patch makes three exact changes:

1. `ConstraintCore::setBreakingForce` is a new inline setter for the existing
   `mAppliedForce` and `mAppliedTorque` members. It adds no storage.
2. In `ConstraintSim::checkMaxForceExceeded`, when `solverOutput.broken` is true,
   copy `linearImpulse * mScene.getOneOverDt()` and
   `angularImpulse * mScene.getOneOverDt()` into those members before marking
   the constraint broken and calling `mCore.breakApart()`.
3. In `ConstraintCore::getForce`, return the saved members instead of zero when
   `mSim` is null. The live-simulation path still calls `mSim->getForce`.
   The existing constructor initializes both saved members to zero.

[JointProbe.cs](../../tests/projects/joints/Assets/Scripts/JointProbe.cs),
`JointProbe.Start`, `Steps` and `BreakRecorder.OnJointBreak`, exercises the `fixed break` case.
Its fixed joint has `breakForce = 40`, and the body initially weighs 5 under
gravity 9.81. The Native
[joints.jsonl](../../tests/projects/joints/Reference/joints.jsonl) contains
`case = break`, `name = fixed break`, `step = 0`, `force = 49.05`, `joints = 1`.
Returning zero after teardown cannot reproduce this observation. The shim's
`ua3_world_broken_joints` reads the saved force through `PxConstraint::getForce`
and passes its magnitude to managed code.

The fixture's [README](../../tests/projects/joints/README.md) and
[compare.py](../../tests/projects/joints/compare.py) describe replay and its
1% relative or 0.01 absolute numeric tolerance. The committed case checks the
linear-force magnitude. It does not separately measure a torque-only break or
all calls to `getForce` after other causes of simulation removal.

## Cancel the character controller retry's artificial step

`SweepTest::moveCharacter` starts with the configured `mStepOffset`. Upstream
skips its up pass during `STF_WALK_EXPERIMENT`, the retry after a non-walkable
hit, but can still subtract that step offset in the down pass.

The patch sets the local `stepOffset` to `0.0f` when
`mFlags & STF_WALK_EXPERIMENT`, immediately after copying `mUserParams.mStepOffset`.
The configured parameter stays unchanged. This also makes the retry's later
step-offset uses see zero, including the down displacement and constrained
climbing check.

The observable requirement is Unity's controller position and collision results
when movement reaches a non-walkable slope.
[ControllerProbe.cs](../../tests/projects/controller/Assets/Scripts/ControllerProbe.cs),
`ControllerProbe.Start` and `Moves`, records the `ramp 60` scenario along with
`ramp 30`, `step low` and `step high` controls in
[controller.jsonl](../../tests/projects/controller/Reference/controller.jsonl).
It records each move's position, velocity, grounded state, collision flags and
hit callbacks. These are the behavioral evidence for the retry adjustment;
they do not expose Unity's internal `STF_WALK_EXPERIMENT` flag.

See the [controller README](../../tests/projects/controller/README.md) and
[comparator](../../tests/projects/controller/compare.py) for replay. The
comparator uses 0.2% relative or 0.002 absolute tolerance and compares a face hit
point along the recorded normal, since triangle ties can choose a different
point on the same face.

## Keep ARM NEON lane constants valid for Clang

In `BSplatElement<index>`, `V4U32SplatElement<index>` and the non-`PX_UWP` branch
of `V4SplatElement<index>`, replace the low-half intrinsic's lane argument
`index` with `index & 1`. The unsigned functions use `vdupq_lane_u32`; the float
function uses `vdupq_lane_f32`. All high-half branches remain unchanged.

For template indices 2 and 3, Clang validates the low-half intrinsic's constant
even though `if(index < 2)` prevents that branch from executing. Masking the
constant keeps it in the two-lane intrinsic's valid range. For executed
low-half branches, indices 0 and 1 are unchanged. This preserves the intended
four-lane splat while allowing the ARM build to compile.

This is a compiler portability patch, with no distinct Unity behavioral
requirement or Native oracle. The reproducible build path is
[build_physx](../../crates/unbound-host/build.rs) through
`cargo build -p unbound-host` on macOS arm64 with the repository toolchain and
Apple Clang. There is no committed isolated ARM compilation test or per-lane
runtime probe. Linux x86_64 probe results do not exercise this header.

## Allow trigger/trigger pairs to reach the filter

In the general `filterRbCollisionPair` overload, upstream returns `eKILL`
immediately when both shapes have `PxShapeFlag::eTRIGGER_SHAPE`. The patch
removes that return and changes the following branch to `if(!isTriggerPair)`.
Any pair with at least one trigger can therefore reach
`filterRbCollisionPairSecondStage`, including the application's filter shader
and callback. Filtering can still suppress the pair. The non-trigger
kinematic, joint and articulation checks keep their previous scope.

Unity reports overlapping trigger/trigger pairs and retains a trigger overlap
when the two colliders exchange trigger roles. Killing the pair prevents those
events. In
[TriggerPairLifecycle.cs](../../tests/projects/physicsscenesettings/ParentProbes/TriggerPairLifecycle.cs),
`SceneSettingsProbe.RunCases`, both boxes become triggers, move apart, move back
together and then become solid again. The canonical
[focused/trigger-pairs.jsonl](../../tests/projects/physicsscenesettings/Reference/unity-6000.0.71f1/focused/trigger-pairs.jsonl)
records, for dynamic/dynamic bodies:

- `dd/tt-new/0/events` reports trigger enter and stay to both objects.
- `dd/tt-out/0/events` reports trigger exit to both objects.
- `dd/tt-in/0/events` reports a new trigger enter and stay.
- `dd/from-tt/0/events` reports trigger exit and collision enter, with four
  contacts on each object's collision callback.

The same workload covers sleeping dynamic bodies, kinematic/dynamic,
kinematic/kinematic, kinematic/static and initially ignored dynamic pairs.
These cases establish the observed events for that workload; they do not
measure static/static triggers or every possible shape type.

## Run filter callbacks when creating an interaction

In the shape-taking `NPhaseCore::createRbElementInteraction` overload, change
the final `runCallbacks` argument of `filterRbCollisionPair` from `false` to
`true`. This affects every pair using that overload, including trigger
overlaps. The separate batched non-trigger filtering overload is unchanged.

With `false`, `runFilter` returns after an `eCALLBACK` filter-shader result
without calling `PxSimulationFilterCallback::pairFound`. Unbound's
[filter shader and Filter::pairFound](../unbound_physx.cpp) use that callback to
consult the ignored shape-pair set. Running it before interaction creation lets
the callback return `eSUPPRESS` for an ignored pair when a collider returns to
the broadphase. This keeps `Physics.IgnoreCollision` effective through collider
disable/enable and trigger changes.

[IgnoredPairLifecycle.cs](../../tests/projects/physicsscenesettings/ParentProbes/IgnoredPairLifecycle.cs),
`SceneSettingsProbe.RunCases`, records the ignore state as well as callbacks.
The Native
[focused/ignore-lifecycle.jsonl](../../tests/projects/physicsscenesettings/Reference/unity-6000.0.71f1/focused/ignore-lifecycle.jsonl)
has `filtered-dd/disable-enable/0/ignored = [1]` and no callbacks in
`filtered-dd/disable-enable/0/events` or `filtered-dd/enabled/0/events`.
The trigger is still ignored after re-registration. In the unfiltered `dd`
case, `ignore/0` produces trigger exits and `restore/0` produces trigger enters
and stays. The `go-disabled` and `go-enabled` phases also check the retained
ignore state through GameObject activation.

These records prove the required callback behavior. They do not show whether
Unity implements its ignored-pair lookup through this same callback mechanism.

## Change a shape's trigger role without broadphase reinsertion

In `ShapeSim::onFlagChange`, the branch where old and new broadphase membership
are equal now calls `AABBManager::setVolumeType` for the existing element ID,
using `eTRIGGER` or `eSHAPE` according to the new flags. When the trigger bit
changes, replace `reinsertBroadPhase()` with
`setElementInteractionsDirty(eFILTER_STATE, eFILTERABLE)`.

This updates the volume type and refilters the existing interaction without
allocating a replacement element ID for a role-only change. The branch for
changes to broadphase membership still adds or removes the volume as before.
The scene-query bounds logic is unchanged.

Unity's measured callback sequence distinguishes role changes from removal and
reinsertion. In
[TriggerFlagLifecycle.cs](../../tests/projects/physicsscenesettings/ParentProbes/TriggerFlagLifecycle.cs),
`SceneSettingsProbe.RunCases`, and its Native
[focused/flags.jsonl](../../tests/projects/physicsscenesettings/Reference/unity-6000.0.71f1/focused/flags.jsonl):

- `dd/ts/0/events` reports collision exit and trigger enter/stay when the first
  collider becomes a trigger.
- `dd/tt/0/events`, `dd/st/0/events`, `dd/st-swap/0/events` and
  `dd/ts-swap/0/events` contain trigger stays with no extra enter or exit while
  an overlap continues and its trigger roles change.
- `dd/a-roundtrip/0/events` and `dd/both-roundtrip/0/events` contain collision
  stays, with four contacts per object, after flags change away and back before
  simulation. The roundtrip does not create a new collision enter.

The public oracle records callback and contact behavior, not element IDs.
[native_flags.gdb](../../tests/projects/physicsscenesettings/ContinuationProbes/native_flags.gdb)
and [native_ids.gdb](../../tests/projects/physicsscenesettings/ContinuationProbes/native_ids.gdb)
are diagnostic tools for Unity 6000.0.71f1 Linux x86_64 with matching debug
symbols. They inspect IDs and flag changes; historical trace outputs belong in
the external validation cache. They are not extra Native oracles. The
identity-preservation explanation depends on that diagnostic observation and
the source change, rather than a canonical JSONL field for element identity.

## Refit objects moved late in a query-tree rebuild

`AABBPruner::updateObjectsAfterManualBoundsUpdates` and
`AABBPruner::updateObjectsAndInflateBounds` record a moved object for the refit
that follows the swap to the progressively rebuilt tree (`mToRefit`) in every
stage after the new tree's nodes exist: `BUILD_NEW_MAPPING` through
`BUILD_FINISHED`. The original condition covered only `BUILD_NEW_MAPPING` and
`BUILD_FULL_REFIT`. It adds no storage beyond the existing array.

Measured requirement: in Unity 6000.0.71f1 a static collider (no Rigidbody)
moved through its Transform and synced with `Physics.SyncTransforms` is found at
its new place by later queries. In tests/projects/particlerender,
`collide/cache/moved_{high,medium,low}` move three static boxes 1.5 units down
(and `collide/cache/gone_*` three more 50 units down) in one frame; the Editor's
particles bounce off the moved boxes in frame 29
(`Reference/unity-6000.0.71f1/values.jsonl`). Before this patch Unbound's sweeps
missed them while rays and overlaps still found them.

The cause is a stale node bound, an implementation inference from the source:
an object moved while the rebuild is in `BUILD_LAST_FRAME` (or `BUILD_FINISHED`)
is marked for refit only in the old tree, which the following commit replaces
with the new tree whose nodes were refit before the move. Until the next
rebuild, a query whose bounds miss the object's old place is culled before it
reaches the object. `PhysicsStaticMoveQueryTests` reproduces it in a local
physics scene: a static that jumps 100 units every seventh frame while another
static keeps the rebuilds going is missed by a sweep on some frames without the
patch.

## Run the BV4 midphase on NEON

Upstream compiles the BV4 midphase (`PxMeshMidPhase::eBVH34`, Unity's "fast midphase"
and its default cooking) only for Intel CPUs: elsewhere `GuMidphaseInterface.h` answers
every query of a BV4 mesh with nothing and reports "BV4 midphase only supported on
Intel platforms" once. On arm64 rays, sweeps, overlaps and contact generation therefore
missed every mesh collider. The BV4 query code is written with SSE2 intrinsics on
`__m128`/`__m128i`.

The added `GuBV4_Simd.h` defines `GU_BV4_SIMD` as `(PX_INTEL_FAMILY || (PX_A64 && PX_NEON))
&& !defined(PX_SIMD_DISABLED)` and, on arm64 NEON, gives the SSE2 types and every intrinsic the BV4
sources use (`_mm_castps_si128`, `_mm_castsi128_ps`, `_mm_shuffle_epi32`, `_mm_and_ps`,
`_mm_or_ps`, `_mm_cmpgt_ps`, `_mm_movemask_ps`, `_mm_load1_ps`, `_mm_store_ps`,
`_mm_load_si128`, `_mm_set_ps`, `_mm_set_epi32`, `_mm_set1_epi32`, `_mm_and_si128`,
`_mm_cvtepi32_ps`, `_mm_srai_epi32`, `_mm_slli_epi32`, `_MM_SHUFFLE`) with the same bits
as the SSE2 instruction: bitwise operations and reinterpretations, an ordered greater-than
comparison, lane sign bits, round-to-nearest integer conversion and shifts. `GuBV4.h`,
`GuBV4_Common.h` and `GuMidphaseInterface.h` include it. Every guard
`PX_INTEL_FAMILY && !defined(PX_SIMD_DISABLED)` of the BV4 sources becomes `GU_BV4_SIMD`,
and every plain `PX_INTEL_FAMILY` guard there becomes `PX_INTEL_FAMILY || GU_BV4_SIMD`, so
the Intel build compiles exactly as before and arm64 compiles the same traversal. The
vector math layer the BV4 code also calls (`Ps::aos`, for example `V4RecipFast`) keeps its
own NEON implementation, so results may differ from Intel in the last bits. Cooking
(`GuBV4Build.cpp`) was never guarded.

Measured requirement: the Unity 6000.0.71f1 macOS arm64 Editor records the same 3203
tests/projects/rayhits values as the Linux reference (`Reference/values.jsonl`), box casts
against meshes included. On macOS arm64 Unbound matches them: 7/3203 before the patch
(every mesh query missing), 3203/3203 with it; tests/projects/physicsbatch gives
5858/5870, the Linux sheriff baseline's result and its 12 retained mesh slot-order rows.
Cooking arm64 meshes with the BVH33 midphase instead was measured as a control and does not
reproduce the reference: rayhits 3083/3203 (120 box-cast hit points on meshes), physicsbatch
5734/5870. The Linux x86_64 probes do not exercise the NEON definitions.

## CCD sweep frame in the SSE2 order

`CCDSweep` builds the matrix of the swept shape's frame relative to the other shape
(`aToB`) from a quaternion. The SSE2 build's `QuatGetMat33V` (`PsVecMathSSE.h`) computes
each column as `v * 2q_i + (2ww - 1, ...)`, every product and sum rounded; the NEON one
computes the textbook `(1 - 2y^2) - 2z^2` form. For a capsule whose axis is turned a
quarter turn (Unity's Y-direction capsule, stored as `(0, 0, -s, s)`) the two give a
diagonal entry of about 1e-7 with opposite signs, which decides the end of the capsule
segment the sweep's first support point comes from. On NEON, `CCDSweep` now builds
`aToB` with the added `QuatGetMat33VSse2Order`, the SSE2 operation order in NEON
intrinsics (separate multiplies and adds). Every other caller, and the Intel build, is
unchanged.

Measured requirement (Unity 6000.0.71f1 macOS arm64 Editor, tests/projects/physicsccdhinge):
the Editor records the Linux reference's values for the capsule's Continuous and
ContinuousDynamic sweeps `ccd/thin/2/{False/1,False/2,True/2}/0/event/0/0` (one contact at
y = -0.0952381), while Unbound on arm64 reported y = 0 from the other end. With the patch the
three rows match: the default suite goes from 6706 to 6709 of 6814, its 5 unresolved rows
being `ccd/ghost/3`, where the macOS Editor gives the same values as Unbound. Against the
macOS Editor's own captures nothing else moves: the five other physicsccdhinge suites,
tests/projects/contactmodify3d (350 of 412 records identical in key and value) and the
capsule, sweeps and geometry suites of tests/projects/contactevents3d are byte-identical
with and without it. Controls that were rejected: the SSE2 order in every
`QuatGetMat33V` of the NEON build, or in the shared `PsMatTransformV` constructor from a
transform (also used by contact generation), resolves the three rows but lowers
contactmodify3d to 328 identical records; fused multiply-adds in the NEON multiply-add
helpers lower it to 317-325. That the macOS Editor's own build computes this one frame in
the SSE2 order is an inference from these measurements, not an observed mechanism; the
Linux x86_64 probes do not exercise the NEON code.

## Replay and maintain the inventory

The [focused probe instructions](../../tests/projects/physicsscenesettings/ParentProbes/README.md)
and [runner](../../tests/projects/physicsscenesettings/parent_probes.py) map
`flags`, `trigger-pairs` and `ignore-lifecycle` to the sources above. Preparation
selects contact-pair mode 3 for these three probes. Each source covers six body
pair kinds and simulates two steps after each lifecycle phase. Event arrays
contain each object's collision enter/stay/exit and trigger enter/stay/exit
counts, then the two collision contact counts. The comparator also checks
callback order, raw contacts, motion and all other recorded fields.

From the repository root, `python3 tests/projects/physicsscenesettings/verify.py`
checks the complete canonical file/schema/source mapping without a private
engine build. Use `parent_probes.py` to replay Native twice and compare a chosen
engine build as described in that README. The unchanged scene-settings,
contact/island, continuation and broader physics fixtures are regression
controls, not substitutes for the focused observations above. A passing subset
does not erase failures elsewhere in those fixtures.

Every new vendored SDK patch must update this file immediately in the same
commit. Record every changed file and function, the exact code change, its
Unity behavior requirement or portability reason, the runnable probe and
canonical Native reference path, the measured release/platform, and any gap
between the observation and the claimed mechanism. Update affected entries
when changing or removing a patch. Keep logs, repeated captures and build
outputs in the external run cache. If behavior differs between Unity releases,
route the difference through the compatibility profile and record the
release-specific evidence here.
