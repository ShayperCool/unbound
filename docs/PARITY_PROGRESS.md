# Parity continuation, 2026-10-30

The interrupted Claude work lives on `compat/close-gaps`, originally checked out in
`.claude/worktrees/compat-gaps`. Its last commit was `982c93b2`. The root checkout was still on
`main` at `4724b839`, so continuing from that checkout alone would have missed 58 commits.

`backup/claude-parity-20261030` preserves those commits and all 35 changed or new files from the
interrupted working tree, including probe assets and reference images. Snapshot commit:
`b184aada`. The old `compat/close-gaps` branch still points at its original commit. The snapshot
branch is checked out in the old worktree; leave it intact.

`compat/parity-continue-20261030` starts at that snapshot. Subsequent implementation and test
commits belong here. The root checkout uses this branch; `main` has not been moved.

## Recovered state

Claude's compatibility audit session stopped on an Anthropic account verification error during
phase 2c, render resources. Its plan was only in `~/.cache/compat/PLAN.md`; this file records the
handoff in the repository.

The earlier session reported:

- Phase 0 tools implemented, including silent-member checks, stub-event warnings, unsupported
  message warnings and unread serialization tracing. Its API check reported zero errors.
- Phase 1 messages and player loop implemented, with 316/316 Editor probe records matching.
- Most of phases 2a and 2b implemented. These are not complete rendering parity: halo/flare
  drawing, per-pixel LPPV, shadowmask mixing, skinned motion vectors and several other details
  remain. Consult `COMPATIBILITY_GAPS.md` and the probe READMEs for each limitation.
- Phase 2c partially implemented: SpriteMask stencil ranges, sorting groups, tiled sprites and
  secondary textures. Secondary-texture API, vertex attributes and URP 2D probes were unfinished.
- Animation/audio, physics, UI/input and assets/networking still have open rows.

The other recent sessions worked on material/shader declarations and on obtaining Editor data
without an installation. Material changes are already ancestors of the parity branch. Editor
archive/package changes were merged into `origin/main` via PR #3, after the parity branch split.
The continuation also merges that version of `origin/main`.

## Validated continuation

Reference Editor: Unity 6000.0.71f1, Linux, NVIDIA GeForce RTX 4060, gamma color space. Run the
Editor with a GPU display. Do not replace it with software GL for reference images.

- Resource probe: imported and created secondary textures, copying/query semantics, texture
  precedence, default texels and sprite vertex frames for every flip in Simple/Sliced/Tiled.
  Sprite flips now change vertex positions and tangent frames without changing the object's
  matrix. The resource comparison also fails for missing images and mismatches; six failure-case
  tests cover the checker itself.
- Unassigned samplers use their own neutral placeholder. The interrupted changes used the
  declared gray default for both, which broke 240 built-in camera-event values. The Editor probe
  measures 127 for the declared default and 128 for an unassigned sampler.
- Command buffer renderers: moving bones, blend shapes, renderer/slot property blocks and
  changes after recording. Four Editor images cover BeforeForwardAlpha and AfterEverything.
  Geometry and property blocks are taken at execution. Late commands keep scene depth during
  MSAA rendering, with the edge antialiasing limitation recorded in the resource probe README.
- URP 2D: 13 scenarios, including normal maps, mask textures, all flip states and SpriteMask.
  Every image is within a mean largest-channel difference of 0.02/255 from the Editor.
- Two inherited ShaderLab test fixtures omitted the new `vertex_lit` field and prevented the
  test target from compiling. They now use the default selection for fields outside their LOD
  checks. All 16 `unbound-app` library tests pass, as do all six resource-comparator tests.
- After merging `origin/main`, the release build succeeded. `api-check` reported zero errors in
  2221 hand-written types against Unity 6000.0.71f1.

## Texture2D mip chains, continued

Runtime and serialized Texture2D chains now retain every mip on the CPU and GPU. The resource
probe has 269/269 matching values and 62/62 images within its mean-difference threshold. The new
182 values cover six byte/float formats, manual levels, `tex2Dlod`, generated NPOT levels, raw
chains, imported CPU levels, `.asset` mips, invalid parameters and readability behavior.
The API check still reports zero errors in 2221 types.

## RenderTexture mip chains, continued

The additional 144 Editor values cover manual render levels, explicit and automatic filtering,
command-buffer generation, cube faces, array slices and MSAA resolve. RFloat and ARGBFloat render
targets now retain 32-bit precision. The compiler also uses the emitted WGSL entry point name when
Naga renames a reserved HLSL identifier, in both graphics and compute programs; all 34 shader
compiler tests pass. The complete resource reference now contains 413 values.

The asset test suite had an outdated baked-cubemap size: its EXR header and scene generator both
specify 64-pixel faces, while the test expected 128. The test now checks the actual fixture.

All 14 related graphics groups passed their regression with the same existing differences:
`~/.cache/compat/rtmips-render-reg-results.txt`. The resource probe matches 413/413 values and
62/62 images, worst mean 0.83/255. Rust library tests passed: app 16, runtime 38, assets 59.
The API check reports zero errors in 2221 types.

## Compressed bundle mip chains, continued

Bundle loading now decodes every BC/ETC/ASTC mip instead of dropping the lower levels. It also
retains encoded source data for raw-data queries and the original graphics format. Editor-built
DXT1 and DXT5 bundles match all 26 new CPU/GPU, format and raw-data values. Missing compressed
levels and invalid mip counts fail with a diagnostic. The complete resource reference now has
439 values.

## SRP shader overrides, continued

Renderer lists now apply `DrawingSettings.overrideShader` while retaining each source material's
properties. Four URP render-graph scenarios check both passes, -1 selecting the first pass, and
the simultaneous material override's precedence and error diagnostic. Every new image matches
the Editor within 1/255 at the worst pixel. The existing URP scenarios retain their prior results.

## Scissor and MRT clears, continued

Command buffers now clip draws, blits and clears with the active scissor. MRT clears accept
different colors per attachment, copy recorded color arrays, and restore every color attachment
for subsequent draws. The Editor's null/empty/oversized color-array errors and fixed dynamic
resolution factors are measured too. All 22 new values match, bringing the resource reference
to 461 values and 62 images. The API check remains at zero errors in 2221 types.

## Float texture component formats, continued

R/RG/RGBA half and full float now retain their native component count and precision on the GPU,
including arrays, volumes, cubes and cube arrays. Layered CPU setters quantize half values and
return the Editor's missing-channel values. All 78 new checks match exact float bits, including
sampler updates on unreadable textures, raw render-target reads and shader samples. The complete
resource reference now has 539 values and 62 images.

## Generic Animator root motion, continued

`tests/projects/animator` now measures 658 records against the Editor. Runtime root motion matches
the callbacks' order, disabled handlers requesting deltas, the apply flag, one builtin application
per evaluation, world/angular velocities, zero and backward time and several crossed loops.
Nested nonuniform and negative scale are included: animation uses the parent's decomposed TRS,
so the API root pose and actual Transform can differ. The root-pose setters retain an independent
API value, and a setter during a movement callback does not move a generic root.

This closes the runtime generic-motion part of the audit row. Imported root-node selection,
Bake Into Pose, matching, humanoids and body/controller integration remain separate work.

The API check reports zero errors in 2221 types. Animation-from-bundle matches 8/8 records,
lifecycle matches exactly, and messages match 316/316. Playables retain their two existing
scenario differences, with no new ones.

## Blend tree interpolation, continued

Simple Directional uses triangle fans with the Editor's central/default contribution, including
trees without idle and degenerate layouts. Freeform Directional uses polar gradient bands rather
than Cartesian ones. Direct nodes honor their normalization flag and preserve raw sums above
one when it is disabled; missing weight comes from bound defaults rather than previous frames.
The same eight tree configurations are measured as text assets and as compiled AssetBundle
controllers. All 1232 new records match; the Animator reference now has 1890 records in total.

## Root motion and physical bodies, continued

The Animator probe now matches 2116/2116 records. Its new body section distinguishes dynamic
velocity driving, kinematic body poses and CharacterController motion with gravity. It measures
frame time separately from Animator.Update's delta, Normal/Fixed mode, repeated evaluations before
physics, damping, initial velocities and gravity on/off. The CharacterController regression matches 23/23 records, bundle animation 8/8, and the API
check reports zero errors. Playables retain their two existing scenario differences.
Dynamic motion preserves existing
velocity projected onto root up. The API root pose retains its last evaluation value across a
physics step, and external root-pose setters remain independent of Transform writes.

## Constraint components, continued

Position, Rotation, Scale, Parent, Aim and LookAt constraints now load from scenes/prefabs and
run in ConstraintManagerUpdate. The initial probe matches 3881/3881 Editor records: source APIs,
rest/offset values, signed weights, local axes, nonuniform/negative parents, repeated frames,
active/enabled/locked changes, and prefab cloning. Scale uses the measured cubic log and
quadratic exp with each normalized source evaluated separately. One positional source uses
its raw weight, while multiple sources normalize their sum. Null sources are skipped.
The expanded probe matches 13498/13498 records with no unexpected output. All Aim up modes,
arbitrary aim/up vectors, LookAt roll and up objects, zero/negative weight sums, runtime
activation commands, initial rest capture and dependency order are covered. LookAt retains an
unclamped global weight; the other constraints clamp it during evaluation. The API check reports
zero errors in 2232 types. Additional source-hierarchy probes found a separate inherited
Transform rotation/scale bug under reflected parents, fixed in the next change.

## Reflected Transform hierarchies, continued

World rotation now composes each local quaternion with the signs of its parent's decomposed
scale. The inverse applies the same signs when setting a world rotation or retaining it during
reparenting. This also corrects `lossyScale`'s rotation removal, with no change to world matrices.
Constraint sources now use those common Transform reads. All 13804 constraint records match.

The generic animation stream keeps its own root quaternion without the parent's reflection.
Root writes now retain the measured local quaternion, while the Transform computes its physical
world rotation. The additional 120 Animator values distinguish those poses, local/world setters
and parent scale changes; all 2236 Animator records match.

All 14 graphics regression groups retain their prior summaries, including known particle,
lighting and terrain differences: `~/.cache/compat/reflected-transform-render-reg-results.txt`.
The API check reports zero errors in 2232 types. CharacterController matches 23/23, animation
from bundles 8/8, and Jobs 541/541 with its four known runtime differences.

## RaycastHit coordinates, continued

Ray hits retain PhysX's barycentrics and map cooked faces to original mesh triangles. Shape
casts retain the first vertex's barycentrics, and primitive/convex hits have triangleIndex -1.
The managed getter reads UVs from the collider's current mesh, including submesh base vertices.
UV2 falls back to UV0 with the Editor's warning. Lightmap coordinates apply renderer scale/offset
regardless of lightmapIndex. The setter's first two supplied barycentric components and the
default hit's lightmap exception match too. The native shim ABI is now version 5.

All 3203 Editor records match, including shuffled cooked triangles, unreadable mesh queries and
exact diagnostic text/severity. The API check reports zero errors in 2232 types. CharacterController
matches 23/23, messages 316/316, and the broad physics probe retains its 11 existing scenario
differences.

## Body sleep and reactivation, continued

Inactive 3D bodies report sleeping; inactive 2D bodies report not sleeping, with IsAwake retaining
its inverse behavior. Reactivated kinematic 3D bodies enter the scene in their configured type,
while first runtime creation keeps the existing initialization sequence. The new force probe
measures these lifecycle cases. The broad physics probe retains 11 existing scenario differences,
joints have 26/27 matching records plus their one known settling-force difference, and
CharacterController matches 23/23.

## ConstantForce components, continued

Both force components now load from scenes/prefabs and queue forces in the fixed script phase.
The 608 Editor records match, including instrumented player-loop phases, manual simulation,
force accumulation across fixed updates, relative forces/torques, enable state and reflected
parents. ConstantForce2D converts its angular force from degrees and wakes zero-force dynamic
bodies. The 3D component has a native singleton limit without DisallowMultipleComponent metadata;
the 2D component permits multiple instances. Native force updates follow negative execution
orders and precede ordinary order-zero scripts, so a FixedUpdate setter affects the next force
evaluation. The expanded ordering probe matches 608/608, messages 316/316, and lifecycle matches
its reference exactly. The API check reports zero errors in 2235 types.

## Distance and spring 2D joints

DistanceJoint2D and SpringJoint2D now load from scenes/prefabs, clone their connected bodies,
and simulate with the existing Box2D port. All 214 Editor records match. Coverage includes
automatic anchors/distance, setter limits, static/kinematic/dynamic connections, reflected
nonuniform parents, zero-frequency springs, enabling/disabling, reconnecting and all four
break actions. OnJointBreak2D is delivered before Disable/Destroy; CallbackOnly repeats on
each exceeding step. ReactionForce is on the connected body and explicit nonpositive time
steps return zero. The port retains Unity's bounded hard-distance correction and computes
spring coefficients from effective constraint mass. Physics-driven rotations preserve shape
and inertia despite changes in the reflected hierarchy's apparent scale. The seven other
Joint2D components remain to be implemented. API signatures match 2240/2240 types.
The broader physics probe now matches its distance and spring scenarios, reducing the existing
differences from 11 to 9 without changing the other results. Forces remain 608/608, controller
23/23 and 3D joints 26/27 with the one known settling-force difference.

## Hinge, fixed and friction 2D joints

HingeJoint2D, FixedJoint2D and FrictionJoint2D now simulate and load/clone their serialized
settings. The joint probe has 691 records with no unexplained differences. Small solver
residuals below 0.001 in angular velocity/force/torque, and limitState at the floating-point
boundary, are listed separately by the comparator. Nine comparator tests verify that missing
records/callbacks, position errors, nonfinite values and larger errors still fail. Coverage
includes motors, equal/reversed limits, high starting angular velocity, useConnectedAnchor,
hard/soft fixed joints, connected-body changes, all break actions and suspended simulation.

Measured 2D anchors use the rigidbody's physics pose and signed lossy scale, excluding shear.
Reference angles retain their initial physics poses; inactive joint query values return zero.
Setting a hinge motor or limits enables that setting. A reversed limit range retains its API
values but disables the native constraint. Suspended bodies retain inertia and joint impulses.
The API check reports zero errors in 2245 types. Broad physics now has 8 existing differences,
with the hinge scenario also matching; forces remain 608/608 and 3D joints retain their one
known settling-force difference. Wheel, Slider, Relative and Target are the remaining joints.

## Wheel suspension and slider joints

WheelJoint2D and SliderJoint2D now load/clone and simulate. The expanded 1054-record fixture
has no unexplained differences. Solver residuals remain listed separately. Wheel relative
angular speed propagates the tolerance of the two body angular velocities it subtracts;
position/settings/callback checks retain their original strictness. Eleven comparator tests
cover those conditions. Wheel requires a connected body, stores its auto-anchor flag without
configuring the anchor, and retains unclamped suspension/motor structs. Slider clamps its
settings, auto-configures angles, converts its linear jointSpeed to degrees, and retains the
coupled limit solver and positional slop measured in Unity. Its reversed limits clamp max
to min. The Box2D 2.3.1 solver adaptation includes its original license and source attribution.

The port's prismatic warm-start assignment typo is fixed; polygon inertia uses the centered
reference point retained by Unity. Suspended joint queries keep their solver's detached
managed objects so moving connected bodies still affect the query. Queries update only the
requested joint. API check: zero errors in 2249 types. Broad physics retains the same 8 known
scenario differences and forces match 608/608. Relative and Target are the remaining joints.

## Relative and target 2D joints

All nine Joint2D components are implemented. RelativeJoint2D uses the motor solver; TargetJoint2D
uses the mouse solver with an explicit local anchor and reversed body order. The probe has
1401 records: 1382 within the original tolerance and the same 19 separately reported solver
rounding records, with no unexplained differences. Its 38 Target diagnostics match severity
and order. Relative auto-offset and Target auto-target recapture on enable; their flags alone
do not overwrite explicit values. Relative.target derives the desired owner position from the
connected body's physics pose and angular offset. Inactive Relative.target returns zero.
Target rejects connectedBody and enableCollision changes with warnings, keeps collision enabled,
reports force on the owner, and its zero-frequency solver retains the measured 0.0001 minimum
while the API still reports zero. Prefab/cloning, reflected scales, suspended bodies, manual
steps, kinematic bodies and runtime destination changes are covered.

API check: zero errors in 2251 types. Broad physics retains its 8 existing differences; forces
match 608/608, CharacterController 23/23, and 3D joints 26/27 plus the known settling force.

Continue with the Effector2D family, then the other open physics/animation/audio/UI/import rows.
Full engine parity is still incomplete.

## Platform and surface effectors, in progress

PlatformEffector2D and SurfaceEffector2D now load/clone settings and affect contacts. The first
553-record probe matches 538 records. Remaining differences are ten contact-normal order
records in a compound platform case and five records caused by the existing material update
behavior. A dedicated material-assignment/mutation probe is being added. Surface speedVariation
is still explicitly unsupported during simulation, rather than silently ignored. Area, Point
and Buoyancy remain stubs. API check reports zero errors in 2254 types.

Implemented behavior includes native singleton limits across effector types without a managed
DisallowMultipleComponent attribute, custom masks overriding the layer matrix, reflected
one-way directions, persistent ignored contacts, side friction/bounce, contact-point versus
center conveyor impulses after the solver, and IsTouching excluding disabled contacts while
GetContacts and callbacks retain them. Do not mark this effector block complete yet.

## Assigned 2D material snapshots

Colliders now capture friction, bounce and their combine modes when assigned or rebuilt.
Mutating a shared PhysicsMaterial2D leaves assigned colliders unchanged until reassignment or
geometry changes; this includes Rigidbody2D material fallback. The ten dedicated material API
records match, and the Surface bounce scenario now matches too. Shape/material updates refresh
existing fixtures and contact mixtures without relying on a material version poll. Global
physics defaults are loaded before resolving a default material.

Broad physics retains its 8 existing scenario differences, joints2d remains 1382/1401 plus the
19 known rounding records, and forces remain 608/608. The expanded effector probe has 563
records. Its remaining compound-contact cases expose contact ordering: adding and removing
an unrelated material-test collider before the same platform case changed Unity's grouped
initial-overlap outcome. Both native runs are retained under ~/.cache/compat/effectors-unity-*
for reconciliation. Keep this limitation explicit while continuing the effector work.

## Native contact allocation variants

The initial compound overlap changes in Unity when an unrelated material collider is created
and removed earlier. The baseline probe commits retain both runs; their case29 states are now
preserved in `effectors/Reference/initial-overlap-variants.json`. The comparator accepts only
those measured alternatives and permutations of otherwise identical contact normals. Eight
comparator tests ensure missing/changed positions, callbacks, contact counts, normals and
nonfinite values still fail. Current result: 543/563 exact-tolerance records, 20 known native
allocation/contact-order records, zero unexplained differences. This reconciles the remaining
initial-overlap differences without changing engine behavior or loosening position tolerance.

## Inactive body material assignment

Rigidbody2D material assignment now updates collider snapshots in inactive children too, while
respecting a nearer Rigidbody2D and an explicitly assigned collider material. The additional
inactive-assignment/reactivation records match the Editor. Material lookup shares a hierarchy
helper instead of relying on membership in the active physics world.

## Surface variation and curved conveyors

Surface speed variation now matches the native seed-zero xorshift128 sequence using a private
Random.State per effector. It draws on every solved contact, even with zero variation, adds a
signed variation times the sampled value, preserves its sequence across enable changes and
starts a new sequence on cloning. The script-global Random state remains unchanged. The probe
includes 64 consecutive draws, re-enable/clone/zero-variation/resume arrays, positive/negative
variation, mirrored/rotated surfaces and a circular conveyor. The 684 records have 674 original-
tolerance matches and 10 known contact-order records, with zero unexplained differences.
API check remains zero errors in 2254 types; all 8 effector and 11 joint comparator tests pass.
Area, Point and Buoyancy are next; no full engine parity claim is implied.

## Volume effectors

Area, Point and Buoyancy now have hand-written properties, prefab serialization, cloning and
simulation. The expanded effectors probe has 2559 Editor records, with 2531 original-tolerance
matches and 28 measured native contact-order records. No unexplained differences remain.
Area/Point queue force after integration, damp velocity after position integration and apply
one force per target body. Area's Collider selection uses the authored shapes' combined bounds center,
while Point selects an overlapping collider and supports constant/inverse-linear/inverse-square
falloff. Each effector has a private seed-zero RNG, independent of script-global Random.
Buoyancy clips circles and polygons against the horizontal fluid surface, accumulates submerged
area and centroid, and applies gravity-scaled buoyancy, flow and linear/angular drag there.
Angular drag torque is skipped at the configured angular sleep tolerance converted to radians;
probes vary drag, mass, immersion and tolerance to distinguish that cutoff from a speed test.

The fixture caught two broader physics issues. Trigger shapes must not contribute to body mass,
center or inertia, and changed collision filters must discover new contacts before the next
integration. Both are fixed. Broad physics retains its 8 existing differences; Joint2D remains
1382/1401 with 19 known rounding records, and forces remain 608/608. The compound Point contact
can select either overlapping collider after unrelated allocation changes in the Editor. Complete
native force/pose alternatives are retained along with the platform overlap alternatives;
comparator tolerance is unchanged. The extension includes low linear force with configurable tolerance, asymmetric polygon bounds,
scaled/signed fluid levels, edges and horizontal/rotated/degenerate capsules. Fluid forces are
combined before the linear tolerance cutoff. Partial capsule immersion clips its oriented
rectangle, but full immersion uses true capsule area; both stages match the Editor. Collider
capsules authored as circles retain a measured 0.0049 minimum endpoint separation, including
their inertia, rather than becoming a CircleShape. General physics summaries are unchanged by
that geometry correction. The next high-impact row is particle custom vertex streams.

## Particle vertex streams

A dedicated tests/projects/particlestreams project records stream lists, baked attribute
formats/dimensions, geometry and every UV channel. The initial non-trail implementation matches
968/968 Editor records, including individual and mixed streams, custom vectors, texture-sheet
frame/blend data, source-mesh UV1, size/rotation/speed/lifetime, mesh axis/index and particle/vertex
indices. Stable vectors use per-component seed offsets and a private xorshift128 first draw;
varying vectors also incorporate normalized age in percent. A 23-seed sweep and several ages
match without using or mutating global Random. Billboard corner order, 3D Z, tangents and
normalDirection's nonsquare normals now match baked Editor output. Native particle SIMD color
conversion differs from Color.linear; all 256 byte inputs are measured and match exactly.

The actual GPU shader reading packed custom data produces a byte-identical 64x64 image.
Existing particles retain 1488/1560 matching value records, 9/94 known differing scenarios and
worst image mean 0.93. No new broad-particle regressions were observed. Native BakeMesh with no
UV-packed attribute reads uninitialized vertex storage, observed in repeated Editor runs; the
fixture saves only stream lists and attribute declarations for those inputs, never heap contents.
The lifecycle extension now matches 1420/1420 records: prefab declarations and clones, explicit
mesh-layout replacement, two-particle sorting/custom-data association, and every BakeMesh flag
combination in World/Local/Custom spaces under reflected/nonuniform scale. Baking now selects
simulation-pose parts rather than applying a renderer inverse matrix. World-space systems ignore
those flags. Emitter scale applies after billboard particle rotation; Size streams remain in
particle units. Flat normals come from the normalized quad diagonals, retaining the native
nonsquare magnitude and reflection sign, with the measured normalDirection spherical blend.
The custom GPU image remains byte-identical. Custom trail declarations now load from prefabs.
Active noise exports and custom trail geometry remain under work; the stream row is partial.

## Trail oracle and parallel work checkpoint

The committed particle-stream reference now has 2860 Editor records, including 18 trail cases.
Trail runtime work has not yet been changed. The oracle demonstrates:

- Native trail samples begin after the first integration substep, not at emission. At 0.1 seconds
  with default maximumParticleDeltaTime and minVertexDistance 0.05, centers are head 0.1, then
  samples 0.075 and 0.025 for unit-X velocity. Tangent points toward older samples; its w is +1.
- Expired tails interpolate to the exact lifetime boundary while retaining an older anchor.
  sizeAffectsLifetime scales lifetime by particle size. Orphan trails still expire after emission
  ends, so their clock must not depend solely on m_IsPlaying/AdvanceEmission.
- Next/PreviousTrailCenter clamp to the endpoint there; PercentageAlongTrail uses distance.
  Texture modes use distance-normalized, tiled distance, index-normalized or repeated-index U.
- Reflected/nonuniform emitter scale multiplies trail width by the cube root of absolute scale
  determinant. Default trail API streams include Normal, but lighting attributes appear in the
  default baked layout only with generateLightingData; explicit custom declarations override it.
- Trail scalar color conversion follows Color.linear and differs from the main particle SIMD
  byte lookup. The measured colorOverLifetime applies to the whole current trail.
- Ribbons connect current particles in descending emission/index order, interleaved by ribbon
  count. The 4-particle/2-ribbon case connects indices 3 to 1, then 2 to 0.

Native oracle files are ~/.cache/compat/particle-streams-unity-trails/values.jsonl and its log;
the complete values are committed in tests/projects/particlestreams/Reference. All values are
finite. Continue implementation in ParticleSystem.Trails.cs and the renderer, then compare all
2860 records and rerun the broad particle checks.

The user explicitly authorized subagents after asking for a rough progress count. A mechanical
count of the current gap tables is 60 marked done out of 162 rows, with 7 additional unchecked
cross-cutting tooling items; 37 percent is a row count, not overall engine completeness.
Parallel changes must use isolated worktrees, distinct owned modules and managed output folders.
All Editor, managed/Rust builds and game probes share the exclusive wrapper
~/.cache/compat/with-heavy-job to prevent simultaneous heavy work on this 30 GiB machine.

## Integrated progressive downloads

The isolated networking delegate completed t3/progressive-download-handlers without touching
other modules. Its four commits were cherry-picked with provenance into the continuation branch.
Script and file handlers receive progressive chunks on the main thread with bounded buffering;
header/data/completion ordering, array ownership, custom progress, rejection/exceptions, callback
aborts, request/handler disposal, file append and remove-on-abort follow the measured behavior.
Two independent Editor runs agree on 458 observations. The parent checkout independently matches
458/458 after integration, builds with zero errors, passes all six UnityWebRequestTests and has
zero api-check errors in 2258 hand-written types. Evidence is under ~/.cache/compat/network-parent-*.

The shared heavy wrapper now uses flock --exclusive --close, so compiler and Editor daemons do
not inherit its descriptor after the owning command exits. A compiler spawned by api-check's
nested build retained the old descriptor and was stopped after verifying its PID, inherited PWD
and lock fd. Ordinary builds disable build servers, shared compilation and node reuse. For nested
CLI builds, export UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1 and
DOTNET_CLI_USE_MSBUILD_SERVER=0 as well. The UI delegate remains isolated from these changes.

## Building and checking

Use a separate Cargo target directory to avoid replacing another checkout's executable. The
recovered target is `/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat`; use at most
six build jobs and run heavy builds/probes sequentially. A reused target can also retain absolute
build-script watch paths from its earlier checkout. Toggling UNBOUND_SKIP_MANAGED_BUILD forces
the script to run for the current checkout; it still builds the native shim. Rebuild managed
code explicitly afterwards, and verify the shim ABI before running physics probes.

When reusing that target from a different checkout, explicitly rebuild managed code from the
current sources too. Cached Cargo build-script dependencies can still point into the old
worktree. Example from the root checkout:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export CARGO_TARGET_DIR=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat
export CARGO_BUILD_JOBS=6
cargo build --release
dotnet build -c Release -o "$CARGO_TARGET_DIR/managed" managed/Unbound.Host/Unbound.Host.csproj
export UNBOUND_MANAGED_DIR="$CARGO_TARGET_DIR/managed"
```

Probe commands are in `tests/projects/resources/README.md` and `tests/projects/urp2d/README.md`.
The recovered broader regression script and its previous results are in
`~/.cache/compat/regress.sh` and `regress-baseline.txt`. That script hard-codes the old worktree;
use the current checkout and a new output directory when running it again. Several earlier
physics, navigation, particle and image probes already differ from the Editor in that baseline.
Do not report them as newly introduced failures, or as full parity.

The continuation completed 25 groups of the broader script before stopping its long LOD sweep.
This was not a complete regression run. Results are in `~/.cache/compat/codex-regress-results.txt`.
After correcting the sampler placeholder, all 14 related graphics groups were rerun via
`~/.cache/compat/codex-render-final.sh`, with results in `codex-render-final-results.txt`.
The 12 unchanged groups reproduce their earlier summaries exactly. The other two are the
expanded resources and URP 2D probes. Built-in RP is back to zero unexpected value differences.
Resources match 87/87 values and 62/62 images within a mean difference of 2/255, worst mean 0.83.
URP 2D has 13 scenarios within a mean difference of 0.02/255. Known baseline differences in
particles, lighting and terrain remain. These results do not establish full engine parity.

## Remaining work

Continue phase 2c with remaining texture/RT formats, sub-elements and shapes, custom render
textures, SRP shader overrides and the remaining low-level graphics calls. Then continue the
animation/audio, physics, UI/input and import/networking sections of `COMPATIBILITY_GAPS.md`.
Keep an Editor probe for each behavioral change and leave the snapshot branch unchanged. A full
parity PR is not ready while those rows remain open.

## Trail runtime and particle aging

Implemented BakeTrailsMesh, default/custom trail layouts, packed neighbor/percentage/width
streams, exact sampled heads/tails, orphan particle retention, 3D width rules and ribbons. The
final oracle has 3544 records across the previous main-stream cases and 29 trail scenarios.
All 3544 match at the unchanged 2e-4 tolerance. Two successive Editor runs agree on all 3544
values after adding regular ribbon GPU captures.

Native arithmetic recovered through public recordings and read-only binary analysis:

- Particle age accumulates as inverseLifetime * (dt * 100), capped at the next float above 100.
  Death uses age > 100; history sampling uses age < 100. Dead orphan particles retain zero
  lifetime and velocity until their trail expires. Public lifetime conversion uses the native
  reciprocal/percent operation order. This fixes the .5/.025 death boundary without tolerances.
- Trail lifetime setters clamp scalar values to [0,1]. Size-dependent lifetime uses maximum XYZ
  size. Nonuniform trail width is sqrt(X*Y), or cbrt(X*Y*Z) in Mesh render mode.
- Lifetime and trail gradients use opposite asymmetric byte multiplies, before scalar linear
  color conversion. PercentageAlongTrail follows texture U for all four texture modes.
- Ribbon side vectors smooth short segments sequentially before bounded miter construction.
  Smoothing uses authored maximum size for ribbons and configured head width per particle.
- Unity 6000.0's multiple-ribbon BakeTrailsMesh emits invalid-index errors. Its compatibility
  profile reproduces the failed bake while regular rendering uses valid separate strips.

Custom streams and both single/multiple-ribbon GPU images are byte-identical to the Editor.
Managed build and integrated api-check pass, 0 errors in 2258 hand-written types. Broad particle
regression remains 1488/1560 with the same nine known scenarios, and worst image mean 0.93.
The trail image mean is 0.49. ParticleSystemTests have 26 passing and four inherited failures;
an independently built archive of HEAD 836cf7f2 reproduces the exact same four failures.
These are emission boundary count, stopped state after Emit, total/base velocity limit semantics
and old billboard vertex/UV/index order expectations. No new unit-test failures were introduced.

Evidence: ~/.cache/compat/particle-trails-unity4 and unity5, particle-trails-unbound6,
particle-trails-build7.log, particle-trails-api.log, particle-trails-regression2*,
particle-trails-tests-final.log and particle-tests-baseline.log. Independent native arithmetic
notes/checkers are under noise-analysis/, particle-age-analysis/ and trail-color-analysis/ there.
Active noise implementation/exports are the next particle work. GetTrails/SetTrails and texture
baking remain distinct open gaps.

## Integrated UI Toolkit transitions

Reviewed and cherry-picked the UI delegate's seven isolated commits with provenance. Parent
commits are 97a41079, 2e85c310, 39f12e43, e4096ccb, f3fa232c, 3972852d and 3af77559.
Runtime supports USS/inline transitions, all 23 easing modes, delay/duration boundaries,
interruption/reversal, property matching/shorthands, lifecycle events and mixed transform units.
Only active transitions update each frame. Inherited colors follow the presented parent style,
and detached elements retain their last layout as in the Editor.

Parent integration verification:

- 253 deterministic samples and 831 callbacks match the Editor with zero differences.
- UIElements/Yoga tests: 256/256 pass.
- Managed host build and api-check pass, 0 errors in 2266 hand-written types.
- Existing uitk GPU probe matches the independently built pre-change implementation in all
  structure, styles, text, 11 interaction steps and three byte-identical PNG images. Its known
  single-pixel native label-width difference remains unchanged.

Evidence is ~/.cache/compat/ui-parent-{build,tests,api,oracle,gpu}*; fixture and comparison are
under tests/projects/uitransitions. Cross-element event ordering, callback-driven retargeting
and every discrete/image/font combination remain unmeasured; internal animation-system APIs
remain stubs. The original transition gap is closed within this measured runtime scope.

Particle files and particlestreams are now exclusively assigned to the isolated noise delegate,
starting from ad1eef67. Parent must not edit them before integration. Parent's next independent
item is Canvas.vertexColorAlwaysGammaSpace, with a dedicated native color oracle before changes.

## Integrated particle noise

Reviewed and cherry-picked the noise delegate's seven commits from ad1eef67 without conflicts:
73cfba9e, d00464c8, f30b6376, fdec9fc0, c6172b84, 6075d942 and 3c128af7.
The engine implements analytic Perlin curl derivatives for all qualities, independent position/
rotation/rendered-size amounts, remap, octave normalization, damping, wrapped scroll integration
and optional accumulated/instantaneous noise stream storage. Manual Emit skips incremental modules;
automatic births evaluate them at their elapsed substep. Public current-size queries exclude noise
as measured, while rendered Size streams include it.

Parent verification: 23646/23646 defined noise observations match at the unchanged 2e-4 tolerance;
two independent Native recordings agree. Existing main/trail coverage stays 3544/3544 with all
three PNGs byte-identical. Broad particle values improve to 1496/1560, with noise 8/8 and eight
remaining differing scenarios; image worst mean remains 0.93 and trail mean 0.49. Build and
integrated api-check pass, 0 errors in 2266 types. ParticleSystemTests remain 26 passing with the
same four independently established inherited failures.

The fixture deliberately does not read undefined optional impulse arrays immediately after Emit;
it records all defined public birth values and every requested attribute after initialization.
No tolerance change or comparator exception was added. Evidence: ~/.cache/compat/noise-parent-*
and tests/projects/particlenoise/README.md. Remaining broad differences are set_particles,
custom_data, both sub-emitter scenarios, collision_world, simulate_long_call, trigger_kill_outside
and external_forces_rotation. GetTrails/SetTrails and texture baking remain open.

## Canvas color batching

Implemented Native Canvas byte batching in ebf93565. Both vertex and renderer RGB convert to
linear before multiplication when the project is Linear and the Canvas flag is false. Other
cases retain gamma RGB; each draw supplies the nearest Canvas's integer flag to the original
Unity shader source. Vertex and renderer/group alpha multiply without gamma conversion. All
channels clamp and quantize half-up to bytes, including pop draws. Source snapshots remain
unchanged, and cached prepared meshes rebuild after geometry, tint, alpha or mode changes.
SetAlpha/GetColor now share alpha storage; Canvas cloning preserves the flag.

The final fixture matches 49201/49201 observations in EACH of Linear and Gamma, 98402 total.
Independent Native runs agree on all values in each profile. It covers World Space and Screen
Space Camera, nested/independent sorting with opposing flags, all 256 input bytes, shader flag
binding, UI/Default's shader conversion, direct custom shader values, group alpha, live changes,
source snapshot behavior, pop materials and clone flags. Original binaries match 4651/36901
values in the explicit-pose pre-fix comparison. No tolerance widening or exceptions were used.

Build and api-check pass, 0 errors in 2266 types; Canvas/UIElements/Yoga tests 261/261 pass.
Existing uitk metadata, eleven interactions and all three GPU images remain byte-identical to
the independently built pre-transition baseline. Evidence and commands: tests/projects/uicolors/
README.md and ~/.cache/compat/uicolors-*. The project is restored to Linear after Gamma runs.

A separate existing discrepancy surfaced during fixture setup: creating an active Canvas
currently applies the default overlay's driven rect eagerly, leaving an unwanted offset when
switched immediately to WorldSpace. Native color probes use explicit matching poses. This
creation/lifecycle scheduling needs its own Native oracle and fix, rather than a color-batching
workaround. Overlay screenshot coverage, HDR/float vertex attributes and additional material/
texture tint combinations also remain unmeasured.

## Integrated Terrain baked lightmaps

Reviewed and cherry-picked the Terrain delegate's four commits as 33fdfc92, f4a5ecd4,
fc95ae27 and c3ee148b. Terrain LightingData entries now load and draws bind their live indices,
STs, normalized UV channels and splat property blocks. Missing-slot/directional behavior and
16-bit index sentinels follow measured Native results. Instantiate remains unbound as in Unity.

The shared surface generator now interpolates UV2 after the vertex modifier, applies
unity_LightmapST and passes it into forward-base GI. Forward add does not repeat baked GI.
Shader cache format is 13, replacing version 12 entries. Protocol and native ABI remain unchanged.

Parent validation:

- Linear and Gamma EACH match 1271/1335 records and 37/41 PNGs. Every baked-only case passes,
  including both installed Terrain shaders and ordinary surface-shader meshes with distinct
  UV0/UV2 and nontrivial STs. Maximum passing image difference is one byte.
- Exactly 64 sample records and four images per color space remain different: cases 12, 13, 14, 19
  with realtime index 5. These remain in the full comparisons, and realtime GI now warns.
  There are zero unexpected failures. Shadowmask and multi-layer add-pass mixing are uncertified.
- Rust shader tests 34/34 pass. Managed build and api-check pass, 0 errors in 2266 types.
- Existing Terrain regression remains 214/216 against Native, with two inherited steepness
  differences. Records and all six images are byte-identical to the pre-change managed baseline.
- Existing baked-scene records and image are byte-identical to the previous parent baseline;
  against Native the image retains mean max-channel difference .15.

Native references include two byte-identical recordings per color space, 1335 records and 41 PNG
files each. Parent outputs and logs are ~/.cache/compat/terrain-parent-*. The full commands,
Native evidence and limitations are in tests/projects/terrainlightmaps/README.md. The fixture
project has been restored to Linear. Full realtime GI is still open; this closes the baked
Terrain binding/UV gap, not the complete lighting subsystem.

## Integrated Canvas creation and driven lifecycle

Reviewed and cherry-picked seven delegate commits as 84e38a1e, 0e2d3ffa, 5ff7419a,
a019ab20, 555767b8, b97222fc and 646cd3c6. New Canvas components default to WorldSpace,
retaining authored poses. Active screen-mode setters and activation drive at the measured
phases; ForceUpdateCanvases does not overwrite later authored rect changes. Nested setters,
driver release, parent scale/rotation and RectTransform reparent/clone callbacks match Native.
Transform.KeepWorldPose is virtual so RectTransform can retain rect size on SetParent(true).

Parent integration matches 8671/8671 observations, including 293 snapshots and 401 callbacks.
Two Native files and the committed reference are byte-identical. Linear/Gamma color checks
remain 49201/49201 EACH, 98402 total; uitk metadata, eleven interaction steps and three PNGs
remain byte-identical to the prior parent baseline. Managed build/API check pass, 0 errors
in 2266 types.

The old CanvasOrder test expected inherited overlay depth to survive layout. Native
transformParent.0.mode proves world Z 0/local Z -100 under a parent at Z 100. Parent corrected
the test in d4dedb12 to assert this behavior and explicit sortingOrder precedence, rather than
changing the implementation back. Canvas/UIElements/Yoga now 261/261 pass; additional
Core/RectTransform coverage 14/14 passes. There are no remaining failures from this handoff.

Evidence: ~/.cache/compat/canvas-parent-* and tests/projects/canvaslifecycle/README.md.
The color fixture is restored to Linear. Earlier Unity releases, standalone players and
additional cross-callback state observations remain unmeasured. The Rigidbody defaults and
Collider.contactOffset follow-up are integrated below.

## Integrated project physics defaults and Collider contact offsets

Reviewed and cherry-picked the Rigidbody delegate's five commits as f91b31ee, dd2273ad,
ad28e0c6, b0a6ee49 and 7596d6b5. Components snapshot project solver iterations, sleep threshold,
angular speed and depenetration speed at construction. Allocation and reactivation retain
those values; imported and cloned components use current defaults. Setter magnitude/clamp,
nonpositive rejection and nonfinite readback follow the measured Native rules.

Parent extended the original 389-record oracle with 193 Collider observations in 48e4c4f1.
Two independent Native recordings agree on all 582 records, including every original value.
The added cases cover six collider types, empty and valid mesh/terrain geometry, active/inactive
objects and parents, disabled components, clone/prefab lifetime and invalid setters. A moving
box measures contact entry, exit and stay when contactOffset changes during simulation.

The integrated Rigidbody changes matched 388/389 original records; Collider.contactOffset
still read the mutable global. Parent's pre-Collider build matches 452/582 expanded records.
Commit 241340a6 snapshots Collider defaults and preserves them through activation, resets
clones, ignores assignments without a shape, and updates live PhysX shapes directly. There is
no body/shape recreation to approximate contact updates. PhysX shim version is 6; the engine
protocol and main native ABI remain unchanged.

Parent validation:

- 582/582 records match at the unchanged tolerances. All 193 added Collider records and 576
  total records are exactly equal. Six solver-motion records retain rounding up to 6.15e-08,
  well within the original 1e-3 motion tolerance. Zero records were excluded.
- Rust CLI/native and managed host builds pass. API-check reports zero errors in 2266 types.
- Managed PhysicsTests/Physics2DTests pass 33/33, with no skips. Build fix c4e09fda selects
  the test native library from CARGO_TARGET_DIR or explicit UnboundNativeDir. Absolute,
  relative and fallback selection were checked; the copied library matches Cargo's SHA256.
- Broad physics remains 21/29 scenarios, with exactly the same eight known differences.
  Joints remain 26/27 plus the known hinge-limit settling-force difference. Controller is
  23/23 and ConstantForce 608/608. Their complete comparison text equals the delegate's
  pre-Collider regression output, including the 85/292 differing 3D stack values.

Evidence and commands: tests/projects/physicsdefaults/README.md, its Evidence directory and
~/.cache/compat/physics-parent-*. The historical 388/389 recording is retained separately;
the final full recording is Evidence/unbound-collider.jsonl. Controller-specific movement
with offset overrides, terrain tree shapes, older releases and invalid-value simulation
remain unmeasured. Existing solver/contact drift and other DynamicsManager settings stay open.

## Integrated Canvas rectangle clipping and softness

Reviewed and cherry-picked the clipping delegate's four commits as db43474b, fba80dd6,
f2e03994 and 72fa2a16. CanvasRenderer now supplies root-Canvas vertex coordinates,
_ClipRect, softness and a private local clipping keyword for normal and pop draws.
Original Unity UI/Default performs clipping. Material/global keyword state is preserved;
custom shaders without the clipping variant draw as Native does, without an imposed scissor.
No lifecycle code, native ABI, protocol, Rust renderer or original shader source changed.

Parent review also fixed unbounded retention of private material copies in 5ae8458b.
Changing renderer material/pop slots or counts and Clear now release those copies. The
fixture's live material changes, source isolation and full rendering checks still pass.

Native pairs agree byte-for-byte in each of Linear, Gamma and LinearMSAA. Parent final
Linear and Gamma EACH pass 1211 observations, 266 complete images and 17432576 float
channels, 2422 observations/532 images/34865152 channels total, at the original 2e-4
absolute/relative tolerance. The pre-change engine passed only 54/266 Linear images.
Cases cover direct renderer and real uGUI masks, root/nested transforms, both sorting modes,
WorldSpace/ScreenSpaceCamera/Overlay, intersections, hard/soft/negative softness, live mask
lifecycle, material/global keyword precedence and shader coordinate/uniform diagnostics.

Parent managed build/API check pass, zero errors in 2266 types. All 275 combined
Canvas/UIElements/Yoga/Core/RectTransform tests pass with no skips. Colors retain 49201
observations per color space, 98402 total; lifecycle 8671; transitions 253 samples and
831 callbacks. UI Toolkit metadata, all eleven interactions and three PNGs are byte-identical
to the independently built pre-change engine. The existing Native label-width discrepancy
is unchanged. Gamma captures use a private project under Library; authored settings stay Linear.

Quality MSAA remains open. Parent reproduces the same 115/266 failing images and
41710/17432576 differing channels. An unclipped rotated control also differs from Native
and is unchanged from the pre-clipping build. FrameBuilder applies quality MSAA to a
single-sample ARGBFloat camera target where Native does not. This sample/format behavior
needs a separate oracle; it is retained as a new open row rather than hidden by the
passing single-sample matrices. No comparisons exclude cases or widen tolerances.

Commands, reference manifests and remaining limits: tests/projects/uirectclipping/README.md.
Parent outputs, build/API/test/regression logs and the complete failing MSAA comparison:
~/.cache/compat/rectclip-parent-*. Nonfinite-input GPU rendering, perspective/tilted canvases,
normal/tangent shader inputs, older releases and additional nested Overlay combinations
remain unmeasured. This closes measured rectangle clipping, not complete UI/rendering parity.
